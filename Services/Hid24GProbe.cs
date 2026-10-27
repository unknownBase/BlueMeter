using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 按厂商协议分派 2.4G 电量读取。不同厂商的命令与响应格式不得互相复用。
/// </summary>
public sealed class Hid24GProbe
{
    private readonly Rapoo24GProbe _rapoo = new();
    private readonly K98_24GProbe _k98 = new();

    public BatteryProbeResult ReadBattery(
        Hid24GDeviceInfo device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        return device.Protocol switch
        {
            Hid24GProtocol.Rapoo => _rapoo.ReadBattery(device, cancellationToken),
            Hid24GProtocol.LingbaoK98 => _k98.ReadBattery(device, cancellationToken),
            _ => BatteryProbeResult.NotSupported(
                "Hid24GProtocol",
                "未识别的 2.4G 设备协议"),
        };
    }
}
