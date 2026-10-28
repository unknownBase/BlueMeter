using BlueMeter.Models;
using Xunit;

namespace BlueMeter.Tests;

public sealed class ProbeHealthTrackerTests
{
    [Fact]
    public void NewTracker_HasNoStatistics()
    {
        var tracker = new ProbeHealthTracker();

        Assert.Equal(0, tracker.ConsecutiveFailures);
        Assert.Equal(0, tracker.TotalAttempts);
        Assert.Equal(0, tracker.TotalFailures);
        Assert.Equal("暂无统计", tracker.SuccessRateText);
    }

    [Fact]
    public void RecordFailure_CountsConsecutiveFailures()
    {
        var tracker = new ProbeHealthTracker();

        tracker.RecordFailure();
        tracker.RecordFailure();

        Assert.Equal(2, tracker.ConsecutiveFailures);
        Assert.Equal(2, tracker.TotalAttempts);
        Assert.Equal(2, tracker.TotalFailures);
    }

    [Fact]
    public void OnlyFirstFailure_RequestsWarning()
    {
        var tracker = new ProbeHealthTracker();

        Assert.True(tracker.RecordFailure());
        Assert.False(tracker.RecordFailure());
        Assert.False(tracker.RecordFailure());
    }

    [Fact]
    public void RecordSuccess_AfterFailures_ReportsRecovery()
    {
        var tracker = new ProbeHealthTracker();
        tracker.RecordFailure();

        Assert.True(tracker.RecordSuccess());
        Assert.Equal(0, tracker.ConsecutiveFailures);
        Assert.Equal(2, tracker.TotalAttempts);
        Assert.Equal(1, tracker.TotalFailures);
    }

    [Fact]
    public void RecordSuccess_WithoutFailures_DoesNotReportRecovery()
    {
        var tracker = new ProbeHealthTracker();

        Assert.False(tracker.RecordSuccess());
    }

    [Fact]
    public void SuccessRateText_ShowsSuccessesAndFailureRate()
    {
        var tracker = new ProbeHealthTracker();
        for (var i = 0; i < 12; i++)
        {
            tracker.RecordSuccess();
        }

        for (var i = 0; i < 4; i++)
        {
            tracker.RecordFailure();
        }

        Assert.Equal("12/16，失败率 25.0%", tracker.SuccessRateText);
    }
}
