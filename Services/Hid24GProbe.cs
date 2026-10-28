using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 按厂商协议分派 2.4G 电量读取。不同厂商的命令与响应格式不得互相复用。
/// </summary>
public sealed class Hid24GProbe
{
    private readonly Rapoo24GProbe _rapoo = new();
    private readonly K98_24GProbe _k98 = new();

    /// <param name="coldStart">
    /// 透传给厂商探针；仅 K98 用它触发首轮预热与更宽预算，其他协议忽略。
    /// </param>
    public BatteryProbeResult ReadBattery(
        Hid24GDeviceInfo device,
        CancellationToken cancellationToken,
        bool coldStart = false)
    {
        ArgumentNullException.ThrowIfNull(device);

        return device.Protocol switch
        {
            Hid24GProtocol.Rapoo => _rapoo.ReadBattery(device, cancellationToken),
            Hid24GProtocol.LingbaoK98 => _k98.ReadBattery(device, cancellationToken, coldStart),
            _ => BatteryProbeResult.NotSupported(
                "Hid24GProtocol",
                "未识别的 2.4G 设备协议"),
        };
    }
}
