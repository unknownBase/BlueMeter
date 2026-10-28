namespace BlueMeter.Models;

/// <summary>
/// 设备电量来源。2.4G 接收器是独立的 HID 传输，不属于 BLE 或经典蓝牙。
/// </summary>
public enum DeviceTransportKind
{
    BluetoothLe,
    BluetoothClassic,
    Hid24G,
}

/// <summary>日志里使用的传输名，例如“BLE”“经典蓝牙”“2.4G”。</summary>
internal static class DeviceTransportKindText
{
    internal static string ToLogText(this DeviceTransportKind kind) =>
        kind switch
        {
            DeviceTransportKind.BluetoothLe => "BLE",
            DeviceTransportKind.BluetoothClassic => "经典蓝牙",
            DeviceTransportKind.Hid24G => "2.4G",
            _ => kind.ToString(),
        };
}
