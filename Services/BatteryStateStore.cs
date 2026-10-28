using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 单台设备的可变状态与读取来源。字段只在状态仓库的锁内修改。
/// </summary>
internal sealed class DeviceEntry
{
    internal DeviceEntry(string key)
    {
        Key = key;
        State = new DeviceBatteryState
        {
            Id = string.Empty,
            Name = "未知设备",
        };
    }

    internal string Key { get; }

    internal DeviceBatteryState State { get; }

    internal DeviceCandidate? BleSource { get; set; }

    internal DeviceCandidate? ClassicSource { get; set; }

    internal Hid24GDeviceInfo? HidSource { get; set; }

    internal int MissedCycles { get; set; }

    /// <summary>2.4G 读取的连续失败与累计统计；日志降噪与轮次失败率都用它。</summary>
    internal ProbeHealthTracker Health { get; } = new();
}

/// <summary>
/// 设备状态仓库：合并枚举结果、应用探针结果、生成对外快照。
/// 所有成员自行加锁，调用方不需要持有锁；事件发布由服务层负责。
/// </summary>
internal sealed class BatteryStateStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DeviceEntry> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<int> _lowBatteryThreshold;

    internal BatteryStateStore(Func<int> lowBatteryThreshold)
    {
        _lowBatteryThreshold = lowBatteryThreshold ??
            throw new ArgumentNullException(nameof(lowBatteryThreshold));
    }

    internal int Count
    {
        get
        {
            lock (_lock)
            {
                return _devices.Count;
            }
        }
    }

    internal IReadOnlyList<DeviceBatterySnapshot> Snapshot()
    {
        lock (_lock)
        {
            return BuildSnapshotsLocked();
        }
    }

    internal static string BuildHidKey(Hid24GDeviceInfo device) => $"hid:{device.Id}";

    internal void UpsertDevice(MergedCandidate candidate)
    {
        lock (_lock)
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

    internal void UpsertHidDevice(Hid24GDeviceInfo device)
    {
        var key = BuildHidKey(device);
        lock (_lock)
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

    /// <summary>连续两轮未出现的蓝牙设备才移除，避免枚举抖动导致列表闪烁。</summary>
    internal List<DeviceEntry> RemoveMissingBluetoothDevices(
        HashSet<string> seenKeys,
        bool bothSourcesEnumerated)
    {
        if (!bothSourcesEnumerated)
        {
            return new List<DeviceEntry>();
        }

        var removed = new List<DeviceEntry>();
        lock (_lock)
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

    internal List<DeviceEntry> RemoveMissingHidDevices(HashSet<string> seenKeys)
    {
        var removed = new List<DeviceEntry>();
        lock (_lock)
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

    /// <summary>
    /// 接口存在但暂时打不开时保留旧条目并标为不可达，不要误判成“接收器已拔出”。
    /// </summary>
    internal void MarkHidDevicesMissing(HashSet<string> seenKeys)
    {
        lock (_lock)
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

    internal void MarkBluetoothDevicesUnavailable()
    {
        lock (_lock)
        {
            foreach (var entry in _devices.Values)
            {
                if (entry.State.Transport == DeviceTransportKind.Hid24G)
                {
                    continue;
                }

                entry.State.Status = entry.State.BatteryPercent.HasValue
                    ? BatteryReadStatus.Stale
                    : BatteryReadStatus.Unavailable;
            }
        }
    }

    internal void ClearNotificationSupport()
    {
        lock (_lock)
        {
            foreach (var entry in _devices.Values)
            {
                entry.State.SupportsNotifications = false;
            }
        }
    }

    internal List<DeviceEntry> GetBleReadTargets()
    {
        lock (_lock)
        {
            return _devices.Values
                .Where(entry => entry.BleSource is not null)
                .OrderByDescending(entry => entry.State.IsConnected)
                .ThenBy(entry => entry.State.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// 只出现在经典蓝牙列表、且没有 BLE 来源的设备：它们只能靠系统属性读电量。
    /// </summary>
    internal List<DeviceEntry> GetClassicReadTargets()
    {
        lock (_lock)
        {
            return _devices.Values
                .Where(entry => entry.ClassicSource is not null && entry.BleSource is null)
                .OrderByDescending(entry => entry.State.IsConnected)
                .ThenBy(entry => entry.State.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    internal List<DeviceEntry> GetHid24GReadTargets()
    {
        lock (_lock)
        {
            return _devices.Values
                .Where(entry => entry.HidSource is not null)
                .OrderBy(entry => entry.State.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    internal void MarkReading(DeviceEntry entry)
    {
        lock (_lock)
        {
            entry.State.Status = BatteryReadStatus.Reading;
            entry.State.LastAttemptAt = DateTimeOffset.Now;
        }
    }

    internal void ApplyProbeResult(
        DeviceEntry entry,
        BatteryProbeResult result,
        ICollection<LowBatteryNotice> notices)
    {
        var now = DateTimeOffset.Now;
        lock (_lock)
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
                case BatteryReadStatus.Stale when result.BatteryPercent.HasValue:
                    // 经典蓝牙离线时的系统缓存值：保留读数，但不刷新 LastSuccessAt。
                    entry.State.BatteryPercent = result.BatteryPercent.Value;
                    entry.State.Status = BatteryReadStatus.Stale;
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

    /// <summary>
    /// 应用 BLE 通知推送的电量。找不到设备时返回 false，由调用方跳过发布。
    /// </summary>
    internal bool TryApplyBleUpdate(
        string deviceId,
        int batteryPercent,
        DateTimeOffset timestamp,
        ICollection<LowBatteryNotice> notices)
    {
        lock (_lock)
        {
            var entry = _devices.Values.FirstOrDefault(
                candidate => string.Equals(
                    candidate.BleSource?.Id,
                    deviceId,
                    StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                return false;
            }

            ApplySuccessLocked(entry, batteryPercent, timestamp, notices);
            entry.State.SupportsNotifications = true;
            return true;
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

        var threshold = _lowBatteryThreshold();
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
                Protocol = entry.HidSource?.Protocol ?? Hid24GProtocol.Unknown,
            })
            .OrderBy(snapshot => snapshot.Status is BatteryReadStatus.Stale or BatteryReadStatus.Unavailable)
            .ThenBy(snapshot => snapshot.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
