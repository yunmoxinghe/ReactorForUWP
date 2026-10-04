using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// WinUI 模板向元素的工厂方法（<see cref="Factories"/> 的 partial 延续）。
/// 命名对齐官方 Dsl：控件名即方法名。
/// </summary>
public static partial class Factories
{
    // ── 链接与图标 ─────────────────────────────────────────────

    /// <summary>超链接按钮（纯文本）。<paramref name="navigateUri"/> 非空时点击由系统打开浏览器。</summary>
    public static HyperlinkButtonElement HyperlinkButton(
        string label,
        Action? onClick = null,
        string? navigateUri = null) =>
        new(label, null, navigateUri, onClick);

    /// <summary>超链接按钮（任意内容，例如 FontIcon + TextBlock）。</summary>
    public static HyperlinkButtonElement HyperlinkButton(
        Element content,
        Action? onClick = null,
        string? navigateUri = null) =>
        new(null, content, navigateUri, onClick);

    /// <summary>字体图标（Segoe Fluent Icons glyph，如 <c>"\uE80F"</c>）。</summary>
    public static FontIconElement FontIcon(string glyph, string? fontFamily = null, double? fontSize = null) =>
        new(glyph) { FontFamily = fontFamily, FontSize = fontSize };

    /// <summary>位图图标（ms-appx 相对路径或绝对 URI）。</summary>
    public static BitmapIconElement BitmapIcon(string uriSource, bool showAsMonochrome = true) =>
        new(uriSource) { ShowAsMonochrome = showAsMonochrome };

    // ── 面包屑 ────────────────────────────────────────────────

    /// <summary>WinUI 2 的 BreadcrumbBar：字符串路径。</summary>
    public static BreadcrumbBarElement BreadcrumbBar(
        IReadOnlyList<string> items,
        Action<int>? onItemClicked = null,
        double? itemFontSize = null,
        string? itemStyleKey = null) =>
        new(items)
        {
            OnItemClicked = onItemClicked,
            ItemFontSize = itemFontSize,
            ItemStyleKey = itemStyleKey,
        };

    /// <summary><see cref="BreadcrumbBar(IReadOnlyList{string}, Action{int}, double?, string?)"/> 的 params 重载。</summary>
    public static BreadcrumbBarElement BreadcrumbBar(
        Action<int>? onItemClicked,
        double? itemFontSize = null,
        string? itemStyleKey = null,
        params string[] items) =>
        new(items)
        {
            OnItemClicked = onItemClicked,
            ItemFontSize = itemFontSize,
            ItemStyleKey = itemStyleKey,
        };

    // ── 展开器 ────────────────────────────────────────────────

    /// <summary>
    /// WinUI 2 的 Expander。
    /// 注意 WinUI 2 的 Expander 没有 Items 集合（那是 WinUI 3 的能力），
    /// 需要"展开区放多张卡片"时用 <see cref="SettingsExpander"/>。
    /// </summary>
    public static ExpanderElement Expander(
        string? header = null,
        Element? content = null,
        bool isExpanded = false,
        Element? headerIcon = null) =>
        new(header, content)
        {
            IsExpanded = isExpanded,
            HeaderIcon = headerIcon,
        };

    // ── 设置卡片（CommunityToolkit）───────────────────────────

    /// <summary>设置页的一行设置项。</summary>
    public static SettingsCardElement SettingsCard(
        string? header = null,
        Element? content = null,
        string? description = null,
        Element? headerIcon = null,
        SettingsCardContentAlignment contentAlignment = SettingsCardContentAlignment.Right,
        Action? onClick = null) =>
        new(header, content)
        {
            Description = description,
            HeaderIcon = headerIcon,
            ContentAlignment = contentAlignment,
            OnClick = onClick,
        };

    /// <summary>可展开的设置卡片；<paramref name="items"/> 里的每一项成为展开区里的一张卡片。</summary>
    public static SettingsExpanderElement SettingsExpander(
        string? header = null,
        Element? content = null,
        string? description = null,
        Element? headerIcon = null,
        bool isExpanded = false,
        SettingsCardContentAlignment contentAlignment = SettingsCardContentAlignment.Right,
        params Element?[] items) =>
        new(header, content, FilterChildren(items))
        {
            Description = description,
            HeaderIcon = headerIcon,
            IsExpanded = isExpanded,
            ContentAlignment = contentAlignment,
        };

    // ── 虚拟化列表 ────────────────────────────────────────────

    /// <summary>
    /// 虚拟化列表：只为可视区域（含上下缓冲）创建真实控件，滚出视野即卸载。
    /// </summary>
    /// <param name="items">数据源（任意对象）。</param>
    /// <param name="itemTemplate">项 → 元素树的构建委托（第二参数是下标）。</param>
    /// <param name="itemHeight">项高度（等高是该实现的前提）。</param>
    /// <param name="height">容器高度；null 表示撑满父容器。</param>
    /// <param name="buffer">视窗外额外渲染的项数（上下各 buffer 项）。</param>
    /// <param name="itemKey">
    /// 项的稳定身份选择器。数据源会发生插入/删除/移动时<b>必须</b>给，
    /// 否则下标位移会让已挂载的项拿错内容；只追加或整体替换的场景可以不给。
    /// </param>
    public static VirtualizingListElement VirtualizingList(
        IReadOnlyList<object?> items,
        Func<object?, int, Element> itemTemplate,
        double itemHeight,
        double? height = null,
        int buffer = 4,
        Func<object?, object?>? itemKey = null) =>
        new(items, itemTemplate, itemHeight) { Height = height, Buffer = buffer, ItemKey = itemKey };

    // ── 弹窗 ──────────────────────────────────────────────────

    /// <summary>
    /// 模态弹窗（<see cref="ContentDialogElement"/>）。
    /// 声明完交给 <see cref="ReactorDialog.ShowAsync"/> 弹出：
    /// <c>await ReactorDialog.ShowAsync(ContentDialog("标题", "是否允许？", primaryButtonText: "是"))</c>。
    /// </summary>
    public static ContentDialogElement ContentDialog(
        string? title = null,
        string? message = null,
        Element? content = null,
        string? primaryButtonText = null,
        string? secondaryButtonText = null,
        string? closeButtonText = null,
        ContentDialogButton defaultButton = ContentDialogButton.Primary,
        double? messageFontSize = null,
        Action<ContentDialogResult>? onResult = null) =>
        new(title, content, message)
        {
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = secondaryButtonText,
            CloseButtonText = closeButtonText,
            DefaultButton = defaultButton,
            MessageFontSize = messageFontSize,
            OnResult = onResult,
        };

    // ── 页面容器 ──────────────────────────────────────────────

    /// <summary>
    /// 内容页容器（对应 XAML 的 Frame）。
    /// </summary>
    /// <param name="transition">页面切换过渡（默认淡入上移）。</param>
    /// <param name="stackDepth">
    /// 当前页面栈深度（<c>1</c> = 只有起始页）。传了它，返回时会走返回方向的过渡
    /// 并播 <c>ElementSoundKind.GoBack</c> 音效，与 XAML 的 <c>Frame.GoBack()</c>
    /// 对齐；不传（默认 <c>-1</c>）则一律按前进处理。
    /// </param>
    public static FrameElement Frame(
        Element? content,
        PageTransition transition = PageTransition.Entrance,
        int stackDepth = -1) =>
        new(content) { Transition = transition, StackDepth = stackDepth };

    // ── 导航视图（模板向重载）─────────────────────────────────

    /// <summary>
    /// NavigationView：显式指定菜单显示方式、返回按钮与回调。
    /// 默认 <see cref="NavPaneDisplayMode.Left"/>（WinUI 设置类 App 的标准形态）。
    /// </summary>
    public static NavigationViewElement NavigationView(
        Element? content,
        IReadOnlyList<NavigationViewItemData> menuItems,
        NavPaneDisplayMode paneDisplayMode = NavPaneDisplayMode.Left,
        int selectedIndex = 0,
        Action<int>? onSelectedIndexChanged = null,
        Action<int>? onItemInvoked = null,
        Action? onBackRequested = null,
        bool isBackButtonVisible = false,
        bool isBackEnabled = false,
        bool isSettingsVisible = true,
        string? header = null) =>
        new(content, menuItems)
        {
            PaneDisplayMode = paneDisplayMode,
            SelectedIndex = selectedIndex,
            OnSelectedIndexChanged = onSelectedIndexChanged,
            OnItemInvoked = onItemInvoked,
            OnBackRequested = onBackRequested,
            IsBackButtonVisible = isBackButtonVisible,
            IsBackEnabled = isBackEnabled,
            IsSettingsVisible = isSettingsVisible,
            Header = header,
        };
}
