namespace BlueMeter.Models;

/// <summary>刷新间隔的显示单位；枚举值即换算成秒的倍数。</summary>
public enum RefreshIntervalUnit
{
    Seconds = 1,
    Minutes = 60,
    Hours = 3600,
}

/// <summary>
/// 刷新间隔的秒数与“数值 + 单位”之间的换算。设置文件只存秒数，界面按最大可整除单位显示。
/// </summary>
public static class RefreshIntervalConverter
{
    public static int ToSeconds(int value, RefreshIntervalUnit unit) =>
        value * (int)unit;

    /// <summary>挑最大的可整除单位，避免把 120 秒显示成“120 秒”而不是“2 分钟”。</summary>
    public static (int Value, RefreshIntervalUnit Unit) FromSeconds(int seconds)
    {
        if (seconds > 0 && seconds % 3600 == 0)
        {
            return (seconds / 3600, RefreshIntervalUnit.Hours);
        }

        if (seconds > 0 && seconds % 60 == 0)
        {
            return (seconds / 60, RefreshIntervalUnit.Minutes);
        }

        return (seconds, RefreshIntervalUnit.Seconds);
    }

    public static string UnitText(RefreshIntervalUnit unit) =>
        unit switch
        {
            RefreshIntervalUnit.Minutes => "分钟",
            RefreshIntervalUnit.Hours => "小时",
            _ => "秒",
        };
}
