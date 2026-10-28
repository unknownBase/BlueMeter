namespace BlueMeter.Services;

/// <summary>
/// 从 WinRT 设备属性解出电量。经典蓝牙没有统一的电量 API，只能依赖 AEP 属性：
/// <c>System.Devices.BatteryPlusCharging</c> 用 “100 + 电量” 编码充电状态，
/// <c>System.Devices.BatteryLife</c> 取 0–100，101 表示未知。
/// </summary>
public static class SystemBatteryPropertyReader
{
    public const string BatteryLifeKey = "System.Devices.BatteryLife";

    public const string BatteryPlusChargingKey = "System.Devices.BatteryPlusCharging";

    /// <summary>
    /// 优先级：<c>BatteryPlusCharging</c>（含充电态）→ <c>BatteryLife</c>。
    /// 0、101 以及越界值都视为未知，避免把“未知”当成 0% 触发低电量告警。
    /// </summary>
    public static (int? Percent, bool Charging) Read(IReadOnlyDictionary<string, object> properties)
    {
        if (TryGetByte(properties, BatteryPlusChargingKey) is byte combined)
        {
            // 100–200：100 + 电量，表示正在充电。
            if (combined is >= 100 and <= 200)
            {
                return (combined - 100, true);
            }

            // 1–99：部分设备直接写入百分比。
            if (combined is > 0 and < 100)
            {
                return (combined, false);
            }
        }

        if (TryGetByte(properties, BatteryLifeKey) is byte life && life is > 0 and <= 100)
        {
            return (life, false);
        }

        return (null, false);
    }

    private static byte? TryGetByte(IReadOnlyDictionary<string, object> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        try
        {
            var number = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            return number is >= 0 and <= 255 ? (byte)number : null;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
