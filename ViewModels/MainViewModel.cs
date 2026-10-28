using System.Collections.ObjectModel;
using System.Windows.Threading;
using BlueMeter.Models;

namespace BlueMeter.ViewModels;

/// <summary>底栏状态点的语义色。</summary>
public enum StatusSeverity
{
    Normal,
    Caution,
    Critical,
}

/// <summary>
/// 主窗口 ViewModel：持有设备行集合、排序后的列表、头部摘要与底栏状态，
/// 并把按钮动作暴露成命令。不直接接触窗口、托盘或 WinRT。
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(30);

    private readonly ObservableCollection<DeviceItemViewModel> _devices = new();
    private readonly ReadOnlyObservableCollection<DeviceItemViewModel> _devicesView;
    private readonly DispatcherTimer _clock;

    private AppSettings _settings = new();
    private IReadOnlyList<DeviceBatterySnapshot> _snapshots = Array.Empty<DeviceBatterySnapshot>();
    private bool _hasScanned;
    private bool _isInitialLoading = true;
    private bool _hasDevices;
    private bool _isEmpty;
    private bool _isRefreshing;
    private bool _isSettingsOpen;
    private bool _disposed;
    private int _hiddenUnsupportedCount;
    private DateTimeOffset? _lastScanAt;

    private string _summaryText = "正在扫描…";
    private string _subtitleText = "已配对设备与 2.4G 接收器";
    private string _listHintText = string.Empty;
    private string _globalStatus = "正在启动设备刷新…";
    private StatusSeverity _statusSeverity = StatusSeverity.Normal;

    public MainViewModel()
    {
        _devicesView = new ReadOnlyObservableCollection<DeviceItemViewModel>(_devices);

        RefreshCommand = new RelayCommand(
            () => RefreshRequested?.Invoke(this, EventArgs.Empty),
            () => !IsRefreshing);
        ToggleSettingsCommand = new RelayCommand(ToggleSettings);
        HideToTrayCommand = new RelayCommand(() => HideToTrayRequested?.Invoke(this, EventArgs.Empty));
        OpenLogDirectoryCommand = new RelayCommand(
            () => OpenLogDirectoryRequested?.Invoke(this, EventArgs.Empty));
        OpenBluetoothSettingsCommand = new RelayCommand(
            () => OpenBluetoothSettingsRequested?.Invoke(this, EventArgs.Empty));

        _clock = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = ClockInterval,
        };
        _clock.Tick += OnClockTick;
    }

    /// <summary>请求立即刷新；由窗口转成服务调用。</summary>
    public event EventHandler? RefreshRequested;

    public event EventHandler? HideToTrayRequested;

    public event EventHandler? OpenLogDirectoryRequested;

    public event EventHandler? OpenBluetoothSettingsRequested;

    public ReadOnlyObservableCollection<DeviceItemViewModel> Devices => _devicesView;

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ToggleSettingsCommand { get; }

    public RelayCommand HideToTrayCommand { get; }

    public RelayCommand OpenLogDirectoryCommand { get; }

    public RelayCommand OpenBluetoothSettingsCommand { get; }

    public string HeaderTitle => "设备电量";

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string SubtitleText
    {
        get => _subtitleText;
        private set => SetProperty(ref _subtitleText, value);
    }

    /// <summary>列表上方的次要信息：传输数量、不可达数量、被隐藏数量。</summary>
    public string ListHintText
    {
        get => _listHintText;
        private set => SetProperty(ref _listHintText, value);
    }

    public string GlobalStatus
    {
        get => _globalStatus;
        private set => SetProperty(ref _globalStatus, value);
    }

    public StatusSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set
        {
            if (!SetProperty(ref _isRefreshing, value))
            {
                return;
            }

            OnPropertyChanged(nameof(RefreshButtonText));
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    public string RefreshButtonText => IsRefreshing ? "正在刷新…" : "立即刷新";

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        private set => SetProperty(ref _isSettingsOpen, value);
    }

    /// <summary>首轮扫描尚未结束：显示进度环而不是空状态。</summary>
    public bool IsInitialLoading
    {
        get => _isInitialLoading;
        private set => SetProperty(ref _isInitialLoading, value);
    }

    public bool HasDevices
    {
        get => _hasDevices;
        private set => SetProperty(ref _hasDevices, value);
    }

    /// <summary>扫描完成且没有可显示设备。</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    /// <summary>相对时间（“3 分钟前”）每分钟级刷新，由窗口可见性控制启停。</summary>
    public void StartClock() => _clock.Start();

    public void StopClock() => _clock.Stop();

    public void SetStatus(string message, StatusSeverity severity = StatusSeverity.Normal)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        GlobalStatus = message;
        StatusSeverity = severity;
    }

    public void OpenSettings() => IsSettingsOpen = true;

    public bool CloseSettings()
    {
        if (!IsSettingsOpen)
        {
            return false;
        }

        IsSettingsOpen = false;
        return true;
    }

    public void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    /// <summary>应用设置：阈值或过滤条件可能改变排序与可见行，需要重新投影。</summary>
    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings.Clone();
        if (_hasScanned)
        {
            SyncRows();
        }

        UpdateTexts();
    }

    /// <summary>接收一轮完整快照：原地更新行、必要时移动位置，再刷新摘要。</summary>
    public void ApplySnapshots(IReadOnlyList<DeviceBatterySnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        _snapshots = snapshots;
        _hasScanned = true;
        _lastScanAt = DateTimeOffset.Now;
        SyncRows();
        UpdateTexts();
    }

    /// <summary>重算所有行的相对时间文案（窗口重新显示、定时器触发时调用）。</summary>
    public void RefreshRelativeTimes()
    {
        foreach (var device in _devices)
        {
            device.RefreshTimeText();
        }

        UpdateTexts();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clock.Stop();
        _clock.Tick -= OnClockTick;
    }

    private void OnClockTick(object? sender, EventArgs e) => RefreshRelativeTimes();

    private void SyncRows()
    {
        var order = DeviceListProjector.Order(
            _snapshots,
            _settings.LowBatteryThreshold,
            _settings.ShowUnsupportedDevices);

        _hiddenUnsupportedCount = order.HiddenUnsupportedCount;

        var existing = _devices.ToDictionary(device => device.Id, StringComparer.Ordinal);
        var desired = new List<DeviceItemViewModel>(order.Visible.Count);

        foreach (var snapshot in order.Visible)
        {
            if (existing.TryGetValue(snapshot.Id, out var row))
            {
                row.Update(snapshot, _settings.LowBatteryThreshold);
                desired.Add(row);
            }
            else
            {
                desired.Add(new DeviceItemViewModel(snapshot, _settings.LowBatteryThreshold));
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            if (index < _devices.Count && ReferenceEquals(_devices[index], desired[index]))
            {
                continue;
            }

            var current = _devices.IndexOf(desired[index]);
            if (current < 0)
            {
                _devices.Insert(index, desired[index]);
            }
            else
            {
                _devices.Move(current, index);
            }
        }

        while (_devices.Count > desired.Count)
        {
            _devices.RemoveAt(_devices.Count - 1);
        }
    }

    private void UpdateTexts()
    {
        HasDevices = _devices.Count > 0;
        IsEmpty = _hasScanned && !HasDevices;
        IsInitialLoading = !_hasScanned;

        var okPercents = _devices
            .Where(device => device.Status == BatteryReadStatus.Ok)
            .Select(device => device.BatteryPercent)
            .OfType<int>()
            .ToList();

        SummaryText = !_hasScanned
            ? "正在扫描…"
            : HasDevices
                ? $"{_devices.Count} 个设备 · 最低 {(okPercents.Count > 0 ? $"{okPercents.Min()}%" : "--")}"
                : "暂无可显示的设备";

        ListHintText = HasDevices ? BuildListHint() : string.Empty;
        SubtitleText = BuildSubtitle();
    }

    private string BuildListHint()
    {
        var parts = new List<string>();
        var ble = _devices.Count(device => device.Transport == DeviceTransportKind.BluetoothLe);
        var classic = _devices.Count(device => device.Transport == DeviceTransportKind.BluetoothClassic);
        var hid24G = _devices.Count(device => device.Transport == DeviceTransportKind.Hid24G);

        if (ble > 0)
        {
            parts.Add($"BLE {ble}");
        }

        if (classic > 0)
        {
            parts.Add($"经典蓝牙 {classic}");
        }

        if (hid24G > 0)
        {
            parts.Add($"2.4G {hid24G}");
        }

        var unreachable = _devices.Count(device => device.IsStale);
        if (unreachable > 0)
        {
            parts.Add($"{unreachable} 台暂时不可达");
        }

        if (_hiddenUnsupportedCount > 0)
        {
            parts.Add($"已隐藏 {_hiddenUnsupportedCount} 台不支持电量的设备");
        }

        return string.Join(" · ", parts);
    }

    private string BuildSubtitle()
    {
        var scanned = _lastScanAt is DateTimeOffset lastScan
            ? lastScan.ToLocalTime().ToString("HH:mm:ss")
            : "尚未完成";

        return $"已配对设备与 2.4G 接收器 · {FormatInterval(_settings.RefreshIntervalSeconds)} · 上次扫描 {scanned}";
    }

    private static string FormatInterval(int seconds)
    {
        if (seconds >= 3600 && seconds % 3600 == 0)
        {
            return $"{seconds / 3600} 小时自动刷新";
        }

        if (seconds >= 60 && seconds % 60 == 0)
        {
            return $"{seconds / 60} 分钟自动刷新";
        }

        return $"{seconds} 秒自动刷新";
    }
}
