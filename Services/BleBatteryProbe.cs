using BlueMeter.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BlueMeter.Services;

/// <summary>
/// 负责单台 BLE 设备的 GATT 电量读取与通知订阅。
/// 同一时刻由 <see cref="DeviceBatteryService"/> 串行调用，避免 GATT 操作互相抢占。
/// </summary>
public sealed class BleBatteryProbe : IDisposable
{
    private const int MaxSubscriptions = 16;
    private static readonly TimeSpan StaleSubscriptionTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SubscriptionWriteTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(1);

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, BleSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public event EventHandler<BleBatteryUpdate>? BatteryUpdated;

    public int SubscriptionCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _sessions.Count;
            }
        }
    }

    public async Task<BatteryProbeResult> ReadAsync(
        string deviceId,
        bool allowSubscription,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        if (allowSubscription)
        {
            await ReleaseStaleSubscriptionsAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // 设置关闭通知订阅后，立即释放可能仍在的旧会话，再走一次性读取。
            await ReleaseDeviceAsync(deviceId).ConfigureAwait(false);
        }

        var session = TryTakeSession(deviceId);
        if (session is not null)
        {
            if (session.IsDisposed || session.Device.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                RemoveSession(deviceId, session);
                await ReleaseSessionCoreAsync(session).ConfigureAwait(false);
            }
            else
            {
                var existingResult = await ReadCharacteristicAsync(
                    session.Characteristic,
                    session.SupportsNotifications,
                    session.Exponent,
                    cancellationToken).ConfigureAwait(false);

                if (existingResult.IsSuccess)
                {
                    return existingResult;
                }

                // 已缓存会话读取失败时释放它，让下一轮从全新连接开始。
                RemoveSession(deviceId, session);
                await ReleaseSessionCoreAsync(session).ConfigureAwait(false);
                return existingResult;
            }
        }

        if (!allowSubscription)
        {
            return await ReadTransientAsync(deviceId, cancellationToken).ConfigureAwait(false);
        }

        return await ReadAndSubscribeAsync(deviceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReleaseDeviceAsync(string deviceId)
    {
        BleSession? session;
        lock (_syncRoot)
        {
            if (!_sessions.Remove(deviceId, out session))
            {
                return;
            }
        }

        await ReleaseSessionCoreAsync(session).ConfigureAwait(false);
    }

    public async Task ReleaseAllAsync()
    {
        List<BleSession> sessions;
        lock (_syncRoot)
        {
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }

        await Task.WhenAll(sessions.Select(ReleaseSessionCoreAsync)).ConfigureAwait(false);
    }

    public async Task ReleaseStaleSubscriptionsAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        List<string> staleDeviceIds;
        lock (_syncRoot)
        {
            staleDeviceIds = _sessions
                .Where(pair => now - pair.Value.LastNotificationAt > StaleSubscriptionTimeout)
                .Select(pair => pair.Key)
                .ToList();
        }

        foreach (var deviceId in staleDeviceIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            AppLog.Debug($"通知订阅超过 {StaleSubscriptionTimeout.TotalMinutes:0} 分钟无更新，释放 {SafeId(deviceId)}", "BleProbe");
            await ReleaseDeviceAsync(deviceId).ConfigureAwait(false);
        }
    }

    private async Task<BatteryProbeResult> ReadTransientAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        BluetoothLEDevice? device = null;

        try
        {
            device = await BluetoothLEDevice.FromIdAsync(deviceId).AsTask(cancellationToken).ConfigureAwait(false);
            if (device is null)
            {
                return BatteryProbeResult.Unavailable(
                    "FromIdAsync",
                    "系统未返回 BluetoothLEDevice");
            }

            return await ReadOnceAsync(device, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BatteryProbeResult.Unavailable(
                "ReadTransient",
                ex.Message,
                ex.HResult);
        }
        finally
        {
            DisposeQuietly(device);
        }
    }

    /// <summary>
    /// 按蓝牙地址读取电池服务。用于双模耳机等“经典蓝牙配对、但电量只从 BLE 侧 GATT 上报”的设备：
    /// 这类设备不会出现在已配对 BLE 列表里，只能通过地址打开 LE 通道。
    /// </summary>
    public async Task<BatteryProbeResult> ReadByBluetoothAddressAsync(
        ulong address,
        CancellationToken cancellationToken)
    {
        BluetoothLEDevice? device = null;

        try
        {
            device = await BluetoothLEDevice
                .FromBluetoothAddressAsync(address)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (device is null)
            {
                return BatteryProbeResult.NotSupported(
                    "FromBluetoothAddressAsync",
                    "系统未提供该地址的 BLE 通道");
            }

            return await ReadOnceAsync(device, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BatteryProbeResult.Unavailable(
                "ReadByAddress",
                ex.Message,
                ex.HResult);
        }
        finally
        {
            DisposeQuietly(device);
        }
    }

    private static async Task<BatteryProbeResult> ReadOnceAsync(
        BluetoothLEDevice device,
        CancellationToken cancellationToken)
    {
        GattDeviceService? service = null;

        try
        {
            var servicesResult = await device
                .GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                return BatteryProbeResult.Unavailable(
                    "GetGattServicesForUuidAsync",
                    servicesResult.Status.ToString());
            }

            service = servicesResult.Services.FirstOrDefault();
            if (service is null)
            {
                return BatteryProbeResult.NotSupported(
                    "GetGattServicesForUuidAsync",
                    "未发现电池服务 0x180F");
            }

            var characteristicsResult = await service
                .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (characteristicsResult.Status != GattCommunicationStatus.Success)
            {
                return BatteryProbeResult.Unavailable(
                    "GetCharacteristicsForUuidAsync",
                    characteristicsResult.Status.ToString());
            }

            var characteristic = characteristicsResult.Characteristics.FirstOrDefault();
            if (characteristic is null)
            {
                return BatteryProbeResult.NotSupported(
                    "GetCharacteristicsForUuidAsync",
                    "未发现电量特征值 0x2A19");
            }

            var supportsNotifications = SupportsNotifications(characteristic);
            var exponent = GetExponent(characteristic);
            return await ReadCharacteristicAsync(
                characteristic,
                supportsNotifications,
                exponent,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DisposeQuietly(service);
        }
    }

    private async Task<BatteryProbeResult> ReadAndSubscribeAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        BluetoothLEDevice? device = null;
        GattDeviceService? service = null;
        GattCharacteristic? characteristic = null;
        TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? valueChangedHandler = null;
        TypedEventHandler<BluetoothLEDevice, object>? connectionHandler = null;
        BleSession? session = null;
        var transferred = false;

        try
        {
            device = await BluetoothLEDevice.FromIdAsync(deviceId).AsTask(cancellationToken).ConfigureAwait(false);
            if (device is null)
            {
                return BatteryProbeResult.Unavailable(
                    "FromIdAsync",
                    "系统未返回 BluetoothLEDevice");
            }

            var servicesResult = await device
                .GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                return BatteryProbeResult.Unavailable(
                    "GetGattServicesForUuidAsync",
                    servicesResult.Status.ToString());
            }

            service = servicesResult.Services.FirstOrDefault();
            if (service is null)
            {
                return BatteryProbeResult.NotSupported(
                    "GetGattServicesForUuidAsync",
                    "未发现电池服务 0x180F");
            }

            var characteristicsResult = await service
                .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (characteristicsResult.Status != GattCommunicationStatus.Success)
            {
                return BatteryProbeResult.Unavailable(
                    "GetCharacteristicsForUuidAsync",
                    characteristicsResult.Status.ToString());
            }

            characteristic = characteristicsResult.Characteristics.FirstOrDefault();
            if (characteristic is null)
            {
                return BatteryProbeResult.NotSupported(
                    "GetCharacteristicsForUuidAsync",
                    "未发现电量特征值 0x2A19");
            }

            var supportsNotifications = SupportsNotifications(characteristic);
            var exponent = GetExponent(characteristic);
            var readResult = await ReadCharacteristicAsync(
                characteristic,
                supportsNotifications,
                exponent,
                cancellationToken).ConfigureAwait(false);

            if (!readResult.IsSuccess || !supportsNotifications || IsAtSubscriptionLimit())
            {
                return readResult;
            }

            var descriptorValue = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                : GattClientCharacteristicConfigurationDescriptorValue.Indicate;

            session = new BleSession(
                deviceId,
                device,
                service,
                characteristic,
                supportsNotifications,
                exponent);

            valueChangedHandler = (_, args) => OnValueChanged(session, args);
            connectionHandler = (_, _) => OnConnectionStatusChanged(session);
            session.ValueChangedHandler = valueChangedHandler;
            session.ConnectionHandler = connectionHandler;

            characteristic.ValueChanged += valueChangedHandler;
            device.ConnectionStatusChanged += connectionHandler;

            using var subscriptionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            subscriptionCts.CancelAfter(SubscriptionWriteTimeout);

            GattCommunicationStatus cccdStatus;
            try
            {
                cccdStatus = await characteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(descriptorValue)
                    .AsTask(subscriptionCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                cccdStatus = GattCommunicationStatus.Unreachable;
            }

            if (cccdStatus != GattCommunicationStatus.Success)
            {
                TryRemoveHandlers(characteristic, device, valueChangedHandler, connectionHandler);
                AppLog.Warn(
                    $"通知订阅失败：{SafeId(deviceId)}，{cccdStatus}",
                    "BleProbe");
                return readResult;
            }

            lock (_syncRoot)
            {
                if (_sessions.Count >= MaxSubscriptions)
                {
                    TryRemoveHandlers(characteristic, device, valueChangedHandler, connectionHandler);
                    return readResult;
                }

                _sessions[deviceId] = session;
            }

            session.LastNotificationAt = DateTimeOffset.UtcNow;
            transferred = true;
            AppLog.Debug($"已订阅电量通知：{SafeId(deviceId)}", "BleProbe");
            return readResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (characteristic is not null && device is not null && valueChangedHandler is not null && connectionHandler is not null)
            {
                TryRemoveHandlers(characteristic, device, valueChangedHandler, connectionHandler);
            }

            return BatteryProbeResult.Unavailable(
                "ReadAndSubscribe",
                ex.Message,
                ex.HResult);
        }
        finally
        {
            if (!transferred)
            {
                DisposeQuietly(service);
                DisposeQuietly(device);
            }
        }
    }

    private static async Task<BatteryProbeResult> ReadCharacteristicAsync(
        GattCharacteristic characteristic,
        bool supportsNotifications,
        int exponent,
        CancellationToken cancellationToken)
    {
        try
        {
            var readResult = await characteristic
                .ReadValueAsync(BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (readResult.Status != GattCommunicationStatus.Success)
            {
                return BatteryProbeResult.Unavailable(
                    "ReadValueAsync",
                    readResult.Status.ToString(),
                    supportsNotifications: supportsNotifications);
            }

            var percent = TryParseBatteryValue(readResult.Value, exponent);
            if (percent is null)
            {
                return BatteryProbeResult.Unavailable(
                    "ParseValue",
                    "电量值无效或超出 0–100",
                    supportsNotifications: supportsNotifications);
            }

            return BatteryProbeResult.Ok(percent.Value, supportsNotifications);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BatteryProbeResult.Unavailable(
                "ReadValueAsync",
                ex.Message,
                ex.HResult,
                supportsNotifications);
        }
    }

    private void OnValueChanged(BleSession session, GattValueChangedEventArgs args)
    {
        if (session.IsDisposed)
        {
            return;
        }

        var percent = TryParseBatteryValue(args.CharacteristicValue, session.Exponent);
        if (percent is null)
        {
            AppLog.Warn($"忽略无效的 BLE 电量通知：{SafeId(session.DeviceId)}", "BleProbe");
            return;
        }

        session.LastNotificationAt = DateTimeOffset.UtcNow;

        var handler = BatteryUpdated;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new BleBatteryUpdate(session.DeviceId, percent.Value, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            AppLog.Error($"处理 BLE 电量通知失败：{SafeId(session.DeviceId)}", ex, "BleProbe");
        }
    }

    private void OnConnectionStatusChanged(BleSession session)
    {
        if (session.IsDisposed ||
            session.Device.ConnectionStatus != BluetoothConnectionStatus.Disconnected)
        {
            return;
        }

        AppLog.Debug($"BLE 设备断开，释放通知订阅：{SafeId(session.DeviceId)}", "BleProbe");
        _ = ReleaseDeviceAsync(session.DeviceId).ContinueWith(
            task =>
            {
                if (task.Exception is not null)
                {
                    AppLog.Warn(
                        $"释放断连订阅失败：{SafeId(session.DeviceId)}",
                        "BleProbe",
                        task.Exception.GetBaseException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private BleSession? TryTakeSession(string deviceId)
    {
        lock (_syncRoot)
        {
            return _sessions.TryGetValue(deviceId, out var session) ? session : null;
        }
    }

    private void RemoveSession(string deviceId, BleSession session)
    {
        lock (_syncRoot)
        {
            if (_sessions.TryGetValue(deviceId, out var current) && ReferenceEquals(current, session))
            {
                _sessions.Remove(deviceId);
            }
        }
    }

    private bool IsAtSubscriptionLimit()
    {
        lock (_syncRoot)
        {
            return _sessions.Count >= MaxSubscriptions;
        }
    }

    private static bool SupportsNotifications(GattCharacteristic characteristic)
    {
        var properties = characteristic.CharacteristicProperties;
        return properties.HasFlag(GattCharacteristicProperties.Notify) ||
               properties.HasFlag(GattCharacteristicProperties.Indicate);
    }

    private static int GetExponent(GattCharacteristic characteristic)
    {
        try
        {
            var formats = characteristic.PresentationFormats;
            return formats.Count > 0 ? formats[0].Exponent : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static int? TryParseBatteryValue(IBuffer? buffer, int exponent)
    {
        if (buffer is null || buffer.Length == 0)
        {
            return null;
        }

        try
        {
            using var reader = DataReader.FromBuffer(buffer);
            if (reader.UnconsumedBufferLength == 0)
            {
                return null;
            }

            var raw = reader.ReadByte();
            var scaled = exponent == 0
                ? raw
                : raw * Math.Pow(10, exponent);

            if (double.IsNaN(scaled) || double.IsInfinity(scaled))
            {
                return null;
            }

            var rounded = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
            return rounded is >= 0 and <= 100 ? rounded : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task ReleaseSessionCoreAsync(BleSession session)
    {
        if (Interlocked.Exchange(ref session.DisposedFlag, 1) != 0)
        {
            return;
        }

        TryRemoveHandlers(
            session.Characteristic,
            session.Device,
            session.ValueChangedHandler,
            session.ConnectionHandler);

        try
        {
            using var cts = new CancellationTokenSource(ReleaseTimeout);
            await session.Characteristic
                .WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None)
                .AsTask(cts.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // 断开或退出时 CCCD 写入失败不影响释放。
        }

        DisposeQuietly(session.Service);
        DisposeQuietly(session.Device);
    }

    private static void TryRemoveHandlers(
        GattCharacteristic characteristic,
        BluetoothLEDevice device,
        TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> valueChangedHandler,
        TypedEventHandler<BluetoothLEDevice, object> connectionHandler)
    {
        try
        {
            characteristic.ValueChanged -= valueChangedHandler;
        }
        catch
        {
            // 已释放的特征值忽略。
        }

        try
        {
            device.ConnectionStatusChanged -= connectionHandler;
        }
        catch
        {
            // 已释放的设备忽略。
        }
    }

    private static void DisposeQuietly(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch
        {
            // 释放失败不能影响调度循环。
        }
    }

    private static string SafeId(string deviceId) =>
        deviceId.Length <= 28
            ? deviceId
            : $"{deviceId[..12]}…{deviceId[^10..]}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        List<BleSession> sessions;
        lock (_syncRoot)
        {
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }

        foreach (var session in sessions)
        {
            if (Interlocked.Exchange(ref session.DisposedFlag, 1) != 0)
            {
                continue;
            }

            TryRemoveHandlers(
                session.Characteristic,
                session.Device,
                session.ValueChangedHandler,
                session.ConnectionHandler);
            DisposeQuietly(session.Service);
            DisposeQuietly(session.Device);
        }
    }

    private sealed class BleSession
    {
        public BleSession(
            string deviceId,
            BluetoothLEDevice device,
            GattDeviceService service,
            GattCharacteristic characteristic,
            bool supportsNotifications,
            int exponent)
        {
            DeviceId = deviceId;
            Device = device;
            Service = service;
            Characteristic = characteristic;
            SupportsNotifications = supportsNotifications;
            Exponent = exponent;
            LastNotificationAt = DateTimeOffset.UtcNow;

            ValueChangedHandler = (_, args) => { };
            ConnectionHandler = (_, _) => { };
        }

        public string DeviceId { get; }

        public BluetoothLEDevice Device { get; }

        public GattDeviceService Service { get; }

        public GattCharacteristic Characteristic { get; }

        public bool SupportsNotifications { get; }

        public int Exponent { get; }

        public DateTimeOffset LastNotificationAt { get; set; }

        public int DisposedFlag;

        public bool IsDisposed => Volatile.Read(ref DisposedFlag) != 0;

        public TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> ValueChangedHandler { get; set; }

        public TypedEventHandler<BluetoothLEDevice, object> ConnectionHandler { get; set; }
    }
}

public sealed record BleBatteryUpdate(
    string DeviceId,
    int BatteryPercent,
    DateTimeOffset Timestamp);
