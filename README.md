# BlueMeter

> 把每一只蓝牙鼠标、键盘、耳机和手柄的电量，安静地放进系统托盘。

BlueMeter 是一个常驻 Windows 系统托盘的轻量桌面应用，用来汇总本机**已配对蓝牙设备**的电量。它同时覆盖 BLE（低功耗蓝牙）与经典蓝牙（BR/EDR），能读到的设备显示实时电量，读不到的设备保留上次成功值并标注“暂时不可达”。

> 说明：经典蓝牙与 BLE 都工作在 2.4GHz 频段；本项目只处理蓝牙设备，不涉及 2.4GHz Wi-Fi、私有无线接收器或 USB 设备。

主窗口使用 Windows 11 Fluent 风格（Mica/Acrylic、系统主题与强调色跟随），关闭窗口只隐藏到托盘，托盘图标是应用的常驻入口。

---

## 当前状态

| 项目 | 状态 |
| --- | --- |
| 文档（README / 路线图 / 架构 / UI / 开发指南） | ✅ 已完成（`0.1.0-docs`） |
| 工程配置与依赖 | ⏳ 待实施 |
| 托盘与窗口生命周期 | ⏳ 待实施 |
| BLE 电量读取 | ⏳ 待实施 |
| 经典蓝牙电量读取 | ⏳ 待实施 |
| Fluent 主窗口与列表 | ⏳ 待实施 |
| 低电量通知与稳定性加固 | ⏳ 待实施 |

> 仓库目前仍是 Visual Studio 生成的空 WPF 脚手架。功能实现顺序、验收标准见 [ROADMAP.md](ROADMAP.md)。在实现完成前，直接运行只会看到一个空白窗口。

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
- **经典蓝牙电量**：读取系统设备属性 `System.Devices.BatteryLife`；读不到时明确显示“不支持”，不做伪值。
- **状态保留**：每台设备维护 `LastKnownBattery`，读取失败保留上次成功值与时间，并标记“暂时不可达”。
- **低电量提醒**：电量跌破阈值（默认 20%）时弹出一次托盘气泡；带恢复迟滞，避免反复打扰。
- **Fluent UI**：`ui:FluentWindow` + Mica/Acrylic、圆角、系统标题栏、Snap Layout；列表展示名称、类型、电量、电量条、更新时间与状态。
- **主题跟随**：`SystemThemeWatcher` 实时同步系统明暗主题、强调色与背景材质。
- **启动即托盘**：可配置启动后只显示托盘图标，主窗口按需弹出。
- **单实例**：重复启动时第二个实例直接退出，不产生第二个托盘图标；唤醒已有窗口可后补。
- **本地设置**：刷新间隔、低电量阈值、通知开关、启动行为等，保存在本地 JSON。

### 明确不做（一期）

- ❌ 经典蓝牙 RFCOMM / `IPHONEACCEV` AT 指令监听（AirPods 等设备的二期方案）。
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
- 可用的蓝牙适配器，且系统蓝牙已开启。
- 需要监测的设备已在本机完成配对（设置 → 蓝牙和设备 → 设备）。
- 设备支持电量上报：
  - BLE 设备需暴露标准电池服务 `0x180F`；
  - 经典蓝牙设备需在系统设备属性中暴露 `System.Devices.BatteryLife`。

> 系统要求的是“已配对”，不是“附近可发现”。未配对设备不会出现在列表中。

---

## 快速开始

```powershell
# 还原依赖
dotnet restore .\BlueMeter.csproj

# 构建
dotnet build .\BlueMeter.csproj -c Debug

# 运行
dotnet run --project .\BlueMeter.csproj
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
| 关闭主窗口 | 取消关闭，隐藏窗口；扫描与托盘继续运行 |
| 最小化主窗口 | 窗口保留在任务栏；托盘图标始终可见 |
| 托盘菜单“退出” | 设置退出标志，停止扫描、释放托盘与 GATT 资源，彻底结束进程 |
| 悬停托盘图标 | 显示一行摘要；完整设备列表在主窗口中 |

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

当前仓库：

```text
BlueMeter/
├─ BlueMeter.sln
├─ BlueMeter.csproj
├─ App.xaml / App.xaml.cs
├─ MainWindow.xaml / MainWindow.xaml.cs
├─ AssemblyInfo.cs
├─ README.md
├─ ROADMAP.md
├─ CHANGELOG.md
└─ docs/
   ├─ ARCHITECTURE.md
   ├─ UI-SPEC.md
   └─ DEVELOPMENT.md
```

实现完成后的目标结构：

```text
BlueMeter/
├─ App.xaml / App.xaml.cs              # 组合根：单实例、托盘、服务、窗口
├─ MainWindow.xaml / .cs               # FluentWindow + 关闭隐藏逻辑
├─ Assets/
│  └─ BlueMeter.ico                    # 多尺寸托盘/窗口图标
├─ Models/
│  ├─ BluetoothDeviceKind.cs
│  ├─ BatteryReadStatus.cs
│  ├─ DeviceBatteryState.cs
│  └─ AppSettings.cs
├─ Services/
│  ├─ IBluetoothBatteryService.cs
│  ├─ BluetoothBatteryService.cs       # 枚举、调度、状态管理、事件
│  ├─ BleBatteryProbe.cs               # GATT 读取与订阅
│  ├─ ClassicBatteryProbe.cs           # 系统设备属性读取
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
| [CHANGELOG.md](CHANGELOG.md) | 版本变更记录 |

---

## 已知限制

| 限制 | 说明 | 影响 |
| --- | --- | --- |
| Mica 仅 Windows 11 | Windows 10 上 WPF-UI 会退化为纯色/兼容背景 | 视觉差异，不影响功能 |
| 经典蓝牙无统一电量 API | 仅部分设备在系统属性中暴露 `System.Devices.BatteryLife` | 部分耳机、手柄显示“不支持” |
| BLE 需要设备可连接且广播电量服务 | 设备休眠、被厂商软件独占、未暴露 `0x180F` 时读取失败 | 显示“暂时不可达”或“--” |
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
| `BluetoothLEDevice.ConnectAsync()` | 通过 GATT 访问触发连接；必要时用 `GattSession.MaintainConnection` | `BluetoothLEDevice` 没有公开的 `ConnectAsync` 方法 |
| `H.NotifyIcon.Wpf` 2.x 最新版 | 2.3.2 | 2.4.1 不含 net8 资产 |

低电量气泡仍使用 `TaskbarIcon.ShowNotification`，不引入 WinForms `NotifyIcon`，满足“轻量托盘气泡”的目标。

---

## 隐私

- 所有设备信息、电量与设置仅保存在本机。
- 不联网、不上报、不包含遥测。
- 日志可能包含设备名称与蓝牙地址，默认写入 `%LOCALAPPDATA%\BlueMeter\logs\`，请勿随意分享。

---

## 许可

许可证待定（TBD）。在补充 `LICENSE` 之前，默认保留所有权利。
