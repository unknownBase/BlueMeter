namespace BlueMeter.Models;

/// <summary>
/// M3 将消费该事件并合并为托盘气泡；M2 先负责去重与触发。
/// </summary>
public sealed record LowBatteryNotice(
    string DeviceId,
    string DeviceName,
    int BatteryPercent);
