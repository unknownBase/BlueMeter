using BlueMeter.Models;

namespace BlueMeter.ViewModels;

/// <summary>
/// 列表行模型：把设备快照投影成卡片所需的展示字段。
/// 行对象在集合里保持稳定（按 <see cref="Id"/> 复用），刷新时原地更新属性，
/// 只有排序名次变化才会移动位置，避免每轮刷新重建列表导致闪烁。
/// </summary>
public sealed class DeviceItemViewModel : ObservableObject
{
    private DeviceBatterySnapshot _snapshot;
    private int _lowBatteryThreshold;

    private string _name = string.Empty;
    private DeviceTransportKind _transport;
    private BatteryReadStatus _status;
    private int? _batteryPercent;
    private string _batteryText = "--";
    private string _metaText = string.Empty;
    private bool _isBatteryKnown;
    private bool _isLowBattery;
    private bool _isStale;
    private bool _isUnsupported;
    private BatteryLevel _batteryLevel = BatteryLevel.Unknown;
    private string _batteryAutomationName = "电量未知";
    private string _automationName = string.Empty;

    public DeviceItemViewModel(DeviceBatterySnapshot snapshot, int lowBatteryThreshold)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _snapshot = snapshot;
        _lowBatteryThreshold = lowBatteryThreshold;
        Id = snapshot.Id;
        Category = DeviceCategoryClassifier.Classify(snapshot.Name, snapshot.Protocol);
        Apply();
    }

    public string Id { get; }

    /// <summary>仅用于选图标，不代表设备能力。</summary>
    public DeviceCategory Category { get; }

    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    public DeviceTransportKind Transport
    {
        get => _transport;
        private set => SetProperty(ref _transport, value);
    }

    public BatteryReadStatus Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public int? BatteryPercent
    {
        get => _batteryPercent;
        private set
        {
            if (SetProperty(ref _batteryPercent, value))
            {
                OnPropertyChanged(nameof(BatteryBarPercent));
            }
        }
    }

    /// <summary>电量条绑定的百分比；无读数时为 null，模板据此隐藏电量条。</summary>
    public double? BatteryBarPercent => _batteryPercent;

    public string BatteryText
    {
        get => _batteryText;
        private set => SetProperty(ref _batteryText, value);
    }

    public string MetaText
    {
        get => _metaText;
        private set => SetProperty(ref _metaText, value);
    }

    public bool IsBatteryKnown
    {
        get => _isBatteryKnown;
        private set => SetProperty(ref _isBatteryKnown, value);
    }

    public bool IsLowBattery
    {
        get => _isLowBattery;
        private set => SetProperty(ref _isLowBattery, value);
    }

    /// <summary>暂时不可达（含保留旧值的场景），整行降透明度。</summary>
    public bool IsStale
    {
        get => _isStale;
        private set => SetProperty(ref _isStale, value);
    }

    public bool IsUnsupported
    {
        get => _isUnsupported;
        private set => SetProperty(ref _isUnsupported, value);
    }

    public BatteryLevel BatteryLevel
    {
        get => _batteryLevel;
        private set => SetProperty(ref _batteryLevel, value);
    }

    public string BatteryAutomationName
    {
        get => _batteryAutomationName;
        private set => SetProperty(ref _batteryAutomationName, value);
    }

    /// <summary>整行的无障碍名称：设备名、电量、状态与更新时间。</summary>
    public string AutomationName
    {
        get => _automationName;
        private set => SetProperty(ref _automationName, value);
    }

    public DateTimeOffset? LastSuccessAt => _snapshot.LastSuccessAt;

    /// <summary>用新快照刷新同一行；阈值变化时一并重算档位。</summary>
    internal void Update(DeviceBatterySnapshot snapshot, int lowBatteryThreshold)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _snapshot = snapshot;
        _lowBatteryThreshold = lowBatteryThreshold;
        Apply();
    }

    /// <summary>快照未变但相对时间过期时调用，只重算与时间相关的文案。</summary>
    public void RefreshTimeText() => Apply();

    private void Apply()
    {
        var snapshot = _snapshot;
        var threshold = _lowBatteryThreshold;

        Name = snapshot.Name;
        Transport = snapshot.Transport;
        Status = snapshot.Status;
        BatteryPercent = snapshot.BatteryPercent;
        IsBatteryKnown = snapshot.BatteryPercent.HasValue;
        BatteryText = snapshot.BatteryPercent is int percent ? $"{percent}%" : "--";
        IsLowBattery = snapshot.BatteryPercent is int low && low <= threshold;
        IsStale = snapshot.Status is BatteryReadStatus.Stale or BatteryReadStatus.Unavailable;
        IsUnsupported = snapshot.Status == BatteryReadStatus.NotSupported;
        BatteryLevel = BatteryLevelCalculator.FromPercent(snapshot.BatteryPercent, threshold);
        BatteryAutomationName = IsBatteryKnown
            ? (IsLowBattery ? $"电量 {BatteryText}，电量低" : $"电量 {BatteryText}")
            : "电量未知";
        MetaText = BuildMetaText(snapshot);
        AutomationName = BuildAutomationName(snapshot);
    }

    private static string BuildAutomationName(DeviceBatterySnapshot snapshot)
    {
        var battery = snapshot.BatteryPercent is int percent ? $"电量 {percent}%" : "电量未知";
        return $"{snapshot.Name}，{battery}，{StatusText(snapshot)}";
    }

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
        (snapshot.Status is BatteryReadStatus.Stale or BatteryReadStatus.Unavailable) &&
        // 进程刚启动时接收器还在被系统枚举，首轮失败不代表键盘休眠；
        // 连续两轮失败才提示按键，避免刚启动就误伤。
        snapshot.ConsecutiveFailures >= 2;

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
