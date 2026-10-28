# BlueMeter

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> 把每一只蓝牙鼠标、键盘、耳机和手柄，以及已确认的 Rapoo / LINGBAO K98 2.4G 接收器设备，安静地放进系统托盘。

BlueMeter 是一个常驻 Windows 系统托盘的轻量桌面应用，用来汇总本机**已配对蓝牙设备**和**已确认的 Rapoo / LINGBAO K98 2.4G 接收器设备**的电量。它覆盖 BLE（低功耗蓝牙）、经典蓝牙（BR/EDR）以及厂商专用 2.4G HID 接收器（Rapoo、LINGBAO K98）；能读到的设备显示实时电量，读不到的设备保留上次成功值并标注“暂时不可达”。

> 说明：经典蓝牙与 BLE 都工作在 2.4GHz 频段；2.4G 支持目前**仅限已确认的 Rapoo 接收器**（`VID_24AE&PID_1861` 的 `MI_01/COL05` 只读报告）和**已确认的 LINGBAO K98 接收器**（`VID_3554&PID_FA09` 的 `MI_01/COL01` 固定只读查询）。它不属于蓝牙，也不承诺通用 2.4G、私有接收器或 USB 设备；其他 LINGBAO 型号和未确认协议设备暂不支持。

主窗口使用 Windows 11 Fluent 风格（Mica/Acrylic、系统主题与强调色跟随），关闭窗口只隐藏到托盘，托盘图标是应用的常驻入口。

---

## 当前状态

| 项目 | 状态 |
| --- | --- |
| 文档（README / 路线图 / 架构 / UI / 开发指南 / 贡献指南） | ✅ 已完成（`0.1.0-docs`） |
| 工程配置与依赖 | ✅ 已完成（`0.2.0`） |
| 托盘与窗口生命周期 | ✅ 已完成（`0.2.0`） |
| 已配对设备发现与 BLE/经典分类 | ✅ 已完成（`0.3.0`） |
| BLE 电量读取与通知订阅 | ✅ 已完成（`0.3.0`） |
| Rapoo / LINGBAO K98 2.4G 接收器只读电量 | ✅ 已完成（`0.3.1`） |
| 经典蓝牙电量读取 | ✅ 已完成（`0.4.0`，`0.4.1` 补 GATT 兜底） |
| 低电量通知与托盘摘要 | ✅ 已完成（`0.4.0`） |
| 设置接线：开机启动 / 列表过滤 / 刷新间隔与阈值 | ✅ 已完成（`0.4.2`，面板形态） |
| Fluent 主窗口与完整列表 | ⏳ 待实施（M4；当前为轻量预览列表） |
| 稳定性加固 | ⏳ 待实施（M5） |

> 当前版本为 `0.4.2`：M1 的托盘与窗口生命周期保持可用；M2 接入已配对 BLE/经典设备枚举、地址去重、BLE GATT 电量读取、通知订阅、刷新调度与状态事件；`0.3.1` 增加 Rapoo 与 LINGBAO K98 2.4G 接收器的只读 HID 电量读取。`0.4.0` 补齐经典蓝牙电量（系统 AEP 属性）、低电量聚合托盘气泡与统一托盘摘要；`0.4.1` 让经典蓝牙在连接后每轮重读属性，并对可连接的设备追加 BLE GATT 兜底；`0.4.2` 把开机启动、列表过滤、刷新间隔与低电量阈值真正接线，并给出可用的设置面板。完整 Fluent 列表与设置页留到 M4。

当前基线：

- 目标框架：`net8.0-windows10.0.19041.0`
- 最低系统：Windows 10 1809（`10.0.17763.0`）
- 目标产物：`BlueMeter.exe`（WPF 桌面托盘应用）

---

## 一期功能范围

### 已规划并纳入 1.0

- **托盘常驻**：关闭主窗口时隐藏而不是退出；左键还原；右键菜单提供“显示主窗口 / 立即刷新 / 退出”。
- **设备发现**：枚举本机已配对的 BLE 与经典蓝牙设备，按地址去重，优先采用 BLE 电量。
- **BLE 电量**：通过 GATT 电池服务 `0x180F` / 电量特征值 `0x2A19` 读取；优先订阅 `ValueChanged`，低频轮询兜底。
- **Rapoo 2.4G 电量（只读）**：枚举 `VID_24AE&PID_1861` 接收器的 `MI_01/COL05` HID 接口，读取 `0xBB` input report；只读取，不发送 output/config 报告。显示为 `2.4G 接收器`，与 BLE/经典蓝牙严格区分。
- **LINGBAO K98 2.4G 电量（只读）**：枚举 `VID_3554&PID_FA09` 接收器的 `MI_01/COL01` HID 接口，发送固定的 20 字节只读查询（`13 4A 01 ...`），解析 `13 4A 01 00 02 <battery> <status> ...`；不写配置，不使用 feature `0x06` 猜测电量。显示为 `2.4G 接收器`。
- **经典蓝牙电量**：连接状态下每轮重读系统设备属性 `System.Devices.BatteryLife`；属性缺失且设备 LE 侧可连接时，按蓝牙地址走 GATT `0x180F` 兜底；两者都读不到时如实显示“暂时不可达”并下轮重试，不伪造数值。
- **状态保留**：每台设备维护 `LastKnownBattery`，读取失败保留上次成功值与时间，并标记“暂时不可达”。
- **低电量提醒**：电量跌破阈值（默认 20%）时弹出一次托盘气泡；带恢复迟滞，避免反复打扰。
- **Fluent UI**：`ui:FluentWindow` + Mica/Acrylic、圆角、系统标题栏、Snap Layout；列表展示名称、类型、电量、电量条、更新时间与状态。
- **主题跟随**：`SystemThemeWatcher` 实时同步系统明暗主题、强调色与背景材质。
- **启动即托盘**：可配置启动后只显示托盘图标，主窗口按需弹出。
- **单实例**：重复启动时第二个实例直接退出，不产生第二个托盘图标；唤醒已有窗口可后补。
- **本地设置**：刷新间隔、低电量阈值、通知开关、启动行为等，保存在本地 JSON。

### 明确不做（一期）

- ❌ 经典蓝牙 RFCOMM / `IPHONEACCEV` AT 指令监听（AirPods 等设备的二期方案）。
- ❌ 通用 2.4G/USB 接收器支持；只支持已确认的 Rapoo `24AE:1861` 与 LINGBAO K98 `3554:FA09` 协议。
- ❌ 向 2.4G 接收器发送配置、非查询输出报告或修改 DPI/灯光/按键设置。
- ❌ 其他 LINGBAO 型号或未确认协议的 2.4G 设备；K98 的只读协议已确认，但不能套用到其他型号。
- ❌ 扫描未配对或仅处于广播状态的设备。
- ❌ 电量历史曲线、云端同步、账号体系。
- ❌ 使用 WinForms `NotifyIcon`。
- ❌ 更换 UI 框架或引入第二套控件库。

---

## 技术栈

| 领域 | 选型 | 版本 | 说明 |
| --- | --- | --- | --- |
| 运行时 | .NET 8 | `8.0` | TFM：`net8.0-windows10.0.19041.0` |
| UI 框架 | WPF | 随 .NET 8 | `UseWPF=true`，`OutputType=WinExe` |
| Fluent 控件 | [WPF UI](https://github.com/lepoco/wpfui) | `4.3.0` | 包名 `WPF-UI`，提供 `FluentWindow`、主题与 Fluent 控件 |
| 托盘 | [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) | `2.3.2` | 包名 `H.NotifyIcon.Wpf`，2.x 系列 |
| 蓝牙 | WinRT 投影 | 随 TFM | `Windows.Devices.Bluetooth`、`Windows.Devices.Enumeration`，无需额外包 |
| 2.4G HID | Win32 P/Invoke | 随系统 | `setupapi.dll` + `hid.dll`；Rapoo 只读 `HidD_GetInputReport`，K98 使用固定只读查询的 `WriteFile/ReadFile`，不引入额外 HID 包 |
| 配置 | `System.Text.Json` | 随 .NET 8 | 保存到 `%LOCALAPPDATA%\BlueMeter\settings.json` |
| MVVM | 手写最小实现 | — | `ObservableObject` + `RelayCommand`，不额外引入 MVVM 框架 |

### 托盘与窗口解耦

`H.NotifyIcon.Wpf` 与 WPF-UI 可以共存，但职责必须分开：

- 托盘图标由 `TrayIconService` 持有 `TaskbarIcon`，不放进 `MainWindow` 的可视树或资源字典。
- 主窗口只负责 `FluentWindow`、主题与列表；关闭隐藏逻辑不依赖托盘对象。
- 两者都只在 UI 线程操作；服务事件通过 `Dispatcher` 编组，避免跨线程访问。

### 版本锁定说明

- **H.NotifyIcon.Wpf 固定 2.3.2，不使用 2.4.1。** 2.4.1 的 NuGet 包只提供 `net10.0-windows7.0` 与 `net462` 资产，无法被 `net8.0` 项目消费；2.3.2 提供 `net8.0-windows7.0` 资产。升级到 .NET 10 后再评估 2.4.x。
- **WPF-UI 固定 4.3.0**，其 `net8.0-windows7.0` 资产与本项目 TFM 兼容。
- 本机已验证可用的 SDK/运行时环境：SDK `10.0.301`、`Microsoft.WindowsDesktop.App 8.0.31`。SDK 8.0.4xx 及以上均可构建本项目。

---

## 系统要求

- Windows 10 版本 1809（build 17763）或更高；推荐 Windows 11 22H2 及以上（Mica 背景、Snap Layout 体验完整）。
- .NET 8 Desktop Runtime（框架依赖发布）；或使用 self-contained 发布。
- 可用的蓝牙适配器且系统蓝牙已开启（仅蓝牙设备需要；Rapoo/K98 2.4G 接收器不依赖）。
- 需要监测的蓝牙设备已在本机完成配对（设置 → 蓝牙和设备 → 设备）。
- Rapoo 2.4G 设备：接收器已插入，且系统识别为 `VID_24AE&PID_1861` 的 `MI_01/COL05` HID 接口。
- LINGBAO K98 2.4G 设备：接收器已插入，且系统识别为 `VID_3554&PID_FA09` 的 `MI_01/COL01` HID 接口。
- 设备支持电量上报：
  - BLE 设备需暴露标准电池服务 `0x180F`；
  - 经典蓝牙设备需在系统设备属性中暴露 `System.Devices.BatteryLife`；耳机这类经典蓝牙设备通常不暴露该属性，只能依靠 LE 侧 GATT；
  - Rapoo 2.4G 接收器需匹配已确认的 `0xBB` 只读报告格式；
  - LINGBAO K98 2.4G 接收器需匹配已确认的 `13 4A` 只读查询格式。

> 蓝牙设备要求已在本机完成配对；Rapoo/K98 2.4G 接收器不需要系统配对，插入并被识别后即可读取。未配对蓝牙设备不会出现在列表中。

---

## 快速开始

```powershell
# 还原依赖
dotnet restore .\BlueMeter.csproj

# 构建
dotnet build .\BlueMeter.csproj -c Debug

# 运行
dotnet run --project .\BlueMeter.csproj

# 单元测试（纯逻辑：电量解码、托盘摘要、低电量文案）
dotnet test .\BlueMeter.Tests\BlueMeter.Tests.csproj
```

使用 Visual Studio 2022（17.8+，安装“.NET 桌面开发”工作负载）打开 `BlueMeter.sln` 亦可。

发布：

```powershell
# 框架依赖（体积小，需要目标机安装 .NET 8 Desktop Runtime）
dotnet publish .\BlueMeter.csproj -c Release -r win-x64 --self-contained false

# 自包含（体积大，开箱即用）
dotnet publish .\BlueMeter.csproj -c Release -r win-x64 --self-contained true
```

> 不要启用 trimming。WinRT 投影与 WPF 的反射/COM 互操作在裁剪后容易在运行时失败。

运行前的完整检查清单见 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#5-运行前检查项)。

---

## 使用方式

| 操作 | 行为 |
| --- | --- |
| 左键单击托盘图标 | 显示并激活主窗口；若窗口最小化则先还原 |
| 右键单击托盘图标 | 打开菜单：显示主窗口、立即刷新、退出 |
| 关闭主窗口 | 取消关闭，隐藏窗口；托盘继续运行 |
| 最小化主窗口 | 窗口保留在任务栏；托盘图标始终可见 |
| 托盘菜单“立即刷新” | 触发一轮手动扫描；若正在扫描则最多排队一次，不并发 |
| 托盘菜单“退出” | 设置退出标志，保存设置、释放托盘与 BLE 订阅，彻底结束进程 |
| 悬停托盘图标 | 显示一行摘要；完整设备列表在主窗口中 |
| 主窗口“设置” | 打开设置面板：刷新间隔（数字 + 秒/分钟/小时，10 秒–24 小时）、低电量阈值、低电量气泡、BLE 通知订阅、显示不支持电量的设备、开机启动、启动时最小化；修改立即保存并生效 |
| 刷新间隔输入 | 填数字、选单位后按 Enter 或点到别处即生效；超范围会回成边界值，留空会恢复原值；单位切换按新单位解释当前数字 |
| 开机时自动启动 | 勾选后写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `BlueMeter` 项；取消勾选立即删除，无需管理员权限 |
| 低电量 | 电量跌破阈值（默认 20%，可在设置中改成 5–50%）时弹一条托盘气泡；多设备同轮告警合并为一条，回升到阈值 + 5% 以上才重新武装 |

托盘 ToolTip 受 Windows `NOTIFYICONDATA.szTip` 长度限制（约 127 字符），因此只显示摘要，不显示完整列表。

---

## 设计原则

1. **不崩溃优先。** 蓝牙设备随时会关机、走远、断开、被系统回收；任何单设备读取失败都只能影响该设备的状态，不能影响进程。
2. **状态诚实。** 读不到就显示“暂时不可达”或“--”，保留上次成功值时必须标注时间，不把旧值伪装成实时值。
3. **串行、低频。** 设备读取串行执行，默认 45 秒一轮，避免多路 GATT 连接竞争和额外耗电。
4. **托盘是入口，不是全部。** 托盘只给最低电量摘要，完整信息在主窗口。
5. **跟随系统。** 明暗主题、强调色、背景材质、DPI 和高对比度都交给 Windows 与 WPF-UI，不硬编码颜色。

---

## 项目结构

当前仓库（`0.4.2`）：

```text
BlueMeter/
├─ BlueMeter.sln
├─ BlueMeter.csproj
├─ App.xaml / App.xaml.cs              # 组合根：单实例、托盘、服务、窗口、异常、退出
├─ MainWindow.xaml / MainWindow.xaml.cs # FluentWindow + 轻量设备预览 + 关闭隐藏
├─ AssemblyInfo.cs
├─ app.manifest                        # Per-Monitor V2 DPI / Win10+ 兼容性
├─ Assets/
│  └─ BlueMeter.ico                    # 16–256 多尺寸应用/托盘图标
├─ Models/
│  ├─ AppSettings.cs
│  ├─ DeviceTransportKind.cs
│  ├─ Hid24GDeviceInfo.cs
│  ├─ BatteryReadStatus.cs
│  ├─ DeviceBatteryState.cs
│  ├─ DeviceBatterySnapshot.cs
│  ├─ BatteryProbeResult.cs
│  ├─ LowBatteryNotice.cs
│  └─ DeviceServiceStatus.cs
├─ Services/
│  ├─ AppLog.cs                        # 按天日志、7 天保留、地址脱敏
│  ├─ SettingsService.cs               # JSON 原子读写、损坏回退
│  ├─ StartupRegistrationService.cs    # HKCU Run 键开机启动项读写与对齐
│  ├─ TrayIconService.cs               # TaskbarIcon、菜单、ToolTip、退出
│  ├─ IDeviceBatteryService.cs
│  ├─ DeviceBatteryService.cs          # 蓝牙 + 2.4G 枚举、调度、状态、事件
│  ├─ SystemBatteryPropertyReader.cs   # 经典蓝牙 AEP 电量属性解码
│  ├─ BleBatteryProbe.cs               # GATT 读取、CCCD 订阅、断连释放
│  ├─ LowBatteryNotifier.cs            # 低电量聚合与托盘气泡
│  ├─ TraySummaryBuilder.cs            # 托盘摘要文案
│  ├─ HidInterop.cs                    # HID P/Invoke（Rapoo 只读；K98 固定只读查询）
│  ├─ Hid24GEnumerationResult.cs       # 合并枚举结果
│  ├─ Hid24GDeviceEnumerator.cs        # Rapoo/K98 枚举分发
│  ├─ Hid24GProbe.cs                   # 2.4G 探针协议分派
│  ├─ Rapoo24GDeviceEnumerator.cs      # Rapoo col05 接口枚举
│  ├─ Rapoo24GProbe.cs                 # 0xBB 只读电量报告解析
│  ├─ K98_24GDeviceEnumerator.cs       # K98 col01 接口枚举
│  └─ K98_24GProbe.cs                  # 13 4A 只读电量查询解析
├─ ViewModels/
│  ├─ DeviceItemViewModel.cs           # M2 预览行模型
│  └─ DeviceListProjector.cs           # 列表排序与“隐藏不支持设备”投影
├─ BlueMeter.Tests/                    # xUnit 单元测试（属性解码 / 托盘摘要 / 低电量文案 / 列表投影 / 开机启动命令）
├─ README.md
├─ ROADMAP.md
├─ CONTRIBUTING.md
├─ CHANGELOG.md
├─ LICENSE
└─ docs/
   ├─ ARCHITECTURE.md
   ├─ UI-SPEC.md
   └─ DEVELOPMENT.md
```

目标结构（M3–M4 完成后）：

```text
BlueMeter/
├─ App.xaml / App.xaml.cs              # 组合根：单实例、托盘、服务、窗口
├─ MainWindow.xaml / .cs               # FluentWindow + 关闭隐藏逻辑
├─ Assets/
│  └─ BlueMeter.ico                    # 多尺寸托盘/窗口图标
├─ Models/
│  ├─ DeviceTransportKind.cs
│  ├─ Hid24GDeviceInfo.cs
│  ├─ BatteryReadStatus.cs
│  ├─ DeviceBatteryState.cs
│  ├─ DeviceBatterySnapshot.cs
│  ├─ BatteryProbeResult.cs
│  ├─ LowBatteryNotice.cs
│  ├─ DeviceServiceStatus.cs
│  └─ AppSettings.cs
├─ Services/
│  ├─ IDeviceBatteryService.cs
│  ├─ DeviceBatteryService.cs          # 枚举、调度、状态管理、事件
│  ├─ BleBatteryProbe.cs               # GATT 读取与订阅
│  ├─ ClassicBatteryProbe.cs           # 系统设备属性读取
│  ├─ HidInterop.cs                    # 2.4G HID 互操作（只读查询）
│  ├─ Hid24GEnumerationResult.cs
│  ├─ Hid24GDeviceEnumerator.cs
│  ├─ Hid24GProbe.cs
│  ├─ Rapoo24GDeviceEnumerator.cs
│  ├─ Rapoo24GProbe.cs
│  ├─ K98_24GDeviceEnumerator.cs
│  ├─ K98_24GProbe.cs
│  ├─ TrayIconService.cs               # TaskbarIcon 封装
│  ├─ SettingsService.cs
│  └─ AppLog.cs
├─ ViewModels/
│  ├─ ObservableObject.cs
│  ├─ RelayCommand.cs
│  ├─ MainViewModel.cs
│  └─ DeviceItemViewModel.cs
├─ Converters/
├─ Controls/
│  └─ BatteryBar.xaml / .cs
└─ docs/
```

---

## 文档导航

| 文档 | 内容 |
| --- | --- |
| [ROADMAP.md](ROADMAP.md) | 里程碑、任务拆分、验收标准、二期 backlog、风险清单 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | 分层与组件、设备发现、BLE/经典读取流程、状态机、线程模型、错误处理、API 事实核对 |
| [docs/UI-SPEC.md](docs/UI-SPEC.md) | 窗口布局、列表项解剖、状态文案、低电量高亮、托盘菜单与气泡、无障碍 |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | 环境准备、构建发布、运行前检查、手测矩阵、故障排查、编码约定 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 分支协作、Conventional Commits 提交规范、提交前检查 |
| [CHANGELOG.md](CHANGELOG.md) | 版本变更记录 |

---

## 已知限制

| 限制 | 说明 | 影响 |
| --- | --- | --- |
| Mica 仅 Windows 11 | Windows 10 上 WPF-UI 会退化为纯色/兼容背景 | 视觉差异，不影响功能 |
| 经典蓝牙无统一电量 API | 优先读系统属性 `System.Devices.BatteryLife`，缺失时按地址走 BLE GATT；两者都不可用时显示“暂时不可达”并持续重试 | 不支持 GATT 的耳机（如 HUAWEI FreeBuds 7i 实测 `Le.IsConnectable=False`）读不到电量，Windows 设置页的数值与第三方应用不通 |
| 主窗口为 M2 预览列表 | 设备列表、电量条与完整设置页在 M4；当前设置以覆盖面板形式提供 | 当前仅提供名称、类型、状态与百分比 |
| 开机启动写当前用户注册表 | 只支持当前用户的 `HKCU` Run 项，不写 `HKLM`、不注册计划任务；移动或删除 exe 后下次启动会自动纠正或清理 | 多用户共享安装时需每台账户各自勾选 |
| BLE 需要设备可连接且广播电量服务 | 设备休眠、被厂商软件独占、未暴露 `0x180F` 时读取失败 | 显示“暂时不可达”或“--” |
| LINGBAO K98 休眠时不响应查询 | 键盘长时间静止后接收器可能不回 `13 4A` 查询；应用会按 4 次尝试重发并唤醒，仍失败时保留上次读数并标“暂时不可达” | 按任意键唤醒键盘后下一轮恢复；日志只告警一次，不刷屏 |
| 2.4G 支持是厂商专用 | 仅匹配已确认的 Rapoo `VID_24AE&PID_1861` 的 `MI_01/COL05` 与 K98 `VID_3554&PID_FA09` 的 `MI_01/COL01`；不承诺其他 2.4G/USB 接收器 | 其他接收器不显示，避免猜测协议 |
| 其他 LINGBAO 型号未支持 | K98 2.4G 的 `13 4A` 只读查询已确认，但不能套用到其他 LINGBAO 型号 | 不显示 2.4G 电量，不伪造读数；确认协议后再接入 |
| 2.4G 接收器可能被厂商应用独占 | 厂商软件持有 HID 句柄时打开可能失败 | 显示“暂时不可达”；关闭厂商软件后下一轮恢复 |
| 2.4G 只读、仅电量 | 不发送配置/设置写入，不读取 DPI、灯光、按键等设置；K98 仅发送固定只读查询 | 不会修改设备配置 |
| AirPods 等 Apple 设备 | 电量通常走私有 RFCOMM 协议 | 一期多数读不到，二期处理 |
| 托盘提示长度 | Windows 限制约 127 字符 | 托盘只显示摘要 |
| 非打包 Win32 应用 | 不需要 MSIX 能力声明，但若改为 MSIX 需补 `bluetooth` 能力 | 打包时注意 |
| .NET 8 支持周期 | .NET 8 LTS 于 2026-11 结束支持 | 1.0 后评估升级 .NET 10，本期不改栈 |

---

## 关键 API 事实核对

需求描述中有几处 API 名称与当前平台/包版本不一致，实施时以本表为准（详细理由见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#19-api-事实核对)）：

| 需求写法 | 实际采用 | 原因 |
| --- | --- | --- |
| `BluetoothLEDevice.GetDeviceSelector()` / `BluetoothDevice.GetDeviceSelector()` | `GetDeviceSelectorFromPairingState(true)` | 明确只枚举已配对设备，语义与需求一致 |
| `System.Devices.BatteryLifePercent` | `System.Devices.BatteryLife` | 系统属性键实际为 `BatteryLife`，类型 `Byte`，`101` 表示未知 |
| `TaskbarIcon.ShowBalloonTip(...)` | `TaskbarIcon.ShowNotification(..., NotificationIcon.Warning)` | H.NotifyIcon 2.3.2 提供的是 `ShowNotification`，底层同样走托盘气泡 |
| `System.Devices.Aep.DeviceCategory` | `System.Devices.Aep.Category` | 前者在 Windows 10/11 上不是有效规范键，传入 `FindAllAsync` 会抛 `COMException 0x8002802B` |
| `BluetoothLEDevice.ConnectAsync()` | 通过 GATT 访问触发连接；必要时用 `GattSession.MaintainConnection` | `BluetoothLEDevice` 没有公开的 `ConnectAsync` 方法 |
| `H.NotifyIcon.Wpf` 2.x 最新版 | 2.3.2 | 2.4.1 不含 net8 资产 |
| Rapoo 2.4G 电量查询 | 只读 `HidD_GetInputReport(0xBB)` | 已用官方应用与独立只读探测确认；不发送 output/config 报告 |
| LINGBAO K98 2.4G 电量查询 | 20 字节固定查询 `13 4A 01 ...`，解析 `13 4A 01 00 02 <battery> <status> ...` | 已用厂商 `CDev3632::ReadPower` 与独立只读探测确认；不使用 feature `0x06`，不写配置 |

低电量气泡仍使用 `TaskbarIcon.ShowNotification`，不引入 WinForms `NotifyIcon`，满足“轻量托盘气泡”的目标。

---

## 隐私

- 所有设备信息、电量与设置仅保存在本机。
- 不联网、不上报、不包含遥测。
- 日志可能包含设备名称与蓝牙地址，默认写入 `%LOCALAPPDATA%\BlueMeter\logs\`，请勿随意分享。

---

## 许可

本项目采用 [MIT License](LICENSE)。

```text
MIT License

Copyright (c) 2026 djw
```

可以自由使用、修改、分发和再许可，但需保留版权声明与许可声明。项目按“现状”提供，不附带任何明示或暗示的担保。
