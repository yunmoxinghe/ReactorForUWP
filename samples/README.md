# 示例

两个独立的 UWP 应用，**都只装 NuGet 包，不引用仓库里的框架源码**：

```xml
<PackageReference Include="Reactor.Uwp" Version="0.1.0-alpha.4" />
```

这样写是有意的——示例能编译通过，就说明**包里导出的公共 API 真的够用**。
用 `ProjectReference` 指向 `Reactor.uwp` 会掩盖导出问题（internal 的也能用到，
`UwpApp` 就是靠 `InternalsVisibleTo` 用了 `EchoStats`）。

示例同样守仓库那两条硬规则（详见根 `README.md` 的「兼容性契约」）：写法与官方
Reactor 互兼容，实现一律套 WinUI 2 真控件。所以 `Reactor.Template` 里出现的每个
控件都是官方那个（真 `BreadcrumbBar` + `ItemTemplate`、真 `Frame.GoBack()`、
Toolkit 的 `SettingsCard` / `SettingsExpander`），与 XAML 模板的差别只允许来自
"用 C# 表达"，不允许来自"自己画一个替代品"。

| 项目 | 用途 |
|---|---|
| `Reactor.Template/` | **起点模板**：一比一复刻 `UWP-Blank-Template`（标题栏 + NavigationView + 主页 / 设置页 + 设置持久化 + 外链确认框），新项目从这里复制 |
| `Reactor.Gallery/` | **示例画廊**：8 个主题页，查某种写法时来这里找 |

两个项目都开着 `PublishAot`（UWP + net10 的 AOT 用平台自带编译链，不额外依赖什么），
平台列 **x64 + arm64**（`Platforms` / `RuntimeIdentifiers` / 两个 `win-*.pubxml` 都成对）。
arm64 能用是因为包的 `runtimes\win-arm64\native\Reactor.Uwp.Native.dll` 从 **alpha.4** 起
就有了；**用 alpha.3 或更早的包在 arm64 下会缺这个 dll**。

## 本机吃到的是哪个包

**示例必须吃 nuget.org 上的包**，否则"能编译"证明不了任何事。但全局包缓存
（`%USERPROFILE%\.nuget\packages\`）里同名版本一旦被本地包占住，restore 就直接命中它，
再也不会去线下载——本机于是悄悄用上了本地产物，而别人 clone 下来还原不了。

看一眼就知道现在吃的是哪份：

```powershell
Get-Content "$env:USERPROFILE\.nuget\packages\reactor.uwp\<版本>\.nupkg.metadata"
```

`source` 是 `https://api.nuget.org/v3/index.json` 才对；如果是某个本地目录，说明被污染了。
切回在线包：

```powershell
Remove-Item "$env:USERPROFILE\.nuget\packages\reactor.uwp\<版本>" -Recurse -Force
dotnet restore samples/Reactor.Template/Reactor.Template.csproj
```

### 改了框架源码之后：必须走一遍的本地循环

alpha.4 还没发到 nuget.org，本机验证只能吃本地 pack 的包。于是有一条闭环，
**漏掉中间任何一步，示例都会静默跑旧代码**：NuGet 只认"这个版本在不在缓存里"，
不认内容是不是换过——同名版本重新 pack，restore 照样命中旧的那份。

"云母不跟随应用主题"就是这么来的：示例还原到的是修复前 pack 的 alpha.4，
那份包里连 `ApplyTheme` 方法都没有，而 `UwpApp` 走 `ProjectReference` 引源码、
吃的是新代码，于是"uwpapp 什么都对、示例全是 bug"。

```bash
# 1. 重新打包。别带 -p:Platform=x64：会把托管程序集编成 x64 专用，
#    arm64 消费方报 CS8012（详见下节）。
dotnet pack Reactor.uwp/Reactor.uwp.csproj -c Release -o D:/fluentapps/local-nuget

# 2. 删掉同名版本的全局缓存，否则第 1 步白做
Remove-Item "$env:USERPROFILE\.nuget\packages\reactor.uwp\0.1.0-alpha.4" -Recurse -Force

# 3. 还原 + 编译
dotnet restore samples/Reactor.Template/Reactor.Template.csproj
dotnet build   samples/Reactor.Template/Reactor.Template.csproj -c Debug -p:Platform=x64
```

别靠"能编译"判断吃到的是不是新包——包 API 没变时旧包照样编译得过。直接查元数据里
有没有你要的那个方法：

```powershell
Select-String -Path "$env:USERPROFILE\.nuget\packages\reactor.uwp\0.1.0-alpha.4\lib\net10.0-windows10.0.26100\Reactor.uwp.dll" `
              -Pattern "ApplyTheme","RefreshBackdropForTheme" -SimpleMatch
```

**验证还没发布的版本时，别让它进全局缓存**——加 `--packages` 指到独立目录：

```bash
dotnet restore samples/Reactor.Template/Reactor.Template.csproj --packages .workbuddy/tmp-pkgs
```

（本地 `dotnet pack` 出来的包想拿给示例试，就临时把 `bin\Release` 当源加上——
**pack 时不要带 `-p:Platform=x64`**，原因见下面"架构支持"一节；
用完记得把版本目录从全局缓存里删掉。Git Bash 里给 `--source` 传 `https://...`
会被 MSYS 当成路径改写成 `https:\...`（报 `NU1301: 本地源不存在`），
要么只传本地目录，要么前面加 `MSYS_NO_PATHCONV=1`。）

## 怎么跑

UWP 不能 `dotnet run`，要部署：

- **VS**：打开 `ReacrorForUWP.slnx`，把要跑的那个设为启动项目，选 `x64`（或 `arm64`），F5
- **VS 不部署时的兜底**：`powershell -ExecutionPolicy Bypass -File samples/run.ps1 -Project Template`
  （`-Project Gallery` 跑另一个；脚本补 Assets → 松散注册 → 按包身份激活）
- **命令行只能验编译，不能验运行**：
  ```bash
  dotnet build samples/Reactor.Template/Reactor.Template.csproj -c Debug -p:Platform=x64
  dotnet build samples/Reactor.Template/Reactor.Template.csproj -c Debug -p:Platform=arm64
  ```

> **`dotnet build` 会毁掉 VS 生成的包布局。** MSIX 那一步（生成 `AppxManifest.xml` /
> `resources.pri` / `Microsoft.UI.Xaml.winmd`）只在 VS 的 MSBuild 里跑，CLI 不参与；
> 而 CLI 的增量清理反过来会把 VS 生成的那些文件删掉。
> 所以别在 VS 跑过之后再用 CLI 构建同一个示例——要验编译就验完再回 VS 重新生成。

开 AOT 后构建是 RID 感知的，输出落在 `bin\x64\Debug\<tfm>\win-x64\`，
部署用的原生桥在 `win-x64\AppX\Reactor.Uwp.Native.dll`。

### 没跑起来先看这里：`0xc0000409` 不是框架的锅

**直接双击 / 调试 `win-x64\*.exe` 必崩**：进程没有 AppX 包身份，`Windows.UI.Xaml.dll`
会直接 fail-fast，退出码 `0xc0000409`（fast-fail 子码 7 = `FAST_FAIL_FATAL_APP_EXIT`），
WER 里 faulting module 是 `Windows.UI.Xaml.dll`、偏移 `0x348aae`，
托管堆栈和 `UnhandledException` 都拦不到——看着像框架崩了，其实只是没打包身份。

判定办法：WER 的 "Faulting package full name" 是空的 = 没身份；
或者拿能跑的 `UwpApp` 从 `win-x64\UwpApp.exe`（而不是 `win-x64\AppX\` 里那个）启动，
同样 `0xc0000409`。

所以 F5 崩了先确认**部署真的发生了**——查 AppX 部署日志最直接：

```powershell
Get-WinEvent -LogName 'Microsoft-Windows-AppXDeploymentServer/Operational' -MaxEvents 40 |
  Select-Object TimeCreated, Id, Message
```

只要没有针对本包 Identity 的 `Register` / `Add` 事件，就说明 VS 压根没部署，
只是把 bin 里的 exe 拉起来了（`.slnx` 里 Deploy 是勾上的，所以通常是别的原因）。

**最常见的挡路石：缺 `Properties/launchSettings.json`。** 这是 VS 决定"F5 怎么启动"
的启动配置文件，里面必须写 `"commandName": "MsixPackage"`，VS 才会走
"构建 → 部署 MSIX 包 → 按包身份激活"。没有这个文件（或用 `commandName: Project`），
F5 退化成"直接执行 bin 里的裸 exe"，于是没有包身份、XAML 立刻 fail-fast:

```json
{
  "profiles": {
    "Reactor.Template": { "commandName": "MsixPackage" }
  }
}
```

仓库里 `UwpApp` 能 F5 就是因为它的 `Properties/launchSettings.json` 写了这一行；
两个示例原先没有这个文件，这就是它们一直 `0xc0000409` 而 UwpApp 没事的全部原因。
VS 里也可以自己配：项目属性 → 调试 → 启动配置文件（或工具栏 Run 下拉框里选带
MSIX 的那个，别选 `commandName: Project` 那个）。

其它该查的：

- **工具栏顶部的 Solution Platform 要选 x64**。官方文档特意提醒：要用顶部那个
  "Active solution platform" 下拉框，而不是 Configuration Manager 里 Deploy 复选框
  旁边那个。平台不对时 VS 会报 "The project needs to be deployed. Please enable
  Deploy in the Configuration Manager"，但那通常不是 Deploy 没勾。

- "输出"窗口（显示：生成）里有没有 `DEPxxxx` 报错
- 包注册上了没：
  ```powershell
  Get-AppxPackage -Name "21CE1A0C-1D93-4A28-AE85-B632189C9236"   # Template
  Get-AppxPackage -Name "E362DB2E-EDAD-4411-B1F6-6355F8CFB661"   # Gallery
  ```

注意松散注册后再让 VS 部署会因同 Identity 冲突失败，先卸掉：
`Get-AppxPackage -Name <Identity> | Remove-AppxPackage`。（`run.ps1` 会自己先卸。）

## 模板里有什么

`Reactor.Template` 是"新项目的起点"，一比一复刻 `UWP-Blank-Template` 的
`MainPage` + `Pages/HomePage` + `Pages/SettingsPage`，只是把 XAML 换成 Reactor 的 C#：

| 文件 | 作用 |
|---|---|
| `App.cs` | 入口：`ReactorApplication<MainPage>`。WinUI 2 资源、窗口挂载、标题栏延伸都是宿主做的；另外在 `OnLaunched` 里抢先设一次控件声音 |
| `MainPage.cs` | 外壳 + 主页 + 设置页：32px 自绘标题栏（`.TitleBar()` / `.OwnsTitleBar()`）、NavigationView + 页面栈（可返回）、`.Backdrop()` 云母或亚克力、`.Theme()` 主题、设置页三组（外观 / 声音 / 关于）+ 外链确认框 |
| `Services/AppSettings.cs` | 设置持久化：JSON + source-gen（`JsonSerializerContext`），AOT 安全 |
| `Services/ElementSound.cs` | 控件声音：把设置里的开关落到 XAML 元素音效上，注释里记了类型与时机两个坑 |

与 XAML 版的对应：

- **页面切换**：页面栈用 `UseState` 存，`onBackRequested` 出栈；同时把深度告诉
  Frame——`Frame(content, transition, stackDepth: stack.Length)`。深度比上一次小
  就是返回，走官方 `Frame.GoBack()`，否则走 `Frame.Navigate(...)`——与 XAML 模板
  `ContentFrame.GoBack()` / `Navigate()` 完全同构，两条路径由官方自己驱动。
  返回那条路径有独立的过渡方向和 `ElementSoundKind.GoBack` 音效（系统里确有其资源：
  `Windows.UI.Xaml.dll` 内的 `GoBack_48000Hz`，音色与 `Invoke` 不同），用 Navigate
  模拟是拿不到的。`Frame.CacheSize` 锁 0（XAML 默认值），GoBack 走的是重建页面路径，
  不靠缓存实例。不传 `stackDepth` 则一律 Navigate 且每次清 BackStack（无人 GoBack，
  留着是泄漏）。
- **标题栏**：`ExtendViewIntoTitleBar` + `Window.Current.SetTitleBar` →
  元素上的 `.TitleBar()`；`.OwnsTitleBar()` 让页面自己接管顶部 32px，
  否则宿主压一次、页面再压一次，内容会掉下来。
- **节标题样式**：`BasedOn BodyStrongTextBlockStyle + Margin` → `StyleSheet.Define`
  （纯代码构造 Style，与 XAML 的 `StaticResource` 等价）。

三个"只改一处"的点：

- **背景材质**：`MainPage` 根元素上的 `.Backdrop(BackdropKind.Mica)`。值来自设置，
  可选云母 / 桌面亚克力。云母需要 Win11（22000+），低版本回退纯色；
  节电模式或系统关闭透明效果时亚克力会失效（Mica 不受影响）。
- **主题**：同一处的 `.Theme(ElementTheme)`。XAML 的主题沿树继承，
  写在根元素上就够了——不用命令式改 `RequestedTheme`。
- **加页面**：`menuItems` 数组加一项，再在 `Render` 里给 `current == "<key>"` 挂上组件。

设置是静态单例（`AppSettings.Current`），改完调 `AppSettings.Update(mutate)` 落盘；
改 UI 的那一份 state 也在 `MainPage` 里，所以不需要额外的"通知外壳重渲染"回调——
`onChanged` 那个重载是给"状态不在同一个组件里"的场景留的。

**"控件声音"的两个坑**（`Services/ElementSound.cs` 里也记了一份）：

- 类型是 `Windows.UI.Xaml.ElementSoundPlayer`，**不是** `Windows.UI.Xaml.Controls`
  下的。写成后者报 CS0234，看着像"只装 NuGet 包的应用工程用不了"，其实一直能用。
- 必须在**创建任何 UI 之前**设 `State`。XAML 在应用控件模板时就按当时的值把
  `ElementSoundMode` 定死了，放到页面组件的 `UseEffect` 里设是无效时机。
  所以启动时那一次放在 `App.OnLaunched`（早于 `base.OnLaunched`）——
  参考模板也是在 `App.OnLaunched` 里、`rootFrame.Navigate` 之前设的；
  开关切换那一次在 `MainPage.ChangeSound` 里补。

## 与 UWP-Blank-Template 的对齐

工程配置对齐 [Furry-Xiyi/UWP-Blank-Template](https://github.com/Furry-Xiyi/UWP-Blank-Template)
（`net10` + `UseUwp` + AOT 的 UWP 模板）。两个示例（`Reactor.Template` / `Reactor.Gallery`）
的对齐项完全一致：

| 项 | 值 |
|---|---|
| `TargetFramework` / `TargetPlatformMinVersion` | `net10.0-windows10.0.26100.0` / `10.0.19041.0`（不是 17763） |
| AOT | `PublishAot=True` + `DisableRuntimeMarshalling=true` |
| 多架构 | `Platforms=x64;arm64`、`RuntimeIdentifiers=win-x64;win-arm64`、`AppxBundlePlatforms=x64\|arm64`，`PublishProfiles` 下两个 `win-*.pubxml` |
| 打包开关 | `AppxBundle=Never`、`AppxSymbolPackageEnabled=False`、`AppxAutoIncrementPackageRevision=False`、`GenerateTestArtifacts=False`、`GenerateAppInstallerFile=False`、`HoursBetweenUpdateChecks=0` |
| 签名 | `AppxPackageSigningEnabled=True` + `GenerateTemporaryStoreCertificate=True` + `AppxPackageSigningTimestampDigestAlgorithm=SHA256` |
| Assets | VS 模板全套 52 个：scale-100/125/150/200/400 + `altform-lightunplated` + `targetsize`，`SmallTile` / `LargeTile` 也在 |
| 本地化 | `Strings\en-US\Resources.resw` + `Strings\zh-CN\Resources.resw`，manifest 走 `ms-resource:AppDisplayName` / `ms-resource:AppDescription`，`<Resources>` 显式列两种语言 |
| 磁贴 | `uap:DefaultTile` 三尺寸：`Wide310x150Logo` / `Square71x71Logo` / `Square310x310Logo` |
| 其它 | `AllowUnsafeBlocks=True`、`DefaultLanguage=zh-CN`、`<Capability Name="internetClient"/>`、`<uap:SplashScreen uap5:Optional="true"/>`、`<mp:PhoneIdentity>`、显式 `<PackageReference Microsoft.UI.Xaml 2.8.7>` |

剩下三处**有意不同**，别照着改回去：

| 项 | 那边 | 这边 | 为什么 |
|---|---|---|---|
| 打包结构 | `*.wapproj` 分离打包项目 + `Microsoft.Windows.SDK.BuildTools` | 单项目 MSIX（`EnableMsixTooling`） | 单项目 MSIX 是微软现代 .NET UWP 模板的写法：一个项目就能 F5，不用在 slnx 里额外挂打包项目。`BuildTools` 是 wapproj 才要显式引（拿来找 makeappx/makepri），单项目 MSIX 由 SDK 自带 |
| UI 写法 | XAML（`App.xaml` + `MainPage.xaml` + `InitializeComponent`） | 纯 C#（`App.cs` + `Component`） | 模板要示范的是 Reactor 写法，加 XAML 就南辕北辙了。**功能是对齐的**：云母 / 亚克力背景、主题切换、自绘标题栏、NavigationView + 设置页（三组：外观 / 声音 / 关于）、页面返回栈、外链确认框、设置持久化全都有，只是用 `.Backdrop()` / `.Theme()` / `.TitleBar()` 修饰符和组件表达，不是 XAML |
| `PackageCertificateKeyFile` | 写死作者本机的 .pfx 路径 | 不写 | 换台机器就失效；改用 `GenerateTemporaryStoreCertificate` 让 VS 按 manifest 的 Publisher 主题自动生成并信任测试证书 |

对齐过来的几项，都是踩过坑才加的：

- **`TargetPlatformMinVersion` = `10.0.19041.0`**：官方 UWP + 现代 .NET 模板的下限
  （原来写 17763），manifest 里的 `TargetDeviceFamily MinVersion` 要跟着改
- **`GenerateTemporaryStoreCertificate=True`**：让 VS 按 manifest 的 Publisher 主题
  自动生成并信任一张测试证书。缺了它，本机没匹配证书时部署会退化成跑裸 exe
- **`<uap:SplashScreen uap5:Optional="true"/>`**：Assets 里只有 `SplashScreen.scale-200.png`，
  没有不限定符的那一份，松散注册（无 `resources.pri`）时会报 `0x80070002`
- **打包开关**：`AppxBundle=Never`、不生成符号包 / 测试产物 / App Installer

### 架构支持（x64 + arm64）

原生桥 `Reactor.Uwp.Native.dll` 现在编了 **x64 和 arm64 两个架构**，产物分别进
包的 `runtimes\win-x64\native\` 和 `runtimes\win-arm64\native\`（只打实际存在的）：

```bash
Reactor.Uwp.Native\build.bat            # 默认 x64
Reactor.Uwp.Native\build.bat arm64      # 交叉编译，工具链 Hostx64\arm64
```

arm64 那份是交叉编译出来的，在 x64 机器上只能验到"PE 头是 `0xAA64` +
构建时正确落到 `win-arm64\AppX\`"，**没法真跑**——真机验证留到 ARM64 设备。

**打包 `Reactor.uwp` 时别带 `-p:Platform=x64`**。带了的话包里的托管程序集会被编成
x64 专属（PE machine `0x8664`），arm64 消费方引用它就报：

```
CSC : warning CS8012: 引用程序集"Reactor.uwp"面向的是另一个处理器
```

用默认平台（AnyCPU）打包，`lib\net10.0-windows10.0.26100\Reactor.uwp.dll` 才是
`0x14c`（纯 IL），x64 / arm64 消费方都能用。也就是：

```bash
dotnet pack Reactor.uwp/Reactor.uwp.csproj -c Release -o <本地源目录>   # 不带 -p:Platform
```

验一个包是不是双架构齐全，看 PE machine（x64 = `0x8664`，arm64 = `0xAA64`）：

```
runtimes/win-x64/native/Reactor.Uwp.Native.dll    0x8664
runtimes/win-arm64/native/Reactor.Uwp.Native.dll  0xaa64
```

编译前 `generated\` 里要有 cppwinrt 生成的投影头（构建产物，不入库）。没了就重新生成，
注意两点：**要用 SDK 自带的 cppwinrt**（`Windows Kits\10\bin\10.0.26100.0\x64`，
用别处下载的旧版会和 SDK 的 `winrt/base.h` 版本对不上，报 `Mismatched C++/WinRT headers`）；
**MUX 的 `IWebView2` 依赖 WebView2 的类型**，那个 winmd 也要一起喂进去：

```bash
cppwinrt.exe -in "C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.26100.0\Windows.winmd" ^
              -in winmd\Microsoft.UI.Xaml.winmd ^
              -in "%USERPROFILE%\.nuget\packages\microsoft.web.webview2\1.0.2849.39\lib\Microsoft.Web.WebView2.Core.winmd" ^
              -out generated
```

## Gallery 里有什么

| 页面 | 看点 |
|---|---|
| 快速开始 | `UseState` + 事件 + `When` / `If` 条件渲染，最小可运行形态 |
| 输入与选择 | TextBox / ComboBox / ToggleSwitch / CheckBox / Slider / RadioButtons，`Optional<T>` 受控写法 |
| 布局 | `Grid` 行列（Auto / 星号）、`Border`、对齐与间距 |
| 列表 | `ListView` / `GridView` / `ForEach`，项数少时用这个 |
| 虚拟化长列表 | 5000 项 `VirtualizingList`（走 `ItemsRepeater` + 原生元素工厂），看只 realize 几十个容器 |
| 设置页 | SettingsCard / SettingsExpander / Expander / ContentDialog |
| 组件 props | `Component<TProps>` 父子传值，record 当 props |
| 原生控件逃生舱 | `Native()`：包还没包住的控件（NumberBox）怎么挂进来 |

## 抄代码时注意

- **入口没有 App.xaml**：`App.cs` 手写 `Main`，manifest 的 `EntryPoint` 指向它
- **`VirtualizingList` 的 `itemKey`**：默认路径是 `ItemsRepeater`，它按下标复用容器、
  不做按 key 复用（官方 Reactor 同样如此），所以 `itemKey` 只在**自绘回退**路径上
  起作用——也就是 x86（没有该架构的原生桥 `Reactor.Uwp.Native.dll`）下才需要它。
  那种场景下数据源会插入 / 删除时，不给稳定身份就会因为下标位移而拿错内容
- **`Native()` 的创建委托要写成字段**：引用必须稳定，只有 `Token` 变化才重建控件
- **别急着关 AOT**：它能第一时间暴露反射 / 运行时代码生成这类写法；
  真需要反射就加 `rd.xml` 或 `[DynamicallyAccessedMembers]`
