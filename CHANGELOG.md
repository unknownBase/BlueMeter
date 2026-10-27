# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 的结构，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added

- 待实现：托盘常驻与窗口生命周期（M1）
- 待实现：蓝牙设备枚举与 BLE 电量读取（M2）
- 待实现：经典蓝牙电量、低电量通知与托盘摘要（M3）
- 待实现：Fluent 主窗口、设备列表与设置（M4）

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
