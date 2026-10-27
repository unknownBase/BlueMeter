# BlueMeter 开发指南

本文覆盖环境准备、工程配置、运行前检查、手测矩阵、故障排查与编码约定。面向在本机继续实现 BlueMeter 的开发者。

---

## 1. 环境准备

### 必需

| 项 | 要求 | 检查方式 |
| --- | --- | --- |
| Windows | 10 1809（17763）或更高；推荐 Win11 22H2+ | `winver` |
| .NET SDK | 8.0.4xx 或更高（9/10 也可构建 net8 目标） | `dotnet --list-sdks` |
| .NET Desktop Runtime | 8.x（运行框架依赖构建时） | `dotnet --list-runtimes` |
| IDE | Visual Studio 2022 17.8+，安装“.NET 桌面开发” | 可选 |
| 蓝牙 | 适配器可用、系统蓝牙开启 | 设置 → 蓝牙和设备 |
| 测试设备 | 至少一台已配对 BLE 设备；经典设备可选 | 设置 → 蓝牙和设备 |

本机当前环境（文档编写时）：

```text
SDK:     10.0.301
Runtime: Microsoft.WindowsDesktop.App 8.0.31
```

`net8.0-windows10.0.19041.0` 首次还原会下载 Windows SDK 投影引用包（`Microsoft.Windows.SDK.NET.Ref`），需要联网。若企业环境限制 NuGet，请提前配置镜像。

### 可选

- [Bluetooth LE Explorer](https://apps.microsoft.com/detail/9n0w1b2bxh6l) 或 nRF Connect：确认设备是否暴露 `0x180F` / `0x2A19`。
- PerfView / dotMemory：排查句柄与内存。
- Windows SDK 10.0.19041+：查看 WinRT 元数据（非必需）。

---

## 2. 依赖与版本策略

```xml
<ItemGroup>
  <PackageReference Include="WPF-UI" Version="4.3.0" />
  <PackageReference Include="H.NotifyIcon.Wpf" Version="2.3.2" />
</ItemGroup>
```

| 包 | 固定版本 | 原因 |
| --- | --- | --- |
| `WPF-UI` | `4.3.0` | 提供 `FluentWindow`、`TitleBar`、主题资源；含 `net8.0-windows7.0` 资产 |
| `H.NotifyIcon.Wpf` | `2.3.2` | 2.4.1 仅含 `net10.0-windows7.0` / `net462`，net8 无法消费；2.3.2 含 `net8.0-windows7.0` |

规则：

- 不使用浮动版本（`*`、`[4.*,)`），避免还原到不兼容版本。
- 升级任一包前，先跑完整手测矩阵；升级 H.NotifyIcon 时确认目标框架资产。
- 不引入 WinForms；H.NotifyIcon 的 WPF 包本身不依赖 WinForms。
- 不引入第二套 UI 控件库；Fluent 样式统一由 WPF-UI 提供。

---

## 3. 工程配置

目标 `BlueMeter.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>BlueMeter</AssemblyName>
    <RootNamespace>BlueMeter</RootNamespace>
    <ApplicationIcon>Assets\BlueMeter.ico</ApplicationIcon>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Version>0.1.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="WPF-UI" Version="4.3.0" />
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.3.2" />
  </ItemGroup>

</Project>
```

注意：

- `TargetPlatformVersion=10.0.19041.0` 与 `SupportedOSPlatformVersion=10.0.17763.0` 同时存在是刻意为之：用较新的 SDK 投影，但声明最低运行系统为 1809。
- 只能使用在 17763 上存在的 API。当前用到的配对选择器（10586）、GATT（15063）、`PresentationFormats`（15063）均满足；Mica 仅 Win11，WPF-UI 会运行时退化。
- `ApplicationIcon` 指向的文件必须存在，否则构建失败（M1 先补图标）。
- `app.manifest` 建议声明 Per-Monitor V2 DPI 与 Windows 10/11 兼容性。

`app.manifest` 参考：

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="BlueMeter.app" />

  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 / 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>

  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

### App.xaml 要点

```xml
<Application x:Class="BlueMeter.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
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

- 删除 `StartupUri`，在 `App.OnStartup` 手动创建窗口与服务。
- `ShutdownMode=OnExplicitShutdown` 是托盘应用的关键；否则关闭隐藏的窗口可能触发退出。
- `ThemesDictionary Theme` 的初始值是占位，实际主题由 `SystemThemeWatcher.Watch(this)` 接管。

### 主窗口要点

```xml
<ui:FluentWindow x:Class="BlueMeter.MainWindow"
                 xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
                 Title="BlueMeter"
                 Width="780" Height="600"
                 MinWidth="560" MinHeight="420"
                 WindowStartupLocation="CenterScreen"
                 WindowBackdropType="Mica"
                 WindowCornerPreference="Round"
                 ExtendsContentIntoTitleBar="True">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="48" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <ui:TitleBar Grid.Row="0" Title="BlueMeter" />
    </Grid>
</ui:FluentWindow>
```

```csharp
public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this); // using Wpf.Ui.Appearance; using Wpf.Ui.Controls;
    }
}
```

> 根元素改为 `ui:FluentWindow` 后，代码后置类必须从 `Wpf.Ui.Controls.FluentWindow` 派生；继续继承 `Window` 会在运行时/XAML 编译时报基类不匹配。

### 托盘要点

```csharp
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;

_trayIcon = new TaskbarIcon
{
    IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/BlueMeter.ico")),
    ToolTipText = "BlueMeter · 正在扫描…",
    Visibility = Visibility.Visible
};

_trayIcon.TrayLeftMouseDown += (_, _) => ShowMainWindow();
_trayIcon.ContextMenu = BuildContextMenu();

// 仅在图标未出现时调用；必须在 UI 线程
_trayIcon.ForceCreate();

// 低电量
_trayIcon.ShowNotification("BlueMeter", "鼠标电量低：15%", NotificationIcon.Warning);
```

- `TaskbarIcon` 必须被字段/服务长期持有，避免被 GC。
- 所有 `TaskbarIcon` 操作在 UI 线程；服务事件通过 `Dispatcher` 编组。
- 退出时调用 `Dispose()`；`IsDisposed` 可用于防止重复释放。
- 不要设置 `TaskbarIcon.Visibility=Collapsed` 来“隐藏到托盘”，主窗口隐藏与托盘图标是两件事。
- `ForceCreate()` 默认会开启 Windows Efficiency Mode；仅在图标的创建时机不可控（如放在资源字典）时使用，不需要时可传 `false`。

---

## 4. 构建与运行

```powershell
# 还原
dotnet restore .\BlueMeter.csproj

# Debug 构建
dotnet build .\BlueMeter.csproj -c Debug

# 运行
dotnet run --project .\BlueMeter.csproj

# Release 构建
dotnet build .\BlueMeter.csproj -c Release
```

Visual Studio：

1. 打开 `BlueMeter.sln`。
2. 确认启动项目为 `BlueMeter`。
3. 目标框架显示 `net8.0-windows10.0.19041.0`。
4. F5 启动。

### 发布

```powershell
# 框架依赖，win-x64
dotnet publish .\BlueMeter.csproj -c Release -r win-x64 --self-contained false -o .\artifacts\win-x64

# 自包含，win-x64
dotnet publish .\BlueMeter.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\win-x64-sc

# ARM64（如需要）
dotnet publish .\BlueMeter.csproj -c Release -r win-arm64 --self-contained false -o .\artifacts\win-arm64
```

约束：

- **不要启用 trimming**（`PublishTrimmed=false`）。WPF + WinRT + COM 互操作在裁剪后容易运行时失败。
- 单文件发布（`PublishSingleFile`）需单独验证 WinRT 投影与图标资源；一期默认不用。
- 自包含包体积较大；对外分发优先框架依赖 + 安装说明。
- 发布前更新 `<Version>` 与 CHANGELOG。

---

## 5. 运行前检查项

按顺序检查；任何一项不满足都可能导致“没有设备”或“读不到电量”。

### 5.1 系统与运行时

- [ ] `winver` ≥ Windows 10 1809（17763）。
- [ ] 系统蓝牙已开启，飞行模式未强制关闭蓝牙。
- [ ] `dotnet --list-runtimes` 包含 `Microsoft.WindowsDesktop.App 8.x`（框架依赖运行时）。
- [ ] 应用以普通用户权限运行（不需要管理员）。
- [ ] 没有另一个 BlueMeter 实例正在运行（单实例互斥）。

### 5.2 设备与配对

- [ ] 目标设备已在 **设置 → 蓝牙和设备 → 设备** 中完成配对，而不是仅出现在“添加设备”列表。
- [ ] 设备电量充足、已开机、未处于深度休眠；首次测试建议唤醒设备并靠近电脑。
- [ ] 设备未被手机/平板同时占用（部分设备同一时间只接受一个主机）。
- [ ] 厂商配套软件（罗技 Options+、雷蛇 Synapse 等）未独占设备；如有冲突，退出厂商软件再试。
- [ ] BLE 设备确认暴露电池服务：
  - 使用 Bluetooth LE Explorer / nRF Connect 查看是否存在 `0x180F` 与 `0x2A19`；
  - 若不存在，BlueMeter 只能显示“不支持电量读取”。
- [ ] 经典蓝牙设备确认系统属性中是否有 `System.Devices.BatteryLife`（可在调试日志中查看枚举属性）。

### 5.3 通知与托盘

- [ ] Windows 通知已开启：设置 → 系统 → 通知。
- [ ] 专注助手/勿扰模式未拦截通知（或已知会被拦截）。
- [ ] 任务栏通知区域未隐藏 BlueMeter 图标（可在“任务栏设置 → 其他系统托盘图标”中打开）。
- [ ] 资源管理器未处于异常状态；如托盘图标不显示，重启 Explorer 或调用 `ForceCreate()`。

### 5.4 构建与资源

- [ ] `Assets/BlueMeter.ico` 存在，构建操作为 `Resource`，且 `<ApplicationIcon>` 路径正确。
- [ ] 托盘 `IconSource` 使用 `pack://application:,,,/Assets/BlueMeter.ico`，资源能被 `BitmapImage` 加载。
- [ ] `App.xaml` 已合并 `ThemesDictionary` 与 `ControlsDictionary`。
- [ ] `ShutdownMode=OnExplicitShutdown`，且没有残留 `StartupUri`。
- [ ] H.NotifyIcon.Wpf 版本为 2.3.2（不是 2.4.x）。
- [ ] 首次还原成功，`obj/project.assets.json` 中存在 `WPF-UI/4.3.0` 与 `H.NotifyIcon.Wpf/2.3.2`。

### 5.5 快速自检命令

```powershell
# SDK / 运行时
dotnet --list-sdks
dotnet --list-runtimes

# 蓝牙设备（PnP 视角）
Get-PnpDevice -Class Bluetooth | Format-Table Status, FriendlyName, InstanceId -AutoSize

# 已配对蓝牙设备（注册表视角，辅助排查）
Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices' -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty PSChildName
```

> 注册表只能证明“曾经配对”，不能证明设备当前可达；最终以应用枚举结果为准。

---

## 6. 手测矩阵

标记：P0 必须通过；P1 尽量覆盖；P2 视条件。

| ID | 优先级 | 场景 | 步骤 | 预期 |
| --- | --- | --- | --- | --- |
| T01 | P0 | 首次启动 | 删除设置文件后启动 | 窗口正常显示；托盘图标出现；开始扫描 |
| T02 | P0 | 关闭隐藏 | 点击窗口关闭 | 窗口消失；进程仍在；托盘图标仍在 |
| T03 | P0 | 托盘还原 | 左键托盘图标 | 窗口显示、还原为 Normal、获得焦点 |
| T04 | P0 | 托盘退出 | 右键 → 退出 | 进程结束；图标消失；无残留 GATT |
| T05 | P0 | 单实例 | 连续启动两次 exe | 只有一个进程/一个托盘图标；第二实例退出 |
| T06 | P0 | BLE 鼠标 | 配对并唤醒 BLE 鼠标 | 显示名称、BLE、电量、更新时间 |
| T07 | P0 | BLE 键盘 | 同上 | 电量合理；通知或轮询至少一种生效 |
| T08 | P1 | 经典鼠标 | 配对经典蓝牙鼠标 | 有系统属性则显示；无属性显示“不支持” |
| T09 | P1 | 不支持设备 | 配对不暴露电池服务的耳机 | 显示 `--` + “不支持电量读取”，不崩溃 |
| T10 | P0 | 设备关机 | 读取成功后关闭设备 | 保留旧值；“暂时不可达”；不弹错误框 |
| T11 | P0 | 设备恢复 | 重新打开设备 | 下一轮或数轮内恢复 Ok |
| T12 | P0 | 低电量提醒 | 将阈值临时调到高于当前电量，等待一轮 | 只弹一次气泡；列表高亮 |
| T13 | P1 | 提醒重新武装 | 充电超过阈值 +5 后再放电 | 可再次提醒 |
| T14 | P1 | 关闭通知 | 设置中关闭低电量通知 | 不弹气泡；列表仍高亮；无异常 |
| T15 | P0 | 蓝牙开关 | 关闭系统蓝牙 30 秒再打开 | 显示蓝牙关闭提示；恢复后自动扫描 |
| T16 | P0 | 睡眠唤醒 | 睡眠 2 分钟后唤醒 | 60 秒内恢复更新；无重复图标 |
| T17 | P1 | Explorer 重启 | 任务管理器重启资源管理器 | 托盘图标自动恢复；菜单可用 |
| T18 | P0 | 主题切换 | 系统切换明暗主题/强调色 | 窗口即时跟随；文字可读 |
| T19 | P1 | 高对比度 | 开启高对比度主题 | 低电量/不可达仍可区分；焦点可见 |
| T20 | P1 | 高 DPI | 200% 缩放、多显示器 | 布局不截断；图标清晰 |
| T21 | P1 | 多设备 | 连接 10–20 台/模拟多设备 | 串行读取；UI 不卡；单轮不重入 |
| T22 | P2 | 24 小时挂机 | 常驻一天，期间休眠/唤醒 | 无崩溃；内存/句柄无明显增长 |
| T23 | P1 | 运行中取消配对 | 在系统设置中移除设备 | 列表移除或标记不可达；不崩溃 |
| T24 | P1 | 注销/关机 | 注销或关机 | 正常退出；不阻止系统关机 |
| T25 | P1 | 通知被拦截 | 开启专注助手后触发低电量 | 不崩溃；日志记录；不重复弹 |
| T26 | P0 | 启动即托盘 | 开启“启动时最小化”，重启应用 | 只出现托盘图标；左键可打开窗口 |
| T27 | P1 | 设置持久化 | 修改间隔/阈值后重启 | 设置保留；非法 JSON 回退默认值 |
| T28 | P1 | ToolTip 长度 | 设备名很长、多设备低电量 | ≤120 字符；摘要正确 |
| T29 | P2 | 无适配器 | 虚拟机/禁用蓝牙适配器 | 显示“未检测到蓝牙适配器”；不崩溃 |
| T30 | P1 | 多电池服务 | 双耳耳机等多服务设备 | 取第一个成功值；日志记录实例数 |

### 低电量测试技巧

不想等待真实放电时：

1. 在设置中把阈值临时调到当前电量之上（例如 95%）。
2. 等待一轮扫描，验证气泡与高亮。
3. 调回 20%，再观察闩锁是否符合预期。
4. 若需要验证“从高到低”，可先在调试构建中注入模拟电量（仅开发环境，不进入 Release）。

---

## 7. 故障排查

| 现象 | 可能原因 | 排查/处理 |
| --- | --- | --- |
| 列表为空 | 蓝牙关闭；设备未配对；选择器用错 | 打开系统蓝牙；确认“设备”列表中有目标；检查是否使用 `GetDeviceSelectorFromPairingState(true)` |
| BLE 设备显示“不支持” | 无 `0x180F`；设备固件不暴露；服务是自定义 UUID | 用 BLE 工具确认服务；一期不解析厂商私有服务 |
| BLE 读取一直失败 | 设备休眠/被手机占用/距离远；GATT 缓存；厂商软件独占 | 唤醒设备、断开手机、重启蓝牙、退出厂商软件；看日志 HRESULT |
| 经典设备无电量 | 系统属性不存在 | 这是预期限制；二期 RFCOMM；不要显示 0% |
| 电量明显偏旧 | 系统属性缓存；设备未连接 | 看 `LastSuccessAt`；UI 标注时间；接受经典设备限制 |
| 托盘图标不出现 | 图标资源未打包；`TaskbarIcon` 被 GC；Explorer 异常 | 确认 Build Action=Resource；字段持有实例；`ForceCreate()`；重启 Explorer |
| 关闭窗口后进程退出 | `ShutdownMode` 不是 `OnExplicitShutdown`；仍有 `StartupUri` | 检查 App.xaml；移除 StartupUri |
| 点“退出”退不掉 | `OnClosing` 未放行；后台线程阻塞 | 设置 `_allowClose`；停止服务带 3 秒超时；不要 `.Wait()` 无限等 |
| Mica 不生效 | Win10；系统关闭透明效果；远程桌面 | Win11 22H2+ 验证；接受纯色退化；不要强行 P/Invoke |
| 主题不跟随 | 未调用 `SystemThemeWatcher.Watch`；资源字典未合并 | 构造窗口后调用；确认 `ThemesDictionary`/`ControlsDictionary` |
| 低电量不弹 | 通知关闭；专注助手；闩锁已置位；状态不是 Ok | 检查系统通知；看日志；确认电量回升到阈值+5 后重置 |
| 气泡弹多次 | 每轮都触发；闩锁未持久 | 检查 `LowBatteryAlerted` 与迟滞逻辑；同轮合并 |
| 构建报 H.NotifyIcon 资产不兼容 | 用了 2.4.1 | 固定 `2.3.2` |
| XAML 设计器报错 | WPF-UI 命名空间/版本不匹配 | 重新还原；清理 `obj`/`bin`；用 VS 2022 17.8+ |
| `dotnet restore` 失败 | 无法访问 NuGet；Windows SDK 引用包缺失 | 配置镜像；检查网络/代理；删除 `obj` 重试 |
| 提示 API 在 17763 不可用 | 使用了更高版本 WinRT API | 改为 17763 可用 API，或加运行时检查；不要提高 SupportedOSPlatformVersion |

### 日志定位

- 默认路径：`%LOCALAPPDATA%\BlueMeter\logs\bluemeter-yyyyMMdd.log`
- 关键字：`[BleProbe]`、`[ClassicProbe]`、`[Cycle]`、`[Tray]`、`[Alert]`
- 失败日志应包含：设备名、类型、阶段、`GattCommunicationStatus`/HRESULT、耗时。
- 分享日志前先检查设备名与地址是否敏感。

---

## 8. 编码约定

### 通用

- 文件范围命名空间；`nullable enable`；不使用 `dynamic`。
- 公共异步方法以 `Async` 结尾，必须接受 `CancellationToken`。
- 服务层 `ConfigureAwait(false)`；UI 层不加。
- 禁止 `.Result`、`.Wait()`、`Thread.Sleep`；禁止无 `try/catch` 的 `async void`（事件处理器除外）。
- 捕获异常必须记录上下文；禁止空 `catch {}`。
- 所有 WinRT 调用带超时；单设备失败不影响整轮。
- 蓝牙地址日志脱敏；不记录完整缓冲区。
- 颜色只用资源键；不在代码里写 `Brushes.Red` 之类的语义色。
- 文案集中到 `Strings`（或资源文件），避免散落硬编码。
- 设备状态用枚举，不用布尔组合表达多种状态。

### 命名

| 类型 | 规则 | 示例 |
| --- | --- | --- |
| 类/记录/枚举 | PascalCase | `BluetoothBatteryService` |
| 接口 | `I` + PascalCase | `IBatteryProbe` |
| 方法 | PascalCase + Async | `ReadBatteryAsync` |
| 私有字段 | `_camelCase` | `_trayIcon` |
| 常量 | PascalCase | `DefaultRefreshSeconds` |
| 局部变量 | camelCase | `deviceInfo` |
| XAML 控件 | `x:Name` PascalCase | `DeviceList` |

### WinRT 注意

- 明确 `using` 别名，避免 `BluetoothDevice` 与自定义类型冲突：
  ```csharp
  using WindowsBluetoothDevice = Windows.Devices.Bluetooth.BluetoothDevice;
  ```
- `BluetoothLEDevice.FromIdAsync` 可能返回 `null`，必须判空。
- `GattDeviceService`、`BluetoothLEDevice` 均实现 `IDisposable`；短生命周期读取用 `using`。
- `GattCommunicationStatus` 要判断，不要只看是否抛异常。
- `AsTask(ct)` 是超时的主要手段；WinRT 操作不保证立即响应取消，但可避免无限等待。
- 所有对 `TaskbarIcon` 的调用回到 UI 线程。
- 单实例 Mutex 名称使用 `Local\` 前缀，避免跨会话冲突。

### 目录与分层

- `Services` 不引用 `System.Windows.Controls`；需要调度时通过事件 + `Dispatcher` 在 UI 侧处理。
- `ViewModels` 不引用 `Windows.Devices.*`。
- `Controls` 只放可复用视觉组件（如 `BatteryBar`）。
- 新增探针实现 `IBatteryProbe`，不要在 `BluetoothBatteryService` 里堆 `if/else`。

### 提交前

```powershell
dotnet build .\BlueMeter.csproj -c Release
dotnet format .\BlueMeter.csproj --verify-no-changes   # 如已配置 dotnet format
```

- 确认没有把 `bin/`、`obj/`、`.vs/`、日志、设置文件提交进仓库。
- 如仓库后续初始化 Git，建议补充 `.gitignore`（VS 的 `VisualStudio.gitignore` 模板即可）。

---

## 9. 完成定义（DoD）

一个功能任务完成需满足：

- [ ] `dotnet build -c Release` 通过，无新增警告或已记录豁免。
- [ ] 对应手测用例通过（至少 P0）。
- [ ] 失败路径有日志、有 UI 状态、有超时，不出现未处理异常。
- [ ] 不引入 WinForms 托盘、第二套 UI 框架或额外蓝牙封装。
- [ ] 文档与实现一致（README / ARCHITECTURE / UI-SPEC / ROADMAP）。
- [ ] CHANGELOG 记录用户可见变更。

---

## 10. 打包与分发备注

- 一期以框架依赖的 zip/文件夹分发为主，附 .NET 8 Desktop Runtime 链接。
- 若改为 MSIX：
  - 添加 `bluetooth` 设备能力（`DeviceCapability`）；
  - 验证打包身份下的设备枚举与 GATT 访问；
  - 通知、开机启动、单实例行为需重新测试；
  - 自动更新另做规划。
- 代码签名可选；未签名 exe 在部分环境会触发 SmartScreen 提示。
- 不把日志、设置、用户设备名写入安装包或示例配置。

---

## 11. 文档维护

- 每个里程碑结束：更新 [ROADMAP.md](../ROADMAP.md) 状态与勾选项。
- 用户可见变更：更新 [CHANGELOG.md](../CHANGELOG.md)。
- 接口/状态/线程模型变化：更新 [docs/ARCHITECTURE.md](ARCHITECTURE.md)。
- 视觉/文案变化：更新 [docs/UI-SPEC.md](UI-SPEC.md)。
- 版本/依赖/构建方式变化：更新本文件与 README。
