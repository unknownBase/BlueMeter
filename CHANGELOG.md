# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 的结构，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added

- 待实现：Fluent 主窗口、完整设备列表与设置（M4）

### Changed

- 2.4G 读取失败日志降噪：同一设备连续失败只在第一轮记 `Warn`，之后记 `Debug` 并带连续次数，恢复时记 `Info`；轮次完成日志追加 2.4G 累计成功率，便于量化稳定性。

### Fixed

- LINGBAO K98 2.4G 电量查询改为 4 次尝试（读超时 1.0 / 1.4 / 1.8 / 1.8 秒，退避 100 / 200 / 300 毫秒，单设备 6.5 秒预算）：每次重发前发送只读版本查询唤醒接收器，并在单次尝试内补发一次查询，降低接收器丢弃命令导致的 `HidRead` 超时。

### Notes

- `K98RetrySchedule` 与 `ProbeHealthTracker` 为纯逻辑类型，新增 11 个单元用例；`dotnet test` 共 58 个用例全部通过。

## [0.4.2] - 2026-10-28

### Added

- 主窗口“设置”面板：刷新间隔（自定义 10 秒–24 小时，按秒 / 分钟 / 小时输入）、低电量阈值（5–50%）、低电量气泡、BLE 通知订阅、显示不支持电量的设备、开机启动、启动时最小化共 7 项，修改即保存并立即生效。
- `RefreshIntervalConverter`：秒数与“数值 + 单位”互转，设置文件只存秒数，界面按最大可整除单位显示（如 120 秒显示为 2 分钟）。
- `StartupRegistrationService`：通过 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 读写开机启动项，不需要管理员权限；启动时对齐设置与注册表（路径变化补写、关闭后清残留）。
- `DeviceListProjector`：把快照投影成列表行，集中排序规则并执行 `ShowUnsupportedDevices` 过滤。

### Changed

- `RunAtStartup`、`ShowUnsupportedDevices`、`RefreshIntervalSeconds`、`LowBatteryThreshold` 从“只存在于配置文件”变为真正接线的可用设置。
- 刷新间隔不再限在 30–60 秒，扩大到 10 秒–24 小时，可自由输入数值并选单位。
- 轮询抖动从固定的 ±5 秒改为间隔的 10%（限制在 1–60 秒），避免 10 秒轮询被拖成 15 秒、小时级间隔抖动过小。
- 设置保存或注册表写入失败时回滚并弹出托盘提示，界面不再与磁盘/系统状态不一致。
- 列表过滤后摘要会标注“已隐藏 N 台不支持电量读取的设备”；全部被隐藏时给出可在设置中恢复的提示。
- 版本提升至 0.4.2。

### Notes

- 刷新间隔在下一轮生效（循环每轮重新读取设置）；低电量阈值在下一次成功读取后生效。
- `dotnet test` 在原有 26 个用例之上新增 21 个（列表投影 5 个、开机启动命令拼装 4 个、间隔换算与范围校验 12 个），共 47 个全部通过。

## [0.4.1] - 2026-10-28

### Added

- 经典蓝牙连接时每轮重新读取系统电量属性（`DeviceInformation.CreateFromIdAsync`，2 秒超时），不再只用枚举阶段的快照，设备连上后系统补上的电量能被读到。
- 经典蓝牙没有系统属性时，按蓝牙地址走 BLE GATT 兜底（`BleBatteryProbe.ReadByBluetoothAddressAsync`，电池服务 `0x180F` / 电量特征值 `0x2A19`）；仅对 `System.Devices.Aep.Bluetooth.Le.IsConnectable` 不为 `false` 的设备尝试，避免每轮白等 8 秒。
- 枚举日志新增经典蓝牙电量属性原文（`BatteryLife` / `BatteryPlusCharging` / 解析结果），便于定位“系统有电量但应用读不到”。

### Changed

- 经典蓝牙不再因为一时读不到就判定“不支持电量读取”：未连接、系统未上报且 BLE 不可连接时统一标记 `Unavailable`，界面显示“暂时不可达”，下一轮继续重试。
- 版本提升至 0.4.1。

### Notes

- 实测 HUAWEI FreeBuds 7i（经典蓝牙已连接）：`System.Devices.BatteryLife` 为 null、`Aep.Bluetooth.Le.IsConnectable=False`、按蓝牙地址打开 GATT 返回 `Unreachable`。这类耳机只把电量报给 Windows 自己的 UI，第三方应用暂无可读接口，因此显示“暂时不可达”而非误报“不支持”。

## [0.4.0] - 2026-10-28

### Added

- 经典蓝牙电量读取：枚举阶段缓存 `System.Devices.BatteryLife` 与 `System.Devices.BatteryPlusCharging`，读取阶段直接应用，不建立 GATT 连接、不占用单轮读取预算。
- `SystemBatteryPropertyReader`：统一解析系统电量属性，`BatteryPlusCharging` 的 100–200 识别充电态，`101`/`0` 与越界值视为未知。
- 经典蓝牙设备离线时保留系统缓存值并标记 `Stale`，不当作实时读数、不触发低电量告警。
- `LowBatteryNotifier`：低电量提醒在 1 秒窗口内合并为一条托盘气泡，单设备显示“鼠标电量低：15%”，多设备显示“2 个设备电量低，最低：键盘 12%”。
- `TraySummaryBuilder`：托盘 ToolTip 统一构建，覆盖无设备、扫描中、单设备、多设备、只有旧值五种状态，整串 ≤ 120 字符。
- 低电量通知开关 `LowBatteryNotificationsEnabled` 生效；关闭后只记录日志，不弹气泡。

### Changed

- 蓝牙不可用时，经典蓝牙设备与 BLE 设备一起标记为不可达，避免残留“已读取”状态。
- 经典蓝牙读取失败日志归类为 `ClassicProbe`，`NotSupported` 只记 Debug。
- 主窗口移除“经典蓝牙电量待 M3 接入”占位文案。
- 版本提升至 0.4.0。

### Fixed

- 退出应用时 `SystemThemeWatcher.UnWatch` 在窗口句柄已销毁后抛 `InvalidOperationException`，被全局异常处理器记为错误；现改为忽略并记 Debug。
- 手动刷新在无设备时不再显示上一轮的托盘摘要，改为“正在扫描…”。

### Notes

- 本机已检测到蓝牙适配器（BARROT USB）。实机枚举到 1 台经典蓝牙设备（HUAWEI FreeBuds 7i），系统未暴露 `System.Devices.BatteryLife`，按设计显示“不支持电量读取”；带有电量属性的经典/BLE 设备需补验。
- 新增 `BlueMeter.Tests`（xUnit，26 个用例）：覆盖属性解码、托盘摘要、低电量文案与合并窗口；`dotnet test` 全部通过。

## [0.3.1] - 2026-10-27

### Added

- Rapoo 2.4G 只读电量支持：`VID_24AE&PID_1861` 的 `MI_01/COL05` HID 接口，`HidD_GetInputReport(0xBB)` 读取电量。
- LINGBAO K98 2.4G 只读电量支持：`VID_3554&PID_FA09` 的 `MI_01/COL01` HID 接口，发送固定 20 字节 `13 4A 01 ...` 查询并解析 `13 4A 01 00 02 <battery> <status> ...`。
- 新增独立的 `Hid24GDeviceInfo` 身份模型、`Hid24GProtocol` 与 `DeviceTransportKind.Hid24G`，UI 明确显示“2.4G 接收器”，不与 BLE/经典蓝牙合并。
- 新增 `HidInterop`（SetupAPI/HID P/Invoke，含 K98 固定查询的 overlapped 读写）、`Rapoo24GDeviceEnumerator`、`Rapoo24GProbe`、`K98_24GDeviceEnumerator`、`K98_24GProbe`，以及合并/分派用的 `Hid24GDeviceEnumerator`、`Hid24GProbe`。
- 无蓝牙适配器时继续读取 2.4G 接收器；2.4G 枚举失败保留旧状态，连续两轮缺失才移除。
- 主窗口摘要分别统计 BLE / 经典蓝牙 / 2.4G；托盘摘要纳入 2.4G 电量。
- 文档记录 Rapoo 与 K98 的协议事实、只读边界、厂商应用独占风险与支持范围。

### Changed

- `BluetoothDeviceKind` → `DeviceTransportKind`（`BluetoothLe` / `BluetoothClassic` / `Hid24G`）。
- `BluetoothBatteryService` → `DeviceBatteryService`；`IBluetoothBatteryService` → `IDeviceBatteryService`。
- `BluetoothServiceStatus` / `BluetoothServiceState` → `DeviceServiceStatus` / `DeviceServiceState`。
- 2.4G 枚举结果拆分为 `Hid24GEnumerationResult`，Rapoo/K98 各自维护枚举器与探针，由 `Hid24GDeviceEnumerator` / `Hid24GProbe` 按协议分派。
- 2.4G 探针每次返回完整充电状态：不充电时清除上一次的“充电中”，避免状态残留。
- 版本提升至 `0.3.1`。

### Notes

- 2.4G 支持严格限定为 Rapoo `24AE:1861/col05` 与 LINGBAO K98 `3554:FA09/col01` 的已确认协议；不承诺通用 2.4G/USB 接收器，其他 LINGBAO 型号不套用 K98 协议。
- 不向 `col04`/`0xBA` 配置通道发送任何报告，不修改 DPI、灯光、按键或设备设置；K98 只发送固定只读查询与必要的 `13 07` 唤醒查询。
- 不使用 K98 的 feature `0x06` 猜测电量，也不依赖 `KB.ini` 的 `ShowPower` 开关；厂商电量 UI 只是旁证。
- 当前实机已验证 Rapoo 接收器读取 69–70%、LINGBAO K98 读取 53%；BLE 真实 GATT 读取仍受开发机无蓝牙适配器限制。

## [0.3.0] - 2026-10-27

### Added

- M2 领域模型：`BluetoothDeviceKind`、`BatteryReadStatus`、`DeviceBatteryState`、`DeviceBatterySnapshot`、`BatteryProbeResult`、`LowBatteryNotice`、`BluetoothServiceStatus`。
- `BluetoothBatteryService`：BLE/经典已配对设备枚举、按蓝牙地址去重、同一设备优先 BLE、离线设备保留与移除、状态合并、结构化日志。
- `BleBatteryProbe`：GATT 电池服务 `0x180F` / 电量特征值 `0x2A19`、`Uncached` 读取、`PresentationFormats` 指数缩放、0–100 校验、单设备超时。
- BLE 通知订阅：Notify/Indicate CCCD、`ValueChanged` 解析、按设备保存、订阅上限 16、断连/15 分钟无通知/退出释放。
- 刷新调度：`PeriodicTimer` + ±5 秒抖动、`SemaphoreSlim` 串行、单设备 8 秒、单轮 30 秒预算、手动刷新排队、睡眠恢复立即刷新。
- 状态事件：`DevicesUpdated`、`LowBatteryDetected`、`StatusChanged`；失败保留旧值并标记 `Stale`，无旧值标记 `Unavailable`。
- 低电量阈值判断与 `LowBatteryAlerted` 闩锁；M3 将消费事件并聚合为托盘气泡。
- 主窗口轻量设备预览列表：名称、BLE/经典类型、连接/状态/更新时间、电量百分比；支持窗口内“立即刷新”。
- 托盘“立即刷新”启用并接入服务；托盘 ToolTip 显示设备数、最低电量或无适配器/蓝牙关闭状态。
- 退出流程等待设备服务最多 3 秒，正常释放 GATT 服务、设备与通知订阅。

### Changed

- 设备属性键由无效的 `System.Devices.Aep.DeviceCategory` 改为 `System.Devices.Aep.Category`，避免 `COMException 0x8002802B`。
- 更新 README、ROADMAP、ARCHITECTURE、DEVELOPMENT 与 CHANGELOG 的 M2 状态、项目结构与验证说明。
- 版本提升至 `0.3.0`。

### Notes

- M2 只读取 BLE 电量；经典蓝牙设备完成枚举与分类，真实电量读取留到 M3。
- 当前开发机无蓝牙适配器，已验证空列表、无适配器状态、日志与退出路径；真实 BLE GATT 读取、睡眠唤醒和蓝牙开关切换需在有适配器的实机补验。
- 主窗口当前为 M2 预览列表，完整 Fluent 列表、排序、设置与低电量通知在 M3/M4 完成。

## [0.2.0] - 2026-10-27

### Added

- 采用仓库已有的 MIT 许可，补齐 README 许可说明与链接。
- 新增 `CONTRIBUTING.md`：单行 Conventional Commits 规范（`type: 中文简述`）、type 枚举、示例与提交前检查。
- M1 工程配置：`net8.0-windows10.0.19041.0`、最低 Windows 10 1809、WPF-UI `4.3.0`、H.NotifyIcon.Wpf `2.3.2`。
- 多尺寸应用图标 `Assets/BlueMeter.ico`（16/20/24/32/48/64/256），资源打包并复用于窗口、托盘和 exe。
- `app.manifest`：Per-Monitor V2 DPI、Windows 10/11 兼容性声明。
- `TrayIconService`：托盘图标、左键显示/还原、右键菜单（显示、立即刷新占位、退出）、ToolTip 截断。
- 窗口生命周期：关闭隐藏到托盘；托盘“退出”统一释放托盘并结束进程；支持启动时最小化到托盘。
- 单实例：`Local\BlueMeter.SingleInstance` 命名互斥体，第二实例静默退出。
- `SettingsService`：`%LOCALAPPDATA%\BlueMeter\settings.json` 原子读写、范围校验、损坏文件备份与默认值回退。
- `AppLog`：`%LOCALAPPDATA%\BlueMeter\logs\bluemeter-yyyyMMdd.log`，按天、5 MB 滚动、保留 7 天、蓝牙地址掩码。
- 全局异常处理：Dispatcher、AppDomain、未观察任务异常；注销/关机时直接退出。
- 主窗口 Fluent 外壳：`ui:FluentWindow`、Mica、圆角、`ui:TitleBar`、系统主题跟随。

### Changed

- 移除 `StartupUri`，由 `App.OnStartup` 手动创建单实例、设置、日志、托盘与窗口。
- README 更新当前状态、项目结构与使用说明。

### Notes

- 设备扫描、BLE/经典蓝牙电量读取尚未接入；托盘“立即刷新”为禁用占位。
- 版本提升至 `0.2.0`；M1 验收中的 Win10 1809 与 Win11 兼容性需在对应实机完成最终手测。

## [0.1.0-docs] - 2026-10-27

### Added

- README：产品定位、一期范围、技术栈、快速开始、已知限制、API 事实核对。
- ROADMAP：M0–M6 里程碑、任务拆分、验收标准、二期 backlog、风险清单。
- `docs/ARCHITECTURE.md`：分层、组件、设备发现、BLE/经典读取、状态机、调度、错误矩阵、线程模型、生命周期。
- `docs/UI-SPEC.md`：窗口布局、列表项、电量条、状态文案、托盘菜单/气泡、无障碍与响应式。
- `docs/DEVELOPMENT.md`：环境准备、工程配置、运行前检查、手测矩阵、故障排查、编码约定。
- 核对并记录关键依赖版本：WPF-UI `4.3.0`、H.NotifyIcon.Wpf `2.3.2`（2.4.1 无 net8 资产）。
- 核对并记录 WinRT/托盘 API 差异：配对选择器、`System.Devices.BatteryLife`、`ShowNotification`、`PresentationFormats`。

### Notes

- 仓库仍为空的 WPF 脚手架，本版本仅包含文档，不含功能实现。
- 文档基线对应的目标框架：`net8.0-windows10.0.19041.0`，最低系统 `10.0.17763.0`。
