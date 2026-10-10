# Reactor.Uwp

UWP + WinUI 2 的声明式 UI 框架：用 C# 描述界面，不写 XAML。

- **仓库**：https://github.com/yunmoxinghe/ReactorForUWP
- **包**：https://www.nuget.org/packages/Reactor.Uwp
- **问题反馈**：https://github.com/yunmoxinghe/ReactorForUWP/issues
- **许可**：MIT

仓库里各目录的用途（这个包只含 `Reactor.uwp/`）：

| 路径 | 是什么 |
|---|---|
| `Reactor.uwp/` | 框架本体，就是本包 |
| `Reactor.Uwp.Native/` | C++/WinRT 原生桥源码 + x64 / arm64 预编译产物 |
| `UwpApp/` | 测试壳：压测 M0~M5、虚拟列表 / Echo 实验室等，手动验证用 |
| `tests/` | 控制台用例，`dotnet run` 即跑 |
| `diag-run.ps1` | 无人值守压测脚本，带 `-Mode` |

## 路线

**映射 WinUI 2，不是内置控件树。** 元素最终都落成真实的 `Windows.UI.Xaml` /
`Microsoft.UI.Xaml`（WinUI 2）控件：样式、输入法、无障碍、性能全部是原生的。
代价是被 WinUI 2 的能力边界卡住——它缺的控件这里也缺。

## 兼容性契约（两条硬规则）

1. **允许、并且要求与官方 Reactor（WinUI 3 版 `microsoft-ui-reactor`）互兼容。**
   `Component` / `Element` / `Render()` / `UseState` / `UseEffect` / `UseRef` /
   `Context` / `Component<TProps>` / `Factories` 的工厂方法，命名与参数顺序都按官方来。
判据：一份组件代码从官方 Reactor 搬过来（或搬回去），只换 `using` 与元素命名空间，
业务逻辑一行不改。

**对齐的"官方那一侧"不用凭记忆写**：官方 NuGet 包里带了一份生成出来的公开 API
索引——`%USERPROFILE%\.nuget\packages\microsoft.ui.reactor\<版本>\agentkit\reactor.api.txt`
（分 `Factories` / `Modifiers` / `Hooks` / `Theme` / `Enums` / `Public types` 几节，
一行一个签名）。缺什么照它比对即可，别照印象补。
它记的是 **WinUI 3 那一版**的表面，`AnnotatedScrollBar` / `ItemsView` /
`SelectorBar` / `ScrollView` / `TitleBar` 这类 WinUI 3 才有的东西不在本库的范围内
（WinUI 2 没有对应控件），照抄它们属于"装了也不生效"。

2. **实现一律套 WinUI 2。** 真控件、真行为、真资源：
   - 出来的节点是 `Windows.UI.Xaml.*` / `Microsoft.UI.Xaml.*`，不是自绘拼装；
   - 行为调官方那个 API（返回导航是 `Frame.GoBack()`，不是 `Navigate` 回上一页）；
   - `ThemeResource.Brush()` 给的是**活引用**：切主题时颜色跟着变，
     不是渲染那一刻抄下来的快照（`SolidColorBrush` 按资源键共享，主题变化时统一改 `Color`）。

不满足其中一条就是替代实现，注释里必须写明为什么不走官方路径。

`VirtualizingList` 走的就是官方那条路：`ItemsRepeater` + 原生元素工厂
（WinUI 2 把 `IElementFactory` 标成 internal、C# 实现不了，由
`Reactor.Uwp.Native.dll` 补上）。原生桥加载失败时（产物没落到 AppX）自动回退到自绘
（ScrollViewer + Canvas）并 Trace 一行；`ItemKey` 只在回退路径上生效。
注：项目只支持 x64 / arm64，**不做 x86**，两个架构都有原生产物。

**对齐清单当前全绿**：导航与返回走 `Frame.GoBack()` + 真 BackStack；受控属性走
`Internal/EchoGuard.cs`（`Expect` 登记 / `Consume` 匹配即吞 / `Forget` 清理），
对应官方 `Controlled<TValue, TArgs>` + counter-echo；虚拟化走 `ItemsRepeater` +
原生元素工厂；主题资源是活引用。

受控站点的数量（下面这个数）**不是手填的**：它由 `EchoContractTests` 扫
`Reactor.uwp/Internal/Handlers.*.cs` 得出，README 里的这个数由同一条测试守着——
新增一个受控站点而忘了改这里，测试会红。

<!-- CONTROLLED-SITES: 28 -->
（上面这个数是"写了受控属性、且有回执通道"的类；涉及的属性 12 个，
另有 `IsPaneOpen` 等属性被显式登记为非受控 —— 元素上没有对应回调，拿不到回执。
`RichEditBox` 的文本连"受控"都没装：官方把文本放在 `Document` 里、没有 `Text`
属性，写进去 `abc` 读出来 `abc\r`，两份值对不上，没有可判定的落点。）

`EchoGuard` 有一条语义别改回去：**只有匹配成功才消费登记**。TextBox 的粘贴 /
IME / selection replacement 会连发多个 `TextChanged`，无条件删除登记会让第二发
被当成用户输入 → 回调 → setState → 重渲染把刚粘进去的文本覆盖掉。

## 安装

```xml
<PackageReference Include="Reactor.Uwp" Version="0.1.0-alpha.9" />
```

消费方项目要求（与本机工程一致）：

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
  <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
  <UseUwp>true</UseUwp>
  <!-- alpha.4 起可写 x64;arm64；更早的包只有 win-x64 的原生桥 -->
  <Platforms>x64;arm64</Platforms>
  <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
</PropertyGroup>
```

包自带 `Microsoft.UI.Xaml 2.8.7` 与 `CommunityToolkit.Uwp.Controls.SettingsControls`
依赖，不用另外装。

## 用法

```csharp
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using static Microsoft.UI.Reactor.Factories;

// 入口：没有 App.xaml，手写 Main 启动
public sealed partial class App : ReactorApplication<CounterPage>
{
    public static void Main(string[] args) =>
        Windows.UI.Xaml.Application.Start(_ => new App());
}

public sealed class CounterPage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(
            TextBlock($"Count: {count}"),
            HStack(
                Button("-", () => setCount(count - 1)),
                Button("+", () => setCount(count + 1))));
    }
}
```

manifest 里把入口指向这个类：

```xml
<Application Id="App" Executable="$targetnametoken$.exe" EntryPoint="MyApp.App" />
```

组件用 `Render()` 描述一次界面，之后状态变化由框架 diff 出最小改动打到真实控件上。
`UseState` / `UseEffect` / `Component<TProps>` / `Provide-Context` 都有。

直接操作原生控件用 `Native(Func<UIElement> factory)` 逃生舱：控件自己造，
框架只负责放进布局和卸载时收走（库还没包住的控件走这条路）。

## 已知限制（alpha）

- **只支持 x64 / arm64，不做 x86**：原生桥带这两个架构的预编译产物。
  `Reactor.Uwp.Native.dll` 用于给 `ItemsRepeater` 提供 C# 实现的
  `IElementFactory`（WinUI 2 的 C# 投影把该接口标成了 `internal`，只能从原生侧补）。
  两个架构都随包分发在 `runtimes\win-x64\native\` 与 `runtimes\win-arm64\native\`，
  由包内的 `build\Reactor.uwp.targets` 复制到真正会加载它的 `AppX\` 目录。
  缺失架构不打包——那种情况下自定义工厂不可用，回退自绘，其余功能不受影响。
- **WinUI 2 的能力边界就是本框架的边界**：它没提供的控件需要走 `Native()` 或自己补。
  `SplitView` / `CommandBar` 这套原先落在这一条里，现已直接套
  `Windows.UI.Xaml.Controls` 的真控件（命令项与菜单项同形：是挂在命令条上的子部件，
  就地物化而不进协调器）；`MenuBar` / `RatingControl` / `PersonPicture` 走 WinUI 2 真控件。
  WinUI 2.8 里已有的（`TabView` / `InfoBar` / `InfoBadge` / `NumberBox` /
  `RadioButtons` / `BreadcrumbBar` / `DropDownButton` / `SplitButton` 等）都已直接
  套真控件，不再列入这一条。日期与时间那四个（`DatePicker` / `TimePicker` /
  `CalendarDatePicker` / `CalendarView`）是 UWP 原生——WinUI 2 没有另做一套，
  别为了"看起来像 WinUI"去换控件。集合那一族补完的 `ListBox` / `FlipView` 是
  UWP 原生（`Selector` 一族），`TreeView` 是 WinUI 2 真控件。
  布局补完那四个（`Canvas` / `Viewbox` / `VariableSizedWrapGrid` /
  `RelativePanel`）也是 UWP 原生——WinUI 2/3 没有另做一套面板。
  `ProgressBar` / `ProgressRing` / `RefreshContainer` 走 WinUI 2 真控件；
  `ProgressRing` 用的是 WinUI 版而不是 UWP 原生版（原生版只有 `IsActive`，
  不支持确定进度，别为了"少引一个包"换回去）。
  视图切换与滑动补完的四个：`SemanticZoom` 是 UWP 原生（两个槽位只收
  `ISemanticZoomInformation`），`PipsPager` / `SwipeControl` / `ParallaxView`
  走 WinUI 2 真控件。命令条上的 `AppBarToggleButton` 是 UWP 原生，
  命令条浮层 `CommandBarFlyout` 走 WinUI 2。
  菜单里那两种可勾选项官方自己就分在两处：`ToggleMenuFlyoutItem` 是 UWP 原生、
  `RadioMenuFlyoutItem` 走 WinUI 2（后者不继承前者）。子菜单 `MenuFlyoutSubItem`
  又是同一条规矩的第三例：它继承的是 `MenuFlyoutItemBase` 而不是
  `MenuFlyoutItem`，所以<b>没有</b> `KeyboardAcceleratorTextOverride`
  ——元素上也就没有那个参数，而不是给了之后偷偷不生效。
  文本命令条 `TextCommandBarFlyout` 是 WinUI 2 的 `CommandBarFlyout` 子类，
  挂在文本控件官方那个 `SelectionFlyout` 槽位上（本库对应 `.SelectionFlyout(...)`
  修饰器；该属性不在 `UIElement` 上，别处写了会被留痕并忽略）。
  图标族补完的 `ImageIcon` 走 WinUI 2（与 UWP 原生的 `BitmapIcon` 是两个控件：
  前者按尺寸缩放 + 原色，后者 1:1 画 + 可单色化，设尺寸是<b>裁</b>）。
  内容型的 `Flyout` 是 UWP 原生，
  是本库第一处**在可视树之外**管一棵真子树的地方（见 `Internal/FlyoutContent.cs`）；
  内容型的 `ToolTip` 是第二处（见 `Internal/ToolTips.cs`）——它挂在
  `ToolTipService.ToolTip` 附加属性上，同样不在可视树里，
  所以卸载入口也在 `Reconciler.UnmountNode` 而不是递归里。
  **两者都不装 `IsOpen` 之外的开合控制**：`ToolTip` 连 `IsOpen` 都不暴露——
  开合由指针与焦点驱动，声明式写一个 `true` 会每帧"拉开—收起"，
  而 `TeachingTip`（程序控制为主）才是受控的那个。
  `NavigationView` 的两个响应式阈值做成 `double?`（`null` = 不写）：那两个数字是
  官方**响应式断点**的一部分、会随模板与版本调整，抄一个具体值进来等于把可能过期的
  数字当成契约。`TextBlock` 的 `TextTrimming` / `IsColorFontEnabled` /
  `CharacterSpacing` 三个都长在 `TextBlock` / `Control` 而**不是** `UIElement`，
  所以按类型分派、给了别的控件就什么都不做（不静默失败成"写了个寂寞"）。
  `SplitView.PaneBackground` 收的是 `Brush` 实例（亚克力用 `AcrylicBrush(...)`，
  纯色用 `new SolidColorBrush(...)`）——刷子是画笔工厂产的实例，不是元素树上的节点，
  这与官方 XAML 里那个属性指向一个刷子资源是同一件事。
  仍未包的（`MediaPlayerElement` / `AnimatedIcon` / `WebView` 等）走 `Native()` 逃生舱。
  反过来，**官方有但 UWP 上没有的也不装**：`TextBox` 的 `ClearButtonVisibility`
  在 UWP 契约里 grep 不到（`Windows.Foundation.UniversalApiContract.winmd` 零命中，
  它是 WinUI 3 才加的），装出来就是"能写但不生效"的假旋钮。
  同一条判据管到"属性在、但配套不在"的那几处：`RatingControl.ItemInfo`
  （要先有 `RatingItemInfo` 元素类型才用得上）与 `TreeView` 的
  `CanDragItems` / `CanReorderItems`（要配套 `AllowDrop`，本库目前没有这个修饰器）
  ——给不出来就不给。

  图标有**两种槽位**，别混：`IconElement`（`QueryIcon` / `MenuFlyoutItem.Icon` /
  `AppBarButton.Icon`，能站进可视树）与 `IconSource`（`InfoBadge.Icon` /
  `TabViewItem.IconSource`，是数据描述）。两者的物化分别是
  `Internal/Handlers.Icons.cs` 里的 `IconElements.From` 与 `IconSources.From`；
  元素那一侧统统是 `FontIcon` / `BitmapIcon` / `SymbolIcon` / `ImageIcon`，
  翻译发生在 handler 里——所以"同一个 `FontIcon`"落进两种槽位会得到两个不同的类，
  这是官方的形状，不是我们多此一举。

- **列表项容器的名字继承项元素**：`ListView` / `GridView` 的项容器
  （`ListViewItem` / `GridViewItem`）由 UWP 生成，而容器名**只在项内容是文本时**
  才自动推导得出来——项本身是个带 peer 的控件（`SettingsCard` 这类卡片）时容器名是空，
  读屏在列表里念出来一片空白。所以 `ItemsViewHandler.Initialize` 订
  `ContainerContentChanging`，把项元素的 `AutomationProperties.Name` 传给容器
  （容器复用走同一句写入，否则上一项的名字会串到下一项头上）。
  写法就是在卡片上 `.AutomationName("…")`，见 `samples` 里 `BrowsePage.ItemCard`。
  这一条只有在真窗口上逐页扫才看得见，见 `samples/README.md` 的「逐页扫描」。

- **`TreeView` 是"不假装受控"的样板**：官方 `SelectedItem` / `SelectedNode` 只有
  getter、`TreeViewNode` 也没有 `IsSelected`——没有任何可编程入口能写选中。
  既然写不进去，"受控"就没有落点，于是选中**只出不进**（`ItemInvoked` 是唯一出口）。
  这与 `CalendarView.SelectedDates` 那个"活集合只出不进"是同一条规矩：
  **装一个受控出来，代价是"点了没反应"而且没人知道为什么**。
  键盘可达性与 `AutomationProperties` 已经照 XAML 同名属性补齐（见
  `Elements/ElementExtensions.Input.cs`）；`x:Uid` 也接了
  （`Internal/Localization.cs`），但只能覆盖"有本地化意义"的那几个属性
- API 尚未稳定，minor 版本内可能变。

## 状态

`0.1.0-alpha.9`。已验证的核心链路：纯 C# 启动与 WinUI 2 资源加载、元素 diff/patch、
Frame 导航与过渡、设置页（SettingsCard / SettingsExpander）、
ItemsRepeater 虚拟化（含回收不变量校验）、
受控属性闭环（回声抑制 / 吞后纠正 / 越界守卫 / 就绪闸，逐条有仿真与反向对照）。

受控属性这一块是 alpha.6 / alpha.7 的重点，修的是同一类症状——"点了没反应"——的若干个不同面孔：
受控写回被当成用户输入回调出去、吞掉"取消选中"后没把控件纠正回来、
受控纠正被绑到"有没有人监听"上、ListView / GridView 此前一份受控设施都没接，
以及 alpha.7 修的"纠正盖掉用户刚选的新值"（一次点击是**两发**手势：`-1` 与新值，
第一发排的纠正会在第二发之后兑现，把新值写成原值）。
每条修法都做成开关，关掉后同一批 20000 条随机序列必须失败；
另外有四道源码级契约守着接线不被照抄漏掉（详见 `docs/release-notes/alpha.6.md`、
`docs/release-notes/alpha.7.md`）。

alpha.7 在**真机**上验过：给 Template 临时装过一套自检（在真实控件上跑完整手势与
"勾单个 RadioButton"，报告落 `selftest.log`），5/5 PASS 之后已把这套调试设施从模板里摘掉——
模板是给新项目当起点的，不该带着探针。需要重现时从 git 历史取：
`git show 5282886:samples/Reactor.Template/Services/Probe.cs`。

另有一条**同症状**（"点了没反应"）但不在受控属性上的缺陷，是跑 UIA 实测时抓到的：
键盘快捷键的回调"**只在第一次有效**"——`InputApplier.Rebind` 原先先落盘 `spec`
再去比较回调引用，两边成了同一个对象，`ReferenceEquals` 恒真，于是只要 handler
挂上过一次就永远提前返回，回调再也不会更新（闭包停在挂载那一帧）。
症状：`Ctrl+F` 第一次能把焦点送到搜索框，之后再按同一个键毫无反应；
界面、UIA 树、控制台测试**全都看不见**——只有连按两次才暴露。修法与守卫见
`Internal/InputApplier.cs` 里 `Rebind` 的注释与 `EchoContractTests` 的
`CallbackRebindComparesBeforeAssign`（合成样本证明判据抓得住反例 + 扫真源码）。

未做：NuGet 上的正式版、xml 文档。**AOT 发布在本机尚未打通**——
`reg.exe` 被安全策略拦掉，链接器拿不到 Windows SDK 那半截 `LIB`（`LNK1181: advapi32.lib`），
属环境问题，不是项目配置问题。

## 发布（Trusted Publishing）

包通过 GitHub Actions 发布，走 nuget.org 的 Trusted Publishing（OIDC），
仓库里不存 API key。工作流见 `.github/workflows/publish.yml`，
手动触发或推 `v*` tag 均可。

nuget.org 侧的策略（`Account → Trusted Publishing`）需要四个值：

| 字段 | 值 |
|---|---|
| Repository Owner | `yunmoxinghe` |
| Repository | `ReactorForUWP` |
| Workflow File | `publish.yml`（只填文件名，不带路径） |
| Environment | 留空 |

仓库侧只留一个 secret：`NUGET_USER` = nuget.org 的**用户名** `yunmoxing`
（不是邮箱，也不是 GitHub 上的 `yunmoxinghe`）。

两点提醒：策略是**按包所有者**生效的，不限于单个包 id，
所以 scope 建议用 glob 限定到 `Reactor.Uwp`；
私有仓库的策略初次只有 7 天临时激活期，
首次成功登录（不必真的推包）后才会永久绑定 GitHub 的 repo/owner ID。
