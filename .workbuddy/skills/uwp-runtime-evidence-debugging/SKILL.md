---
name: uwp-runtime-evidence-debugging
description: UWP/WinUI 排查律令——先拿运行时证据再下结论。用于"值不对/图不出来/控件显示异常"这类查了三轮还在猜的问题，以及任何你准备说"这是平台限制/改不了"的时刻。源自 2026-10 本仓库「应用名显示成『声音』+ 图标小到看不见」事故，AI 连续三轮误判的完整复盘。
agent_created: true
---

# 先拿证据，再下结论

> 本 skill 不是知识库，是**事故复盘**。
> 背景：本仓库模板页出现「应用标题和关于卡片都显示『声音』」「图标小于鼠标指针」，
> AI 连续三轮给出诊断，**三轮全错**，第四轮靠运行时证据才定案。
> 下面先复原那三次误判，再抽出可复用的律令。

---

## 一、事故复原

### 现象

- 窗口标题栏 / 关于卡片标题：显示 **`声音`**（应该是 `Reactor 模板`）
- 「关于」卡片的应用图标：小到几乎看不见

### 三轮误判（每轮听起来都对）

| 轮 | 我下的结论 | 错在哪 |
|---|---|---|
| 1 | 「`SettingsCardHeaderIconMaxSize` = 20，是原版硬上限，要改得偏离原版」 | **拿 GitHub `main` 源码当"已安装版本"的证据**。装的是 `SettingsControls 8.2.251219`，其包里**根本没有这个键**（二进制搜也搜不到）。main 分支有 ≠ 你装的有 |
| 1 | 「`BitmapIcon` 要显式给 `.Width(20).Height(20)`」 | 这是**自己制造的新 bug**。`BitmapIcon` 不像 `Image` 那样把位图缩放适配 —— 设尺寸等于把 106px 原图**裁**出一个角，看着就是"图标缩没了"，于是下一轮继续往尺寸方向钻 |
| 2 | 「清单 `<Logo>Assets\StoreLogo.png</Logo>` 的反斜杠混进 URI，静默加载失败」 | 方向对（URI 确实要规范化），但**具体形态猜错了**：`Package.Current.Logo` 交出来的是 `file:///<安装目录>/Assets/SmallTile.scale-150.png`，**不带反斜杠**，而且是 SmallTile 不是 StoreLogo |
| 3 | 「是 OS 层 `ms-resource` 解析串了，C# 改不动，只能清部署缓存」 | **过早投降**。真实原因是清单里**两个 DisplayName 走不同的解析链**，只有一条坏了。改坏的那条就行，好的那条（外壳/开始菜单）保留 `ms-resource:` 照旧本地化 |

### 真正的根因（两条独立的链，不是一个 bug）

```
uap:VisualElements/@DisplayName = ms-resource:AppDisplayName
   → 外壳 / 开始菜单 / 磁贴 走这条     → 实测解析正确 = "Reactor 模板"  ✅

<Properties>/<DisplayName> = ms-resource:AppDisplayName
   → Package.Current.DisplayName 走这条 → 实测解析成 SoundGroup/Text = "声音"  ❌
```

`Package.Current.DisplayName` 那条链解析到了**毫不相干的键**的值。
按名字查资源表（`ResourceLoader.GetString("AppDisplayName")`）则**永远正确**。

图标那边是两件事叠加：URI 走的是 `file:///`（非官方包内通道）+ 我上一轮加的尺寸把图裁了。

---

## 二、六条律令（照做，别再翻车）

### 1. GitHub main ≠ 你安装的版本

查"某个资源/属性/常量的真值"时，**证据必须来自装的那份**，不是源码仓库。

```bash
# 装的那份在哪
ls ~/.nuget/packages/<包名>/<版本>/
# 查资源真值（包里带 .pri 的）
makepri dump -if <包内 .pri> -of dump.xml -dt detailed
# 查常量/字符串（包里是二进制的就二进制搜）
grep -ral "SettingsCardHeaderIconMaxSize" ~/.nuget/packages/<包名>/<版本>/
```

**搜不到就是没有。** 源码仓库 main 分支里有，对你装的这个版本**不构成任何证据**。

### 2. 没有运行时证据，不许下结论

猜三轮的成本 >> 加一次诊断的成本。**一次把所有可疑值全部打出来**：

```csharp
// 临时打进 UI（比打日志快，不用找文件）
description: $"[诊断] DisplayName={Package.Current.DisplayName} | " +
             $"资源AppDisplayName={loader.GetString("AppDisplayName")} | " +
             $"Logo={Package.Current.Logo} | " +
             $"语言={string.Join(",", ApplicationLanguages.Languages)}";
```

一次跑完，四个字段同时到手，一轮定案。
**验证完立刻删**（标 `TODO(诊断)` 便于收尾 grep）。

### 3. 「不可能的值」= 消费链错位，不是资源本身错

看到 A 的位置显示了 B 的值，**先证明资源本身没问题**，再去查谁在消费它。

```bash
makepri dump -if <AppX/resources.pri> -of d.xml -dt detailed
```

确认三件事：值对不对、是不是只有一条、URI 对不对。
三条都对 → 资源没问题 → 问题在**消费它的那条链**。这时要问的是「有几条链」，不是「这条链哪错了」。

### 4. 同名/同类 API 可能走不同的链

本事故里的三组分叉：

| 看起来是一回事 | 实际是两条链 |
|---|---|
| 清单的两个 `DisplayName` | `<Properties>` → `Package.Current.DisplayName`（坏）；`VisualElements/@` → 外壳（好） |
| `Image` vs `BitmapIcon` | 都吃 URI，但一个有显式尺寸撑着（图没出来也看不出），一个靠位图撑自然尺寸（直接塌成 0） |
| 按名字查资源 vs 系统解析 `ms-resource:` | 前者永远对，后者可能串 |

**改之前先问：有几条链？坏的是哪条？好的那条别动。**
（本事故只改坏的那条成字面量，好的那条保留 `ms-resource:`，所以外壳名字仍然本地化。）

### 5. 上一轮自己的"修复"，可能就是这一轮的 bug

改完回头**逐行审一遍上一轮加的每一行**。
这次的 `.Width(20).Height(20)` 就是上一轮我自己加的，它制造了"图标缩没了"这个症状，
然后我又围着这个假症状查了两轮。

### 6. 静默失败：先查加载，不是查尺寸

URI 非法 / 资源解析失败，**不抛异常、不报错**，图就是不出来。
所以"图标小到看不见"的第一嫌疑人是 **URI 没加载成功**，不是尺寸设小了。

---

## 三、取证手段（按性价比排序）

| # | 手段 | 拿到什么 |
|---|---|---|
| 1 | 运行时诊断串打进 UI | 一次拿到全部真实取值。**永远先做这个** |
| 2 | `makepri dump -dt detailed` | PRI 里键的实际值、索引、URI —— 证明"资源本身对不对" |
| 3 | `Get-StartApps` | 外壳（开始菜单）解析出的显示名，与 `Package.Current.DisplayName` 对照即可判定是哪条链坏了 |
| 4 | 二进制 grep 装的 NuGet 包 | 某个常量/资源在**已安装版本**里到底存不存在 |
| 5 | 注册表 `HKCU:\...\AppModel\Repository\Packages\<FullName>` | 值是未解析的 `ms-resource:` 原文 → 证明是**实时解析**而非注册期缓存 |
| 6 | 官方源码对照：`git -C <microsoft-ui-reactor> show v0.1.0-preview.16:src/...` | 某个语义是**官方本来就有的**还是我们自己引入的。本地 clone 带全部 tag，按我们对齐的基线版本取，不要用 main |

PowerShell 输出容易被吞，落文件再读：
```powershell
"结果" | Out-File D:\...\out.txt -Encoding utf8
(Get-StartApps) | ... | Out-File D:\...\out.txt -Append -Encoding utf8
```

---

## 四、两个反复踩的环境坑

### Debug 构建不刷新 AppX 目录

`bin/.../AppX/AppxManifest.xml` 和 `AppX/resources.pri` **只有部署时才刷**，`dotnet build` 不动它们。
表现：改了 resw / manifest，跑起来还是旧值 → 误判成"改了没生效 / 平台 bug"。

**用 `winapp run .` 跑，别只 build。** 打包验证加 `-p:GenerateAppxPackageOnBuild=true`。

### 别用 `dotnet build` + 松散注册去跑示例（会把 VS 的部署顶掉）

`UwpApp` 那套 `bin/.../win-x64` 直接注册能跑，于是想当然地套到 `samples/` 上——**跑不起来**：
进程起来、LocalState 目录建好，然后 3 秒内无声退出，没有 Application Error、没有 .NET Runtime 事件。
回退到未改动的干净版本**同样退出**，所以这不是代码回归。

原因：示例依赖 WinUI 2 + CommunityToolkit，`Microsoft.UI.Xaml.winmd` / `resources.pri`
只存在于 VS 生成的 `bin/.../AppX/` 子目录，松散注册用的根目录里没有，
手工把这两个文件拷过去也救不活（还缺别的）。

**代价不只是跑不起来**：`run.ps1` 会先 `remove old registration` 再松散注册，
把 VS `F5` 部署的那个包顶掉。用户的验证链路因此断掉，需要他在 VS 里重新 F5 一次才恢复。

纪律：
- 示例的运行验证交给 VS `F5`（VS 自己会生成完整 MSIX 布局并部署）
- 需要"不手动点就能取证"时，用**源码里的自检**（`DispatcherTimer` + 程序化改控件值 +
  布局采样），别指望从外部驱动 app
- 任何 `DispatcherTimer` / 事件回调里的诊断代码必须自己 `try/catch`：
  UWP 里这类异常没人接，**进程直接终止**，表现就是"启动即退出"，
  排查工具本身成了崩溃源，会把人往错的方向带

### AOT 发布撞 `LNK1181: advapi32.lib` —— 先自检探测链，别急着归因

ILC 找链接器/库路径的**真实**链路（`Microsoft.NETCore.Native.Windows.targets:126`）：

1. MSBuild `Exec` 调 `findvcvarsall.bat $(_targetArchitecture)`，`IgnoreExitCode=true` + `ConsoleToMSBuild`
2. 该 bat 用 `vswhere.exe` 找 VS → `CALL vcvarsall.bat amd64` → `where link` → `ECHO %LIB%`
3. **exit 0** → 输出 `#` 后那一段进 `AdditionalNativeLibraryDirectories`，`CppLinker` 指向找到的 link.exe
4. **exit 1** → MSBuild 直接 `Error "Platform linker not found. Ensure you have ... Desktop Development for C++ workload"`

**为什么报的是 LNK1181 而不是 *Platform linker not found***（这两个错的含义完全不同）：

`vcvarsall.bat` 里 VC 的工具路径不靠注册表，所以即使 `reg.exe` 被拦，它照样能设出
**VC 那半截** `LIB`（`MSVC\14.51\lib\x64`）→ `findvcvarsall.bat` 的 `IF "%LIB%"==""` 不成立
→ exit 0 → `CppLinker` 正常指向 `...\MSVC\...\bin\Hostx64\x64\link.exe`，
**不会**触发 *Platform linker not found*。
但 Windows SDK 那半截（`Windows Kits\10\lib\<ver>\um\x64`，`advapi32.lib` 就在那儿）
是查注册表拿的 → 被拦 → 这半截丢了 → 链接时找不到 `advapi32.lib` → **LNK1181**。

所以 LNK1181 的准确含义是：**VC 找到了，SDK 没找到**。是环境（注册表探测被拦），
不是项目配置、也不是框架的问题 —— 别去改 csproj。

自检只要 30 秒，别猜：

```bash
cmd //c "C:\Users\gold\.nuget\packages\microsoft.dotnet.ilcompiler\<ver>\build\findvcvarsall.bat" x64
```

成功会打印两行：`...\bin\Hostx64\x64#` 和一条含 `Windows Kits\10\lib\<ver>\um\x64` 的 LIB。
空输出或非零退出才是探测真坏了。

**`reg.exe` 确实在黑名单里 —— stderr 实证（2026-10-05）：**

```
PROGRAM BLOCKED BY SECURITY POLICY - The sandbox prevented a program on the
configured Program Blacklist from starting:
  - reg.exe (C:\WINDOWS\system32\reg.exe)
```

紧接着就是 `LINK : fatal error LNK1181: 无法打开输入文件"advapi32.lib"`。

**但拦截是"时灵时不灵"的，别拿一次手动成功当反证。** 同一天我踩过这个坑：
手动 `reg query HKLM\...\Windows Kits\Installed Roots` 正常返回、
手动跑 `findvcvarsall.bat x64` 也成功输出完整 LIB、甚至有一次 publish 直接过了
（exit 0，11.1 MB 产物）—— 于是我判定"沙箱没拦 reg.exe、旧结论是过度归纳"，
结果下一次 publish 就撞了 LNK1181 并打出上面那段 `PROGRAM BLOCKED`。
**教训**：手动跑通 ≠ publish 里那条子进程链也跑得通。判据要取**失败那次的完整 stderr**，
不要取"我试了一下没报错"。

**首选根治方案：绕开探测，别让 ILC 去调 `findvcvarsall.bat`。**
`Microsoft.NETCore.Native.Windows.targets:126` 那个 `Exec` 的条件是
`'$(IlcUseEnvironmentalTools)' != 'true'` —— 置为 true 就整个跳过，
也就不会走到 `vcvarsall.bat` → `reg.exe`（已实测 EXIT=0，无 LNK1181）：

```powershell
$vc   = "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231"
$kits = "C:\Program Files (x86)\Windows Kits\10"
$sdk  = "$kits\Lib\10.0.26100.0"
$env:PATH    = "$vc\bin\Hostx64\x64;" + $env:PATH
$env:LIB     = "$vc\lib\x64;$sdk\um\x64;$sdk\ucrt\x64"
$env:INCLUDE = "$vc\include;$kits\Include\10.0.26100.0\ucrt;$kits\Include\10.0.26100.0\um;$kits\Include\10.0.26100.0\shared"
dotnet publish UwpApp\UwpApp.csproj -c Release -p:Platform=x64 -r win-x64 `
  -p:PublishAot=true -p:IlcUseEnvironmentalTools=true
```

不改命令行、只补 `LIB` 也能过（已实测两次 EXIT=0），但那条路仍会调 `findvcvarsall.bat`，
属于"运气好才不撞"：

```powershell
$sdk = "C:\Program Files (x86)\Windows Kits\10\Lib\10.0.26100.0"
$env:LIB = "$sdk\um\x64;$sdk\ucrt\x64;$env:LIB"
dotnet publish UwpApp\UwpApp.csproj -c Release -p:Platform=x64 -r win-x64 -p:PublishAot=true
```

**判成败以产物为准，不看退出码**：沙箱/安全策略的拦截信息会混进 stderr 并污染退出码，
publish 可能实际成功却被判失败。检查产物：exe 存在、PE 机器码 `0x8664`、
同目录没有托管主程序集（`UwpApp.dll` 不该出现在 publish 目录）。

### 示例项目可能引用的是发布包，不是源码

本仓库 `samples/*` 引的是 **NuGet 发布包**（看 csproj 里锁的版本），
`UwpApp` 才是 `ProjectReference`。所以**框架侧的修复示例项目吃不到**，
要么就地做一份等价实现（幂等，新旧包都对），要么用包里已有的 API。
给框架加新参数前先看 csproj 的引用方式，否则会编译失败。

**就地实现是欠债，不是解法**：等含该修复的版本发到 nuget.org、示例把版本号升上来，
就必须删掉那份就地实现。留着会变成"框架改了、这里没改"的两处不一致——
2026-10-05 清过一次：`MainPage.cs` / `BlankTemplateApp.cs` 各有一份 `ToAppx()`
（`file:///` → `ms-appx:///`），框架 `Internal/PackUri.cs` 修好后包升到 alpha.5，
两份都删了。判断能不能删：确认新包里真的有那段逻辑（解开 nupkg 在
`lib\net10.0-windows10.0.26100\Reactor.uwp.dll` 里搜类型/方法名），别靠版本号猜。

**怎么解开 nupkg（本机实测可行的一条路）**：PowerShell 两条常规路都堵——
`Add-Type` 会被安全策略拦（"compiles and loads .NET code at runtime"），
`Expand-Archive` 解到 `%TEMP%` 下最容易**输出被进度条吞掉**且目录空空如也。
用 python 的 `zipfile` 即可，还能顺手把两项校验一起做了：

```bash
"$HOME/.workbuddy/binaries/python/versions/3.13.12/python.exe" -c "
import zipfile, struct
z = zipfile.ZipFile(r'D:\...\artifacts\Reactor.Uwp.0.1.0-alpha.6.nupkg')
for n in z.namelist(): print(n, z.getinfo(n).file_size)
d = z.read('lib/net10.0-windows10.0.26100/Reactor.uwp.dll')
off = struct.unpack_from('<I', d, 0x3c)[0]
print('machine=0x%x' % struct.unpack_from('<H', d, off+4)[0])   # 0x14c=纯IL；0x8664=被 pack 平台污染
for n in ['SelectionRestore','ShouldExpectEcho','CancelIfUnconsumed']:
    print(n, d.find(n.encode('utf-8')))                          # -1 = 这批改动没进包
"
```

- **`machine` 必须是 `0x14c`**：`dotnet pack` 一旦带 `-p:Platform=x64`，托管 dll 会被编成 x64 专属，
  arm64 消费方引用时报 `CS8012`（引用程序集面向的是另一个处理器）。CI 里 pack **不带** platform 就是为此。
- **类型名搜 PDB 之外的东西一律用 UTF-8**：元数据里的字符串是 UTF-8，`utf-16-le` 查 `(utf16=-1)` 属正常。
- 包体布局：`lib/<tfm>/` 下只有**一个**托管 dll，双架构原生桥在 `runtimes/win-x64|win-arm64/native/`；
  这两个架构必须同时存在，少一个时包照样能推、只有 arm64 消费方才炸，所以 CI 里有
  专用步骤卡（`publish.yml` 的"校验双架构原生桥都在包里"）。

### 取证之前，先确认"量具"本身可信

排查「设置页点了没反应」时连着踩了四个坑，全都是**探针造出来的假象**，
按这些假象推出来的结论（"控件每拍重建"、"面包屑条目掉成 0"）全部作废：

1. **`RuntimeHelpers.GetHashCode(fe)` 不能当实例指纹。** AOT 下它会随 GC 变，
   同一拍里两个不同控件也可能撞号。要判断"控件是不是被重建了"，用
   `ConditionalWeakTable<控件, 序号>` 自己发号：换个号才是换了个实例。
2. **CsWinRT 的投影集合只实现泛型 `IEnumerable<T>`。**
   写 `if (obj is System.Collections.IEnumerable seq)` 判 WinRT 集合（如 `ItemCollection`）
   恒为 false，然后 `Count()` 出来的 0 是"探针自己造的 0"，不是真实条目数。
   必须用 `IEnumerable<object>`，或者直接用强类型的 `.Count`。
3. **别让诊断代码去改被测对象。** 上一版自检用 `DispatcherTimer` 每秒拨一次
   `ToggleSwitch.IsOn` / `RadioButtons.SelectedIndex`——它自己就是最大的干扰源，
   日志里那些"状态在变"极可能只是自检在拨。观测一律只读；
   要"不手动点也能取证"就给控件挂**只读**的事件监听，记录事件当下控件自己报的值。
4. **示例用的是 NuGet 包，不是工程引用**（`samples/*/Reactor.Template.csproj`
   里是 `PackageReference Reactor.Uwp`）。加在 `Reactor.uwp/` 里的 `Trace`
   在示例里**根本不会执行**——日志里看不到不是"没触发"，是"没编进去"。
   要观测示例，埋点必须落在示例自己的源码里；只有 `UwpApp`（`ProjectReference`）才会带框架侧日志。

---

## 四·五、日志到底在哪（`LocalState` 是符号链接）

UWP 应用的 `ApplicationData.Current.LocalFolder` **在这一台机器上不在 C 盘**：

```
C:\Users\<user>\AppData\Local\Packages\<PFN>\LocalState
   → 符号链接 → D:\WpSystem\S-1-5-21-<...>\AppData\Local\Packages\<PFN>\LocalState
```

**坑**：`find` 默认不跟随符号链接。在 `C:\Users\<user>\AppData\Local` 里
搜 `reactor-startup.log` **永远搜不到**，于是很容易得出"没有日志 / 日志没生成"的错误结论。
先 `ls -la` 看 `Packages\<PFN>\`，确认哪个是 symlink，再顺着 `-L` 读：

```bash
ls -la /c/Users/gold/AppData/Local/Packages/<PFN>/ | grep LocalState
find -L /c/Users/gold/AppData/Local/Packages -maxdepth 3 -iname "*.log"
```

PFN 不是包显示名：看 `<Package.appxmanifest>` 的 `<Identity Name=... Publisher=...>`，
目录名是 `<Name>_<hash>`，跟 msix 里的 DisplayName 毫无关系。认错目录同样是"没有日志"的假象。

---

## 四·六、三个"看着像数据、其实是量具"的读数

同一个当事人踩三次，列成对照表：

| 读数 | 真实情况 | 怎么验证 |
|---|---|---|
| `RuntimeHelpers.GetHashCode(fe)` 当实例指纹，**每拍都变** | AOT 下哈希随 GC 变，"控件被反复重建"是假的 | 用 `ConditionalWeakTable` 自己发号，换号才代表换了实例 |
| 非泛型 `IEnumerable` 判 `ItemCollection` → Count 恒 0 | CsWinRT 的投影集合**只实现泛型** `IEnumerable<T>`，非泛型判永远 false | 判泛型版本，或直接读 `IVector<object>.Count` |
| patch 刚落盘前拍快照 → "受控值没同步" | 快照跑在 `UseEffect` 里，**早于** handler 的 `Update` 写属性 | 隔一拍（下一帧）再拍一次做对照，差值才是真相 |

还有一条反过来的：**这不是时间错位**——WinUI 的 `RadioButtons` / `ComboBox` 换选项时会
**先抛一次 `SelectedIndex = -1`（取消旧选中），紧接着才抛新值**。把它当用户输入回调出去，
state 会被打成非法值 -1，界面表现是"第一次点不生效 / 要点两次才对"。

### GetHashCode 会换着花样骗第二次

同一条教训会伪装成别的样子回到你面前。第一次是 `RuntimeHelpers.GetHashCode(fe)` 当实例
指纹（AOT 下随 GC 变）；第二次是**日志取证里的 `GetHashCode`**：面包屑那条修复要证
"每次都换了数据源实例"，日志于是打印 `#{hash:X}`，看上去四个会话四个不同的号，像是证据。

**它不是。** 那四行分别隶属于四个进程（每行前面都跟着一行宿主初始化 `BACKDROP ...`，
那才是会话分界线），而 `GetHashCode` **跨进程本来就不一样**——那四个号**根本算不上证据**。

定成硬规矩：**仓库里任何给人看的实例身份，一律 `CtlId.Tag(...)`，不许 `GetHashCode`。**
`CtlId` 是引用相等的稳定编号（同一实例永远同号、号只增不减），日志里 `#12 → #37`
就是真的换了实例。

配套的一步同样不能省：要证"每次都换新实例"，必须在**同一个进程会话里触发两次**。
嵌在一个一辈子只下发一次的 UI 上（比如条目写死的面包屑），那条证据根本取不到——
所以改动证据链时要顺手给 UI 加触发点，而不是等着它自己发生。

> **⚠️ 这条的旧结论翻修了两次，最终判据见下。**
> ① 第一版修法"回调里过滤掉负值"——那是猜的，会误伤真实的"清空选择"；
> ② 第二版改成按就绪过滤（`ReadyGate.cs`）——**仍然不够**：控件早就 Loaded 过之后，
>    换选项依然每次都抛 `-1`。实测症状是"点几下之后回调不再更新"——
>    最后一发是 `-1` → `setState(-1)` → 受控下发 -1 → 界面上没有任何一项被选中。
> ③ **站得住的判据是事件参数。** `RadioButtons.cpp:378` 抛的是
>    `SelectionChangedEventArgs({RemovedItems}, {AddedItems})`；用户点一下会同时走
>    两条路径（`cpp:415` `Select(N)` / `cpp:431` `Select(-1)`），**顺序没有保证**
>    （实测 -1 有时在真值前 3ms、有时在后 4ms），值和就绪状态都区分不开它们。
>    而 `Select(-1)` 那发的 `AddedItems` 是 `{ null }`（`GetDataAtIndex` 在
>    `cpp:401` 显式 `return nullptr`）——**判据是"AddedItems 里有没有非 null 的项"，
>    不是 `Count > 0`**（Count 就是 1）。
> 现行实现：`Handlers.Controls.cs` 的 `SelectionArgs.SelectedSomething`。
>
> **教训：值、时序、就绪状态都是不可靠判据；能拿到的最结构化信息（事件参数）才是。**

---

## 四·七、先查源码，再动手改（源 → 证 → 改）

"改一次、让用户跑一次、再看一次"这个循环的代价极高，而且**插桩本身会扰动被测对象**。
遇到"控件行为不符合预期"，顺序应当是：

1. **源**：去上游源码里找这个控件的实现，把"它为什么这么设计"读出来，**记下 `文件:行号`**。
   - WinUI 2 = 分支 `release/2.8`，路径前缀 `dev/<控件名>/`（**不是** `controls/dev/...`）；
   - **`dev/` 里搜不到的控件去 `main` 分支的 XAML 内核找**：`ToggleSwitch` 这种
     `Windows.UI.Xaml` 的 OS 控件不在 WinUI 2 库里，全树搜 `dev/` 只出样式 XAML，
     真身在 `main` 的 `dxaml/xcp/dxaml/lib/<控件>_Partial.cpp`
     （还要配 `winrtgeneratedclasses/<控件>.g.cpp` 追调用链、`*_Partial.h` 看字段清单）。
     WinUI 2 / WinUI 3 共用这份内核实现，所以它对两边都成立。
   - **连 `main` 里也没有的 API 转 [API 文档](https://learn.microsoft.com/)**：
     `ElementSoundPlayer` 这类 OS 全局 API 没有任何 `.cpp` 可参考，事实来源是
     **文档的 Property Value / Remarks 原文**（把它引下来写进注释）。
     只取 ± 点到是不够的——**必须查默认值**，因为默认值常常和直觉相反：
     `ElementSoundPlayer.State` 默认 `Auto` 在 **desktop 上等于不响**（只有 Xbox 响），
     于是"开着却在桌面没声"看起来就像没接后端；而 `Volume` 默认 `1.0`（不是 0），
     这条替我们排除了另一个假诊断。同一次核定还要查**有没有连带副作用**
     （启用它会**自动启用空间音频**，得显式 `SpatialAudioMode = Off` 才回到普通音效）。
   - CommunityToolkit = `main`，`components/<组件>/src/...`；
   - 笔记统一写在 `docs/winui2-source-notes.md`，改 handler 前先看那里。
   - **先拉整棵树再按关键字找路径**（`git/trees/<ref>?recursive=1`），逐个文件猜路径就是 404 循环。
2. **证**：按源码给出的判据设计观测点，一次性把四个环节摆出来
   （事件有没有落到控件 → 闸门有没有放行 → 回调有没有改 state → 值有没有回写控件）。
3. **改**：只改源码指明要改的地方，注释里写明依据出处。

**没有源码依据的规则不许进代码。** "看着像中间态就丢""加个 Loaded 兜底试试"这类补丁，
当时能压住症状，下一次同样的病再犯还是没证据——而且它可能是下一个 bug 的来源（律令 5）。

---

## 四·八、诊断页是默认仪器，别再临时插桩

`samples/Reactor.Gallery` 里有**受控控件诊断页**（`Pages/DiagnosticsPage.cs`），
屏幕上直接摆出：state 值 / 回调次数 / **控件实际值**（只读可视树探针）/ 框架日志。

- 探针 `Pages/LiveProbe.cs` 是**只读**的：一个属性都不写、一个事件都不挂；
- 快照在 `UseEffect` 里采（`RenderContext.EndRender` 跑，patch 已落盘），
  **不要在 `Render()` 里读可视树**——那会读到上一帧的控件；
- 框架日志走 `ReactorLog`（`Reactor.uwp/Hosting/ReactorLog.cs`）：有通道 + 级别 + 内存环形缓冲，
  页内 `ReactorLog.Tail(n)` 直接读，**不用再去翻 `LocalState` 那个符号链接目录**。

**所以：别再往业务代码里插 `[probe-xxx]` 临时打印了。** 埋点常驻、默认级别关掉即可；
"排查时插桩、排完必须记得删"这个模式本身就是缺陷（每次插桩都可能改到被测对象，
删掉之后同样的病再犯还是没证据）。要加观测点就加进 `ReactorLog` 的某个通道。

### 仪器在最关键时刻失明：失败场景不重渲染 ⇒ 读数停在"上一次"

读数靠 UI 呈现，而 UI 靠 `setState` 刷新。"点了没反应"那一次恰恰是 **state 没变、不重渲染**的，
于是屏幕上那排数字停在**上一次成功操作**的时刻——**在唯一需要取证的时候，仪器是哑的。**

两个补丁缺一不可：
1. **轮询**（500ms 一次 `setPulse`），让读数不依赖 state 变化。计时器必须在 `UseEffect`
   的 cleanup 里 `Stop()`，否则页面下树后它还活着，既把整棵树钉住又会往死掉的 `setState` 打。
   轮询的 effect 依赖里**不要**塞"会导致写日志的 effect"的输入，否则自己的心跳会把日志刷满。
2. **看增量，不看绝对值**：单调只增的计数，一次操作的区别只在"哪几项各涨了多少"。
   所以要有「记基线」按钮，并直接把"涨了 `matched` = 被判成回声吞掉 = 你找的东西"
   翻译成人话写在屏幕上（人不用记六个名的含义）。

> 闭包陷阱：定时器里最容易栽在这。`setPulse(pulse + 1)` 里的 `pulse` 是这一帧的值，
> 写进定时器后每次算出的都是同一个数 ⇒ 只会有第一次更新。用 `UseRef` 跨渲染递增。

### 仪器自己也要有测试（尤其是"格式哨兵"）

诊断屏是**仪器**：它安静地显示一行错字，人就照着错字去改代码，比没有读数更糟。
它最常见的死法是被依赖的格式悄悄变了（本仓库：`ReactorLog.Counters()` 的文本来自
`EchoStats.Snapshot()` / `ReadyStats.Snapshot()`）：解析器拆出空字典 →
界面上"永远零增量" → 被读成"事件压根没到框架"。

所以纯字符串那部分要抽出来（不碰 XAML），Link 进 `net10.0` 测试工程，至少有三条：

1. 用**真的** `EchoStats` 造一份读数（不照抄字面量），断言七个键名都能解析出来；
2. 反向对照：把分隔符换掉，哨兵必须报警；
3. 键名要在**源码**里各有出处（`EchoGuard.cs` / `ReadyGate.cs` 里搜 `名字=`），
   否则改一次格式、测试跟着一起改，等于没哨兵。

### 埋点也会互相淹没：高频通道必须能单独隔离

环形缓冲只有 512 条。**一次用户操作会连带出 2 帧**（state 一帧、效果里回写快照再一帧），
把"帧N …"记在 `Info` 级，屏幕上 16 条全是帧号，真正要看的 `Input`（Pass/Gate）早被挤没了
——看着"有日志"，其实一条证据都没有。已经这么翻过一次车。

规则：

- **每帧/每轮的记录一律 `Trace`**，不进 `Info`；"有没有重渲染"用**计数**（`ReactorLog.Frames`）回答，
  一个数字不占版面，把它并进关键行里（如 `帧18 state: …`）。
- **`Tail(n, channel)` 支持按通道过滤**，诊断页上有通道切换按钮。判"点了没反应"先看 `Input`。
- **每个受控控件都要埋 Pass/Gate**，别只埋一半——漏掉的那个（ToggleSwitch 就漏过）
  在日志里是**黑洞**：回调明明在跑，日志却显示"没有任何事件"。

### 让仪器自己下判定，别让人肉比对

"state 一行、控件实际值一行，你自己看对不对得上"——这个比对方式看走眼一次就是假结论，
然后照着假结论去改。**比对交给代码**（`DiagnosticsPage.Verdict`）：逐项给 `✓ / ✗ / ?`，
且只在**对不上**时进 Warn 日志（全对时刷 Warn 会把真异常淹掉）。
采不到样（控件在折叠区没展开）给 `?` 而不是 `✗`——那是"没采到"，不是"值错了"。

---

## 四·九、写回归用例：反向对照会悄悄掉牙

用户明确要求「不要再一次改一次查，让测试自己发现-修复」时，走这套
（本仓库 `tests/Reactor.Core.Tests/`）：受控控件的行为用**模型**复刻
（框架是 UWP TFM，`net10.0` 测试工程引不进来，只能把纯逻辑文件 `<Compile Link=...>` 编进来），
再随机序列（20000 条 × 24 步）断言**不变量**。每个修法都做成**开关**，
关掉后跑同一批序列必须失败——这叫**反向对照**。一条修不修都绿的用例等于没写。

掉牙的方式，全踩过（这张表还会继续长，别以为只有这几条）：

| 坑 | 症状 | 处置 |
|---|---|---|
| **新机制擦掉了旧 bug 的反向对照** | 加了"写入后撤销未消费登记"之后，<br>"无条件登记"那条对照从 2270 条掉到 **0 条** → 假绿 | 复现旧行为时把**所有**相关开关一起复位<br>（如 `SealEchoAfterWrite = !leakEcho`） |
| **终态一致性不是好探针** | 回调为空期间用户改了控件，没人转告 state，<br>两者分开是**正确**的，却报成 bug | 真正的探针是 **per-step 断言**（"这次换值点击<br>必须恰好回调一次"）；正确分歧用计数器记账后豁免 |
| **"事后强制渲染再断言"把真 bug 也抹平** | 与上一条同症状（反向对照掉到 0） | 收敛不变量在**正常 Drain 之后**判，<br>不要自制一次同步下发去救 |
| **模型分支缺源码出处** | 补 per-step 断言后批量报错，查下去是模型自己<br>补发了一次源码里没有的事件 | 模型的每个分支都要有源码出处；<br>没有出处的标注为假设并**两个面都跑** |
| **一次性事件做成了可重复调用** | `ApplyTemplate()` 每次调用都重建 → 等于免费给 bug 一次补救，<br>面包屑反向对照直接 **0/20000** | 模板套用、`Loaded`、初始化这类**每个对象只发生一次**的事，<br>模型里必须做成"第二次调用是空操作"，否则 bug 的后果被抹平 |
| **把异步机制简化成同步** | 某道防御当时**零覆盖**：删掉那行一行测试都不红。<br>根因是仿真让它跟别的事共用同一个队列，<br>于是"快照会陈旧"这个前提在模型里根本不可能成立 | `RunAsync` / 派发器回调要建成<b>独立队列 + 快照</b>，<br>并给它的每道防御单独配反向对照。<br>检验方法很直接：**删掉那行防御，测试必须变红** |
| **批处理帧的粒度写死了** | 每步都 flush = 假设每次操作后必定紧跟一次渲染，<br>一整片"两次操作落在同一帧"的交错序永远测不到 | 仿真的 `Drain()` 要像 Toggle 那样**随机**触发；<br>跨帧机制的排序问题只在随机里露出来 |
| **把「声明」当成了「现状」** | `HasCallback`（element 上写了回调）判期望，<br>但实际要不要回调取决于**已经 patch 到控件上的那份订阅**：<br>`Rebind` 排在写入之后，换回调要下一帧才生效 → 批量假失败 | 模型要能区分这两个量（如 `Observing = 订阅已挂 && 回调非空`），<br>并用"是否有人接"而不是"是否声明了"来记期望 |
| **回调之后没让 state 跟进** | 用户改 → 回调 → app 侧 `setState` 这条链在模型里断了，<br>收敛不变量一片红，看着像框架 bug | 模型里记录回调的同时把 `state` 同步成控件值。<br>这类"批量假失败"最危险：人会想把断言放松，防线就废了 |

模型还有一条硬纪律：**模型不能自己编行为。**
点当前项的 `Checked/Unchecked` 两条路径，WinUI 里点已勾选项 `IsChecked` 只是
`true→false`、**只发 `Unchecked`**；模型若图省事也补发一次 `Checked`，
"点当前项"就会凭空回调出值——那是模型的行为，不是控件的，会把人往错的方向带。

---

### 修完之后做一次 generalization scan（同一份接线，不止一处）

一个 bug 落在某个控件上，很少是因为**这个控件**，多数是因为**这份接线**。
修完先全仓扫一遍同样的形状，再决定"修完了没有"：

```bash
grep -rn "\.Expect(" Reactor.uwp/Internal/      # 例：回声登记 → 9 处里的 6 处还漏着
```

本仓库的实证：`ToggleSwitch` 的 bug C 修完后扫出**另外 9 处**一模一样的站点
（`TextBox.Text` / `CheckBox` / `Slider` / `RadioButton.IsChecked` /
`NavigationView.SelectedItem` / `PasswordBox` / `AutoSuggestBox` / `NumberBox`），
而且这一步的证据**不用查外部源码**——`Reconciler.cs:1013-1092` 写着
`Rebind(control, null)` 会先退订再 `return`，于是"回调为空 ⟹ 这一发没人领"是仓库内自证的。

**只补那几个站点还不够**：下一次新增受控属性照抄同一份接线，病会原样复发。
所以顺手加一条**源码级契约自检**（本仓库 `EchoContractTests.cs`）：
每条 `Expect(` 之后必须有同一个守卫的 `CancelIfUnconsumed(`，否则列出文件名行号直接变红。

> 扫描器自身也要反向对照：喂一段明知的违约样本，它必须抓出来。
> 一条永远不会红的防线等于没有防线——包括"扫描器自己写错了"这一种寂静失效。

---

### generalization scan 的第二层：装状态的那张表归谁

同一份形状不只在"写入"处，还在**存状态的那张静态表**上。
本仓库实证：`EchoGuard` 是 `static readonly`，键是<b>真实控件</b>，原先存在
`Dictionary<object, ...>` 里——于是"控件能不能回收"押在**另一个模块**记不记得调
`Unmount → Forget` 上，而 `Unmount` 会不会被调到，`EchoGuard` **自己保证不了**
（`Reconciler` 的任何一条漏路都会让整棵控件子树连同 `DataContext`、命令、宿主页面被钉死）。
九个站点补完之后才发现这一层也是同一类病：**把"不泄漏"押在调用方纪律上的设计，本身就是缺陷。**

> **凡是"以控件（或任何别人控制的宿主对象）为键的静态表，一律用弱键。**
> 本仓库的 `WeakTable` 是 `ConditionalWeakTable` 的封装（套了 `StrongBox`，值类型/null 也能存）。
> 判据回到对象自己：对象不可达 → 条目自动消失，手动清理降级成"提前释放"。
> `EchoGuard`、handler 的事件回调表、面包屑数据源载体都是这一条的具体实例。
> 只有"有意强持有"的缓存（`DataTemplate` 缓存、`StyleSheet`）才留 `Dictionary`。

配套：除了"写入面"的契约，还得有一条"**卸载面**"的契约——每个 handler 用过的守卫，
必须在它自己的 `Unmount` 里被 `Forget`、在同类里有人 `Consume`，
压根没写 `Unmount` 的也要报警（照抄新受控属性时最常见的漏法就是连 `Unmount` 一起跳过）。

---

### GC 也能当断言（两个坑会让它假绿）

| 坑 | 症状 | 处置 |
|---|---|---|
| **`ConditionalWeakTable` 容器不收缩** | 弱键版第一轮之后也留一块常驻（本仓库 2 万个键 ≈ 0.65 MB），<br>只看"一轮后有多大"会把它误判成泄漏 | 泄漏的定义是**每轮都涨**：先跑一轮预热把容器撑开，<br>再测第二轮的净增长（弱键 ≈ 0，强键线性，<br>本仓库实测 2.3 MB / 轮） |
| **被内联进了调用方栈帧** | "什么时候不可达"变随机，断言时绿时红 | 造对象的辅助方法加 `[MethodImpl(NoInlining)]`；<br>强推至少 3 轮 `GC.Collect(2, Forced, true, true)` + `WaitForPendingFinalizers` |

反向对照同样是必需的：**把修复前的实现留一份副本**（本仓库
`EchoLifetimeTests.StrongEchoGuard`）喂给同一套断言，它必须被判泄漏。
它要是绿了，说明这套断言已经测不出东西。

---

### 源码级契约要照着真实源码做变异

合成样本只能证明"扫描器认识这种形状"，证明不了它对**现在这份源码**有效——
某段代码可能因为缩进、嵌套、写法的缘故压根没进它的视野，照样假绿。
做法是：读真文件 → **在内存里**删掉一行（`Forget` / `Consume` 各挑一处）→ 喂回扫描器，
要求它**指名道姓**报警（输出里带出守卫名 + 原因），一处不许溜过。
本仓库 `EchoContractTests.MutationsOfRealSources` 对 22 处真行做这件事，22/22 全红。

**只在内存里改，别落盘**——落盘跑（脚本改文件再跑测试）在并发 CI 上会互相踩，
而且中途失败会留下改坏的源码。真要做一次性验证，
务必 `try/finally` 还原并用 sha256 校验还原结果。

### 变异测到第 3 条不动：那是阴性结果，不是测试没写好

给 `ReadyPolicy.Decide` 做变异时，三个里两个红，**第三个纹丝不动**（调换两个分支，
246 项仍全绿）。反应不应该是"补个用例把它弄红"，而是先问：**它为什么绿？**
答案常常是"那一对条件不可能同时为真"——我把前提消掉了，所以顺序无关紧要。

处理办法是**改注释，不是编理由**：原先那段"必须先判 A，否则就会 B"的因果说明
当场作废，改成照实写"没有不变式守护这个顺序，留着它只因为更好读"。

> 由此得到一条硬要求：**注释里每一句"否则就会……"都得指得出某次实测**
> （变红或不变红都算）。写不出证据的那部分是作者当时的脑补，留着就会误导下一个人。

### 「拿 X 来决定要不要 X」= 自证循环

`ReadyGate.Arm` 曾用 `IsReady(control)` 判断要不要订阅 `Loaded`，
而那份标记偏偏就是 Loaded 回调才置上的——拿**结果**决定**要不要订阅**。

**症状是无声故障：**只在"控件其实已经进树、而副本还是 false"这一个输入下断，
断掉之后控件永久停在"未就绪"，期间的选中事件被那一道判据一发不留地吞掉，
用户看到的就是"点了没反应"，不报错、不打日志。靠肉眼看界面定位几乎不可能。

查找办法：**凡出现"我们先记一份状态，再拿它决定要不要继续"的代码，先去找
有没有权威信号可直接问。** 权威信号常常就在同一个仓库里另一个人已经用上了
（`FrameworkElement.IsLoaded` 就被 `InputApplier.ApplyFocus` 用过）——
两处各答一份，就会各答一半。

### 修复要标清「今天可达吗」，别让人高估

上述那条当前**不可达**（`Reconciler.BuildChildren` 是先 `Build` 后 `Children.Add`，
`Arm` 一律先于入树）。写 relea note 时照样把它单列，但必须写明"今天不可达、
修的是把正确性从押在调用顺序上换成押在控件自己的状态上"，
并配一个**可证伪的读数**（`ReadyStats.AlreadyLoaded`）：
真机上它非 0，就证明那条路径确实被走到了。

> 宁可写"当前不可达，证据在此"，也不要让人以为今天正在复现这个 bug。

---

### 一句「提前返回」会跳过它后面所有东西

`Dispatch` 门口的 `if (callback is null) return;` 本意只是"没人接就别回调"，
但它站在门口 ⇒ 后面的四道判据 **和** 纠正一起跳过。同一个前提（回调为空）
因此有两个受害者，上一轮只看见第一个（`Consume` 不被调用 → 登记泄漏）。

**排查时把每个提前返回后面跟着的东西列一遍**，别默认它只管自己那一行。
尤其警惕这类在语义上无关的东西被顺手带上：
"有没有人监听"（调用方的选择）不该参与"属性受不受控"（控件自己的承诺）。

配套的一条约定：把这种判据抽成纯函数时，**签名里不要带那个无关参数**。
写成 `(verdict, hasCallback)` 就是在邀请下一个读代码的人把两件事重新捆起来。
带上 ⇔ 同一个病在两个页面上有两个答案 ⇔ bug 报告里最难描述的那种"换个写法就好了"。

---

### 判据允许什么，就是它看不见什么

模糊测试的最终一致性判据，别写成"出过某类事件就整条序列免检"这种一刀切。
原写法 `UnreportedUserActions > 0 || State == ControlIndex` 一旦触发，
后续**所有**偏差都被赦免；换成按"最后一次用户点击"精确豁免之后，
同一批改法的反向对照全线提高（有跳法的：178 → 1039，21 → 3961）。

但精确判据必须**成对**，单留一条会失明——实测：

| 单留 `Explained`（控件值 ∈ {受控目标, 末次点击}） | 陈旧回写把用户此后的选择盖回去，恰好落在它<b>允许</b>的头一种 | 反向对照 4384 → **0** |
|---|---|---|

拿到"反向对照数字很小"时，**先怀疑判据而不是补兵器**：
多加几万条随机序列通常只能让 offending 变大一点，而换判据会带来数量级的变化。

---

### 仿真够不到的代码，唯一的防线是把它当文本读

仿真的纯判据能 Link 进测试工程，是因为它们不依赖 UWP 类型；
**handler 本体恰恰依赖**，所以那边的退化仿真一概看不见——做变异时当场撞上：
三个变异里前两个变红，改 handler 的第三个只能原地跳过。

结论：**仿真全绿 ≠ 那份代码没事**。对 Link 不进来的文件补源码级契约
（扫它的写法而不是跑它的行为），并且同样要用变异证明契约有牙。

---

## 五、行动前自查

遇到「值不对 / 图不出来 / 控件显示异常」，在开口给出结论之前：

- [ ] 我有没有**运行时实际取值**？（没有 → 先加诊断，别猜）
- [ ] 我说"这是 XX 版本的行为"，证据来自**装的那份**还是 GitHub main？
- [ ] 我准备说"改不了"了 —— 有没有确认过是不是**只有某一条链坏了**？
- [ ] 上一轮我加的修复，有没有可能是本轮症状的来源？
- [ ] 静默失败的场景（URI/资源），我有没有先排除"根本没加载"？
- [ ] 是不是只 build 没部署？（AppX 目录可能是旧的）
- [ ] 我"试了一下没报错"，试的是**整条链**还是其中一个环节？（手动跑通的子进程 ≠ publish 里那条链也通）
- [ ] 我引用的那些数字，**量具本身可信吗**？（实例指纹是不是 `GetHashCode`？集合是不是用非泛型 `IEnumerable` 判的？探针有没有在改被测对象？埋点有没有真的编进这个 app？）
- [ ] 我要下的这条规则，**能从上游源码里翻到出处吗**？（翻不到 → 那是在猜，先去查 `docs/winui2-source-notes.md`）
- [ ] 我用的是不是**已发布包**？框架侧的改动有没有真的编进这个 app？（示例 `Reactor.Template` 走 `PackageReference`，`Reactor.Gallery` 走 `ProjectReference`，别搞混）

---

## 六、配套

- 具体到本地化的键名规则 / `x:Uid` / resw → `.workbuddy/skills/uwp-winui-localization/SKILL.md`
- 包内资源 URI 的实现 → `Reactor.uwp/Internal/PackUri.cs`

---

## 四·十、源码拿不到落盘副本时：窗口取回 + 重叠区互证行号

`curl` / `gh` 可能**整个出网失败**（本机 `github.com`、`api.github.com`、
`raw.githubusercontent.com` 一律 `000`，`gh` 的 token 也过期），此时只剩服务端取回这一条路：

1. **不要猜行号，要"窗口化"取回**：让取回工具输出 `第 N 行到第 M 行，逐行带行号`，
   一次只要 100 行左右（要多了它会退化成整文件摘要，行号就没了）。
2. **行号必须互证**：取两个**有重叠**的窗口（如 1-140 与 130-200），
   重叠区逐字一致才敢把这一段的行号写进注释；**没互证过的行号一律按函数名引用**。
3. **把这个"可信度"写进笔记**：明确记下"哪一段验过、哪一段没验"，
   免得后来人把"没写行号"当成"没查过源码"。

（`Selector_Partial.cpp` 那次就是这么做的：只有 `144-148` 验过，
`EndChange` / `NotifyOfSourceChanged` 都按函数名引用。）

---

## 四·十一、契约扫描器会"对某个形状是瞎的"——三个检查动作

源码级契约（正则扫 `Handlers.*.cs`）比仿真更贴近真代码，但它自己的失效是<b>寂静</b>的。
每次写/改这类扫描器，至少做这三件事：

1. **反向对照之外，再拿<b>真源码</b>逐行变异**：合成样本只证明"它认得这种形状"，
   证明不了"现在这份源码在它视野内"。
2. **逐个类变异，而不是逐个 token 变异一次就完**：同一个类里
   `Mount` / `Update` 各登记一次的类（如 `NavigationView`），
   只抹一处还剩一处，判据本来就该判它合格——那是规则对，不是扫描器瞎。
   要抹就把<b>这个类里该 token 的行全部</b>抹掉。
3. **检查类块识别正则认不认 `abstract` / 泛型基类**：
   `^(?:\w+\s+)?(?:sealed\s+)?class\s+(\w+)` 认不出
   `internal abstract class ItemsViewHandler<TElement, TControl>`——
   于是这个类的行被算进<b>前一个</b>类，`ListView` / `GridView`
   **整整两个版本没进过任何一条契约的视野**，合成样本对此一声不吭。

顺带一条通病：**"每一条 X 都要有 Y"这种契约，天然看不见"压根没有 X"**。
`if (expected.Count == 0) continue;`（没有登记就跳过）让"照抄了一份受控属性
却压根没接回声抑制"这种漏法完美隐身。补契约时要反过来再问一次：
"写了受控属性 + 有回执通道的类，三件套齐不齐？"

---

## 四·十二、发前验包：先清 `~/.nuget` 缓存

`dotnet pack` 重新打出同名版本后，让消费方（`samples/Reactor.Template`）
临时升到该版本编译——**这一步可能验的是旧包**：

- NuGet 的版本号没变 ⇒ 直接命中 `%USERPROFILE%\.nuget\packages\<id>\<版本>` 的缓存，
  **不会**去源里取新包；
- 修法：把新 `.nupkg` 放进本地源（`dotnet nuget list source` 看路径），
  **删掉缓存里那个版本目录**，再重新 restore/build；
- 验完别忘了两件事：把消费方 csproj 还原回线上版本号，以及
  确认解析出来的 dll 与本地构建 `sha256` 一致（`sha256sum` 比对前 8 位就够）。


---

## 四·十三、手写清单本身要有一道闸

凡是"扫描器 + 手写清单"的组合（本仓库的 `EchoProne` 就是），清单漏一项 =
那个项目上的所有 bug 从此隐身，而且**没有任何一条测试会红**。所以清单必须自己也是被测对象：

- 断言：**源码里每一个"命中类别"的写回点，必须在清单里，或者被显式登记为"有意不管"（附理由）**；
- 两种归宿分开写：真该管 → 补登记 + 配套；有意不管 → 登记理由。
  "不管"也必须是**决定**，不能是"没注意到"；
- 自查两半：往真实源码里**塞一个清单外的**（必须报警），以及
  **把"有意不管"那份登记整份拿掉**（对应项必须立刻变成没人认领）——
  后者证明登记是承重的，不是写上去好看的。

顺带一条：把"扫出所有 X"这件事做完后，**要回头审一遍扫描本身的形状**。
只认 `control.X = ` 这种写法的正则会漏掉别的接收者名；
只扫 `Handlers.*.cs` 会漏掉 `Reconciler` / `InputApplier`。
先不限接收者名地扫一遍，把"我这份枚举是不是完整的"问清楚，再下结论。

---

## 四·十四、控件"声明了"的事件可能是死的

**别凭"这个控件应该有 XxxChanged 事件"就动手接。** 先查它到底有没有人抛。

本地参考源码（`tools/ctk-ref`、`tools/winui2-ref`）能查的先查：
`SettingsExpander` 在 `SettingsExpander.Events.cs:12`、`:17` 声明了
`Expanded` / `Collapsed`，但**全树没有任何一处 `.Invoke`** —— 死事件，订阅了也收不到。
用户点表头走的是 XAML 里 `ToggleButton.IsChecked ↔ IsExpanded` 的 **TwoWay 绑定**，
落到 `OnIsExpandedChanged`（只抛 automation peer 事件），CLR 事件根本不参与。

这类属性只能是 `defaultValue`（非受控）语义：**不是"还没接"，是接不上**。
要在注释里把这一点写死，免得后来人反复"补接线"。

同时注意另一半：**本地参考树里没有某个控件时，不许写"官方是怎么做的"**。
`tools/winui2-ref/dev/` 只有四个目录，没有 `Expander`，所以"官方用 counter-echo"
这种话在本仓库里没有依据——**注释说一套、代码做一套比没注释更危险**，
下一个读代码的人会以为那件事已经做了，于是真出问题时先排除掉正确的方向。

---

## 四·十五、用 heredoc 写 Python 正则：Git Bash 会把 `\s` 变成 `/s`

在 Git Bash 里用 `python - << 'PYEOF'` 传脚本时，`\s` / `\d` / `\w` 这类反斜杠转义
**会被转成 `/s` / `/d` / `/w`**（打印出来才看得见）。后果是正则**静默不匹配**——
脚本不报错，只是永远 `None`，于是你会去查包、查文件、查编码，唯独想不到是模式被改了。

本机实证（2026-10-06）：`re.search(r'CONTROLLED-SITES:/s*(/d+)', text)` 返回 `None`，
而同一段文本里那行明明是 `<!-- CONTROLLED-SITES: 12 -->`。
把模式打印出来才看见它已经变成 `CONTROLLED-SITES:/s*(/d+)`。

两条自保：

1. **模式里不用反斜杠**：`'CONTROLLED-SITES:' + '[ ]*' + '([0-9]+)'`，或
   `re.escape` + 显式字符类；
2. 或者**先把模式打印出来**再看匹配结果——这一步能省掉半小时反向排查。

同一类坑的通用教训：**校验脚本自己也要先证明它能匹配到一个已知存在的东西**，
再去断言"某处缺失"。否则"缺失"可能只是脚本瞎了。

---

## 四·十六、AOT 发布：`reg.exe` 仍在黑名单（2026-10-06 复核）

`dotnet publish -p:PublishAot=true` 在本机**依旧打不通**，复核结论与上次一致：

```
cmd //c "...\microsoft.dotnet.ilcompiler\10.0.12\build\findvcvarsall.bat" x64
→ 只吐出 VC 那半截 LIB（...\MSVC\14.51\lib\x64），没有 Windows Kits\10\lib\<ver>\um\x64
→ 沙箱：PROGRAM BLOCKED BY SECURITY POLICY - reg.exe
```

SDK 那半截 LIB 是查注册表拿的，`reg.exe` 被拦 → 那半截丢 → `LNK1181: advapi32.lib`。
**这是环境，不是项目配置**，别去改 csproj。要打通得由用户在
Security Center → Command Security → Program Blacklist 里放行 `reg.exe`。
不要试图绕开（换 shell、等价命令都属绕过）。

---

## 四·十七、"按控件建的表"要单独一道契约，且方法体识别要认三种形状

### 弱键只是兜底，不是许可

把所有 `static Dictionary<控件, …>` 换成 `WeakTable`（弱键）之后，
"忘了在 `Unmount` 里摘"从**永久泄漏**降级成"条目要等控件被回收才消失"。
于是很容易得出"不用管了"的结论——**这是错的**，因为：

- 弱键只覆盖**你自己那张表**；`Reconciler` 里的
  `Dictionary<Button, RoutedEventHandler>` 之类仍是**强键**，
  那边漏摘就是真泄漏；
- handler 这边根本看不见那张表，只能靠 `reconciler.RebindXxx(control, null)`
  这个**成对动作**来判定。

所以判据要按"登记 / 摘除"**配对**来写，而不是"表是不是弱的"：

> 类里出现 `<表>.Set(` / `<表>[x] =` / `reconciler.RebindXxx(x, 非 null)`，
> 该类的 `Unmount` 里就必须出现同一张表的 `Remove`，或同一个 `RebindXxx(x, null)`。

两个必须处理的细节：

- **排除标量键**：`Dictionary<string, DataTemplate>` 这种模板缓存不是"按控件建的表"，
  不排除它，真正的违约会被淹没在一堆误报里；
- **摘除可能藏在本地助手里**：`PasswordBox` / `AutoSuggestBox` / `NumberBox` 的
  `Unmount` 只写 `Rebind(control, null)`，真正的 `Handlers.Remove` 在 `Rebind` 体内。
  扫到 `Unmount` 里有 `Rebind(x, null)` 时，要把那个 `Rebind` 的方法体也算作释放证据。

### 方法体识别要认三种形状（血泪）

按大括号配平找方法体，遇到**表达式体**就翻车：

1. 块体 → 配平；
2. 单行表达式体 `=> Callbacks.Remove(control);` → 签名行以 `;` 结尾；
3. **跨行表达式体**（签名一行、`=> …;` 在下一行）→ 必须一直吃到分号那一行。

第 3 种最容易漏：只取签名行会把方法体判成空，
于是"这里什么都没释放"被误报成"压根没写过 `Unmount`"。
`ButtonHandler.Unmount` 就是这个写法，而第二道契约恰好因为
"Button 没有回声登记"而一直没红——**洞潜伏了整整两个版本**。

自保：写完这类扫描器，**把每个类的 `Unmount` 起止行打印出来**扫一眼，
确认没有哪个是空的。

### 变异要"逐条"，不要"每类一次"

删掉释放语句做承重自查时，**每个类里每删一行都要单独跑一次判定**。
只删一行的坏处：`ComboBox` 的 `Unmount` 里有三条 `Remove`，
删掉 `Callbacks.Remove` 还剩两条，判据本来就该判它合格——
那是规则对，不是扫描器瞎。逐条删才能得到"23 处全部被抓"这种可复核的数字。

---

## 四·十八、扫不到的那一类登记：服务类的 Arm / Disarm，以及"口径"要量出来

第六道契约写完之后，顺手把扫描范围从 `Handlers.*.cs` 扩到整个 `Internal/*.cs`
做了一次对照——**这是必须做的一步**，因为"为什么只扫这些文件"通常从来没被问过。

结果只多出两个类，恰好代表两种"不是表、但同样是按控件登记"的形状：

| 类 | 形状 | 处置 |
|---|---|---|
| `ReadyGate` | 表在**服务类自己那里**，handler 只写 `Arm` / `Disarm` | 按**动作配对**判：`X.Arm(` ⇒ `Unmount` 里必须有 `X.Disarm(` |
| `InputApplier` | 没有 `Unmount`，靠每次 `Apply` 自清 + 弱键 | 不归这条管（无 `Unmount` 就无从配对） |

**服务类登记这一类此前完全没人守**：`ComboBox` / `RadioButtons` 都写了 `Disarm`，
但那只是写对了——删掉那行没有任何测试会红。这正是本仓库反复出现的
"照抄一份接线却漏了配套"，只是漏的对象从"表"变成了"服务"。

两条方法上的要求：

1. **凡是"按控件登记的东西"，登记表在哪不重要，重要的是有没有成对的摘除动作。**
   看不见表就按动作名配对（`Arm`/`Disarm`、`Rebind(x, 非 null)`/`Rebind(x, null)`）；
2. **扫描范围要反过来量一次。** 扩到全目录跑一遍，把"多出来的类"逐个归类
   （该管的 / 不该管的 / 形状不同的），并把结论写进注释。
   不做这一步，"只扫 Handlers.*.cs"就只是一句没人验证过的假设。

---

## 四·十九、"顺序"本身是 bug 的一个类别，而且要能被反事实证明

有一类 bug 不来自判据写错，而来自**动作先后**：写属性 → 订阅 → 又写属性。
`Localization.ApplyUid`（给 `x:Uid` 套 resw 的值）就排在 `handler.Mount` 订阅之后，
于是那一笔写抛的事件没有任何回声登记，被当成用户输入回调出去。

三条做法：

1. **把次序从源码里逐行读出来，写进文档的表里**（`Build` 三步：BeginMount →
   handler.Mount → ApplyModifiers）。不要写"应该是在……之后"，写行号。
2. **修法用两条互补的判据，并把边界写死**：`PropWriter.IsMounting`（同步抛时命中）
   `|| !IsLoaded`（延后到挂树之前时兜住）。两者都假时拦不住 —— 这个窗口
   **要写成一条用例**，而不是藏起来。
3. **反事实实验证明病因**：把那笔写挪到订阅之前（模型里的开关），
   不修也不该有假回调。这一步区分"病因是顺序"和"病因是这个属性不该写"，
   两种修法完全不同。

---

## 四·二十、登记/豁免清单必须指向"真实存在的中和手段"

凡是"把某处登记为不管/已中和"的清单（本仓库的 `UncontrolledByDesign`、
第七道的 `OutsideWriteLedger`），都要带一个**锚点**：方法名 + 必须在方法体里
匹配到的正则。契约去真实源码里找，找不到就报警。否则"登记了但没修"是最容易
蒙混过关的一种失效。

变异必须做**两个方向**：

- 删掉那段中和代码 → **必须红**（证明登记是承重的）；
- 只删掉它旁边那行**日志** → **不该红**（证明锚点盯的是机制，不是日志）。

只做第一个方向的话，"把锚点配到一行无关代码上"这种失聪照样能过。

顺带：**"接收者必须被声明成控件类型"** 这一层不能省。扫"用户可改属性的写回点"时，
`hook.Value = x`（`Core/RenderContext.cs`）和 `box.Value = x`（`WeakTable` 内部盒子）
都会被算进来，不挡掉就淹没真违约。做法：接收者变量名在同一文件里被
`case <控件类型> x:` / 参数 / 局部变量声明过才算数。


## 四·二十一、受控写入有两半：「事件没来」和「事件来了但值不对」

`EchoGuard` 的三件套（`Expect` → `Consume` → `CancelIfUnconsumed`）只覆盖了前一半：

| 半 | 现象 | 谁管 |
|---|---|---|
| 事件**没来** | 登记没人领 → 留成陈旧期望，之后吞掉真实操作 | `CancelIfUnconsumed` |
| 事件**来了但值不对** | 控件把写进去的值夹/改成别的值 → `mismatch`，那一发照样回调出去 | **此前没人管** |

第二半的典型触发：写 `Minimum` / `Maximum` 会把受控 `Value` 夹进新区间，
回读出来的是**夹取后**的值（WinUI 2 `NumberBox.cpp` 的 `CoerceValue` 就是这么写的），
登记的是声明值 ⇒ 永远对不上。

判据：**登记/下发都用"控件会回读出来的那个值"**（本仓库 `RangePolicy.Coerce`）。
它不改变控件终态（写声明值会被控件夹成同一个值），改的只是回声变得可预测 ——
这点要写进注释，否则会被读成"改了下发的语义"。

顺带：注释里早就写着"写下去可能被夹成别的值，登记永远等不到匹配"，
说明现象当时已经看见了，只补了一半。**看到注释里已有预警时，去查它的另一半。**

## 四·二十二、事先不知道会变成什么值 → 用"窗"而不是"猜值"

`Expect` 要求"我知道回读出来是什么"。做不到时（夹取值事先未知、且可能**不止一发**：
WinUI 2 里 `OnMinimumPropertyChanged` 与 `OnMaximumPropertyChanged` **各**调一次
`CoerceValue`）别去猜，开一段**静默窗**（本仓库 `EchoGuard.Silence`）：
窗内 `Consume` 一律判为回声。

合法性来自"这段窗在渲染路径内部，里面不可能有真实用户输入"，
而不是来自对值的预测。**代价不对称**：窗在"永远没等到事件"时成本为零，
漏罩则是一发假回调 —— 所以证据不够强（只有文档、没有源码）时也按"宁可罩上"处理，
但要在笔记里**按强度分开记**，别混着说。

互补的两条已知边界要照实写：窗罩不住"延后到窗关之后才到"的异步事件。

## 四·二十三、源码扫描器：方法头正则必须要求修饰符

写"往上回溯到方法头为止"这类扫描时，正则别写成"修饰符可有可无"。
否则**行首的一个普通调用**（`ApplyRange(control, n);`）会被判成方法头，
回溯就地中断 —— "调用点没罩窗"这一档直接失聪（本轮真踩到，合成样本才把它逼出来）。

要求**至少一个修饰符关键字**（`private|internal|public|protected|...`）+ 返回类型 + 名字 + `(`。
另外"辅助方法"这一类要单独处理：违约行的方法头认不出时，改查**它的调用点**，
并且**`Update` 本身不算辅助方法**（它没有调用点可查，放行它等于放行一切）。

## 四·二十四、变异要有"位置"方向，不只是"删除"

判据若是"某行必须在窗里"，只删掉那行不够 —— 证明不了扫描器看的是**位置**。
加一个方向：**把窗挪到写入之后**（要挪到块尾那个 `}` 的**后面**，
挪 3 行可能还落在写入之前，那样"挪了"和"没挪"没区别，这一向的对照是假的）。
删窗 / 挪窗两个方向都必须红。

## 四·二十五、两个开关要分开计量，并断言它们不相等

一处修复若由两个机制共同兜住（本轮：静默窗 + 登记夹取后的值），
反向对照不能只断言"关掉修法必须红" —— 那分不清哪个开关坏了。
两个开关**各关一次**、各计一次数，并额外断言两个数**不相等**
（相等往往意味着同一个开关在兜两处，另一个是摆设）。

外加**无关对照**：把"控件会夹取"这个前提也关掉，假回调必须消失 ——
否则说明病因认错了（那些假回调另有出处）。

## 四·二十六、发前验包：本机有**两层**缓存（2026-10-06 踩到）

`~/.nuget/packages/reactor.uwp/<版本>` 之外，还有一个**本地源目录**
（本例 `D://fluentapps//local-nuget`，`dotnet nuget list source` 里叫 `LocalFluentApps`）。
**只清前者不够**：restore 会从后者拿回旧的，消费方照样 0 错误 —— 验的是旧包。

正确顺序：`dotnet pack` → 把新包**复制进本地源** → 删 `~/.nuget` 里那个版本目录
→ 删消费方的 `obj/`（否则 assets.json 不重新解包）→ 两个平台各编一次 → 还原版本。

判定办法（别只看"0 错误"）：去
`~/.nuget/packages/reactor.uwp/<版本>/lib/*/Reactor.uwp.dll` 上取 `sha256`，
与包里那份比对。**对不上就说明你在验旧包。**

## 四·二十七、`WebFetch` 取 GitHub 源码：先列目录，别猜路径

`raw.githubusercontent.com` 猜路径连着 404 三次（`main/dev/...` 是 WinUI 2 时代的布局，
WinUI 3 的 `main` 已经把 `dev/` 去掉了），而**404 和"网络被拦"长得一模一样**，
很容易误判成"又出不了网"。先列目录再取文件：

- 本机之前 `curl` 成功时留下的全树清单（`/tmp/tree.json`）直接查路径最快；
- 或者取目录接口。

确认路径存在再取单个文件。另外 `.g.cpp` 这类**生成**代码仓库里没有，
取不到是正常的 —— 记笔记时写明"这一段没有源码"，别拿文档级依据冒充源码级。

## 四·二十八、按「名字白名单」认写入点 = 漏掉名单之外的全部

第八道契约只认 `Minimum` / `Maximum` 两个名字，于是 `SelectionMode`、`MaxLength`、
`GroupName` 这些**同样会牵动受控值**的写入一条都进不了视野 —— 漏掉的是名单外的全部。

正确方向是**反转判据**：默认全管，豁免要登记（本仓库 `InertByDesign`），
登记必须写理由。以后新增一处属性写入，契约先红，逼出一句"它会不会牵动受控值"。

登记一条的门槛：**说得出它为什么改不动受控值**。说不出就不登记，开窗。
另外要加一条反向对照：**把整份登记当作不存在，真源码里必须冒出违约** ——
否则那份登记是摆设（写了从没人写过的属性名也不会被发现）。

## 四·二十九、扫描器：多窗方法会「串窗」

"从写入行往上回溯，先遇到窗就算合规"这个判据，在一个方法里有两道窗时会**串**：
前面那道把后面那道罩的写入一并认领。删掉后面那道窗，扫描器照样判"罩住了"
⇒ 变异测试**假绿**。

改成算窗的**块范围**（`using { … }` 之间，靠缩进匹配块尾 `}`），
写入行要真的落在里面才算罩住；没罩住就继续往前找更早的窗，别就地断言。

（这一条是"把契约泛化"逼出来的：只管 Min / Max 时恰好没有多窗方法，
泛化到"任何非受控属性"后立刻撞上。）

## 四·三十、变异要按各自视野分派

两道契约各管一段视野时，变异断言不能"两道都要求报警"：
新增的窗会让只管 Min / Max 的那一道"没报警"—— 但它本来就不该管它们（假红）。

按"这个窗罩的是什么"分派：罩 `Minimum` / `Maximum` → 第八道必须报警；
其余 → 第九道必须报警。（用窗块内有没有 `RangeWrite` 来分派。）

## 四·三十一、"丢状态"和"假回调"是两类，别共用一个计数

`items` 重建把选中清空之后，只按"声明值变了才写"的判据**永远补不回来**——
这不是假回调，是**选中态永久丢失**，界面上表现为"什么都没选中"。
它此前没有任何契约盯着（假回调有计数、有契约，丢状态没有）。

行为模型里要给它单独一个计数（`Lost`），并断言两个开关**分段正确**：
只关静默窗 → 丢选中仍是 0；只关补发 → 假回调仍是 0。
外加"两个数字不相等"—— 相等往往意味着同一个开关在兜两处，另一个是摆设。

## 四·三十二、取证前先问"这个属性在哪个类上"

找 `SelectionMode` 的处理逻辑时直奔基类 `Selector_Partial.cpp`，
拿回来的答复是"这个文件里没有 SelectionMode"——
它其实是 `ListViewBase` 引入的（`KnownPropertyIndex::ListViewBase_SelectionMode`）。

**在错误的类里找不到，和"这一条不存在"长得一模一样。**
先确认属性属于哪个类（谁在 idl / 文档里声明它），再去对应的 cpp。

## 四·三十三、Python heredoc 里的反斜杠会被 Git Bash 吃掉（再记一次）

`python - <<'PY'` 里写 `r'\s'` 到文件里会变成 `/s`（带引号的 heredoc 也躲不掉），
于是"包内 README 标记"这种校验会假报缺失。

判据类脚本要么避开反斜杠（用字符类 `[0-9]` 代替 `\d`），
要么干脆先 `find()` 定位再切片。这一轮又踩了一次。

## 四·三十四、按"形状"认写入点，漏掉的是别的形状的全部

四·二十八讲的是"按**名字**白名单认写入点"。同一条的另一半是"按**形状**认"：
判据写成 `X.Prop = v` 这种赋值形式，`Items.Clear()` / `MenuItems.Add(…)` /
`ItemsSource =` 这类**方法调用**形状一条都进不了视野。

危险在于：**换集合恰好是牵动受控选中值最典型的动作**，
所以被漏掉的往往正是最有嫌疑的那一整类，而契约还在给绿灯——
让人误以为那一类已经被管住。

查完一类判据后追问一句：**它是"真合规"还是"压根没被看见"？**
判据是"默认全管、豁免要登记"就能自动回答；是白名单就得手动问。

## 四·三十五、合成样本必须长得像真源码（包括"带不带接收者"）

调用点扫描的正则若写成 `(?<![\w.])Method\(`（不许前面是点），
真源码里 `reconciler.PatchItems(control, …)` 这种**带接收者**的调用
会被整条排除 ⇒ "调用点没罩住"那一档永远不报警，变异测试跟着假绿。

而合成样本里通常写成 `ApplyRange(control, n);`（不带点）——
**样本替你测的，只有它自己走过的那条路**，于是这个洞一直藏着。

规则：合成样本要和真源码用同一种调用写法；更好的是**样本里两种都放**。
另外合成样本的方法名别用真源码里已有的名字（按名字查调用点时会串到真那份上）。

## 四·三十六、跨文件的变异要替换进"所有文件"再数全局

删掉 A 文件里一道抑制块，真正冒出来的违约可能在 B 文件里
（例：删掉 `Handlers.Controls.cs` 的 `Rebuilding.Set(true)`，
冒出来的是 `Reconciler.cs` 里 `PatchItems` 那两行）。
只在被改的那个文件里数违约 ⇒ **假绿**。

改法：变异后把新内容**替换进"所有文件"列表**，再数**全局**总数。
替换时用引用相等（`ReferenceEquals`）定位那一项。

同时仍要按"这个块罩住了什么"分派（见四·三十）：
块里若只有别的契约管的写入，本道本来就不该红，要求它红是假红。

## 四·三十七、时间窗 vs 持续判据：异步后果要用后者

静默窗 / `Rebuilding` 标记 / `IsMounting` 这类**时间窗**，
只罩得住**同步**抛出的事件。凡是"后果由布局/下一帧驱动"的
（`Visibility = Collapsed` → repeater 在下一帧才回收元素 → 才抛 `SelectionChanged`），
窗早就关了，**罩不住**。

这类要用**持续判据**：判"控件当时处于什么状态"，
而不是"这一刻是不是在窗里"。`ReadyGate`（控件进过可视树吗）就是这种——
它对"延后到 Loaded 之后"照样成立。

评估一个"加一道更大的时间窗"的方案时，先问：
**它罩不罩得住异步后果？** 罩不住的话，加了会让人以为罩住了——比不加更糟。
（本轮因此否决了"渲染期闸门 `PropWriter.IsPatching`"，见源码笔记 §18.5。）

## 四·三十八、想加机制前先查"那一发是不是已经被别的判据罩住了"

排查 `Visibility` 折叠引发的假回调时，一路推到"要不要加渲染期闸门"，
回头查才看见：那一发走的是 `Select(-1)`，`AddedItems` 为空 ⇒
判据零（`SelectionArgs.SelectedSomething`）已经命中 ⇒ 早就被 `Suppress` 了。

**已经有机制罩住的，别再加一层**——重复记账会让以后没人说得清"到底是谁拦的"。
列一遍现有判据再决定要不要新增。

## 四·三十九、惰性登记按（类名, 属性名），别只按属性名

同一个属性名在不同类上语义可以完全相反：`Items` 在 `ComboBoxHandler` 上
是 `Selector.Items`（`Clear` 会冲掉选中，必须管），
在 `SettingsExpanderHandler` 上只是展开区卡片容器（不用管）。

只按属性名登记，等于**给最危险的那条发免死金牌**。

（已知的债：第九道那份 `InertByDesign` 只按属性名记，
因为它只扫 `Handlers.*.cs`、类名恰好不冲突。等它扩视野那天会咬人。）

## 四·四十、"包内 dll 与本地构建一致"只在打完那一刻成立（2026-10-06 真事故）

四·二十六记的是"两层缓存会让你验到旧包"。同一条的另一半是
**包自己会过期**：第 17 节打完包（11:12）之后框架源码又被改过（11:35），
到下一轮校验时包内 dll 是 `072bfd46…`、本地构建已经是 `bd3abe31…` ——
**包过期了 23 分钟，发前校验表上还写着"通过（072bfd46…）"**。

所以这条 sha 比对不是一次性的动作，是**发版前最后一步**。
每次发版前重跑一次；对不上就重打，重打后按四·二十六的流程重验消费方。

顺带：判断"是构建不确定还是包真的旧"的办法是**连编两次比 sha**
（.NET 默认确定性构建，两次一样 ⇒ 那就是包旧了）。
别一上来就怀疑构建不确定，那是冤枉它。

## 四·四十一、临时升版本验证后，还原要用"替换"而不是"拷回备份"

验证消费方时常见动作：`cp csproj /tmp/x.bak` → 升版本 → 编 → `cp /tmp/x.bak csproj` 还原。
这一轮换了个顺序就**没还原成功**（备份里已经是 alpha.6），
而且 `cp` 静默失败时 `&&` 链照样往下走，最后只看到"0 个错误"。

改成**直接替换目标串**（`b.replace(b'alpha.6', b'alpha.5')`）并**打印替换了几处**，
再把"当前是不是 alpha.5"作为断言输出。顺手做一次换行规范化
（python 写过会把 CRLF 转成 LF，`git status` 会把整个文件显示为已改）。

## 四·四十二、只守"动作"的契约，守不住"能力没接"

十条契约全守**写入**（写了受控值得有人领、写了别的东西得在抑制块里），
它们的共同前提是"这个 handler 已经接了判据"。
于是存在一种漏法：每一条写入都规规矩矩开了窗（全部绿灯），判据本身一件没接。

查法是按**能力面**逐件查，不是按动作查：

```
受控选中类（有 SelectionChanged 订阅 + 持有 EchoGuard）
  → 逐件查五件套：ShouldApply / ShouldExpectEcho / Decide / Restore / 持续标记
  → 缺一件：要么接上，要么按类名登记豁免并写明理由
```

`NavigationViewHandler` 就是这样被顶出来的（四件缺、一件用错手段）。
**登记门槛与惰性登记一样：说得出为什么不需要，且理由能追溯到控件源码。**

## 四·四十三、豁免登记必须带反向对照

"登记豁免"是很容易变成"给最危险的那条发免死金牌"的动作。
所以豁免那份名单自己要有闸：**把豁免整份拿掉，真源码必须立刻冒出违约**。
撤掉也不红，说明这些件本来就已接上——那份登记是多余的，该删。

与第九、十道惰性登记的反向对照是同一个形状，三处保持一致。

## 四·四十四、同一套概念在两道契约里两种口径 → 迟早咬人

已知三处（都已修）：

- **抑制块**（已咬）：第九道只认 `Silence` 窗，第十道认窗 + `Rebuilding` 标记。
  把 NavigationView 改用持续标记之后，第九道立刻报了两处**假**违约。
- **惰性登记颗粒度**：第九道按属性名记，第十道按（类名, 属性名）记。
  按名字记 = **一条登记、十二个类受益**。`Content` 在 `CheckBox` 上是标签、
  在 `NavigationView` 上是整棵子树，两种语义混进一条豁免。
- **扫描范围**：第九、十一道扫 `Handlers.*.cs`（7 个），第十道扫全树（66 个）。
  第十一道路拉平后命中数没变——但**"没扫到"和"扫了没问题"是两件事**。

**判别办法**：换了一种写法之后某道契约突然红了，先分清
"源码退步"还是"这道契约自己失聪"——看它的判定口子认不认新写法。
认就修契约，不认就改源码。

**拉平之前先算一遍成本**：第九道不用拉，因为它按"类里有没有 `EchoGuard`"准入，
而 `EchoGuard` 只存在于三个文件里，全树扫结果一样。
别为了整齐去扫一堆注定没有结果的文件。

## 四·四十五、取不到函数体时，找同类对照，并明说没取到

查导航类控件源码时 `OnMenuItemsSourceCollectionChanged` 的函数体取不到
（WebFetch 截断）。**不要猜它做了什么**，去找**同类对照**：
Footer 那条路的 `OnFooterItemsSourceCollectionChanged` 是完整的，
末尾 `UpdatePaneLayout()` = 布局失效 = 真正的工作推到下一帧；
主菜单那条路末尾同样是 `InvalidateTopNavPrimaryLayout()` / `UpdatePaneLayout()`。

对照足以判定"后果不同步"。**没取到的部分在结论里明确写"没取到"**，
别用"应该是同步的"糊过去——那是无依据断言。

## 四·四十六、控件自带等价守卫时，登记豁免而不是硬接

`NavigationViewSelectionChangedEventArgs` 没有 `AddedItems`，
共用的 `SelectedSomething` 接不上去。但源码 `RaiseSelectionChangedEvent` 在
`nextItem` 为 nullptr 时**不设** `SelectedItemContainer`，
于是 handler 自己的 `SelectedItemContainer is …` 守卫天然兑现了同一件事。

这时候**登记豁免（写明源码依据）比硬接一层更诚实**：
硬接要么得伪造一个 `hasRealItem` 实参（假信号），
要么得写一个用不上的适配层。判三条：控件自己做了吗？做了就登记。

## 四·四十七、源码注释里"X 之后发生"要读出两件事

`NavigationView.cpp` 那条早退的注释：

```cpp
// 2. Template has not been applied yet. SelectionModel's selectedIndex state will get
//    properly updated after the repeater finishes loading.
if (… || !m_appliedTemplate) { return; }
```

第一遍只读出了"未就绪期间一发都不抛 → 判据一不用接"，**写了一张假豁免**。
漏掉的是后半句：`selectedIndex` 会在 repeater 加载完之后被"正确更新"——
**那一发会回来**，只是晚一点，而且回来时选中的是实项（判据零拦不住）。

现在把它拆成两个必问的问题：
1. **会不会延后？**（会）
2. **延后到什么时候为止？**（到 repeater 的 `Loaded`；而 repeater 是模板子树的
   一部分，**子先于父**，所以那一刻控件自己的 `Loaded` 还没到 → `IsReady` 仍是 false）

第 2 问才是决定"该由哪道闸门接"的那一条。
**只读第 1 问，会把一条早退误当成"这一发不存在"。**

配套的反面：反过来也不能因为"会延后"就立刻换持续标记——
`finally` 关门同样在 `Update` 返回那一刻，**持续标记也罩不住下一帧**。

## 四·四十八、修完必须建行为模型，否则理由可能是错的

第 19 节改完源码契约全绿，理由写的是"时间窗罩不住异步后果"。
建了 `RebuildEchoSim` 之后发现：**理由错位，修法侥幸对了**。

模型把两档分开（`ReapplyConverges` 开关）：

| 补发之后控件的行为 | 只罩 Clear+Add（旧） | 罩到补发完（新） |
|---|---|---|
| 停在写入的那个值 | 不漏 | 不漏 |
| 自己收敛成别的值 | **漏** | 不漏 |

承重在**第二行**。而"延后到下一帧"那一档（`DefersToNextFrame`）
两种形状都不漏——那一发是取消选中，判据零本来就接得住。

**做法**：每改完一处，问"我凭什么说它是这个原因"，
然后用 Link 真判据的仿真把那个原因**单独关掉**看红不红。
关掉之后不红 = 那个原因不是真原因，理由得改写（修法可能仍是对的，但别写错理由）。

## 四·四十九、契约件在一个类里有多处落点 → 逐行变异覆盖不到

判据簇的 `ReadyGate.IsReady` 在 `NavigationView` 里就有三处
（`ApplySelectedItem` 一处 + 两个事件入口各一处）。
逐行变异的规矩是"只删**唯一**落点"（否则删一处剩下的还在，要求报警就是假红），
于是这种件**一处都删不动**，变异对它完全失明。

补一个**类级变异**：对每个（类 × 件）组合，把该类里该 Pattern 的
**全部**匹配行一次性删掉，违约数必须变多。本轮 21 组全部承重。

判据：`if (hits.Count == 0) continue;`（该类没接这一件，那是违约检查的事，
不是变异的事），删完要与 `before` 比**全局**违约数（跨文件同理）。

## 四·五十、"整份撤登记必须红"挡不住反向的腐烂

豁免/惰性登记表的反向对照，常见写法是**整份清空**再数违约，要求 > 0。
它只能证明"这份表里**至少有**一条真被用到"。挡不住的是反方向：
往表里塞**没人用**的条目（比如把某个其实落在抑制块里、根本不需要豁免
的写入也登记上），整份对照照样绿，而那条假豁免会在将来真有人写它的时候
免掉一次该报的警。

**补一条：逐条撤。** 对登记表里每一条，单独移除后重跑真源码，违约数必须增加：

```csharp
foreach (var entry in Registry)
{
    Keys.Remove(entry.Key);
    try { if (Violations().Count == 0) idle.Add(entry); }
    finally { Keys.Add(entry.Key); }
}
```

同时它与"缺一条就红"那一半合起来给出**恰好**的清单：
多一条 → 逐条检查红；少一条 → 违约检查红。登记表因此不可能漂。

## 四·五十一、合成样本不要往真登记表里塞假条目

要验"登记生效"，最省事的写法是往真登记表里加一条 `("DemoHandler", "Header")`。
别这么做——那条假条目随后会被"逐条撤登记"（四·五十）判成不承重而红，
或者更糟：被人当成真豁免读。

把豁免表做成**可注入的参数**（`inert ?? InertKeys`），合成样本自带自己那份。
顺带，颗粒度改细之后必须有一条专门的样本证明它**真的改细了**：
两个类写同一个属性名，只登记其中一个，另一个必须报警。
换了个类照样不报 = 还是按属性名在记。

## 四·五十二、同一个语义两套实现时，先看嵌套/健壮性是否一致

`EchoGuard.Silence` 是 `WeakTable<object, int>` **深度计数**（可嵌套），
`Rebuilding` 是 `WeakTable<TControl, bool>`（嵌套会提前关门）。
同一个语义（"这一段的作者是渲染路径"）两种健壮性。

**先问嵌套可不可达**，不要急着统一。四处 `Rebuilding` 的区间里都不会重进
同一控件的同一段，今天咬不到人——那就**登记成已知边界**而不是加计数器：
加机制是给一个不存在的问题买保险，还多一处状态要维护。
真出现嵌套时症状是"用户操作被吞"（内层提前关门），与"漏罩"方向相反，认得出。

## 四·五十三、跑测试的时候不要并发构建：测试会临时改真源码

第七道的变异对照 `Break()` 是**往真源码里写**的（删一行 → 重判 → `finally` 还原），
这是它"有牙"的代价。于是存在这样一个窗口：**那一行正被删掉的时候，
任何并发的框架构建都会把变异后的源码编进去**。

把"构建框架"和"跑测试"放进同一批并发调用，实测撞出三个互相不同的 dll 哈希，
排查了很久才定位到是这个窗口。发版动作上：

- **打包之前不要并发跑测试**；打包之后也不要；
- 看到"同一份源码两次构建哈希不同"，先怀疑这个，别急着怀疑构建不确定性。

## 四·五十四、临时脚本改真源码前先备份（自伤记录）

为了复现一个哈希，我用临时脚本直接改了 `Reactor.uwp/Internal/Reconciler.cs`，
随后一步写错把它**截断成 0 字节**。工作区那 4912 字节从未提交过，
git 里没有，只能重建。

**能做的与做不到的**：

- `git checkout` 不能用：文件相对 `HEAD` 是 `M`，会连当前迭代的改动一起丢；
- 文件已空时 `git diff HEAD` 会把 **HEAD 那一侧的全文**当删除行输出 ——
  取所有 `-` 行去掉前缀即可精确还原 HEAD 版本；
- 丢掉的未提交改动只能靠**契约反查**：跑一遍测试，缺什么它会指名报出来
  （这次是第七道报 `RebindTextChanged 里找不到 PropWriter\.IsMounting\s*\|\|\s*!textBox\.IsLoaded`）；
- **字面量可以从旧包的 dll 里捞回来**：`#US` 堆存着字符串字面量，
  UTF-16-LE，插值的洞会把一条消息切成几段。这次靠它把日志文案
  `TextBox{0} 挂载期回执（IsMounting={1}，IsLoaded={2}），不算用户输入` 完整捞回。

**规矩**：临时验证要改真源码，先 `cp` 一份带时间戳的副本，改完用副本还原并比对哈希。
不要"读-改-写"三连写在一条脚本里——写错一步就是 0 字节。

## 四·五十五、dll 的 `sha256` 不能跨次比

同一份源码，`dotnet build --no-incremental` 连编两次也能给出不同哈希
（`MVID` / PDB GUID / 时间戳）。所以：

- **能比**：`dotnet pack` 之后**立刻**拿 `bin/` 那份与包里那份比——同一次构建的产物；
- **不能比**：拿文档里记的哈希去对另一次构建。对不上是正常的，**不代表包过期**；
- **跨次要比就比语义**：文件大小 + 符号集（可打印串的集合）。
  本轮实测两边都是 350720 字节、2540 个共有符号，差异只在几十个字节上。

判定"包是不是旧构建的产物"要看**内容**：修复用符号在不在包里、
那句关键日志字面量在不在，而不是哈希。

## 四·五十六、写入侧盯完别忘了订阅侧

前十一道契约全从"**写**"出发（往控件上写什么、写在什么窗里），
共同前提是"这个 handler 已经接好了判据"。**接**的那一侧（`X.Event += h`）
一直没人管。危险只在一种形状：订阅点**每次 Patch 都会走到**（`Rebind*` 就是），
却没有幂等保护——每渲染一次叠一层，用户点一下回调跑 N 遍。
症状是"点了之后状态跳来跳去"，与受控值被写歪同类、不同入口。

**幂等的两种写法都要认**（只认一种会逼人改写法而不是改语义）：

- **表驱动**：`+=` 罩在 `if (!Table.ContainsKey(control))` 里，只订一次、之后换委托；
- **配对退订**：**同一个方法体内**有 `.<同一事件> -=`；
- 两条都不是 → 登记"一次性"，写明所在方法每个实例至多跑一次。

**关键口径**：配对退订的判定范围是"同一个方法内"，**不是"同一个类里"**。
`+=` 在 `Update`、`-=` 只在 `Unmount` 这种真漏，放到类级就蒙混过关——
而它正是要拦的形状。代价是 `+=` 在 `Mount`、`-=` 在 `Unmount`
（`BreadcrumbBar`）得走登记：它安全靠的是"Mount 每实例一次"，不是靠配对。
登记里要把这句话写明。

## 四·五十七、变异要"从订阅点反查保护者"，别按模式批量拆

按"所有含 `X` 的行"批量拆去证明契约有牙，会踩两个坑：

- **已豁免对象自己的配套代码**。拆 `BreadcrumbBar.Unmount` 的 `-=`
  当然不该有事（那个订阅点靠"Mount 只跑一次"过关）→ **假红**。
- **一对操作符在同一行**。`InputApplier.ApplyKeyEvents` 把
  `(h) => native.KeyDown += h, (h) => native.KeyDown -= h` 当参数传进辅助方法，
  整行注释掉连订阅点一起没了，违约数**不升反降**——
  看起来跟真漏一模一样，极难分辨。

正确做法：**先找出订阅点，再看是谁在保护它**，然后只改那个（些）行。
配对保护要改就**全改**（改一处留一处仍然成对，会假绿）。
拆法一律"替换行内字符串"，不整行注释、不删行（删行会让括号配平塌掉，
扫描器读成"结构坏了"而不是"保护没了"）。

## 四·五十八、真源码变异的还原必须逐字节

变异是真往源码里写再在 `finally` 还原。**还原不要用 `File.WriteAllLines`**——
它一律按 `Environment.NewLine` 重拼。仓库若是**混合换行**
（本项目 `Core/` 11 个、`Elements/` 19 个、`Internal/` 20 个、`Hosting/` 3 个文件是裸 LF），
哪天有人往 LF 文件里加一处订阅，跑一次测试就把整个文件悄悄改成 CRLF，
`git status` 把整个文件显示为已改——**与"变异没还原"长得一模一样**，
容易被误判成第二次事故。

```
var original = File.ReadAllBytes(path);
try { ...写变异... }
finally { File.WriteAllBytes(path, original); }
```

验证也要升级：跑完测试后用 `sha256sum -c` 逐个核对被变异的文件，
比"看 `git status` 干不干净"强一档（后者在混合换行的仓库里会骗人）。

## 四·五十九、"有没有"守得住的，"排在哪"未必守得住

一条契约若只做**包含判断**（窗口里有没有某个串），它就看不见**相对次序**。
受控三件套「登记 → 下发 → 撤销」里，撤销挪到下发之前、或登记挪到下发之后，
三件套**一件不少**，第一、四道照样全绿——但那一发回声永远等不到
（`NotExpected` → 框架自己的写入被当成用户输入回调出去）。

判据要补一层**位置**：登记点与它的撤销之间，必须存在一条
**以同一控件为接收者**的下发。接收者也要比对——区间里一条
`other.Value = …` 同样长得像下发。

> 分工要说清：没有撤销不算次序违约，交给"登记有没有人领"那道。
> 否则同一处漏子被两个扫描器各数一遍，变异计数会搅浑。

## 四·六十、变异手法要按"你想证明什么"来挑

想证明"这条契约咬的是次序、不是少了一件"，就不能用删行做变异——
删行会让别的契约一起红，分不清两件事。改用**对调两行**：

- 「登记 ↔ 下发」对调 → 登记排到下发之后；
- 「撤销 ↔ 下发」对调 → 撤销排到下发之前。

三件套一件不少、只是位置变了，于是可以拿老契约去量同一批变异，
做**反向对照**：老契约必须一条都看不见（本项目实测 0 处）。
这一个检查同时证明两件事——新契约咬的是它声称的那个形状，
且它不是重复防线。

对调还有个附带好处：不删行就不会让括号配平塌掉（见四·五十七）。

## 四·六十一、"在不在"守得住的，"有没有被用"未必守得住

一个**返回值形式**的判据（`Consume` 返回「这是回声吗」），
契约若只查"源码里有没有这个调用"，就看不见两种把它用废的写法：

- 当独立语句调用 → 返回值扔掉，等于没判；
- 在 `if` 里判了却不拦（体内只记日志，后面照样回调用户）。

判据要补两层：**在 `if` 条件里** + **块体里真的停下来**。

反向对照要做**两条**才能把分工钉死：

1. 把调用弄成"用废了"但**字符串还在** → 老契约必须 0 报警；
2. 把调用**换名**（站点消失）→ 老契约必须立刻报警。

两条合起来才说明：老契约盯存在性、新契约盯用法，互补而不是重复。
只做第 1 条，无法区分"新契约是重复防线"和"老契约真瞎"。

## 四·六十二、块体判据：不能只看第一行，也不能"有赋值就算"

判"这个 `if` 有没有真的停下来"时：

- **只看体内第一行会假红**。真实写法是
  「先 `ReactorLog.Gate("…回声，吞 IsOn=…")` 再 `return`」——
  第一行是日志，判"没停下来"就误报了。要看**整个块体**。
- **放宽成"块内有赋值就算"又会假绿**。块里一句 `var tag = …` 也是赋值，
  算成"停下来"等于没查。所以要认**改判的是条件表达式里出现过的标识符**
  （`verdict = Echo` 正是这种）。

**变异同理**：只改块体第一行没用——`ToggleSwitch` 那处改掉日志行、后面的
`return` 还在，照样判合规。要改就**整段**换掉。

## 四·六十三、"罩没罩住"和"关不关"是两件事

抑制用的窗（静默窗、持续标记）有两类失效：

- **罩不住**——该罩的写入裸在外面（第八、九、十道盯的）；
- **关不掉**——窗开了没关，或只关在正常路径上（**没人盯**）。

后者代价是**永久**的：`Silence` 是深度计数器，不 `Dispose` 则该控件
**之后所有用户输入**都被判成"我们自己写的"；`Rebuilding` 不关则
**之后所有选中事件**一律被吞。而且**只在前面抛过一次异常时才现形**——
所以关闭必须走 `finally` / `Dispose`，不能走正常路径。

判据：`Silence` 必须包 `using (`；`Set(…, true)` 的配对 `Set(…, false)`
必须落在 `finally` 块里。

**不是每个 `Set(ctl, true)` 都是窗**：一次性幂等标记（请求过焦点就永远为真）
和就绪位（取消走整条摘除）开了本来就不该关。所以**默认全管、例外登记**，
实测集合必须恰好等于登记表。

## 四·六十四、"看得见同一段代码"不等于"看得见同一个问题"

要证明新契约不是重复防线，别靠推理——**拿老契约的判据函数直接去量变异后的样本**。

实例：第八、九道认"窗"用的是 `\.Silence\(` 这个串，**不认 `using`**。
把 `using` 拆掉之后，那段写入在它们眼里**仍然被罩着**，判据照旧返回 `true`
——也就是说"窗没关"这件事它们一条都报不出来。

所以反向对照的正确做法是：把老契约的**判定函数**（不是整条契约）
喂进变异后的样本，看它是不是仍然判"合格"。仍然合格 → 新契约确实补了个新维度。

## 四·六十五、反向对照真的能抓出**你自己**刚写出来的重复契约

别把反向对照当"证明自己不是重复防线"的仪式——它有实际的杀伤力。

实例：第十六道的第一版判「登记过的表必须在 `Unmount` 里被同一张表 `Forget`」。
合成样本五档全绿，看着是个没人管的维度。**接上反向对照后立刻烧起来**：
抹掉 `Forget`，第四道也报了。查下去才发现第二道 `LifetimeViolations`
**早就按表名判了「漏 Forget / 清错表 / 没 Unmount」**，那 12 处还配了真源码逐行变异。
第一版是它的**超集**，整块撤掉重写（改成盯"三件套的控件参数是不是同一个"，
这个才是真没人管）。

注意这个失败模式的隐蔽性：**只写合成样本、不做反向对照的话，它会一路绿灯
混进契约网**，占着 9 条测试却什么都拦不住——而且**比没有更糟**，因为它会让人
以为这个维度已经有人管了。

**推论**：写新契约前先 grep 一遍老契约的判据函数里有没有出现过你打算用的那个
token（表名 / 正则 / 关键字）。出现过 → 你多半在写超集。反向对照是兜底的，
但先 grep 能省一轮返工。

## 四·六十六、"齐不齐"守得住的，"作用在谁身上"未必守得住

老契约数的是**符号出现与否**（`TextEcho` 有没有人 `Consume`、有没有 `Forget`），
**不看参数**。于是把 `X.Consume(control, …)` 换成 `X.Consume(sender, …)`
——表名在、三件套在、第二道和第四道**实测 0 反应**——而回声判据已经彻底失效
（键是控件，键不同 → 一律 `NotExpected` → 框架每次写入都被当成用户输入）。

**"有没有这张表"和"这张表作用在谁身上"是两个维度。** 前者齐了不代表后者对。

## 四·六十七、"写在哪"守得住的，"写的是什么"未必守得住

第十三道要求「登记与撤销之间有一条**以同一控件为接收者**的下发」，
但 `WriteProp` 正则只取了**接收者和属性名**两个分组，**右值根本没进正则**。
于是 `Expect(control, a); control.Value = b;` 完全合格——而回读的是 `b`、
登记的是 `a` → 永远 `NotExpected` → 每次写入都被当成用户输入。

**同一个语句，按"在哪"和按"是什么"能切出两个独立维度。** 加新契约时先看
老正则取了哪几个分组，没取的那些就是候选。

## 四·六十八、豁免对象自己的配套代码不能拿来变异（会假红）

第十二道踩过一次，第十七道又踩了一次，值得单列：

**豁免/白名单里的对象，本身就是"违规形状"**。比如 NavigationView 登记下标、
下发对象，本来就是"登记值与下发值不同"，靠豁免表放行。
拿它做变异（换掉右值）之后**仍然是"不同"**，违约数不变 → 不承重 → 假红。

**job 收集时必须显式跳过豁免对象**，不要以为"变异数 == 站点数"才对。
实测数字会少（第十七道是 11 处而不是 12 处），这恰恰是对的。

## 四·六十九、"被罩住的动作"不止赋值一种形状

写"某个块里在改什么"这类判据时，只认 `X.Prop =` 会漏掉一半。实测至少三种：

| 形状 | 实例 |
| --- | --- |
| 赋值 | `control.PaneDisplayMode = x;` |
| 集合动作 | `control.Items.Clear()` / `.Add(item)` |
| 控件当实参交给 helper | `reconciler.PatchItems(control, …)` / `ApplyRange(control, newElement)` |

第三种最容易漏：**改控件的活儿在 helper 内部**，调用行上没有 `control.` 前缀。
第十八道第一版只认赋值，9 处里只认出 7 处，漏的两处正是这三种里的后两种。

**判据要按"有没有明确接收者"排序**：赋值 / 集合动作匹配到就 `continue`，
只有前两种都没匹配时才看实参形状——否则 `control.Items.Add(item)` 的
实参 `item` 会被误报成"在改别的控件"。

## 四·七十、`Xxx(y)` 这类"找调用"正则的两个坑

1. **`(?:…)?` 是贪婪的。** `(?:[^()]*?,\s*)?([A-Za-z_]\w*)\s*[,)]` 对
   `ApplyRange(control, newElement)` 会优先匹配一次可选组，抓到 **`newElement`**
   而不是 `control`。要第一个实参就得取整串再切：**`(.*)` 抓到最后一个 `)`**，
   再从里面找标识符。
2. **会把 `if (modeChanged)` 认成调用。** 一次三处假违约。按函数名
   黑名单排掉关键字（`if` / `while` / `switch` / `for` / `foreach` / `return` /
   `catch` / `lock` / `using` / `fixed` / `do`），字符串字面量也要先抠掉——
   `ReactorLog.Gate("hello")` 里的 `hello` 会被当成"在改别的控件"。

另外 `(?<![\w.])` 这种后顾会把 `reconciler.PatchItems(` 排除掉（前面是点）。
要允许成员调用就用 `(?<![\w])`。

### 六十七：**"有窗"和"窗开在哪张表上"是两个维度**（第十九道）

`EchoGuard` 的 `_pending` / `_silenced` 是**实例字段**，每个受控属性一张表。
`Consume` 判回声时先看 `_silenced[control] > 0`——**只认自己那张表的窗**。
所以 `using (TextEcho.Silence(control)) { control.Maximum = 9; }` 是静默失效：
那一发回声走 `ValueEcho.Consume`，而 `ValueEcho` 的窗没开。

**判据**：窗的表名必须 ∈ 这个类 `Consume` 用的表集合（没有 `Consume` 就退回 `Expect`）。
理由：`Consume` 是"接"的那一侧，回声回来走的就是它。

**推论（可推广）**：任何"某某存在"的包含判断，都还要再问一句
"这个某某是**哪个实例**"。第十八道问的是"罩在哪个控件上"，
第十九道问的是"开在哪张表上"——同一个 `using` 行能切出两个独立维度，
因为键有两个（控件 + 表）。换一个不影响另一个，所以两道都得有。

**复用基建**：`ClassSpans(lines)` 返回 `(Start, End, Name)` 三元组，
按顶层类切区间——写新的"按类聚合"契约时直接用它，别再自己写一份。

### 六十八：**换条件 = 拆站点，不能当变异方向**（第二十道）

想证明"这条契约盯的是条件"就去改条件，结果把**锚点串本身**改没了：
`SelectionGate.Suppress(verdict)` → `false` 之后站点消失，
违约数**不升反平**（0 → 0），看着跟"契约瞎了"一模一样。
第十八道换 `Rebuilding.Set(other)` 让第二个参数丢失、站点消失，是同一个陷阱。

**判据靠什么串定位站点，变异就不能动那个串。**
要换就换站点**之外**的东西（第二十道改成动 `return` 那行：
抽掉、或降级成 `if (false) return;`）。

### 六十九："块里有没有 return"必须分三层看

1. 嵌在更里层 `if` 里的 `return` —— **不算**（那个 if 不满足时照样往下走）；
2. 单行 `if (x) return;` —— **不算**（条件退出）；
3. 只有深度 1、且**这一行以 `return` 起头**的才算。

初版只查"行里有没有 `return` 这个词"，`if (false) return;` 就一路绿灯。
**教训**：判"有没有退出"时，别用"包含某个关键字"——
和"包含判断守不住相对次序"是同一类错误（见四·五十九）。
