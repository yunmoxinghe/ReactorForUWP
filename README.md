# ReactorForUWP

[![publish](https://github.com/yunmoxinghe/ReactorForUWP/actions/workflows/publish.yml/badge.svg)](https://github.com/yunmoxinghe/ReactorForUWP/actions/workflows/publish.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Reactor.Uwp)](https://www.nuget.org/packages/Reactor.Uwp)

UWP + WinUI 2 的声明式 UI 框架：用 C# 描述界面，不写 XAML。
框架本体以 NuGet 包 **[Reactor.Uwp](https://www.nuget.org/packages/Reactor.Uwp)** 发布，
这个仓库是它的源码 + 测试载体。

**路线是映射 WinUI 2，不是内置控件树**：元素最终落成真实的
`Windows.UI.Xaml` / `Microsoft.UI.Xaml`（WinUI 2）控件，样式、输入法、无障碍、
性能全部是原生的；代价是被 WinUI 2 的能力边界卡住。

## 兼容性契约（两条硬规则）

写代码前先看这两条，它们是本仓库的判定标准，不是口号：

1. **允许、并且要求与官方 Reactor（WinUI 3 版 `microsoft-ui-reactor`）互兼容。**
   API 的形状跟官方对齐：`Component` / `Element`（不可变 record）/ `Render()` /
   `UseState` / `UseEffect` / `UseRef` / `Context` / `Component<TProps>` /
   `Factories` 里的工厂方法名与参数顺序都按官方来。
   **判据**：一份组件代码从官方 Reactor 搬到本框架（或反过来），只需要换
   `using` 与元素所在命名空间，业务逻辑一行都不用改。

2. **实现一律套 WinUI 2，不另起炉灶。**
   每个元素落成真控件，改外观走官方属性 / `Style` / `ItemTemplate`，取资源走官方
   资源解析，行为调官方 API。
   **判据三条**：
   - **真控件**——`VisualTreeHelper` 走出来的节点类型是 `Windows.UI.Xaml.*` 或
     `Microsoft.UI.Xaml.*`，不是我们自己画的 `StackPanel` 拼装。
   - **真行为**——调官方那个 API，而不是"模拟得差不多"。返回导航就是
     `Frame.GoBack()`（不是 `Navigate` 到上一页），音效就是
     `ElementSoundPlayer.Play(ElementSoundKind.GoBack)`。
   - **真资源**——主题资源是活引用（`{ThemeResource}` 的语义，切主题要跟着变），
     不是"渲染那一刻抄一份颜色下来"。

三条里任何一条不满足，就是**替代实现**，必须在代码注释里写明为什么不走官方路径，
否则视为待修的债。

`VirtualizingList` 以前是最大的一处替代实现（自绘 `ScrollViewer` + `Canvas`），
原因是 WinUI **2** 把 `IElementFactory` 标成 internal、C# 侧实现不了；
原生桥 `Reactor.Uwp.Native.dll` 补上之后已改回官方路径：
`ItemsRepeater` + 原生元素工厂。**原生桥加载失败时（产物没落到 AppX）自动回退自绘**
并 Trace 一行——那是部署防御，不是架构分支：项目只支持 x64 / arm64，两个架构都有
原生产物。实测（5000 项）只 realize 48 个容器，虚拟化是生效的。
`ItemKey` 在 `ItemsRepeater` 那条路上不起作用（WinUI 2 的 ItemsRepeater 不做按 key
复用，官方 Reactor 同样如此），只在自绘回退路径上生效。

**对齐清单当前全绿**：

| 项 | 落在哪 | 官方对应 |
|---|---|---|
| 页面导航与返回 | `Frame.Navigate` / `Frame.GoBack()` + 真 BackStack，返回音效 `ElementSoundPlayer.Play(ElementSoundKind.GoBack)` | 同 |
| 受控属性的回声抑制 | `Internal/EchoGuard.cs`（`Expect` 登记期望值 / `Consume` 匹配即吞 / `Forget` 卸载清理），已在 Text / Check / Value / Selection / Toggle 等 7 处接好 | `Controlled<TValue, TArgs>` + counter-echo |
| 虚拟化长列表 | `ItemsRepeater` + 原生元素工厂 | 同 |
| `ListBox` / `FlipView` | 真 UWP 原生控件。与 `ListView` / `GridView` **同一条选中契约**（`Selector.SelectedIndex` + 回声抑制 + 四道判据），但基类不同（只是 `Selector`：无 `Header`、无 `ItemClick`、选中模式是 `SelectionMode`），所以走另一条 handler 基类 `SelectorHandler`，不去改 `ItemsViewHandler` 那条已验过的路 | 同（`Selector` 语义） |
| `TreeView` | 真 WinUI 2 `TreeView`。**本版唯一"选中结构上就不可写"的控件**：官方 `SelectedItem` / `SelectedNode` 只有 getter、`TreeViewNode` 没有 `IsSelected` → 不假装受控，选中**只出不进**（`ItemInvoked` 是官方唯一出口，`TreeView` 没有 `SelectionChanged`）。节点树挂载期物化，节点内容只收字符串 | 同（`TreeView` 无 `SelectionChanged`，只有 `ItemInvoked`） |
| `Group` / `ForEach` 摊平 | `GroupElement` 作为**子项**传进别的容器时被 `FilterChildren` 摊平，N 个结果变成父容器的 N 个子项/列表项 | 官方 Reactor 无对应物（这是本仓库 `ForEach` 的语义补丁，见下） |
| `Canvas` / `VariableSizedWrapGrid` | 真 UWP 原生面板。**位置/跨格是附加属性，写在子元素身上**（`.Canvas(left:,top:,zIndex:)`、`.WrapSpan(rowSpan:,columnSpan:)`），与官方 `GridAttached` 同一形状，落点在 `ElementHandler.AttachChild` | 同（XAML 的 `<Canvas Left="…">` / `VariableSizedWrapGrid.RowSpan`） |
| `Viewbox` | 真 UWP 原生 `Viewbox`。布局族里唯一的**单子元素**容器；它继承 `FrameworkElement` 而非 `ContentControl`，所以在 `SingleChildAccessor` 登记过，走 `PatchSingleChild` 通用路径 | 同 |
| `RelativePanel` | 真 UWP 原生 `RelativePanel`。两类关系：贴面板（`AlignXxxWithPanel`，布尔）、贴兄弟（`Below` / `RightOf` / `AlignLeftWith` …，填**同层子元素的下标**——声明式树没有 `x:Name` 可给）。每轮全量重落，所以"去掉一条关系"真的会失效 | 同（XAML 里写兄弟名字，标记编译器把它解析成对象引用；这里用下标得到同一个结果） |
| `ProgressBar` / `ProgressRing` | 真 WinUI 2 控件。**确定 / 不确定由 `Value` 是否为 null 决定**，不是另有一个开关（两种形态互斥，分成两个字段就会出现自相矛盾的组合）。`ProgressRing` 用 WinUI 版而非 UWP 原生版——原生版只有 `IsActive`、不支持确定进度 | 同（`IsIndeterminate` 语义） |
| `RefreshContainer` | 真 WinUI 2 `RefreshContainer`（单子元素容器，继承 `ContentControl`）。**刷新是可延迟的**：回调拿到 `RefreshTicket`（官方 `Deferral` 的包装），取了之后可视化器一直转，直到 `Complete()`。`Visualizer` 与命令式的 `RequestRefresh()` 不暴露——前者有官方默认值、后者在声明式树里没有拿句柄的地方 | 同（`RefreshRequestedEventArgs.GetDeferral()`） |
| 主题资源 | `ThemeResource` 活引用（`SolidColorBrush` 按键共享，切主题统一改 `Color`） | `{ThemeResource}` 语义 |
| `TabView` 受控 `SelectedIndex` | 真 WinUI 2 `TabView`；宿主是 `Grid`（`TabView` 继承 `Control`，没有 `Content`），选中闭环走 `EchoGuard` + `SelectionGate` + `ReadyGate` | 同（`Controlled<SelectedIndex>`） |
| `Pivot` 受控 `SelectedIndex` | 真 UWP 原生 `Pivot`。与 `TabView` 的取舍**相反**：每页的内容各挂自己的 `PivotItem.Content`（`PivotItem` 继承 `ContentControl`），而不是只在容器上留一份——"内容跟着手势横移"这个过渡的前提就是内容在 `PivotItem` 里 | 同（XAML 的 `<Pivot><PivotItem Header="…">…</PivotItem></Pivot>`） |
| `RichTextBlock` / `Paragraph` / `Run` | 真 `Block` / `Inline`，handler 内部按位 patch（它们不是 `UIElement`，协调器那条路的输入输出约定是 `UIElement`） | 官方未提供此路径（官方 Reactor 无富文本元素），此处按"套 WinUI 2 真控件"这条硬规则自行补齐 |
| `InfoBadge` | 真 WinUI 2.8 `InfoBadge`；`Value = -1` 是官方"圆点"那一档；预设样式按 XAML 资源键（`SuccessBadgeStyle` 那几个）从 `Application.Resources` 取，取不到降级默认外观并 Trace 一行 | 同（XAML 的 `Style="{StaticResource …BadgeStyle}"`） |
| `NavigationView.AutoSuggestBox` | 元素上的 `SearchBox` 槽位 → 官方同名属性 | 同（XAML 的 `<NavigationView.AutoSuggestBox>`） |
| `NavigationView` 设置项 | `OnItemInvoked` 以 **-1** 回调（它不在 `MenuItems` 里）→ 官方 `IsSettingsVisible` 那个原生项 | 同（`ItemInvoked` 的 `IsSettingsInvoked`） |
| 键盘快捷键 | 元素上的 `KeyboardAccelerators` → 建真的 `Windows.UI.Xaml.Input.KeyboardAccelerator` 加进集合（不是"自己监听按键再派发"） | 同（XAML 的 `<UIElement.KeyboardAccelerators>`） |
| 请求焦点 `FocusToken(int)` | 令牌**变了**就调一次 `control.Focus(FocusState.Programmatic)`（边沿触发，不是每帧抢焦点） | 官方无声明式对应物；等价的是代码后置里那句 `control.Focus(...)`（WinUI 3 Gallery 的 Ctrl+F 正是这么写的） |
| 按钮内容槽 `Button(Element, onClick)` | 真 `Button`（`ContentControl`），内容走 `PatchSingleChild` | 同（官方 `Button` 同样收内容） |
| `DropDownButton` / `SplitButton` | 真 WinUI 2 同名控件；`DropDownButton` **没有点击回调**（官方也就不提供 `Click`），`SplitButton` 左半 `Click`、右半展开 | 同 |
| 声明式菜单 `MenuFlyout` / `MenuItem` / `MenuSeparator` | 建成真的 `MenuFlyout` / `MenuFlyoutItem` / `MenuFlyoutSeparator`。它们不是 `UIElement`（走 `FlyoutBase` / `MenuFlyoutItemBase` → `DependencyObject`），因此与 `RichTextBlock` 同路：**元素是描述，宿主 handler 就地物化**，不进协调器 | 同（XAML 的 `<MenuFlyout><MenuFlyoutItem …/></MenuFlyout>`） |
| 右键菜单 `ContextMenu(...)` | 修饰器上的 `ContextMenu` 槽位 → 官方 `UIElement.ContextFlyout`；另保留 `ContextFlyout(...)` 收已造好的原生实例 | 同（XAML 的 `<UIElement.ContextFlyout>`） |
| `SplitView` | 真 UWP 原生 `SplitView`。两个独立槽位（`Pane` / `Content`），不是"面板是第一个子元素"；`Content` 借 `SingleChildAccessor` 走协调器的单子槽位，`Pane` 是主槽之外的第二个槽（配对的 `Element` 只有 handler 有），改由 handler 通过 `ExtraSlotsOf` 报给协调器、卸载时跟着一起递归。`IsPaneOpen` **非受控**：官方那四个开合事件一个都没订阅，没有回执通道就不装成受控 | 同（XAML 的 `<SplitView><SplitView.Pane>…</SplitView.Pane>…</SplitView>`） |
| `CommandBar` / `AppBarButton` / `AppBarSeparator` | 真 UWP 原生控件。命令项与菜单项**同形**：是挂在命令条上的子部件（`ICommandBarElement`），进了 `PrimaryCommands` 就不能再进 `Panel`，所以就地物化、不进协调器。窗口变窄时主命令区放不下的项由官方溢出算法收进 `…` | 同（XAML 的 `<CommandBar><AppBarButton …/></CommandBar>`） |
| `MenuBar` / `MenuBarItem` | 真 WinUI 2 控件。每组的 `Items` 与 `MenuFlyout.Items` 收的是**同一个类型**（`MenuFlyoutItemBase`），所以直接复用 `MenuItem` / `MenuSeparator`、物化复用 `MenuFlyouts.Materialize`——"菜单项怎么写"只有一份写法 | 同（XAML 的 `<MenuBar><MenuBarItem Title="…"><MenuFlyoutItem …/></MenuBarItem></MenuBar>`） |
| `RatingControl` 受控 `Value` | 真 WinUI 2 `RatingControl`；写 `Value` 会同步抛 `ValueChanged`，那一发靠 `EchoGuard` 认下来（与 `NumberBox` / `Slider` 同形）。只读是官方 `IsReadOnly` 属性，不是把回调摘掉 | 同（`Controlled<Value>`） |
| `PersonPicture` | 真 WinUI 2 `PersonPicture`。无受控值（用户改不了它），写入一律"值变了才写" | 同 |
| `DatePicker` / `TimePicker` / `CalendarDatePicker` 受控 `Date` / `Time` | 真 UWP 原生控件（WinUI 2 没有另做一套日期时间控件）。`Date` / `Time` 受控，写回靠 `EchoGuard`；`CalendarDatePicker.Date` **可空**（`null` = 没有值，官方给的就是可空）。区间属性（`MinYear` / `MaxYear` / `MinuteIncrement`）会把当前值夹/吸附过去，那一发罩静默窗——与 `Slider` 的 `Minimum` / `Maximum` 同形 | 同（`Controlled<Date>`） |
| `CalendarView` | 真 UWP 原生 `CalendarView`。**选中值非受控**：`SelectedDates` 是控件持有的活集合，没有回执通道能把两份状态对齐到可判定，所以只把结果（**快照**，不是那个活集合）送出去，从不往回写。配置属性只在挂载时写 | 官方未提供此路径（官方 Reactor 无月历元素），此处按"套 UWP 真控件"这条硬规则自行补齐 |
| `ToggleButton` / `RepeatButton` | 真 UWP 原生控件（住在 `Controls.Primitives`）。`ToggleButton` 受控 `IsChecked`（与 `CheckBox` 同形，回执是 `Checked` / `Unchecked` **两个**事件）；`RepeatButton` 只有 `Click`，**没有**选中态，按住会被反复回调——这是它的全部意义 | 同（XAML 的 `<ToggleButton>` / `<RepeatButton>`） |
| `ToggleSplitButton` | 真 WinUI 2 `ToggleSplitButton`：受控 `IsChecked` + `Click` + 菜单三个通道。回执是 `IsCheckedChanged`（**不是** `Checked` / `Unchecked`——它不继承 UWP 的 `ToggleButton`）。官方 `IsChecked` **不可空**，没有三态 | 同（XAML 的 `<ToggleSplitButton>`） |
| `TeachingTip` | 真 WinUI 2 `TeachingTip`：受控 `IsOpen`（轻 dismiss / 关闭按钮都是用户在改它，那一发由 `Closed` 回执，靠 `EchoGuard` 认下来）。**`Target` 填的是"同层第几个"**：官方靠 `x:Name` 拿到对象引用，声明式树里的元素没有名字，只有下标能稳定指向兄弟（与 `RelativePanel` 同一条规矩）。`Closing` 那条带 `Cancel` + `Deferral` 的通道不暴露——声明式树下没有能拦住一次关闭的地方 | 同（XAML 的 `<TeachingTip Target="{x:Bind …}"/>`；名字 → 下标是这里唯一的替换） |
| `ColorPicker` | 真 WinUI 2 `ColorPicker`：受控 `Color`，回执 `ColorChanged`。**会把颜色夹走的那一批开关罩静默窗**——关掉 `IsAlphaEnabled` 会把 A 拉到 255、换 `ColorSpectrumComponents` 会把颜色投影到新轴上，与 `DatePicker` 的 `MinYear` 同形 | 同（`Controlled<Color>`） |
| `TwoPaneView` | 真 WinUI 2 `TwoPaneView`：两个独立槽位（`Pane1` / `Pane2`，与 `SplitView` 同形、都由 handler 自己管）。**`Mode` 只出不进**：官方 `Mode` 是只读的（由可用尺寸算出），唯一出口是 `ModeChanged`——压根不可写的属性更不能装成受控。命令式的 `ToggleActiveView()` 不暴露 | 同（XAML 的 `<TwoPaneView><TwoPaneView.Pane1>…` ） |
| `RichEditBox` | 真 UWP 原生 `RichEditBox`。**文本只出不进**：官方<b>没有</b> `Text` 属性（实测赋值直接 CS1061），文本住在 `Document` 里；写进去 `abc`、读出来 `abc\r`，末尾那个 `\r` 是文档结构——剥掉它只是我们自造的归一化，控件不认这份约定，于是受控没有可判定的落点（与 `CalendarView.SelectedDates` 同款处理）。`initialText` 只在挂载时写一次 | 官方未提供受控路径（`RichEditBox` 无 `Text` 属性）；此处按"套 UWP 真控件"这条硬规则自行补齐 |
| `NavigationView`（画廊条目） | 库里早就有这个元素，画廊此前没列它——补上示例条目。选中受控、开合**不受控**（官方没给能对齐两份状态的回执通道）；设置项以 **-1** 回调 | 同 |
| `SemanticZoom` | 真 UWP 原生 `SemanticZoom`：两个槽位（`ZoomedInView` / `ZoomedOutView`）**只收实现了 `ISemanticZoomInformation` 的控件**（实务上就是 `ListView` / `GridView`），给了别的就留空并留痕、不抛。受控 `IsZoomedInViewActive`，但那一发是**异步**的（写它触发的是带动画的切换，`ViewChangeCompleted` 晚于这次调用）——按 `EchoGuard` 的既定取舍，多出来的是一次"值相同、不重渲染"的空转回调。`ToggleActiveView()` 不暴露 | 同（XAML 的 `<SemanticZoom><SemanticZoom.ZoomedInView>…` ） |
| `PipsPager` | 真 WinUI 2 `PipsPager`：受控 `SelectedPageIndex`。**属性名与事件名不对称是官方的**——属性叫 `SelectedPageIndex`，回执事件反倒叫 `SelectedIndexChanged`。改 `NumberOfPages` 会把当前页夹回范围里，那一发罩静默窗（与 `NumberBox` 的 `Minimum` / `Maximum` 同形）。它自己不装内容，页面内容由外面摆 | 同（XAML 的 `<PipsPager NumberOfPages="…"/>`） |
| `SwipeControl` | 真 WinUI 2 `SwipeControl`：四组命令（左 / 右 / 上 / 下）+ 单子内容。**模式长在「组」上**（官方 `SwipeItems.Mode` 决定滑到头直接执行还是只露出来），所以元素收的是 `SwipeItemsData`（一组 + 一个模式）而不是散装的项。命令整组重建，与 `CommandBar` 同一个取舍；图标收 `IconSource` 而不是 `IconElement`（与 `TabViewItem` 同一类槽位）。`Close()` 不暴露 | 同（XAML 的 `<SwipeControl><SwipeControl.RightItems><SwipeItems Mode="Execute">…` ） |
| `AppBarToggleButton` | 真 UWP 原生 `AppBarToggleButton`。它**继承 UWP 的 `ToggleButton`**（不像 `ToggleSplitButton` 那样是 WinUI 2 另起的炉灶），所以回执就是 `Checked` / `Unchecked`，受控写法与 `ToggleButton` 一致。命令项随命令组**整体重建**，每轮都是新实例——但"写下去会同步抛事件"没变，所以订阅仍要先挂、受控值后写，中间照旧靠 `EchoGuard`；释放点不是重写的 `Unmount` 而是 `AppBarCommands.Unmount`（组重建前统一摘一次） | 同（XAML 的 `<AppBarToggleButton …/>`） |
| `CommandBarFlyout` | 真 WinUI 2 `CommandBarFlyout`。它是 `FlyoutBase` 的子类、**不是 `UIElement`**，因此与 `MenuFlyout` 同路：元素是描述、由 `FlyoutSlot` 就地物化。挂法两种——按钮的 `Flyout` 槽位（点开）或 `ContextMenu` 修饰器（右键 / 长按）。两组命令（`PrimaryCommands` / `SecondaryCommands`）与 `CommandBar` 完全同形，所以装的是同一批 `AppBarButton` / `AppBarSeparator` / `AppBarToggleButton`。**普通的 `Flyout`（任意内容那一种）仍没收**：它的 `Content` 是一棵子树，要在协调器之外另管挂载 / patch / 卸载三件事，而 `MenuFlyout` 能就地物化靠的是"项只有文本 / 图标 / 回调、没有跨帧状态" | 同（XAML 的 `<UIElement.ContextFlyout><CommandBarFlyout>…`） |
| `Flyout`（内容型） | 真 UWP 原生 `Flyout`（XAML 里 `<Button.Flyout><Flyout>…</Flyout>` 那一种）：里面装的是**一棵子树**，不是"项"。因此它**不像 `MenuFlyout` 那样整体重建**，而是走进 `Reconciler` 的 `Build` / `Patch` / `UnmountNative` 三条路——整体重建会把输入框里的光标每轮抹掉一次。代价是"卸载"多了一个入口：浮出层的内容**不在可视树里**（不在 `Content` / `Children` 上），`UnmountTree` 递归不到，所以 `ContextFlyout` 由 `UnmountNode` 统一收、按钮的 `Flyout` 槽位由各 handler 的 `Unmount` 收。**没有受控 `IsOpen`**：打开动作是 `ShowAt(目标)`，而目标正是挂它的那个按钮，框架替你做了，元素上没有这个槽位可填 | 同（XAML 的 `<Button.Flyout><Flyout><StackPanel>…</StackPanel></Flyout>`） |
| `ToggleMenuFlyoutItem` / `RadioMenuFlyoutItem` | 真控件，且**官方自己就把它们分在两处**：前者在 `Windows.UI.Xaml.Controls`（UWP 原生），后者在 `Microsoft.UI.Xaml.Controls`（WinUI 2），**后者不继承前者**（实测互相赋值编译不过）。两者都**只有 `Click` 一个回执通道**——没有 `Checked` / `Unchecked`、也没有 `IsCheckedChanged`，值是回读出来的，所以"我们写的"与"用户点的"仍然要靠 `EchoGuard` 分。受控 `IsChecked`；两者**共用一个 `Consume`**（一个站点一份判据）。`RadioMenuFlyoutItem` 的 `GroupName` 只管同组互斥、不管选中，所以会**逐项**回调过来 | 同（XAML 的 `<ToggleMenuFlyoutItem>` / `<muxc:RadioMenuFlyoutItem GroupName="…"/>`） |
| `ParallaxView` | 真 WinUI 2 `ParallaxView`：参照另一处滚动的进度把自己错开一点。它要**两个**东西——`Child`（被错开的内容，唯一子槽位）与 `Source`（参照谁的滚动，通常是**兄弟**那个 `ScrollViewer`）。声明式树没有 `x:Name`，所以 `Source` 填**同层下标**，与 `TeachingTip.Target` / `RelativePanel` 贴兄弟同一条规矩。**没有受控属性**：能被用户改的只有"滚动到哪儿"，而那是 `Source` 的状态。下标指错不会崩——取不到滚动宿主就是安静地不动，与"越界给 null"同款处理 | 同（XAML 的 `<ParallaxView Source="{x:Bind …}"/>`；名字 → 下标是唯一替换） |
| `SymbolIcon` | 真 UWP 原生 `SymbolIcon`（落到 `IconSource` 槽位时翻成 WinUI 2 的 `SymbolIconSource`）。它和 `FontIcon` 是**同一件事的两种写法**：官方 `Symbol` 枚举就是"Segoe MDL2 Assets 里那批常用码位"的具名清单 | 同（XAML 的 `<SymbolIcon Symbol="Copy"/>`） |
| `MenuFlyoutSubItem`（子菜单） | 真 UWP 原生 `MenuFlyoutSubItem`。官方继承的是 `MenuFlyoutItemBase` **而不是** `MenuFlyoutItem`（实测：互相赋值编译不过），所以有 `Text` / `Icon` / `IsEnabled`，**没有** `KeyboardAcceleratorTextOverride`——元素上因此也不给这个参数（给了不生效的旋钮比不给更糟）。它收的是与菜单同一个类型，于是能**一层层往下嵌**；代价是 `Unmount` 要顺着子菜单递归摘回声登记（`MenuFlyouts.ForgetNested`）：可勾选项挂在里层时同样是"写完在等回执"的站点 | 同（XAML 的 `<MenuFlyoutSubItem Text="…"><MenuFlyoutItem …/></MenuFlyoutSubItem>`） |
| `TextCommandBarFlyout` | 真 WinUI 2 `TextCommandBarFlyout`，`CommandBarFlyout` 的**子类**：两组命令与 `AlwaysExpanded` 与它同形，多出来的只有"**它认得文本控件**"——剪贴板那几条命令由控件自己按选区状态填并改可用状态，手填一份就丢了这份联动。它挂的槽位是官方给文本控件单独留的 `SelectionFlyout`（**不是**右键那个 `ContextFlyout`），本库对应 `.SelectionFlyout(...)` 修饰器；该属性不在 `UIElement` 上，给别的控件写会留痕并忽略 | 同（XAML 的 `<TextBox.SelectionFlyout><muxc:TextCommandBarFlyout/></TextBox.SelectionFlyout>`） |
| `ImageIcon` | 真 WinUI 2 `ImageIcon`（落到 `IconSource` 槽位时翻成 `ImageIconSource`）。与 UWP 原生的 `BitmapIcon` 是**两个控件**：后者 1:1 画 + `ShowAsMonochrome` 单色化，**设尺寸是裁不是缩放**；前者内部是一个 `Image`，**按尺寸缩放 + 画原色**。要"同一张图放进不同大小的槽位"用它 | 同（XAML 的 `<muxc:ImageIcon Source="…"/>`） |
| `ToolTip` | 真 UWP 原生 `ToolTip`，挂在宿主的附加属性 `ToolTipService.ToolTip` 上（**不进可视树**，与 `ContextMenu` 同形）。两条入口对应官方 XAML 的**两种写法**：`.ToolTip("文本")` = 特性语法 `ToolTipService.ToolTip="…"`，`.ToolTip(元素)` = 属性元素语法 `<ToolTipService.ToolTip>…子树…</ToolTipService.ToolTip>`。**没有 `IsOpen`**：开合由指针与焦点驱动，声明式写 `true` 会每帧"拉开—收起"（与 `TeachingTip` 的受控 `IsOpen` 是两回事）。内容型气泡走协调器的 Build / Patch / 卸载，卸载入口在 `Reconciler.UnmountNode` | 同（XAML 的 `<ToolTipService.ToolTip>`；`Placement` 取官方 `PlacementMode`） |

已经修掉的那几处留着当反面教材（注释里都写了实证）：面包屑曾用自绘
`StackPanel` + "›"（→ 现为真 `BreadcrumbBar` + `ItemTemplate`）；条目外观曾"把字号设到
`BreadcrumbBar` 上指望继承"（→ `BreadcrumbBarItem` 默认样式硬设了字号，继承链断）；
向量里曾直接塞 `UIElement`（→ 0x800F1000，UIElement 天生要占树上一个位置，当不了数据）。

```csharp
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

## 仓库里有什么

| 路径 | 是什么 |
|---|---|
| `Reactor.uwp/` | 框架本体，就是发布出去的那个包 |
| `Reactor.Uwp.Native/` | C++/WinRT 原生桥（给 `ItemsRepeater` 补 `IElementFactory`），含 x64 / arm64 预编译产物，`build.bat` 可重建 |
| `UwpApp/` | 测试壳：压测 M0~M5、虚拟列表 / Echo 实验室、CoreLoop 回归、元素画廊，一次部署点菜单跑完 |
| `tests/` | 控制台用例（EchoGuard、虚拟列表身份、Hook、Context），不需要开 App |
| `diag-run.ps1` | 无人值守压测：构建 → 同步产物 → 启动 → 等本轮跑完 → 打印 summary |
| `samples/` | 示例：`Reactor.Template`（起点模板，走 `PackageReference`）+ `Reactor.Gallery`（9 个主题页，走 `ProjectReference`）——两者引用框架的方式刻意不同，理由与代价见 `samples/README.md` |
| `tools/` | 辅助脚本 |

## 跑起来

环境：.NET 10 SDK、Windows 10 SDK `10.0.26100`、x64。
UWP 项目不能像普通控制台那样直接跑，要用 Visual Studio 打开
`ReacrorForUWP.slnx`，把 `UwpApp`（测试壳）或 `samples` 下的
`Reactor.Template` / `Reactor.Gallery` 设为启动项目部署运行（F5）。

```bash
# 只构建
dotnet build UwpApp/UwpApp.csproj -c Debug -p:Platform=x64
dotnet build samples/Reactor.Template/Reactor.Template.csproj -c Debug -p:Platform=x64
dotnet build samples/Reactor.Gallery/Reactor.Gallery.csproj -c Debug -p:Platform=x64

# 打框架包（别带 -p:Platform：会把托管程序集编成 x64 专属，arm64 消费方报 CS8012）
dotnet pack Reactor.uwp/Reactor.uwp.csproj -c Release
```

## 发布必须是 AOT

**这个项目的交付形态是原生 AOT，不是 IL。** `UwpApp` 与两个示例都开着
`<PublishAot>true</PublishAot>` + `<DisableRuntimeMarshalling>true</DisableRuntimeMarshalling>`，
所以任何"靠反射 / 运行时代码生成 / 动态加载程序集"的写法都会<b>在构建期</b>炸，
而不是等用户点上去才炸——这正是坚持 AOT 的理由。

```bash
dotnet publish UwpApp/UwpApp.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64
dotnet publish samples/Reactor.Template/Reactor.Template.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64
```

产物是单个原生 exe（本机 x64 实测 10.9 MB，`PE machine = 0x8664`，
同目录<b>没有</b>托管 `UwpApp.dll`——有就说明没编成原生）。
真要反射请显式加 `rd.xml` 或 `[DynamicallyAccessedMembers]`，别关 AOT。

已部署过一次之后，压测可以无人值守：

```powershell
.\diag-run.ps1 -Mode 1     # 0~5 = 压测 M0~M5，6/7 = 两个实验室，见 UwpApp\TestShell.cs
```

每轮的 `manifest / config / events.ndjson / summary.json` 归档在
`%LOCALAPPDATA%\Packages\<包>\LocalState\ReactorRuns\<runId>\`。

## 测试怎么跑

- **不开 App 的那部分**：`dotnet run --project tests/Reactor.Core.Tests -c Release`
- **要眼睛过一遍的那部分**：部署 `UwpApp`，左侧菜单直接切页。压测页会把
  ItemsRepeater 内嵌在页面里跑，页面上有进度行和「重跑 / 停止」

## 状态更新是批处理的（setState 不同步生效）

一轮消息泵内连发 N 次 `setState` **只渲染一次**：请求先排队，
排到当前调用栈结束之后才跑。这是 React / 官方 Reactor 的语义，
也是"一个事件处理里改三个状态"不付三倍代价的原因。

代价是：**`setState` 之后立刻读 UI 状态，读到的还是旧值**。
要读新值就在渲染之后读（`UseEffect` 里）。

两条重渲染路径各有一把独立的锁（`RenderBatcher`）：
宿主那把管整棵树，每个组件节点自己的那把管它的子树——
子组件的粒度更细，不能共用宿主的排队状态，否则宿主一轮渲染会把
子组件的更新并掉。

## 已知限制

- **只支持 x64 / arm64**：原生桥 `Reactor.Uwp.Native.dll` 有这两个架构的预编译产物
  （`build.bat` / `build.bat arm64` 可重建），**不做 x86**
- **WinUI 2 的能力边界就是本框架的边界**：它没封装的控件走 `Native()` 逃生舱或自己补。
  已暴露的元素见 `Reactor.uwp/Elements/Factories*.cs`；
  仍缺的（如 `MediaPlayerElement` / `AnimatedIcon` / `WebView`）随时可照现有元素加，
  一个元素 = 一个 record + 一个工厂 + 一个 handler + 一行注册。
  两条都各有各的卡点，都不是"照抄一遍 API"就能了事：
  `MediaPlayerElement` 卡在**素材**上而不在 API 上——`Source` 是
  `IMediaPlaybackSource`（要 `MediaSource.CreateFromUri`），画廊里没有可播的素材，
  没有素材的示例页等于开一个永远空白的播放器；
  `AnimatedIcon` 的 `Source` 是 `IAnimatedVisualSource2`（Lottie 生成的类），
  没有源时它只剩 `FallbackIconSource` 那一层静态图标，示例等于开一个不会动的图标
  `RichEditBox` 已加但<b>文本只出不进</b>（它官方没有 `Text` 属性，见上表）——
  要把它做成受控，得先定"回读值与写入值怎么算相等"这件事，别在没定之前动手
- **官方画廊里有些属性是 WinUI 3 才有的，本库一律不装**（装一个"能写但官方没有"
  的旋钮，只会让人以为它该生效）。已实测的两处：
  `TextBox` 的**清除按钮**（`ClearButtonVisibility`）——UWP 的
  `Windows.Foundation.UniversalApiContract.winmd` 里 grep 不到
  `ClearButtonVisibility` / `TextBoxClearButtonVisibility` 这两个名字；
  `ToolTip` 的 `IsOpen` 官方倒是有，但气泡开合由指针与焦点驱动，
  声明式写 `true` 会每帧"拉开—收起"，所以也不暴露
- **另有几条"属性存在、但装了也不动"的，同样不装**（实测类型都在，缺的是配套）：
  `RatingControl` 的 `ItemInfo`（`RatingItemInfo`）要用它就得先新增一种元素类型，
  而它只在"每颗星单独设内容"时才用得上；
  `TreeView` 的 `CanDragItems` / `CanReorderItems` 要配套 `AllowDrop`
  （本库目前没有这个修饰器），只给这两个开关等于给一对不会动的旋钮。
  两条都留在 `Native()` 那条路上
- **`NavigationView` 的两个响应式阈值不给默认值**：`CompactModeThresholdWidth` /
  `ExpandedModeThresholdWidth` 在元素上是 `double?`，`null` = 不写（用控件自己的
  默认）。那两个数字是官方响应式断点的一部分、会随模板与版本调整，
  抄一个具体值进来等于把可能过期的数字当成契约
- **`SplitView.PaneBackground` 收的是 `Brush` 实例，不是元素**：要亚克力给
  `AcrylicBrush(...)`，要纯色给 `new SolidColorBrush(...)`（元素那一侧没有
  "刷子"这一种节点，它是画笔工厂产出来的实例，与官方 XAML 里
  `PaneBackground="{StaticResource …}"` 指向一个刷子资源是同一件事）
- **`x:Uid` 只覆盖"有本地化意义"的那几个属性**：`TextBlock.Text`、
  `TextBox` 的 `Text` / `Header` / `PlaceholderText`、
  `ContentControl.Content`、`ToolTip`、`AutomationProperties.Name`
  （清单在 `Internal/Localization.cs`）。XAML 编译器是<b>照 resw 里写了什么</b>
  生成赋值，纯代码没有那份清单，只能按类型试；需要别的属性时用 `Native()`
- **切语言会重建整棵树**：`x:Uid` 只在挂载时解析（对齐 XAML 编译器生成的
  初始化代码），所以语言变了必须重新 `Build` 一遍，光重渲染没用。
  宿主已接好 `ResourceContext.QualifierValues` 的 `Language` 变化，
  会自动重建；代价是焦点会丢
- API 尚未稳定；xml 文档还没有
- **测试集里没有一项碰真实 XAML 控件**（全是纯逻辑），真控件行为的回归靠
  `UwpApp` 手跑 + 每次改动后 AOT 发布一次

## 发布

包通过 GitHub Actions 走 nuget.org 的 **Trusted Publishing**（OIDC），
仓库里不存 API key。工作流在 `.github/workflows/publish.yml`，
手动触发或推 `v*` tag，详情见 `Reactor.uwp/README.md`。

## 许可

MIT，见 [LICENSE](LICENSE)。
