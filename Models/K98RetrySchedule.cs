namespace BlueMeter.Models;

/// <summary>
/// K98 2.4G 电量查询的重试计划。
/// 键盘省电时会丢弃单条查询，因此用递增的读超时、尝试间唤醒与尝试内快速重发提高命中率，
/// 同时用总预算限制单台设备的耗时，避免挤占整轮刷新。
/// </summary>
public static class K98RetrySchedule
{
    public const int AttemptCount = 4;

    public const int WriteTimeoutMilliseconds = 500;

    public const int WakeReadTimeoutMilliseconds = 500;

    public const int QuickResendIntervalMilliseconds = 400;

    public const int MaxResendPerAttempt = 1;

    /// <summary>单台 K98 的读取硬上限，包含读、退避与唤醒。</summary>
    public const int TotalBudgetMilliseconds = 6500;

    private static readonly int[] ReadTimeouts = { 1000, 1400, 1800, 1800 };

    private static readonly int[] RetryDelays = { 100, 200, 300 };

    /// <summary>第 <paramref name="attempt"/> 次尝试（从 0 开始）的读超时；越界取最后一个值。</summary>
    public static int ReadTimeoutMilliseconds(int attempt) =>
        ReadTimeouts[Clamp(attempt, ReadTimeouts.Length)];

    /// <summary>第 <paramref name="attempt"/> 次尝试失败后的退避时间；越界取最后一个值。</summary>
    public static int RetryDelayMilliseconds(int attempt) =>
        RetryDelays[Clamp(attempt, RetryDelays.Length)];

    private static int Clamp(int attempt, int length) =>
        attempt < 0 ? 0 : attempt >= length ? length - 1 : attempt;
}
