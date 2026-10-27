namespace BlueMeter.Models;

/// <summary>
/// 单台设备当前电量的可用状态。
/// </summary>
public enum BatteryReadStatus
{
    Unknown,
    Reading,
    Ok,
    Stale,
    Unavailable,
    NotSupported,
}
