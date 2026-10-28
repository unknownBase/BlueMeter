using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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
    private int _pendingLowBatteryThreshold = 20;
    private int _updateScheduled;

    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
    }

    public event EventHandler? RefreshRequested;

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

    public void AllowClose()
    {
        _allowClose = true;
    }

    /// <summary>
    /// 可从服务后台线程调用；同一帧内只提交一次 UI 更新。
    /// </summary>
    public void QueueDevicesUpdate(
        IReadOnlyList<DeviceBatterySnapshot> snapshots,
        int lowBatteryThreshold)
    {
        Volatile.Write(ref _pendingSnapshots, snapshots);
        Volatile.Write(ref _pendingLowBatteryThreshold, lowBatteryThreshold);

        if (Interlocked.Exchange(ref _updateScheduled, 1) != 0)
        {
            return;
        }

        if (Dispatcher.HasShutdownStarted)
        {
            Interlocked.Exchange(ref _updateScheduled, 0);
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                Interlocked.Exchange(ref _updateScheduled, 0);
                var pending = Volatile.Read(ref _pendingSnapshots);
                if (pending is not null)
                {
                    ApplySnapshots(
                        pending,
                        Volatile.Read(ref _pendingLowBatteryThreshold));
                }
            }));
    }

    public void SetGlobalStatus(string message)
    {
        if (Dispatcher.CheckAccess())
        {
            GlobalStatusText.Text = message;
            return;
        }

        if (!Dispatcher.HasShutdownStarted)
        {
            Dispatcher.BeginInvoke(new Action(() => SetGlobalStatus(message)));
        }
    }

    public void SetRefreshing(bool refreshing)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                Dispatcher.BeginInvoke(new Action(() => SetRefreshing(refreshing)));
            }

            return;
        }

        RefreshButton.IsEnabled = !refreshing;
        RefreshButton.Content = refreshing ? "正在刷新…" : "立即刷新";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            AppLog.Info("主窗口关闭已拦截，隐藏到托盘", "Window");
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // 窗口句柄已销毁时 WPF-UI 可能抛 InvalidOperationException，不影响退出流程。
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

        base.OnClosed(e);
    }

    private void ApplySnapshots(
        IReadOnlyList<DeviceBatterySnapshot> snapshots,
        int lowBatteryThreshold)
    {
        var ordered = snapshots
            .OrderBy(snapshot => SortRank(snapshot, lowBatteryThreshold))
            .ThenBy(snapshot => snapshot.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        DeviceList.ItemsSource = ordered
            .Select(snapshot => new DeviceItemViewModel(snapshot, lowBatteryThreshold))
            .ToList();

        var bleCount = snapshots.Count(
            snapshot => snapshot.Transport == DeviceTransportKind.BluetoothLe);
        var classicCount = snapshots.Count(
            snapshot => snapshot.Transport == DeviceTransportKind.BluetoothClassic);
        var hid24GCount = snapshots.Count(
            snapshot => snapshot.Transport == DeviceTransportKind.Hid24G);

        SummaryText.Text = snapshots.Count == 0
            ? "尚未发现蓝牙或 2.4G 设备，稍后会自动重试"
            : $"已发现 {snapshots.Count} 台设备 · BLE {bleCount} · " +
              $"经典蓝牙 {classicCount} · 2.4G {hid24GCount}";

        EmptyStateText.Visibility = snapshots.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        DeviceList.Visibility = snapshots.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static int SortRank(DeviceBatterySnapshot snapshot, int lowBatteryThreshold)
    {
        if (snapshot.BatteryPercent is int percent && percent <= lowBatteryThreshold)
        {
            return 0;
        }

        if (snapshot.BatteryPercent.HasValue)
        {
            return 1;
        }

        return snapshot.Status is BatteryReadStatus.Stale or BatteryReadStatus.Unavailable
            ? 2
            : 3;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        SetRefreshing(true);
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHideToTrayClick(object sender, RoutedEventArgs e)
    {
        Hide();
        AppLog.Info("用户从主窗口隐藏到托盘", "Window");
    }

    private void OnOpenLogDirectoryClick(object sender, RoutedEventArgs e)
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
}
