using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 设置编辑链路：保存 → 回显 → 失败回滚，并把“开机启动写注册表”这类外部副作用收在一处。
/// 组合根只负责装配，调用方拿到的始终是最终落盘的值。
/// </summary>
public sealed class SettingsCoordinator
{
    private readonly SettingsService _settings;
    private readonly Func<bool, bool>? _setStartupEnabled;
    private readonly Action<AppSettings> _apply;
    private readonly Action<AppSettings>? _committed;
    private readonly Action<string> _notify;

    /// <param name="settings">设置读写。</param>
    /// <param name="setStartupEnabled">写开机启动项；返回值表示是否成功，null 表示不处理该副作用。</param>
    /// <param name="apply">把最终值回显到界面（保存失败时也会以旧值调用）。</param>
    /// <param name="committed">修改确实落盘后触发，用于同步托盘等派生展示。</param>
    /// <param name="notify">面向用户的失败提示。</param>
    public SettingsCoordinator(
        SettingsService settings,
        Func<bool, bool>? setStartupEnabled,
        Action<AppSettings> apply,
        Action<AppSettings>? committed,
        Action<string> notify)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(notify);

        _settings = settings;
        _setStartupEnabled = setStartupEnabled;
        _apply = apply;
        _committed = committed;
        _notify = notify;
    }

    public AppSettings Current => _settings.Current;

    public string SettingsPath => _settings.SettingsPath;

    /// <summary>
    /// 落盘一次设置修改。返回 false 表示保存失败、界面已回滚到旧值；
    /// 开机启动写入失败时只回滚该项，其余修改保留。
    /// </summary>
    public bool TryUpdate(Action<AppSettings> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var previousRunAtStartup = _settings.Current.RunAtStartup;

        if (!_settings.Update(edit))
        {
            AppLog.Warn("设置保存失败，已放弃本次修改", "Settings");
            _notify("设置保存失败，已恢复为原值。");
            _apply(_settings.Current);
            return false;
        }

        var current = _settings.Current;
        if (current.RunAtStartup != previousRunAtStartup &&
            _setStartupEnabled?.Invoke(current.RunAtStartup) == false)
        {
            _settings.Update(settings => settings.RunAtStartup = previousRunAtStartup);
            _notify("开机启动设置失败，请检查系统权限。");
        }

        _apply(_settings.Current);
        _committed?.Invoke(_settings.Current);
        return true;
    }
}
