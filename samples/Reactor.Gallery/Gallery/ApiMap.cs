using System;
using System.Collections.Generic;

namespace Reactor.Gallery;

/// <summary>
/// <b>索引条目 → 官方 WinUI / UWP 类型</b>的对应表。
/// </summary>
/// <remarks>
/// 为什么要单独一张表、而不是把字段挂到 <see cref="GalleryItem"/> 上：
/// 这是<b>两种不同性质的信息</b>。条目说的是"画廊里有什么、怎么排"，
/// 本表说的是"这个样例背后那个控件在官方叫什么、文档在哪"。混在一起之后
/// 索引那份数据会被 80 多条文档链接淹没，而真正要紧的层级关系反而看不清。
/// 分开放之后两边各自可审：改索引不用碰链接，补链接不用动索引。
/// <para>
/// <b>键必须等于索引里的条目 id</b>：<c>GalleryIndexTests</c> 会反查这一条——
/// 索引删了某个条目而表里那行还在，界面上就永远不可能显示它，也不会有人
/// 发现它是死的。
/// </para>
/// <para>
/// <b>链接指向哪里。</b><c>Microsoft.UI.Xaml.Controls.*</c> 那几个控件在 UWP 侧
/// 是由 <b>WinUI 2</b>（WinUI 2 for UWP）提供的，类型与 Windows App SDK 里的
/// WinUI 3 同名、API 形状基本一致，但文档站只维护 WinUI 3 那份 API 参考页
/// （WinUI 2 的文档页已经合并进去）。所以这里一律指向
/// <c>windows/windows-app-sdk/api/winrt/</c> 下<b>同名类型</b>的页面，
/// 并在界面上写明这一点——不写就等于让读者以为自己看的是 WinUI 2 专页。
/// </para>
/// </remarks>

/// <summary>一个条目用到的官方类型，以及文档页。</summary>
/// <param name="Apis">官方类型全名。第一个是"主角"，其余是配套（图标、内容宿主等）。</param>
/// <param name="Docs">learn.microsoft.com 上的 API 参考页。</param>
internal sealed record ApiReference(string[] Apis, string Docs);

internal static class ApiMap
{
    /// <summary>UWP 原生控件的 API 参考页前缀。</summary>
    private const string Uwp = "https://learn.microsoft.com/uwp/api/windows.ui.xaml.controls.";

    /// <summary>
    /// WinUI 控件（<c>Microsoft.UI.Xaml.Controls</c>）的 API 参考页前缀。
    /// 指向 WinUI 3 的同名类型——见类型注释里那段关于文档站的说明。
    /// </summary>
    private const string Mux =
        "https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.";

    /// <summary>
    /// 形状（<c>Windows.UI.Xaml.Shapes</c>）的 API 参考页前缀。
    /// </summary>
    /// <remarks>
    /// 与上面两个同形，只是<b>命名空间不同</b>：官方把形状单独放在
    /// <c>Shapes</c> 命名空间下（<c>Windows.UI.Xaml.Shapes.Shape</c>），
    /// 硬塞进 <see cref="Uwp"/> 那个 <c>controls.</c> 前缀会拼出一个不存在的 URL。
    /// 加一个前缀常量而不是手写整条链接，是为了让"链接必须走前缀"那条契约
    /// 仍然管得住它（见 <c>GalleryIndexTests</c>）。
    /// </remarks>
    private const string Shapes = "https://learn.microsoft.com/uwp/api/windows.ui.xaml.shapes.";

    /// <summary>
    /// 画笔（<c>Windows.UI.Xaml.Media</c>）的 API 参考页前缀。
    /// </summary>
    /// <remarks>
    /// 第四个前缀，理由与 <see cref="Shapes"/> 完全一样：亚克力是
    /// <c>Windows.UI.Xaml.Media.AcrylicBrush</c>，套 <c>controls.</c>
    /// 会拼出不存在的页面。与其为它开一个手写链接的口子，不如多认一个前缀常量。
    /// </remarks>
    private const string Media = "https://learn.microsoft.com/uwp/api/windows.ui.xaml.media.";

    /// <summary>
    /// WinUI 画笔（<c>Microsoft.UI.Xaml.Media</c>）的 API 参考页前缀。
    /// </summary>
    /// <remarks>
    /// 第五个前缀。与 <see cref="Mux"/> 同形、理由也相同（文档站只维护 WinUI 3
    /// 那份），差别只落在命名空间：<b>径向渐变是 WinUI 的一员</b>——
    /// UWP 的 <c>Media</c> 里只有线性的 <c>LinearGradientBrush</c>，没有径向那一份。
    /// </remarks>
    private const string MuxMedia =
        "https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.";

    private static readonly Dictionary<string, ApiReference> Map = new(StringComparer.Ordinal)
    {
        // ── 命令与工具栏（Menus & toolbars）──────────────────────────────
        ["app-bar-button"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.AppBarButton",
                // 分隔线是另一个类型（命令条里的那条线，不是菜单里的 MenuFlyoutSeparator）。
                "Windows.UI.Xaml.Controls.AppBarSeparator",
                "Windows.UI.Xaml.Controls.ICommandBarElement",
            },
            Uwp + "appbarbutton"),

        ["app-bar-toggle-button"] = new(
            new[] { "Windows.UI.Xaml.Controls.AppBarToggleButton" },
            Uwp + "appbartogglebutton"),

        ["command-bar"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.CommandBar",
                "Windows.UI.Xaml.Controls.AppBarButton",
                "Windows.UI.Xaml.Controls.AppBarSeparator",
                "Windows.UI.Xaml.Controls.ICommandBarElement",
            },
            Uwp + "commandbar"),

        ["command-bar-flyout"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.CommandBarFlyout",
                "Windows.UI.Xaml.Controls.AppBarButton",
            },
            Mux + "commandbarflyout"),

        // 文本命令条是 CommandBarFlyout 的子类（WinUI 2），命令项与命令条同形：
        // AppBarButton / AppBarToggleButton 是 UWP 原生。
        ["text-command-bar-flyout"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.TextCommandBarFlyout",
                "Windows.UI.Xaml.Controls.AppBarButton",
                "Windows.UI.Xaml.Controls.AppBarToggleButton",
                // 槽位：SelectionFlyout 是文本控件自带的属性（UWP 原生），
                // 不在 UIElement 上。
                "Windows.UI.Xaml.Controls.TextBox",
                "Windows.UI.Xaml.Controls.RichEditBox",
            },
            Mux + "textcommandbarflyout"),

        ["menu-bar"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.MenuBar",
                "Microsoft.UI.Xaml.Controls.MenuBarItem",
                "Windows.UI.Xaml.Controls.MenuFlyoutItem",
            },
            Mux + "menubar"),

        ["menu-flyout"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.MenuFlyout",
                "Windows.UI.Xaml.Controls.MenuFlyoutItem",
                "Windows.UI.Xaml.Controls.MenuFlyoutSeparator",
                "Windows.UI.Xaml.Controls.MenuFlyoutSubItem",
                // 两种可勾选项官方自己就分在两处：一个是 UWP 原生、一个是 WinUI 2，
                // 且后者不继承前者。
                "Windows.UI.Xaml.Controls.ToggleMenuFlyoutItem",
                "Microsoft.UI.Xaml.Controls.RadioMenuFlyoutItem",
            },
            Uwp + "menuflyout"),

        ["swipe-control"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.SwipeControl",
                "Microsoft.UI.Xaml.Controls.SwipeItems",
                "Microsoft.UI.Xaml.Controls.SwipeItem",
            },
            Mux + "swipecontrol"),

        // ── 集合与虚拟化（Collections）──────────────────────────────────
        ["list-view"] = new(
            new[] { "Windows.UI.Xaml.Controls.ListView" },
            Uwp + "listview"),

        ["grid-view"] = new(
            new[] { "Windows.UI.Xaml.Controls.GridView" },
            Uwp + "gridview"),

        ["flip-view"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.FlipView",
                "Windows.UI.Xaml.Controls.Primitives.Selector",
            },
            Uwp + "flipview"),

        ["list-box"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.ListBox",
                "Windows.UI.Xaml.Controls.Primitives.Selector",
            },
            Uwp + "listbox"),

        // TreeView 在 WinUI 2 里（UWP 没有同名控件），文档页指向 WinUI 3 同名控件。
        ["tree-view"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.TreeView",
                "Microsoft.UI.Xaml.Controls.TreeViewNode",
            },
            Mux + "treeview"),

        ["items-repeater"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.ItemsRepeater",
                "Windows.UI.Xaml.Controls.ScrollViewer",
            },
            Mux + "itemsrepeater"),

        ["pull-to-refresh"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.RefreshContainer",
                "Microsoft.UI.Xaml.Controls.RefreshVisualizer",
            },
            Mux + "refreshcontainer"),

        // ── 日期与时间（Date & time）────────────────────────────────────
        // 四个都是 UWP 原生控件：WinUI 2 没有另做一套日期时间控件，
        // Gallery 里那一页在这几个控件上指的就是这几个。
        ["date-picker"] = new(
            new[] { "Windows.UI.Xaml.Controls.DatePicker" },
            Uwp + "datepicker"),

        ["time-picker"] = new(
            new[] { "Windows.UI.Xaml.Controls.TimePicker" },
            Uwp + "timepicker"),

        ["calendar-date-picker"] = new(
            new[] { "Windows.UI.Xaml.Controls.CalendarDatePicker" },
            Uwp + "calendardatepicker"),

        ["calendar-view"] = new(
            new[] { "Windows.UI.Xaml.Controls.CalendarView" },
            Uwp + "calendarview"),

        // ── 基础输入（Basic input）──────────────────────────────────────
        ["button"] = new(
            new[] { "Windows.UI.Xaml.Controls.Button" },
            Uwp + "button"),

        ["drop-down-button"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.DropDownButton",
                "Windows.UI.Xaml.Controls.MenuFlyout",
            },
            Mux + "dropdownbutton"),

        ["split-button"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.SplitButton",
                "Windows.UI.Xaml.Controls.MenuFlyout",
            },
            Mux + "splitbutton"),

        // ToggleButton / RepeatButton 是 UWP 原生（住在 Controls.Primitives 里的
        // 两个 ButtonBase 派生），ToggleSplitButton 是 WinUI 2。
        ["toggle-button"] = new(
            new[] { "Windows.UI.Xaml.Controls.Primitives.ToggleButton" },
            Uwp + "primitives.togglebutton"),

        ["repeat-button"] = new(
            new[] { "Windows.UI.Xaml.Controls.Primitives.RepeatButton" },
            Uwp + "primitives.repeatbutton"),

        ["toggle-split-button"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.ToggleSplitButton" },
            Mux + "togglesplitbutton"),

        ["hyperlink-button"] = new(
            new[] { "Windows.UI.Xaml.Controls.HyperlinkButton" },
            Uwp + "hyperlinkbutton"),

        ["check-box"] = new(
            new[] { "Windows.UI.Xaml.Controls.CheckBox" },
            Uwp + "checkbox"),

        ["toggle-switch"] = new(
            new[] { "Windows.UI.Xaml.Controls.ToggleSwitch" },
            Uwp + "toggleswitch"),

        ["combo-box"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.ComboBox",
                "Windows.UI.Xaml.Controls.ComboBoxItem",
            },
            Uwp + "combobox"),

        // 主角是 UWP 原生的 RadioButton；RadioButtons 是 WinUI 2 的分组控件。
        ["radio-button"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.RadioButton",
                "Microsoft.UI.Xaml.Controls.RadioButtons",
            },
            Uwp + "radiobutton"),

        ["rating-control"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.RatingControl" },
            Mux + "ratingcontrol"),

        ["color-picker"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.ColorPicker" },
            Mux + "colorpicker"),

        ["slider"] = new(
            new[] { "Windows.UI.Xaml.Controls.Slider" },
            Uwp + "slider"),

        // ── 文本（Text）─────────────────────────────────────────────────
        ["text-block"] = new(
            new[] { "Windows.UI.Xaml.Controls.TextBlock" },
            Uwp + "textblock"),

        ["text-box"] = new(
            new[] { "Windows.UI.Xaml.Controls.TextBox" },
            Uwp + "textbox"),

        ["rich-text-block"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.RichTextBlock",
                "Windows.UI.Xaml.Documents.Paragraph",
                "Windows.UI.Xaml.Documents.Run",
            },
            Uwp + "richtextblock"),

        ["rich-edit-box"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.RichEditBox",
                "Windows.UI.Text.ITextDocument",
            },
            Uwp + "richeditbox"),

        ["password-box"] = new(
            new[] { "Windows.UI.Xaml.Controls.PasswordBox" },
            Uwp + "passwordbox"),

        ["auto-suggest-box"] = new(
            new[] { "Windows.UI.Xaml.Controls.AutoSuggestBox" },
            Uwp + "autosuggestbox"),

        ["number-box"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.NumberBox" },
            Mux + "numberbox"),

        // ── 状态与信息（Status & info）──────────────────────────────────
        ["info-bar"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.InfoBar" },
            Mux + "infobar"),

        ["info-badge"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.InfoBadge" },
            Mux + "infobadge"),

        ["progress-bar"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.ProgressBar" },
            Mux + "progressbar"),

        ["progress-ring"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.ProgressRing" },
            Mux + "progressring"),

        ["tool-tip"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.ToolTip",
                "Windows.UI.Xaml.Controls.ToolTipService",
            },
            Uwp + "tooltip"),

        // ── 对话框与浮出层（Dialogs & flyouts）──────────────────────────
        ["content-dialog"] = new(
            new[] { "Windows.UI.Xaml.Controls.ContentDialog" },
            Uwp + "contentdialog"),

        ["flyout"] = new(
            new[] { "Windows.UI.Xaml.Controls.Flyout" },
            Uwp + "flyout"),

        ["teaching-tip"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.TeachingTip" },
            Mux + "teachingtip"),

        // Popup 是 UWP 原生的 Primitives 成员：它是 FrameworkElement（不是
        // ContentControl），内容在 Child 上——这一层差别在框架里有两处代价，
        // 见 PopupHandler 的类注释。
        ["popup"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.Primitives.Popup",
                "Windows.UI.Xaml.Controls.Primitives.PopupPlacementMode",
            },
            Uwp + "primitives.popup"),

        // ── 滚动与翻页（Scrolling）──────────────────────────────────────
        ["scroll-viewer"] = new(
            new[] { "Windows.UI.Xaml.Controls.ScrollViewer" },
            Uwp + "scrollviewer"),

        ["semantic-zoom"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.SemanticZoom",
                "Windows.UI.Xaml.Controls.ISemanticZoomInformation",
            },
            Uwp + "semanticzoom"),

        ["pips-pager"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.PipsPager" },
            Mux + "pipspager"),

        // ── 布局（Layout）───────────────────────────────────────────────
        ["grid"] = new(
            new[] { "Windows.UI.Xaml.Controls.Grid" },
            Uwp + "grid"),

        ["border"] = new(
            new[] { "Windows.UI.Xaml.Controls.Border" },
            Uwp + "border"),

        ["stack-panel"] = new(
            new[] { "Windows.UI.Xaml.Controls.StackPanel" },
            Uwp + "stackpanel"),

        // 布局补完：几个都是 UWP 原生（WinUI 2 / 3 没有另做一套面板）
        ["canvas"] = new(
            new[] { "Windows.UI.Xaml.Controls.Canvas" },
            Uwp + "canvas"),

        ["relative-panel"] = new(
            new[] { "Windows.UI.Xaml.Controls.RelativePanel" },
            Uwp + "relativepanel"),

        ["variable-sized-wrap-grid"] = new(
            new[] { "Windows.UI.Xaml.Controls.VariableSizedWrapGrid" },
            Uwp + "variablesizedwrapgrid"),

        ["viewbox"] = new(
            new[] { "Windows.UI.Xaml.Controls.Viewbox" },
            Uwp + "viewbox"),

        ["expander"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.Expander" },
            Mux + "expander"),

        ["split-view"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.SplitView",
                // 面板背景那一块：收的是 Brush（亚克力是 UWP 原生画笔，不是 WinUI 的）。
                "Windows.UI.Xaml.Media.AcrylicBrush",
            },
            Uwp + "splitview"),

        ["two-pane-view"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.TwoPaneView" },
            Mux + "twopaneview"),

        // ── 导航（Navigation）───────────────────────────────────────────
        ["navigation-view"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.NavigationView",
                "Microsoft.UI.Xaml.Controls.NavigationViewItem",
            },
            Mux + "navigationview"),

        ["pivot"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.Pivot",
                "Windows.UI.Xaml.Controls.PivotItem",
            },
            Uwp + "pivot"),

        ["tab-view"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.TabView" },
            Mux + "tabview"),

        ["breadcrumb-bar"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.BreadcrumbBar" },
            Mux + "breadcrumbbar"),

        // ── 媒体与图像（Media）──────────────────────────────────────────
        ["image"] = new(
            new[] { "Windows.UI.Xaml.Controls.Image" },
            Uwp + "image"),

        ["person-picture"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.PersonPicture" },
            Mux + "personpicture"),

        // ── 样式与材质（Styles）─────────────────────────────────────────
        ["icon-element"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.IconElement",
                "Windows.UI.Xaml.Controls.SymbolIcon",
                "Windows.UI.Xaml.Controls.FontIcon",
                "Windows.UI.Xaml.Controls.BitmapIcon",
                // ImageIcon 是 WinUI 2 的（UWP 原生那一族里没有它）：
                // 内部是 Image，按尺寸缩放、画原色。
                "Microsoft.UI.Xaml.Controls.ImageIcon",
            },
            Uwp + "iconelement"),

        ["shape"] = new(
            new[]
            {
                "Windows.UI.Xaml.Shapes.Shape",
                "Windows.UI.Xaml.Shapes.Ellipse",
                "Windows.UI.Xaml.Shapes.Rectangle",
                // 描边与填充收的都是 Brush，虚线段长按官方的集合类型下发。
                "Windows.UI.Xaml.Media.Brush",
                "Windows.UI.Xaml.Media.DoubleCollection",
            },
            Shapes + "shape"),

        ["line"] = new(
            new[]
            {
                "Windows.UI.Xaml.Shapes.Line",
                "Windows.UI.Xaml.Shapes.Shape",
                "Windows.UI.Xaml.Media.PenLineCap",
            },
            Shapes + "line"),

        // 亚克力是 Media 命名空间下的画笔：这是第四个前缀常量的由来。
        ["acrylic-brush"] = new(
            new[]
            {
                "Windows.UI.Xaml.Media.AcrylicBrush",
                "Windows.UI.Xaml.Media.AcrylicBackgroundSource",
            },
            Media + "acrylicbrush"),

        // 径向渐变反过来：刷子是 WinUI 2 的（第五个前缀的由来），
        // 而它收的成员——停靠点、SpreadMethod、MappingMode 三个——全是 UWP 的。
        // InterpolationSpace 用的是合成层那个枚举（CompositionColorSpace），
        // 而不是 xaml 命名空间下任何一种"色彩空间"。
        ["radial-gradient-brush"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Media.RadialGradientBrush",
                "Windows.UI.Xaml.Media.GradientStop",
                "Windows.UI.Xaml.Media.GradientStopCollection",
                "Windows.UI.Xaml.Media.GradientSpreadMethod",
                "Windows.UI.Xaml.Media.BrushMappingMode",
                "Windows.UI.Composition.CompositionColorSpace",
            },
            MuxMedia + "radialgradientbrush"),

        // ── 动效（Motion）───────────────────────────────────────────────
        ["parallax-view"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.ParallaxView" },
            Mux + "parallaxview"),

        // ── 写法指南（整页）—— 报的是这一页主要演示的那个官方类型 ────────
        ["getting-started"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.Button",
                "Windows.UI.Xaml.Controls.TextBlock",
            },
            Uwp + "button"),

        ["guide-inputs"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.TextBox",
                "Windows.UI.Xaml.Controls.ComboBox",
            },
            Uwp + "textbox"),

        ["guide-layout"] = new(
            new[] { "Windows.UI.Xaml.Controls.Grid" },
            Uwp + "grid"),

        ["guide-lists"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.ListView",
                "Windows.UI.Xaml.Controls.GridView",
            },
            Uwp + "listview"),

        ["guide-virtualization"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.ItemsRepeater" },
            Mux + "itemsrepeater"),

        ["diagnostics"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.TextBox",
                "Windows.UI.Xaml.Controls.Slider",
            },
            Uwp + "textbox"),

        ["component-props"] = new(
            new[] { "Windows.UI.Xaml.Controls.StackPanel" },
            Uwp + "stackpanel"),

        ["native-interop"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.NumberBox" },
            Mux + "numberbox"),

        ["guide-settings"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.SettingsCard",
                "Microsoft.UI.Xaml.Controls.SettingsExpander",
            },
            Mux + "expander"),
    };

    /// <summary>取某个条目的官方对应；没有登记过返回 <c>null</c>。</summary>
    public static ApiReference? Find(string itemId) =>
        Map.TryGetValue(itemId, out var reference) ? reference : null;

    /// <summary>登记过的全部条目 id —— 契约测试靠它反查"有没有指向不存在条目的死链接"。</summary>
    public static IEnumerable<string> Ids => Map.Keys;
}
