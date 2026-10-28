using BlueMeter.Models;

namespace BlueMeter.ViewModels;

/// <summary>列表投影结果：可见行与因设置被隐藏的数量。</summary>
public sealed record DeviceListProjection(
    IReadOnlyList<DeviceItemViewModel> Items,
    int HiddenUnsupportedCount);

/// <summary>排序后的可见快照与隐藏数量，供行集合原地同步使用。</summary>
public sealed record DeviceListOrder(
    IReadOnlyList<DeviceBatterySnapshot> Visible,
    int HiddenUnsupportedCount);

/// <summary>
/// 把设备快照投影成列表：低电量 → 警告区 → 正常 → 暂时不可达 → 其余，
/// 同组内按电量升序、再按名称，并按 <see cref="AppSettings.ShowUnsupportedDevices"/>
/// 决定是否隐藏明确不支持电量读取的设备。
/// </summary>
public static class DeviceListProjector
{
    public static DeviceListProjection Create(
        IReadOnlyList<DeviceBatterySnapshot> snapshots,
        int lowBatteryThreshold,
        bool showUnsupportedDevices)
    {
        var order = Order(snapshots, lowBatteryThreshold, showUnsupportedDevices);

        var items = order.Visible
            .Select(snapshot => new DeviceItemViewModel(snapshot, lowBatteryThreshold))
            .ToList();

        return new DeviceListProjection(items, order.HiddenUnsupportedCount);
    }

    /// <summary>只算顺序，不建行对象；<see cref="MainViewModel"/> 用它原地同步已有行。</summary>
    public static DeviceListOrder Order(
        IReadOnlyList<DeviceBatterySnapshot> snapshots,
        int lowBatteryThreshold,
        bool showUnsupportedDevices)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var visible = showUnsupportedDevices
            ? snapshots
            : snapshots.Where(snapshot => !IsUnsupported(snapshot)).ToList();

        var ordered = visible
            .OrderBy(snapshot => SortRank(snapshot, lowBatteryThreshold))
            .ThenBy(snapshot => snapshot.BatteryPercent ?? int.MaxValue)
            .ThenBy(snapshot => snapshot.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new DeviceListOrder(ordered, snapshots.Count - visible.Count);
    }

    /// <summary>“不支持”只包含设备明确回复没有电池服务的场景；暂时不可达仍保留在列表里。</summary>
    public static bool IsUnsupported(DeviceBatterySnapshot snapshot) =>
        snapshot.Status == BatteryReadStatus.NotSupported;

    private static int SortRank(DeviceBatterySnapshot snapshot, int lowBatteryThreshold)
    {
        if (snapshot.BatteryPercent is int percent)
        {
            if (percent <= lowBatteryThreshold)
            {
                return 0;
            }

            return percent < lowBatteryThreshold + BatteryLevelCalculator.CautionBandWidth ? 1 : 2;
        }

        return snapshot.Status is BatteryReadStatus.Stale or BatteryReadStatus.Unavailable ? 3 : 4;
    }
}
