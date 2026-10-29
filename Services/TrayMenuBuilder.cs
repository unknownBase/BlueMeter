using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 托盘右键菜单“设备电量”条目的唯一来源：只列有读数的设备，
/// 按电量升序取前 <see cref="MaxDevices"/> 台，并在文案里标出低电量与旧值。
/// 菜单项只读展示，点击不执行动作，完整列表仍在主窗口。
/// </summary>
public static class TrayMenuBuilder
{
    /// <summary>托盘菜单最多展示的设备数，其余设备由主窗口查看。</summary>
    public const int MaxDevices = 3;

    public const string SectionTitleText = "设备电量";

    /// <summary>分组标题；刷新中在标题里同步状态，避免用户误把旧值当本轮结果。</summary>
    public static string SectionTitle(bool refreshing) =>
        refreshing ? $"{SectionTitleText} · 正在刷新…" : SectionTitleText;

    private const int MaxDeviceNameLength = 18;

    public static IReadOnlyList<string> Build(
        IReadOnlyList<DeviceBatterySnapshot> snapshots,
        int lowBatteryThreshold)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        return snapshots
            .Where(snapshot => snapshot.BatteryPercent.HasValue)
            .OrderBy(snapshot => snapshot.BatteryPercent!.Value)
            .ThenBy(snapshot => snapshot.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxDevices)
            .Select(snapshot => Format(snapshot, lowBatteryThreshold))
            .ToList();
    }

    private static string Format(DeviceBatterySnapshot snapshot, int lowBatteryThreshold)
    {
        var percent = snapshot.BatteryPercent!.Value;
        var text = $"{ShortenName(snapshot.Name)} · {percent}%";

        // 菜单项不可点击，因此低电量与旧值只能靠文字标注，不能只靠颜色。
        if (percent <= lowBatteryThreshold)
        {
            text += " · 电量低";
        }

        if (snapshot.Status is BatteryReadStatus.Stale or BatteryReadStatus.Unavailable)
        {
            text += " · 旧值";
        }

        return text;
    }

    private static string ShortenName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "未知设备" : name.Trim();
        return trimmed.Length <= MaxDeviceNameLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, MaxDeviceNameLength - 1), "…");
    }
}
