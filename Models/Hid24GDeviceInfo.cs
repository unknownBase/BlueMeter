namespace BlueMeter.Models;

/// <summary>
/// 已确认的 2.4G HID 接收器设备。
/// 目前只枚举 Rapoo 私有接收器；不把该传输归类为蓝牙。
/// </summary>
public sealed record Hid24GDeviceInfo(
    string Id,
    string Name,
    string? Manufacturer,
    ushort VendorId,
    ushort ProductId,
    string InterfacePath,
    string ReceiverId,
    int InputReportLength);
