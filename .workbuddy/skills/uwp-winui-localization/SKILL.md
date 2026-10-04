---
name: uwp-winui-localization
description: |
  UWP / WinUI 2（含纯代码、无 XAML 编译器场景）的本地化实战指南：resw 布局与标识符规则、
  x:Uid 的真实语义、附加属性 [using:...] 语法与 PRI 键推导、ResourceLoader 选型矩阵、
  运行时切换语言必须重建 loader 而不是清缓存、MakePri dump 验证。
  当用户要做 .resw 本地化、接 x:Uid、切语言不生效、Tooltip/AutomationProperties.Name 查不到资源时使用。
agent_created: true
---

# UWP / WinUI 2 本地化

两代 API 名字很像但命名空间不同，**先分清你在哪一套**：

| 场景 | 命名空间 |
|---|---|
| UWP（本项目）/ WinUI 2 | `Windows.ApplicationModel.Resources`、`.Core` |
| WinUI 3 / Windows App SDK / 非打包 | `Microsoft.Windows.ApplicationModel.Resources` |

WinUI 2 最高只到 **2.8（2022-07）**，之后只有 patch，没有新的本地化 API。别拿 WinUI 3 的
MRTCore 文档往 UWP 上套（`ResourceLoader` 构造签名、异常行为都不一样）。

权威文档：
- <https://learn.microsoft.com/windows/uwp/app-resources/localize-strings-ui-manifest>
- <https://learn.microsoft.com/windows/uwp/app-resources/specify-language-resources>
- <https://learn.microsoft.com/windows/uwp/app-resources/using-mrt-for-converted-desktop-apps-and-games>（非打包）

## 1. resw 放在哪

```
Strings/
  en-US/Resources.resw     ← 默认语言，必须有
  zh-CN/Resources.resw
```

- 文件夹名是 **BCP-47 语言标记**（`zh-CN` 不是 `zh-Hans`）。
- 文件名推荐 `Resources.resw`（默认名，引用时可省略文件名）。
- csproj 里 `<DefaultLanguage>zh-CN</DefaultLanguage>` 必须与默认那份 resw 的文件夹名一致。
- 打包清单里显式列出语言，不要写 `x-generate`（`x-generate` 只按 DefaultLanguage 收一份）：

```xml
<Resources>
  <Resource Language="en-US" />
  <Resource Language="zh-CN" />
</Resources>
```

## 2. 标识符：两种，不能混

| 类型 | 例子 | 谁用 |
|---|---|---|
| **属性标识符** | `Greeting.Text`、`Greeting.Width` | `x:Uid` 用；代码里要把 `.` 换成 `/` 才能查 |
| **简单标识符** | `Farewell` | 只能代码 `GetString("Farewell")` 查；挂 `x:Uid` **不产生任何效果** |

硬规则：
- 同名不能同时有简单标识符和属性标识符 —— resw 编译期 **Duplicate Entry** 报错。
- 标识符**大小写不敏感**，且每个 resw 内唯一。
- 送翻后**不要改标识符**（"identifier shift" 会让整条字符串被当成删除+新增，要重翻）。
- Comment 列是给翻译的备注，不需要翻译。

## 3. x:Uid 的真实语义（最容易理解错）

```xaml
<TextBlock x:Uid="Greeting"/>
```

- XAML **编译器**把它展开成 `ResourceLoader.GetForCurrentView().GetString("Greeting/Text")`
  再赋到 `Text` 上。**运行时 `UIElement` 上不存在 `Uid` 属性可查** —— 纯代码场景没人帮你做这步。
- **只有属性标识符生效**：`x:Uid="Farewell"` 而 resw 里只有 `Farewell`（简单标识符）→ 什么都不发生。
- **所有属性标识符都必须适配该元素**：`x:Uid="Greeting"` 挂到 `Button` 上，`Greeting.Text` 会
  **运行时报错**（Button 没有 Text）。要么改键为 `ButtonGreeting.Content`，要么换 Uid。
- resw 的值**覆盖**标记/代码里本地设置的值，且只在**加载初始化时求值一次**。

## 4. 附加属性：`[using:...]`

resw Name 列必须写完整的 `所有者.属性名`，并用方括号声明命名空间：

```
Greeting.[using:Windows.UI.Xaml.Automation]AutomationProperties.Name
Greeting.[using:Windows.UI.Xaml.Controls]ToolTipService.ToolTip
```

**`[using:...]` 不会被剥掉 —— 它是键的一部分**，再叠加"点转分层"。
实测（`makepri dump`）：

| resw 的 Name 列 | PRI 里的真实 uri |
|---|---|
| `Greeting.Text` | `Resources/Greeting/Text` |
| `Auto.[using:Windows.UI.Xaml.Automation]AutomationProperties.Name` | `Resources/Auto/[using:Windows.UI.Xaml.Automation]AutomationProperties/Name` |
| `Tip.[using:Windows.UI.Xaml.Controls]ToolTipService.ToolTip` | `Resources/Tip/[using:Windows.UI.Xaml.Controls]ToolTipService/ToolTip` |

推论（很多人搞错）：纯代码里查附加属性，键**必须带 `[using:命名空间]` 前缀，
并且 `Owner.Property` 之间的点也要换成 `/`**：

```csharp
// ✅ 命中
loader.GetString("Greeting/[using:Windows.UI.Xaml.Automation]AutomationProperties/Name");
loader.GetString("Greeting/[using:Windows.UI.Xaml.Controls]ToolTipService/ToolTip");
// ❌ 命中不了
loader.GetString("Greeting/AutomationProperties.Name");
loader.GetString("Greeting/ToolTip");
```

WinUI 2 控件的附加属性用 `Microsoft.UI.Xaml.*` 命名空间，不要写成 `Windows.UI.Xaml.*`。

> WinUI 3 曾有过回归（microsoft-ui-xaml#3649），部分版本下 `[using:...]` 语法会
> XamlParseException；UWP/WinUI 2 上这是官方推荐写法。

## 5. 点 → 斜杠（只对资源名，不对文件名）

```csharp
// resw: <data name="Fare.Well">
loader.GetString("Fare/Well");        // ✅ 资源名里的 . 换成 /
// resw 文件叫 Err.Msgs.resw
ResourceLoader.GetForCurrentView("Err.Msgs");  // ✅ 文件名里的 . 保留
```

## 6. ResourceLoader 选型矩阵

| 条件 | 用哪个 |
|---|---|
| 打包 UWP，UI 线程 | `ResourceLoader.GetForCurrentView()` |
| 后台/工作线程 | `GetForViewIndependentUse()`，或用 `CoreWindow.GetForCurrentThread() != null` 兜住 `GetForCurrentView()` |
| 非打包（unpackaged） | **必须** `GetForViewIndependentUse()`，无 view 时 `GetForCurrentView()` 直接抛 |
| 类库 / 组件 | 从**宿主应用**加载（运行时加载的是宿主的资源）。库自带资源时要给宿主替换的口子 |

失败表现：在无 CoreWindow 的线程上调 `GetForCurrentView()` →
"*may not be created on threads that do not have a CoreWindow*"。

## 7. 运行时切换语言（最常踩的坑）

`ResourceLoader` 对象在创建时**快照**了 ResourceContext。
**清自己的缓存字典救不了你 —— 必须换 loader。**

正确做法（三选一，按控制粒度）：

```csharp
// A. 应用级覆盖语言（推荐给"应用内切语言"）
Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "de-DE";

// B. 全局限定符覆盖
ResourceContext.SetGlobalQualifierValue("Language", "de-DE");

// C. 单次查询用自定义 context
var ctx = new ResourceContext();          // 注意：不是 GetForCurrentView()
ctx.QualifierValues["Language"] = "de-DE";
ResourceManager.Current.MainResourceMap
    .GetSubtree("Resources").GetValue("Farewell", ctx).ValueAsString();
```

然后：

1. **丢掉旧的 ResourceLoader**（重建，不要复用缓存的实例）；
2. 清自己的字符串缓存；
3. 订阅限定符变化，回到 UI 线程重刷：

```csharp
ResourceContext.GetForCurrentView().QualifierValues.MapChanged += (s, e) =>
{
    var d = Dispatcher;
    if (d.HasThreadAccess) RefreshUIText();
    else await d.RunAsync(CoreDispatcherPriority.Normal, RefreshUIText);
};
```

4. XAML 应用靠"重建页面/重新 InitializeComponent"刷新；**纯代码构建的 UI 不会自动重建**，
   必须自己把文本再写一遍。
5. 回退规则：找不到匹配语言 → 逐级回退 → 最终用 **应用默认语言**。

## 8. 多个 resw 文件

| 位置 | 引用方式 |
|---|---|
| XAML | `x:Uid="/ErrorMessages/PasswordTooWeak"` |
| 代码 | `ResourceLoader.GetForCurrentView("ErrorMessages").GetString("MismatchedPasswords")` |
| 清单 | `ms-resource:/ManifestResources/AppDisplayName` |

`Resources.resw` 是默认名，省略前缀即指它。

## 9. 清单本地化

`ms-resource:AppDisplayName` 写在 `Package.appxmanifest` 的 Display name / Description /
Short name 上。可本地化项清单见文档 "Localizable manifest items"。

## 10. 验证：MakePri dump

拿不准键名就别猜，dump PRI 看真实 uri：

```bat
makepri dump -if resources.pri -of dump.xml
```

输出里每个资源长这样：

```xml
<NamedResource name="Well" uri="ms-resource://<GUID>/Resources/Fare/Well">
```

**这是唯一的权威答案。** 键名对不对、resw 有没有被打进 PRI，一眼可见。
非打包应用还要手动 `makepri new` 并把 `resources.pri` 拷到 exe 目录（改了资源必须重生成）。

## 10b. 离线验证键名的招数（不用动项目）

拿不准某个 resw 键会落成什么 uri，就造一个最小工程单独生成 PRI：

```bat
mkdir Strings\en-US
:: 写一个只含几条 <data> 的 Resources.resw
makepri createconfig -cf priconfig.xml -dq en-US -pv 10.0.0
makepri new -pr . -cf priconfig.xml -of resources.pri -in Reactor.Probe
makepri dump -if resources.pri -of dump.xml
```

在 Git Bash 里跑 makepri，参数要用 `-if` 而不是 `/if`，并且加
`MSYS_NO_PATHCONV=1` —— 否则 MSYS 会把 `/if` 当成路径转成
`C:/Program Files/PortableGit/.../if`（报 PRI104 unknown option）。

---

# 纯代码场景（Reactor / 无 XAML 编译器）

没有 XAML 编译器，`x:Uid` 的展开得自己实现。对照官方语义，逐条对齐：

1. **按 `Uid/Property` 查**，不是按 `Uid.Property`。PRI 里不存在带点的资源名
   （点已被转成分层），写 `Uid.Property` 是死代码，永远返回空。
2. **按类型显式列出可本地化属性**（`TextBlock.Text`、`TextBox.Text/Header/PlaceholderText`、
   `ContentControl.Content`…），**不要反射找 `XProperty` 静态字段** —— AOT 下靠 rd.xml 续命且失败静默。
3. **只在元素挂载（mount）时求值一次**，且放在属性写入的最后 —— 与 XAML 编译器一致
   （resw 的值覆盖代码里写的值）。
4. **附加属性必须拼 `[using:命名空间]Owner/Property`**（见第 4 节的实测表）。
   写 `ToolTip`、`AutomationProperties.Name` 都命中不了。
5. **查不到就什么都不做**（`GetString` 对不存在的键返回空串，不抛），与编译器行为一致：
   只为 resw 里真实存在的键生成赋值。
6. **缓存用大小写不敏感的比较器**（资源标识符大小写不敏感）。
7. **键不存在 ≠ 值为空串**：别用 `IsNullOrEmpty` 当"没资源"，否则"翻译为空"的资源会被静默丢弃。

## 语言切换在纯代码 UI 里必须自己接线

XAML 应用切语言后会重建页面，纯代码 UI 不会。所以必须显式做这三步，缺一个都不生效：

1. 订阅 `ResourceContext.GetForCurrentView().QualifierValues.MapChanged`（或 app 自己切语言时主动触发）；
2. **重建 ResourceLoader**（`_loader = null`），只清自己的字典没用；
3. 把文本重新写回控件 —— 要么重渲染整棵树，要么给每个带 Uid 的元素重新 Apply 一次。

## 包内资源（图标/图片）：代码里必须自己补 `ms-appx:`

官方文档 `Image.Source` 的原话：XAML 属性里的相对路径由 **XAML 解析器** 拿页面 base URI 补全，
代码里 **没有这个捷径**，必须自己构造带 `ms-appx:` 的绝对 URI —— WinRT 不接受 `UriKind.Relative`。

```csharp
// 对：显式绝对 URI
img.Source = new BitmapImage(new Uri("ms-appx:///Assets/Logo.png"));
// 错：相对路径，WinRT 直接拒绝
img.Source = new BitmapImage(new Uri("Assets/Logo.png"));
```

**配套一脚坑：反斜杠。** 清单里写的是 Windows 路径 `<Logo>Assets\StoreLogo.png</Logo>`。
如果某条路径把这份字符串原样交出来，直接拼成 `ms-appx:///Assets\StoreLogo.png`，
反斜杠会带进 `Windows.Foundation.Uri` 变成普通字符或 `%5C` → 资源解析失败。
**失败是静默的**：不抛异常、不报错，图就是不出来。

```csharp
// 先规范化分隔符，再决定要不要补前缀
var norm = source.Replace('\\', '/');
var uri = norm.Contains("://") ? new Uri(norm) : new Uri("ms-appx:///" + norm.TrimStart('/'));
```

### `Package.Current.Logo` 实测交出的是什么（2026-10 在本仓库实测）

**不是**相对路径，**不是**清单里那个 `Assets\StoreLogo.png`，而是**绝对文件 URI**：

```
file:///D:/…/AppX/Assets/SmallTile.scale-150.png
```

两个意外之处：

1. 它是 `file:///` 而不是 `ms-appx:`。官方给包内资源的通道是 `ms-appx:///相对路径`
   （Raymond Chen：「要引用包里的内容不需要取路径，用 ms-appx 协议就行」）。
   `file:///` 能"碰巧"加载，但它不是受支持的路径 —— 建议映射回 `ms-appx:`。
2. 取到的是 **SmallTile**（`uap:VisualElements/@Square71x71Logo` 那份，scale-150 后 106px），
   **不是** `<Properties>/<Logo>` 的 StoreLogo。

映射办法（幂等；不是安装目录下的 `file:///` 就别动，比如用户自己选的本地文件）：

```csharp
static string? ToAppx(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    var norm = raw.Replace('\\', '/').Trim();
    if (!norm.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) return norm;

    var install = Package.Current.InstalledLocation.Path.Replace('\\', '/').TrimEnd('/');
    var at = norm.IndexOf(install, StringComparison.OrdinalIgnoreCase);
    if (at < 0) return norm;

    var rel = norm[(at + install.Length)..].TrimStart('/');
    return rel.Length == 0 ? norm : "ms-appx:///" + rel;
}
```

### 别给 `BitmapIcon` 设 Width / Height

`BitmapIcon` **不像 `Image` 那样把位图缩放适配**：设了 `Width`/`Height` 就是把原图
**裁**出一个角（106px 的图设成 20×20 → 只剩左上角一小块），看起来就是"图标缩没了"。
位图图标的自然尺寸来自解码后的位图，**要缩放交给宿主**（Viewbox / 卡片的图标呈现器）。

所以"图标小/看不见"有两条独立成因，别只查一条：

| 成因 | 表现 | 怎么确认 |
|---|---|---|
| URI 没加载成功 | 自然尺寸 0 → 彻底不显示 | 换成 `ms-appx:///` 再试 |
| 自己设了 Width/Height | 原图被**裁**，只剩一角 | 去掉尺寸，让宿主缩放 |
| 宿主有上限（如 `SettingsCardHeaderIconMaxSize`） | 整体等比缩小 | 查卡片的资源字典；**先确认装的版本里到底有没有这个键** |

> 踩过的一脚：GitHub `main` 上 CommunityToolkit 的 `SettingsCard.xaml` 有
> `<x:Double x:Key="SettingsCardHeaderIconMaxSize">20</x:Double>`，
> 但**装的版本 8.2.251219 的包里根本没有这个键**（二进制搜也搜不到）。
> 拿 `main` 源码当"已安装版本"的证据会得出错误结论 ——
> 查这类值必须查实际安装的包，或运行时读 `Application.Current.Resources`。

## 应用名：别把 `ms-resource:` 原始串显示给用户

清单写 `ms-resource:AppDisplayName` 后，系统（MSIX 运行时）负责解析成当前语言的字符串。
但解析依赖包部署/PRI 在位；没解析出来时属性会**原样**返回 `ms-resource:AppDisplayName`。

```csharp
var name = Package.Current.DisplayName;           // 正常：已本地化的名字
if (string.IsNullOrWhiteSpace(name) ||
    name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
{
    // 兜底：自己查资源表
    name = ResourceLoader.GetForViewIndependentUse().GetString("AppDisplayName");
}
```

要点：

- 兜底查的键就是 resw 里的 `AppDisplayName`，与清单引用同名。
- 用 `GetForViewIndependentUse()`：`GetForCurrentView()` 要求当前线程有 CoreWindow，
  静态初始化阶段调用会直接抛。
- `Package.Current.DisplayName` 在 < 10.0.19041 上对非 `Package.Current` 的实例返回空串。

### ⚠️ 实测：`Package.Current.DisplayName` 会解析成**别的键的值**（2026-10）

本仓库实测（PRI 用 `makepri dump -dt detailed` 验过，资源本身完全正确）：

| 取法 | 结果 |
|---|---|
| `ResourceLoader.GetForViewIndependentUse().GetString("AppDisplayName")` | `Reactor 模板` ✅ |
| `Package.Current.DisplayName`（清单 `ms-resource:AppDisplayName`） | `声音` ❌ |

`声音` 是 `SoundGroup/Text` 的值 —— 一个毫不相干的键。PRI 里 `AppDisplayName`
只有一个、URI 正确（`Resources/AppDisplayName`），值也正确。所以：

- **按名字查（ResourceLoader）永远对**；
- **走清单 `ms-resource:` 的系统解析（AppModel 层）会串** —— 这条路径不在我们代码里，
  改不了，参考实现用同一套 API 也会中招。

**结论：应用名一律自己按名字查资源表，不要用 `Package.Current.DisplayName` 当来源。**

### 清单里两个 DisplayName 走的是**不同的解析链**

```
Package.DisplayName          ← <Properties>/<DisplayName>        ← 实测会串（AppModel 实时解析）
开始菜单 / 磁贴 / 外壳        ← uap:VisualElements/@DisplayName   ← 实测解析正确
```

所以遇到"系统解析出错误名字"时，**只把 `<Properties>/<DisplayName>` 改成字面量**
（切断坏的链），`uap:VisualElements/@DisplayName` 保留 `ms-resource:`（外壳名字照旧本地化）。

### 排查这类问题的取证手段（按性价比排序）

| 手段 | 能证明什么 |
|---|---|
| `makepri dump -dt detailed` | PRI 里的键、URI、索引、每种语言的值到底对不对 |
| `Get-StartApps`（PowerShell） | **外壳解析出来的名字**（`AppID` 带包标识） |
| 注册表 `HKCU:\Software\Classes\Local Settings\...\AppModel\Repository\Packages\<PackageFullName>` 的 `DisplayName` 值 | 存的**是未解析的 `ms-resource:`** → 证明是**实时解析**、不是注册期缓存 |
| `Get-AppxPackage` | 有没有同标识的包重复注册 / 装在哪 |

```powershell
Get-StartApps | Where-Object { $_.AppID -like "*<Identity Name>*" } | Format-List Name, AppID
(Get-ItemProperty "HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages\<PackageFullName>").DisplayName
```

> 注意：`reg.exe` 常被安全策略拦，用 PowerShell 的 `HKCU:\...` PSDrive 代替。
> `SHLoadIndirectString` 在离线下拿 `@{<pri路径>? ms-resource://...}` 会返回
> `0x80070057 (E_INVALIDARG)` —— **它不能当离线 oracle**，别浪费时间。

## 自查清单

- [ ] resw 在 `Strings/<BCP-47>/Resources.resw`？csproj 的 `DefaultLanguage` 与默认文件夹一致？
- [ ] 清单 `<Resources>` 显式列了所有语言（不是 `x-generate`）？
- [ ] 同一基础名没有同时存在简单标识符和属性标识符？
- [ ] 属性标识符都适配目标元素类型（`Button` 上不能有 `.Text`）？
- [ ] 附加属性用了 `[using:...]`，代码侧查的是 `[using:命名空间]Owner/Property` 完整形式？
- [ ] 代码查资源时把 `.` 换成了 `/`？
- [ ] `ResourceLoader` 选型对了（非打包 / 后台线程别用 `GetForCurrentView`）？
- [ ] 切语言：重建 loader + 清缓存 + 重刷 UI，三件都做了？
- [ ] 用 `makepri dump` 确认过键名真的在 PRI 里？
- [ ] 包内图片/图标在代码里用了 `ms-appx:///` 绝对 URI，且把 `\` 规范化成了 `/`？
- [ ] 应用名有兜底，不会把 `ms-resource:` 原始串显示给用户？
