# 示例

两个独立的 UWP 应用，**引用框架的方式刻意不同**，这是分工决定的：

| 项目 | 引用方式 | 为什么这样定 |
|---|---|---|
| `Reactor.Template` | `PackageReference`：`Reactor.Uwp` | 它是**新项目的起点**。它能编译，才说明包里导出的公共 API 真的够用。改成 `ProjectReference` 会把导出问题掩盖掉——internal 的照样能用到（`UwpApp` 就是靠 `InternalsVisibleTo` 用了 `EchoStats`），于是"别人装了包却用不了"这类问题在仓库里永远测不出来 |
| `Reactor.Gallery` | `ProjectReference`：`..\..\Reactor.uwp` | 它是**框架自身的验证载体**。里面那个「受控控件诊断」页要读 `ReactorLog` / 闸门计数这类**尚未发版**的观测 API，只能走工程引用。发 alpha 包之前用它跑一遍受控控件，是最快的一次性定性手段 |

**这个分工的代价要事先说清，不然排查方向会被带到别处去：**

- **Gallery 编译通过证明不了"包够用"**，只有 Template 能证明。两个项目不是同一类证据。
- 反过来说，同一份写法在 Gallery 里正常、在 Template 里出问题，**第一件事就是查两边吃到的是不是同一份实现**——模板锁的那个版本有可能在 nuget.org 上还没有（那时本机靠本地源补上，见下一节），于是 Gallery 吃新代码、Template 吃旧包，阴阳脸就出来了。
- 别照着 Gallery 的 csproj 去改 Template 的引用方式，也别反过来。

示例同样守仓库那两条硬规则（详见根 `README.md` 的「兼容性契约」）：写法与官方
Reactor 互兼容，实现一律套 WinUI 2 真控件。所以 `Reactor.Template` 里出现的每个
控件都是官方那个（真 `BreadcrumbBar` + `ItemTemplate`、真 `Frame.GoBack()`、
Toolkit 的 `SettingsCard` / `SettingsExpander`），与 XAML 模板的差别只允许来自
"用 C# 表达"，不允许来自"自己画一个替代品"。

| 项目 | 用途 |
|---|---|
| `Reactor.Template/` | **起点模板**：一比一复刻 `UWP-Blank-Template`（标题栏 + NavigationView + 主页 / 设置页 + 设置持久化 + 外链确认框），新项目从这里复制 |
| `Reactor.Gallery/` | **示例画廊**：分类 → 控件条目 → 样例的三级索引（首页 / 浏览 / 详情 / 搜索 / 设置五张页面），每个样例都带**可复制的真实源码**，查某种写法时来这里找 |

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

下面这套本地循环只在一种情况下需要：**框架改了、 Template 锁的那个版本在 nuget.org 上还没有**（那时尚未发布，本机只能先用本地 pack 的包顶着）。

平时版本在线上是真实存在的，改示例不用走这套——`dotnet build` 就行。

判据很简单：把 Template 的 `PackageReference` 版本号往 nuget.org 的
[版本列表](https://api.nuget.org/v3-flatcontainer/reactor.uwp/index.json) 上比对，
**列表里有 = 走线上还原，没有 = 只能走这套**。

漏掉中间任何一步，示例都会**静默跑旧代码**：NuGet 只认"这个版本在不在缓存里"，
不认内容是不是换过——同名版本重新 pack，restore 照样命中旧的那份。

"云母不跟随应用主题"就是这么来的：示例还原到的是修复前 pack 的那份 alpha.4，
包里连 `ApplyTheme` 方法都没有（alpha.4 当时从没发到 nuget.org，本机那份是本地
pack 的），而 `UwpApp` 走 `ProjectReference` 引源码、吃的是新代码，于是
"uwpapp 什么都对、示例全是 bug"。

```bash
# 1. 重新打包。别带 -p:Platform=x64：会把托管程序集编成 x64 专用，
#    arm64 消费方报 CS8012（详见下节）。
dotnet pack Reactor.uwp/Reactor.uwp.csproj -c Release -o D:/fluentapps/local-nuget

# 2. 删掉同名版本的全局缓存，否则第 1 步白做
Remove-Item "$env:USERPROFILE\.nuget\packages\reactor.uwp\<版本>" -Recurse -Force

# 3. 还原 + 编译
dotnet restore samples/Reactor.Template/Reactor.Template.csproj
dotnet build   samples/Reactor.Template/Reactor.Template.csproj -c Debug -p:Platform=x64
```

别靠"能编译"判断吃到的是不是新包——包 API 没变时旧包照样编译得过。直接查元数据里
有没有你要的那个方法：

```powershell
Select-String -Path "$env:USERPROFILE\.nuget\packages\reactor.uwp\<版本>\lib\net10.0-windows10.0.26100\Reactor.uwp.dll" `
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

  注册挑的是**完整**的那份布局：`win-<arch>/AppX`（VS 生成的，带 `resources.pri`
  与 `Microsoft.UI.Xaml.winmd`）存在就用它，没有才退回"在 `win-<arch>` 根目录
  现造一份清单"。**别用根目录那份**：它注册得上是 `Status=Ok`、激活也报"完成"，
  但进程压根起不来——既不崩也不留日志，看上去像应用坏了，其实只是布局残缺。
  应用起不来时先确认 `InstallLocation` 指向的是不是 `AppX` 那个目录：

  ```powershell
  (Get-AppxPackage -Name "E362DB2E-EDAD-4411-B1F6-6355F8CFB661").InstallLocation
  ```
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

**走工程引用的项目要自己声明原生桥。** Gallery 引用的是仓库里的框架工程
（不是 NuGet 包），而库工程没有 RID，它的 Content 落不到消费方带 RID 的
`win-x64\AppX\` 里——`Reactor.Uwp.Native.dll` 就丢了，虚拟化那条路会静默退化成
自绘回退（不报错，只是 realized 容器数不对）。所以 `Reactor.Gallery.csproj` 里
照 `UwpApp.csproj` 的写法显式声明了一次。`Reactor.Template` 不用写：它走
NuGet 包，包里的 `runtimes\win-$(Platform)\native\` + build targets 会送过去。
排查同类问题时最快的判据是**比对两个 AppX 布局的文件清单**（212 / 212 相等）。

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

结构对齐 WinUI 3 Gallery：**分类 → 控件条目 → 样例**，三级都由
`Gallery/SampleIndex.cs` 这一份数据长出来（左侧导航、首页卡片、浏览页网格、
搜索结果全读它），**加一个示例只要动那一个文件**。

| 页面 | 看点 |
|---|---|
| 首页 `Pages/HomePage` | hero + 三张入口卡 + 分类卡片墙 + 当前设置读数 |
| 浏览 `Pages/BrowsePage` | 按分类的 `GridView` 卡片网格 |
| 详情 `Pages/ItemPage` | 上预览、下源码，成对排布；`BreadcrumbBar` 显示所在分类；顶部一张「官方对应」卡 |
| 搜索 `Pages/SearchPage` | 顶栏 `AutoSuggestBox` 全程可搜；标题 / 描述 / 样例标题三档排序，结果右侧标注**命中在哪** |
| 设置 `Pages/AppSettingsPage` | 主题（RadioButtons）/ 音效（ToggleSwitch + 试听）/ 快捷键 / 高级 / 关于 |
| 外壳 `SampleShell` | `NavigationView`（自带搜索框 + 官方设置项）+ `Frame` 导航栈 |

**键盘**：`Ctrl+F` 把焦点送到顶部搜索框，`Alt+←` 返回上一层。
两条都是挂在外壳根元素上的官方 `KeyboardAccelerator`，不是页面自己监听按键。
焦点那一头由 `FocusToken(int)` 承载——`Focus()` 是方法不是属性，声明式里只能
用"令牌变了"表达"再聚焦一次"（边沿触发，所以不会每帧把焦点抢回来）。
设置项走的是 `NavigationView` 原生的那个（`IsSettingsVisible` 默认开着），
它不在 `MenuItems` 里，`OnItemInvoked` 以 **-1** 回调认出它。

分类：**按钮** / 文本与提示 / 输入与选择 / **日期与时间** / **命令与外壳**
（含 `SwipeControl`，外加**命令条补完**的 `AppBarToggleButton` / `CommandBarFlyout` /
`TextCommandBarFlyout`，外加**菜单补完**的 `ToggleMenuFlyoutItem` /
`RadioMenuFlyoutItem` / `MenuFlyoutSubItem` 与**内容型**的 `Flyout`）
/ 集合与虚拟化（`ListView` / `GridView` / 虚拟化长列表 /
`ListBox` / `FlipView` / `TreeView`，外加**视图切换补完**的 `SemanticZoom` /
`PipsPager`）/ **状态与信息**（`ProgressBar` / `ProgressRing` /
`RefreshContainer`，外加**提示气泡**的 `ToolTip`）/ 布局与容器（`Grid` / `Border` / `Expander` / `TabView` / `Pivot` /
`BreadcrumbBar` / `ScrollViewer` / `ContentDialog`，外加**布局补完**的
`Canvas` / `Viewbox` / `VariableSizedWrapGrid` / `RelativePanel` / `ParallaxView`）/
媒体、图像与图标（`Image` / `FontIcon` / `BitmapIcon` / `ImageIcon` /
`PersonPicture`，外加**形状**的 `Ellipse` / `Rectangle` / `Line`），
外加一份「写法指南」（整页级示例，住在 `Pages/`）。
写法指南那 9 项是原来那些主题页，一个没删，只是搬到了索引里，于是它们也能被搜到。

`Pivot` 与 `TabView` 是刻意排在相邻位置的一对：同为分页容器，内容策略却相反——
`TabView` 只在容器上留一份内容（切页签时 patch 同一棵树），`Pivot` 每页各挂一份
（只有这样才能保住"内容跟着手势横移"那个过渡）。两边取舍都写在各自 handler 的注释里。
`Pivot` 那份还要自己走一遍卸载：协调器的 `UnmountTree` 只替组件包装 /
`Panel` + `ChildrenOf` / `SingleChildOf` 三种形状递归，"N 个 `PivotItem`、
每个一份内容"三种都不是，不自己来则每页里的组件 cleanup 永远不跑。

「命令与外壳」那一族（`CommandBar` / `MenuBar` / `SplitView`）是同一条规矩的三个
面孔：**子部件就地物化，不进协调器**。命令项（`AppBarButton`）与菜单项
（`MenuFlyoutItem`）都是"挂在别人身上的部件"——前者进了 `PrimaryCommands`、
后者进了 `MenuFlyout.Items`，都不能再当可视树上的独立节点。所以它们与
`RichTextBlock` 同路：元素是描述，宿主 handler 就地造。
`SplitView` 反过来是**两个独立槽位**（`Pane` / `Content`），`Content` 借
`SingleChildAccessor` 走协调器那条通用路径，`Pane` 由 handler 自己管；
卸载同样要自己走一遍（理由同上：`SplitView` 继承 `Control`，`UnmountTree`
认不出它）。

同一族里唯一的**例外**是内容型的 `Flyout`：它装的不是"项"而是<b>一棵子树</b>，
所以走的是**另一条路**——进协调器，`Build` / `Patch` / 递归卸载三条一个不少。
理由是硬的：菜单能整体重建是因为它的项没有跨帧状态，而浮出层里可以有一个正在输入、
带着光标的 `TextBox`，每轮重建一次就把光标抹掉一次。代价是"卸载"多一个入口：
浮出层的内容不在可视树里，`UnmountTree` 顺着 `Content` / `Children` 递归不到它，
所以 `ContextFlyout` 由 `UnmountNode` 统一收、按钮的 `Flyout` 槽位由各 handler 收
（见 `Reactor.uwp/Internal/FlyoutContent.cs`）。

「菜单与文本命令」补完那一族（`MenuFlyoutSubItem` / `TextCommandBarFlyout` /
`ImageIcon`）是三条各自独立的缺口，摆在一起是因为它们分别演示"官方把同一件事
分在了两个类型上"：
`MenuFlyoutSubItem` 官方继承的是 `MenuFlyoutItemBase` **而不是** `MenuFlyoutItem`
（实测：两者互相赋值编译不过），所以它有 `Text` / `Icon` / `IsEnabled`，
**却没有** `KeyboardAcceleratorTextOverride`——本库因此也不给那个参数，
而不是给了之后偷偷不生效（给了不生效的旋钮比不给更糟：人会对着界面调半天）。
它收的是与菜单同一个类型，于是能一层层往下嵌；代价是卸载时回声登记要顺着
子菜单往下摘一遍（`MenuFlyouts.ForgetNested`）——可勾选项挂在子菜单里时同样是
"写完在等回执"的站点，只走顶层一层会留下一发悬空登记。
`TextCommandBarFlyout` 是 `CommandBarFlyout` 的**子类**：两组命令与
`AlwaysExpanded` 与它完全同形，多出来的只有一件事——**它认得文本控件**，
剪贴板那几条命令由控件自己按"当前有没有选中文字"填并改可用状态，
手填一份就丢了这份联动。于是它挂的槽位不是右键那个 `ContextFlyout`，
而是官方给文本控件单独留的 `SelectionFlyout`（本库对应 `.SelectionFlyout(...)`
修饰器；这个属性不在 `UIElement` 上，给别的控件写会留一条痕并被忽略——
静默变成"怎么选都不弹"比留痕难查得多）。
`ImageIcon`（WinUI 2）与 `BitmapIcon`（UWP 原生）则是"同一个用途、两个控件"：
后者按原图 1:1 画、`ShowAsMonochrome` 时单色化，**设尺寸是裁不是缩放**；
前者内部是一个 `Image`，**按宿主给的尺寸缩放、画原图颜色**。
要"同一张图放到不同大小的槽位里"用 `ImageIcon`，要"跟着主题变色"用 `BitmapIcon`。

「日期与时间」那一族（对应 Gallery 的 *Date & time* 一页）四个控件都是 UWP 原生：
`DatePicker` / `TimePicker` / `CalendarDatePicker` 的受控值（`Date` / `Time`）走
`EchoGuard`，区间属性（`MinYear` / `MaxYear` / `MinuteIncrement`）会把当前值夹
或吸附过去，那一发罩静默窗——与 `Slider` 的 `Minimum` / `Maximum` 同形。
`CalendarDatePicker.Date` 是**可空**的（官方给的就是可空：输入框可以没有值），
于是回调签名也是 `Action<DateTimeOffset?>`，拿到 `null` 表示"清空了"。
`CalendarView` 是这一族里唯一**只出不进**的：官方 `SelectedDates` 是控件持有的
活集合，没有回执通道能把"集合里有什么"与"state 里有什么"对齐到可判定，
所以框架从不往回写，只把**快照**（不是那个活集合）通过回调送出去；
它的配置属性也只在挂载时写（改它们会牵动选中集合并抛事件）。

「集合补完」那一族（`ListBox` / `FlipView` / `TreeView`）对应 Gallery 里三个
各自有页面的控件。`ListBox` / `FlipView` 与 `ListView` / `GridView` 是**同一条
选中契约**（`Selector.SelectedIndex` + 回声抑制 + 四道判据），但基类不同——
它们只是 `Selector`：没有 `Header`、没有 `ItemClick`、选中模式是
`SelectionMode` 而不是 `ListViewSelectionMode`。所以这两家走另一条 handler 基类
（`Internal/Handlers.Selectors.cs`），`ItemsViewHandler` 那条已验过的路一行没动。
`TreeView` 反过来是**只出不进**的第二个样板，理由比 `CalendarView` 更硬：
官方 `SelectedItem` / `SelectedNode` 都只有 getter、`TreeViewNode` 连 `IsSelected`
都没有——压根没有可写的入口。它的节点树在挂载期物化（改结构等于整棵树重来，
代价是展开态归零），节点内容只收字符串（`TreeViewNode.Content` 收 `object`，
但塞 `UIElement` 等于把一棵子树交给协调器以外的地方养，卸载没人管）。

「视图切换与滑动」那一族（`SemanticZoom` / `PipsPager` / `SwipeControl`）是三个
不同形状的槽位，摆在一起正好能把"槽位类型"这件事讲清楚：
`SemanticZoom` 的**两个槽位只收 `ListView` / `GridView`**——官方那两个属性的类型
是 `ISemanticZoomInformation`，缩放要靠"这一组里当前是哪一项"才能对上，容器必须
自己报得出这个信息；给了别的就留空并留痕，不抛（抛会把整页炸掉）。它的受控值
`IsZoomedInViewActive` 是本库第一个**异步**回执：写它触发的是带动画的切换，
`ViewChangeCompleted` 晚于这次调用才到，按 `EchoGuard` 那条"宁可多回调一次"的
既定价，多出来的是一次"值相同、不重渲染"的空转，不是抖动。
`PipsPager` 反过来**自己不装内容**，只是"第几页"的指示；它的属性名与事件名是
**不对称**的（属性 `SelectedPageIndex`、事件 `SelectedIndexChanged`），照官方抄，
不是笔误；改 `NumberOfPages` 会把当前页夹回范围里，那一发罩静默窗。
`SwipeControl` 是第四种形状：四组命令 + 一个单子内容，**模式长在「组」上**
（官方 `SwipeItems.Mode` 决定滑到头直接执行还是只露出来），所以元素收的是
`SwipeItemsData`（一组 + 一个模式）而不是散装的项——摊平就丢了这一层。
命令整组重建（与 `CommandBar` 同一个取舍），图标收 `IconSource` 而不是
`IconElement`（与 `TabViewItem` 同一类槽位：图标的**数据描述**，由宿主按需物化）。

「输入的形态」那一族（本轮补的 `Slider` 刻度 / `TextBox` 多行 / `NumberBox` 校验）
补的是**已有控件页里"官方演示了、我们没演示"的那半边**——控件清单对完之后，
对齐的下一层是能力而不是控件名。三条最容易被想错的地方：
`Slider` 的**画刻度与吸附到刻度是两件事**（`TickFrequency` 只决定每隔多少画一个，
`SnapsTo` 才决定拖动吸附到什么；官方默认 `StepValues`，所以"给了刻度却仍然连续"
是默认行为）；`TextBox` 的**多要两个属性凑**（`AcceptsReturn` 只管回车语义，
折行还要 `.Wrap()`）；`NumberBox` 的 `ValidationMode = Disabled` 下输入越界值
**什么都不做**（不改 `Value`、也不回调，于是"输了 999 界面还是 100"会真的发生，
这是官方给的那一档，不是 bug）。
这三族里会牵动受控值（`Value`）的写入（`StepFrequency` / `TickFrequency` /
`SnapsTo`）都落在静默窗里，不牵动的（`Header` / `Orientation` / `TickPlacement` /
`IsThumbToolTipEnabled`）按（类名, 属性名）登记为惰性——**登记一条的门槛是说得出
它为什么改不动受控值**，说不出就开窗。
`TextBox` 的**清除按钮不装**：`ClearButtonVisibility` 在 UWP 契约里根本不存在
（grep 整个 `Windows.Foundation.UniversalApiContract.winmd` 零命中，它是 WinUI 3
才加的），装出来就是"能写但不生效"的假旋钮。

再往下挖一层（本轮补的八个控件、十二个旋钮）仍然是"官方演示了、我们没暴露"，
但这一批的重点是**展示型控件**：`PersonPicture` 此前**根本没有图片**——
没有 `ProfilePicture`，所谓"优先级"（图 → 首字母 → 按名字推）只剩后两级在起作用，
所以 `PreferSmallImage`（小尺寸时还看不看图）在补上图之前是个不会动的开关，
这次两个一起给；`RatingControl` 的 `PlaceholderValue` 是"底衬浅色星"那一档，
官方默认让它跟着值走，要"3 分压在 5 颗浅色星上"必须显式给。
`ComboBox` 的 `IsEditable` 那一档要认清**打的字不等于选中项**（官方把文本放在
`Text` 上，`SelectedIndex` 仍是下标）；`AutoSuggestBox` 的 `QueryIcon` 收的是
`IconElement` 而不是 `IconSource`（与 `InfoBadge` / `TabViewItem` 那两处不同族，
不翻译），它是**内容槽**：元素每帧都是新的，按形状比，形状没变就不重建，否则每帧换
一次图标会闪。`TabView` 的两个模式（`TabWidthMode` / `CloseButtonOverlayMode`）
里，后者只管关闭按钮**什么时候可见**（能不能关是页签的 `IsClosable`）；
`ScrollViewer` 的 `ZoomMode` 缩放的是**那一棵子树**，所以官方演示的是图片。
`RatingControl` 的 `ItemInfo` 与 `TreeView` 的 `CanDragItems` / `CanReorderItems`
**不装**：前者要先新增一种元素类型才用得上，后者要配套 `AllowDrop`
（本库目前没有这个修饰器）—— 给不出来就不给，不留空旋钮。

再往下（本轮补的九个控件、十七个旋钮）挖的是**外壳与排版**那一层。三条最容易被
想错的地方：`TextBlock` 的**截断要两个修饰器凑**——`MaxLines` 只说"最多排几行"，
超出部分是硬裁的（行被切一半、没有提示），"…"由 `TextTrimming` 单独决定，只给前者
会得到"文字断得莫名其妙"，只给后者则永远不触发；而且**截断需要宽度被限住才看得见**
（放进按内容撑开的 `VStack` 里文字想排几行就排几行）。`NavigationView` 的
`IsPaneToggleButtonVisible` 是**"那个键在不在"，不是"面板能不能开合"**——藏起汉堡键
之后轻扫（触屏）与顶部模式下的入口仍能把面板拉出来；两个响应式阈值
（`CompactModeThresholdWidth` / `ExpandedModeThresholdWidth`）本库用 `null` 表示
**不写**（交给控件自己的默认），因为这两个数字是官方响应式断点的一部分、会随模板与
版本调整，抄进来就是把一个可能过期的数字当成契约。`ColorPicker` 的
`IsMoreButtonVisible` 管的是**展开按钮**而不是展开内容——关掉它，alpha 滑杆与
十六进制框那一片反而**常驻**了（没有按钮可折叠）。
`AutoSuggestBox` 的 `UpdateTextOnSelect` 只决定"点候选时填不填框"，关掉之后
`QuerySubmitted` 仍然会抛（带 `ChosenSuggestion`），只是框里的字不变。
`ContentDialog` 的 `FullSizeDesired` 是**"申请"不是"保证"**（窗口不够高时官方会忽略
这一笔）。`Slider` 的 `IsDirectionReversed` 换的是**值增大的方向**，
`Min` / `Max` / `Value` 一个都不动。`DatePicker` 换 `CalendarIdentifier` 改的是
**表示法**不是那一天（2026 年公历落在回历 1447 年），所以和 `MinYear` 同一族、罩
同一个静默窗。`SplitView` 的 `PaneBackground` 收的是 **`Brush` 实例而不是元素**：亚克力给
`AcrylicBrush(tintColor, tintOpacity:, fallbackColor:)`，纯色给
`new SolidColorBrush(color)`。刷子由画笔工厂产出（它是实例，不是元素树上的节点），
这与官方 XAML 里 `PaneBackground="{StaticResource …}"` 指向一个刷子资源是同一件事。
顺带一个常见误会：亚克力取的是**它后面那层**，`Inline` 形态下面板与内容并排、
背后是页面背景那一层纯色，于是看到的只有 `fallbackColor`——不是亚克力坏了，
是它没有素材。

「图形与画笔」这一族（本轮补的三个形状 + 一支刷子）补的是**声明式一侧缺的那层原语**：
`Ellipse` / `Rectangle` / `Line` 都是 UWP 原生 `Windows.UI.Xaml.Shapes.Shape`
（不是自绘，也不是位图），三个都是**形状而不是控件**——没有模板、没有内容、
不接收焦点、Tab 走不到它身上，要能点就用 `Button` 套一个形状当内容。
描边那九件套（颜色 / 粗细 / 虚线段长 / 虚线偏移 / 三个端帽 / 拐角接法 / 尖角上限）
官方放在 `Shape` 基类上，三个派生类一个都没改语义，所以本库也把它们收在
`ShapeElement` 基类里（handler 同理走泛型基类 `ShapeHandler`），
不抄三遍——抄三遍的代价是将来"改一处漏两处"。
两样**只长在部分形状上**的没有跟着进基类：`Fill` 只有椭圆与矩形有
（`Line` 是两个端点之间的一条线，没有"内部"可填，给它 `Fill` 等于给一个永远不动的
旋钮）；`Stretch` 也只给那两个（线的几何由端点定死，撑不撑都是那一条）。
矩形的圆角叫 `Radius` 而不是 `CornerRadius`：官方这里是**椭圆弧的两个半径**
（`RadiusX` / `RadiusY`），不是 `Border` 那个四角分设的 `CornerRadius`，
名字照抄官方免得让人以为能单独设某一个角。
`AcrylicBrush(...)` 是**画笔工厂**产实例，不是元素——元素那一侧（`Fill` /
`Background` / `PaneBackground`…）收的都是 `Brush`，之前没有它，"给某个属性配一块
亚克力"只能走 `Native()`；`PaneBackground` 这个槽位正是因此才在此时补上
（它是 `Brush` 类型的属性，刷子没到位之前装它等于装一个写不了值的旋钮）。
顺带两条：**刷子按引用比**（`PropWriter.SetRef`），所以样例里那些颜色一律做成
静态字段，每轮 `new` 一支刷子就是一次真的重绘；虚线的段长按**内容**比
（`double[]` 的默认比较器是引用，每轮新数组会让描边每帧重建一次），
所以虚线走 `Seq.SequenceEqual` 而不是 `PropWriter.Set`。

「布局补完」那一族（对应 Gallery 的 *Layout* 一页里此前空缺的四项）四个都是 UWP
原生面板 / 容器，共同点是**位置关系写在子元素身上**（XAML 里的附加属性）：
`Canvas` 是 `.Canvas(left:, top:, zIndex:)`、`VariableSizedWrapGrid` 是
`.WrapSpan(rowSpan:, columnSpan:)`——与官方 `GridAttached`（`.Grid(row:, column:)`）
同一个形状，落点都是 `ElementHandler.AttachChild` 那个钩子。
`RelativePanel` 是这一族里唯一**拿不到兄弟就不能落**的：它一半关系只问面板
（`AlignXxxWithPanel`，布尔），另一半要指向另一个子元素，而 `AttachChild`
 只有自己这一个子元素可用，所以它改成"孩子们都造好之后整体重落一遍"。
兄弟在元素树里是**同层下标**（不是 `x:Name`——声明式树里没有名字可给）；
每轮全量重落，所以"去掉一条关系"是真的会失效，不会留下上一轮的旧值。
`Viewbox` 是布局族里唯一的**单子元素**容器，且继承 `FrameworkElement` 而非
`ContentControl`（子内容在 `Child` 上），已在 `SingleChildAccessor` 登记，
不登记就每轮重建整棵子树。

「状态与信息」这一族是 Gallery 里此前**整个缺掉的一个分类**（`ProgressBar` /
`ProgressRing` 的元素早就有，只是从没进过索引）。三者共用一条判据：
**确定 / 不确定由 `Value` 是不是 null 决定**，不是另有一个 `IsIndeterminate`
开关——两种形态本来互斥，分成两个字段就会出现"既给了值又说不确定"这种没有答案的
组合。另两个状态位（`ShowError` / `ShowPaused`）是**叠加**在进度之上的状态，
不是另一种进度，所以两者可以同时出现。
`ProgressRing` 用的是 WinUI 2 那个而不是 UWP 原生版：原生版只有 `IsActive`、
压根不支持确定进度。它的 `IsActive = false` 是**整个消失**而不是暂停——
要暂停请把 `Value` 停住，或换 `ProgressBar` 的 `ShowPaused`。
`RefreshContainer` 是这一族里唯一带"延迟回执"的：`RefreshRequested` 在官方是
可延迟的（取 `Deferral` 之后可视化器一直转，直到 `Complete()`），
本框架把它包成 `RefreshTicket`——直接把 WinRT 的 `Deferral` 递给用户代码等于把原生
对象泄漏进组件层，而且"回调里 Complete 两次"没有任何防线。
它也是单子元素容器（继承 `ContentControl`），里面的内容**必须自己能滚**，
否则永远拉不出刷新。

`ToolTip` 是本库**第二处**在可视树之外管一棵真子树的地方（第一处是内容型
`Flyout`）：它挂在宿主的附加属性 `ToolTipService.ToolTip` 上，不在 `Content` /
`Children` 里，协调器顺着可视树递归走不进去，所以卸载入口显式放在
`Reconciler.UnmountNode`（与 `ContextFlyout` 同一个位置、同一个理由）。
两条入口对应官方 XAML 的**两种写法**而不是同一个旋钮的两个名字：
`.ToolTip("文本")` = 特性语法 `ToolTipService.ToolTip="…"`（没有子树），
`.ToolTip(元素)` = 属性元素语法 `<ToolTipService.ToolTip>…子树…</ToolTipService.ToolTip>`
（官方画廊里"图文混排的提示"就是这一档，内容是任意元素）。
**不暴露 `IsOpen`**：气泡的开合由指针与焦点驱动，声明式写 `true` 之后它会自己
收起、下一轮又被写回成 `true`，于是每帧都在"拉开—收起"。这与 `TeachingTip`
（程序控制为主，做成受控）是两回事，别照抄。

「浮层与双窗格」那一族（`TeachingTip` / `TwoPaneView`）是 WinUI 2 的两个真控件，
共同点是**槽位/目标都不是"第一个子元素"那类隐式约定**：`TwoPaneView` 与
`SplitView` 同形（`Pane1` / `Pane2` 两个独立槽位，都由 handler 自己管，
`UnmountTree` 认不出 `Control` 型的容器）；`TeachingTip` 的 `Target` 要指向
**另一个元素**，而 XAML 靠 `x:Name` 拿到对象引用、声明式树里没有名字可给 ——
所以它沿用 `RelativePanel` 那条规矩：填**同层下标**，进树之后（`Loaded`）
再换成真正的兄弟控件（挂载那一刻还问不到 `Parent`）。
`TeachingTip.IsOpen` 是受控的：轻 dismiss 与关闭按钮都是用户在改它，那一发
由 `Closed` 回执、靠 `EchoGuard` 认下来；`Closing` 那条带 `Cancel` + `Deferral`
的通道不暴露（声明式树下没有能拦住一次关闭的地方）。`TwoPaneView.Mode` 反过来
**只出不进**：官方 `Mode` 是只读的（由可用尺寸算出），压根不可写的属性更不能
装成受控。

「取值与富文本」那一族（`ColorPicker` / `RichEditBox`）是一对**对照**：
`ColorPicker` 受控 `Color`（写 `Color` 会同步抛 `ColorChanged`，那一发靠
`EchoGuard` 认下来），而"会把颜色夹走"的那批开关（`IsAlphaEnabled` 会把 A 拉到
255、`ColorSpectrumComponents` 会把颜色投影到新轴上）罩静默窗——与 `DatePicker`
的 `MinYear` 同形。`RichEditBox` 反过来**连受控都没装**：官方<b>没有</b> `Text`
属性（文本住在 `Document` 里，实测赋值直接 CS1061），而且写进去 `abc`、读出来
`abc\r`——末尾那个 `\r` 是**文档结构**不是用户输入的文本，剥掉它只是我们自造的
归一化，控件并不认这份约定。受控的前提是"写进去的值"与"回读出来的值"是同一个
东西，这条不成立时就不装（与 `CalendarView` / `TreeView` 同一条规矩）。
它的 `initialText` 只在挂载时写一次，之后文本一律只出不进。

**`ForEach` 传进容器会被摊平**——这一条不写出来，样例很容易写成"看着对、
跑起来全叠在一起"。`Group` / `ForEach` 造的是 `GroupElement`（渲染成一层**裸
Grid**）：它作为一整棵树的根时是"覆盖容器"（`Reactor.Template` 的自绘标题栏
就靠它压在内容区上面），但作为**子项**传进另一个容器时会被 `FilterChildren`
摊平，N 个结果直接变成父容器的 N 个子项。摊平之前，
`VStack(20, ForEach(cats, Section))` 拿到的是 1 个 Grid，里面 N 个分区全叠在
(0,0)；`GridView(ForEach(items, Card))` 更是只有 **1 项**，选中下标永远是 0。
这两种都是寂静的错误：编译得过、不抛异常、只是界面不对。

每个样例卡（对应 WinUI 3 Gallery 的 `ControlExample`）是三块：标题行右侧一个
**预览主题按钮**（把这一块预览单独切成浅 / 深 / 跟随应用，外壳不受影响），
中间是带边框的预览区，下面是**可折叠的源码**（`Expander`，折叠状态由控件自己持有）。

**官方对应卡**放在详情页顶部：列出这个样例背后那个控件的**官方类型全名**
（等宽、可选中复制）与文档链接。对应关系住在 `Gallery/ApiMap.cs`，与索引分开——
这两种信息性质不同（索引说"画廊里有什么、怎么排"，ApiMap 说"官方那边叫什么"），
混在一起索引那份数据会被 20 多条文档链接淹没。分开放的代价是可能脱节，
所以有一条契约测试盯着：表里每个 id 都必须真有那么一个条目（插个死键当场红）。
`Microsoft.UI.Xaml.Controls.*` 那几个控件在 UWP 侧由 **WinUI 2** 提供，
文档站只维护 WinUI 3 同名类型的页面，卡上写明了这一点。

搜索结果右侧那行小字写的是"这一条命中在标题 / 描述 / 哪个样例"。只有排序
而不说明命中位置，遇到"搜 TabView 出来一堆没写着 TabView 的条目"时，读者
分不清是排序错了还是搜索坏了——写出来这两件事就分开了。

导航：<b>外壳持一整条历史栈</b>，栈顶是当前页。之前只存"当前一页"、返回一律
跳回「全部示例」，于是「首页 → 搜索 → 条目」按返回会掉到浏览页——历史里根本
没记这一层，观感是"返回键在乱跳"。栈深同时就是 `Frame` 的 `stackDepth`：
变深走 `Navigate`，变浅走官方 `Frame.GoBack()`（**真** BackStack，不是自己
维护一个页面栈），返回过渡与返回音效都由官方那条路驱动。

深浅色主题由 `ThemeResource` 活引用画笔统一换色——换主题不重建树，只改 `Color`。

### 展示的源码就是跑起来的那份

源码块**读的是包里的嵌入资源**（`Reactor.Gallery.csproj` 里
`EmbeddedResource Include="Gallery/Samples/**/*.cs"`），不是另抄的样板。
`SampleIndex` 静态构造按 `Component<T>()` 的类型名推出
`SourcePath = "Gallery/Samples/<类名>.cs"`，于是「文件名 == 类名」成了一条契约，
由 `tests/Reactor.Core.Tests/GalleryIndexTests.cs` 守着：

- 样例文件里必须声明与文件同名的组件类
- 索引引用的每个组件都有对应源文件（只看代码里的 `Component<…>`，
  描述文案里的字样不算——所以匹配前先把字符串字面量抹掉）
- 条目 id / 分类 id 不重号，样例目录没有孤儿文件

读不到时页面显示「源码未随包一起构建」——这条 break glass 提示比显示一段
过期的样板诚实。

## UIA 自动化自检

`samples/uia-check.ps1` 跑一遍真实窗口的 UIA 树：

```powershell
powershell -ExecutionPolicy Bypass -File samples/uia-check.ps1
```

它守四条只有跑起来才看得见的回归：

1. **遍历全程不抛异常**（自定义 AutomationPeer 的 `GetChildrenCore` 返回已 detach
   的元素时，整棵树会断在这里）。走 `RawViewWalker` 而不是 `ControlViewWalker`——
   客户端真正常用的正是 Raw 视图。
2. **可聚焦控件都有 Name**（屏幕阅读器念出来是一串空白，自动化脚本也抓不住）。
3. **同一层内 AutomationId 不重号**（按 id 找元素会找错人）。
4. **顶部搜索框在树里，且 `Ctrl+F` 连按两次都把焦点送过去**（这一条跨了两层实现：
   快捷键是官方 `KeyboardAccelerator`，焦点由 `FocusToken` 的边沿驱动）。
   之所以要按**两次**：令牌是边沿触发，而回调闭包捕获的是它当帧看到的值——
   框架若把第一次的闭包永久留在登记表里（后续每帧新写的闭包跟不上），第二次按下去
   算出来还是同一个值，令牌不动、焦点也不动。只按一次永远发现不了。
   这一段要求窗口在**前台**（`SendKeys` 发不进去时只报告、不判红）。

最后再点一个导航项，确认树真的变了。定位窗口用「进程名」+「窗口标题」两条
判据：UWP 的顶层窗口是 `ApplicationFrameWindow`，宿主进程叫
`ApplicationFrameHost` 而不是应用自己，只按进程名找一个都找不到。

拿去量别的应用（比如 Reactor 模板）时加 `-RequireSearchBox $false`——
它没顶栏搜索框，第 4 条会误报。

**它不会自己拉起应用**：UWP 要从开始菜单 / VS 里启动（见上一节）。脚本等
30 秒，应用起来了就自己挂上去。

### 逐页扫描

`uia-check.ps1` 量的是「当前停在的那一页」。`samples/uia-scan-pages.sh` 把左侧
导航一项项点过去，在**每一页**的全树里找「可聚焦但没有 Name」的控件：

```bash
bash samples/uia-scan-pages.sh <HWND>
```

HWND 从 `winapp ui list-windows --show-hidden` 拿。它**不做** `--hide-offscreen`：
屏外元件同样是读者能滚到的，漏掉它等于漏报。

这一层是补出来的：卡片列表那种「项本身是个控件」的形状（`SettingsCard` 当
`GridView` 的项）里，容器 `GridViewItem` 的名字不会被推导出来，读屏在列表里念的是
空白——而它在每一页都成立，只看一页反而会以为是个孤例。修法见库里的
`ItemsViewHandler.Initialize`：容器继承项元素的 `AutomationProperties.Name`。

## 抄代码时注意

- **入口没有 App.xaml**：`App.cs` 手写 `Main`，manifest 的 `EntryPoint` 指向它
- **`VirtualizingList` 的 `itemKey`**：默认路径是 `ItemsRepeater`，它按下标复用容器、
  不做按 key 复用（官方 Reactor 同样如此），所以 `itemKey` 只在**自绘回退**路径上
  起作用——也就是原生桥没加载起来（产物没落到 `AppX`）而回退自绘时才需要它。
  那种场景下数据源会插入 / 删除时，不给稳定身份就会因为下标位移而拿错内容
- **`Native()` 只有 `Token` 这一个旋钮**：`Internal/Handlers.Native.cs` 的 `Update`
  全程只比 `Equals(oldElement.Token, newElement.Token)`，`Factory` 的引用**不参与比较**。
  所以"创建委托必须引用稳定"这句话是错的（`Reactor.uwp/Elements/Native.cs` 上的旧注释
  与实现不符）——内联 lambda 不会每帧重建控件，但**换了工厂而 Token 没变，新工厂也不会
  被调用**。要换控件（含换工厂）就改 `Token`。示例页把委托写成字段，是为了让
  "重建只可能来自 Token"这件事在代码里看得见，不是为了绕开那条不存在的限制
- **别急着关 AOT**：它能第一时间暴露反射 / 运行时代码生成这类写法；
  真需要反射就加 `rd.xml` 或 `[DynamicallyAccessedMembers]`
