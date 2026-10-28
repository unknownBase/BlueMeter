# BlueMeter 架构设计

本文描述 BlueMeter 一期的运行时结构、数据流、蓝牙读取策略、状态语义、线程模型与异常兜底。实现时以本文为准；若实现不得不偏离，请同步更新本文与 [ROADMAP.md](../ROADMAP.md)。

---

## 1. 目标与约束

### 目标

- 常驻托盘，关闭主窗口不退出。
- 汇总本机**已配对** BLE/经典蓝牙设备，以及已确认的 Rapoo / LINGBAO K98 2.4G HID 接收器设备的电量。
- 设备不稳定（休眠、走远、关机、被独占）时保持进程稳定，保留上次成功值并诚实标注。
- Fluent UI 跟随系统主题、强调色与背景材质。
- 低频、串行、可控的蓝牙与 HID 访问，避免连接风暴与额外耗电。

### 硬约束

- .NET 8，`net8.0-windows10.0.19041.0`，`SupportedOSPlatformVersion=10.0.17763.0`。
- WPF-UI `4.3.0`；H.NotifyIcon.Wpf `2.3.2`；不使用 WinForms `NotifyIcon`。
- 一期不实现经典蓝牙 RFCOMM `IPHONEACCEV`。
- 不引入第二套 UI 框架或额外的蓝牙封装库。
- 2.4G 支持仅限已确认的 Rapoo `24AE:1861/col05` 只读报告与 LINGBAO K98 `3554:FA09/col01` 固定只读查询；不发送配置/设置写入，不承诺通用 2.4G/USB 接收器。

---

## 2. 系统上下文

```text
┌──────────────────────────────────────────────────────────────────┐
│ Windows Shell                                                    │
│  ├─ 通知区域（托盘图标、ToolTip、气泡通知）                       │
│  ├─ 设置 → 蓝牙和设备（配对关系）                                 │
│  └─ HID 设备接口（Rapoo / LINGBAO K98 2.4G 接收器）               │
└───────────────▲───────────────────────────────▲──────────────────┘
                │ Shell_NotifyIcon             │ 配对属性 / HID 接口
┌───────────────┴───────────────────────────────┴──────────────────┐
│ BlueMeter.exe                                                    │
│  ├─ WPF UI（FluentWindow / 设备列表 / 设置）                      │
│  ├─ 托盘服务（H.NotifyIcon.TaskbarIcon）                          │
│  └─ 设备服务（WinRT 枚举 + GATT / 设备属性 + HID 只读报告）       │
└───────────────▲───────────────────────────────▲──────────────────┘
                │ WinRT / SetupAPI              │ 设备属性 / HID report
┌───────────────┴───────────────────────────────┴──────────────────┐
│ Windows 设备栈                                                   │
│  ├─ BLE：GATT 0x180F / 0x2A19 / 0x2904 / CCCD                    │
│  ├─ BR/EDR：系统设备属性 System.Devices.BatteryLife              │
│  └─ 2.4G：Rapoo 24AE:1861/col05 → HidD_GetInputReport(0xBB)      │
│            LINGBAO K98 3554:FA09/col01 → 固定只读查询 13 4A      │
└──────────────────────────────────────────────────────────────────┘
```

BlueMeter 不访问网络，不启动后台服务，不安装驱动。

---

## 3. 分层与组件

```text
┌──────────────────────────── UI 线程（STA） ────────────────────────────┐
│                                                                        │
│  App.xaml.cs（组合根）                                                 │
│    ├─ 单实例 Mutex                                                     │
│    ├─ SettingsService / AppLog                                         │
│    ├─ DeviceBatteryService ──────────────┐                          │
│    ├─ TrayIconService ── TaskbarIcon        │                          │
│    └─ MainWindow ── DeviceItemViewModel[]                            │
│                         ▲                   │                          │
│                         │ Dispatcher 编组   │ DevicesUpdated           │
└─────────────────────────┼───────────────────┼──────────────────────────┘
                          │                   │
┌─────────────────────────┴───────────────────▼──────────────────────────┐
│ 后台线程 / 线程池                                                      │
│  DeviceBatteryService（单轮循环、SemaphoreSlim 串行、轮次编排）         │
│    ├─ BluetoothDeviceEnumerator：蓝牙枚举、属性解析、适配器状态         │
│    ├─ 2.4G 枚举：SetupAPI + HID caps（Rapoo/K98 独立厂商路径）          │
│    ├─ BleBatteryProbe：GATT 读取、CCCD 订阅、ValueChanged              │
│    ├─ Hid24GProbe：按协议分派 Rapoo / K98                             │
│    ├─ Rapoo24GProbe：HidD_GetInputReport(0xBB)，只读                   │
│    ├─ K98_24GProbe：固定 13 4A 只读查询 + overlapped 超时              │
│    └─ BatteryStateStore：状态合并、快照生成、AlertLatch                │
└────────────────────────────────────────────────────────────────────────┘
```

### 组件职责

| 组件 | 职责 | 不负责 |
| --- | --- | --- |
| `App` | 组合根、单实例、全局异常、启动/退出编排 | 蓝牙细节、UI 布局 |
| `TrayIconService` | 创建/释放 `TaskbarIcon`、菜单、左键还原、ToolTip、气泡 | 电量读取 |
| `MainWindow` | Fluent 外壳、关闭隐藏、最小化策略 | 枚举设备 |
| `MainViewModel` | 设备集合、排序、汇总、命令、设置绑定 | 直接调用 WinRT |
| `DeviceBatteryService` | 生命周期、刷新循环与轮次编排、事件发布、失败日志 | 枚举细节、状态字段维护、窗口/托盘 UI |
| `BluetoothDeviceEnumerator` | BLE/经典蓝牙枚举、系统属性解析、按地址去重合并、适配器与无线电状态探测 | GATT 连接与读取、状态存储 |
| `BatteryStateStore` | 设备条目与读数状态、增删去重、探针结果应用、低电量判定、快照生成 | 枚举与读取、事件发布、调度 |
| `BleBatteryProbe` | 单台 BLE 设备的 GATT 读取与通知订阅 | 全局调度 |
| `ClassicBatteryProbe` | 单台经典设备的系统属性读取 | GATT |
| `Rapoo24GDeviceEnumerator` | 枚举 Rapoo 2.4G `24AE:1861` 的 `MI_01/COL05` HID 接口、校验 caps | 读取电量 |
| `Rapoo24GProbe` | `HidD_GetInputReport(0xBB)` 只读电量解析 | 发送配置/输出报告 |
| `K98_24GDeviceEnumerator` | 枚举 LINGBAO K98 2.4G `3554:FA09` 的 `MI_01/COL01` HID 接口、校验 caps | 读取电量 |
| `K98_24GProbe` | 固定 `13 4A` 只读查询、校验 checksum、解析电量/充电状态；`K98RetrySchedule` 控制重试（4 次尝试、递增读超时、重发前 `13 07` 唤醒、6.5 秒预算） | 写配置、feature `0x06` 猜测 |
| `Hid24GDeviceEnumerator` | 合并 Rapoo/K98 枚举结果，保留单厂商失败状态 | 解析电量 |
| `Hid24GProbe` | 按 `Hid24GProtocol` 分派厂商探针 | 共享命令格式 |
| `HidInterop` | SetupAPI/HID P/Invoke、caps/字符串、K98 固定查询的 overlapped 读写 | 写配置/设置 |
| `SettingsService` | JSON 读写、默认值、校验、变更事件 | UI |
| `AppLog` | 文件日志、滚动、脱敏 | 业务判断 |
| `ProbeHealthTracker` | 单台探针的连续失败与累计成功率；HID 失败日志降噪与轮次统计 | 业务读取逻辑 |

> 当前 `0.3.1` 已实现 `DeviceBatteryService`、`BleBatteryProbe`、`Rapoo24GDeviceEnumerator`、`Rapoo24GProbe`、`K98_24GDeviceEnumerator`、`K98_24GProbe`、合并分派器与 M2 领域模型；主窗口暂用轻量预览列表，`MainViewModel`、`ClassicBatteryProbe`、`BatteryBar` 等留到 M3/M4。

---

## 4. 项目结构与命名空间

```text
BlueMeter/
├─ App.xaml(.cs)                 namespace BlueMeter
├─ MainWindow.xaml(.cs)          namespace BlueMeter
├─ Models/                       namespace BlueMeter.Models
│  ├─ DeviceTransportKind.cs
│  ├─ Hid24GDeviceInfo.cs
│  ├─ DeviceServiceStatus.cs
│  ├─ BatteryReadStatus.cs
│  ├─ DeviceBatteryState.cs
│  ├─ DeviceBatterySnapshot.cs
│  ├─ BatteryProbeResult.cs
│  ├─ K98RetrySchedule.cs
│  ├─ ProbeHealthTracker.cs
│  ├─ RefreshIntervalConverter.cs
│  └─ AppSettings.cs
├─ Services/                     namespace BlueMeter.Services
│  ├─ IDeviceBatteryService.cs
│  ├─ DeviceBatteryService.cs
│  ├─ BluetoothDeviceEnumerator.cs
│  ├─ BatteryStateStore.cs
│  ├─ BleBatteryProbe.cs
│  ├─ HidInterop.cs
│  ├─ Hid24GEnumerationResult.cs
│  ├─ Hid24GDeviceEnumerator.cs
│  ├─ Hid24GProbe.cs
│  ├─ Rapoo24GDeviceEnumerator.cs
│  ├─ Rapoo24GProbe.cs
│  ├─ K98_24GDeviceEnumerator.cs
│  ├─ K98_24GProbe.cs
│  ├─ TrayIconService.cs
│  ├─ SettingsService.cs
│  └─ AppLog.cs
├─ ViewModels/                   namespace BlueMeter.ViewModels
│  ├─ ObservableObject.cs
│  ├─ RelayCommand.cs
│  ├─ AsyncRelayCommand.cs
│  ├─ MainViewModel.cs
│  └─ DeviceItemViewModel.cs
├─ Controls/                     namespace BlueMeter.Controls
│  └─ BatteryBar.xaml(.cs)
├─ Converters/                   namespace BlueMeter.Converters
└─ Assets/BlueMeter.ico
```

约定：

- 使用文件范围命名空间（file-scoped namespace）。
- 服务层不引用 `System.Windows`（日志除外），保证可单测。
- ViewModel 不直接调用 WinRT；只消费服务快照。
- 所有公共异步方法带 `CancellationToken`，并以 `Async` 结尾。

---

## 5. 领域模型

### 5.1 枚举

```csharp
public enum DeviceTransportKind
{
    BluetoothLe,
    BluetoothClassic,
    Hid24G
}

public enum BatteryReadStatus
{
    Unknown,        // 尚未读取
    Reading,        // 本轮读取中
    Ok,             // 最近一次读取成功且在新鲜期内
    Stale,          // 曾经成功，但最近一次失败或超过新鲜期（仍有旧值）
    Unavailable,    // 读取失败，且没有可用的历史值
    NotSupported    // 设备未暴露电量来源
}

public enum Hid24GProtocol
{
    Unknown,        // 未识别的 2.4G 接口，不尝试读取
    Rapoo,          // HidD_GetInputReport(0xBB)
    LingbaoK98      // 固定 13 4A 只读查询
}
```

`Hid24GDeviceInfo` 携带 `Protocol`，`Hid24GProbe` 只按该枚举分派到对应厂商探针；协议不从 VID/PID 之外的特征推断，也不在厂商之间复用命令格式。

### 5.2 设备状态

```csharp
public sealed class DeviceBatteryState
{
    public required string Id { get; set; }
    public ulong? Address { get; set; }               // 蓝牙去重键；2.4G 为 null
    public required string Name { get; set; }
    public DeviceTransportKind Transport { get; set; }

    public int? BatteryPercent { get; set; }          // 0–100，null = 未知
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset LastAttemptAt { get; set; }
    public BatteryReadStatus Status { get; set; }

    public bool IsConnected { get; set; }
    public bool SupportsNotifications { get; set; }
    public string? ChargingText { get; set; }

    // 2.4G HID 身份；蓝牙设备为 null
    public ushort? VendorId { get; set; }
    public ushort? ProductId { get; set; }
    public string? InterfacePath { get; set; }
    public string? ReceiverId { get; set; }
    public string? VendorName { get; set; }

    public bool LowBatteryAlerted { get; set; }
}
```

对外快照使用不可变记录，避免 UI 读到半更新状态：

```csharp
public sealed record DeviceBatterySnapshot(
    string Id,
    ulong? Address,
    string Name,
    DeviceTransportKind Transport,
    int? BatteryPercent,
    BatteryReadStatus Status,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset LastAttemptAt,
    bool IsConnected,
    bool SupportsNotifications,
    string? ChargingText)
{
    public ushort? VendorId { get; init; }
    public ushort? ProductId { get; init; }
    public string? InterfacePath { get; init; }
    public string? ReceiverId { get; init; }
    public string? VendorName { get; init; }
}
```

### 5.3 状态机

```text
                 ┌──────────┐
     枚举到设备 → │ Unknown  │
                 └────┬─────┘
                      │ 开始读取
                 ┌────▼─────┐
                 │ Reading  │
                 └────┬─────┘
          ┌───────────┼───────────────┬────────────────┐
          │ 成功      │ 失败且有旧值  │ 失败且无旧值   │ 无电池来源
     ┌────▼────┐ ┌────▼────┐    ┌────▼──────┐   ┌────▼────────┐
     │   Ok    │ │  Stale  │    │Unavailable│   │ NotSupported│
     └────┬────┘ └────┬────┘    └────┬──────┘   └────┬────────┘
          │ 超期      │ 下轮成功      │ 下轮成功       │ 设备后续暴露服务
          └──────────►└──────────────┴────────────────┘
                （回到 Reading → Ok）
```

补充规则：

- `Stale` 表示“有旧值但不再新鲜”：最近一次读取失败且已有历史值，或 `LastSuccessAt` 超过 3 个刷新周期。它是快照时计算的派生状态。
- `Unavailable` 与 `NotSupported` 的区别：前者“本可以读但这次失败且没有历史值”，后者“该设备没有可读来源”。
- 设备从系统中消失（`FromIdAsync` 返回 null 或枚举不到）：保留 1 轮，下一轮移除；避免闪一下就消失。
- 读取失败不清空 `BatteryPercent`，只更新 `Status`、`LastAttemptAt`。

---

## 6. 设备发现与分类

### 6.1 查询

```csharp
private static readonly string[] PropertyKeys =
{
    "System.Devices.Aep.DeviceAddress",
    "System.Devices.Aep.IsConnected",
    "System.Devices.Aep.Bluetooth.Le.IsConnectable",
    "System.Devices.Aep.Category",
    "System.Devices.BatteryLife",
    "System.Devices.BatteryPlusCharging",
    "System.Devices.BatteryPlusChargingText",
    "System.Devices.Manufacturer",
    "System.Devices.ModelName"
};

// BLE（已配对）
var bleSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
var bleDevices = await DeviceInformation.FindAllAsync(bleSelector, PropertyKeys)
                                   .AsTask(ct);

// 经典蓝牙 BR/EDR（已配对）
var classicSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
var classicDevices = await DeviceInformation.FindAllAsync(classicSelector, PropertyKeys)
                                          .AsTask(ct);
```

要点：

- 使用 `GetDeviceSelectorFromPairingState(true)`，语义是“已配对”，比 `GetDeviceSelector()` 更精确。
- 两个查询都可能返回 0 条；蓝牙关闭时通常返回空集合或抛异常，需作为全局状态处理。
- `DeviceInformation.Name` 可能为空，回退为 `"未知设备"`；不要因此丢弃设备。
- 不调用 `CreateWatcher` 做后台扫描；一期用“定时全量枚举”即可，配对设备数量有限。
- 属性键必须使用规范名称。`System.Devices.Aep.DeviceCategory` 在 Windows 10/11 上会导致 `COMException 0x8002802B`；正确的是 `System.Devices.Aep.Category`。

### 6.2 分类

分类依据是**来源查询**，不是 GATT 内容：

| 来源 | `DeviceTransportKind` | 电量来源 |
| --- | --- | --- |
| `BluetoothLEDevice` 选择器 | `BluetoothLe` | GATT 电池服务 |
| `BluetoothDevice` 选择器 | `BluetoothClassic` | 系统设备属性 |
| Rapoo `24AE:1861/col05` HID 接口 | `Hid24G` | `0xBB` input report（只读） |
| LINGBAO K98 `3554:FA09/col01` HID 接口 | `Hid24G` | `13 4A` 固定只读查询 |

“是否支持电量”在读取阶段判定：BLE 找 `0x180F`，经典找 `BatteryLife` 属性；2.4G 仅对已确认的 Rapoo/K98 接口使用各自协议。遍历 GATT 全表只用于诊断，不作为常规分类手段（开销大）。

2.4G 不参与蓝牙地址去重，也不与同一物理鼠标的 BLE/经典条目合并：它们是不同的连接方式，应分别显示。

### 6.3 去重

同一物理设备可能同时出现在 BLE 与经典列表（例如支持双模的鼠标）。

1. 优先解析 `System.Devices.Aep.DeviceAddress` 为 `ulong`（蓝牙地址）。
2. 有地址：按地址分组，保留一个逻辑设备，`Transport=BluetoothLe`，同时保留经典条目作为回退来源。
3. 无地址：按 `DeviceInformation.Id` 保留；无法判断重复时保留两条，并在 UI 中显示类型，宁可多显示也不误合并。
4. 名称相同但地址不同：绝不合并。

合并读取策略：

```text
if (hasBleEntry)
    result = BleProbe.Read(bleEntry)
    if (result is NotSupported or Unavailable with no value)
        result = ClassicProbe.Read(classicEntry)   // 回退
else
    result = ClassicProbe.Read(classicEntry)
```

---

## 7. BLE 电量读取

### 7.1 标准 UUID

| 对象 | UUID | 说明 |
| --- | --- | --- |
| 电池服务 | `0x180F` | `GattServiceUuids.Battery` |
| 电量特征值 | `0x2A19` | `GattCharacteristicUuids.BatteryLevel`，`uint8`，0–100 |
| 展示格式描述符 | `0x2904` | 通过 `GattCharacteristic.PresentationFormats` 暴露 |
| CCCD | `0x2902` | `WriteClientCharacteristicConfigurationDescriptorAsync` |

### 7.2 单次读取流程

```text
1. BluetoothLEDevice.FromIdAsync(info.Id)
      └─ null → 设备已移除/不可用 → Unavailable
2. GetGattServicesForUuidAsync(GattServiceUuids.Battery, Uncached)
      └─ Status != Success 或 Services 为空 → NotSupported / Unavailable
3. GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, Uncached)
      └─ 无特征值 → NotSupported
4. ReadValueAsync(Uncached)
      └─ Status != Success → Unavailable
5. DataReader 读取第一个字节 → 0–100
6. 若有 PresentationFormats 且 Exponent != 0 → value * 10^exponent
7. 超出 0–100 → 丢弃，视为读取失败
8. 成功 → 更新 BatteryPercent / LastSuccessAt / Status=Ok
9. 若未建立订阅，释放 GattDeviceService、BluetoothLEDevice；订阅中的设备保留到断连/超时/退出
```

实现细节：

- 每个 WinRT `IAsyncOperation` 都通过 `.AsTask(linkedToken)` 接入超时，避免 GATT 卡死拖住整轮。
- 使用 `BluetoothCacheMode.Uncached`，防止读到系统缓存的旧值。
- `GattCommunicationStatus` 可能是 `Success` 但 `Services` 为空，仍按“无电池服务”处理。
- 读取结果第一个字节之外的字节忽略。
- 设备电源状态变化（充电中）不影响电量值，只影响文案。
- 部分设备要求先建立连接；GATT 访问本身会触发系统连接与配对校验，无需也不存在 `ConnectAsync`。

### 7.3 通知订阅（优先）

对支持 `Notify` 或 `Indicate` 的特征值：

```csharp
var value = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
    ? GattClientCharacteristicConfigurationDescriptorValue.Notify
    : GattClientCharacteristicConfigurationDescriptorValue.Indicate;

var status = await characteristic
    .WriteClientCharacteristicConfigurationDescriptorAsync(value)
    .AsTask(ct);

if (status == GattCommunicationStatus.Success)
{
    characteristic.ValueChanged += OnValueChanged;
    // 保存 subscription：device、service、characteristic、handler
}
```

生命周期：

- 每个逻辑设备最多一个订阅对象；重复刷新不重复订阅。
- `ValueChanged` 回调只做“复制字节 + 解析 + 更新状态 + 发事件”，不做 I/O。
- 设备断连（`ConnectionStatusChanged` → Disconnected）时释放订阅；下一轮可重新订阅。
- 连续 15 分钟没有收到通知且设备仍在线 → 主动释放订阅，交给轮询；下一轮重新评估。
- 订阅对象总数上限 16，超出后只轮询，避免异常设备导致句柄膨胀。
- 应用退出时逐个移除事件、写 CCCD 关闭通知（失败忽略）、释放服务与设备。

> 订阅能让电量变化即时到达，但会维持 BLE 链路。默认开启，设置项 `useGattNotifications=true`；用户可在设置中关闭，退化为纯轮询。

### 7.4 轮询兜底

无论是否订阅，每轮仍执行一次主动读取：

- 对不支持通知的设备：这是唯一数据来源，每轮都读。
- 对支持通知的设备：每轮主动读一次，用于校准、发现“通知静默失效”，并保证刚启动时立即有值。
- 轮询间隔 30–60 秒（默认 45 秒），单设备超时 8 秒。

### 7.5 BLE 识别边界

- 只有暴露 `0x180F` 的设备才能通过 GATT 读电量；仅暴露厂商自定义电量服务的设备一期不支持。
- Windows 可能对已配对但未连接的设备返回 `Unreachable`；这是正常状态，不是程序错误。
- 设备可能同时暴露多个电池服务实例（如耳机左右耳）；一期取第一个成功读取的服务，并在日志中记录实例数。
- 电池特征值可能是 `Indicate` 而非 `Notify`；两者都支持。

---

## 7.6 2.4G HID（Rapoo / LINGBAO K98，独立传输）

> 2.4G **不是蓝牙**；它使用 USB HID 接收器，拥有独立的传输类型、枚举路径和厂商探针。Rapoo 与 K98 的命令/报告格式彼此独立，`Hid24GProtocol` 只用于分派，不得跨厂商复用命令或按 VID/PID 之外的信息推断协议。

### 范围

- 仅支持已确认的两种接收器：
  - Rapoo `VID_24AE&PID_1861`，接口 `MI_01/COL05`，Top-level usage `0xFF00/0x0002`。
  - LINGBAO K98 `VID_3554&PID_FA09`，接口 `MI_01/COL01`，Top-level usage `0xFF02/0x0002`。
- 仅读取电量。**禁止**向 Rapoo 的 `col04`/`0xBA` 配置集合发送 output/feature 报告；禁止修改 DPI、灯光、按键或设备设置。
- K98 只发送固定的 `13 4A` 电量查询与必要的 `13 07` 唤醒查询，不使用 feature `0x06` 猜测电量。
- K98 的 `KB.ini` `ShowPower=1` 只用于打开厂商电量 UI，BlueMeter 不依赖该开关。
- 其他 LINGBAO 型号或其他 2.4G 接收器不套用上述协议，确认前保持不支持。

### 枚举

```text
SetupDiGetClassDevs(HID GUID, PRESENT | DEVICEINTERFACE)
  → SetupDiEnumDeviceInterfaces / SetupDiGetDeviceInterfaceDetail
  → path 过滤已确认的 VID/PID + 接口 + 集合
  → CreateFile(只读或固定只读查询所需访问权限, share read/write)
  → HidD_GetAttributes                  // VID/PID
  → HidD_GetPreparsedData + HidP_GetCaps
  → 校验 usage page/usage 与报告长度
  → HidD_GetProductString / ManufacturerString
```

| 厂商 | 路径过滤 | 打开方式 | caps 校验 |
| --- | --- | --- | --- |
| Rapoo | `vid_24ae&pid_1861`、`&mi_01&`、`&col05#` | `GENERIC_READ` | `0xFF00/0x0002`，input report ≥ 7 |
| LINGBAO K98 | `vid_3554&pid_fa09`、`&mi_01&`、`&col01#` | 枚举用只读；读取用读写 | `0xFF02/0x0002`，input/output report ≥ 20 |

只打开目标厂商集合；不枚举键盘/鼠标的标准 input 集合，不读取用户输入。K98 的读写句柄仅用于固定只读查询，`HidInterop` 不调用 `HidD_SetFeature` / `HidD_SetOutputReport`。

### Rapoo 报告

| 项 | 值 |
| --- | --- |
| 接口 | `\\?\hid#vid_24ae&pid_1861&mi_01&col05#...` |
| Top-level usage | `0xFF00 / 0x0002` |
| Input report ID | `0xBB` |
| 读取方式 | `HidD_GetInputReport(h, buffer, 7)`（只读） |
| 官方应用 `ReadFile` 示例 | `BB B0 42 40 06 01 46` → 电量 `0x46` = 70 |
| `HidD_GetInputReport` 示例 | `B0 42 40 06 01 46 00` → 电量索引 5 = 70 |
| 查询频率 | 接收器约每 3 秒主动上报；应用按刷新周期读取，不发送查询 |

解析同时兼容带 `0xBB` report id（索引 6）和 `HidD_GetInputReport` 返回的 `0xB0` payload（索引 5），最后校验 0–100。

### K98 报告

| 项 | 值 |
| --- | --- |
| 接口 | `\\?\hid#vid_3554&pid_fa09&mi_01&col01#...` |
| Top-level usage | `0xFF02 / 0x0002` |
| Report 长度 | 20 字节（input/output 均 ≥ 20） |
| 请求 | `13 4A 01` + 16 个 `0x00` + checksum；checksum 为前 19 字节字节和的低 8 位 |
| 响应 | `13 4A 01 00 02 <battery> <status> ... <checksum>` |
| 电量 | offset 5，取值 0–100 |
| 充电状态 | offset 6 高半字节非零时为充电中 |
| 唤醒查询 | 仅在上一次未收到响应时发送 `13 07`，不解析返回值、不写配置 |
| 超时 | 写 500 ms，读 1500 ms（唤醒读 1000 ms）；最多 3 次尝试，间隔 80 ms |

解析前校验 report id、命令、子命令与 checksum；电量 >100 或 checksum 不符视为无效响应，继续读到超时，不猜测、不改读 feature。每次读取都重新打开句柄并在结束时释放，不长期持有。

### 状态与生命周期

- 枚举成功但没有目标设备 → 当前无 2.4G 设备；连续两轮缺失才移除旧条目。
- HID 打开/读取失败 → 有旧值标 `Stale`，无旧值标 `Unavailable`；不阻塞 BLE 或其他设备。
- 2.4G 读取排在 BLE 之前，避免慢 GATT 设备耗尽 30 秒单轮预算。
- 蓝牙不可用（无适配器/关闭）时仍继续枚举和读取 2.4G；全局状态用 `Ready` + “蓝牙不可用；已检测到 2.4G 接收器设备”。
- 厂商应用独占 HID 句柄时打开可能失败；下一轮自动重试。
- K98 的读写通过 overlapped I/O 实现超时；超时或退出时调用 `CancelIoEx` 中止未完成请求并释放句柄，不留下悬挂 I/O。

---

## 8. 经典蓝牙电量读取

Windows 没有统一的经典蓝牙电量 API。一期只读取系统设备属性：

```csharp
var props = info.Properties;

if (props.TryGetValue("System.Devices.BatteryLife", out var raw) && raw is byte life)
{
    if (life <= 100)
        battery = life;                  // 0–100
    else
        battery = null;                  // 101 = UnknownStatus
}

if (props.TryGetValue("System.Devices.BatteryPlusCharging", out var plusRaw)
    && plusRaw is byte plus)
{
    // 0–100：电量；101–200：电量 + 充电中（值 - 100）
    // 201：未知
}

if (props.TryGetValue("System.Devices.BatteryPlusChargingText", out var text))
{
    chargingText = text as string;       // 例如 "Charging"
}
```

规则：

- `BatteryLife` 是 `Byte` 百分比；`101` 表示未知，不能当成 101% 或 1%。属性值在部分系统/驱动上可能以 `uint`/`int` 返回，实现时统一转 `int` 再校验。
- `BatteryPlusCharging` 使用 `+100` 编码充电状态；`201` 表示未知。取值下限按 0、上限按 200 处理，超出视为未知。
- 属性存在但类型不符/为空 → 视为不可用，并记录一次 Warn。
- 属性不存在 → `NotSupported`，不参与低电量告警，不显示伪造的 0%。
- 经典设备通常只在上次连接后由系统缓存电量；显示的“最后更新时间”必须反映实际读取时间，而不是系统缓存时间（无法获知时按本轮时间，并在 UI 上以“系统报告”弱化实时性）。
- 二期在 `IBatteryProbe` 下增加 `ClassicRfcommProbe`，不改变上层接口。

---

## 9. 刷新调度

### 9.1 参数

| 参数 | 默认值 | 范围 | 说明 |
| --- | --- | --- | --- |
| `RefreshIntervalSeconds` | 45 | 30–60 | 定时轮询间隔 |
| `PerDeviceTimeoutSeconds` | 8 | 3–15 | 单设备读取超时 |
| `CycleBudgetSeconds` | 30 | 15–60 | 单轮总预算，超出则跳过剩余设备 |
| `StaleAfterCycles` | 3 | 固定 | 超过 3 个周期未成功 → Stale |
| `JitterSeconds` | ±5 | 固定 | 避免与其他蓝牙软件同时扫描 |

### 9.2 循环

```csharp
public async Task RunAsync(CancellationToken ct)
{
    await RefreshOnceAsync(manual: false, ct);   // 启动后先扫一轮

    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval + jitter));
    while (await timer.WaitForNextTickAsync(ct))
    {
        try
        {
            await RefreshOnceAsync(manual: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            // 单轮失败不能杀死循环
            log.Error("刷新轮次失败", ex);
        }
    }
}
```

- 枚举本身也加 10 秒超时。
- `SemaphoreSlim(1,1)` 保证同一时刻只有一轮在跑。
- 手动刷新：若正在扫描，置 `_refreshQueued=true`，本轮结束后立即补跑一次；不并发。
- 单轮内设备**串行**读取；每台设备独立超时、独立 try/catch。
- 2.4G 读取是本地同步 HID 报告读取，排在 BLE 之前；不占用 BLE 的单设备 8 秒超时。
- 单轮总预算耗尽后，剩余设备标记为“本轮跳过”（保留原状态），不标记失败。
- 系统从睡眠恢复（`PowerModeChanged.Resume`）后清空等待，立即刷新。
- 蓝牙开关变化（`Radio.StateChanged` / 全局检测）后下一轮重试；不做事件级重连风暴。
- 窗口隐藏时服务继续运行；应用退出时通过 `CancellationToken` 停止，最多等待 3 秒。

### 9.3 事件

```csharp
public interface IDeviceBatteryService
{
    event EventHandler<IReadOnlyList<DeviceBatterySnapshot>>? DevicesUpdated;
    event EventHandler<IReadOnlyList<LowBatteryNotice>>? LowBatteryDetected;
    event EventHandler<DeviceServiceStatus>? StatusChanged;

    IReadOnlyList<DeviceBatterySnapshot> Current { get; }
    DeviceServiceStatus Status { get; }

    Task StartAsync(CancellationToken ct);
    Task RefreshAsync(bool manual, CancellationToken ct);
    Task StopAsync();
}
```

- `DevicesUpdated`：每台设备读取完成后可发一次（增量），每轮结束再发一次完整快照；UI 侧做节流（100 ms 合并）。
- `LowBatteryDetected`：每轮最多一次，批量携带本轮新触发的设备。
- `StatusChanged`：蓝牙关闭、无适配器、枚举失败，或“蓝牙不可用但 2.4G 可用”等全局状态。

事件在后台线程触发。`MainViewModel` 捕获 UI `Dispatcher` 并 `BeginInvoke`，禁止在服务层直接操作控件。

---

## 10. 状态保留与时间语义

| 字段 | 含义 | 失败时 |
| --- | --- | --- |
| `BatteryPercent` | 最近一次成功读取的电量 | 保留旧值，不清空 |
| `LastSuccessAt` | 最近一次成功读取的本地时间 | 不变 |
| `LastAttemptAt` | 最近一次尝试时间 | 更新 |
| `Status` | 当前可用性 | 有旧值 → `Stale`；无旧值 → `Unavailable` |
| `LowBatteryAlerted` | 本次放电是否已提醒 | 不清除（除非回升） |

UI 展示规则：

| 状态 | 电量显示 | 副标题 |
| --- | --- | --- |
| `Ok` | `85%` | `刚刚` / `2 分钟前` |
| `Stale` | `85%` | `暂时不可达 · 2 分钟前` |
| `Unavailable` | `--` | `暂时不可达` |
| `NotSupported` | `--` | `不支持电量读取` |
| `Reading` | 旧值或 `--` | `正在读取…` |
| `Unknown` | `--` | `等待扫描` |

“暂时不可达”不等于“设备离线”：可能只是本轮 GATT 失败。文案避免断言设备已关闭。

---

## 11. 低电量通知

### 11.1 触发规则

```text
threshold = settings.LowBatteryThreshold   // 默认 20
hysteresis = 5

on fresh reading (status == Ok):
    if battery <= threshold:
        if !state.LowBatteryAlerted:
            raise alert
            state.LowBatteryAlerted = true
    else if battery >= threshold + hysteresis:
        state.LowBatteryAlerted = false
```

- 首次读到低电量也会提醒（不能假设用户之前看到过）。
- 旧值/不可达/不支持不触发、不复位闩锁。
- 电量在阈值附近抖动时，靠 +5% 迟滞避免反复弹窗。
- 阈值修改后，重新评估所有设备的闩锁状态（避免改阈值后永远不提醒）。

### 11.2 聚合

同一轮多台设备同时跌破阈值时，合并成一条气泡：

```text
1 台：鼠标电量低：15%
多台：2 个设备电量低，最低：键盘 12%
```

气泡使用：

```csharp
_taskbarIcon.ShowNotification(
    "BlueMeter",
    message,
    NotificationIcon.Warning);
```

- 通知被系统关闭、专注助手拦截、Explorer 未就绪：捕获异常/忽略返回值，不重试。
- 每次提醒写一条 Info 日志（设备名、电量、阈值），便于验证去重逻辑。
- 不在气泡中放长列表；完整信息在主窗口。

---

## 12. 托盘与窗口生命周期

### 12.1 启动

```text
App.OnStartup
 ├─ 获取单实例 Mutex；失败 → 第二实例直接退出（唤醒已有窗口可后补）
 ├─ 加载设置、初始化日志
 ├─ 创建 DeviceBatteryService
 ├─ 创建 TrayIconService（TaskbarIcon，Visible）
 ├─ 创建 MainWindow + MainViewModel，订阅服务事件
 ├─ 若 !StartMinimized → Show()
 ├─ 启动服务循环（后台）
 └─ 订阅 SessionEnding
```

`App.xaml`：

```xml
<Application x:Class="BlueMeter.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Light" />
                <ui:ControlsDictionary />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

> `ThemesDictionary` 的初始值只是占位；`SystemThemeWatcher.Watch(this)` 会在窗口创建后按系统实际主题、强调色和背景材质覆盖。

### 12.2 关闭与最小化

```csharp
protected override void OnClosing(CancelEventArgs e)
{
    if (!_allowClose)
    {
        e.Cancel = true;
        Hide();
        return;
    }
    base.OnClosing(e);
}
```

- `_allowClose` 只由统一退出流程设置。
- 最小化：`StateChanged` 时确保托盘图标 `Visible`；**不隐藏窗口**，主窗口仍留在任务栏（符合需求）。
- 关闭：隐藏窗口，扫描继续。
- 隐藏后再次显示：`Show()` → `WindowState = Normal` → `Activate()`；若前台激活被系统拒绝，可调用 `SetForegroundWindow` 兜底。

### 12.3 退出

```text
Tray menu "退出"
 ├─ App.RequestExit()
 ├─ _allowClose = true
 ├─ 取消服务 CTS，等待 StopAsync（≤3s）
 ├─ 释放 BLE 订阅、GATT 服务、设备对象与 K98 HID 句柄
 ├─ TrayIconService.Dispose()（移除托盘图标）
 ├─ MainWindow.Close()（允许关闭）
 └─ Application.Current.Shutdown()
```

- 托盘“退出”是唯一彻底结束进程的入口。
- `SessionEnding`（注销/关机）直接走退出流程，不取消。
- 退出过程中忽略新的刷新请求；日志落盘后结束。
- 不使用 `Environment.Exit`，除非退出流程超时且已记录。

### 12.4 单实例

- 命名：`Local\BlueMeter.SingleInstance`（每用户会话）。
- 第二个实例检测到互斥体已存在后立即退出；首版可以不实现“唤醒已有窗口”，但不得产生第二个托盘图标。
- 若后续实现唤醒，优先命名管道（`System.IO.Pipes`），避免 `WM_COPYDATA` 的窗口句柄时序问题。

### 12.5 Explorer 重启

任务栏重建后，H.NotifyIcon 会处理 `TaskbarCreated` 并恢复图标；仍需在手测矩阵中验证。若发现图标丢失，再显式监听 `TaskbarCreated` 并重建 `TaskbarIcon`。

---

## 13. 托盘 ToolTip 构建

Windows `NOTIFYICONDATA.szTip` 为 128 个 `WCHAR`（含结尾 `\0`），实际可用约 127 字符。统一上限 120，留出余量。

优先级：

```text
1. 有 Ok 状态设备：
   1 台  → "{名称} {电量}%"
   多台  → "{N} 个设备，最低 {电量}%"
2. 只有 Stale/Unavailable 且有旧值：
   1 台  → "{名称} {电量}%（暂时不可达）"
   多台  → "{N} 个设备，最低 {电量}%（旧值）"
3. 有设备但都不可读：
   "BlueMeter · 未发现可读电量"
4. 无已配对设备：
   "BlueMeter · 未发现已配对设备"
5. 正在首轮扫描且无任何数据：
   "BlueMeter · 正在扫描…"
```

实现细节：

- 选择最低电量时只考虑 `Ok`；没有 `Ok` 才考虑旧值。
- 设备名超过 24 字符时截断并加 `…`。
- 最终再做一次 120 字符硬截断（保留结尾 `…`）。
- 单行文本；不拼接换行，避免不同 Windows 版本提示框高度异常。
- 每次 `DevicesUpdated` 后重建；相同文本不重复赋值，减少 Shell 调用。

---

## 14. 线程模型

| 线程/上下文 | 工作 | 约束 |
| --- | --- | --- |
| UI（STA） | WPF、`TaskbarIcon`、ViewModel、命令 | 所有集合变更必须在此 |
| 服务循环（线程池） | 枚举、GATT、属性读取 | 禁止触碰 UI 对象 |
| WinRT 回调 | `ValueChanged`、`ConnectionStatusChanged` | 只做解析与事件，不阻塞 |
| 日志 | 同步追加 + 锁 | 单行写入，避免高频 I/O |

规则：

- 服务层 `.ConfigureAwait(false)`；UI 层不写。
- 禁止 `.Result` / `.Wait()` / `async void`（事件处理器除外，且必须 try/catch）。
- 所有取消通过 `CancellationToken` 串联：应用退出 → 服务循环 → 单设备读取。
- 服务事件携带不可变快照；ViewModel 在 Dispatcher 上替换集合，避免跨线程访问 `ObservableCollection`。
- 相对时间（“2 分钟前”）由 UI `DispatcherTimer` 每 30 秒刷新，不放在服务层。

---

## 15. 错误处理矩阵

| 场景 | 检测方式 | 设备状态 | UI | 处理 |
| --- | --- | --- | --- | --- |
| 无蓝牙适配器 | `BluetoothAdapter.GetDefaultAsync()` 返回 null | — | 全局提示“未检测到蓝牙适配器”；若 2.4G 可用则显示其电量 | 仍枚举/读取 2.4G，定期重试蓝牙 |
| 蓝牙关闭 | 枚举空 + Radio 状态 Off | 全部保留旧值 | 全局 `InfoBar` | 下一轮重试；2.4G 不受影响 |
| 2.4G 接收器未插入 | SetupAPI 找不到 `24AE:1861/col05` 或 `3554:FA09/col01` | 连续 2 轮后移除 | 列表移除 | 不视为错误 |
| 2.4G 接口被独占 | `CreateFile` 返回访问被拒 | `Stale`/`Unavailable` | “暂时不可达” | 只读共享打开；下轮重试；提示关闭厂商应用 |
| Rapoo 报告无法解析 | 非 `0xB0/0xBB` 前缀或电量 >100 | `Stale`/`Unavailable` | 旧值或 `--` | Warn 日志，不写设备，不猜测 |
| K98 响应无效 | 长度不足 20、report id/命令不符、checksum 不符或电量 >100 | `Stale`/`Unavailable` | 旧值或 `--` | Warn 日志；继续读到超时，不改读 feature、不写配置 |
| K98 查询超时 | 写/读 overlapped 超过 500/1500 ms 或 3 次尝试均失败 | `Stale`/`Unavailable` | 旧值或 `--` | 取消 I/O、释放句柄；下轮重试 |
| 2.4G 枚举失败 | SetupAPI/HID 异常，或单个厂商枚举失败 | 保留该厂商上一轮条目 | 不崩溃 | Warn 日志；不调用移除逻辑 |
| 设备不可达 | `GattCommunicationStatus.Unreachable`、`COMException`（如 `0x800710DF` `ERROR_DEVICE_NOT_AVAILABLE`） | `Stale`/`Unavailable` | “暂时不可达” | 下轮重试，不立即重连 |
| 访问被拒 | `UnauthorizedAccessException`、`0x80070005` | `Stale`/`Unavailable` | “暂时不可达” | 记录 Warn；可能需要在系统设置中检查权限/配对 |
| 无电池服务 | 服务/特征值为空 | `NotSupported` | “不支持电量读取” | 每轮复查（设备固件可能动态暴露） |
| 读取超时 | `AsTask` 取消 / `TimeoutException` | `Stale`/`Unavailable` | “暂时不可达” | 单设备取消，继续下一台 |
| 读取值非法 | 空缓冲区、>100、类型错误 | `Stale`/`Unavailable` | 旧值或 `--` | 记录 Warn，保留旧值 |
| 设备枚举后消失 | `FromIdAsync` 返回 null、COM 异常 | 保留 1 轮后移除 | 列表移除 | 不视为错误 |
| 应用退出时对象已释放 | `ObjectDisposedException` | — | — | Debug 日志后忽略 |
| 单轮未捕获异常 | 循环边界 catch | 保持上轮 | 不崩溃 | Error 日志，等待下一轮 |
| 气泡通知失败 | 异常/系统禁用 | — | 主窗口仍有状态 | Warn 日志，不重试 |
| 设置文件损坏 | JSON 解析失败 | — | 使用默认值 | 备份坏文件为 `.bak`，记录 Warn |

异常日志必须包含：设备 Id（可截断）、名称、类型、操作阶段（枚举/服务/特征值/读取/订阅）、HRESULT（若有）。蓝牙地址做掩码（如 `AA:BB:CC:**:**:**`）。

---

## 16. 设置

路径：`%LOCALAPPDATA%\BlueMeter\settings.json`

```json
{
  "schemaVersion": 1,
  "refreshIntervalSeconds": 45,
  "lowBatteryThreshold": 20,
  "lowBatteryNotificationsEnabled": true,
  "useGattNotifications": true,
  "startMinimized": false,
  "runAtStartup": false,
  "showUnsupportedDevices": true
}
```

| 字段 | 默认 | 校验 |
| --- | --- | --- |
| `refreshIntervalSeconds` | 45 | 30–60 |
| `lowBatteryThreshold` | 20 | 5–50 |
| `lowBatteryNotificationsEnabled` | true | 系统通知关闭时仍保留此偏好 |
| `useGattNotifications` | true | false = 纯轮询 |
| `startMinimized` | false | 启动不显示主窗口 |
| `runAtStartup` | false | 可选；未实现开机启动时隐藏此项 |
| `showUnsupportedDevices` | true | false 时列表隐藏 `NotSupported` 设备 |

行为：

- 加载失败 → 重命名为 `settings.json.bak-{timestamp}`，使用默认值。
- 保存使用临时文件 + `File.Replace`/`File.Move(overwrite)`，避免半写。
- 设置变更事件 → 服务更新间隔/阈值，无需重启；托盘与 UI 立即刷新。
- 未知字段忽略；缺失字段用默认值；数值越界钳制。
- 不把设置写入注册表（开机启动键除外）。

---

## 17. 日志

- 路径：`%LOCALAPPDATA%\BlueMeter\logs\bluemeter-yyyyMMdd.log`
- 格式：`2026-10-27 14:32:05.123 [INFO] [BleProbe] ...`
- 级别：Debug / Info / Warn / Error；Release 默认 Info。
- 保留 7 天，启动时清理过期文件。
- 单文件上限 5 MB，超出后滚动为 `.1`。
- 记录：启动/退出、设置加载、每轮设备数/耗时、每次失败原因、低电量触发、托盘重建。
- 不记录：完整 GATT 缓冲区、用户目录、与功能无关的系统信息。
- 所有写入 try/catch，日志失败不得影响主流程。

---

## 18. 隐私与安全

- 无网络访问、无遥测、无自动更新检查。
- 设备列表仅存于内存；设置与日志仅本地。
- 日志中的蓝牙地址掩码；设备名称保留（用于排查），文档提示用户分享日志前自行检查。
- 2.4G 只记录产品名/厂商/VID/PID/失败阶段，不记录完整 HID 接口路径、报告缓冲区或 K98 查询内容。
- 不请求管理员权限；不安装服务/驱动。
- 非打包 Win32 应用不需要声明 `bluetooth` 能力；若未来改为 MSIX，必须添加 `bluetooth` 能力与相应 `DeviceCapability`，否则枚举/访问会被系统拒绝。

---

## 19. API 事实核对

| # | 需求/常见写法 | 实际 API | 结论 |
| --- | --- | --- | --- |
| 1 | `GetDeviceSelector()` 取已配对 | `GetDeviceSelectorFromPairingState(true)` | 采用后者；前者不保证配对状态 |
| 2 | `System.Devices.BatteryLifePercent` | `System.Devices.BatteryLife` (`Byte`) | 正确键名是 `BatteryLife`；`101` = UnknownStatus |
| 3 | 经典电量充电状态 | `System.Devices.BatteryPlusCharging` (`Byte`)、`System.Devices.BatteryPlusChargingText` (`String`) | `+100` 表示充电中，`201` 未知 |
| 4 | `BluetoothLEDevice.ConnectAsync()` | 不存在公开方法 | 通过 `GetGattServicesAsync`/`GetGattServicesForUuidAsync` 触发连接；需要维持时用 `GattSession.MaintainConnection` |
| 5 | `TaskbarIcon.ShowBalloonTip` | `TaskbarIcon.ShowNotification(string, string, NotificationIcon)` | H.NotifyIcon 2.3.2 的实际 API；同样显示托盘气泡 |
| 6 | H.NotifyIcon.Wpf 最新版 | 2.4.1 仅含 `net10.0-windows7.0` / `net462` | net8 项目必须用 2.3.2 |
| 7 | WPF-UI 主题监听 | `SystemThemeWatcher.Watch(Window, WindowBackdropType, bool)` | 4.3.0 可用；`FluentWindow` 支持 `WindowBackdropType`、`WindowCornerPreference` |
| 8 | `0x2904` 指数缩放 | `GattCharacteristic.PresentationFormats` | 无需手动读描述符；取第一个格式的 `Exponent` |
| 9 | 通知订阅 | `WriteClientCharacteristicConfigurationDescriptorAsync` | 需要特征值支持 Notify/Indicate；写 CCCD 后再订阅 `ValueChanged` |
| 10 | 最低系统 17763 | 所用 API 引入版本：配对选择器 10586、GATT 15063、`PresentationFormats` 15063、`GattSession` 15063 | 均 ≤ 17763；Mica 仅 Win11，运行时退化 |
| 11 | `System.Devices.Aep.DeviceCategory` | `System.Devices.Aep.Category` | 前者不是有效规范键，传给 `FindAllAsync` 会抛 `COMException 0x8002802B` |
| 12 | Rapoo 2.4G 电量查询 | 只读 `HidD_GetInputReport(0xBB)` | 官方应用与独立探测确认；不使用 `col04`/`0xBA`，不发送 output/config 报告 |
| 13 | LINGBAO K98 2.4G 电量查询 | 20 字节固定查询 `13 4A 01 ...` + `ReadFile` 响应 | 厂商 `CDev3632::ReadPower` 与独立只读探测确认；不使用 feature `0x06`，不写配置 |

> 若未来将 TFM 升到 .NET 10，可同时评估 H.NotifyIcon.Wpf 2.4.x、WPF-UI 新版，并重新跑手测矩阵。

---

## 20. 二期扩展点

### 20.1 经典蓝牙 RFCOMM

新增 `ClassicRfcommProbe : IBatteryProbe`：

- 通过 `RfcommDeviceService.GetDeviceSelector(RfcommServiceId.SerialPort)` 发现串口服务。
- 连接后监听 `IPHONEACCEV` AT 事件（AirPods 等）。
- 解析 `+IPHONEACCEV:<count>,<key>,<value>,...`，key 1/2/3 对应左/右/充电盒。
- 与现有 `ClassicBatteryProbe` 组成回退链：系统属性优先（便宜），RFCOMM 补充。
- 需要处理独占连接、设备不响应 AT、协议差异；以独立功能开关控制。

### 20.2 其他

- `IBatteryProbe` 可继续扩展其他厂商 GATT 服务或已确认的 USB/HID 接收器；未确认协议不接入。
- 设备历史写入本地存储（SQLite/JSONL），UI 增加趋势图。
- 通知操作（静音 1 小时）需要通知回调/COM 激活，评估成本后再做。

---

## 21. 关键决策记录（ADR 摘要）

| 决策 | 选择 | 理由 |
| --- | --- | --- |
| UI 框架 | WPF + WPF-UI | 需求锁定；FluentWindow 提供 Mica/圆角/主题 |
| 托盘 | H.NotifyIcon.Wpf 2.3.2 | 纯 WPF、支持 XAML/事件、无 WinForms 依赖；2.4.1 不兼容 net8 |
| 设备发现 | 定时全量枚举 | 配对设备少；避免 watcher 生命周期与事件风暴 |
| BLE 策略 | 通知优先 + 全量轮询兜底 | 兼顾实时性与静默失效检测 |
| 并发 | 单轮串行 | 降低 GATT 资源竞争与设备耗电 |
| 状态 | LastKnown + 显式状态枚举 | UI 诚实表达，避免旧值伪装实时 |
| 2.4G（Rapoo / K98） | 独立 HID 传输 + 厂商专用只读协议 | Rapoo 只读 `0xBB`，K98 固定 `13 4A` 查询；不把 2.4G 归为蓝牙；绝不写配置集合 |
| MVVM | 手写最小基类 | 减少依赖；本项目状态简单 |
| 配置 | 本地 JSON | 无需注册表/数据库；易排查 |
| 日志 | 自写轻量文件日志 | 避免引入 Serilog 等额外依赖 |

---

## 22. 验收相关

架构层面的完成标准：

- [ ] 任何单设备异常都不会终止服务循环。
- [ ] 服务层不引用 UI 类型；ViewModel 不引用 WinRT。
- [ ] 退出时所有 GATT/托盘资源释放，进程 3 秒内结束。
- [ ] 托盘、主窗口、服务三者生命周期边界清晰，无循环引用导致无法退出。
- [ ] 失败状态在 UI 与 ToolTip 中均有一致文案。
