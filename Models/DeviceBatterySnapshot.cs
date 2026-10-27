namespace BlueMeter.Models;

/// <summary>
/// 线程安全的设备状态快照。事件订阅者不得修改该对象。
/// </summary>
public sealed record DeviceBatterySnapshot(
    string Id,
    ulong? Address,
    string Name,
    BluetoothDeviceKind Kind,
    int? BatteryPercent,
    BatteryReadStatus Status,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset LastAttemptAt,
    bool IsConnected,
    bool SupportsNotifications,
    string? ChargingText);
