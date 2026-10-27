namespace BlueMeter.Models;

/// <summary>
/// 线程安全的设备状态快照。事件订阅者不得修改该对象。
/// </summary>
public sealed record DeviceBatterySnapshot(
    string Id,
    ulong? Address,
    string Name,
    DeviceTransportKind Transport,
    int? BatteryPercent,
    BatteryReadStatus Status,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset LastAttemptAt,
    bool IsConnected,
    bool SupportsNotifications,
    string? ChargingText)
{
    /// <summary>HID 2.4G 设备的 USB 厂商 ID；蓝牙设备为 null。</summary>
    public ushort? VendorId { get; init; }

    /// <summary>HID 2.4G 设备的 USB 产品 ID；蓝牙设备为 null。</summary>
    public ushort? ProductId { get; init; }

    /// <summary>HID 2.4G 接收器的设备接口路径；蓝牙设备为 null。</summary>
    public string? InterfacePath { get; init; }

    /// <summary>HID 2.4G 接收器身份，用于稳定关联设备状态。</summary>
    public string? ReceiverId { get; init; }

    /// <summary>HID 2.4G 接收器厂商名，例如 Rapoo。</summary>
    public string? VendorName { get; init; }
}
