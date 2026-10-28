using System.Globalization;

namespace BlueMeter.Models;

/// <summary>
/// 单台探针的读取健康度：连续失败次数、累计尝试/失败与失败率文本。
/// 连续失败用于把“每轮刷一条 Warn”降噪为首次告警，累计数字用于验收读取稳定性。
/// </summary>
public sealed class ProbeHealthTracker
{
    public int ConsecutiveFailures { get; private set; }

    public long TotalAttempts { get; private set; }

    public long TotalFailures { get; private set; }

    /// <summary>成功次数/总尝试次数 + 失败率，例如 “12/16，失败率 25.0%”。</summary>
    public string SuccessRateText =>
        TotalAttempts == 0
            ? "暂无统计"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{TotalAttempts - TotalFailures}/{TotalAttempts}，失败率 {(double)TotalFailures / TotalAttempts * 100:0.0}%");

    /// <summary>记录一次失败；返回 true 表示这是连续失败的第一轮，应当记录为 Warn。</summary>
    public bool RecordFailure()
    {
        TotalAttempts++;
        TotalFailures++;
        ConsecutiveFailures++;
        return ConsecutiveFailures == 1;
    }

    /// <summary>记录一次成功；返回 true 表示此前处于连续失败状态，已经恢复。</summary>
    public bool RecordSuccess()
    {
        TotalAttempts++;
        var recovered = ConsecutiveFailures > 0;
        ConsecutiveFailures = 0;
        return recovered;
    }
}
