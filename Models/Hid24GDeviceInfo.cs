namespace BlueMeter.Models;

/// <summary>
/// 2.4G HID 接收器使用的厂商协议。不同厂商的命令与报告格式彼此独立，
/// 不能根据 VID/PID 之外的信息互相推断。
/// </summary>
public enum Hid24GProtocol
{
    Unknown = 0,
    Rapoo = 1,
    LingbaoK98 = 2,
}

/// <summary>
/// 已确认的 2.4G HID 接收器设备。
/// 目前只枚举 Rapoo 与 LINGBAO K98 的已确认接口；不把该传输归类为蓝牙。
/// </summary>
public sealed record Hid24GDeviceInfo(
    string Id,
    string Name,
    string? Manufacturer,
    ushort VendorId,
    ushort ProductId,
    string InterfacePath,
    string ReceiverId,
    int InputReportLength)
{
    /// <summary>该接口所属的厂商协议；未知协议不会尝试读取。</summary>
    public Hid24GProtocol Protocol { get; init; } = Hid24GProtocol.Unknown;
}
