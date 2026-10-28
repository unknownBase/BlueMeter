using BlueMeter.Models;

namespace BlueMeter.ViewModels;

/// <summary>
/// M2 预览列表使用的轻量行模型。M4 会替换为完整的 Fluent 设备项。
/// </summary>
public sealed class DeviceItemViewModel
{
    public DeviceItemViewModel(DeviceBatterySnapshot snapshot, int lowBatteryThreshold)
    {
        Id = snapshot.Id;
        Name = snapshot.Name;
        Transport = snapshot.Transport;
        Status = snapshot.Status;
        BatteryPercent = snapshot.BatteryPercent;
        IsBatteryKnown = snapshot.BatteryPercent.HasValue;
        IsLowBattery = snapshot.BatteryPercent is int percent && percent <= lowBatteryThreshold;
        BatteryText = snapshot.BatteryPercent is int value ? $"{value}%" : "--";
        MetaText = BuildMetaText(snapshot);
    }

    public string Id { get; }

    public string Name { get; }

    public DeviceTransportKind Transport { get; }

    public BatteryReadStatus Status { get; }

    public int? BatteryPercent { get; }

    public bool IsBatteryKnown { get; }

    public bool IsLowBattery { get; }

    public string BatteryText { get; }

    public string MetaText { get; }

    private static string BuildMetaText(DeviceBatterySnapshot snapshot)
    {
        var parts = new List<string>
        {
            TransportText(snapshot.Transport),
        };

        if (snapshot.Transport == DeviceTransportKind.Hid24G &&
            !string.IsNullOrWhiteSpace(snapshot.VendorName))
        {
            parts.Add(snapshot.VendorName.Trim());
        }

        if (snapshot.IsConnected)
        {
            parts.Add("已连接");
        }

        parts.Add(StatusText(snapshot));

        if (!string.IsNullOrWhiteSpace(snapshot.ChargingText))
        {
            parts.Add(snapshot.ChargingText.Trim());
        }

        return string.Join(" · ", parts);
    }

    private static string TransportText(DeviceTransportKind transport) =>
        transport switch
        {
            DeviceTransportKind.BluetoothLe => "BLE",
            DeviceTransportKind.BluetoothClassic => "经典蓝牙",
            DeviceTransportKind.Hid24G => "2.4G 接收器",
            _ => "未知传输",
        };

    private static string StatusText(DeviceBatterySnapshot snapshot)
    {
        var text = snapshot.Status switch
        {
            BatteryReadStatus.Ok => snapshot.LastSuccessAt is null
                ? "已读取"
                : $"{FormatRelative(snapshot.LastSuccessAt.Value)}更新",
            BatteryReadStatus.Reading => "正在读取…",
            BatteryReadStatus.Stale => snapshot.LastSuccessAt is null
                ? "暂时不可达"
                : $"暂时不可达，上次 {FormatRelative(snapshot.LastSuccessAt.Value)}",
            BatteryReadStatus.Unavailable => "暂时不可达",
            BatteryReadStatus.NotSupported => "不支持电量读取",
            _ => snapshot.Transport switch
            {
                DeviceTransportKind.Hid24G => "等待读取",
                _ => "等待扫描",
            },
        };

        // K98 接收器在键盘休眠时不回包，重试也无效，只能提示用户先按键激活。
        return NeedsWakeHint(snapshot) ? $"{text}，按键唤醒后刷新" : text;
    }

    private static bool NeedsWakeHint(DeviceBatterySnapshot snapshot) =>
        snapshot.Transport == DeviceTransportKind.Hid24G &&
        snapshot.Protocol == Hid24GProtocol.LingbaoK98 &&
        snapshot.Status is BatteryReadStatus.Stale or BatteryReadStatus.Unavailable;

    private static string FormatRelative(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.Now - timestamp.ToLocalTime();
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        }

        return timestamp.ToLocalTime().ToString("HH:mm");
    }
}
