using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Reactor.Gallery.Samples;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery;

/// <summary>
/// 画廊的全部内容索引。
/// </summary>
/// <remarks>
/// <b>加一个示例要动的地方只有这里。</b>左侧导航、首页卡片、浏览页网格、搜索结果
/// 全都由这一份数据长出来——新增时不必改那四处。
/// <para>
/// <b>分组是照 WinUI Gallery 排的。</b>分类的 id、名称、<b>顺序</b>以及"哪个控件
/// 归到哪一组"，都对的是官方那份 <c>ControlInfoData.json</c>（WinUI Gallery 仓库里
/// 驱动它自己导航的那份数据）。这么做的好处是两边可以逐条对读：在官方画廊里
/// 从"Collections"点进 "FlipView"，在这里点开的就是同一个位置。
/// <para>
/// 官方那 19 组里有相当一部分是 <b>WinUI 3 / Windows App SDK 才有</b>的
/// （Windowing、System、Shell、ItemsView、TableView、ScrollView、System Backdrops…）。
/// 本库跑在 UWP + WinUI 2 上，那些没有对应类型，因此<b>不在这里占一个空条目</b>——
/// 逐条列在 <c>samples/README.md</c> 的「不适用清单」里，免得读者以为是我们漏了。
/// </para>
/// </para>
/// <para>
/// 每个 <c>Build</c> 委托刻意写成"嵌一个组件"而不是"直接产生元素"：
/// 样例里有自己的状态（受控输入框那种），状态只对那个组件自己有意义，
/// 直接内联到调用方会把状态和不相关的兄弟节点混成一棵树。
/// </para>
/// <para>
/// <c>SourcePath</c> 是<b>仓库内的相对路径</b>，由 <see cref="SourceLoader"/> 按
/// 它从包里取出源码展示；两者一旦对不上，页面会显示"源码未随包一起构建"
/// ——这条 break glass 提示比显示一段过期的样板诚实。
/// </para>
/// </remarks>
internal static class SampleIndex
{
    private const string SampleRoot = "Gallery/Samples/";

    /// <summary>
    /// 「写法指南」：<b>整页</b>形态的那几个示例。
    /// </summary>
    /// <remarks>
    /// 与前面那些分类的区别是粒度的，不是内容的：这里每一项是一张完整的页面
    /// （自带滚动与标题），演示的是"几个控件凑在一起怎么写"；
    /// 前面那些分类演示的是"单个控件怎么用"。
    /// <para>
    /// 它对应官方的 <b>Fundamentals</b> 那一组（Resources / Style / Binding /
    /// Templates / Custom &amp; User Controls）。官方那组讲的是 XAML 那套机制，
    /// 而声明式树上没有 <c>x:Bind</c> 与 <c>ControlTemplate</c>，
    /// 所以这里换成"在这套框架里怎么写"这一侧的东西。
    /// </para>
    /// <para>
    /// 它们住在 <c>Pages/</c> 而不是 <c>Gallery/Samples/</c>，
    /// 所以 <c>SourcePath</c> 要显式给（不能按类型名推目录）。
    /// </para>
    /// </remarks>
    public static GalleryCategory Guide { get; } = new(
        "guide",
        "写法指南",
        "\uE8A5",
        "整页级别的组合示例：状态、受控诊断、props、原生逃生舱",
        new[]
        {
            new GalleryItem("getting-started", "快速开始", "状态 / 事件 / 条件渲染的最小形态。",
                new[] { CaseFile("整页", "UseState + When/If 条件渲染。", () => Component<Pages.GettingStartedPage>(), "Pages/GettingStartedPage.cs") }),
            new GalleryItem("guide-inputs", "输入与选择（整页）", "受控写法的完整一张表单。",
                new[] { CaseFile("整页", "TextBox / ComboBox / ToggleSwitch / CheckBox / Slider / RadioButtons。", () => Component<Pages.InputsPage>(), "Pages/InputsPage.cs") }),
            new GalleryItem("guide-layout", "布局（整页）", "Grid 行列、Border、对齐与间距。",
                new[] { CaseFile("整页", "Auto / 星号两种轨道的差别在这里看得最清楚。", () => Component<Pages.LayoutPage>(), "Pages/LayoutPage.cs") }),
            new GalleryItem("guide-lists", "列表（整页）", "ListView / GridView / ForEach。",
                new[] { CaseFile("整页", "项数少时的常规做法。", () => Component<Pages.ListsPage>(), "Pages/ListsPage.cs") }),
            new GalleryItem("guide-virtualization", "虚拟化长列表（整页）", "5000 项的回收情况。",
                new[] { CaseFile("整页", "看清楚「只 realize 几十个容器」到底是什么意思。", () => Component<Pages.VirtualizationPage>(), "Pages/VirtualizationPage.cs") }),
            new GalleryItem("diagnostics", "受控控件诊断", "回声 / 闸门计数的实时读数。",
                new[] { CaseFile("整页", "把「点了没反应」换成可以当场念出来的数字。", () => Component<Pages.DiagnosticsPage>(), "Pages/DiagnosticsPage.cs") }),
            new GalleryItem("component-props", "组件 props", "Component<TProps> 父子传值。",
                new[] { CaseFile("整页", "record 当 props。", () => Component<Pages.ComponentPropsPage>(), "Pages/ComponentPropsPage.cs") }),
            new GalleryItem("native-interop", "原生控件逃生舱", "库还没包住的控件怎么挂进来。",
                new[] { CaseFile("整页", "Native() 的唯一旋钮是 Token。", () => Component<Pages.NativeInteropPage>(), "Pages/NativeInteropPage.cs") }),
            new GalleryItem("guide-settings", "设置页写法", "SettingsCard / SettingsExpander / Expander / ContentDialog。",
                new[] { CaseFile("整页", "一张标准 WinUI 设置页长什么样。", () => Component<Pages.SettingsPage>(), "Pages/SettingsPage.cs") }),
        });

    public static readonly IReadOnlyList<GalleryCategory> Categories = new[]
    {
        // ── Menus & toolbars ────────────────────────────────────────────
        // 官方这一组 = 挂在命令条 / 菜单条上的那一族部件。
        // 官方还列了 StandardUICommand 与 XamlUICommand：那是 WinUI 的
        // ICommand 实现，UWP + WinUI 2 这一侧没有对应类型，不在这里占条目。
        new GalleryCategory(
            "menus-toolbars",
            "命令与工具栏",
            "\uE8B0",
            "一排命令、一条菜单、一片浮出的命令条",
            new[]
            {
                new GalleryItem(
                    "app-bar-button",
                    "AppBarButton",
                    "命令条上的一颗按钮（含 AppBarSeparator）。<b>它不是 Button 的子类</b>，"
                    + "是挂在命令组上的部件。",
                    new[]
                    {
                        Case("符号图标 / 文字 / 字体图标 / 禁用 + 分隔线",
                            "它的槽位是 IconElement（SymbolIcon / FontIcon / BitmapIcon）；"
                            + "进了 PrimaryCommands 就不能再进 Panel。",
                            () => Component<AppBarButtonBasic>()),
                    }),
                new GalleryItem(
                    "app-bar-toggle-button",
                    "AppBarToggleButton",
                    "命令条上「按下去就保持」的那个按钮。它是真的 <c>ToggleButton</c> 子类。",
                    new[]
                    {
                        Case("受控加粗 / 斜体",
                            "命令组整体重建，但受控下发仍会同步抛事件 —— 回声照样要认。",
                            () => Component<AppBarToggleButtonBasic>()),
                    }),
                new GalleryItem(
                    "command-bar",
                    "CommandBar",
                    "一行命令 + 一片内容区。窗口变窄时放不下的项由官方溢出算法收进「…」。",
                    new[]
                    {
                        Case("主命令区与次命令区 + 溢出按钮常驻",
                            "命令项是挂在命令条上的子部件，不是独立控件；"
                            + "<b>OverflowButtonVisibility</b> = Visible 不会凭空造出溢出项。",
                            () => Component<CommandBarBasic>()),
                    }),
                new GalleryItem(
                    "command-bar-flyout",
                    "CommandBarFlyout",
                    "挂在别人身上的一条命令条：点开（按钮的 Flyout）或右键弹出（ContextMenu）。",
                    new[]
                    {
                        Case("两个入口 + 两组命令",
                            "与 CommandBar 同形：主要命令横排、次要命令收进「…」。",
                            () => Component<CommandBarFlyoutBasic>()),
                    }),
                new GalleryItem(
                    "text-command-bar-flyout",
                    "TextCommandBarFlyout",
                    "挂在文本控件上的一条命令条：剪贴板那几条命令<b>由控件自己按选区状态填</b>。"
                    + "（WinUI 2 独有，官方画廊把它合在 CommandBarFlyout 那一页里。）",
                    new[]
                    {
                        Case("官方命令 + 自定义命令并存",
                            "槽位叫 SelectionFlyout（选中文字后弹出），不是右键那个 ContextFlyout。",
                            () => Component<TextCommandBarFlyoutBasic>()),
                    }),
                new GalleryItem(
                    "menu-bar",
                    "MenuBar",
                    "横排的若干组（文件 / 编辑 / 视图），每组点开一个浮出菜单。",
                    new[]
                    {
                        Case("三组菜单",
                            "与「MenuFlyout」那条共用 MenuItem / MenuSeparator —— "
                            + "两处收的是同一个类型。",
                            () => Component<MenuBarBasic>()),
                    }),
                new GalleryItem(
                    "menu-flyout",
                    "MenuFlyout",
                    "浮出菜单本身：<b>不是控件</b>，是挂在别人身上的部件（不进可视树）。",
                    new[]
                    {
                        Case("普通项 / 分隔线 / 禁用 / 可勾选 / 子菜单 + 右键菜单",
                            "可勾选项两个类型官方自己就分在两处，且都<b>只有 Click 一个回执通道</b>；"
                            + "子菜单能一层层往下嵌。",
                            () => Component<MenuFlyoutBasic>()),
                    }),
                new GalleryItem(
                    "swipe-control",
                    "SwipeControl",
                    "内容上一滑，从边上露出几条命令。四个方向各一组，模式长在「组」上。",
                    new[]
                    {
                        Case("左「置顶」右「删除」",
                            "右边 Execute 一滑到底就执行，左边 Reveal 露出来还要再点。",
                            () => Component<SwipeControlBasic>()),
                    }),
            }),

        // ── Collections ─────────────────────────────────────────────────
        new GalleryCategory(
            "collections",
            "集合与虚拟化",
            "\uE8B7",
            "一排数据怎么摆",
            new[]
            {
                new GalleryItem(
                    "list-view",
                    "ListView",
                    "纵向列表。受控 SelectedIndex，与 GridView 同一条选中契约。",
                    new[]
                    {
                        Case("受控选中", "选中是下标，不是「哪一项对象」。", () => Component<ListViewBasic>()),
                    }),
                new GalleryItem(
                    "grid-view",
                    "GridView",
                    "宫格。项内容可以是任意元素树。",
                    new[]
                    {
                        Case("宫格铺开", "与 ListView 同形，换的是布局（ItemsPanel）。", () => Component<GridViewBasic>()),
                    }),
                new GalleryItem(
                    "flip-view",
                    "FlipView",
                    "翻页视图：一次一项，左右翻。",
                    new[]
                    {
                        Case("受控当前页", "页内容是任意元素树，不是字符串。", () => Component<FlipViewBasic>()),
                    }),
                new GalleryItem(
                    "list-box",
                    "ListBox",
                    "列表框。<b>WinUI 3 没有这一页</b>（UWP 原生控件）："
                    + "与 ListView 同形但基类不同——只是 Selector，没有 Header 与 ItemClick。",
                    new[]
                    {
                        Case("选中三档",
                            "多选时 SelectedIndex 只报第一个 —— 它是\"当前项\"，不是集合。",
                            () => Component<ListBoxBasic>()),
                    }),
                new GalleryItem(
                    "tree-view",
                    "TreeView",
                    "树。<b>本库唯一\"选中结构上就不可写\"的控件</b>：官方 SelectedItem 只有 getter。",
                    new[]
                    {
                        Case("节点树与三个通知",
                            "ItemInvoked / Expanding / Collapsed，全都只出不进。",
                            () => Component<TreeViewBasic>()),
                    }),
                new GalleryItem(
                    "items-repeater",
                    "ItemsRepeater（VirtualizingList）",
                    "长列表：只为看得见的项建控件（走官方 ItemsRepeater）。",
                    new[]
                    {
                        Case("5000 项", "屏幕上通常只有几十个容器。", () => Component<VirtualizingListBasic>()),
                    }),
                new GalleryItem(
                    "pull-to-refresh",
                    "PullToRefresh（RefreshContainer）",
                    "下拉刷新。内容必须自己能滚，否则永远拉不出来。",
                    new[]
                    {
                        Case("下拉刷新 + 可延迟的回执",
                            "拿到 ticket 后先去做事，做完了再 Complete。",
                            () => Component<RefreshContainerBasic>()),
                    }),
            }),

        // ── Date & time ─────────────────────────────────────────────────
        new GalleryCategory(
            "datetime",
            "日期与时间",
            "\uE787",
            "选一天、选一刻、看一整张月历",
            new[]
            {
                new GalleryItem(
                    "date-picker",
                    "DatePicker",
                    "年月日三个下拉。受控 Date；不传就是非受控。",
                    new[]
                    {
                        Case("受控 / 藏栏 / 限年份 / 换日历",
                            "minYear / maxYear 只比年份那一段；换日历改的是<b>表示法</b>不是那一天。",
                            () => Component<DatePickerBasic>()),
                    }),
                new GalleryItem(
                    "time-picker",
                    "TimePicker",
                    "时刻选择。值是 TimeSpan（一天里的哪一刻），不是 DateTime。",
                    new[]
                    {
                        Case("十二 / 二十四小时制与步长",
                            "改 minuteIncrement 会把当前值吸附到整档。",
                            () => Component<TimePickerBasic>()),
                    }),
                new GalleryItem(
                    "calendar-date-picker",
                    "CalendarDatePicker",
                    "一个输入框 + 一张可弹出的月历。Date 可空——没有值是一个合法状态。",
                    new[]
                    {
                        Case("受控（可空）与区间", "回调拿到 null 表示被清空了。", () => Component<CalendarDatePickerBasic>()),
                    }),
                new GalleryItem(
                    "calendar-view",
                    "CalendarView",
                    "一整张月历。<b>选中值只出不进</b>——从不往回写。",
                    new[]
                    {
                        Case("单选 / 多选 / 十年视图",
                            "回调给的是快照，不是控件内部那个活集合。",
                            () => Component<CalendarViewBasic>()),
                    }),
            }),

        // ── Basic input ─────────────────────────────────────────────────
        new GalleryCategory(
            "basic-input",
            "基础输入",
            "\uE8A1",
            "从用户那里取值",
            new[]
            {
                new GalleryItem(
                    "button",
                    "Button",
                    "点击回调写在工厂方法里；内容是任意元素树（Button 是 ContentControl）。",
                    new[]
                    {
                        Case("最小形态", "回调闭包里读到的 state 就是本帧那个值。", () => Component<ButtonBasic>()),
                        Case("三种变体", "标准 / 强调 / 禁用，以及图标 + 文字的内容。", () => Component<ButtonVariants>()),
                    }),
                new GalleryItem(
                    "drop-down-button",
                    "DropDownButton",
                    "按下去只展开菜单：<b>官方连 Click 都不提供</b>，动作由菜单里的项承担。",
                    new[]
                    {
                        Case("挂菜单 + 按钮自己带图标",
                            "按钮上的图标与文字一起是它的<b>内容</b>（ContentControl），"
                            + "不是另有一个 Icon 属性——这一点与 AppBarButton 不同。",
                            () => Component<DropDownButtonBasic>()),
                    }),
                new GalleryItem(
                    "split-button",
                    "SplitButton",
                    "左半边执行、右半边展开菜单。适合「有一个默认动作、另外几个是变体」。",
                    new[]
                    {
                        Case("保存 + 另存为 / 导出", "右半边那半个按钮的展开动作由控件自己处理。", () => Component<SplitButtonBasic>()),
                    }),
                new GalleryItem(
                    "toggle-button",
                    "ToggleButton",
                    "按下就保持的按钮。与 CheckBox 之别是外观，不是行为——连三态都一样。",
                    new[]
                    {
                        Case("受控 / 任意内容 / 禁用", "禁用是 isEnabled，不是「不给回调」。", () => Component<ToggleButtonBasic>()),
                    }),
                new GalleryItem(
                    "repeat-button",
                    "RepeatButton",
                    "按住不放会连续触发点击。没有选中态，与 ToggleButton 是两条路。",
                    new[]
                    {
                        Case("按住连发", "回调被反复调用是这个控件的全部意义，不是 bug。", () => Component<RepeatButtonBasic>()),
                    }),
                new GalleryItem(
                    "toggle-split-button",
                    "ToggleSplitButton",
                    "会保持按下的拆分按钮：左半执行并锁住按下态，右半展开菜单。",
                    new[]
                    {
                        Case("按下态与菜单各管一半",
                            "IsCheckedChanged 与 Click 是官方留的两个通道"
                            + "（注意：不是 Checked / Unchecked——它不继承 UWP 的 ToggleButton）。",
                            () => Component<ToggleSplitButtonBasic>()),
                    }),
                new GalleryItem(
                    "hyperlink-button",
                    "HyperlinkButton",
                    "看起来是链接、行为是按钮。UIA 里报的是 Hyperlink 而不是 Button。",
                    new[]
                    {
                        Case("两种内容形态", "内容是元素树时，AutomationName 得自己给。", () => Component<HyperlinkBasic>()),
                    }),
                new GalleryItem(
                    "check-box",
                    "CheckBox",
                    "勾选。语义是\"选中 / 参与某一项\"，与 ToggleSwitch 的\"设置开关\"不是一回事。",
                    new[]
                    {
                        Case("二态受控 + 三态（只出不进）",
                            "官方三态是三个独立事件；本库回调是 Action&lt;bool&gt;，"
                            + "只能表达前两个 —— 于是三态那一档不假装受控。",
                            () => Component<CheckBoxBasic>()),
                    }),
                new GalleryItem(
                    "toggle-switch",
                    "ToggleSwitch",
                    "开关：某项设置<b>开 / 关且立即生效</b>。",
                    new[]
                    {
                        Case("带标题 + 开 / 关文案",
                            "onContent / offContent 是开关上那两个字，header 才是它上方的标题。",
                            () => Component<ToggleSwitchBasic>()),
                    }),
                new GalleryItem(
                    "combo-box",
                    "ComboBox",
                    "下拉选择。按下标定值，<b>可编辑那一档打的字不等于选中项</b>。",
                    new[]
                    {
                        Case("受控 / 清空 / 可编辑 / 敲字跳转",
                            "-1 表示没有选中项；<b>IsTextSearchEnabled</b> 是另一个开关，"
                            + "它在可编辑模式下也生效。",
                            () => Component<ComboBoxBasic>()),
                    }),
                new GalleryItem(
                    "radio-button",
                    "RadioButton / RadioButtons",
                    "单选。成组用 <b>RadioButtons</b>（官方分组控件）：键盘方向键在组内移动"
                    + "这一条由它的模板提供，自己排一排 RadioButton 拼不出来。",
                    new[]
                    {
                        Case("分组控件受控 + 自己成组（groupName）",
                            "散在表单不同位置时才用 groupName。",
                            () => Component<RadioButtonsBasic>()),
                    }),
                new GalleryItem(
                    "rating-control",
                    "RatingControl",
                    "评分。只读是官方的一个属性，不是把回调摘掉。",
                    new[]
                    {
                        Case("受控 / 可清零 / 只读 / 底衬",
                            "摘掉回调 = 点了没反应；只读才是「能看不能改」。底衬要显式给 placeholderValue。",
                            () => Component<RatingBasic>()),
                    }),
                new GalleryItem(
                    "color-picker",
                    "ColorPicker",
                    "取色。受控 Color；透明度那几个开关会把颜色夹走。",
                    new[]
                    {
                        Case("受控颜色 / 光谱两轴 / 「更多」按钮",
                            "关掉透明度会把 A 拉到 255——那一发不是用户输入；"
                            + "关掉「更多」按钮后那一片反而<b>常驻</b>。",
                            () => Component<ColorPickerBasic>()),
                    }),
                new GalleryItem(
                    "slider",
                    "Slider",
                    "连续值。<b>拖动是连续回调</b>：回调里必须是廉价的动作。",
                    new[]
                    {
                        Case("基本 / 刻度 / 竖直 / 反向",
                            "画刻度与吸附到刻度是<b>两件事</b>；"
                            + "<b>IsDirectionReversed</b> 只改「值往哪边增大」。",
                            () => Component<SliderBasic>()),
                        Case("刻度细节与吸附",
                            "默认只画不吸附；要停在刻度上另给 snapsTo。",
                            () => Component<SliderTicks>()),
                    }),
            }),

        // ── Text ────────────────────────────────────────────────────────
        new GalleryCategory(
            "text",
            "文本",
            "\uE70F",
            "把字摆出来、把字收进来",
            new[]
            {
                new GalleryItem(
                    "text-block",
                    "TextBlock",
                    "文本展示的基础单位：字号 / 字重 / 折行与截断。",
                    new[]
                    {
                        Case("排版档位", "套 XAML 同名命名样式的几档字号与字重。", () => Component<TextBlockStyles>()),
                        Case("截断 / 彩色字形 / 字距",
                            "只给 MaxLines 是<b>硬裁</b>没有省略号；「…」由 TextTrimming 单独决定。",
                            () => Component<TextBlockFormatting>()),
                    }),
                new GalleryItem(
                    "text-box",
                    "TextBox",
                    "单行文本输入。<b>受控</b>与<b>非受控</b>的分界线就是那个 Optional。",
                    new[]
                    {
                        Case("非受控", "不给值，控件自己持有文本。", () => Component<TextBoxBasic>()),
                        Case("受控", "给值 + 给回调，输入经 setState 回写。", () => Component<TextBoxControlled>()),
                        Case("多行 / 只读 / 长度上限",
                            "官方画廊那个「清除按钮」UWP 上没有对应属性，本库就没装。",
                            () => Component<TextBoxOptions>()),
                    }),
                new GalleryItem(
                    "rich-text-block",
                    "RichTextBlock",
                    "一行里多段异色文本；画廊的源码框就是同一个控件在工作。",
                    new[]
                    {
                        Case("Run 的着色", "每段 Run 自带前景色与字重。", () => Component<RichTextBasic>()),
                    }),
                new GalleryItem(
                    "rich-edit-box",
                    "RichEditBox",
                    "带格式的文本编辑。<b>它官方没有 Text 属性</b>——文本住在 Document 里，所以只出不进。",
                    new[]
                    {
                        Case("只出不进 + 只读 / 回车 / 拼写检查",
                            "写进去 abc、读出来 abc\\r：那个 \\r 是文档结构。",
                            () => Component<RichEditBoxBasic>()),
                    }),
                new GalleryItem(
                    "password-box",
                    "PasswordBox",
                    "密码框。遮罩与明文切换由官方控件自带，不是 TextBox 换个样式。",
                    new[]
                    {
                        Case("受控取值 + 换遮罩",
                            "回写之后 state 里真的有一份明文——别把它落进日志；遮罩只在显示层。",
                            () => Component<PasswordBoxBasic>()),
                    }),
                new GalleryItem(
                    "auto-suggest-box",
                    "AutoSuggestBox",
                    "带候选列表的输入框（画廊顶部的示例搜索就是它）。",
                    new[]
                    {
                        Case("跟着输入变候选 + 搜索图标 + 填不填框",
                            "候选列表由当前文本算出来，不留 state；<b>QueryIcon</b> 是内容槽，按形状比；"
                            + "<b>UpdateTextOnSelect</b> 只管点候选时填不填框，不影响提交回执。",
                            () => Component<AutoSuggestBasic>()),
                    }),
                new GalleryItem(
                    "number-box",
                    "NumberBox",
                    "数值输入。<b>官方把它放在 Text 这一组</b>（它继承的是 TextBox 那一族）。",
                    new[]
                    {
                        Case("表达式 / 校验 / 回绕",
                            "Disabled 那一档下输 999 什么都不做，这是官方给的，不是 bug。",
                            () => Component<NumberBoxOptions>()),
                    }),
            }),

        // ── Status & info ───────────────────────────────────────────────
        new GalleryCategory(
            "status",
            "状态与信息",
            "\uE9D9",
            "进行到哪儿了、出没出错",
            new[]
            {
                new GalleryItem(
                    "info-bar",
                    "InfoBar",
                    "页面内的一条状态提示：普通 / 成功 / 警告 / 错误。",
                    new[]
                    {
                        Case("四种严重级别 + 图标开关",
                            "级别决定图标与强调色；<b>IsIconVisible</b> 只隐藏图标，文字不会左移。",
                            () => Component<InfoBarBasic>()),
                    }),
                new GalleryItem(
                    "info-badge",
                    "InfoBadge",
                    "贴在控件角落上的小圆点 / 数字 / 图标。位置由布局说了算，它不认主人。",
                    new[]
                    {
                        Case("数字 / 圆点 / 图标 + 五种预设样式",
                            "Value = -1 是官方的圆点档；给了图标就不显示数字。",
                            () => Component<InfoBadgeBasic>()),
                    }),
                new GalleryItem(
                    "progress-bar",
                    "ProgressBar",
                    "一条横条。<c>Value</c> 为 null 就是不确定进度——不是另有一个开关。",
                    new[]
                    {
                        Case("确定 / 不确定 / 出错 / 暂停",
                            "ShowError 与 ShowPaused 是叠加在进度之上的状态。",
                            () => Component<ProgressBarBasic>()),
                    }),
                new GalleryItem(
                    "progress-ring",
                    "ProgressRing",
                    "一个圈。用的是 WinUI 2 那个（UWP 原生版不支持确定进度）。",
                    new[]
                    {
                        Case("转圈 / 确定进度 / 停掉",
                            "IsActive = false 是「整个消失」，不是暂停。",
                            () => Component<ProgressRingBasic>()),
                    }),
                new GalleryItem(
                    "tool-tip",
                    "ToolTip",
                    "悬停 / 聚焦时冒出来的那行说明。挂在别人身上，不进可视树。",
                    new[]
                    {
                        Case("文本 / 图文 / 方位 / 偏移",
                            "两条入口对应官方 XAML 的两种写法；没有 IsOpen——开合由指针与焦点驱动。",
                            () => Component<ToolTipBasic>()),
                    }),
            }),

        // ── Dialogs & flyouts ───────────────────────────────────────────
        // 官方这一组还有 Popup：UWP 有这个类型，但本库没有包它
        // （它不是一个能进协调器的控件：自己管 Placement 与 IsOpen），
        // 所以这里不占条目，记在「不适用清单」里。
        new GalleryCategory(
            "dialogs",
            "对话框与浮出层",
            "\uE8B1",
            "挡在内容之上的那一层",
            new[]
            {
                new GalleryItem(
                    "content-dialog",
                    "ContentDialog",
                    "模态弹窗。",
                    new[]
                    {
                        Case("确认框 / 铺满 / 按钮置灰",
                            "fullSizeDesired 是「申请」不是保证；按钮的「在不在」与「能不能点」是两个属性。",
                            () => Component<ContentDialogBasic>()),
                    }),
                new GalleryItem(
                    "flyout",
                    "Flyout",
                    "内容型浮出层：里面装的是一棵子树，不是「项」。",
                    new[]
                    {
                        Case("子树与就地 patch",
                            "输入框里的光标不会因重渲染被抹掉；没有受控 IsOpen。",
                            () => Component<FlyoutBasic>()),
                    }),
                new GalleryItem(
                    "teaching-tip",
                    "TeachingTip",
                    "挂在某个控件旁边的说明。目标填的是<b>同层下标</b>——元素没有名字。",
                    new[]
                    {
                        Case("受控展开 + 可以拒绝关闭",
                            "锁住之后把「关掉」这个回执丢掉，下一轮又写回成展开。",
                            () => Component<TeachingTipBasic>()),
                    }),
                new GalleryItem(
                    "popup",
                    "Popup",
                    "浮层容器：<b>它自己没有皮</b>，跟着目标或窗口左上角开——是容器不是对话框。",
                    new[]
                    {
                        Case("按钮开关 / 轻 dismiss / 挂在目标旁边",
                            "IsOpen 受控：轻 dismiss 那一发走 Closed 回执才能同步回来。",
                            () => Component<PopupBasic>()),
                    }),
            }),

        // ── Scrolling ───────────────────────────────────────────────────
        // 官方这一组还有 AnnotatedScrollBar / PagerControl / ScrollView：
        // 前两个是 Windows App SDK 的控件，ScrollView 是 WinUI 3 的——
        // UWP + WinUI 2 这一侧没有，不占条目。
        new GalleryCategory(
            "scrolling",
            "滚动与翻页",
            "\uE8FD",
            "内容比容器大时怎么走",
            new[]
            {
                new GalleryItem(
                    "scroll-viewer",
                    "ScrollViewer",
                    "给一块内容加上滚动。<c>滚动条显不显示</c>与<c>能不能滚</c>是两个开关。",
                    new[]
                    {
                        Case("两条轴各两个开关 + 缩放",
                            "Visibility 只管滚动条，Mode 才管能不能滚；<b>ZoomMode</b> 缩放的是那一棵子树。",
                            () => Component<ScrollViewerBasic>()),
                    }),
                new GalleryItem(
                    "semantic-zoom",
                    "SemanticZoom",
                    "同一批数据的\"细看\"与\"总览\"两种视图。两个槽位只收 ListView / GridView。",
                    new[]
                    {
                        Case("两个槽位 + 受控切换",
                            "回执是异步来的：那一发多出来的是一次不重渲染的空转。",
                            () => Component<SemanticZoomBasic>()),
                    }),
                new GalleryItem(
                    "pips-pager",
                    "PipsPager",
                    "一排小点 + 前后翻页按钮。它自己不装内容，只是\"第几页\"的指示。",
                    new[]
                    {
                        Case("受控当前页 + 改页数",
                            "属性叫 SelectedPageIndex，回执事件反倒叫 SelectedIndexChanged。",
                            () => Component<PipsPagerBasic>()),
                    }),
            }),

        // ── Layout ──────────────────────────────────────────────────────
        // 官方这一组还有 LayoutPanel / WrapPanel：那是 Windows App SDK 的
        // 新面板，UWP 没有，不占条目。
        new GalleryCategory(
            "layout",
            "布局",
            "\uE946",
            "东西放在哪里",
            new[]
            {
                new GalleryItem(
                    "grid",
                    "Grid",
                    "二维布局：行列 + 占位。",
                    new[]
                    {
                        Case("行列与占位", "Auto 按内容收缩，Star 分剩余空间。", () => Component<LayoutGrid>()),
                    }),
                new GalleryItem(
                    "border",
                    "Border",
                    "带边框的背景块。<b>只装一个子元素</b>：描边 + 背景 + 内边距。",
                    new[]
                    {
                        Case("只有描边 / 描边 + 背景 / 四边不等厚",
                            "描边色收的是<b>刷子</b>（取主题资源，切主题自己换色）；"
                            + "Padding 是内容与边之间的距离，不是外边距。",
                            () => Component<BorderBasic>()),
                    }),
                new GalleryItem(
                    "stack-panel",
                    "StackPanel",
                    "顺序堆叠（<b>VStack</b> / <b>HStack</b>）。最基础的容器。",
                    new[]
                    {
                        Case("方向 / 间距 / 子元素对齐",
                            "沿堆叠方向的可用尺寸是<b>无限</b>的：竖排时 VAlign 没有意义，"
                            + "只有垂直于堆叠方向那一条看得出来。",
                            () => Component<StackPanelBasic>()),
                    }),
                new GalleryItem(
                    "canvas",
                    "Canvas",
                    "绝对定位。坐标是附加属性，写在子元素身上；容器不给子元素任何可用尺寸。",
                    new[]
                    {
                        Case("坐标与叠放次序", "ZIndex 决定重叠区域谁在上面。", () => Component<CanvasBasic>()),
                    }),
                new GalleryItem(
                    "relative-panel",
                    "RelativePanel",
                    "子元素之间、子元素与面板之间互相定位。兄弟关系填的是同层下标。",
                    new[]
                    {
                        Case("贴面板与贴兄弟", "每轮全量重落，所以去掉一条关系会真的失效。", () => Component<RelativePanelBasic>()),
                    }),
                new GalleryItem(
                    "variable-sized-wrap-grid",
                    "VariableSizedWrapGrid",
                    "固定格子换行排布，子项可跨行跨列。没有行列号，位置由换行顺序决定。",
                    new[]
                    {
                        Case("跨格", "跨格是附加属性 .WrapSpan(...)，写在子元素身上。", () => Component<WrapGridBasic>()),
                    }),
                new GalleryItem(
                    "viewbox",
                    "Viewbox",
                    "把一个子元素整体缩放到可用空间里。布局族里唯一的单子元素容器。",
                    new[]
                    {
                        Case("四档 Stretch", "Stretch 管怎么缩，StretchDirection 管允许往哪个方向缩。", () => Component<ViewboxBasic>()),
                    }),
                new GalleryItem(
                    "expander",
                    "Expander",
                    "WinUI 2 的可折叠分组容器。",
                    new[]
                    {
                        Case("折叠与展开", "展开状态由控件自己持有。", () => Component<ExpanderBasic>()),
                    }),
                new GalleryItem(
                    "split-view",
                    "SplitView",
                    "侧边面板 + 主内容区。四种 DisplayMode 分「盖上去」与「挤开」两派。",
                    new[]
                    {
                        Case("四种形态与开合", "IsPaneOpen 是非受控的：没有回执通道就不装成受控。", () => Component<SplitViewBasic>()),
                        Case("面板背景（亚克力）",
                            "<b>PaneBackground</b> 收的是<b>刷子</b>不是元素；亚克力取的是"
                            + "「它后面那层」——面板没盖住东西时看到的就是 fallbackColor。",
                            () => Component<SplitViewPaneBackground>()),
                    }),
                new GalleryItem(
                    "two-pane-view",
                    "TwoPaneView",
                    "两块内容按可用尺寸决定并排还是只留一块。Mode 是只读的。",
                    new[]
                    {
                        Case("并排 / 上下 / 只留一块",
                            "哪一块留下由 PanePriority 决定，不由声明顺序决定。",
                            () => Component<TwoPaneViewBasic>()),
                    }),
            }),

        // ── Navigation ──────────────────────────────────────────────────
        // 官方这一组还有 SelectorBar（WinUI 3 新控件），UWP 没有，不占条目。
        new GalleryCategory(
            "navigation",
            "导航",
            "\uE700",
            "在几个页面 / 几片内容之间走",
            new[]
            {
                new GalleryItem(
                    "navigation-view",
                    "NavigationView",
                    "WinUI 的标准导航外壳。画廊自己的外壳就是它。",
                    new[]
                    {
                        Case("四种菜单形态 + 受控选中 + 汉堡键",
                            "选中受控、开合不受控；设置项以 -1 回调；"
                            + "藏起汉堡键<b>不等于</b>面板锁死——管的是「键在不在」。",
                            () => Component<NavigationViewBasic>()),
                    }),
                new GalleryItem(
                    "pivot",
                    "Pivot",
                    "横向滑动切换的分页容器，每页各自持有内容。",
                    new[]
                    {
                        Case("三页 + 受控选中", "切走再切回来，第一页输入框里的字还在。", () => Component<PivotBasic>()),
                    }),
                new GalleryItem(
                    "tab-view",
                    "TabView",
                    "页签容器。源码框的那两个标签就是它。",
                    new[]
                    {
                        Case("切页签 + 宽度 / 关闭按钮时机",
                            "一次只有一份内容树；<b>TabWidthMode</b> 默认等宽，"
                            + "<b>CloseButtonOverlayMode</b> 只管可见时机。",
                            () => Component<TabViewBasic>()),
                    }),
                new GalleryItem(
                    "breadcrumb-bar",
                    "BreadcrumbBar",
                    "层级路径，允许点任意一级往回走。",
                    new[]
                    {
                        Case("字符串路径", "喂进去的必须是数据，不能是 UIElement。", () => Component<BreadcrumbBarBasic>()),
                    }),
            }),

        // ── Media ───────────────────────────────────────────────────────
        // 官方这一组还有 AnimatedVisualPlayer / Capture Element / InkCanvas /
        // MapControl / MediaPlayerElement / Sound / WebView2：
        // 本库没有包这些类型（它们各自要一套运行时依赖），不占条目。
        new GalleryCategory(
            "media",
            "媒体与图像",
            "\uEB9F",
            "把图片与人摆出来",
            new[]
            {
                new GalleryItem(
                    "image",
                    "Image",
                    "图片。<c>Stretch</c> 决定尺寸不一致时溢出与留白归谁。",
                    new[]
                    {
                        Case("三种 Stretch", "同一张源图并排摆，差别一眼看得出来。", () => Component<ImageBasic>()),
                    }),
                new GalleryItem(
                    "person-picture",
                    "PersonPicture",
                    "人物头像。显示什么由官方的优先级决定，不由参数顺序决定。",
                    new[]
                    {
                        Case("四种内容来源与角标",
                            "图 → 首字母 → 按名字推；角标四种内容互相替换。"
                            + "<b>preferSmallImage</b> 管小尺寸时还看不看图。",
                            () => Component<PersonPictureBasic>()),
                    }),
            }),

        // ── Styles ──────────────────────────────────────────────────────
        // 官方这一组还有 AnimatedIcon / Compact Sizing / RadialGradientBrush /
        // System Backdrops (Mica/Acrylic) / SystemBackdropElement / ThemeShadow：
        // 都是 WinUI 3 / Windows App SDK 的（Mica 与 DesktopAcrylic 需要
        // 宿主窗口配合），UWP + WinUI 2 这一侧没有，不占条目。
        new GalleryCategory(
            "styles",
            "样式与材质",
            "\uE7BA",
            "图标、形状与那层半透明的材质",
            new[]
            {
                new GalleryItem(
                    "icon-element",
                    "IconElement",
                    "图标：SymbolIcon / FontIcon / BitmapIcon / ImageIcon。",
                    new[]
                    {
                        Case("常用码位与两种位图图标",
                            "SymbolIcon 与 FontIcon 是<b>同一件事的两种写法</b>；"
                            + "BitmapIcon 设尺寸是<b>裁</b>不是缩放，别设；要缩放用 ImageIcon。",
                            () => Component<IconsBasic>()),
                    }),
                new GalleryItem(
                    "shape",
                    "Shape（Ellipse / Rectangle）",
                    "画出来的几何：<b>形状不是控件</b>——没模板、没内容、不接焦点。",
                    new[]
                    {
                        Case("填充 / 描边 / 圆角 / 虚线与端帽",
                            "描边九件套三个形状通用；圆角是 RadiusX / RadiusY 不是 CornerRadius。",
                            () => Component<ShapesBasic>()),
                    }),
                new GalleryItem(
                    "line",
                    "Line",
                    "直线。官方把它单列一页：<b>它的几何由两个端点定死</b>，"
                    + "不认 Width / Height，也没有 Fill。",
                    new[]
                    {
                        Case("端帽 / 虚线 / 虚线偏移",
                            "两端各有一个端帽属性；<b>StrokeDashCap</b> 只管虚线每一小段的两端。",
                            () => Component<LineBasic>()),
                    }),
                new GalleryItem(
                    "acrylic-brush",
                    "AcrylicBrush",
                    "亚克力：<b>它是刷子，不是元素</b>。UWP 原生的那一块（应用内亚克力）。",
                    new[]
                    {
                        Case("三档着色浓度 + 带颜色的 tint",
                            "它糊的是<b>窗口内、它后面那层</b>；后面没东西时看到的就是"
                            + "fallbackColor 那一层纯色——不是 bug，是素材没了。",
                            () => Component<AcrylicBrushBasic>()),
                    }),
                new GalleryItem(
                    "radial-gradient-brush",
                    "RadialGradientBrush",
                    "径向渐变：<b>它是 WinUI 2 的类型</b>，UWP 那一侧只有线性的。",
                    new[]
                    {
                        Case("圆心到边缘 + 三种铺法",
                            "SpreadMethod 只管「末端之后」；GradientStops 是只读集合，只能往里 Add。",
                            () => Component<RadialGradientBrushBasic>()),
                    }),
            }),

        // ── Motion ──────────────────────────────────────────────────────
        // 官方这一组七项里有六项是 XAML 动画（动画 interop / 连接动画 /
        // 缓动函数 / 隐式过渡 / 页面过渡 / 主题过渡）：本库没有包 Storyboard 那一层，
        // UWP 上要动画请走原生逃生舱（见「原生控件逃生舱」）。
        // 这里只留官方也归在这一组的 ParallaxView。
        new GalleryCategory(
            "motion",
            "动效",
            "\uE894",
            "跟着别处的进度动",
            new[]
            {
                new GalleryItem(
                    "parallax-view",
                    "ParallaxView",
                    "参照另一处滚动的进度把自己错开一点。参照谁填的是同层下标。",
                    new[]
                    {
                        Case("错开量与口径",
                            "下标指错不会崩，只会安静地不动 —— 取不到滚动宿主就没有视差。",
                            () => Component<ParallaxViewBasic>()),
                    }),
            }),

            // 「写法指南」也进同一份索引：搜索要能搜到它，首页也能从它拿到入口，
            // 只是左侧导航不逐条展开（它本身有 9 项，展开会把导航拉长一倍）。
            Guide,
    };

    /// <summary>全部不含 Guide 的分类（左侧导航用；Guide 单独放，不进菜单）。</summary>
    public static IReadOnlyList<GalleryCategory> MenuCategories =>
        Categories.Where(c => c.Id != Guide.Id).ToArray();

    private static SampleCase Case(string title, string? description, Func<Element> build) =>
        new(title, description, build, string.Empty);

    /// <summary>源码不在 <c>Gallery/Samples/</c> 下的样例（那些"整页"示例住在 <c>Pages/</c>）。</summary>
    private static SampleCase CaseFile(
        string title, string? description, Func<Element> build, string sourcePath) =>
        new(title, description, build, sourcePath);

    public static IEnumerable<GalleryItem> AllItems =>
        Categories.SelectMany(c => c.Items);

    public static GalleryItem? FindItem(string? id) =>
        string.IsNullOrEmpty(id) ? null : AllItems.FirstOrDefault(i => i.Id == id);

    public static GalleryCategory? FindCategoryOf(GalleryItem item) =>
        Categories.FirstOrDefault(c => c.Items.Any(i => i.Id == item.Id));

    /// <summary>
    /// 搜索。<b>标题与描述都参与匹配</b>，命中标题的排在前面。
    /// </summary>
    /// <remarks>
    /// 只搜标题会让人很困惑：明明在这个示例里见过的写法，搜它的关键词却什么都
    /// 没有。对一个目的是"查某种写法该怎么写"的应用，这比多列出几条冗余结果严重。
    /// </remarks>
    public static IReadOnlyList<GalleryItem> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GalleryItem>();
        }

        var needle = query.Trim();

        return AllItems
            .Select(item => (Item: item, Rank: RankOf(item, needle)))
            .Where(pair => pair.Rank > 0)
            .OrderByDescending(pair => pair.Rank)
            .ThenBy(pair => pair.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => pair.Item)
            .ToArray();
    }

    /// <summary>
    /// 这一条是<b>在哪里</b>命中的：结果列表里把它写出来，人才知道"为什么它在这儿"。
    /// </summary>
    /// <remarks>
    /// 只按相关度排序而不说明命中位置，遇到"搜 TabView 出来一堆没写着 TabView
    /// 的条目"时，读者无从判断是排序错了还是搜索坏了。写清楚之后这两件事就分开了。
    /// <para>
    /// 返回 <c>null</c> 表示没命中（调用方据此决定是否展示）。
    /// </para>
    /// </remarks>
    public static string? HitReasonOf(GalleryItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var needle = query.Trim();

        if (Contains(item.Title, needle))
        {
            return "命中标题";
        }

        if (item.Description is { } description && Contains(description, needle))
        {
            return "命中描述";
        }

        var sample = item.Samples.FirstOrDefault(s => Contains(s.Title ?? string.Empty, needle));

        return sample is null ? null : $"命中样例「{sample.Title}」";
    }

    /// <summary>命中程度：标题 = 3，描述 = 2，样例标题 = 1；都没命中 = 0。</summary>
    private static int RankOf(GalleryItem item, string needle)
    {
        if (Contains(item.Title, needle))
        {
            return 3;
        }

        if (item.Description is { } description && Contains(description, needle))
        {
            return 2;
        }

        return item.Samples.Any(s => Contains(s.Title ?? string.Empty, needle)) ? 1 : 0;
    }

    private static bool Contains(string text, string needle) =>
        text.Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// 给索引补 <c>SourcePath</c>：样例文件名是从 <c>Build</c> 委托里<
    /// b>不好拿</b>的（委托里是一次组件嵌套），所以按调用顺序补。
    /// </summary>
    /// <remarks>
    /// 这一段依赖"<see cref="SampleCase.Build"/> 最终是 <c>Component&lt;T&gt;()</c>"这一约定：
    /// 调用一次建出来的元素，它的 element 类型就是那个组件类型，于是文件名 = 类型名 + ".cs"。
    /// 比在手写的常量里再抄一遍强：漏改一次，画廊就开始显示别处的代码。
    /// </remarks>
    static SampleIndex()
    {
        foreach (var category in Categories)
        {
            foreach (var item in category.Items)
            {
                for (var i = 0; i < item.Samples.Length; i++)
                {
                    var sample = item.Samples[i];
                    if (sample.SourcePath.Length > 0)
                    {
                        continue;
                    }

                    item.Samples[i] = sample with { SourcePath = SampleRoot + TypeNameOf(sample.Build) + ".cs" };
                }
            }
        }
    }

    /// <summary>从 <c>Component&lt;T&gt;()</c> 产出的元素里读回那个组件的类型名。</summary>
    private static string TypeNameOf(Func<Element> build)
    {
        // ComponentElement<TProps> 也是 ComponentElement 的子类，一个分支就够。
        return build() is ComponentElement component ? component.ComponentType.Name : string.Empty;
    }
}
