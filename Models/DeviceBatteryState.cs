namespace BlueMeter.Models;

/// <summary>
/// 服务层持有的可变设备状态。对外只暴露 <see cref="DeviceBatterySnapshot"/>。
/// </summary>
public sealed class DeviceBatteryState
{
    public required string Id { get; set; }

    public ulong? Address { get; set; }

    public required string Name { get; set; }

    public BluetoothDeviceKind Kind { get; set; }

    public int? BatteryPercent { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset LastAttemptAt { get; set; }

    public BatteryReadStatus Status { get; set; } = BatteryReadStatus.Unknown;

    public bool IsConnected { get; set; }

    public bool SupportsNotifications { get; set; }

    public string? ChargingText { get; set; }

    /// <summary>
    /// 本次放电周期是否已经触发过低电量事件；读数回升到阈值 + 5 以上后重新武装。
    /// </summary>
    public bool LowBatteryAlerted { get; set; }
}
