namespace BlueMeter.Models;

/// <summary>
/// 电量档位：由读数与低电量阈值共同决定，供电量条与行高亮使用。
/// </summary>
public enum BatteryLevel
{
    /// <summary>没有可用读数，UI 不显示电量条。</summary>
    Unknown,

    /// <summary>正常区间（≥ 阈值 + 20）。</summary>
    Normal,

    /// <summary>警告区间（阈值 &lt; x &lt; 阈值 + 20）。</summary>
    Caution,

    /// <summary>低电量（≤ 阈值）。</summary>
    Critical,
}

/// <summary>把读数换算成档位，保证电量条颜色、行高亮与“电量低”文案使用同一阈值。</summary>
public static class BatteryLevelCalculator
{
    /// <summary>警告区间宽度：阈值之上 20 个百分点。</summary>
    public const int CautionBandWidth = 20;

    public static BatteryLevel FromPercent(int? percent, int lowBatteryThreshold)
    {
        if (percent is not int value)
        {
            return BatteryLevel.Unknown;
        }

        if (value <= lowBatteryThreshold)
        {
            return BatteryLevel.Critical;
        }

        return value < lowBatteryThreshold + CautionBandWidth
            ? BatteryLevel.Caution
            : BatteryLevel.Normal;
    }
}
