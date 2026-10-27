using System.Diagnostics;
using System.Globalization;
using BlueMeter.Models;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace BlueMeter.Services;

/// <summary>
/// 已配对蓝牙设备与 Rapoo / LINGBAO K98 2.4G 接收器枚举、电量读取、状态合并与刷新调度。
/// 所有 GATT 操作串行执行；手动刷新只排队一次，避免并发访问蓝牙栈。
/// </summary>
public sealed class DeviceBatteryService : IDeviceBatteryService
{
    private static readonly TimeSpan PerDeviceTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CycleBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EnumerationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BluetoothStatusTimeout = TimeSpan.FromSeconds(5);

    private static readonly string[] DevicePropertyKeys =
    {
        "System.Devices.Aep.DeviceAddress",
        "System.Devices.Aep.IsConnected",
        "System.Devices.Aep.Bluetooth.Le.IsConnectable",
        "System.Devices.Aep.Category",
        "System.Devices.BatteryLife",
        "System.Devices.BatteryPlusCharging",
        "System.Devices.BatteryPlusChargingText",
        "System.Devices.Manufacturer",
        "System.Devices.ModelName",
    };

    private readonly SettingsService _settingsService;
    private readonly BleBatteryProbe _probe = new();
    private readonly Hid24GDeviceEnumerator _hid24GEnumerator = new();
    private readonly Hid24GProbe _hid24GProbe = new();
    private readonly object _stateLock = new();
    private readonly Dictionary<string, DeviceEntry> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _cycleGate = new(1, 1);

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private DeviceServiceStatus _status = DeviceServiceStatus.Create(
        DeviceServiceState.Stopped,
        "尚未启动");
    private int _cycleCount;
    private int _refreshQueued;
    private int _started;
    private int _disposed;

    public DeviceBatteryService(SettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public event EventHandler<IReadOnlyList<DeviceBatterySnapshot>>? DevicesUpdated;

    public event EventHandler<IReadOnlyList<LowBatteryNotice>>? LowBatteryDetected;

    public event EventHandler<DeviceServiceStatus>? StatusChanged;

    public IReadOnlyList<DeviceBatterySnapshot> Current
    {
        get
        {
            lock (_stateLock)
            {
                return BuildSnapshotsLocked();
            }
        }
    }

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

                var delay = GetNextDelay();
                try
                {
                    using var timer = new PeriodicTimer(delay);
                    await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
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

        var bluetoothStatus = await ProbeBluetoothStatusAsync(cancellationToken).ConfigureAwait(false);
        var bluetoothUsable = bluetoothStatus.State is not (
            DeviceServiceState.NoAdapter or DeviceServiceState.BluetoothOff);
        var bluetoothEnumerationSucceeded = false;
        var bleEnumerationSucceeded = false;

        if (!bluetoothUsable)
        {
            MarkBleDevicesUnavailable();
            await ReleaseAllSubscriptionsAsync(bluetoothStatus.Message).ConfigureAwait(false);
        }
        else
        {
            var bleTask = EnumerateAsync(DeviceTransportKind.BluetoothLe, cancellationToken);
            var classicTask = EnumerateAsync(DeviceTransportKind.BluetoothClassic, cancellationToken);
            var bleResult = await bleTask.ConfigureAwait(false);
            var classicResult = await classicTask.ConfigureAwait(false);

            bleEnumerationSucceeded = bleResult.Succeeded;
            bluetoothEnumerationSucceeded = bleResult.Succeeded || classicResult.Succeeded;
            if (bluetoothEnumerationSucceeded)
            {
                var candidates = new List<DeviceCandidate>(
                    bleResult.Devices.Count + classicResult.Devices.Count);
                candidates.AddRange(bleResult.Devices);
                candidates.AddRange(classicResult.Devices);

                var merged = MergeCandidates(candidates);
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in merged)
                {
                    seenKeys.Add(candidate.Key);
                    UpsertDevice(candidate);
                }

                var removed = RemoveMissingBluetoothDevices(
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
                seenHidKeys.Add(BuildHidKey(device));
                UpsertHidDevice(device);
            }

            if (hidResult.FailedInterfaceCount > 0)
            {
                // 接口存在但暂时无法只读打开时，保留旧条目并标为不可达，
                // 不要把它误判成“接收器已拔出”。
                MarkHidDevicesMissing(seenHidKeys);
            }
            else
            {
                foreach (var removedEntry in RemoveMissingHidDevices(seenHidKeys))
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
            ? GetReadTargets()
            : new List<DeviceEntry>();
        var hidReadTargets = hidResult.Succeeded
            ? GetHid24GReadTargets()
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

            ApplyProbeResult(entry, result, notices);
            PublishSnapshots();

            if (!result.IsSuccess)
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

            ApplyProbeResult(entry, result, notices);
            PublishSnapshots();

            if (!result.IsSuccess)
            {
                LogProbeFailure(entry, result);
            }
        }

        // 队列中的手动刷新可能在读取期间到达，当前轮结束后由外层循环立即补跑。
        PublishSnapshots();
        RaiseLowBatteryDetected(notices);

        AppLog.Info(
            $"第 {cycleNumber} 轮完成：{Current.Count} 台设备，" +
            $"{readTargets.Count} 台 BLE，{hidReadTargets.Count} 台 2.4G，" +
            $"{successCount} 成功，{failureCount} 失败，" +
            $"{skippedCount} 跳过，用时 {stopwatch.Elapsed.TotalSeconds:0.0} 秒",
            "Cycle");
    }

    private async Task<EnumerationResult> EnumerateAsync(
        DeviceTransportKind kind,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(EnumerationTimeout);

        try
        {
            var selector = kind == DeviceTransportKind.BluetoothLe
                ? BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)
                : BluetoothDevice.GetDeviceSelectorFromPairingState(true);

            var devices = await DeviceInformation
                .FindAllAsync(selector, DevicePropertyKeys)
                .AsTask(cts.Token)
                .ConfigureAwait(false);

            var candidates = new List<DeviceCandidate>(devices.Count);
            foreach (var device in devices)
            {
                var candidate = CreateCandidate(device, kind);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            AppLog.Debug(
                $"{KindText(kind)} 枚举 {devices.Count} 条，保留 {candidates.Count} 条",
                "Enum");
            return new EnumerationResult(kind, candidates, true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            AppLog.Warn($"{KindText(kind)} 枚举超时", "Enum");
            return new EnumerationResult(kind, new List<DeviceCandidate>(), false, "timeout");
        }
        catch (Exception ex)
        {
            AppLog.Warn(
                $"{KindText(kind)} 枚举失败：0x{ex.HResult:X8} {ex.Message}",
                "Enum");
            return new EnumerationResult(kind, new List<DeviceCandidate>(), false, ex.Message);
        }
    }

    private async Task<DeviceServiceStatus> ProbeBluetoothStatusAsync(
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(BluetoothStatusTimeout);

        try
        {
            var adapter = await BluetoothAdapter
                .GetDefaultAsync()
                .AsTask(cts.Token)
                .ConfigureAwait(false);

            if (adapter is null)
            {
                return await ProbeRadioFallbackAsync(cts, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var radio = await adapter
                    .GetRadioAsync()
                    .AsTask(cts.Token)
                    .ConfigureAwait(false);

                if (radio is not null && radio.State != RadioState.On)
                {
                    return DeviceServiceStatus.Create(
                        DeviceServiceState.BluetoothOff,
                        $"蓝牙未开启（{RadioStateText(radio.State)}）");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 无线电状态读取失败时仍尝试 GATT，避免误判。
                AppLog.Debug($"读取蓝牙无线电状态失败：{ex.Message}", "Bluetooth");
            }

            return DeviceServiceStatus.Create(DeviceServiceState.Ready, "蓝牙可用");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return DeviceServiceStatus.Create(
                DeviceServiceState.Error,
                "读取蓝牙状态超时");
        }
        catch (Exception ex)
        {
            return DeviceServiceStatus.Create(
                DeviceServiceState.Error,
                $"读取蓝牙状态失败：0x{ex.HResult:X8}");
        }
    }

    private static async Task<DeviceServiceStatus> ProbeRadioFallbackAsync(
        CancellationTokenSource cts,
        CancellationToken cancellationToken)
    {
        try
        {
            var radios = await Radio
                .GetRadiosAsync()
                .AsTask(cts.Token)
                .ConfigureAwait(false);

            var bluetoothRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.Bluetooth);
            if (bluetoothRadio is not null && bluetoothRadio.State != RadioState.On)
            {
                return DeviceServiceStatus.Create(
                    DeviceServiceState.BluetoothOff,
                    $"蓝牙未开启（{RadioStateText(bluetoothRadio.State)}）");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 无线电查询失败时保持“无适配器”结论。
        }

        return DeviceServiceStatus.Create(
            DeviceServiceState.NoAdapter,
            "未检测到蓝牙适配器");
    }

    private static List<MergedCandidate> MergeCandidates(IEnumerable<DeviceCandidate> candidates)
    {
        var groups = new Dictionary<string, MergedCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var key = candidate.Address.HasValue
                ? $"a:{candidate.Address.Value:X12}"
                : $"i:{candidate.Id}";

            if (!groups.TryGetValue(key, out var group))
            {
                group = new MergedCandidate(key);
                groups[key] = group;
            }

            if (candidate.Transport == DeviceTransportKind.BluetoothLe)
            {
                group.Ble ??= candidate;
            }
            else
            {
                group.Classic ??= candidate;
            }
        }

        return groups.Values.ToList();
    }

    private static DeviceCandidate? CreateCandidate(
        DeviceInformation device,
        DeviceTransportKind kind)
    {
        if (string.IsNullOrWhiteSpace(device.Id))
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(device.Name)
            ? "未知设备"
            : device.Name.Trim();

        if (IsClearlyNotDevice(name))
        {
            AppLog.Debug($"过滤非设备条目：{name}", "Enum");
            return null;
        }

        var properties = device.Properties;
        var address = TryGetAddress(properties);
        var isConnected = GetBoolean(properties, "System.Devices.Aep.IsConnected");
        var chargingText = GetString(properties, "System.Devices.BatteryPlusChargingText");

        return new DeviceCandidate(
            device.Id,
            name,
            address,
            kind,
            isConnected,
            string.IsNullOrWhiteSpace(chargingText) ? null : chargingText.Trim());
    }

    private static bool IsClearlyNotDevice(string name) =>
        name.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Bluetooth LE Device", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Microsoft Bluetooth Enumerator", StringComparison.OrdinalIgnoreCase);

    private static ulong? TryGetAddress(IReadOnlyDictionary<string, object> properties)
    {
        if (!properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var value) ||
            value is null)
        {
            return null;
        }

        switch (value)
        {
            case ulong address:
                return address;
            case long signedAddress when signedAddress >= 0:
                return (ulong)signedAddress;
            case string text:
            {
                var trimmed = text.Trim();
                var hex = trimmed.Replace(":", string.Empty).Replace("-", string.Empty);
                if (ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }

                if (ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }

                break;
            }
        }

        try
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static bool GetBoolean(
        IReadOnlyDictionary<string, object> properties,
        string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
        {
            return false;
        }

        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false,
        };
    }

    private static string? GetString(
        IReadOnlyDictionary<string, object> properties,
        string key) =>
        properties.TryGetValue(key, out var value) ? value as string : null;

    private void UpsertDevice(MergedCandidate candidate)
    {
        lock (_stateLock)
        {
            if (!_devices.TryGetValue(candidate.Key, out var entry))
            {
                entry = new DeviceEntry(candidate.Key);
                _devices[candidate.Key] = entry;
            }

            entry.State.Id = candidate.Id;
            entry.State.Address = candidate.Address;
            entry.State.Name = candidate.Name;
            entry.State.Transport = candidate.Transport;
            entry.State.IsConnected = candidate.IsConnected;
            entry.State.ChargingText = candidate.ChargingText;
            entry.State.VendorId = null;
            entry.State.ProductId = null;
            entry.State.InterfacePath = null;
            entry.State.ReceiverId = null;
            entry.State.VendorName = null;
            entry.BleSource = candidate.Ble;
            entry.ClassicSource = candidate.Classic;
            entry.HidSource = null;
            entry.MissedCycles = 0;

            if (candidate.Ble is null)
            {
                entry.State.SupportsNotifications = false;
            }
        }
    }

    private List<DeviceEntry> RemoveMissingBluetoothDevices(
        HashSet<string> seenKeys,
        bool bothSourcesEnumerated)
    {
        if (!bothSourcesEnumerated)
        {
            return new List<DeviceEntry>();
        }

        var removed = new List<DeviceEntry>();
        lock (_stateLock)
        {
            foreach (var pair in _devices.ToList())
            {
                // 2.4G 设备由独立的枚举成功标志和 seen 集合管理。
                if (pair.Value.HidSource is not null || seenKeys.Contains(pair.Key))
                {
                    continue;
                }

                pair.Value.MissedCycles++;
                if (pair.Value.MissedCycles > 1)
                {
                    _devices.Remove(pair.Key);
                    removed.Add(pair.Value);
                }
            }
        }

        return removed;
    }

    private List<DeviceEntry> GetReadTargets()
    {
        lock (_stateLock)
        {
            return _devices.Values
                .Where(entry => entry.BleSource is not null)
                .OrderByDescending(entry => entry.State.IsConnected)
                .ThenBy(entry => entry.State.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    private List<DeviceEntry> GetHid24GReadTargets()
    {
        lock (_stateLock)
        {
            return _devices.Values
                .Where(entry => entry.HidSource is not null)
                .OrderBy(entry => entry.State.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    private void UpsertHidDevice(Hid24GDeviceInfo device)
    {
        var key = BuildHidKey(device);
        lock (_stateLock)
        {
            if (!_devices.TryGetValue(key, out var entry))
            {
                entry = new DeviceEntry(key);
                _devices[key] = entry;
            }

            entry.State.Id = device.Id;
            entry.State.Address = null;
            entry.State.Name = device.Name;
            entry.State.Transport = DeviceTransportKind.Hid24G;
            entry.State.IsConnected = true;
            entry.State.ChargingText = null;
            entry.State.SupportsNotifications = false;
            entry.State.VendorId = device.VendorId;
            entry.State.ProductId = device.ProductId;
            entry.State.InterfacePath = device.InterfacePath;
            entry.State.ReceiverId = device.ReceiverId;
            entry.State.VendorName = device.Manufacturer;
            entry.BleSource = null;
            entry.ClassicSource = null;
            entry.HidSource = device;
            entry.MissedCycles = 0;
        }
    }

    private List<DeviceEntry> RemoveMissingHidDevices(HashSet<string> seenKeys)
    {
        var removed = new List<DeviceEntry>();
        lock (_stateLock)
        {
            foreach (var pair in _devices.ToList())
            {
                if (pair.Value.HidSource is null || seenKeys.Contains(pair.Key))
                {
                    continue;
                }

                pair.Value.MissedCycles++;
                if (pair.Value.MissedCycles > 1)
                {
                    _devices.Remove(pair.Key);
                    removed.Add(pair.Value);
                }
            }
        }

        return removed;
    }

    private void MarkHidDevicesMissing(HashSet<string> seenKeys)
    {
        lock (_stateLock)
        {
            foreach (var pair in _devices)
            {
                if (pair.Value.HidSource is null || seenKeys.Contains(pair.Key))
                {
                    continue;
                }

                pair.Value.State.Status = pair.Value.State.BatteryPercent.HasValue
                    ? BatteryReadStatus.Stale
                    : BatteryReadStatus.Unavailable;
            }
        }
    }

    private static string BuildHidKey(Hid24GDeviceInfo device) => $"hid:{device.Id}";

    private void MarkReading(DeviceEntry entry)
    {
        lock (_stateLock)
        {
            entry.State.Status = BatteryReadStatus.Reading;
            entry.State.LastAttemptAt = DateTimeOffset.Now;
        }

        PublishSnapshots();
    }

    private void ApplyProbeResult(
        DeviceEntry entry,
        BatteryProbeResult result,
        ICollection<LowBatteryNotice> notices)
    {
        var now = DateTimeOffset.Now;
        lock (_stateLock)
        {
            entry.State.LastAttemptAt = now;
            entry.State.SupportsNotifications = result.SupportsNotifications;

            if (entry.State.Transport == DeviceTransportKind.Hid24G)
            {
                // 2.4G 探针每次返回完整充电状态；不充电时必须清掉上一次的“充电中”。
                entry.State.ChargingText = result.ChargingText;
            }
            else if (!string.IsNullOrWhiteSpace(result.ChargingText))
            {
                entry.State.ChargingText = result.ChargingText;
            }

            switch (result.Status)
            {
                case BatteryReadStatus.Ok when result.BatteryPercent.HasValue:
                    ApplySuccessLocked(entry, result.BatteryPercent.Value, now, notices);
                    break;
                case BatteryReadStatus.NotSupported:
                    entry.State.Status = BatteryReadStatus.NotSupported;
                    entry.State.BatteryPercent = null;
                    entry.State.LastSuccessAt = null;
                    break;
                default:
                    entry.State.Status = entry.State.BatteryPercent.HasValue
                        ? BatteryReadStatus.Stale
                        : BatteryReadStatus.Unavailable;
                    break;
            }
        }
    }

    private void ApplySuccessLocked(
        DeviceEntry entry,
        int batteryPercent,
        DateTimeOffset timestamp,
        ICollection<LowBatteryNotice> notices)
    {
        entry.State.BatteryPercent = batteryPercent;
        entry.State.LastSuccessAt = timestamp;
        entry.State.Status = BatteryReadStatus.Ok;

        var threshold = _settingsService.Current.LowBatteryThreshold;
        if (batteryPercent <= threshold && !entry.State.LowBatteryAlerted)
        {
            entry.State.LowBatteryAlerted = true;
            notices.Add(new LowBatteryNotice(
                entry.State.Id,
                entry.State.Name,
                batteryPercent));
        }
        else if (batteryPercent >= threshold + 5)
        {
            entry.State.LowBatteryAlerted = false;
        }
    }

    private void MarkBleDevicesUnavailable()
    {
        lock (_stateLock)
        {
            foreach (var entry in _devices.Values)
            {
                if (entry.State.Transport != DeviceTransportKind.BluetoothLe)
                {
                    continue;
                }

                entry.State.Status = entry.State.BatteryPercent.HasValue
                    ? BatteryReadStatus.Stale
                    : BatteryReadStatus.Unavailable;
            }
        }
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

        lock (_stateLock)
        {
            foreach (var entry in _devices.Values)
            {
                entry.State.SupportsNotifications = false;
            }
        }

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
        var updated = false;

        lock (_stateLock)
        {
            var entry = _devices.Values.FirstOrDefault(
                candidate => string.Equals(
                    candidate.BleSource?.Id,
                    update.DeviceId,
                    StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                return;
            }

            ApplySuccessLocked(entry, update.BatteryPercent, update.Timestamp, notices);
            entry.State.SupportsNotifications = true;
            updated = true;
        }

        if (updated)
        {
            PublishSnapshots();
            RaiseLowBatteryDetected(notices);
        }
    }

    private void LogProbeFailure(DeviceEntry entry, BatteryProbeResult result)
    {
        var message =
            $"{entry.State.Name}（{KindText(entry.State.Transport)}）电量读取失败：" +
            $"{result.FailureStage} {result.FailureMessage}" +
            (result.HResult.HasValue ? $" HRESULT=0x{result.HResult.Value:X8}" : string.Empty);

        var category = entry.State.Transport == DeviceTransportKind.Hid24G
            ? "Hid24G"
            : "BleProbe";

        if (result.Status == BatteryReadStatus.NotSupported)
        {
            AppLog.Debug(message, category);
        }
        else
        {
            AppLog.Warn(message, category);
        }
    }

    private void PublishSnapshots()
    {
        IReadOnlyList<DeviceBatterySnapshot> snapshots;
        lock (_stateLock)
        {
            snapshots = BuildSnapshotsLocked();
        }

        RaiseSafely(DevicesUpdated, snapshots, "DevicesUpdated");
    }

    private IReadOnlyList<DeviceBatterySnapshot> BuildSnapshotsLocked() =>
        _devices.Values
            .Select(entry => new DeviceBatterySnapshot(
                entry.State.Id,
                entry.State.Address,
                entry.State.Name,
                entry.State.Transport,
                entry.State.BatteryPercent,
                entry.State.Status,
                entry.State.LastSuccessAt,
                entry.State.LastAttemptAt,
                entry.State.IsConnected,
                entry.State.SupportsNotifications,
                entry.State.ChargingText)
            {
                VendorId = entry.State.VendorId,
                ProductId = entry.State.ProductId,
                InterfacePath = entry.State.InterfacePath,
                ReceiverId = entry.State.ReceiverId,
                VendorName = entry.State.VendorName,
            })
            .OrderBy(snapshot => snapshot.Status is BatteryReadStatus.Stale or BatteryReadStatus.Unavailable)
            .ThenBy(snapshot => snapshot.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

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
        var interval = Math.Clamp(
            _settingsService.Current.RefreshIntervalSeconds,
            AppSettings.MinRefreshIntervalSeconds,
            AppSettings.MaxRefreshIntervalSeconds);
        var jitter = Random.Shared.Next(-5, 6);
        return TimeSpan.FromSeconds(Math.Max(5, interval + jitter));
    }

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

    private static string KindText(DeviceTransportKind kind) =>
        kind switch
        {
            DeviceTransportKind.BluetoothLe => "BLE",
            DeviceTransportKind.BluetoothClassic => "经典蓝牙",
            DeviceTransportKind.Hid24G => "2.4G",
            _ => kind.ToString(),
        };

    private static string RadioStateText(RadioState state) =>
        state switch
        {
            RadioState.On => "开启",
            RadioState.Off => "关闭",
            RadioState.Disabled => "已禁用",
            _ => state.ToString(),
        };

    private sealed record DeviceCandidate(
        string Id,
        string Name,
        ulong? Address,
        DeviceTransportKind Transport,
        bool IsConnected,
        string? ChargingText);

    private sealed class MergedCandidate
    {
        public MergedCandidate(string key)
        {
            Key = key;
        }

        public string Key { get; }

        public DeviceCandidate? Ble { get; set; }

        public DeviceCandidate? Classic { get; set; }

        public string Id => Ble?.Id ?? Classic!.Id;

        public string Name => Ble?.Name ?? Classic!.Name;

        public ulong? Address => Ble?.Address ?? Classic?.Address;

        public DeviceTransportKind Transport =>
            Ble is not null ? DeviceTransportKind.BluetoothLe : DeviceTransportKind.BluetoothClassic;

        public bool IsConnected =>
            (Ble?.IsConnected ?? false) || (Classic?.IsConnected ?? false);

        public string? ChargingText => Ble?.ChargingText ?? Classic?.ChargingText;
    }

    private sealed class DeviceEntry
    {
        public DeviceEntry(string key)
        {
            Key = key;
            State = new DeviceBatteryState
            {
                Id = string.Empty,
                Name = "未知设备",
            };
        }

        public string Key { get; }

        public DeviceBatteryState State { get; }

        public DeviceCandidate? BleSource { get; set; }

        public DeviceCandidate? ClassicSource { get; set; }

        public Hid24GDeviceInfo? HidSource { get; set; }

        public int MissedCycles { get; set; }
    }

    private sealed record EnumerationResult(
        DeviceTransportKind Transport,
        List<DeviceCandidate> Devices,
        bool Succeeded,
        string? Error);
}
