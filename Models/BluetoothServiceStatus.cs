namespace BlueMeter.Models;

public enum BluetoothServiceState
{
    Stopped,
    Starting,
    Ready,
    NoAdapter,
    BluetoothOff,
    Error,
}

/// <summary>
/// 全局蓝牙状态。设备级失败仍由 <see cref="BatteryReadStatus"/> 表达。
/// </summary>
public sealed record BluetoothServiceStatus(
    BluetoothServiceState State,
    string Message,
    DateTimeOffset Timestamp)
{
    public static BluetoothServiceStatus Create(BluetoothServiceState state, string message) =>
        new(state, message, DateTimeOffset.Now);
}
