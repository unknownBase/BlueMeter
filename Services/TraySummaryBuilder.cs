using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 托盘 ToolTip 文案的唯一来源。Windows 的 <c>NOTIFYICONDATA.szTip</c> 约 127 字符，
/// 这里统一截断到 120 字符，并限制设备名长度，避免关键信息被截掉。
/// </summary>
public static class TraySummaryBuilder
{
    public const int MaxLength = 120;
    private const int MaxDeviceNameLength = 28;

    public static string Scanning() => "BlueMeter · 正在扫描蓝牙与 2.4G 设备…";

    public static string StatusHint(string message) => Truncate($"BlueMeter · {message}");

    public static string Build(IReadOnlyList<DeviceBatterySnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return "BlueMeter · 未发现蓝牙或 2.4G 设备";
        }

        var withBattery = BatterySnapshotSelector.ReadableLowestFirst(snapshots);

        if (withBattery.Count == 0)
        {
            return Truncate($"BlueMeter · 已发现 {snapshots.Count} 台设备，暂无可读电量");
        }

        var lowest = withBattery[0];

        if (withBattery.Count == 1)
        {
            var suffix = lowest.Status == BatteryReadStatus.Ok
                ? string.Empty
                : "（暂时不可达）";
            return Truncate(
                $"BlueMeter · {ShortenName(lowest.Name)} {lowest.BatteryPercent}%{suffix}");
        }

        var hasStaleValue = withBattery.Any(snapshot => snapshot.Status != BatteryReadStatus.Ok);
        return Truncate(
            $"BlueMeter · {snapshots.Count} 台设备，最低 {lowest.BatteryPercent}%" +
            (hasStaleValue ? "（含旧值）" : string.Empty));
    }

    private static string ShortenName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "未知设备" : name.Trim();
        return trimmed.Length <= MaxDeviceNameLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, MaxDeviceNameLength - 1), "…");
    }

    private static string Truncate(string text) =>
        text.Length <= MaxLength
            ? text
            : string.Concat(text.AsSpan(0, MaxLength - 1), "…");
}
