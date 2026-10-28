namespace BlueMeter.Models;

/// <summary>
/// 用户设置。字段与 <c>%LOCALAPPDATA%\BlueMeter\settings.json</c> 一一对应。
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public const int MinRefreshIntervalSeconds = 10;
    public const int MaxRefreshIntervalSeconds = 24 * 60 * 60;
    public const int MinLowBatteryThreshold = 5;
    public const int MaxLowBatteryThreshold = 50;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public int RefreshIntervalSeconds { get; set; } = 45;

    public int LowBatteryThreshold { get; set; } = 20;

    public bool LowBatteryNotificationsEnabled { get; set; } = true;

    public bool UseGattNotifications { get; set; } = true;

    public bool StartMinimized { get; set; }

    public bool RunAtStartup { get; set; }

    public bool ShowUnsupportedDevices { get; set; } = true;

    /// <summary>
    /// 修正旧版本、手工编辑或损坏文件中的越界值。未知字段在反序列化时已被忽略。
    /// </summary>
    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        RefreshIntervalSeconds = Math.Clamp(
            RefreshIntervalSeconds,
            MinRefreshIntervalSeconds,
            MaxRefreshIntervalSeconds);
        LowBatteryThreshold = Math.Clamp(
            LowBatteryThreshold,
            MinLowBatteryThreshold,
            MaxLowBatteryThreshold);
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
