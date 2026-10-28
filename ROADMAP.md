# BlueMeter Roadmap

版本策略：SemVer。`0.x` 允许接口和 UI 调整；`1.0.0` 表示一期范围全部达到验收标准。文档基线为 `0.1.0-docs`，当前代码版本为 `0.4.3`。

状态图例：✅ 完成 ｜ 🚧 进行中 ｜ ⏳ 未开始 ｜ ⛔ 暂缓

---

## 里程碑总览

| 里程碑 | 版本 | 主题 | 状态 | 粗略工作量 |
| --- | --- | --- | --- | --- |
| M0 | `0.1.0-docs` | 需求梳理与文档基线 | ✅ | 已完成 |
| M1 | `0.2.0` | 工程配置、托盘、窗口生命周期 | ✅ | 已完成 |
| M2 | `0.3.0` | 设备发现与 BLE 电量读取 | ✅ | 已完成 |
| M2.1 | `0.3.1` | Rapoo / LINGBAO K98 2.4G 只读电量 | ✅ | 已完成 |
| M3 | `0.4.0` | 经典蓝牙电量、低电量通知、托盘摘要 | 🚧 代码完成，待实机复核 | 1–2 人日 |
| M3.2 | `0.4.2` | 设置接线：开机启动、列表过滤、刷新与阈值 | ✅ | 已完成 |
| M3.3 | `0.4.3` | 稳定性收口：托盘退出、K98 重试与提示、间隔即时生效、服务拆分 | ✅ | 已完成 |
| M4 | `0.5.0` | Fluent 主窗口、列表与设置 | 🚧 主体完成，待收尾提示条与行键盘焦点 | 2–3 人日 |
| M5 | `0.9.0` | 稳定性、性能、兼容性加固 | ⏳ | 2–3 人日 |
| M6 | `1.0.0` | 发布准备 | ⏳ | 0.5–1 人日 |
| P2 | `1.1.0+` | 经典蓝牙 RFCOMM、增强功能 | ⛔ | 待评估 |

> 工作量按单人开发估算，仅用于排序，不作为承诺。

---

## M0 — 文档基线 ✅

**目标**：把需求转成可执行、可验收的工程文档，锁定技术栈与关键 API 事实。

- [x] 明确一期/非一期范围与产品定位
- [x] 锁定 .NET 8、WPF-UI 4.3.0、H.NotifyIcon.Wpf 2.3.2
- [x] 核对 WinRT 选择器、蓝牙电量属性、托盘通知 API
- [x] 输出架构、UI 规范、开发指南、路线图、变更记录
- [x] 采用远程仓库已有的 MIT 许可，并补充 Git 提交规范
- [x] 识别 H.NotifyIcon 2.4.1 不兼容 net8 的版本风险
- [x] 定义错误兜底、状态保留、低电量去重策略

**验收**：文档可独立指导实现；API 名称与包版本无歧义。

---

## M1 — 工程配置、托盘与窗口生命周期 ✅

**目标**：应用能作为托盘程序稳定运行：关闭隐藏、左键还原、右键退出、单实例、主题跟随。

> `0.2.0` 已完成。设备扫描与“立即刷新”的真实行为已在 M2 接入；托盘“立即刷新”现已启用。

### 任务

- [x] **M1-1 工程配置**
  - `TargetFramework=net8.0-windows10.0.19041.0`
  - `SupportedOSPlatformVersion=10.0.17763.0`
  - `OutputType=WinExe`、`UseWPF=true`、`Nullable=enable`、`ImplicitUsings=enable`
  - `ApplicationIcon=Assets\BlueMeter.ico`
  - 引入 `WPF-UI 4.3.0`、`H.NotifyIcon.Wpf 2.3.2`
  - 可选：`app.manifest` 声明 Per-Monitor V2 DPI 感知与 Windows 10/11 兼容性
- [x] **M1-2 应用图标**
  - 制作多尺寸 `BlueMeter.ico`（16/20/24/32/48/256）
  - 作为 `Resource` 打包；托盘、窗口、可执行文件复用
- [x] **M1-3 App 组合根**
  - 移除 `StartupUri`，改为手动创建窗口与服务
  - `ShutdownMode=OnExplicitShutdown`
  - 注册 `DispatcherUnhandledException`、`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`
  - `SessionEnding` 时允许退出，不弹“隐藏到托盘”
- [x] **M1-4 主题**
  - `App.xaml` 合并 `ui:ThemesDictionary`、`ui:ControlsDictionary`
  - 主窗口构造时 `SystemThemeWatcher.Watch(this)`
  - 明暗主题、强调色、背景材质运行时跟随
- [x] **M1-5 托盘服务**
  - 封装 `TrayIconService`，持有唯一的 `H.NotifyIcon.TaskbarIcon`
  - `TrayLeftMouseDown` → 显示/还原/激活
  - 右键菜单：显示主窗口、立即刷新（占位）、退出
  - `ToolTipText` 动态更新，长度 ≤ 120 字符
  - 退出时 `Dispose()` 托盘图标
- [x] **M1-6 窗口生命周期**
  - `OnClosing` 取消关闭并 `Hide()`，除非已请求退出
  - “退出”设置退出标志 → 停止服务 → 释放托盘 → `Application.Current.Shutdown()`
  - 最小化时确保托盘图标可见；主窗口保留在任务栏
  - `StartMinimized` 为 true 时不显示主窗口，只创建隐藏实例
- [x] **M1-7 单实例**
  - 命名 `Mutex`（`Local\BlueMeter.SingleInstance`）
  - 第二实例退出；通过 `WM_COPYDATA` 或命名管道唤醒已有窗口（可先只退出）
- [x] **M1-8 设置与日志骨架**
  - `%LOCALAPPDATA%\BlueMeter\settings.json` 读写、原子替换、损坏回退默认值
  - 日志：`%LOCALAPPDATA%\BlueMeter\logs\bluemeter-YYYYMMDD.log`，按天滚动、保留 7 天
  - 日志中蓝牙地址做掩码

### 验收

1. 启动出现托盘图标，窗口正常显示。
2. 关闭窗口后进程仍在，托盘图标仍在，再次左键可还原。
3. “退出”后进程结束，托盘图标消失。
4. 重复双击 exe 不产生第二个托盘图标。
5. 系统切换明暗主题/强调色后，窗口无需重启即可跟随。
6. 在 Win10 1809+ 与 Win11 上均不崩溃；Mica 不可用时自动退化。

> 验收状态：Windows 11 实机已验证启动、托盘、关闭隐藏、单实例、启动最小化、设置损坏回退与日志写入；Win10 1809 兼容性待对应实机复核。
>
> 2026-10-28 复核：托盘退出的 `Could not get window handle` 来自 `SystemThemeWatcher.UnWatch` 在 `WM_DESTROY` 之后调用；已改为在 `OnClosing`（句柄仍有效）取消监听，`OnClosed` 只做幂等兜底，待实机托盘退出复测。

---

## M2 — 设备发现与 BLE 电量读取 ✅

**目标**：正确分类已配对设备，稳定读取 BLE 电量，失败时保留状态。

### 任务

- [x] **M2-1 领域模型**
  - `DeviceTransportKind`（`BluetoothLe` / `BluetoothClassic` / `Hid24G`）
  - `BatteryReadStatus`（Unknown / Reading / Ok / Stale / Unavailable / NotSupported）
  - `DeviceBatteryState`：Id、Address、Name、Transport、BatteryPercent、LastSuccessAt、LastAttemptAt、IsConnected、Status、SupportsNotifications、ChargingText、LowBatteryAlerted；HID 设备额外保存 VendorId/ProductId/InterfacePath/ReceiverId/VendorName
- [x] **M2-2 设备枚举**
  - BLE：`DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), keys)`
  - 经典：`DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), keys)`
  - 请求属性：`System.Devices.Aep.DeviceAddress`、`System.Devices.Aep.IsConnected`、`System.Devices.Aep.Category`、`System.Devices.BatteryLife`、`System.Devices.BatteryPlusChargingText` 等
  - 空名称回退为“未知设备”并保留；过滤明显非设备的条目
  - 保留离线设备以显示上次值
  - 按蓝牙地址去重，同一设备同时出现在两类列表时优先 BLE
- [x] **M2-3 BLE 读取**
  - `BluetoothLEDevice.FromIdAsync` → `GetGattServicesForUuidAsync(GattServiceUuids.Battery, Uncached)`
  - 获取 `GattCharacteristicUuids.BatteryLevel` 特征值
  - `ReadValueAsync(Uncached)`，取第一个字节；必要时用 `PresentationFormats` 的指数缩放
  - 校验 0–100，非法值视为读取失败
  - 所有 `IAsyncOperation` 通过 `AsTask(ct)` 加超时
- [x] **M2-4 通知订阅**
  - 特征值支持 Notify/Indicate 时写入 CCCD
  - 订阅 `ValueChanged`，回调解析电量并更新状态
  - 订阅对象按设备保存；断连/15 分钟无通知/蓝牙关闭/停止时释放；订阅上限 16
  - 不支持通知的设备走轮询
- [x] **M2-5 刷新调度**
  - `PeriodicTimer`，默认 45 秒，±5 秒抖动
  - `SemaphoreSlim(1,1)` 保证单轮串行、禁止重入
  - 单设备超时 8 秒；单轮总预算 30 秒，超预算设备跳过并保留旧值
  - 手动刷新：若正在扫描则排队一次，不并发
  - 系统从睡眠恢复后立即刷新一轮
- [x] **M2-6 状态与事件**
  - `DevicesUpdated`（每次设备状态变化 + 每轮结束）
  - 结构化失败日志（设备、阶段、状态/错误码），不向 UI 抛异常
  - `LowBatteryDetected`（M3 消费）
  - 失败且已有旧值 → `Stale`；无旧值 → `Unavailable`；后续周期保持该状态直到恢复
- [x] **M2-7 资源释放**
  - 未建立订阅的读取在当轮结束释放 `GattDeviceService` / `BluetoothLEDevice`
  - 订阅中的设备保留对象，断连/超时/退出时释放
  - 退出流程最多等待 3 秒，不阻塞进程结束

### 实现说明

- 设备属性键使用有效的 `System.Devices.Aep.Category`；`System.Devices.Aep.DeviceCategory` 会导致 `COMException 0x8002802B`，已从实现和文档中移除。
- 当前机器无蓝牙适配器，已验证空设备列表、无适配器状态、日志与退出路径；真实 BLE 设备的 GATT 读取需在有适配器的 Windows 11/10 实机补验。
- M2 只读取 BLE 电量；经典设备完成枚举、分类与地址去重，真实电量读取按计划留到 M3。
- 主窗口本阶段提供轻量设备预览列表，用于观察 M2 结果；完整 Fluent 列表、排序与设置页在 M4 完成。

### 验收

1. 列表能区分 BLE 与经典蓝牙；同一设备不重复出现。
2. 支持电池服务的 BLE 鼠标/键盘能读出电量，且与厂商软件差异在合理范围内。
3. 设备关机或走远后，界面保留旧值并标记“暂时不可达”，进程不崩溃。
4. 连续运行 2 小时无句柄泄漏、无 GATT 对象无限增长。
5. 睡眠唤醒、蓝牙开关切换后能自动恢复读取。
6. 20 台模拟/真实设备下，单轮扫描不重叠、UI 不卡顿。

> 验收状态：代码路径与无适配器环境已验证；真实 BLE 电量、睡眠唤醒、蓝牙开关切换和 2 小时资源稳定性仍需在有蓝牙适配器的实机补验。

---

## M2.1 — Rapoo / LINGBAO K98 2.4G 只读电量 ✅

**目标**：在不把 2.4G 当作蓝牙、不写入设备配置的前提下，读取已确认的 Rapoo 与 LINGBAO K98 2.4G 接收器电量。

> `0.3.1` 已完成。实现依据为官方 Rapoo 应用流量、LINGBAO 厂商 `CDev3632::ReadPower` 与独立只读探测；当前确认 Rapoo `VID_24AE&PID_1861` 的 `MI_01/COL05`，以及 LINGBAO K98 `VID_3554&PID_FA09` 的 `MI_01/COL01`。
>
> K98 的 `KB.ini` 曾用于打开厂商电量 UI（`ShowPower=1`），但 BlueMeter 不依赖该 UI，也不使用 feature `0x06` 猜测电量；只使用已确认的 `13 4A` 只读查询。其他 LINGBAO 型号不套用该协议。

### 任务

- [x] **M2.1-1 传输与身份模型**
  - `DeviceTransportKind.Hid24G` 与 BLE/经典并列，不合并为蓝牙地址
  - `Hid24GDeviceInfo`：VID、PID、HID 接口路径、接收器身份、产品名、厂商名、input report 长度
  - 快照与状态携带 2.4G 身份字段，UI 显示“2.4G 接收器”
- [x] **M2.1-2 只读 HID 枚举**
  - 使用 SetupAPI 枚举 HID 接口，Rapoo 过滤 `vid_24ae&pid_1861`、`&mi_01&` 与 `&col05#`
  - K98 过滤 `vid_3554&pid_fa09`、`&mi_01&` 与 `&col01#`，校验 `0xFF02/0x0002` 与 20 字节 input/output report
  - 用 `HidD_GetAttributes`、`HidD_GetPreparsedData` / `HidP_GetCaps` 校验 caps
  - Rapoo 仅以 `GENERIC_READ` 打开；K98 枚举用只读句柄，读取时仅以固定查询所需的读写句柄打开
- [x] **M2.1-3 厂商电量探针**
  - Rapoo：`HidD_GetInputReport(0xBB)` 只读报告，兼容 `B0 ... 46 00`（电量索引 5）与带 `0xBB` 前缀（索引 6）
  - K98：发送 20 字节 `13 4A 01 ... checksum`，解析 `13 4A 01 00 02 <battery> <status> ...`；battery=offset 5，status=offset 6
  - K98 只发送固定只读查询；必要时用 `13 07` 版本查询唤醒，不使用 feature `0x06`，不写配置
  - 校验 0–100；失败保留旧值并标记 `Stale`/`Unavailable`
  - 不发送配置/设置写入，不读取 DPI、灯光、按键等设置
- [x] **M2.1-4 调度与状态合并**
  - Rapoo 与 K98 使用独立枚举器/探针，由 `Hid24GDeviceEnumerator` / `Hid24GProbe` 按协议分派
  - 无蓝牙适配器时仍继续枚举/读取 2.4G
  - 2.4G 枚举失败时保留旧状态，不误判离线；连续两轮缺失才移除
  - 2.4G 读取优先于 BLE 轮次，避免被 BLE 单轮预算挤掉
- [x] **M2.1-5 UI 与文档**
  - 主窗口摘要分别统计 BLE / 经典 / 2.4G
  - 托盘摘要包含 2.4G 电量；无蓝牙适配器但 2.4G 可用时不被全局状态覆盖
  - README / ARCHITECTURE / UI-SPEC / DEVELOPMENT / CHANGELOG 同步

### 验收

1. Rapoo 接收器可见，显示为“2.4G 接收器”，不显示为 BLE/经典蓝牙。
2. 官方 Rapoo 应用显示 70% 时，BlueMeter 读取到 70%（允许同一报告周期的正常波动）。
3. LINGBAO K98 接收器可见，读取值与厂商应用一致；实测 53%。
4. Rapoo 读取过程只调用 `HidD_GetInputReport`；K98 只发送固定的 `13 4A` 只读查询（必要时 `13 07` 唤醒）；两者都不写配置、不修改 DPI/灯光/按键。
5. 拔掉接收器后下一轮标记不可达，连续两轮后移除；重新插入后恢复。
6. 无蓝牙适配器的机器上，2.4G 读取仍正常工作。
7. 其他 LINGBAO 型号或未确认协议设备不出现伪造的 2.4G 电量。

> 验收状态：当前实机已通过 Rapoo `24AE:1861` 读取 69–70%，通过 LINGBAO K98 `3554:FA09` 读取 53%，且 `dotnet build -c Release` 零警告零错误。BLE 真实 GATT、睡眠唤醒仍受无蓝牙适配器环境限制。
>
> 2026-10-28 追加观察：K98 在键盘长时间静止后单轮读取失败 21/75（约 28%），失败阶段均为 `HidRead` 超时。`K98RetrySchedule` 已改为 4 次尝试、递增读超时、重发前唤醒与单次尝试内补发一次查询（单设备上限 6.5 秒），失败日志按连续次数降噪，轮次日志输出 2.4G 累计失败率；修复后的失败率待实机复测。

---

## M3 — 经典蓝牙、低电量通知与托盘摘要 🚧

**目标**：补齐系统属性可读的经典设备；低电量只提醒一次；托盘摘要准确。

> `0.4.0` 代码已完成，`0.4.1` 做了实测修正。经典蓝牙电量连接后每轮重读系统属性，属性缺失且 LE 侧可连接时按地址走 GATT 兜底；低电量告警由 `LowBatteryNotifier` 合并后发托盘气泡；托盘摘要统一由 `TraySummaryBuilder` 构建。实机已枚举到 1 台经典蓝牙设备（HUAWEI FreeBuds 7i），它既不暴露系统电量属性、LE 侧也不可连接，因此显示“暂时不可达”；带电量属性或可连接 BLE 的经典设备仍需补验。

### 任务

- [x] **M3-1 经典蓝牙读取**
  - 从 `DeviceInformation.Properties` 读取 `System.Devices.BatteryLife`（`byte`）；连接状态下每轮 `CreateFromIdAsync` 重读一次属性
  - `101` 视为未知；`BatteryPlusCharging` 的 `+100` 编码用于识别充电状态
  - `BatteryPlusChargingText` 作为状态文案补充（可选）
  - 属性缺失且 `Aep.Bluetooth.Le.IsConnectable` 不为 `false` 时，按蓝牙地址走 GATT `0x180F` 兜底
  - 两条路径都读不到时标 `Unavailable`（界面“暂时不可达”）并下轮重试；只有设备明确无电池服务时才标 `NotSupported`
- [x] **M3-2 低电量规则**
  - 默认阈值 20%，可配置 5–50%
  - 触发：首次有效读数 ≤ 阈值，或从 > 阈值降到 ≤ 阈值
  - 每设备闩锁 `AlertLatch`；读数回升到 `阈值 + 5` 以上才重新武装
  - 只在 `Ok` 状态告警；`Stale` / `Unavailable` / `NotSupported` 不告警
  - 同一轮多设备告警合并为一条气泡，避免轰炸
- [x] **M3-3 托盘气泡**
  - `TaskbarIcon.ShowNotification("BlueMeter", message, NotificationIcon.Warning)`
  - 文案示例：`鼠标电量低：15%`、`2 个设备电量低，最低：键盘 12%`
  - 通知被系统关闭/专注助手拦截时不重试、不崩溃，仅记录日志
- [x] **M3-4 托盘 ToolTip**
  - 有有效电量：单设备 `鼠标 15%`；多设备 `3 个设备，最低 15%`
  - 只有旧值：`鼠标 15%（暂时不可达）`
  - 无设备：`BlueMeter · 未发现蓝牙或 2.4G 设备`
  - 扫描中且无数据：`BlueMeter · 正在扫描…`
  - 超长名称截断，整串 ≤ 120 字符
- [x] **M3-5 托盘菜单联动**
  - “立即刷新”调用服务并禁用至本轮结束
  - “退出”走统一退出流程
  - 菜单项支持键盘操作与高对比度

### 实现说明

- 经典蓝牙电量优先读系统 AEP 属性；枚举拿到的是快照，设备连上后系统会补写属性，所以连接状态下每轮用 `DeviceInformation.CreateFromIdAsync`（2 秒超时）重读一次。
- 属性仍缺失且 `Aep.Bluetooth.Le.IsConnectable` 不为 `false` 时，用 `BluetoothLEDevice.FromBluetoothAddressAsync` 打开 LE 通道读 GATT `0x180F`；非可连接设备直接跳过，避免每轮白等 8 秒。
- 设备离线时属性只是系统缓存值：保留读数但标 `Stale`，不刷新 `LastSuccessAt`，也不触发低电量告警。
- 低电量告警在 `LowBatteryNotifier` 里做 1 秒合并窗口，同一设备重复触发以最后一次读数为准；`LowBatteryNotificationsEnabled` 关闭时静默丢弃。
- 蓝牙不可用时，经典蓝牙设备与 BLE 设备一起标为不可达，避免残留“已读取”状态。

### 验收

1. 经典蓝牙鼠标若系统属性有电量，能显示；属性缺失但 LE 可连接时能走 GATT 读到；两者都不可用时显示“暂时不可达”。
2. 电量跌破阈值只弹一次；回升再跌破可再次提醒。
3. 关闭通知后功能不报错。
4. 托盘摘要与实际最低设备一致，长度不超限。
5. 托盘菜单“立即刷新”不产生并发扫描。

> 验收状态：2026-10-28 实机（蓝牙适配器 BARROT USB）已枚举到 1 台经典蓝牙设备（HUAWEI FreeBuds 7i）。实测 `System.Devices.BatteryLife` 为 null、`Aep.Bluetooth.Le.IsConnectable=False`、按地址打开 GATT 返回 `Unreachable`，现在正确显示“暂时不可达”而不是“不支持电量读取”（Windows 设置页的数值走系统内部通道，第三方应用不可读）。带电量属性的经典设备（多数鼠标/键盘）仍需补验第 1、4 条。
>
> 日志复核：`经典蓝牙属性：HUAWEI FreeBuds 7i → BatteryLife=null，BatteryPlusCharging=null，解析=无` 已稳定输出，确认该耳机不向第三方暴露电量属性。

---

## M4 — Fluent 主窗口与列表 🚧

**目标**：完成 Windows 11 风格的设备列表、状态表达与设置入口。

### 任务

- [x] **M4-1 窗口外壳**
  - 根元素 `ui:FluentWindow`
  - `WindowBackdropType="Mica"`、`WindowCornerPreference="Round"`
  - `ExtendsContentIntoTitleBar="True"` + `ui:TitleBar`
  - 标题 `BlueMeter`，图标与托盘一致；支持 Snap Layout
  - 尺寸 780×600，最小 560×420，居中启动
- [x] **M4-2 设备列表**
  - `ui:ListView` 或 `ItemsControl` + 虚拟化
  - 列表项：类型图标、名称、类型/状态、电量百分比、电量条、最后更新时间
  - 低电量行高亮；不可达行降透明度并显示上次值
  - 排序：低电量优先 → 可用状态优先 → 名称
  - 滚动条使用 Fluent 样式
- [x] **M4-3 电量条**
  - 自绘 `BatteryBar`（圆角轨道 + 填充），绑定 `0–100`
  - 颜色：`≤阈值` 危险色；`阈值 < x < 阈值 + 20` 警告色；`≥阈值 + 20` 强调色/成功色
  - 动画 150ms，尊重“减少动画”设置
- [ ] **M4-4 页面状态**（加载/空状态已完成，剩余全部不可达提示与扫描失败 `InfoBar`）
  - 首次加载：进度环 + “正在扫描蓝牙与 2.4G 设备…”
  - 空状态：图标 + “未发现蓝牙或 2.4G 设备” + 配对/插入指引
  - 全部不可达：提示检查蓝牙开关/设备电源，而非空白
  - 扫描失败：非阻塞 `InfoBar` + 重试按钮
- [x] **M4-5 头部与底部**
  - 头部：设备总数、最低电量、最后扫描时间
  - 底部：立即刷新按钮、自动刷新状态、设置入口
- [x] **M4-6 设置**
  - 刷新间隔（自定义秒/分钟/小时，10 秒–24 小时）
  - 低电量阈值（5–50%）
  - 通知开关
  - 启动时最小化到托盘
  - 开机启动（HKCU Run，可选）
  - 设置即时生效并持久化
  - 注：`0.4.2` 已用主窗口覆盖面板接线上述全部项（含 `StartupRegistrationService` 与 `DeviceListProjector`），M4 只需换成正式设置页与 Fluent 控件
  - 现状：设置已改为底栏触发的卡片式面板（`IsSettingsOpen`），控件与持久化沿用 `0.4.2`
- [x] **M4-7 明细刷新**
  - 每 30 秒刷新“x 分钟前”相对时间
  - 窗口隐藏时暂停相对时间计时器，显示时立即刷新
- [ ] **M4-8 无障碍与适配**（自动化名与 Esc/F5/Ctrl+, 已完成，剩余列表行键盘焦点）
  - `AutomationProperties.Name` 覆盖图标按钮与电量
  - 支持 100%–200% 缩放、高对比度
  - 键盘 Tab 顺序、Enter/Space 激活、Esc 关闭到托盘
  - 文本不依赖颜色单独传达状态

### 验收

1. Win11 上 Mica/圆角/标题栏/Snap Layout 正常；Win10 优雅降级。
2. 明暗主题、强调色切换即时生效，文字对比度可读。
3. 列表在 20 台设备下滚动流畅，无 UI 线程阻塞。
4. 空状态、加载、全部不可达、读取失败四种场景均有明确反馈。
5. 高对比度模式下低电量与不可达状态仍可区分。
6. 设置修改后无需重启即可生效。

> 验收状态：2026-10-28 实机完成第 2、6 条（780×600 卡片布局、Rapoo 67% 蓝色电量条、K98 淡显 `--` + “按键唤醒后刷新”、头部“2 个设备 · 最低 67%”、底栏提示与状态点）；设置卡片可展开并写入 `settings.json`，“立即刷新”立起第 2 轮扫描；窗口缩到 560×520 后行按电量升序重排、元信息正常截断。第 3 条 20 台并发与第 5 条高对比度仍待补验。

---

## M5 — 稳定性与兼容性加固 ⏳

**目标**：把“能跑”变成“敢长期挂在托盘里”。

### 任务

- [ ] **M5-1 异常矩阵落地**
  - 覆盖设备不可达、访问被拒、GATT 服务缺失、读取超时、对象已释放、枚举后设备变化
  - 所有异常带设备上下文记录，单设备失败不终止整轮
- [ ] **M5-2 资源与性能**
  - 用任务管理器/PerfView 观察 24 小时句柄、内存、GATT 对象
  - 空闲 CPU < 0.5%，常驻内存目标 < 150 MB
  - 窗口隐藏时停止不必要的 UI 计时器
- [ ] **M5-3 生命周期边界**
  - 睡眠/唤醒、锁屏/解锁、注销/关机、Explorer 重启、蓝牙服务重启
  - 退出时无未观察任务异常
- [ ] **M5-4 兼容性**
  - Windows 10 1809 / 21H2、Windows 11 22H2 / 23H2 / 24H2
  - x64 必测；ARM64 有时间则测
  - 多显示器、不同 DPI、远程桌面
- [ ] **M5-5 日志与诊断**
  - 日志级别（Info/Warn/Error）
  - 提供“打开日志目录”入口（设置页或关于）
  - 日志脱敏检查
- [ ] **M5-6 手测矩阵**
  - 执行 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#6-手测矩阵) 全部用例
  - 记录结果与已知问题

### 验收

1. 24 小时挂机无崩溃、无明显内存增长。
2. 手测矩阵 P0 用例全部通过，P1 无阻塞性缺陷。
3. 睡眠唤醒后 60 秒内恢复电量更新。
4. Explorer 重启后托盘图标自动恢复。
5. 退出流程 3 秒内完成，无残留进程。

---

## M6 — 1.0 发布 ⏳

- [ ] 版本号统一为 `1.0.0`（csproj / AssemblyInfo / 关于页）
- [ ] Release 构建零警告（至少零错误，警告逐条评估）
- [ ] 自包含与框架依赖两种发布包校验
- [ ] README、CHANGELOG、截图更新
- [x] 许可协议确定：MIT（[LICENSE](LICENSE)）
- [ ] 在干净 Windows 虚拟机做首次运行测试
- [ ] 输出已知问题清单

**1.0 出口标准**：一期 P0 功能全部可用；无已知崩溃；文档与实际行为一致。

---

## 二期 Backlog（1.1+）

按价值/风险排序，未承诺：

| 优先级 | 功能 | 说明 |
| --- | --- | --- |
| P1 | 经典蓝牙 RFCOMM `IPHONEACCEV` 监听 | 覆盖 AirPods 等不暴露系统属性的设备；需要处理配对、RFCOMM 通道发现、AT 事件解析 |
| P1 | 每设备独立阈值/静音 | 鼠标阈值 20%，耳机 10% 等 |
| P2 | 电量历史与趋势 | 本地 SQLite/JSONL，主窗口迷你图表 |
| P2 | 开机自启管理 UI | HKCU Run + 启动参数，含“启动即托盘” |
| P2 | 多语言 | 中/英资源化，`*.resx` |
| P2 | MSIX 打包 | 补 `bluetooth` 能力、`DeviceCapability`，自动更新 |
| P3 | 主题/密度选项 | 紧凑模式、仅托盘模式 |
| P3 | 设备别名与隐藏 | 用户自定义名称、忽略设备 |
| P3 | 通知操作按钮 | “静音此设备 1 小时” |

---

## 风险清单

| 风险 | 概率 | 影响 | 缓解 |
| --- | --- | --- | --- |
| 经典蓝牙电量属性在多数设备上不可用 | 高 | 中 | 明确显示“不支持”；二期 RFCOMM；不承诺全设备覆盖 |
| BLE 设备休眠/被厂商软件占用导致读取失败 | 高 | 中 | 超时、保留旧值、状态标注；串行低频；不自动重试风暴 |
| 2.4G 协议仅覆盖已确认接收器 | 高 | 中 | 只匹配 Rapoo `24AE:1861/col05` 与 K98 `3554:FA09/col01`；不猜测其他 LINGBAO/2.4G 设备；文档明确不支持范围 |
| 厂商应用独占 HID 句柄 | 中 | 低 | 只读共享打开；失败标 `Unavailable`，下轮重试；提示关闭厂商软件 |
| GATT 连接引发额外耗电 | 中 | 中 | 默认 45 秒轮询；通知优先；单轮预算；后续可加“仅充电时高频” |
| H.NotifyIcon 与 WPF-UI 版本升级破坏兼容 | 中 | 中 | 锁定 2.3.2 / 4.3.0；升级前跑手测矩阵 |
| .NET 8 于 2026-11 结束支持 | 高 | 中 | 1.0 后评估 .NET 10；届时 H.NotifyIcon 可升 2.4.x |
| Windows 通知被系统设置/专注助手拦截 | 中 | 低 | 不重试、不崩溃；主窗口仍有视觉提示 |
| 多设备同名/地址去重误判 | 中 | 低 | 优先地址去重；无地址时保留双条目并标注类型 |
| 托盘 ToolTip 超长被截断 | 中 | 低 | 统一构建器，≤120 字符，超长截断设备名 |
| Win10 上 Mica 不可用 | 高 | 低 | WPF-UI 自动退化；UI 不依赖透明背景 |
| 单实例唤醒实现复杂 | 低 | 低 | 首版允许第二实例直接退出，唤醒可后补 |

---

## 完成定义（Definition of Done）

一个任务只有同时满足以下条件才算完成：

- 代码通过 `dotnet build -c Release`，无新增警告（或已记录豁免理由）。
- 失败路径有日志、有 UI 状态、有超时，不出现未处理异常。
- 新增/修改行为已跑对应手测用例。
- 文档（README / ARCHITECTURE / UI-SPEC）与实现一致。
- 无新增第三方 UI 框架或 WinForms 托盘依赖。
