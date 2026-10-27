# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 的结构，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added

- 待实现：经典蓝牙电量、低电量通知与托盘摘要（M3）
- 待实现：Fluent 主窗口、完整设备列表与设置（M4）

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
