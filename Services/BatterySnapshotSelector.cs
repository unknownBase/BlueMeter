using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 托盘 ToolTip 与右键菜单共用的设备选择规则：只取有读数的设备，按电量升序、
/// 同电量按名称排序。两个入口共用同一份规则，避免摘要与菜单给出的“最低电量”不一致。
/// </summary>
public static class BatterySnapshotSelector
{
    public static IReadOnlyList<DeviceBatterySnapshot> ReadableLowestFirst(
        IReadOnlyList<DeviceBatterySnapshot> snapshots,
        int take = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        return snapshots
            .Where(snapshot => snapshot.BatteryPercent.HasValue)
            .OrderBy(snapshot => snapshot.BatteryPercent!.Value)
            .ThenBy(snapshot => snapshot.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(take)
            .ToList();
    }
}
