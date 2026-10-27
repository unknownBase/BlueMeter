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
