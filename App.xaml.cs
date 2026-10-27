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
    private IBluetoothBatteryService? _bluetoothBatteryService;
    private TrayIconService? _trayIconService;
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
            _bluetoothBatteryService?.Dispose();
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

        _bluetoothBatteryService = new BluetoothBatteryService(_settingsService);
        _bluetoothBatteryService.DevicesUpdated += OnDevicesUpdated;
        _bluetoothBatteryService.LowBatteryDetected += OnLowBatteryDetected;
        _bluetoothBatteryService.StatusChanged += OnBluetoothStatusChanged;

        _trayIconService = new TrayIconService();
        _trayIconService.ShowRequested += OnShowRequested;
        _trayIconService.ExitRequested += OnExitRequested;
        _trayIconService.RefreshRequested += OnRefreshRequested;
        _trayIconService.SetToolTip("BlueMeter · 正在扫描已配对设备…");

        _mainWindow = new MainWindow();
        _mainWindow.RefreshRequested += OnRefreshRequested;
        _mainWindow.SetGlobalStatus("正在读取已配对设备…");

        if (_settingsService.Current.StartMinimized)
        {
            AppLog.Info("按设置启动到托盘，不显示主窗口", "App");
        }
        else
        {
            _mainWindow.Show();
        }

        _ = _bluetoothBatteryService.StartAsync(_appCts.Token);

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
        if (_bluetoothBatteryService is null ||
            _appCts is null ||
            Volatile.Read(ref _exitRequested) != 0)
        {
            return;
        }

        _trayIconService?.SetRefreshBusy(true);
        _mainWindow?.SetRefreshing(true);

        try
        {
            await _bluetoothBatteryService.RefreshAsync(manual: true, _appCts.Token);
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
        var threshold = _settingsService?.Current.LowBatteryThreshold ?? 20;
        _mainWindow?.QueueDevicesUpdate(snapshots, threshold);

        if (_bluetoothBatteryService?.Status is { State: BluetoothServiceState.NoAdapter or BluetoothServiceState.BluetoothOff } serviceStatus)
        {
            _trayIconService?.SetToolTip($"BlueMeter · {serviceStatus.Message}");
            return;
        }

        _trayIconService?.SetToolTip(BuildTrayToolTip(snapshots));
    }

    private void OnBluetoothStatusChanged(
        object? sender,
        BluetoothServiceStatus status)
    {
        _mainWindow?.SetGlobalStatus(status.Message);

        if (status.State is BluetoothServiceState.NoAdapter
            or BluetoothServiceState.BluetoothOff
            or BluetoothServiceState.Error)
        {
            _trayIconService?.SetToolTip($"BlueMeter · {status.Message}");
        }
    }

    private void OnLowBatteryDetected(
        object? sender,
        IReadOnlyList<LowBatteryNotice> notices)
    {
        AppLog.Debug(
            $"收到 {notices.Count} 条低电量事件，M3 将聚合为托盘通知",
            "Battery");
    }

    private static string BuildTrayToolTip(
        IReadOnlyList<DeviceBatterySnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return "BlueMeter · 未发现已配对设备";
        }

        var withBattery = snapshots
            .Where(snapshot => snapshot.BatteryPercent.HasValue)
            .ToList();

        if (withBattery.Count == 0)
        {
            return "BlueMeter · 已发现设备，暂无可读电量";
        }

        if (withBattery.Count == 1)
        {
            var snapshot = withBattery[0];
            var suffix = snapshot.Status == BatteryReadStatus.Ok
                ? string.Empty
                : "（暂时不可达）";
            return $"BlueMeter · {snapshot.Name} {snapshot.BatteryPercent}%{suffix}";
        }

        var lowest = withBattery.Min(snapshot => snapshot.BatteryPercent!.Value);
        var hasStaleValue = withBattery.Any(snapshot => snapshot.Status != BatteryReadStatus.Ok);
        return $"BlueMeter · {snapshots.Count} 台设备，最低 {lowest}%" +
               (hasStaleValue ? "（含旧值）" : string.Empty);
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

            if (_bluetoothBatteryService is not null)
            {
                var stopTask = _bluetoothBatteryService.StopAsync();
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
                    _bluetoothBatteryService.Dispose();
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
