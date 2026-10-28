using BlueMeter.Models;

namespace BlueMeter.ViewModels;

/// <summary>列表投影结果：可见行与因设置被隐藏的数量。</summary>
public sealed record DeviceListProjection(
    IReadOnlyList<DeviceItemViewModel> Items,
    int HiddenUnsupportedCount);

/// <summary>
/// 把设备快照投影成列表行：低电量优先，其次有读数、暂时不可达、其余，
/// 并按 <see cref="AppSettings.ShowUnsupportedDevices"/> 决定是否隐藏明确不支持电量读取的设备。
/// </summary>
public static class DeviceListProjector
{
    public static DeviceListProjection Create(
        IReadOnlyList<DeviceBatterySnapshot> snapshots,
        int lowBatteryThreshold,
        bool showUnsupportedDevices)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var visible = showUnsupportedDevices
            ? snapshots
            : snapshots.Where(snapshot => !IsUnsupported(snapshot)).ToList();

        var items = visible
            .OrderBy(snapshot => SortRank(snapshot, lowBatteryThreshold))
            .ThenBy(snapshot => snapshot.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(snapshot => new DeviceItemViewModel(snapshot, lowBatteryThreshold))
            .ToList();

        return new DeviceListProjection(items, snapshots.Count - visible.Count);
    }

    /// <summary>“不支持”只包含设备明确回复没有电池服务的场景；暂时不可达仍保留在列表里。</summary>
    public static bool IsUnsupported(DeviceBatterySnapshot snapshot) =>
        snapshot.Status == BatteryReadStatus.NotSupported;

    private static int SortRank(DeviceBatterySnapshot snapshot, int lowBatteryThreshold)
    {
        if (snapshot.BatteryPercent is int percent && percent <= lowBatteryThreshold)
        {
            return 0;
        }

        if (snapshot.BatteryPercent.HasValue)
        {
            return 1;
        }

        return snapshot.Status is BatteryReadStatus.Stale or BatteryReadStatus.Unavailable
            ? 2
            : 3;
    }
}
