namespace BlueMeter.Models;

/// <summary>
/// K98 2.4G 电量查询的重试计划。
/// 键盘省电时会丢弃单条查询，因此用递增的读超时、尝试间唤醒与尝试内快速重发提高命中率，
/// 同时用总预算限制单台设备的耗时，避免挤占整轮刷新。
/// 进程启动后的冷启动阶段，接收器刚被系统枚举、尚未被叫醒，用更多尝试与更长预算兜住这一窗口；
/// 一旦成功读到电量就回到常规计划。
/// </summary>
public static class K98RetrySchedule
{
    public const int AttemptCount = 4;

    /// <summary>冷启动阶段的尝试次数；进程内尚未成功读到电量时使用。</summary>
    public const int ColdStartAttemptCount = 6;

    public const int WriteTimeoutMilliseconds = 500;

    public const int WakeReadTimeoutMilliseconds = 500;

    public const int QuickResendIntervalMilliseconds = 400;

    public const int MaxResendPerAttempt = 1;

    /// <summary>单台 K98 的读取硬上限，包含预热唤醒、读、退避与尝试间唤醒。</summary>
    public const int TotalBudgetMilliseconds = 6500;

    /// <summary>冷启动阶段的读取硬上限；接收器被系统枚举后需要更长时间才能响应。</summary>
    public const int ColdStartTotalBudgetMilliseconds = 10000;

    private static readonly int[] ReadTimeouts = { 1000, 1400, 1800, 1800 };

    private static readonly int[] ColdStartReadTimeouts = { 1000, 1200, 1400, 1800, 1800, 1800 };

    private static readonly int[] RetryDelays = { 100, 200, 300 };

    private static readonly int[] ColdStartRetryDelays = { 100, 200, 300, 400, 500 };

    /// <summary>该阶段使用的尝试次数。</summary>
    public static int AttemptCountFor(bool coldStart) =>
        coldStart ? ColdStartAttemptCount : AttemptCount;

    /// <summary>该阶段单台设备的读取硬上限。</summary>
    public static int TotalBudgetMillisecondsFor(bool coldStart) =>
        coldStart ? ColdStartTotalBudgetMilliseconds : TotalBudgetMilliseconds;

    /// <summary>第 <paramref name="attempt"/> 次尝试（从 0 开始）的读超时；越界取最后一个值。</summary>
    public static int ReadTimeoutMilliseconds(int attempt, bool coldStart = false)
    {
        var timeouts = coldStart ? ColdStartReadTimeouts : ReadTimeouts;
        return timeouts[Clamp(attempt, timeouts.Length)];
    }

    /// <summary>第 <paramref name="attempt"/> 次尝试失败后的退避时间；越界取最后一个值。</summary>
    public static int RetryDelayMilliseconds(int attempt, bool coldStart = false)
    {
        var delays = coldStart ? ColdStartRetryDelays : RetryDelays;
        return delays[Clamp(attempt, delays.Length)];
    }

    private static int Clamp(int attempt, int length) =>
        attempt < 0 ? 0 : attempt >= length ? length - 1 : attempt;
}