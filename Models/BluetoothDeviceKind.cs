namespace BlueMeter.Models;

/// <summary>
/// 设备来源。BLE 与经典蓝牙可能指向同一物理设备，服务层会按地址合并。
/// </summary>
public enum BluetoothDeviceKind
{
    Ble,
    Classic,
}
