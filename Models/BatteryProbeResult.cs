namespace BlueMeter.Models;

/// <summary>
/// 单次探针读取结果。失败时保留阶段与错误码，便于结构化日志和诊断。
/// </summary>
public sealed record BatteryProbeResult
{
    public required BatteryReadStatus Status { get; init; }

    public int? BatteryPercent { get; init; }

    public bool SupportsNotifications { get; init; }

    public string? ChargingText { get; init; }

    public string? FailureStage { get; init; }

    public string? FailureMessage { get; init; }

    public int? HResult { get; init; }

    public bool IsSuccess => Status == BatteryReadStatus.Ok && BatteryPercent.HasValue;

    public static BatteryProbeResult Ok(
        int batteryPercent,
        bool supportsNotifications,
        string? chargingText = null) =>
        new()
        {
            Status = BatteryReadStatus.Ok,
            BatteryPercent = batteryPercent,
            SupportsNotifications = supportsNotifications,
            ChargingText = chargingText,
        };

    public static BatteryProbeResult NotSupported(
        string stage,
        string message,
        bool supportsNotifications = false) =>
        new()
        {
            Status = BatteryReadStatus.NotSupported,
            SupportsNotifications = supportsNotifications,
            FailureStage = stage,
            FailureMessage = message,
        };

    public static BatteryProbeResult Unavailable(
        string stage,
        string message,
        int? hResult = null,
        bool supportsNotifications = false) =>
        new()
        {
            Status = BatteryReadStatus.Unavailable,
            SupportsNotifications = supportsNotifications,
            FailureStage = stage,
            FailureMessage = message,
            HResult = hResult,
        };

    /// <summary>
    /// 经典蓝牙设备离线时，系统属性里的电量是上次上报的缓存值。
    /// 保留该值并标记为 <see cref="BatteryReadStatus.Stale"/>，不能当作实时读数。
    /// </summary>
    public static BatteryProbeResult Cached(
        int batteryPercent,
        string? chargingText = null) =>
        new()
        {
            Status = BatteryReadStatus.Stale,
            BatteryPercent = batteryPercent,
            ChargingText = chargingText,
        };
}
