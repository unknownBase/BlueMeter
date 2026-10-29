using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using BlueMeter.Models;

namespace BlueMeter.ViewModels;

/// <summary>数值下拉框条目（低电量阈值）。</summary>
public sealed record NumberOption(int Value, string Unit = "")
{
    public override string ToString() => $"{Value}{Unit}";
}

/// <summary>刷新间隔单位下拉框条目。</summary>
public sealed record UnitOption(RefreshIntervalUnit Unit)
{
    public override string ToString() => RefreshIntervalConverter.UnitText(Unit);
}

/// <summary>
/// 设置面板 ViewModel：把持久化设置映射成控件值，编辑后通过 <see cref="EditRequested"/>
/// 交回组合根（保存、注册表副作用与失败回滚都在 <c>App</c> 里）。
/// 不引用任何 WPF 控件，可直接单元测试。
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly UnitOption[] _unitOptions;

    private AppSettings _settings = new();
    private string? _settingsPath;
    private bool _suppress;

    private string _refreshIntervalText = "45";
    private UnitOption? _selectedUnit;
    private NumberOption? _selectedThreshold;
    private bool _lowBatteryNotificationsEnabled = true;
    private bool _showBatteryInTrayMenu = true;
    private bool _useGattNotifications = true;
    private bool _showUnsupportedDevices = true;
    private bool _runAtStartup;
    private bool _startMinimized;
    private string _settingsPathText = "修改会立即生效并自动保存";

    public SettingsViewModel()
    {
        _unitOptions = new[]
        {
            new UnitOption(RefreshIntervalUnit.Seconds),
            new UnitOption(RefreshIntervalUnit.Minutes),
            new UnitOption(RefreshIntervalUnit.Hours),
        };

        var thresholdCount =
            (AppSettings.MaxLowBatteryThreshold - AppSettings.MinLowBatteryThreshold) / 5 + 1;
        ThresholdOptions = new ObservableCollection<NumberOption>(
            Enumerable.Range(0, thresholdCount)
                .Select(index => new NumberOption(
                    AppSettings.MinLowBatteryThreshold + index * 5,
                    " %")));

        Bind();
    }

    /// <summary>设置面板发起的修改；由窗口转给 <c>App</c> 保存并回写界面。</summary>
    public event EventHandler<Action<AppSettings>>? EditRequested;

    public ObservableCollection<NumberOption> ThresholdOptions { get; }

    public IReadOnlyList<UnitOption> UnitOptions => _unitOptions;

    /// <summary>刷新间隔输入框里的数字部分；单位切换时按新单位重新解释。</summary>
    public string RefreshIntervalText
    {
        get => _refreshIntervalText;
        set => SetProperty(ref _refreshIntervalText, value);
    }

    public UnitOption? SelectedUnit
    {
        get => _selectedUnit;
        set
        {
            if (!SetProperty(ref _selectedUnit, value) || _suppress || value is null)
            {
                return;
            }

            // 旧行为：切换单位时按新单位解释当前数字（例如 45 秒 → 45 分钟）再保存。
            CommitRefreshInterval();
        }
    }

    public NumberOption? SelectedThreshold
    {
        get => _selectedThreshold;
        set
        {
            if (!SetProperty(ref _selectedThreshold, value) || _suppress || value is null)
            {
                return;
            }

            RaiseEdit(settings => settings.LowBatteryThreshold = value.Value);
        }
    }

    public bool LowBatteryNotificationsEnabled
    {
        get => _lowBatteryNotificationsEnabled;
        set => SetToggle(
            ref _lowBatteryNotificationsEnabled,
            value,
            (settings, enabled) => settings.LowBatteryNotificationsEnabled = enabled);
    }

    public bool ShowBatteryInTrayMenu
    {
        get => _showBatteryInTrayMenu;
        set => SetToggle(
            ref _showBatteryInTrayMenu,
            value,
            (settings, enabled) => settings.ShowBatteryInTrayMenu = enabled);
    }

    public bool UseGattNotifications
    {
        get => _useGattNotifications;
        set => SetToggle(
            ref _useGattNotifications,
            value,
            (settings, enabled) => settings.UseGattNotifications = enabled);
    }

    public bool ShowUnsupportedDevices
    {
        get => _showUnsupportedDevices;
        set => SetToggle(
            ref _showUnsupportedDevices,
            value,
            (settings, enabled) => settings.ShowUnsupportedDevices = enabled);
    }

    public bool RunAtStartup
    {
        get => _runAtStartup;
        set => SetToggle(
            ref _runAtStartup,
            value,
            (settings, enabled) => settings.RunAtStartup = enabled);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetToggle(
            ref _startMinimized,
            value,
            (settings, enabled) => settings.StartMinimized = enabled);
    }

    public string SettingsPathText
    {
        get => _settingsPathText;
        private set => SetProperty(ref _settingsPathText, value);
    }

    /// <summary>同步最新设置并重新绑定控件；保存失败回滚时也走这里。</summary>
    public void ApplySettings(AppSettings settings, string? settingsPath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings.Clone();
        _settingsPath = settingsPath;
        SettingsPathText = string.IsNullOrWhiteSpace(settingsPath)
            ? "修改会立即生效并自动保存"
            : $"修改会立即生效，保存在 {settingsPath}";
        Bind();
    }

    /// <summary>
    /// 把“数字 + 单位”换算成秒并交给组合根；非法输入或超范围时回显边界内数值。
    /// </summary>
    public void CommitRefreshInterval()
    {
        if (_suppress || SelectedUnit is not UnitOption unit)
        {
            return;
        }

        if (!int.TryParse(
                RefreshIntervalText.Trim(),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var value) ||
            value <= 0)
        {
            Bind();
            return;
        }

        var seconds = Math.Clamp(
            RefreshIntervalConverter.ToSeconds(value, unit.Unit),
            AppSettings.MinRefreshIntervalSeconds,
            AppSettings.MaxRefreshIntervalSeconds);

        var display = RefreshIntervalConverter.FromSeconds(seconds);
        _suppress = true;
        try
        {
            RefreshIntervalText = display.Value.ToString(CultureInfo.CurrentCulture);
            SelectedUnit = FindUnit(display.Unit);
        }
        finally
        {
            _suppress = false;
        }

        if (seconds != _settings.RefreshIntervalSeconds)
        {
            RaiseEdit(settings => settings.RefreshIntervalSeconds = seconds);
        }
    }

    private void Bind()
    {
        _suppress = true;
        try
        {
            var interval = RefreshIntervalConverter.FromSeconds(_settings.RefreshIntervalSeconds);
            RefreshIntervalText = interval.Value.ToString(CultureInfo.CurrentCulture);
            SelectedUnit = FindUnit(interval.Unit);
            SelectedThreshold = FindThreshold(_settings.LowBatteryThreshold);
            LowBatteryNotificationsEnabled = _settings.LowBatteryNotificationsEnabled;
            ShowBatteryInTrayMenu = _settings.ShowBatteryInTrayMenu;
            UseGattNotifications = _settings.UseGattNotifications;
            ShowUnsupportedDevices = _settings.ShowUnsupportedDevices;
            RunAtStartup = _settings.RunAtStartup;
            StartMinimized = _settings.StartMinimized;
        }
        finally
        {
            _suppress = false;
        }
    }

    private void SetToggle(
        ref bool field,
        bool value,
        Action<AppSettings, bool> apply,
        [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName) || _suppress)
        {
            return;
        }

        RaiseEdit(settings => apply(settings, value));
    }

    private void RaiseEdit(Action<AppSettings> edit) => EditRequested?.Invoke(this, edit);

    private UnitOption FindUnit(RefreshIntervalUnit unit) =>
        _unitOptions.First(option => option.Unit == unit);

    private NumberOption FindThreshold(int value)
    {
        var option = ThresholdOptions.FirstOrDefault(item => item.Value == value);
        if (option is null)
        {
            // 手工编辑过的设置可能不是 5 的倍数，补一个条目以免下拉框空白。
            option = new NumberOption(value, " %");
            ThresholdOptions.Add(option);
        }

        return option;
    }
}
