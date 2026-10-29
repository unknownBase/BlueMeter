using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BlueMeter.Models;
using BlueMeter.Services;
using BlueMeter.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BlueMeter;

public partial class MainWindow : FluentWindow
{
    private bool _allowClose;
    private IReadOnlyList<DeviceBatterySnapshot>? _pendingSnapshots;
    private bool _themeWatched = true;
    private int _deviceUpdatesScheduled;

    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        ViewModel.RefreshRequested += OnViewModelRefreshRequested;
        ViewModel.HideToTrayRequested += OnHideToTrayRequested;
        ViewModel.OpenLogDirectoryRequested += OnOpenLogDirectoryRequested;
        ViewModel.OpenBluetoothSettingsRequested += OnOpenBluetoothSettingsRequested;
        Settings.EditRequested += OnSettingsEditRequested;

        SystemThemeWatcher.Watch(this);
        IsVisibleChanged += OnWindowIsVisibleChanged;
    }

    public event EventHandler? RefreshRequested;

    /// <summary>设置面板发起的修改；由 App 交给 SettingsService 校验、保存并回写界面。</summary>
    public event EventHandler<Action<AppSettings>>? SettingsEditRequested;

    public MainViewModel ViewModel { get; }

    /// <summary>设置面板 ViewModel；控件值的同步与编辑提交都在这里。</summary>
    public SettingsViewModel Settings => ViewModel.Settings;

    /// <summary>设置文件路径，仅用于在设置面板里提示保存位置。</summary>
    public string? SettingsFilePath { get; set; }

    /// <summary>
    /// 同步最新设置：重新绑定控件，并按过滤条件重新投影当前列表。
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(new Action(() => ApplySettings(settings)));
            }

            return;
        }

        ViewModel.ApplySettings(settings);
        Settings.ApplySettings(settings, SettingsFilePath);
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Focus();
    }

    public void AllowClose() => _allowClose = true;

    /// <summary>
    /// 可从服务后台线程调用；同一帧内只提交一次 UI 更新。
    /// </summary>
    public void QueueDevicesUpdate(IReadOnlyList<DeviceBatterySnapshot> snapshots)
    {
        Volatile.Write(ref _pendingSnapshots, snapshots);

        if (Interlocked.Exchange(ref _deviceUpdatesScheduled, 1) != 0)
        {
            return;
        }

        if (Dispatcher.HasShutdownStarted)
        {
            Volatile.Write(ref _deviceUpdatesScheduled, 0);
            return;
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
                Volatile.Write(ref _deviceUpdatesScheduled, 0);
                var pending = Volatile.Read(ref _pendingSnapshots);
                if (pending is not null)
                {
                    ViewModel.ApplySnapshots(pending);
                }
            }));
    }

    public void SetGlobalStatus(string message, bool isWarning = false) =>
        ViewModel.SetStatus(message, isWarning ? StatusSeverity.Caution : StatusSeverity.Normal);

    /// <summary>服务状态统一走 ViewModel：底栏文案、状态点和扫描失败 InfoBar 同源。</summary>
    public void SetServiceStatus(DeviceServiceStatus status) =>
        ViewModel.ApplyServiceStatus(status);

    public void SetRefreshing(bool refreshing) => ViewModel.IsRefreshing = refreshing;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            AppLog.Info("主窗口关闭已拦截，隐藏到托盘", "Window");
            return;
        }

        // 必须在窗口句柄仍有效时取消主题监听：OnClosed 时 WM_DESTROY 已执行，
        // WPF-UI 会抛 “Could not get window handle”。
        UnwatchSystemTheme();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // 启动即托盘且从未显示过的窗口没有句柄，UnwatchSystemTheme 会直接跳过。
        UnwatchSystemTheme();
        IsVisibleChanged -= OnWindowIsVisibleChanged;
        ViewModel.Dispose();
        base.OnClosed(e);
    }

    private void UnwatchSystemTheme()
    {
        if (!_themeWatched)
        {
            return;
        }

        _themeWatched = false;

        if (new WindowInteropHelper(this).Handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            SystemThemeWatcher.UnWatch(this);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Debug($"取消主题监听时窗口句柄已释放：{ex.Message}", "Window");
        }
        catch (Exception ex)
        {
            AppLog.Warn("取消系统主题监听失败", "Window", ex);
        }
    }

    /// <summary>窗口隐藏到托盘时暂停“x 分钟前”的计时器，重新显示时立即补一次。</summary>
    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            ViewModel.StartClock();
            ViewModel.RefreshRelativeTimes();
        }
        else
        {
            ViewModel.StopClock();
        }
    }

    private void OnSettingsEditRequested(object? sender, Action<AppSettings> edit) =>
        SettingsEditRequested?.Invoke(this, edit);

    /// <summary>Esc 先收起设置面板，再隐藏窗口；F5 立即刷新；Ctrl+, 切换设置面板。</summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!ViewModel.CloseSettings())
            {
                Hide();
                AppLog.Info("用户按 Esc 隐藏到托盘", "Window");
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            if (ViewModel.RefreshCommand.CanExecute(null))
            {
                ViewModel.RefreshCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ViewModel.ToggleSettings();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 列表行键盘导航：Tab 进入后可用上下方向键在行间移动，Home/End 跳到首尾。
    /// 一期没有详情页，Enter/Space 不触发任何操作。
    /// </summary>
    private void OnDeviceRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End) ||
            sender is not FrameworkElement row)
        {
            return;
        }

        var rows = GetDeviceRows();
        var index = rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        var target = e.Key switch
        {
            Key.Up => index - 1,
            Key.Down => index + 1,
            Key.Home => 0,
            _ => rows.Count - 1,
        };

        if (target >= 0 && target < rows.Count)
        {
            rows[target].Focus();
            rows[target].BringIntoView();
        }

        e.Handled = true;
    }

    /// <summary>按显示顺序取出当前列表行的卡片 Border；容器尚未生成时跳过。</summary>
    private List<FrameworkElement> GetDeviceRows()
    {
        var rows = new List<FrameworkElement>(DeviceList.Items.Count);
        for (var i = 0; i < DeviceList.Items.Count; i++)
        {
            if (DeviceList.ItemContainerGenerator.ContainerFromIndex(i) is not DependencyObject container)
            {
                continue;
            }

            var card = FindFocusableCard(container);
            if (card is not null)
            {
                rows.Add(card);
            }
        }

        return rows;
    }

    private static Border? FindFocusableCard(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Border { Focusable: true } card)
            {
                return card;
            }

            var nested = FindFocusableCard(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void OnViewModelRefreshRequested(object? sender, EventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OnHideToTrayRequested(object? sender, EventArgs e)
    {
        Hide();
        AppLog.Info("用户从主窗口隐藏到托盘", "Window");
    }

    private void OnOpenLogDirectoryRequested(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{AppLog.LogDirectory}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn("打开日志目录失败", "Window", ex);
        }
    }

    private void OnOpenBluetoothSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:bluetooth",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn("打开系统蓝牙设置失败", "Window", ex);
        }
    }
}
