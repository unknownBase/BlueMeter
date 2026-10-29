using BlueMeter.Models;
using BlueMeter.ViewModels;
using Xunit;

namespace BlueMeter.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void ApplySettings_BindsControlsWithoutRaisingEdits()
    {
        var viewModel = new SettingsViewModel();
        var edits = 0;
        viewModel.EditRequested += (_, _) => edits++;

        viewModel.ApplySettings(
            new AppSettings
            {
                RefreshIntervalSeconds = 120,
                LowBatteryThreshold = 35,
                LowBatteryNotificationsEnabled = false,
                ShowBatteryInTrayMenu = false,
                UseGattNotifications = false,
                ShowUnsupportedDevices = false,
                RunAtStartup = true,
                StartMinimized = true,
            },
            @"C:\settings.json");

        Assert.Equal("2", viewModel.RefreshIntervalText);
        Assert.Equal(RefreshIntervalUnit.Minutes, viewModel.SelectedUnit?.Unit);
        Assert.Equal(35, viewModel.SelectedThreshold?.Value);
        Assert.False(viewModel.LowBatteryNotificationsEnabled);
        Assert.False(viewModel.ShowBatteryInTrayMenu);
        Assert.False(viewModel.UseGattNotifications);
        Assert.False(viewModel.ShowUnsupportedDevices);
        Assert.True(viewModel.RunAtStartup);
        Assert.True(viewModel.StartMinimized);
        Assert.Contains(@"C:\settings.json", viewModel.SettingsPathText);
        Assert.Equal(0, edits);
    }

    [Fact]
    public void ApplySettings_AddsOptionForNonStepThreshold()
    {
        var viewModel = new SettingsViewModel();

        viewModel.ApplySettings(new AppSettings { LowBatteryThreshold = 23 });

        Assert.Equal(23, viewModel.SelectedThreshold?.Value);
        Assert.Contains(viewModel.ThresholdOptions, option => option.Value == 23);
    }

    [Fact]
    public void ToggleShowBatteryInTrayMenu_RaisesEdit()
    {
        var viewModel = new SettingsViewModel();
        AppSettings? edited = null;
        viewModel.EditRequested += (_, edit) =>
        {
            var copy = new AppSettings();
            edit(copy);
            edited = copy;
        };

        viewModel.ShowBatteryInTrayMenu = false;

        Assert.NotNull(edited);
        Assert.False(edited!.ShowBatteryInTrayMenu);
    }

    [Fact]
    public void ChangeThreshold_RaisesEdit()
    {
        var viewModel = new SettingsViewModel();
        AppSettings? edited = null;
        viewModel.EditRequested += (_, edit) =>
        {
            var copy = new AppSettings();
            edit(copy);
            edited = copy;
        };

        viewModel.SelectedThreshold = viewModel.ThresholdOptions.First(option => option.Value == 15);

        Assert.NotNull(edited);
        Assert.Equal(15, edited!.LowBatteryThreshold);
    }

    [Fact]
    public void ChangeUnit_CommitsSameNumberWithNewUnit()
    {
        var viewModel = new SettingsViewModel();
        viewModel.ApplySettings(new AppSettings { RefreshIntervalSeconds = 45 });

        AppSettings? edited = null;
        viewModel.EditRequested += (_, edit) =>
        {
            var copy = new AppSettings();
            edit(copy);
            edited = copy;
        };

        viewModel.RefreshIntervalText = "5";
        viewModel.SelectedUnit = viewModel.UnitOptions
            .First(option => option.Unit == RefreshIntervalUnit.Minutes);

        Assert.NotNull(edited);
        Assert.Equal(300, edited!.RefreshIntervalSeconds);
        Assert.Equal("5", viewModel.RefreshIntervalText);
    }

    [Fact]
    public void CommitRefreshInterval_ClampsToMaximum()
    {
        var viewModel = new SettingsViewModel();
        viewModel.ApplySettings(new AppSettings { RefreshIntervalSeconds = 45 });

        AppSettings? edited = null;
        viewModel.EditRequested += (_, edit) =>
        {
            var copy = new AppSettings();
            edit(copy);
            edited = copy;
        };

        viewModel.RefreshIntervalText = "100000";
        viewModel.SelectedUnit = viewModel.UnitOptions
            .First(option => option.Unit == RefreshIntervalUnit.Hours);

        Assert.NotNull(edited);
        Assert.Equal(AppSettings.MaxRefreshIntervalSeconds, edited!.RefreshIntervalSeconds);
        Assert.Equal("24", viewModel.RefreshIntervalText);
        Assert.Equal(RefreshIntervalUnit.Hours, viewModel.SelectedUnit?.Unit);
    }

    [Fact]
    public void CommitRefreshInterval_InvalidText_RevertsWithoutEdit()
    {
        var viewModel = new SettingsViewModel();
        viewModel.ApplySettings(new AppSettings { RefreshIntervalSeconds = 120 });

        var edits = 0;
        viewModel.EditRequested += (_, _) => edits++;

        viewModel.RefreshIntervalText = "abc";
        viewModel.CommitRefreshInterval();

        Assert.Equal(0, edits);
        Assert.Equal("2", viewModel.RefreshIntervalText);
        Assert.Equal(RefreshIntervalUnit.Minutes, viewModel.SelectedUnit?.Unit);
    }

    [Fact]
    public void CommitRefreshInterval_UnchangedValue_DoesNotRaise()
    {
        var viewModel = new SettingsViewModel();
        viewModel.ApplySettings(new AppSettings { RefreshIntervalSeconds = 45 });

        var edits = 0;
        viewModel.EditRequested += (_, _) => edits++;

        viewModel.RefreshIntervalText = "45";
        viewModel.CommitRefreshInterval();

        Assert.Equal(0, edits);
    }
}
