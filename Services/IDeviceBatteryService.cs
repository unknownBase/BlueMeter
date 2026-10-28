using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 设备枚举、蓝牙与 2.4G 电量读取及刷新调度。事件可能在后台线程触发。
/// </summary>
public interface IDeviceBatteryService : IDisposable
{
    event EventHandler<IReadOnlyList<DeviceBatterySnapshot>>? DevicesUpdated;

    event EventHandler<IReadOnlyList<LowBatteryNotice>>? LowBatteryDetected;

    /// <summary>
    /// 全局状态。<see cref="DeviceServiceState.Error"/> 表示整轮扫描失败，
    /// 界面据此显示非阻塞提示；下一轮状态恢复后同事件会自动转回 Ready。
    /// </summary>
    event EventHandler<DeviceServiceStatus>? StatusChanged;

    IReadOnlyList<DeviceBatterySnapshot> Current { get; }

    DeviceServiceStatus Status { get; }

    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 手动刷新。若已有轮次在运行，只排队一次，不并发执行。
    /// </summary>
    Task RefreshAsync(bool manual, CancellationToken cancellationToken);

    Task StopAsync();
}
