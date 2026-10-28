using BlueMeter.Models;
using Xunit;

namespace BlueMeter.Tests;

public sealed class K98RetryScheduleTests
{
    [Fact]
    public void AttemptCount_IsFour()
    {
        Assert.Equal(4, K98RetrySchedule.AttemptCount);
    }

    [Fact]
    public void ReadTimeout_IncreasesWithAttempt()
    {
        for (var attempt = 1; attempt < K98RetrySchedule.AttemptCount; attempt++)
        {
            Assert.True(
                K98RetrySchedule.ReadTimeoutMilliseconds(attempt) >=
                K98RetrySchedule.ReadTimeoutMilliseconds(attempt - 1));
        }
    }

    [Fact]
    public void ReadTimeouts_StayWithinTotalBudget()
    {
        var total = 0;
        for (var attempt = 0; attempt < K98RetrySchedule.AttemptCount; attempt++)
        {
            total += K98RetrySchedule.ReadTimeoutMilliseconds(attempt);
        }

        Assert.True(
            total <= K98RetrySchedule.TotalBudgetMilliseconds,
            $"读超时总和 {total} 超出预算 {K98RetrySchedule.TotalBudgetMilliseconds}");
    }

    [Fact]
    public void ColdStart_UsesMoreAttemptsAndBudget()
    {
        Assert.Equal(6, K98RetrySchedule.AttemptCountFor(coldStart: true));
        Assert.Equal(4, K98RetrySchedule.AttemptCountFor(coldStart: false));
        Assert.Equal(10000, K98RetrySchedule.TotalBudgetMillisecondsFor(coldStart: true));
        Assert.Equal(6500, K98RetrySchedule.TotalBudgetMillisecondsFor(coldStart: false));
    }

    [Fact]
    public void ColdStartReadTimeouts_StayWithinColdStartBudget()
    {
        var total = 0;
        var attempts = K98RetrySchedule.AttemptCountFor(coldStart: true);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            total += K98RetrySchedule.ReadTimeoutMilliseconds(attempt, coldStart: true);
        }

        Assert.True(
            total <= K98RetrySchedule.TotalBudgetMillisecondsFor(coldStart: true),
            $"冷启动读超时总和 {total} 超出预算 {K98RetrySchedule.TotalBudgetMillisecondsFor(coldStart: true)}");
    }

    [Fact]
    public void ColdStartReadTimeouts_AreNonDecreasing()
    {
        var attempts = K98RetrySchedule.AttemptCountFor(coldStart: true);
        for (var attempt = 1; attempt < attempts; attempt++)
        {
            Assert.True(
                K98RetrySchedule.ReadTimeoutMilliseconds(attempt, coldStart: true) >=
                K98RetrySchedule.ReadTimeoutMilliseconds(attempt - 1, coldStart: true));
        }
    }

    [Fact]
    public void RetryDelay_IncreasesWithAttempt()
    {
        // 最后一次尝试后不再退避，退避表比尝试次数少一项。
        for (var attempt = 1; attempt < K98RetrySchedule.AttemptCount - 1; attempt++)
        {
            Assert.True(
                K98RetrySchedule.RetryDelayMilliseconds(attempt) >
                K98RetrySchedule.RetryDelayMilliseconds(attempt - 1));
        }
    }

    [Fact]
    public void OutOfRangeAttempt_ClampsToLastValue()
    {
        var lastReadTimeout = K98RetrySchedule.ReadTimeoutMilliseconds(
            K98RetrySchedule.AttemptCount - 1);
        var lastRetryDelay = K98RetrySchedule.RetryDelayMilliseconds(
            K98RetrySchedule.AttemptCount - 1);

        Assert.Equal(lastReadTimeout, K98RetrySchedule.ReadTimeoutMilliseconds(99));
        Assert.Equal(lastRetryDelay, K98RetrySchedule.RetryDelayMilliseconds(99));
        Assert.Equal(
            K98RetrySchedule.ReadTimeoutMilliseconds(0),
            K98RetrySchedule.ReadTimeoutMilliseconds(-1));
    }
}
