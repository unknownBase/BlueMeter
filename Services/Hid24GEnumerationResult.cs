using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 一次 2.4G HID 枚举结果。枚举失败时保留上次状态，不把设备误判为离线。
/// </summary>
public sealed record Hid24GEnumerationResult(
    IReadOnlyList<Hid24GDeviceInfo> Devices,
    bool Succeeded,
    string? Error,
    int MatchingInterfaceCount = 0,
    int FailedInterfaceCount = 0);
