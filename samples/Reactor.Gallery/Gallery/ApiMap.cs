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
/// 索引那份数据会被 20 多份文档链接淹没，而真正要紧的层级关系反而看不清。
/// 分开放之后两边各自可审：改索引不用碰链接，补链接不用动索引。
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

    private static readonly Dictionary<string, ApiReference> Map = new(StringComparer.Ordinal)
    {
        // ── 文本与提示 ──────────────────────────────────────────────────
        ["text-block"] = new(
            new[] { "Windows.UI.Xaml.Controls.TextBlock" },
            Uwp + "textblock"),

        ["info-bar"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.InfoBar" },
            Mux + "infobar"),

        ["info-badge"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.InfoBadge" },
            Mux + "infobadge"),

        ["rich-text"] = new(
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

        // ── 按钮 ────────────────────────────────────────────────────────
        ["button"] = new(
            new[] { "Windows.UI.Xaml.Controls.Button" },
            Uwp + "button"),

        ["hyperlink"] = new(
            new[] { "Windows.UI.Xaml.Controls.HyperlinkButton" },
            Uwp + "hyperlinkbutton"),

        ["menus"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.DropDownButton",
                "Microsoft.UI.Xaml.Controls.SplitButton",
                "Windows.UI.Xaml.Controls.MenuFlyout",
                "Windows.UI.Xaml.Controls.MenuFlyoutItem",
                "Windows.UI.Xaml.Controls.MenuFlyoutSeparator",
                // 子菜单是 UWP 原生，且它继承的是 MenuFlyoutItemBase 而不是
                // MenuFlyoutItem —— 官方这个划分照抄，不顺手统一。
                "Windows.UI.Xaml.Controls.MenuFlyoutSubItem",
                // 两种可勾选项官方自己就分在两处：一个是 UWP 原生、一个是 WinUI 2，
                // 且后者不继承前者。
                "Windows.UI.Xaml.Controls.ToggleMenuFlyoutItem",
                "Microsoft.UI.Xaml.Controls.RadioMenuFlyoutItem",
            },
            Mux + "dropdownbutton"),

        ["flyout"] = new(
            new[] { "Windows.UI.Xaml.Controls.Flyout" },
            Uwp + "flyout"),

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

        // ── 日期与时间 ──────────────────────────────────────────────────
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

        // ── 输入与选择 ──────────────────────────────────────────────────
        ["text-box"] = new(
            new[] { "Windows.UI.Xaml.Controls.TextBox" },
            Uwp + "textbox"),

        ["auto-suggest"] = new(
            new[] { "Windows.UI.Xaml.Controls.AutoSuggestBox" },
            Uwp + "autosuggestbox"),

        ["toggles"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.CheckBox",
                "Windows.UI.Xaml.Controls.ToggleSwitch",
            },
            Uwp + "checkbox"),

        ["choice"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.ComboBox",
                "Microsoft.UI.Xaml.Controls.RadioButtons",
            },
            Uwp + "combobox"),

        ["numbers"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.Slider",
                "Microsoft.UI.Xaml.Controls.NumberBox",
                "Windows.UI.Xaml.Controls.ProgressBar",
                "Microsoft.UI.Xaml.Controls.ProgressRing",
            },
            Uwp + "slider"),

        ["password-box"] = new(
            new[] { "Windows.UI.Xaml.Controls.PasswordBox" },
            Uwp + "passwordbox"),

        ["rating"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.RatingControl" },
            Mux + "ratingcontrol"),

        ["color-picker"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.ColorPicker" },
            Mux + "colorpicker"),

        // ── 命令与外壳 ──────────────────────────────────────────────────
        ["command-bar"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.CommandBar",
                "Windows.UI.Xaml.Controls.AppBarButton",
                "Windows.UI.Xaml.Controls.AppBarSeparator",
                "Windows.UI.Xaml.Controls.ICommandBarElement",
            },
            Uwp + "commandbar"),

        ["app-bar-toggle-button"] = new(
            new[] { "Windows.UI.Xaml.Controls.AppBarToggleButton" },
            Uwp + "appbartogglebutton"),

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

        ["split-view"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.SplitView",
                // 面板背景那一块：收的是 Brush（亚克力是 UWP 原生画笔，不是 WinUI 的）。
                "Windows.UI.Xaml.Media.AcrylicBrush",
            },
            Uwp + "splitview"),

        ["navigation-view"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.NavigationView",
                "Microsoft.UI.Xaml.Controls.NavigationViewItem",
            },
            Mux + "navigationview"),

        ["swipe-control"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.SwipeControl",
                "Microsoft.UI.Xaml.Controls.SwipeItems",
                "Microsoft.UI.Xaml.Controls.SwipeItem",
            },
            Mux + "swipecontrol"),

        // ── 集合与虚拟化 ────────────────────────────────────────────────
        ["lists"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.ListView",
                "Windows.UI.Xaml.Controls.GridView",
            },
            Uwp + "listview"),

        ["virtualizing"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.ItemsRepeater",
                "Windows.UI.Xaml.Controls.ScrollViewer",
            },
            Mux + "itemsrepeater"),

        ["list-box"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.ListBox",
                "Windows.UI.Xaml.Controls.Primitives.Selector",
            },
            Uwp + "listbox"),

        ["flip-view"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.FlipView",
                "Windows.UI.Xaml.Controls.Primitives.Selector",
            },
            Uwp + "flipview"),

        // TreeView 在 WinUI 2 里（UWP 没有同名控件），文档页指向 WinUI 3 同名控件。
        ["tree-view"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.TreeView",
                "Microsoft.UI.Xaml.Controls.TreeViewNode",
            },
            Mux + "treeview"),

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

        // ── 布局与容器 ──────────────────────────────────────────────────
        ["grid"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.Grid",
                "Windows.UI.Xaml.Controls.Border",
            },
            Uwp + "grid"),

        // 布局补完：四个都是 UWP 原生（WinUI 2 / 3 没有另做一套面板）
        ["canvas"] = new(
            new[] { "Windows.UI.Xaml.Controls.Canvas" },
            Uwp + "canvas"),

        ["viewbox"] = new(
            new[] { "Windows.UI.Xaml.Controls.Viewbox" },
            Uwp + "viewbox"),

        ["wrap-grid"] = new(
            new[] { "Windows.UI.Xaml.Controls.VariableSizedWrapGrid" },
            Uwp + "variablesizedwrapgrid"),

        ["relative-panel"] = new(
            new[] { "Windows.UI.Xaml.Controls.RelativePanel" },
            Uwp + "relativepanel"),

        ["parallax-view"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.ParallaxView" },
            Mux + "parallaxview"),

        ["expander"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.Expander" },
            Mux + "expander"),

        ["tab-view"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.TabView" },
            Mux + "tabview"),

        ["pivot"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.Pivot",
                "Windows.UI.Xaml.Controls.PivotItem",
            },
            Uwp + "pivot"),

        ["breadcrumb"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.BreadcrumbBar" },
            Mux + "breadcrumbbar"),

        ["scroll-viewer"] = new(
            new[] { "Windows.UI.Xaml.Controls.ScrollViewer" },
            Uwp + "scrollviewer"),

        ["two-pane-view"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.TwoPaneView" },
            Mux + "twopaneview"),

        ["dialog"] = new(
            new[] { "Windows.UI.Xaml.Controls.ContentDialog" },
            Uwp + "contentdialog"),

        // ── 媒体、图像与图标 ────────────────────────────────────────────
        ["image"] = new(
            new[] { "Windows.UI.Xaml.Controls.Image" },
            Uwp + "image"),

        ["icons"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.FontIcon",
                "Windows.UI.Xaml.Controls.BitmapIcon",
                // ImageIcon 是 WinUI 2 的（UWP 原生那一族里没有它）：
                // 内部是 Image，按尺寸缩放、画原色。
                "Microsoft.UI.Xaml.Controls.ImageIcon",
                "Windows.UI.Xaml.Controls.IconElement",
            },
            Uwp + "fonticon"),

        ["shapes"] = new(
            new[]
            {
                "Windows.UI.Xaml.Shapes.Shape",
                "Windows.UI.Xaml.Shapes.Ellipse",
                "Windows.UI.Xaml.Shapes.Rectangle",
                "Windows.UI.Xaml.Shapes.Line",
                // 描边与填充收的都是 Brush，虚线段长按官方的集合类型下发。
                "Windows.UI.Xaml.Media.Brush",
                "Windows.UI.Xaml.Media.DoubleCollection",
            },
            Shapes + "shape"),

        ["person-picture"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.PersonPicture" },
            Mux + "personpicture"),

        // ── 状态与信息 ──────────────────────────────────────────────────
        ["progress-bar"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.ProgressBar" },
            Mux + "progressbar"),

        ["progress-ring"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.ProgressRing" },
            Mux + "progressring"),

        ["refresh-container"] = new(
            new[]
            {
                "Microsoft.UI.Xaml.Controls.RefreshContainer",
                "Microsoft.UI.Xaml.Controls.RefreshVisualizer",
            },
            Mux + "refreshcontainer"),

        ["teaching-tip"] = new(
            new[] { "Microsoft.UI.Xaml.Controls.TeachingTip" },
            Mux + "teachingtip"),

        ["tool-tip"] = new(
            new[]
            {
                "Windows.UI.Xaml.Controls.ToolTip",
                "Windows.UI.Xaml.Controls.ToolTipService",
            },
            Uwp + "tooltip"),

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
