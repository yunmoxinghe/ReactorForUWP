# 示例集 ↔ WinUI 3 Gallery 逐条对齐台账

这份台账回答一个问题：**画廊里每一处渲染差在哪里、照官方该怎么改、哪些改不了。**

生成方式见文末「怎么复现」。所有结论都对 source，没有凭印象写。

## 0. 基准从哪来

官方每个控件目录下有两种源码，**只认第一种**：

| 文件 | 是什么 | 能不能当基准 |
|---|---|---|
| `<Name>Page.xaml` 里 `ControlExample` 的示例段 | 真正跑起来的那棵树，含 `Width/Height/Spacing/Margin/Style` 等一切影响像素的属性 | ✅ 就是它 |
| `<Name>/*.txt` | 页面下方「源码展示」用的**简化片段**，故意省掉尺寸与排版 | ❌ 照它对齐必然偏 |

本仓库 80 个示例 ↔ 官方 121 个控件。

## 0.5 进度（边改边删 §2 里对应的行）

**已落地的第一批（按钮与输入组，10 个文件）**：

| 文件 | 改了什么 |
|---|---|
| `ButtonVariants.cs` | 外层间距 8 → **16**；补弱化档 `.Subtle()`；补官方"自动折行"档（`MaxWidth 240` + `WrapWholeWords`） |
| `ComboBoxBasic.cs` | 两处宽度 220 → **200** |
| `SliderBasic.cs` | 三处宽度 260 → **200**；竖直档 `.Height(140)` → `.Size(100, 100)` 并补两侧刻度（`tickFrequency 10 / Outside`） |
| `SliderTicks.cs` | 两条主 Slider 补 Width **290** |
| `AutoSuggestBasic.cs` | 三个框补 Width **300**，后两个补 `HAlign(Left)` |
| `RatingBasic.cs` | 五档各补 `HAlign(Left)`（默认 Stretch 会把星级拉宽） |
| `CheckBoxBasic.cs` | 子项说明 `.Padding(20,0,0,0)` → `.Margin(24,0,0,0)` |
| `ToggleSwitchBasic.cs` | 补官方第二例形态：开关 + `ProgressRing`（宽 32、`IsActive` 跟着开关走） |
| `RepeatButtonBasic.cs` | 读数从下方挪进按钮那一行的 `HStack`，`Margin(8,0,0,0)` + 垂直居中 |
| `DropDownButtonBasic.cs` | 补官方第二例：纯图标内容 + `AutomationName`（没文字就没名字） |

**第二批（文本与状态组，9 个文件）**：

| 文件 | 改了什么 |
|---|---|
| `PasswordBoxBasic.cs` | 主框补 `Width(300)`、次框 `Width(250)` + 右 `Margin(8)` |
| `ProgressBarBasic.cs` | 三根补 `Width(130)`；不确定档 `.Margin(10,10,0,0).VAlign(Top)`；读数行 `.Width(60).TextAlignment(Center)` |
| `ProgressRingBasic.cs` | 64×64 → `Size(60,60)`；不确定档同上 Margin/对齐；第三档右 `Margin(60)` |
| `RichEditBoxBasic.cs` | `.Height(160)` → `.Size(800, 200)` |
| `TimePickerBasic.cs` | 外层 `VStack(14)` → `VStack(8)`；补不带 header 的裸档 |
| `InfoBadgeBasic.cs` | 两行 `HStack(12)` → `HStack(20)` + 居中；补 Attention 三形态；补 Button 宿主 + 右上角徽章 |
| `DatePickerBasic.cs` | 补 `yearVisible: false` 档 |
| `TextBlockStyles.cs` | 补裸 `TextBlock` 档；补 `FontSize(24) + CharacterSpacing(200)` + CornflowerBlue 折行档 |
| `RichTextBasic.cs` | 补单段 `Paragraph` 的裸 `RichTextBlock` 档 |
| `ToolTipBasic.cs` | 补 TextBlock 宿主 + `verticalOffset: -80` 档 |

> `PasswordBox` 的「CheckBox 切换明文」那档**没做**：框架没有 `PasswordRevealMode`，
> `PasswordBoxElement` 只有 `PasswordChar`，`IsPasswordRevealButtonEnabled` 既无工厂形参也无修饰器。

**第三批（集合与导航组，18 个文件）**：

| 文件 | 改了什么 |
|---|---|
| `ListViewBasic.cs` / `ScrollViewerBasic.cs` | `Size(350,400)` / `Size(400,266)`（后者正文里"限高 160"的文案同步改掉） |
| `ListBoxBasic.cs` | 三档统一 `Width(400)`，高度保留（一页塞三档） |
| `TreeViewBasic.cs` | `Height(220)` → `MinWidth(345).MaxHeight(400)` + `.Margin(0,12,0,0)` 居中置顶 |
| `SemanticZoomBasic.cs` / `NavigationViewBasic.cs` | `Height(500)` / `Height(460)` |
| `PipsPagerBasic.cs` / `TeachingTipBasic.cs` | `.Margin(0,12,0,0)` / child `.Margin(0,16,0,0)` |
| `RefreshContainerBasic.cs` | 容器居中、内层 `MinWidth(200)` |
| `PivotBasic.cs` / `FlipViewBasic.cs` | `MinHeight(400)` / `.MaxWidth(400)` |
| `GridViewBasic.cs` / `TabViewBasic.cs` | 项 `.Margin(5)` / 补 `isAddTabButtonVisible: true` |
| `ContentDialogBasic.cs` | 三按钮与读数挪进 `HStack` 同排 |
| `FlyoutBasic.cs` / `PopupBasic.cs` | `VStack(10)`→`12`、首行 `.BaseText()` / `.Width(280)`→`.MinWidth(240)` + 标题 `FontSize(16)` |
| `MenuBarBasic.cs` / `MenuFlyoutBasic.cs` | 补 `RadioMenuItem` 第二组；MenuBar 补真实 `KeyboardAccelerator`（Ctrl+N / Ctrl+S） |

> `MenuFlyoutBasic` 的**菜单项**快捷键**没做**，不是缺 API 而是那条路不吃它：
> `Handlers.Menus.cs` 物化菜单项时只读 `Text/Icon/AcceleratorText/IsEnabled`，不读
> `ElementModifiers`（`MenuFlyoutItem` 不是 `UIElement`，`InputApplier` 不跑）。
> 挂上去能编译、永不触发；要真演示得给一个可聚焦宿主，属改结构，留作后续。

**第四批（布局与样式组，18 个文件）**：

| 文件 | 改了什么 |
|---|---|
| `WrapGridBasic.cs` | Item 44×44、`MaximumRowsOrColumns` 3、容器 `Width(400)` |
| `RelativePanelBasic.cs` | 容器 `Width(300)`、子项 `Size(50,50)` 与两条 Margin；五条关系说明挪成图例行 |
| `CanvasBasic.cs` | 画布 `Size(140,140)`、块 `Size(40,40)`、坐标 (20,20)/(40,40)/(60,60)、ZIndex 1/2/3 |
| `ImageBasic.cs` / `ExpanderBasic.cs` | 100×100 / `isExpanded` 初值 false + 例 2 `Width(500).Padding(0)` |
| `SwipeControlBasic.cs` / `SplitViewBasic.cs` | `Width(500).Height(68).Margin(12)` / `Height(300)` + `OpenPaneLength(320)` + 初值 `CompactOverlay` |
| `ParallaxViewBasic.cs` | `verticalShift` 500、补 Header（`MaxWidth(280) FontSize(28)` 白字居中） |
| `CommandBarBasic.cs` / `IconsBasic.cs` | 图标改 `Symbol.Add/Edit/Delete/Share/Setting` / `FontIcon` 补 `fontFamily` 且各包一层 Button |
| `LayoutGrid.cs` / `BorderBasic.cs` | 补官方 3×3 定长色块矩阵 / 补白底金边 2px + `Margin(8,5).FontSize(18)` 档 |
| `StackPanelBasic.cs` / `ViewboxBasic.cs` | 第一段改 Slider 驱动 spacing + 四个 40×40 Rectangle / 尺寸接 Slider（40–360）+ 补复合内容档 |
| `AcrylicBrushBasic.cs` | 底图换官方三件套（100×200 Aqua / 152×152 Magenta / 80×100 Yellow，容器高 200、`MinWidth(320)`） |
| `RadialGradientBrushBasic.cs` | OffCenter 档补齐官方六项参数，容器 `Size(200,200)` |
| `ShapesBasic.cs` / `LineBasic.cs` | 补 SteelBlue 填充（Line 只有描边）+ 黑描边基础档 |

> `LineBasic` 的 "SteelBlue 填充"**不成立**：框架刻意没给 `LineElement` 装 `Fill`
> （见 `ElementExtensions.Shapes.cs` 注释），只补了 SteelBlue **描边**那一档。

## 0.6 不能编译，怎么保证改对了

示例 app 是打包 UWP（有 AppX），**原地 `dotnet build` 会清掉 VS 生成的 MSIX 布局**，
app 会起不来。所以这轮用三件替代品把关，外加一次隔离真编译：

| 手段 | 抓什么 | 抓不住什么 |
|---|---|---|
| `tools/parity/check_brackets.py` | 少/多括号（跳字符串与注释后数配对） | 语法以外的任何问题 |
| `tools/parity/check_api.py` | 方法名不存在、拼错 | 参数类型、参数个数 |
| `tools/parity/check_params.py` | 命名参数 `foo:` 拼错（链式 API 最常见的暗雷） | 类型不匹配 |
| **复制工程到临时目录编译** | 真的类型检查 | — |

最后那条是关键兜底：把 Gallery 整个工程复制到 `%TEMP%\reactor-parity-build`，
把 `<ProjectReference>` 改成框架工程的绝对路径后 `dotnet build`。改的是副本，
原地那份 AppX 一个字节都不动，于是"能编译"和"app 还能跑"不冲突。

静态脚本的两个坑（都踩过）：符号提取必须**按行**做（整文件一把梭会被前一个
匹配横跨吞掉 `Caption` 这种紧凑扩展方法）；注释里的中文说明（`DownOnly 只缩小`）
和三元表达式的 `? a : c` 都带冒号，会被误判成命名参数，要先剔掉。

复制也不需要每次手敲，固化为脚本：

```bash
python tools/parity/build_probe.py     # 复制到 %TEMP% 后编译
```

它把 csproj 里那条相对路径的项目引用改成绝对路径——注意**要用正向斜杠**写路径，
XML 里的反斜杠会在 Python 字符串、sed、shell 一层层传递中被吃掉（`D:\fluentapps`
里的 `\f` 会被当成换页符，MSBuild 报 "hexadecimal value 0x0C is invalid"）。

**当前结论**：152 条里已落地的部分全部通过上述三重静态检查，
且 `build_probe.py` 的隔离编译结果为 **0 警告 0 错误**（含全部 80 个示例文件）。

**第五批（输入组剩余档位，6 个文件）**：

| 文件 | 补了什么 |
|---|---|
| `NumberBoxOptions.cs` | 补 SpinButton 档：`smallChange: 10` / `largeChange: 100` / `spinButtonPlacementMode: Compact` |
| `ToggleSplitButtonBasic.cs` | 补纯图标内容档 `SymbolIcon(Symbol.List)`，并手挂 `AutomationName`（内容不是字符串时没名字可读屏只剩"按钮"） |
| `AppBarButtonBasic.cs` | 补 `FontIcon("Σ", fontFamily: "Candara")` 档；补挂 Ctrl+S 快捷键档 |
| `AppBarToggleButtonBasic.cs` | 补 `FontIcon("Σ", fontFamily: "Candara")` 受控档 |
| `HyperlinkBasic.cs` | 第一个链接补 `navigateUri: "https://www.microsoft.com"`（原先文案与实现不符的那处） |
| `TextBoxOptions.cs` | 多行档补 `acceptsReturn: true`（**原来只有文案说支持多行、代码没给**）+ `.MinWidth(400)` |

> 已知偏差，`AppBarButton` 上那条 Ctrl+S **不会真正生效**：命令项走
> `Handlers.Shell.CreateButton` 就地物化，那段只读 `Label/Icon/IsEnabled/OnClick`，
> 不读元素上的 input modifiers。代码注释里已写明，修它得动框架，本轮不动。

**这一批的通行做法**（后面几组照此办理）：
改动前先在 `Reactor.uwp/Elements/` 里确认 API 真的存在；改完跑
`python tools/parity/check_brackets.py`（80 个示例文件的括号配平冒烟）。
**示例工程不能用 CLI build**——`dotnet build` 的增量清理会删掉 VS 生成的 MSIX，
app 会起不来；验收请在 VS 里 Ctrl+Shift+B。

## 1. 总数

| 组 | 本地文件 | 官方控件 | 可对齐 | 不适用 | 缺素材 |
|---|---|---|---|---|---|
| 按钮与输入 | 20 | 18 | 21 | 10 | 3 |
| 文本与状态 | 20 | 14 | 28 | 19 | 1 |
| 集合与导航 | 20 | 18 | 46 | 26 | 6 |
| 布局与样式 | 20 | 20 | 57 | 22 | 8 |
| **合计** | **80** | — | **152** | **77** | **18** |

## 2. 可直接对齐的 152 条（按类型分）

### 2.1 尺寸数字（最多、最机械、风险最低）

| 本地文件 | 现在 | 官方 |
|---|---|---|
| `ComboBoxBasic.cs` | `.Width(220)` ×2 | `Width="200"` |
| `SliderBasic.cs` | `.Width(260)` ×3 | `Width="200"` |
| `SliderTicks.cs` | 无宽度 | `Width="290"` |
| `AutoSuggestBasic.cs` | 无宽度 | `Width="300"` |
| `PasswordBoxBasic.cs` | 无宽度 | `Width="300"`（第二条 250 + `Margin(right: 8)`） |
| `ListViewBasic.cs` | 撑满 | `Width="350" Height="400"` |
| `ListBoxBasic.cs` | 150/140/140 | `Width="400"`（高度保留：一页塞三档） |
| `TreeViewBasic.cs` | `.Height(220)` | `MinWidth="345" MaxHeight="400"` |
| `ScrollViewerBasic.cs` | `.Height(160)` | `Width="400" Height="266"` |
| `SemanticZoomBasic.cs` | `.Height(240)` | `Height="500"` |
| `ProgressBarBasic.cs` | 无宽度 | `Width="130"` |
| `ProgressRingBasic.cs` | 64×64 | 60×60 |
| `RichEditBoxBasic.cs` | `.Height(160)` | `Width="800" Height="200"` |
| `ImageBasic.cs` | 120×120 | `Width="100" Height="100"` |
| `WrapGridBasic.cs` | Item 72 / 默认 4 列 | `ItemWidth=ItemHeight="44"`、`MaximumRowsOrColumns="3"`、`Width="400"` |
| `RelativePanelBasic.cs` | 无宽度、子项随内容 | `.Width(300)`、子项 `.Size(50,50)` |
| `CanvasBasic.cs` | `.Height(150)`、块 80×80 | `140×140`、块 `40×40` |
| `NavigationViewBasic.cs` | `.Height(260)` | `Height="460"` |
| `PivotBasic.cs` | `.MinHeight(210)` | `MinHeight="400"` |
| `SwipeControlBasic.cs` | 无 | `.Width(500).Height(68).Margin(12)` |
| `SplitViewBasic.cs` | `.Height(180)`/200 | `Height="300"`、`OpenPaneLength=320` |
| `ParallaxViewBasic.cs` | `verticalShift` 120（上限 240） | `VerticalShift="500"` |
| `ViewboxBasic.cs` | 固定 150×84 | 接一个 Slider 驱动尺寸 |
| `StackPanelBasic.cs` | 固定 8/12/20 | 第一段接 Slider 驱动 spacing |
| `FlipViewBasic.cs` | 无 | `.MaxWidth(400)`（180 已对齐） |

### 2.2 间距 / 内外边距 / 对齐

| 本地文件 | 现在 | 官方 |
|---|---|---|
| `ButtonVariants.cs` | `HStack(8)` | `Spacing="16"` |
| `CheckBoxBasic.cs` | 说明行 `.Padding(20,0,0,0)` | `.Margin(24,0,0,0)` |
| `TimePickerBasic.cs` | `VStack(14)` | `Spacing="8"` |
| `InfoBadgeBasic.cs` | `HStack(12)`、无居中 | `Spacing="20"` + `.HAlign(Center)` |
| `RepeatButtonBasic.cs` | 计数在下方 | 挪进 `HStack` + `.Margin(8,0,0,0).VAlign(Center)` |
| `Grid 组多个文件` | 无边框 | `WithBorder(TextControlBorderBrush)` 等（key 需确认） |
| `ProgressBarBasic.cs` | 无 | `.Margin(10,10,0,0).VAlign(Top)`；输出 `.Width(60).TextAlignment(Center)` |
| `ProgressRingBasic.cs` | 无 | 不确定 `.Margin(10,10,0,0).VAlign(Top)`；确定 `.Margin(right: 60)` |
| `TreeViewBasic.cs` | 无 | `.Margin(0,12,0,0).HAlign(Center).VAlign(Top)` |
| `PipsPagerBasic.cs` | 只有居中 | `.Margin(0,12,0,0)` |
| `RefreshContainerBasic.cs` | 无 | `.HAlign(Center).VAlign(Center)`、内层 `.MinWidth(200)` |
| `RatingBasic.cs` | 无 | 各档 `.HAlign(Left)` |
| `ContentDialogBasic.cs` | `VStack(10)` 三按钮竖排 | 按钮 + 读数改 `HStack` 同排 |
| `FlyoutBasic.cs` | `VStack(10)` | `VStack(12)`；首行 `.BaseText()` 而非 `.Caption()` |
| `PopupBasic.cs` | `.Width(280)` | `.MinWidth(240)`；标题 `.FontSize(16)` |
| `TeachingTipBasic.cs` | child 无 Margin | `.Margin(0,16,0,0)` |
| `CanvasBasic.cs` | 坐标随意 | (20,20)/(40,40)/(60,60)、ZIndex 1/2/3 |
| `RelativePanelBasic.cs` | 无 | `.Margin(8,0,0,0)` / `.Margin(0,8,0,0)` |
| `ExpanderBasic.cs` | `isExpanded: true` | `false`；例 2 补 `.Width(500).Padding(0)` |

### 2.3 补档（官方有、本地完全没有形态）

- `ButtonVariants.cs`：补 `.Subtle()` 档；补换行档（`MaxWidth(240)` + `WrapWholeWords`）
- `ToggleSwitchBasic.cs`：补「开关 + `ProgressRing` Width 32」横排档
- `NumberBoxOptions.cs`：补 SpinButton 档（`smallChange: 10 / largeChange: 100 / spinButtonPlacementMode: Compact`）
- `DropDownButtonBasic.cs`：补纯图标内容档
- `ToggleSplitButtonBasic.cs`：补纯图标内容档（`SymbolIcon(Symbol.List)`）
- `AppBarButtonBasic.cs` / `AppBarToggleButtonBasic.cs`：补 `FontIcon("Σ", fontFamily: "Candara")` 档；前者再补 Ctrl+S 快捷键档
- `HyperlinkBasic.cs`：第一个补 `navigateUri`（官方 `https://www.microsoft.com`）
- `PasswordBoxBasic.cs`：补「密码框 + CheckBox 显示密码」横排档
- `DatePickerBasic.cs`：补 `yearVisible: false` 档
- `TextBlockStyles.cs`：补裸 `TextBlock("I am a TextBlock.")`；补 `CharacterSpacing(200) FontSize(24) CornflowerBlue + WrapWholeWords` 档
- `TimePickerBasic.cs`：补不带 header 的裸 TimePicker
- `RichTextBasic.cs`：补单段 `Paragraph("I am a RichTextBlock.")`
- `InfoBadgeBasic.cs`：补 Attention 档三形态；补 `Button` 宿主 + 右上角图标徽章（`Background #C42B1C`）
- `ToolTipBasic.cs`：补 TextBlock 宿主 + `verticalOffset: -80` 档
- `LayoutGrid.cs`：补官方 3×3 定长色块矩阵（240×160 Gray）
- `BorderBasic.cs`：补官方写死色那一段（白底 + 金边 2px + 内文 `Margin(8,5) FontSize(18)`）
- `StackPanelBasic.cs`：补四个 40×40 纯色 Rectangle
- `ViewboxBasic.cs`：补官方那套复合内容
- `ShapesBasic.cs` / `LineBasic.cs`：补 SteelBlue 描黑边的基础档
- `AcrylicBrushBasic.cs`：改官方三件套几何（100×200 Aqua / 152×152 Magenta / 80×100 Yellow，容器 200 高、MinWidth 320）
- `RadialGradientBrushBasic.cs`：OffCenter 档改成官方完整参数，`Size(200,200)`
- `ParallaxViewBasic.cs`：补 Header 文本 `MaxWidth(280) FontSize(28) 白色居中`
- `GridViewBasic.cs`：项加 `.Margin(5)`
- `TabViewBasic.cs`：第一个补 `isAddTabButtonVisible: true`
- `TextBoxOptions.cs`：补 `acceptsReturn: true`（现在文案说有、代码没给）、`.MinWidth(400)`
- `IconsBasic.cs`：`FontIcon` 补 `fontFamily: "Segoe MDL2 Assets"`；五个图标各包一层 Button
- `MenuFlyoutBasic.cs` / `MenuBarBasic.cs`：补 `RadioMenuItem` 第二组；真实 `KeyboardAccelerator`
- `CommandBarBasic.cs`：图标改 `SymbolIcon(Symbol.Add/Edit/Share/Setting)`
- `SplitViewBasic.cs`：初值改 `CompactOverlay`，标题文本与 Margin 对齐官方

## 3. 不适用（77 条）——改不了，写下来免得下次再查一遍

### 3.1 框架尚未暴露的属性（要动框架才谈得上对齐）

> **本轮已补掉一批，见 §5.6**：`ComboBox.Header` / `PlaceholderText`、`RadioButtons.Header`、
> `InfoBar.Title` / `IsOpen` / `IsClosable`。下面这些仍然没暴露。

`RadioButtons.MaxColumns`、`ColorPicker.IsAlphaTextInputVisible` / `IsColorChannelTextInputVisible`、
`NumberBox.PlaceholderText`、`ToolTip.PlacementRect`、
`TextBox` 的字体族与 `FontStyle` / `SelectionHighlightColor` / `IsTextSelectionEnabled` / `ClearButtonVisibility`、
`PasswordBox.PasswordRevealMode`、`DatePicker.DayFormat`、`CalendarView.IsGroupLabelVisible` / `IsOutOfScopeEnabled`、
`AppBarButton.Flyout` 槽位、`TeachingTip.IconSource` / `PlacementMargin`、`Expander` 的内容对齐、
`ProgressBar` / `ListView` 的 `ItemsPanel` / `GroupStyle` / `ItemTemplateSelector`、
`MenuFlyout.Placement`、`RadialGradientBrush` 之外的分布 heterogeneity。

### 3.2 框架尚未提供的类型

`PathIcon`、`Polygon`、`Polyline`、`Path`、`GeometryGroup`、`BitmapImage`、`LinearGradientBrush` 工厂、
`RichTextBlockOverflow` / `OverflowContentTarget`、`SplitMenuFlyoutItem`、`BreadcrumbBarItem`、
`Image.NineGrid`。

### 3.3 WinUI 3 / Windows App SDK 才有（UWP 侧没有对应类型）

`ItemsView`、`TableView`、`SelectorBar`、`AnnotatedScrollBar`、`WrapPanel`、
`SystemBackdrops`（Mica / DesktopAcrylic）、`RichEditBox` 的 Math API。
另：官方没有 `TwoPaneView` 页、没有 `TextCommandBarFlyout` 的 Example 段、
CommandBarFlyout 页仅 1 例且示例本体在 code-behind。

> **这一节被修正过。** 原先还列着 `ScrollView`、`PagerControl`、`LayoutPanel`、
> `ThemeShadow`、`AnimatedIcon` 五个，写的是"WinUI 3 独有"——**错了**，当初是凭印象判的。
> §5.5 那轮用编译器逐个验过：这五个在**本机**都有类型（前四个分别在
> `Microsoft.UI.Xaml.Controls` / `Windows.UI.Xaml.Media` 里），属于"框架没暴露工厂"，
> 已一并挪到 §5.5 的可补清单。教训是"不适用"这种结论不能靠印象下。

### 3.4 官方自定义资源（本地资源字典没有同名键）

`CustomTextBlockStyle`、`OutputTextBlockStyle`、`AcrylicInAppFillColorDefaultBrush` 的具体数值。

## 4. 缺素材（18 处 → **已全部处理**）

官方示例引用的 `/Assets/SampleMedia/*`（Slices.png、cliff.jpg、rainier.jpg、
sunset.jpg、treetops.jpg、valley.jpg、CoffeeCup.png、cmd/powershell/linux.png …）
本地原本一个都没有，后果不是"少几个文件"，而是**六个示例全指着同一个应用
logo**（`Square150x150Logo.scale-100.png`）——Stretch 三档、滚动缩放、头像、
图标这些档位的差别全部看不出来。

**处理方式：按需生成替代素材**，不搬微软的图、也不引第三方图像库。
`tools/parity/make_sample_media.py`（纯标准库手写 PNG，算法固定、可复现）
生成四张，落在 `samples/Reactor.Gallery/Assets/SampleMedia/`：

| 文件 | 形状 | 替给谁 | 为什么是它 |
|---|---|---|---|
| `treetops.png` | 横向 400×267，带网格线 | `ImageBasic` 五档、`ToolTipBasic` | 非正方形源图才看得出 Stretch 三档差别；网格线让 `Fill` 的形变一眼可见（官方 treetops.jpg 的角色） |
| `tall-cliff.png` | 高图 240×720 | `ScrollViewerBasic` 缩放档 | 150×150 的 logo 放在缩放演示里太小，放大缩小都看不出名堂 |
| `avatar.png` | 方形 128×128，透明底 | `PersonPictureBasic` 四档 | 头像就该是头像，不是应用 logo |
| `slices.png` | 48×48 透明小图标 | `IconsBasic`、`ViewboxBasic` 复合档 | 官方 `Slices.png` 的角色；logo 是应用身份标识，拿它当"随便一个图标"语义跑偏 |

素材是**打包默认 glob** 收进包的（三个 csproj 都没有显式 Content 条目，
现有 logo 走的就是这条路），VS 构建时随 AppX 进包。
换素材的六个文件里，所有"源图 150×150"之类的数字文案已同步改掉。

**没替换的那些**：`GridView/FlipView/PipsPager/TabView/TeachingTip/
SwipeControl/CommandBarFlyout` 等档位本地本来就用的文字/FontIcon 替代
（官方图片在这里只当"随便什么内容"），换图收益为零，保持不动。

## 5. 保留本地语义的那些（不要为了对齐而对齐）

本地示例很多是为了教一件事（受控 / 非受控的分界、回声抑制、只出不进、虚拟化回收、
echo guard），官方没有对应形态。这些**保留本地文案与结构**，只对齐排版参数：

`ButtonBasic`（点数演示）、`TextBoxBasic/TextBoxControlled`（受控非受控）、
`RichEditBoxBasic`（文本住 Document）、`PasswordBoxBasic`（换面具不换内容）、
`TreeViewBasic`（SelectedItem 只有 getter）、`NaviationViewBasic`（四档 PaneDisplayMode）、
`PivotBasic`（每页各自持有状态）、`SplitViewPaneBackground`（亚克力 fallback）、
`AcrylicBrushBasic`（tint 三档）、`DiagnosticsPage` 整页、`GettingStartedPage` 整页。

## 5.5 覆盖矩阵：官方 121 个控件，本地到底差在哪

前面几节回答的是"**已有的**示例写得像不像"。这一节回答**覆盖面**：
官方画廊里那些我们压根没有示例的条目，到底是什么情况。

```
官方 121 ──┬─ 已覆盖         72   （72 个官方控件页有本地示例承载）
           └─ 未覆盖         49 ─┬─ 教学主题（不是控件）  24
                                 ├─ WinUI 3/WASDK 独有    15
                                 └─ 本机有类型、框架未暴露 10   ← 唯一可推进的
```

### 判定方法：让编译器说话，不靠印象

"这是 WinUI 3 才有的"很省事，但没有证据。所以这里分两步：

1. `tools/parity/coverage.py` —— 在 winmd 的 `#Strings` 流里搜类型名，**只作为线索**。
   它分不清 TypeDef 与 TypeRef：名字出现在元数据里，可能只是"某个签名引用了它"。
   实测 UWP 契约里就能搜到 `NavigationView`、`AppWindow`、`JumpList`，全是噪声。
2. `tools/parity/probe_types.py` —— **终审**。生成一行行 `typeof(全名)` 塞进示例工程编译，
   让 C# 编译器回答有没有。结果落到 `tools/parity/type-probe.json`，
   `coverage.py` 有它就采信它。

跑过一遍就知道这个区分不是形式主义：**线索模式把 5 个不存在的类型判成"UWP 有"**
（`AppWindow`/`AppWindowTitleBar`/`JumpList`/`StandardUICommand`/`XamlUICommand`），
编译器模式把它们全拨回"WinUI 3 独有"；反过来，线索模式漏掉的
`LayoutPanel`/`PagerControl`/`ScrollView`/`ThemeShadow`/`WebView2` 被拨到"本机有"。

两个坑都踩过，写在脚本注释里了：

* **错误码是 `CS0234` 不是 `CS0246`**。命名空间存在、里面没这个类型 → `CS0234`；
  命名空间本身不存在 → `CS0246`。一开始只认后者，25 个候选全部"存在"，
  整张表判反。
* **`#Strings` 堆是 UTF-8 空终止**（ECMA-335），只有 `#US` 才是 UTF-16。
  按 UTF-16 搜连 `TextBlock` 都搜不到，全是假阴性。

### 10 个"本机有类型，但框架没暴露工厂"

这些**不是**"WinUI 3 独有"——类型就在本机工具链里，只是本框架没有对应工厂，
所以画廊没法给它们写示例。这是框架侧的**可补清单**，不是示例集的事。

| 控件 | 命名空间 | 备注 |
|---|---|---|
| `WebView2` | `Microsoft.UI.Xaml.Controls` | 需求最实的一个 |
| `AnimatedIcon` | `Microsoft.UI.Xaml.Controls` | 需要 `IAnimatedVisualSource`（一般由 Lottie 生成）才有动画 |
| `AnimatedVisualPlayer` | `Microsoft.UI.Xaml.Controls` | 同上，配套播放器 |
| `ScrollView` | `Microsoft.UI.Xaml.Controls` | 与 `ScrollViewer` 是两套东西 |
| `PagerControl` | `Microsoft.UI.Xaml.Controls` | 与已有的 `PipsPager` 定位不同 |
| `LayoutPanel` | `Microsoft.UI.Xaml.Controls` | |
| `MediaPlayerElement` | `Windows.UI.Xaml.Controls` | |
| `InkCanvas` | `Windows.UI.Xaml.Controls` | 需要手写输入设备才有意义 |
| `ThemeShadow` | `Windows.UI.Xaml.Media` | 不是控件，是挂在元素上的影子对象 |
| `MapControl` | `Windows.UI.Xaml.Controls.Maps` | **需要 `MapServiceToken**；地图服务涉及测绘与地图数据合规要求，补之前先确认资质与范围，别先写示例 |

### 24 个"教学主题"（官方那页本来就不是控件）

`AccessibilityKeyboard`、`AccessibilityScreenReader`、`Binding`、`Chart`、
`Clipboard`、`CompactSizing`、`ConnectedAnimation`、`CustomUserControls`、
`CustomXamlConditionals`、`EasingFunction`、`Geometry`、`ImplicitTransition`、
`PageTransition`、`Sound`、`StoragePickers`、`SystemBackdrops`、`Templates`、
`ThemeTransition`、`Typography`、`Windowing`、`XamlCompInterop`、`XamlResources`、
`XamlStyles`、`CaptureElementPreview`。

它们演示的是**机制**（绑定、模板、资源、动画、无障碍属性），没有对应控件类型。
本库把其中可迁移的几项换成了「写法指南」那一组整页（见 §5）。

### 15 个"WinUI 3 / WASDK 独有"（编译器实测两边都没有）

`AnnotatedScrollBar`、`AppNotification`、`AppWindow`、`AppWindowTitleBar`、
`BadgeNotificationManager`、`ContentIsland`、`ItemsView`、`JumpList`、`SelectorBar`、
`StandardUICommand`、`SystemBackdropElement`、`TableView`、`TitleBar`、`WrapPanel`、
`XamlUICommand`。

### 反向检查：框架有工厂、示例却没展示的部件 = 0

顺手查了另一头。`BitmapIcon`、`ImageIcon`、`MenuSeparator`、`SubMenuItem`、
`ToggleMenuItem` 都在示例里出现过（`IconsBasic` / `MenuFlyoutBasic` / `AppBarButtonBasic` 等），
`SettingsCard` / `SettingsExpander` 由「设置页写法」整页承载，`AppBarSeparator`
在 `AppBarButtonBasic` 里放了两根——没给它们单开页是因为官方那页也只有一例，
形态已经演示到了。

## 5.6 反过来补框架：三个控件缺的属性

示例那一侧对齐到底之后，剩下的缺口都在**框架没暴露属性**上（§3.1）。
这一批专挑"元素类已经有、只差工厂开个形参"或"纯增量加属性"的来做——
**默认值一律保持现有行为**，所以不碰已有调用点。

| 补的 | 动的地方 | 为什么安全 |
|---|---|---|
| `ComboBox.Header` / `PlaceholderText` | 只有 `Factories.New.cs` 加工厂形参 | 元素类早有 `init` 属性，`ComboBoxHandler` 连 `Update` 都写好了，纯粹是工厂没开口 |
| `RadioButtons.Header` | 只有 `Factories.New.cs` | 同上，`RadioButtonsHandler` 的 Mount/Update 都已处理 |
| `InfoBar.Title` / `IsClosable` | 元素类 + Handler（Mount/Update）+ 工厂 | 纯新增，`Title` 默认 null（不显示）、`IsClosable` 默认 false（官方默认） |
| `InfoBar.IsOpen` | 元素类 + Handler（**只 Mount**）+ 工厂 | 默认 `true`，与原先硬编码行为一致 |

### `IsOpen` 不是受控值，是种子值

官方 InfoBar 关掉之后就不该被下一轮重渲染重新打开——那不是"状态同步"，
那是把用户的操作撤回。所以 `IsOpen` **只在 Mount 写一次，不进 Update**。

这个"故意不下发"差点被自己的纪律拦下：`PropertyDriftTests` 那条 invariant
（"只在 Mount 里被读的旋钮必须登记"）第一次跑就报警了：

```
FAIL 只在 Mount 里被读的旋钮都登记过（扫了 73 个 handler） — InfoBarElement.IsOpen
```

按框架既有约定登记（`// MOUNT-ONLY: IsOpen` + 上面那段论证）之后转绿。
**这条 invariant 本来就是为了抓这类"忘了下发"的漂移，登记必须连着理由一起写**，
否则就只是一个被关掉的报警。

### 改完的验证

* 单测 **607 通过 / 0 失败**（顺带证明 `GalleryIndexTests` 那组"索引与源文件对得上"仍成立）
* 隔离编译 **0 警告 0 错误**
* 三重静态检查全绿——其中 `check_params.py` 正好覆盖新加的形参名

### 剩下的（§3.1）

`RadioButtons.MaxColumns`、`NumberBox.PlaceholderText`、`ToolTip.PlacementRect`、
`PasswordBox.PasswordRevealMode`、`ColorPicker` 那两个可见性开关等。
它们都是"元素类还没有这个属性"，要动三处，风险比这一批略高，按需再推。

## 6. 怎么复现

```bash
# 1) 拉官方源码（浅克隆 + 稀疏检出，几分钟而不是几 GB）
git clone --depth 1 --filter=blob:none --sparse \
    https://github.com/microsoft/WinUI-Gallery.git /d/fluentapps/repos/references/WinUI-Gallery
cd /d/fluentapps/repos/references/WinUI-Gallery
git sparse-checkout set WinUIGallery/Samples

# 2) 抽基准 → tools/parity/official/<控件>.md
python tools/parity/extract_official.py

# 3) 拿这份台账逐条对；改完把对应行删掉

# 4) 覆盖面：官方 121 个控件里本地差哪些、为什么（§5.5 那张矩阵）
python tools/parity/coverage.py       # 读 type-probe.json；没有就退回 winmd 线索
python tools/parity/probe_types.py    # 先跑这个：编译器判定，结果落 type-probe.json
```

`extract_official.py` 的两个坑已经写在脚本注释里了：**只抽 Page.xaml 的 Example 段**
（`.txt` 是简化版），以及**官方有两种写法**（老写法塞在 `.Example` 属性元素里、
新写法直接当 `ControlExample` 的内容），两种都要认，否则会漏掉一半控件。
