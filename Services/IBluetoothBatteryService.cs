using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 设备枚举、BLE 电量读取与刷新调度。事件可能在后台线程触发。
/// </summary>
public interface IBluetoothBatteryService : IDisposable
{
    event EventHandler<IReadOnlyList<DeviceBatterySnapshot>>? DevicesUpdated;

    event EventHandler<IReadOnlyList<LowBatteryNotice>>? LowBatteryDetected;

    event EventHandler<BluetoothServiceStatus>? StatusChanged;

    IReadOnlyList<DeviceBatterySnapshot> Current { get; }

    BluetoothServiceStatus Status { get; }

    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 手动刷新。若已有轮次在运行，只排队一次，不并发执行。
    /// </summary>
    Task RefreshAsync(bool manual, CancellationToken cancellationToken);

    Task StopAsync();
}
