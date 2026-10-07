using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 外壳与命令：<c>SplitView</c> / <c>CommandBar</c> / <c>MenuBar</c>。
/// </summary>
/// <remarks>
/// 这一组对应 WinUI 3 Gallery 里「Navigation」「Menus &amp; toolbars」两页的那几个控件。
/// <c>SplitView</c> 与 <c>CommandBar</c> 是 <c>Windows.UI.Xaml.Controls</c>（UWP 原生），
/// <c>MenuBar</c> 是 <c>Microsoft.UI.Xaml.Controls</c>（WinUI 2）——三个都是真控件，
/// 没有一个是"自己拼出来的替代品"。
/// </remarks>
public static partial class Factories
{
    /// <summary>
    /// 分栏外壳。
    /// </summary>
    /// <param name="pane">侧边面板（第一个槽位）。</param>
    /// <param name="content">主内容区（第二个槽位）。</param>
    /// <param name="isPaneOpen">面板是否展开。<b>非受控</b>，理由见 <see cref="SplitViewElement"/>。</param>
    /// <param name="displayMode">面板形态：覆盖 / 紧凑 / 并排。</param>
    /// <param name="openPaneLength">展开宽度。</param>
    /// <param name="compactPaneLength">紧凑宽度。</param>
    /// <param name="panePlacement">面板在左还是右。</param>
    /// <param name="paneBackground">
    /// 面板那一块的背景（<c>Brush</c>）。要亚克力给 <see cref="AcrylicBrush"/>，
    /// 要纯色给 <c>new SolidColorBrush(color)</c>——它是刷子，不是元素。
    /// </param>
    public static SplitViewElement SplitView(
        Element? pane = null,
        Element? content = null,
        bool isPaneOpen = false,
        SplitViewDisplayMode displayMode = SplitViewDisplayMode.Overlay,
        double openPaneLength = 320,
        double compactPaneLength = 48,
        SplitViewPanePlacement panePlacement = SplitViewPanePlacement.Left,
        Windows.UI.Xaml.Media.Brush? paneBackground = null) =>
        new(pane, content)
        {
            IsPaneOpen = isPaneOpen,
            DisplayMode = displayMode,
            OpenPaneLength = openPaneLength,
            CompactPaneLength = compactPaneLength,
            PanePlacement = panePlacement,
            PaneBackground = paneBackground,
        };

    /// <summary>
    /// 命令条。
    /// </summary>
    /// <param name="content">命令条下面那片内容区（它继承 <c>ContentControl</c>）。</param>
    /// <param name="primary">主命令区，放 <see cref="AppBarButton"/> / <see cref="AppBarSeparator"/>。</param>
    /// <param name="secondary">次命令区（收进 <c>...</c> 溢出菜单的那些）。</param>
    /// <param name="defaultLabelPosition">标签相对图标的位置。</param>
    /// <param name="isSticky">常驻：点空白处不收起。</param>
    public static CommandBarElement CommandBar(
        Element? content = null,
        Element?[]? primary = null,
        Element?[]? secondary = null,
        CommandBarDefaultLabelPosition defaultLabelPosition = CommandBarDefaultLabelPosition.Right,
        CommandBarOverflowButtonVisibility overflowButtonVisibility =
            CommandBarOverflowButtonVisibility.Auto,
        bool isSticky = false) =>
        new(content, primary, secondary)
        {
            DefaultLabelPosition = defaultLabelPosition,
            OverflowButtonVisibility = overflowButtonVisibility,
            IsSticky = isSticky,
        };

    /// <summary>命令条上的一个按钮。</summary>
    /// <param name="label">按钮文字（官方 <c>Label</c>；给 null 就是纯图标按钮）。</param>
    /// <param name="icon">图标（<c>FontIcon</c> / <c>BitmapIcon</c>）。</param>
    /// <param name="onClick">点击回调。</param>
    /// <param name="isEnabled">禁用态。</param>
    public static AppBarButtonElement AppBarButton(
        string? label = null,
        Element? icon = null,
        Action? onClick = null,
        bool? isEnabled = null) =>
        new(label) { Icon = icon, OnClick = onClick, IsEnabled = isEnabled };

    /// <summary>命令条按钮的便捷重载：<paramref name="glyph"/> 直接给 Segoe MDL2 码位，省一层 <c>FontIcon</c>。</summary>
    public static AppBarButtonElement AppBarButton(
        string? label,
        string glyph,
        Action? onClick = null,
        bool? isEnabled = null) =>
        AppBarButton(label, FontIcon(glyph), onClick, isEnabled);

    /// <summary>命令条上的分隔线。</summary>
    public static AppBarSeparatorElement AppBarSeparator() => new();

    /// <summary>
    /// 命令条上的开关按钮。
    /// </summary>
    /// <param name="label">按钮文字。</param>
    /// <param name="icon">图标（<c>FontIcon</c> / <c>BitmapIcon</c> / <c>SymbolIcon</c>）。</param>
    /// <param name="isChecked">选中态。<b>受控</b>：给了值才受控，不给就是"不管它"。</param>
    /// <param name="onIsCheckedChanged">状态变化回调（中间态不回调）。</param>
    /// <param name="isEnabled">禁用态。</param>
    public static AppBarToggleButtonElement AppBarToggleButton(
        string? label = null,
        Element? icon = null,
        bool isChecked = false,
        Action<bool>? onIsCheckedChanged = null,
        bool? isEnabled = null) =>
        new(label)
        {
            Icon = icon,
            IsChecked = isChecked,
            OnIsCheckedChanged = onIsCheckedChanged,
            IsEnabled = isEnabled,
        };

    /// <summary>命令条开关按钮的便捷重载：<paramref name="glyph"/> 直接给 Segoe MDL2 码位。</summary>
    public static AppBarToggleButtonElement AppBarToggleButton(
        string? label,
        string glyph,
        bool isChecked = false,
        Action<bool>? onIsCheckedChanged = null,
        bool? isEnabled = null) =>
        AppBarToggleButton(label, FontIcon(glyph), isChecked, onIsCheckedChanged, isEnabled);

    /// <summary>
    /// 命令条浮层（WinUI 2 的 <c>CommandBarFlyout</c>）。
    /// </summary>
    /// <param name="primary">主要命令，横排可见。</param>
    /// <param name="secondary">次要命令，收进 <c>...</c> 溢出菜单。</param>
    /// <param name="alwaysExpanded">次要命令平铺、不收进溢出菜单。</param>
    /// <remarks>
    /// 它挂在别人身上（按钮的 <c>Flyout</c> 或元素的 <c>ContextMenu</c>），
    /// 自己不能单独站位——理由见 <see cref="CommandBarFlyoutElement"/>。
    /// </remarks>
    public static CommandBarFlyoutElement CommandBarFlyout(
        Element?[]? primary = null,
        Element?[]? secondary = null,
        bool alwaysExpanded = false) =>
        new(primary, secondary) { AlwaysExpanded = alwaysExpanded };

    /// <summary>
    /// 文本命令条（WinUI 2 的 <c>TextCommandBarFlyout</c>）：挂在文本控件上，
    /// 剪贴板那几条命令<b>由控件自己按选区状态填</b>。
    /// </summary>
    /// <param name="primary">
    /// 自定义的主要命令（横排可见）。官方那几条剪贴板命令由控件自己补，
    /// 与这里给的并存——不填也能用，只是没有自定义命令。
    /// </param>
    /// <param name="secondary">自定义的次要命令，收进 <c>...</c> 溢出菜单。</param>
    /// <param name="alwaysExpanded">次要命令平铺、不收进溢出菜单。</param>
    /// <remarks>
    /// 它挂在文本控件上（<c>.SelectionFlyout(...)</c> 或 <c>ContextMenu</c>）——
    /// 理由见 <see cref="TextCommandBarFlyoutElement"/>。
    /// </remarks>
    public static TextCommandBarFlyoutElement TextCommandBarFlyout(
        Element?[]? primary = null,
        Element?[]? secondary = null,
        bool alwaysExpanded = false) =>
        new(primary, secondary) { AlwaysExpanded = alwaysExpanded };

    /// <summary>菜单栏。<paramref name="items"/> 里放 <see cref="Menu"/> 造出来的组。</summary>
    public static MenuBarElement MenuBar(params MenuBarItemData[] items) => new(items);

    /// <summary>菜单栏里的一组。<paramref name="items"/> 里放 <see cref="MenuItem"/> / <see cref="MenuSeparator"/>。</summary>
    public static MenuBarItemData Menu(string title, params Element?[] items) => new(title, items);
}
