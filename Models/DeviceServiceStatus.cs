namespace BlueMeter.Models;

public enum DeviceServiceState
{
    Stopped,
    Starting,
    Ready,
    NoAdapter,
    BluetoothOff,
    Error,
}

/// <summary>
/// 全局设备服务状态。设备级失败仍由 <see cref="BatteryReadStatus"/> 表达。
/// 蓝牙不可用时，2.4G 接收器仍可能处于可用状态。
/// </summary>
public sealed record DeviceServiceStatus(
    DeviceServiceState State,
    string Message,
    DateTimeOffset Timestamp)
{
    public static DeviceServiceStatus Create(DeviceServiceState state, string message) =>
        new(state, message, DateTimeOffset.Now);
}
