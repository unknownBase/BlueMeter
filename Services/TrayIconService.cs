using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace BlueMeter.Services;

/// <summary>
/// 托盘图标的唯一所有者。所有公开方法都应在 UI 线程调用。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int MaxToolTipLength = 120;

    private readonly TaskbarIcon _taskbarIcon;
    private readonly List<UIElement> _batterySection = new();
    private ContextMenu? _contextMenu;
    private MenuItem? _refreshItem;
    private List<string> _batteryHeaders = new();
    private bool _disposed;

    public TrayIconService()
    {
        _taskbarIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri(
                "pack://application:,,,/Assets/BlueMeter.ico",
                UriKind.Absolute)),
            ToolTipText = "BlueMeter",
            Visibility = Visibility.Visible,
        };

        _taskbarIcon.TrayLeftMouseDown += OnTrayLeftMouseDown;
        _taskbarIcon.TrayMouseDoubleClick += OnTrayLeftMouseDown;
        _contextMenu = BuildContextMenu();
        _taskbarIcon.ContextMenu = _contextMenu;

        // ForceCreate 会同时处理 Explorer 重启后的图标恢复。
        _taskbarIcon.ForceCreate();
        AppLog.Info("托盘图标已创建", "Tray");
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? RefreshRequested;

    public string ToolTipText => _taskbarIcon.ToolTipText ?? string.Empty;

    public void SetToolTip(string? text)
    {
        if (_disposed)
        {
            return;
        }

        if (!_taskbarIcon.Dispatcher.CheckAccess())
        {
            _taskbarIcon.Dispatcher.BeginInvoke(new Action(() => SetToolTip(text)));
            return;
        }

        var normalized = NormalizeToolTip(text);
        if (string.Equals(_taskbarIcon.ToolTipText, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _taskbarIcon.ToolTipText = normalized;
    }

    public void SetRefreshBusy(bool busy)
    {
        if (_disposed)
        {
            return;
        }

        if (!_taskbarIcon.Dispatcher.CheckAccess())
        {
            _taskbarIcon.Dispatcher.BeginInvoke(new Action(() => SetRefreshBusy(busy)));
            return;
        }

        if (_refreshItem is null)
        {
            return;
        }

        _refreshItem.Header = busy ? "正在刷新…" : "立即刷新";
        _refreshItem.IsEnabled = !busy;
    }

    public void ShowNotification(string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!_taskbarIcon.Dispatcher.CheckAccess())
        {
            _taskbarIcon.Dispatcher.BeginInvoke(new Action(() => ShowNotification(message)));
            return;
        }

        try
        {
            _taskbarIcon.ShowNotification(
                "BlueMeter",
                message,
                NotificationIcon.Warning);
        }
        catch (Exception ex)
        {
            AppLog.Warn("托盘通知发送失败", "Tray", ex);
        }
    }

    /// <summary>
    /// 更新右键菜单里的“设备电量”段。传入 <paramref name="enabled"/> 为 false
    /// 或条目为空时整段（含分隔线）不显示；内容未变时不重建菜单项。
    /// </summary>
    public void SetDeviceBatteryMenu(bool enabled, IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (_disposed)
        {
            return;
        }

        if (!_taskbarIcon.Dispatcher.CheckAccess())
        {
            _taskbarIcon.Dispatcher.BeginInvoke(
                new Action(() => SetDeviceBatteryMenu(enabled, headers)));
            return;
        }

        var desired = enabled
            ? headers.Take(TrayMenuBuilder.MaxDevices).ToList()
            : new List<string>();

        if (desired.SequenceEqual(_batteryHeaders))
        {
            return;
        }

        _batteryHeaders = desired;
        RebuildBatterySection();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _taskbarIcon.TrayLeftMouseDown -= OnTrayLeftMouseDown;
            _taskbarIcon.TrayMouseDoubleClick -= OnTrayLeftMouseDown;
            _taskbarIcon.ContextMenu = null;
            _taskbarIcon.Dispose();
            AppLog.Info("托盘图标已释放", "Tray");
        }
        catch (Exception ex)
        {
            AppLog.Warn("释放托盘图标时出错", "Tray", ex);
        }
    }

    private void RebuildBatterySection()
    {
        var menu = _contextMenu;
        if (menu is null)
        {
            return;
        }

        foreach (var element in _batterySection)
        {
            menu.Items.Remove(element);
        }

        _batterySection.Clear();

        if (_batteryHeaders.Count == 0)
        {
            return;
        }

        var section = new List<UIElement>
        {
            new Separator(),
            new MenuItem
            {
                Header = TrayMenuBuilder.SectionTitle,
                FontWeight = FontWeights.SemiBold,
                IsEnabled = false,
            },
        };

        foreach (var header in _batteryHeaders)
        {
            // 只读展示：不可点击，避免用户误以为能从这里操作设备。
            section.Add(new MenuItem { Header = header, IsEnabled = false });
        }

        var insertAt = _refreshItem is null
            ? 0
            : menu.Items.IndexOf(_refreshItem) + 1;

        for (var index = 0; index < section.Count; index++)
        {
            menu.Items.Insert(insertAt + index, section[index]);
        }

        _batterySection.AddRange(section);
    }

    private ContextMenu BuildContextMenu()
    {
        var showItem = new MenuItem
        {
            Header = "显示主窗口",
            FontWeight = FontWeights.SemiBold,
        };
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        var refreshItem = new MenuItem
        {
            Header = "立即刷新",
            ToolTip = "扫描蓝牙与 2.4G 设备并读取电量",
        };
        refreshItem.Click += (_, _) =>
        {
            if (refreshItem.IsEnabled)
            {
                RefreshRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        _refreshItem = refreshItem;

        var exitItem = new MenuItem
        {
            Header = "退出",
        };
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private void OnTrayLeftMouseDown(object? sender, RoutedEventArgs e)
    {
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string NormalizeToolTip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "BlueMeter";
        }

        var singleLine = text
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return singleLine.Length <= MaxToolTipLength
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, MaxToolTipLength - 1), "…");
    }
}
