using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BlueMeter.Models;
using BlueMeter.Services;

namespace BlueMeter;

public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private SettingsService? _settingsService;
    private IDeviceBatteryService? _deviceBatteryService;
    private TrayIconService? _trayIconService;
    private LowBatteryNotifier? _lowBatteryNotifier;
    private MainWindow? _mainWindow;
    private SettingsCoordinator? _settingsCoordinator;
    private TrayPresenter? _trayPresenter;
    private CancellationTokenSource? _appCts;
    private int _exitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.Initialize();

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            AppLog.Info("检测到已有 BlueMeter 实例，当前实例退出", "App");
            _singleInstance.Dispose();
            _singleInstance = null;
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
            _appCts?.Cancel();
            _deviceBatteryService?.Dispose();
            _lowBatteryNotifier?.Dispose();
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
            _appCts?.Dispose();
            _appCts = null;
            _singleInstance?.Dispose();
            _singleInstance = null;
            base.OnExit(e);
        }
    }

    private void InitializeApplication()
    {
        _settingsService = new SettingsService();
        var settingsService = _settingsService;
        _appCts = new CancellationTokenSource();

        // 设置里开着开机启动但注册表没有（重装/换路径）时补齐，关着但残留时清掉。
        var startupRegistration = new StartupRegistrationService();
        startupRegistration.Reconcile(settingsService.Current.RunAtStartup);

        var deviceService = new DeviceBatteryService(settingsService);
        _deviceBatteryService = deviceService;
        deviceService.DevicesUpdated += OnDevicesUpdated;
        deviceService.LowBatteryDetected += OnLowBatteryDetected;
        deviceService.StatusChanged += OnDeviceStatusChanged;

        var trayIconService = new TrayIconService();
        _trayIconService = trayIconService;
        trayIconService.ShowRequested += OnShowRequested;
        trayIconService.ExitRequested += OnExitRequested;
        trayIconService.RefreshRequested += OnRefreshRequested;

        var trayPresenter = new TrayPresenter(
            trayIconService,
            () => settingsService.Current,
            () => deviceService.Current);
        _trayPresenter = trayPresenter;
        trayPresenter.ShowScanning();

        _lowBatteryNotifier = new LowBatteryNotifier(
            () => settingsService.Current.LowBatteryNotificationsEnabled,
            trayIconService.ShowNotification);

        var mainWindow = new MainWindow();
        _mainWindow = mainWindow;
        mainWindow.RefreshRequested += OnRefreshRequested;
        mainWindow.SettingsEditRequested += OnSettingsEditRequested;

        var settingsCoordinator = new SettingsCoordinator(
            settingsService,
            startupRegistration.TrySetEnabled,
            apply: mainWindow.ApplySettings,
            committed: _ => trayPresenter.ApplySettings(),
            notify: trayIconService.ShowNotification);
        _settingsCoordinator = settingsCoordinator;

        mainWindow.SettingsFilePath = settingsCoordinator.SettingsPath;
        mainWindow.ApplySettings(settingsService.Current);
        mainWindow.SetGlobalStatus("正在读取已配对设备与 2.4G 接收器…");
        trayPresenter.ApplySettings();

        if (settingsService.Current.StartMinimized)
        {
            AppLog.Info("按设置启动到托盘，不显示主窗口", "App");
        }
        else
        {
            mainWindow.Show();
        }

        _ = deviceService.StartAsync(_appCts.Token);

        // 第二个实例会通过命名管道请这里把窗口显示出来。
        _singleInstance?.StartListening(() => Dispatcher.BeginInvoke(new Action(ShowMainWindow)));

        AppLog.Info("托盘、窗口、设置与设备服务初始化完成", "App");
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnShowRequested(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void ShowMainWindow() => _mainWindow?.ShowAndActivate();

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        var service = _deviceBatteryService;
        var appCts = _appCts;
        if (service is null ||
            appCts is null ||
            Volatile.Read(ref _exitRequested) != 0)
        {
            return;
        }

        _trayPresenter?.SetRefreshBusy(true);
        _mainWindow?.SetRefreshing(true);

        if (service.Current.Count == 0)
        {
            _trayPresenter?.ShowScanning();
        }

        try
        {
            await service.RefreshAsync(manual: true, appCts.Token);
        }
        catch (OperationCanceledException)
        {
            // 退出或取消时忽略。
        }
        catch (Exception ex)
        {
            AppLog.Error("手动刷新失败", ex, "App");
        }
        finally
        {
            _trayPresenter?.SetRefreshBusy(false);
            _mainWindow?.SetRefreshing(false);
        }
    }

    private void OnDevicesUpdated(
        object? sender,
        IReadOnlyList<DeviceBatterySnapshot> snapshots)
    {
        _mainWindow?.QueueDevicesUpdate(snapshots);
        _trayPresenter?.ApplySnapshots(snapshots, _deviceBatteryService?.Status);
    }

    private void OnDeviceStatusChanged(
        object? sender,
        DeviceServiceStatus status)
    {
        // 严重级别与扫描失败 InfoBar 都由 ViewModel 根据服务状态统一决定。
        _mainWindow?.SetServiceStatus(status);
        _trayPresenter?.ApplyServiceStatus(status);
    }

    private void OnLowBatteryDetected(
        object? sender,
        IReadOnlyList<LowBatteryNotice> notices)
    {
        _lowBatteryNotifier?.Notify(notices);
    }

    /// <summary>
    /// 设置面板的修改交给 <see cref="SettingsCoordinator"/> 落盘：回显、回滚与
    /// 开机启动注册表副作用都在那里统一处理，保存失败时界面与磁盘不会不一致。
    /// </summary>
    private void OnSettingsEditRequested(object? sender, Action<AppSettings> edit)
    {
        _settingsCoordinator?.TryUpdate(edit);
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

    private async void ExitApplication()
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) != 0)
        {
            return;
        }

        AppLog.Info("开始退出流程", "App");

        try
        {
            _appCts?.Cancel();

            if (_deviceBatteryService is not null)
            {
                var stopTask = _deviceBatteryService.StopAsync();
                var completed = await Task.WhenAny(
                    stopTask,
                    Task.Delay(TimeSpan.FromSeconds(3)));

                if (completed == stopTask)
                {
                    try
                    {
                        await stopTask;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("设备服务停止时出错", ex, "App");
                    }
                }
                else
                {
                    AppLog.Warn("设备服务停止超时，执行强制释放", "App");
                    _deviceBatteryService.Dispose();
                }
            }

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
            _appCts?.Dispose();
            _appCts = null;
            Shutdown();
        }
    }
}
