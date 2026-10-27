using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 合并各厂商独立 2.4G 枚举器的结果。
/// 各厂商协议保持独立；这里只负责合并设备列表与状态，不共享命令格式。
/// </summary>
public sealed class Hid24GDeviceEnumerator
{
    private readonly Rapoo24GDeviceEnumerator _rapoo = new();
    private readonly K98_24GDeviceEnumerator _k98 = new();

    public Hid24GEnumerationResult Enumerate(CancellationToken cancellationToken)
    {
        var rap = _rapoo.Enumerate(cancellationToken);
        var k98 = _k98.Enumerate(cancellationToken);

        var devices = new List<Hid24GDeviceInfo>(rap.Devices.Count + k98.Devices.Count);
        devices.AddRange(rap.Devices);
        devices.AddRange(k98.Devices);

        var succeeded = rap.Succeeded || k98.Succeeded;
        var failedPathCount = (rap.Succeeded ? 0 : 1) + (k98.Succeeded ? 0 : 1);
        var errors = new[] { rap.Error, k98.Error }
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .ToArray();

        AppLog.Debug(
            $"2.4G 枚举完成：Rapoo {rap.Devices.Count} 台，LINGBAO K98 {k98.Devices.Count} 台",
            "Hid24G");

        return new Hid24GEnumerationResult(
            devices,
            succeeded,
            errors.Length > 0 ? string.Join("; ", errors) : null,
            rap.MatchingInterfaceCount + k98.MatchingInterfaceCount,
            rap.FailedInterfaceCount + k98.FailedInterfaceCount + failedPathCount);
    }
}
