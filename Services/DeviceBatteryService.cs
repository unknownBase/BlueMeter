using System.Diagnostics;
using BlueMeter.Models;
using Microsoft.Win32;

namespace BlueMeter.Services;

/// <summary>
/// 已配对蓝牙设备与 Rapoo / LINGBAO K98 2.4G 接收器的电量读取编排与刷新调度。
/// 枚举与属性解析交给 <see cref="BluetoothDeviceEnumerator"/>，状态合并交给
/// <see cref="BatteryStateStore"/>；本类型只负责生命周期、轮次编排与对外事件。
/// 所有 GATT 操作串行执行；手动刷新只排队一次，避免并发访问蓝牙栈。
/// </summary>
public sealed class DeviceBatteryService : IDeviceBatteryService
{
    private static readonly TimeSpan PerDeviceTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CycleBudget = TimeSpan.FromSeconds(30);

    private readonly SettingsService _settingsService;
    private readonly BleBatteryProbe _probe = new();
    private readonly BluetoothDeviceEnumerator _bluetooth = new();
    private readonly Hid24GDeviceEnumerator _hid24GEnumerator = new();
    private readonly Hid24GProbe _hid24GProbe = new();
    private readonly BatteryStateStore _store;
    private readonly CycleWaiter _waiter;
    private readonly SemaphoreSlim _cycleGate = new(1, 1);

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private DeviceServiceStatus _status = DeviceServiceStatus.Create(
        DeviceServiceState.Stopped,
        "尚未启动");
    private int _cycleCount;
    private int _refreshQueued;
    private int _appliedIntervalSeconds;
    private int _started;
    private int _disposed;

    public DeviceBatteryService(SettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _store = new BatteryStateStore(() => _settingsService.Current.LowBatteryThreshold);
        _waiter = new CycleWaiter(GetNextDelay);
    }

    public event EventHandler<IReadOnlyList<DeviceBatterySnapshot>>? DevicesUpdated;

    public event EventHandler<IReadOnlyList<LowBatteryNotice>>? LowBatteryDetected;

    public event EventHandler<DeviceServiceStatus>? StatusChanged;

    public IReadOnlyList<DeviceBatterySnapshot> Current => _store.Snapshot();

    public DeviceServiceStatus Status => Volatile.Read(ref _status);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            AppLog.Warn("订阅系统电源事件失败，睡眠恢复后需等待下一轮刷新", "Power", ex);
        }

        _settingsService.SettingsChanged += OnSettingsChanged;
        _probe.BatteryUpdated += OnBatteryUpdated;
        Interlocked.Exchange(ref _appliedIntervalSeconds, CurrentIntervalSeconds());
        SetStatus(DeviceServiceState.Starting, "正在启动设备刷新");
        AppLog.Info("设备电量服务启动", "Service");

        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token));
        return Task.CompletedTask;
    }

    public async Task RefreshAsync(bool manual, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (manual)
        {
            if (!await _cycleGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                Interlocked.Exchange(ref _refreshQueued, 1);
                AppLog.Info("手动刷新已排队，等待当前轮次结束", "Cycle");
                return;
            }
        }
        else
        {
            await _cycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await RunOneOrQueuedCycleAsync(manual, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _cycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        Interlocked.Exchange(ref _started, 0);

        var cts = Interlocked.Exchange(ref _loopCts, null);
        var loopTask = _loopTask;
        _loopTask = null;

        try
        {
            cts?.Cancel();
        }
        catch
        {
            // 取消已释放的令牌源不影响退出。
        }

        if (loopTask is not null)
        {
            var completed = await Task.WhenAny(
                loopTask,
                Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);

            if (completed == loopTask)
            {
                try
                {
                    await loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 正常停止。
                }
                catch (Exception ex)
                {
                    AppLog.Error("刷新循环异常退出", ex, "Service");
                }
            }
            else
            {
                AppLog.Warn("刷新循环未在 3 秒内结束，继续执行释放", "Service");
            }
        }

        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch
        {
            // 未成功订阅时忽略。
        }

        _settingsService.SettingsChanged -= OnSettingsChanged;
        _probe.BatteryUpdated -= OnBatteryUpdated;

        try
        {
            await _probe.ReleaseAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("释放 BLE 通知订阅失败", "Service", ex);
        }

        cts?.Dispose();
        SetStatus(DeviceServiceState.Stopped, "已停止");
        AppLog.Info("设备电量服务停止", "Service");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _loopCts?.Cancel();
        }
        catch
        {
            // 忽略。
        }

        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch
        {
            // 忽略。
        }

        _settingsService.SettingsChanged -= OnSettingsChanged;
        _probe.BatteryUpdated -= OnBatteryUpdated;
        _probe.Dispose();
        _cycleGate.Dispose();
        _loopCts?.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _cycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await RunOneOrQueuedCycleAsync(manual: false, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _cycleGate.Release();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 单轮失败不能杀死循环。
                    AppLog.Error("刷新轮次失败", ex, "Cycle");
                }

                try
                {
                    await _waiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常取消。
        }
        catch (Exception ex)
        {
            AppLog.Error("刷新循环终止", ex, "Service");
        }
    }

    /// <summary>
    /// 调用方必须持有 <see cref="_cycleGate"/>。每次只消费一个排队标记，保证手动刷新不会堆积。
    /// </summary>
    private async Task RunOneOrQueuedCycleAsync(bool manual, CancellationToken cancellationToken)
    {
        do
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            await RunCycleCoreAsync(manual, cancellationToken).ConfigureAwait(false);
            manual = false;
        }
        while (Volatile.Read(ref _refreshQueued) != 0 &&
               !cancellationToken.IsCancellationRequested);
    }

    private async Task RunCycleCoreAsync(bool manual, CancellationToken cancellationToken)
    {
        var cycleNumber = Interlocked.Increment(ref _cycleCount);
        var stopwatch = Stopwatch.StartNew();
        AppLog.Info(
            $"开始第 {cycleNumber} 轮设备刷新{(manual ? "（手动）" : string.Empty)}",
            "Cycle");

        var bluetoothStatus = await _bluetooth
            .ProbeStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        var bluetoothUsable = bluetoothStatus.State is not (
            DeviceServiceState.NoAdapter or DeviceServiceState.BluetoothOff);
        var bluetoothEnumerationSucceeded = false;

        var bleEnumerationSucceeded = false;
        var classicEnumerationSucceeded = false;

        if (!bluetoothUsable)
        {
            _store.MarkBluetoothDevicesUnavailable();
            await ReleaseAllSubscriptionsAsync(bluetoothStatus.Message).ConfigureAwait(false);
        }
        else
        {
            var bleTask = _bluetooth.EnumerateAsync(DeviceTransportKind.BluetoothLe, cancellationToken);
            var classicTask = _bluetooth.EnumerateAsync(DeviceTransportKind.BluetoothClassic, cancellationToken);
            var bleResult = await bleTask.ConfigureAwait(false);
            var classicResult = await classicTask.ConfigureAwait(false);

            bleEnumerationSucceeded = bleResult.Succeeded;
            classicEnumerationSucceeded = classicResult.Succeeded;
            bluetoothEnumerationSucceeded = bleResult.Succeeded || classicResult.Succeeded;
            if (bluetoothEnumerationSucceeded)
            {
                var candidates = new List<DeviceCandidate>(
                    bleResult.Devices.Count + classicResult.Devices.Count);
                candidates.AddRange(bleResult.Devices);
                candidates.AddRange(classicResult.Devices);

                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in BluetoothDeviceEnumerator.Merge(candidates))
                {
                    seenKeys.Add(candidate.Key);
                    _store.UpsertDevice(candidate);
                }

                var removed = _store.RemoveMissingBluetoothDevices(
                    seenKeys,
                    bleResult.Succeeded && classicResult.Succeeded);
                foreach (var removedEntry in removed)
                {
                    if (removedEntry.BleSource is not null)
                    {
                        await _probe.ReleaseDeviceAsync(removedEntry.BleSource.Id).ConfigureAwait(false);
                    }

                    AppLog.Info($"设备已移除：{removedEntry.State.Name}", "Enum");
                }
            }
            else
            {
                AppLog.Warn(
                    $"第 {cycleNumber} 轮蓝牙枚举全部失败，保留上次蓝牙设备状态",
                    "Cycle");
            }
        }

        var hidResult = _hid24GEnumerator.Enumerate(cancellationToken);
        var seenHidKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (hidResult.Succeeded)
        {
            foreach (var device in hidResult.Devices)
            {
                seenHidKeys.Add(BatteryStateStore.BuildHidKey(device));
                _store.UpsertHidDevice(device);
            }

            if (hidResult.FailedInterfaceCount > 0)
            {
                // 接口存在但暂时无法只读打开时，保留旧条目并标为不可达，
                // 不要把它误判成“接收器已拔出”。
                _store.MarkHidDevicesMissing(seenHidKeys);
            }
            else
            {
                foreach (var removedEntry in _store.RemoveMissingHidDevices(seenHidKeys))
                {
                    AppLog.Info($"2.4G 设备已移除：{removedEntry.State.Name}", "Hid24G");
                }
            }
        }

        UpdateStatusAfterEnumeration(
            bluetoothStatus,
            bluetoothUsable,
            bluetoothEnumerationSucceeded,
            hidResult);
        PublishSnapshots();

        if (!bluetoothEnumerationSucceeded && !hidResult.Succeeded)
        {
            AppLog.Warn(
                $"第 {cycleNumber} 轮枚举全部失败，保留上次设备状态",
                "Cycle");
            return;
        }

        if (!bluetoothUsable && hidResult.Devices.Count == 0)
        {
            AppLog.Info(
                $"第 {cycleNumber} 轮跳过：{bluetoothStatus.Message}",
                "Cycle");
            return;
        }

        var readTargets = bleEnumerationSucceeded
            ? _store.GetBleReadTargets()
            : new List<DeviceEntry>();
        var hidReadTargets = hidResult.Succeeded
            ? _store.GetHid24GReadTargets()
            : new List<DeviceEntry>();
        var classicReadTargets = classicEnumerationSucceeded
            ? _store.GetClassicReadTargets()
            : new List<DeviceEntry>();
        var notices = new List<LowBatteryNotice>();
        var successCount = 0;
        var failureCount = 0;
        var skippedCount = 0;

        // 2.4G 读取是本地 HID 报告读取，先执行，避免被 BLE 单轮预算挤掉。
        foreach (var entry in hidReadTargets)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (stopwatch.Elapsed >= CycleBudget)
            {
                skippedCount++;
                continue;
            }

            MarkReading(entry);

            BatteryProbeResult result;
            try
            {
                result = _hid24GProbe.ReadBattery(entry.HidSource!, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = BatteryProbeResult.Unavailable(
                    "HidProbe",
                    ex.Message,
                    ex.HResult);
            }

            if (result.IsSuccess)
            {
                successCount++;
            }
            else if (result.Status != BatteryReadStatus.NotSupported)
            {
                failureCount++;
            }

            // 2.4G 失败时保留旧值并每轮重试；日志只在连续失败的第一轮告警，
            // 避免设备休眠时每轮刷一条 Warn。
            var shouldWarnFailure = RecordHidHealth(entry, result);

            _store.ApplyProbeResult(entry, result, notices);
            PublishSnapshots();

            if (!result.IsSuccess)
            {
                LogHidProbeFailure(entry, result, shouldWarnFailure);
            }
        }

        // 经典蓝牙：系统属性可读时直接应用；没有属性时对已连接的双模设备用 BLE GATT 兜底。
        foreach (var entry in classicReadTargets)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var classic = entry.ClassicSource!;
            BatteryProbeResult result;
            var usedBleFallback = false;
            var propertyPercent = classic.BatteryPercent;
            var chargingText = classic.ChargingText ?? (classic.IsCharging ? "充电中" : null);

            if (classic.IsConnected)
            {
                // 枚举拿到的是快照属性，连接后耳机会在系统属性里补上电量，所以每轮再读一次。
                var (freshPercent, freshChargingText) = await BluetoothDeviceEnumerator
                    .ReadSystemBatteryAsync(classic.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (freshPercent.HasValue)
                {
                    propertyPercent = freshPercent;
                    chargingText = freshChargingText ?? chargingText;
                }
            }

            if (propertyPercent is int classicPercent)
            {
                result = classic.IsConnected
                    ? BatteryProbeResult.Ok(classicPercent, supportsNotifications: false, chargingText)
                    : BatteryProbeResult.Cached(classicPercent, chargingText);
            }
            else if (classic.IsConnected && classic.Address.HasValue && classic.LeConnectable != false)
            {
                var budget = CycleBudget - stopwatch.Elapsed;
                var timeout = budget < PerDeviceTimeout ? budget : PerDeviceTimeout;
                if (timeout < TimeSpan.FromSeconds(1))
                {
                    skippedCount++;
                    continue;
                }

                MarkReading(entry);
                usedBleFallback = true;

                using var deviceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deviceCts.CancelAfter(timeout);

                try
                {
                    result = await _probe
                        .ReadByBluetoothAddressAsync(classic.Address.Value, deviceCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    result = BatteryProbeResult.Unavailable(
                        "Timeout",
                        $"经典蓝牙 BLE 兜底读取超过 {timeout.TotalSeconds:0.#} 秒");
                }
                catch (Exception ex)
                {
                    result = BatteryProbeResult.Unavailable(
                        "ClassicBleFallback",
                        ex.Message,
                        ex.HResult);
                }
            }
            else if (classic.IsConnected)
            {
                // 系统属性没有电量、LE 侧又不可连接（耳机常见的双模形态）：不要下“不支持”的结论，下轮再试。
                result = BatteryProbeResult.Unavailable(
                    "BleNotConnectable",
                    "系统未上报电量，且设备未开放 BLE 连接");
            }
            else
            {
                // 未连接且系统没有电量属性：当前没有可用数据源，保留旧值并标为暂时不可达。
                result = BatteryProbeResult.Unavailable("ClassicDisconnected", "设备未连接");
            }

            if (result.IsSuccess)
            {
                successCount++;
            }
            else if (result.Status != BatteryReadStatus.NotSupported)
            {
                failureCount++;
            }

            _store.ApplyProbeResult(entry, result, notices);
            PublishSnapshots();

            if (usedBleFallback && !result.IsSuccess)
            {
                LogProbeFailure(entry, result);
            }
            else if (!usedBleFallback && result.Status == BatteryReadStatus.NotSupported)
            {
                LogProbeFailure(entry, result);
            }
        }

        foreach (var entry in readTargets)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (stopwatch.Elapsed >= CycleBudget)
            {
                skippedCount++;
                continue;
            }

            var remaining = CycleBudget - stopwatch.Elapsed;
            var timeout = remaining < PerDeviceTimeout ? remaining : PerDeviceTimeout;
            if (timeout < TimeSpan.FromSeconds(1))
            {
                skippedCount++;
                continue;
            }

            MarkReading(entry);

            using var deviceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deviceCts.CancelAfter(timeout);

            BatteryProbeResult result;
            try
            {
                result = await _probe.ReadAsync(
                    entry.BleSource!.Id,
                    _settingsService.Current.UseGattNotifications,
                    deviceCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                result = BatteryProbeResult.Unavailable(
                    "Timeout",
                    $"单设备读取超过 {timeout.TotalSeconds:0.#} 秒");
            }
            catch (Exception ex)
            {
                result = BatteryProbeResult.Unavailable(
                    "Probe",
                    ex.Message,
                    ex.HResult);
            }

            if (result.IsSuccess)
            {
                successCount++;
            }
            else if (result.Status != BatteryReadStatus.NotSupported)
            {
                failureCount++;
            }

            _store.ApplyProbeResult(entry, result, notices);
            PublishSnapshots();

            if (!result.IsSuccess)
            {
                LogProbeFailure(entry, result);
            }
        }

        // 队列中的手动刷新可能在读取期间到达，当前轮结束后由外层循环立即补跑。
        PublishSnapshots();
        RaiseLowBatteryDetected(notices);

        var hidHealthText = hidReadTargets.Count == 0
            ? string.Empty
            : $"，{BuildHidHealthText(hidReadTargets)}";

        AppLog.Info(
            $"第 {cycleNumber} 轮完成：{_store.Count} 台设备，" +
            $"{readTargets.Count} 台 BLE，{hidReadTargets.Count} 台 2.4G，" +
            $"{classicReadTargets.Count} 台经典蓝牙，" +
            $"{successCount} 成功，{failureCount} 失败，" +
            $"{skippedCount} 跳过，用时 {stopwatch.Elapsed.TotalSeconds:0.0} 秒" +
            hidHealthText,
            "Cycle");
    }

    private void MarkReading(DeviceEntry entry)
    {
        _store.MarkReading(entry);
        PublishSnapshots();
    }

    private async Task ReleaseAllSubscriptionsAsync(string reason)
    {
        var hadSubscriptions = _probe.SubscriptionCount > 0;

        try
        {
            await _probe.ReleaseAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("释放全部 BLE 通知订阅失败", "BleProbe", ex);
        }

        _store.ClearNotificationSupport();

        if (hadSubscriptions)
        {
            AppLog.Info($"已释放全部 BLE 通知订阅：{reason}", "BleProbe");
        }
    }

    private void OnBatteryUpdated(object? sender, BleBatteryUpdate update)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var notices = new List<LowBatteryNotice>();
        if (!_store.TryApplyBleUpdate(
                update.DeviceId,
                update.BatteryPercent,
                update.Timestamp,
                notices))
        {
            return;
        }

        PublishSnapshots();
        RaiseLowBatteryDetected(notices);
    }

    private void LogProbeFailure(DeviceEntry entry, BatteryProbeResult result)
    {
        var message =
            $"{entry.State.Name}（{entry.State.Transport.ToLogText()}）电量读取失败：" +
            $"{result.FailureStage} {result.FailureMessage}" +
            (result.HResult.HasValue ? $" HRESULT=0x{result.HResult.Value:X8}" : string.Empty);

        var category = entry.State.Transport switch
        {
            DeviceTransportKind.Hid24G => "Hid24G",
            DeviceTransportKind.BluetoothClassic => "ClassicProbe",
            _ => "BleProbe",
        };

        if (result.Status == BatteryReadStatus.NotSupported)
        {
            AppLog.Debug(message, category);
        }
        else
        {
            AppLog.Warn(message, category);
        }
    }

    /// <summary>
    /// 记录 2.4G 读取健康度：成功时清零连续失败，并从失败中恢复时记一条 Info；
    /// 失败时返回 true 表示这是连续失败的第一轮，需要按 Warn 记录。
    /// </summary>
    private static bool RecordHidHealth(DeviceEntry entry, BatteryProbeResult result)
    {
        var health = entry.Health;
        if (result.IsSuccess)
        {
            var previousFailures = health.ConsecutiveFailures;
            if (health.RecordSuccess() && previousFailures > 0)
            {
                AppLog.Info(
                    $"{entry.State.Name} 2.4G 读取已恢复（此前连续 {previousFailures} 次失败）",
                    "Hid24G");
            }

            return false;
        }

        if (result.Status == BatteryReadStatus.NotSupported)
        {
            return false;
        }

        return health.RecordFailure();
    }

    private static void LogHidProbeFailure(
        DeviceEntry entry,
        BatteryProbeResult result,
        bool shouldWarn)
    {
        var failures = result.Status == BatteryReadStatus.NotSupported
            ? 0
            : entry.Health.ConsecutiveFailures;
        var message =
            $"{entry.State.Name}（{entry.State.Transport.ToLogText()}）电量读取失败：" +
            $"{result.FailureStage} {result.FailureMessage}" +
            (failures > 1 ? $"（连续第 {failures} 次）" : string.Empty);

        if (shouldWarn)
        {
            AppLog.Warn(message, "Hid24G");
        }
        else
        {
            AppLog.Debug(message, "Hid24G");
        }
    }

    /// <summary>轮次日志用的 2.4G 累计成功率，便于对比修复前后的读取稳定性。</summary>
    private static string BuildHidHealthText(List<DeviceEntry> entries)
    {
        long attempts = 0;
        long failures = 0;
        foreach (var entry in entries)
        {
            attempts += entry.Health.TotalAttempts;
            failures += entry.Health.TotalFailures;
        }

        if (attempts == 0)
        {
            return "2.4G 累计暂无数据";
        }

        return $"2.4G 累计 {attempts - failures}/{attempts} 次成功，" +
            $"失败率 {(double)failures / attempts * 100:0.0}%";
    }

    private void PublishSnapshots() =>
        RaiseSafely(DevicesUpdated, _store.Snapshot(), "DevicesUpdated");

    private void UpdateStatusAfterEnumeration(
        DeviceServiceStatus bluetoothStatus,
        bool bluetoothUsable,
        bool bluetoothEnumerationSucceeded,
        Hid24GEnumerationResult hidResult)
    {
        var hidAvailable = hidResult.Succeeded && hidResult.Devices.Count > 0;

        if (!bluetoothUsable)
        {
            if (hidAvailable)
            {
                SetStatus(
                    DeviceServiceState.Ready,
                    $"蓝牙不可用；已检测到 {hidResult.Devices.Count} 台 2.4G 接收器设备");
            }
            else
            {
                SetStatus(bluetoothStatus);
            }

            return;
        }

        if (!bluetoothEnumerationSucceeded)
        {
            if (hidAvailable)
            {
                SetStatus(
                    DeviceServiceState.Ready,
                    $"蓝牙枚举失败；2.4G 接收器设备 {hidResult.Devices.Count} 台可用");
            }
            else
            {
                SetStatus(DeviceServiceState.Error, "设备枚举失败，请查看日志");
            }

            return;
        }

        var message = hidAvailable
            ? $"{bluetoothStatus.Message}；2.4G 接收器设备 {hidResult.Devices.Count} 台"
            : bluetoothStatus.Message;
        if (bluetoothStatus.State == DeviceServiceState.Error)
        {
            SetStatus(
                DeviceServiceState.Ready,
                hidAvailable
                    ? $"蓝牙枚举成功；2.4G 接收器设备 {hidResult.Devices.Count} 台"
                    : "蓝牙设备枚举成功");
        }
        else
        {
            SetStatus(DeviceServiceStatus.Create(bluetoothStatus.State, message));
        }
    }

    private void RaiseLowBatteryDetected(IReadOnlyList<LowBatteryNotice> notices)
    {
        if (notices.Count == 0)
        {
            return;
        }

        foreach (var notice in notices)
        {
            AppLog.Info(
                $"低电量：{notice.DeviceName} {notice.BatteryPercent}%",
                "Battery");
        }

        RaiseSafely(LowBatteryDetected, notices, "LowBatteryDetected");
    }

    private void SetStatus(DeviceServiceState state, string message) =>
        SetStatus(DeviceServiceStatus.Create(state, message));

    private void SetStatus(DeviceServiceStatus status)
    {
        var previous = Interlocked.Exchange(ref _status, status);
        if (previous.State == status.State && previous.Message == status.Message)
        {
            return;
        }

        AppLog.Info($"设备服务状态：{status.State}，{status.Message}", "Status");
        RaiseSafely(StatusChanged, status, "StatusChanged");
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        var cts = _loopCts;
        if (cts is null || cts.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        AppLog.Info("系统从睡眠恢复，排队立即刷新", "Power");
        _ = RefreshAsync(manual: true, cts.Token).ContinueWith(
            task =>
            {
                if (task.Exception is not null)
                {
                    AppLog.Warn(
                        "睡眠恢复刷新失败",
                        "Power",
                        task.Exception.GetBaseException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        AppLog.Info(
            $"设置已更新：刷新间隔 {settings.RefreshIntervalSeconds}s，" +
            $"通知订阅 {settings.UseGattNotifications}",
            "Settings");

        // 间隔变了就打断当前等待：新间隔立即生效，不用等满旧间隔；
        // 正在刷新时没有等待可打断，新间隔从下一次等待开始生效。
        var interval = ClampInterval(settings.RefreshIntervalSeconds);
        if (Interlocked.Exchange(ref _appliedIntervalSeconds, interval) != interval)
        {
            var wasWaiting = _waiter.IsWaiting;
            _waiter.Interrupt();
            AppLog.Info(
                wasWaiting
                    ? $"刷新间隔已改为 {interval}s，当前等待已重置"
                    : $"刷新间隔已改为 {interval}s，下一轮生效",
                "Cycle");
        }

        if (settings.UseGattNotifications)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _probe.ReleaseAllAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Warn("关闭通知订阅时释放失败", "BleProbe", ex);
            }
        });
    }

    private TimeSpan GetNextDelay()
    {
        var interval = CurrentIntervalSeconds();

        // 抖动按间隔的 10% 取，并限制在 1–60 秒：10 秒轮询不会被 ±5 秒的固定抖动拖成 15 秒，
        // 小时级间隔也不会因为抖动太小而和其他实例同步。
        var jitterRange = Math.Clamp((int)Math.Ceiling(interval * 0.1), 1, 60);
        var jitter = Random.Shared.Next(-jitterRange, jitterRange + 1);
        return TimeSpan.FromSeconds(Math.Max(5, interval + jitter));
    }

    private int CurrentIntervalSeconds() =>
        ClampInterval(_settingsService.Current.RefreshIntervalSeconds);

    private static int ClampInterval(int seconds) =>
        Math.Clamp(
            seconds,
            AppSettings.MinRefreshIntervalSeconds,
            AppSettings.MaxRefreshIntervalSeconds);

    private static void RaiseSafely<T>(
        EventHandler<T>? handler,
        T argument,
        string eventName)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList().Cast<EventHandler<T>>())
        {
            try
            {
                subscriber(null, argument);
            }
            catch (Exception ex)
            {
                AppLog.Error($"事件 {eventName} 的订阅者抛出异常", ex, "Event");
            }
        }
    }
}
