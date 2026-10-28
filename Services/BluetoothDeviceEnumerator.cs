using System.Globalization;
using BlueMeter.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace BlueMeter.Services;

/// <summary>枚举阶段得到的单条蓝牙设备快照，不包含任何连接对象。</summary>
internal sealed record DeviceCandidate(
    string Id,
    string Name,
    ulong? Address,
    DeviceTransportKind Transport,
    bool IsConnected,
    string? ChargingText,
    int? BatteryPercent,
    bool IsCharging,
    bool? LeConnectable);

/// <summary>同一蓝牙地址下 BLE 与经典蓝牙两条来源的合并结果。</summary>
internal sealed class MergedCandidate
{
    internal MergedCandidate(string key)
    {
        Key = key;
    }

    internal string Key { get; }

    internal DeviceCandidate? Ble { get; set; }

    internal DeviceCandidate? Classic { get; set; }

    internal string Id => Ble?.Id ?? Classic!.Id;

    internal string Name => Ble?.Name ?? Classic!.Name;

    internal ulong? Address => Ble?.Address ?? Classic?.Address;

    internal DeviceTransportKind Transport =>
        Ble is not null ? DeviceTransportKind.BluetoothLe : DeviceTransportKind.BluetoothClassic;

    internal bool IsConnected =>
        (Ble?.IsConnected ?? false) || (Classic?.IsConnected ?? false);

    internal string? ChargingText => Ble?.ChargingText ?? Classic?.ChargingText;
}

/// <summary>单类设备的枚举结果；失败时保留原因，避免把“枚举失败”当成“设备消失”。</summary>
internal sealed record EnumerationResult(
    DeviceTransportKind Transport,
    List<DeviceCandidate> Devices,
    bool Succeeded,
    string? Error);

/// <summary>
/// 已配对 BLE / 经典蓝牙设备的枚举、属性解析与蓝牙适配器状态探测。
/// 只读取系统设备属性，不建立 GATT 连接；GATT 读取由 <see cref="BleBatteryProbe"/> 负责。
/// </summary>
internal sealed class BluetoothDeviceEnumerator
{
    private static readonly TimeSpan EnumerationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BluetoothStatusTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SystemBatteryReadTimeout = TimeSpan.FromSeconds(2);

    internal static readonly string[] DevicePropertyKeys =
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

    internal async Task<EnumerationResult> EnumerateAsync(
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
                $"{kind.ToLogText()} 枚举 {devices.Count} 条，保留 {candidates.Count} 条",
                "Enum");
            return new EnumerationResult(kind, candidates, true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            AppLog.Warn($"{kind.ToLogText()} 枚举超时", "Enum");
            return new EnumerationResult(kind, new List<DeviceCandidate>(), false, "timeout");
        }
        catch (Exception ex)
        {
            AppLog.Warn(
                $"{kind.ToLogText()} 枚举失败：0x{ex.HResult:X8} {ex.Message}",
                "Enum");
            return new EnumerationResult(kind, new List<DeviceCandidate>(), false, ex.Message);
        }
    }

    internal async Task<DeviceServiceStatus> ProbeStatusAsync(CancellationToken cancellationToken)
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

    /// <summary>
    /// 按蓝牙地址去重，同一设备同时出现在 BLE 与经典列表时保留两条来源，读取时优先 BLE。
    /// </summary>
    internal static List<MergedCandidate> Merge(IEnumerable<DeviceCandidate> candidates)
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

    /// <summary>
    /// 重新读取单台设备的系统电量属性（不经枚举缓存）。失败时返回空值，由调用方决定后续兜底。
    /// </summary>
    internal static async Task<(int? Percent, string? ChargingText)> ReadSystemBatteryAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(SystemBatteryReadTimeout);

            var info = await DeviceInformation
                .CreateFromIdAsync(deviceId, DevicePropertyKeys)
                .AsTask(cts.Token)
                .ConfigureAwait(false);

            if (info is null)
            {
                return (null, null);
            }

            var battery = SystemBatteryPropertyReader.Read(info.Properties);
            var chargingText = GetString(info.Properties, "System.Devices.BatteryPlusChargingText");
            return (battery.Percent, string.IsNullOrWhiteSpace(chargingText) ? null : chargingText.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Debug($"重新读取系统电量属性失败：{ex.Message}", "ClassicProbe");
            return (null, null);
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
        var battery = SystemBatteryPropertyReader.Read(properties);
        var leConnectable = GetNullableBoolean(properties, "System.Devices.Aep.Bluetooth.Le.IsConnectable");

        if (kind == DeviceTransportKind.BluetoothClassic)
        {
            // 经典蓝牙没有统一电量 API，属性缺失时要能看到实际拿到的键值。
            AppLog.Debug($"经典蓝牙属性：{name} → {DescribeSystemBattery(properties)}", "Enum");
        }

        return new DeviceCandidate(
            device.Id,
            name,
            address,
            kind,
            isConnected,
            string.IsNullOrWhiteSpace(chargingText) ? null : chargingText.Trim(),
            battery.Percent,
            battery.Charging,
            leConnectable);
    }

    private static string DescribeSystemBattery(IReadOnlyDictionary<string, object> properties)
    {
        var life = properties.TryGetValue(SystemBatteryPropertyReader.BatteryLifeKey, out var rawLife)
            ? rawLife?.ToString() ?? "null"
            : "缺失";
        var combined = properties.TryGetValue(SystemBatteryPropertyReader.BatteryPlusChargingKey, out var rawCombined)
            ? rawCombined?.ToString() ?? "null"
            : "缺失";
        var (percent, charging) = SystemBatteryPropertyReader.Read(properties);

        return $"BatteryLife={life}，BatteryPlusCharging={combined}，" +
               $"解析={percent?.ToString() ?? "无"}{(charging ? "（充电）" : string.Empty)}";
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

    private static bool? GetNullableBoolean(
        IReadOnlyDictionary<string, object> properties,
        string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? GetString(
        IReadOnlyDictionary<string, object> properties,
        string key) =>
        properties.TryGetValue(key, out var value) ? value as string : null;

    private static string RadioStateText(RadioState state) =>
        state switch
        {
            RadioState.On => "开启",
            RadioState.Off => "关闭",
            RadioState.Disabled => "已禁用",
            _ => state.ToString(),
        };
}
