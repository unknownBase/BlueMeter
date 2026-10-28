using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BlueMeter.Models;
using BlueMeter.Services;

namespace BlueMeter;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\BlueMeter.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private SettingsService? _settingsService;
    private StartupRegistrationService? _startupRegistration;
    private IDeviceBatteryService? _deviceBatteryService;
    private TrayIconService? _trayIconService;
    private LowBatteryNotifier? _lowBatteryNotifier;
    private MainWindow? _mainWindow;
    private CancellationTokenSource? _appCts;
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
            ReleaseSingleInstance();
            base.OnExit(e);
        }
    }

    private void InitializeApplication()
    {
        _settingsService = new SettingsService();
        _appCts = new CancellationTokenSource();

        // 设置里开着开机启动但注册表没有（重装/换路径）时补齐，关着但残留时清掉。
        _startupRegistration = new StartupRegistrationService();
        _startupRegistration.Reconcile(_settingsService.Current.RunAtStartup);

        _deviceBatteryService = new DeviceBatteryService(_settingsService);
        _deviceBatteryService.DevicesUpdated += OnDevicesUpdated;
        _deviceBatteryService.LowBatteryDetected += OnLowBatteryDetected;
        _deviceBatteryService.StatusChanged += OnDeviceStatusChanged;

        _trayIconService = new TrayIconService();
        _trayIconService.ShowRequested += OnShowRequested;
        _trayIconService.ExitRequested += OnExitRequested;
        _trayIconService.RefreshRequested += OnRefreshRequested;
        _trayIconService.SetToolTip(TraySummaryBuilder.Scanning());

        _lowBatteryNotifier = new LowBatteryNotifier(
            () => _settingsService?.Current.LowBatteryNotificationsEnabled ?? false,
            message => _trayIconService?.ShowNotification(message));

        _mainWindow = new MainWindow();
        _mainWindow.RefreshRequested += OnRefreshRequested;
        _mainWindow.SettingsFilePath = _settingsService.SettingsPath;
        _mainWindow.SettingsEditRequested += OnSettingsEditRequested;
        _mainWindow.ApplySettings(_settingsService.Current);
        _mainWindow.SetGlobalStatus("正在读取已配对设备与 2.4G 接收器…");

        if (_settingsService.Current.StartMinimized)
        {
            AppLog.Info("按设置启动到托盘，不显示主窗口", "App");
        }
        else
        {
            _mainWindow.Show();
        }

        _ = _deviceBatteryService.StartAsync(_appCts.Token);

        AppLog.Info("托盘、窗口、设置与设备服务初始化完成", "App");
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

        _trayIconService?.SetRefreshBusy(true);
        _mainWindow?.SetRefreshing(true);

        if (service.Current.Count == 0)
        {
            _trayIconService?.SetToolTip(TraySummaryBuilder.Scanning());
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
            _trayIconService?.SetRefreshBusy(false);
            _mainWindow?.SetRefreshing(false);
        }
    }

    private void OnDevicesUpdated(
        object? sender,
        IReadOnlyList<DeviceBatterySnapshot> snapshots)
    {
        _mainWindow?.QueueDevicesUpdate(snapshots);

        var hasHid24G = snapshots.Any(
            snapshot => snapshot.Transport == DeviceTransportKind.Hid24G);

        if (!hasHid24G &&
            _deviceBatteryService?.Status is
                { State: DeviceServiceState.NoAdapter or DeviceServiceState.BluetoothOff } serviceStatus)
        {
            _trayIconService?.SetToolTip(TraySummaryBuilder.StatusHint(serviceStatus.Message));
            return;
        }

        _trayIconService?.SetToolTip(TraySummaryBuilder.Build(snapshots));
    }

    private void OnDeviceStatusChanged(
        object? sender,
        DeviceServiceStatus status)
    {
        _mainWindow?.SetGlobalStatus(
            status.Message,
            status.State is DeviceServiceState.NoAdapter
                or DeviceServiceState.BluetoothOff
                or DeviceServiceState.Error
            || status.Message.Contains("不可用", StringComparison.Ordinal));

        if (status.State is DeviceServiceState.NoAdapter
            or DeviceServiceState.BluetoothOff
            or DeviceServiceState.Error)
        {
            var hasHid24G = _deviceBatteryService?.Current.Any(
                snapshot => snapshot.Transport == DeviceTransportKind.Hid24G) == true;
            if (!hasHid24G)
            {
                _trayIconService?.SetToolTip(TraySummaryBuilder.StatusHint(status.Message));
            }
        }
    }

    private void OnLowBatteryDetected(
        object? sender,
        IReadOnlyList<LowBatteryNotice> notices)
    {
        _lowBatteryNotifier?.Notify(notices);
    }

    /// <summary>
    /// 设置面板的修改在这里落盘，并处理需要外部副作用的项（开机启动写注册表）。
    /// 保存或注册表写入失败时回滚到旧值，避免界面与磁盘/系统状态不一致。
    /// </summary>
    private void OnSettingsEditRequested(object? sender, Action<AppSettings> edit)
    {
        var service = _settingsService;
        if (service is null || edit is null)
        {
            return;
        }

        var previousRunAtStartup = service.Current.RunAtStartup;

        if (!service.Update(edit))
        {
            AppLog.Warn("设置保存失败，已放弃本次修改", "Settings");
            _trayIconService?.ShowNotification("设置保存失败，已恢复为原值。");
            _mainWindow?.ApplySettings(service.Current);
            return;
        }

        var current = service.Current;
        if (current.RunAtStartup != previousRunAtStartup &&
            _startupRegistration?.TrySetEnabled(current.RunAtStartup) == false)
        {
            service.Update(settings => settings.RunAtStartup = previousRunAtStartup);
            _trayIconService?.ShowNotification("开机启动设置失败，请检查系统权限。");
        }

        _mainWindow?.ApplySettings(service.Current);
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
