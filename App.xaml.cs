using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BlueMeter.Services;

namespace BlueMeter;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\BlueMeter.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private SettingsService? _settingsService;
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private int _exitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.Initialize();

        if (!TryAcquireSingleInstance())
        {
            AppLog.Info("检测到已有 BlueMeter 实例，当前实例退出", "App");
            Shutdown(0);
            return;
        }

        RegisterGlobalExceptionHandlers();
        SessionEnding += OnSessionEnding;

        try
        {
            InitializeApplication();
        }
        catch (Exception ex)
        {
            AppLog.Error("应用启动失败", ex, "App");
            MessageBox.Show(
                $"BlueMeter 启动失败，详情已写入日志：\n{AppLog.LogDirectory}",
                "BlueMeter",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ExitApplication();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _trayIconService?.Dispose();
            _settingsService?.Save();
            AppLog.Info("BlueMeter 已退出", "App");
        }
        catch (Exception ex)
        {
            AppLog.Error("退出清理失败", ex, "App");
        }
        finally
        {
            ReleaseSingleInstance();
            base.OnExit(e);
        }
    }

    private void InitializeApplication()
    {
        _settingsService = new SettingsService();
        _trayIconService = new TrayIconService();
        _trayIconService.ShowRequested += OnShowRequested;
        _trayIconService.ExitRequested += OnExitRequested;
        _trayIconService.SetToolTip("BlueMeter · 托盘已就绪");

        _mainWindow = new MainWindow();

        if (_settingsService.Current.StartMinimized)
        {
            AppLog.Info("按设置启动到托盘，不显示主窗口", "App");
        }
        else
        {
            _mainWindow.Show();
        }

        AppLog.Info("托盘、窗口与设置初始化完成", "App");
    }

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                SingleInstanceMutexName,
                out var createdNew);

            _ownsSingleInstanceMutex = createdNew;

            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            return createdNew;
        }
        catch (Exception ex)
        {
            AppLog.Error("单实例互斥体创建失败", ex, "App");
            return false;
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnShowRequested(object? sender, EventArgs e)
    {
        _mainWindow?.ShowAndActivate();
    }

    private void OnRefreshRequested(object? sender, EventArgs e)
    {
        // M2 接入设备扫描后，这里会触发一次排队刷新。
        AppLog.Info("收到手动刷新请求；设备扫描尚未接入", "Tray");
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        ExitApplication();
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        AppLog.Info("系统会话即将结束，直接退出而不隐藏窗口", "App");
        ExitApplication();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("UI 线程未处理异常", e.Exception, "App");
        _trayIconService?.ShowNotification("发生未处理异常，详情已写入日志。");
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error(
            "AppDomain 未处理异常",
            e.ExceptionObject as Exception,
            "App");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("未观察的任务异常", e.Exception, "App");
        e.SetObserved();
    }

    private void ExitApplication()
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) != 0)
        {
            return;
        }

        AppLog.Info("开始退出流程", "App");

        try
        {
            _settingsService?.Save();
            _trayIconService?.Dispose();

            if (_mainWindow is not null)
            {
                _mainWindow.AllowClose();
                _mainWindow.Close();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("退出流程出错", ex, "App");
        }
        finally
        {
            Shutdown();
        }
    }

    private void ReleaseSingleInstance()
    {
        if (_singleInstanceMutex is null)
        {
            return;
        }

        try
        {
            if (_ownsSingleInstanceMutex)
            {
                _singleInstanceMutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("释放单实例互斥体失败", "App", ex);
        }
        finally
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            _ownsSingleInstanceMutex = false;
        }
    }
}
