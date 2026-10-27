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
        _taskbarIcon.ContextMenu = BuildContextMenu();

        // ForceCreate 会同时处理 Explorer 重启后的图标恢复。
        _taskbarIcon.ForceCreate();
        AppLog.Info("托盘图标已创建", "Tray");
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? ExitRequested;

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
            IsEnabled = false,
            ToolTip = "设备扫描将在 M2 阶段接入",
        };

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
