using System.IO;
using BlueMeter.Models;
using BlueMeter.Services;
using Xunit;

namespace BlueMeter.Tests;

public sealed class SettingsCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "BlueMeter.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryUpdate_SavesAppliesAndWritesStartup()
    {
        var settings = new SettingsService(_directory);
        var notifications = new List<string>();
        AppSettings? applied = null;
        AppSettings? committed = null;
        bool? startupEnabled = null;

        var coordinator = new SettingsCoordinator(
            settings,
            enabled =>
            {
                startupEnabled = enabled;
                return true;
            },
            apply: value => applied = value,
            committed: value => committed = value,
            notify: notifications.Add);

        var result = coordinator.TryUpdate(next =>
        {
            next.RefreshIntervalSeconds = 90;
            next.RunAtStartup = true;
        });

        Assert.True(result);
        Assert.Empty(notifications);
        Assert.Equal(90, applied!.RefreshIntervalSeconds);
        Assert.True(committed!.RunAtStartup);
        Assert.True(startupEnabled);
        Assert.Equal(90, settings.Current.RefreshIntervalSeconds);
    }

    [Fact]
    public void TryUpdate_SaveFailure_RollsBackAndNotifies()
    {
        // 用一个“文件”当父目录，Directory.CreateDirectory 必然失败，Save 返回 false。
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocker, "x");

        var settings = new SettingsService(Path.Combine(blocker, "settings"));
        var notifications = new List<string>();
        AppSettings? applied = null;
        var startupCalls = 0;

        var coordinator = new SettingsCoordinator(
            settings,
            _ =>
            {
                startupCalls++;
                return true;
            },
            apply: value => applied = value,
            committed: _ => { },
            notify: notifications.Add);

        var before = settings.Current.RefreshIntervalSeconds;
        var result = coordinator.TryUpdate(next =>
        {
            next.RefreshIntervalSeconds = 120;
            next.RunAtStartup = true;
        });

        Assert.False(result);
        Assert.Equal(before, settings.Current.RefreshIntervalSeconds);
        Assert.Equal(before, applied!.RefreshIntervalSeconds);
        Assert.Equal(0, startupCalls);
        Assert.Contains(notifications, text => text.Contains("保存失败", StringComparison.Ordinal));
    }

    [Fact]
    public void TryUpdate_StartupFailure_RollsBackOnlyThatSetting()
    {
        var settings = new SettingsService(_directory);
        var notifications = new List<string>();

        var coordinator = new SettingsCoordinator(
            settings,
            _ => false,
            apply: _ => { },
            committed: _ => { },
            notify: notifications.Add);

        var result = coordinator.TryUpdate(next =>
        {
            next.RunAtStartup = true;
            next.LowBatteryThreshold = 30;
        });

        Assert.True(result);
        Assert.False(settings.Current.RunAtStartup);
        Assert.Equal(30, settings.Current.LowBatteryThreshold);
        Assert.Contains(notifications, text => text.Contains("开机启动", StringComparison.Ordinal));
    }

    [Fact]
    public void TryUpdate_WithoutStartupHandler_SkipsRegistrySideEffect()
    {
        var settings = new SettingsService(_directory);
        var coordinator = new SettingsCoordinator(
            settings,
            setStartupEnabled: null,
            apply: _ => { },
            committed: _ => { },
            notify: _ => { });

        var result = coordinator.TryUpdate(next => next.RunAtStartup = true);

        Assert.True(result);
        Assert.True(settings.Current.RunAtStartup);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论。
        }
    }
}
