using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using BlueMeter.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BlueMeter;

public partial class MainWindow : FluentWindow
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
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

    public void AllowClose()
    {
        _allowClose = true;
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
        SystemThemeWatcher.UnWatch(this);
        base.OnClosed(e);
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
