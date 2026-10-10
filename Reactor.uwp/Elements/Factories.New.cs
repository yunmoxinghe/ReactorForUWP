using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 新增元素的工厂方法（<see cref="Factories"/> 的 partial 延续）。
/// 方法名与参数名对齐官方 Microsoft.UI.Reactor 的 Dsl。
/// </summary>
public static partial class Factories
{
    // ── 二维布局 ───────────────────────────────────────────────

    /// <summary>Grid：强类型轨道定义 + 子元素。</summary>
    public static GridElement Grid(
        GridSize[] columns,
        GridSize[] rows,
        params Element?[] children)
    {
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (rows is null) throw new ArgumentNullException(nameof(rows));

        return new GridElement(new GridDefinition(columns, rows), FilterChildren(children));
    }

    /// <summary>Grid：字符串轨道（<c>"Auto"</c> / <c>"*"</c> / <c>"1.5*"</c> / <c>"200"</c>）便捷重载。</summary>
    public static GridElement Grid(
        string[] columns,
        string[] rows,
        params Element?[] children)
    {
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (rows is null) throw new ArgumentNullException(nameof(rows));

        return Grid(
            columns.Select(GridSize.Parse).ToArray(),
            rows.Select(GridSize.Parse).ToArray(),
            children);
    }

    /// <summary>单列多行（常见表单布局）：一列自适应 + 每行 Auto。</summary>
    public static GridElement GridRows(params Element?[] children) =>
        Grid(
            new[] { GridSize.Star() },
            children.Select(_ => GridSize.Auto).ToArray(),
            children);

    /// <summary>带背景/边框的单子元素容器。</summary>
    public static BorderElement Border(Element? child) => new(child);

    // ── 输入与选择 ─────────────────────────────────────────────

    /// <param name="isEditable">可编辑：收起时那个框变成输入框。</param>
    /// <param name="isTextSearchEnabled">敲字时跳到匹配的项（默认开）。</param>
    /// <param name="header">标题（框上方那行小字）。官方 ComboBox 示例里几乎都有。</param>
    /// <param name="placeholderText">没选任何项时框里的灰字。</param>
    public static ComboBoxElement ComboBox(
        string[] items,
        Optional<int> selectedIndex = default,
        Action<int>? onSelectedIndexChanged = null,
        bool isEditable = false,
        bool isTextSearchEnabled = true,
        string? header = null,
        string? placeholderText = null) =>
        new(items, selectedIndex, onSelectedIndexChanged)
        {
            IsEditable = isEditable,
            IsTextSearchEnabled = isTextSearchEnabled,
            Header = header,
            PlaceholderText = placeholderText,
        };

    public static ToggleSwitchElement ToggleSwitch(
        Optional<bool> isOn = default,
        Action<bool>? onIsOnChanged = null,
        string? onContent = null,
        string? offContent = null,
        string? header = null) =>
        new(isOn, onIsOnChanged, onContent, offContent) { Header = header };

    public static RadioButtonElement RadioButton(
        string label,
        Optional<bool> isChecked = default,
        Action<bool>? onIsCheckedChanged = null,
        string? groupName = null) =>
        new(label, isChecked, onIsCheckedChanged, groupName);

    /// <param name="header">标题（整组单选按钮上方那行小字）。</param>
    public static RadioButtonsElement RadioButtons(
        string[] items,
        Optional<int> selectedIndex = default,
        Action<int>? onSelectedIndexChanged = null,
        string? header = null) =>
        new(items, selectedIndex, onSelectedIndexChanged) { Header = header };

    // ── 进度与媒体 ─────────────────────────────────────────────

    /// <summary>
    /// 进度条。<paramref name="value"/> 为 null 表示<b>不确定</b>进度（一条来回扫的横条）。
    /// </summary>
    public static ProgressElement Progress(
        double? value = null,
        double minimum = 0,
        double maximum = 100,
        bool showError = false,
        bool showPaused = false) =>
        new(value) { Minimum = minimum, Maximum = maximum, ShowError = showError, ShowPaused = showPaused };

    /// <summary><see cref="Progress"/> 的别名（与 ProgressBar 控件同名，便于查找）。</summary>
    public static ProgressElement ProgressBar(
        double? value = null,
        double minimum = 0,
        double maximum = 100,
        bool showError = false,
        bool showPaused = false) =>
        Progress(value, minimum, maximum, showError, showPaused);

    /// <summary>
    /// 进度环。<paramref name="value"/> 为 null 表示<b>不确定</b>进度（一直转的圈）。
    /// </summary>
    public static ProgressRingElement ProgressRing(
        double? value = null,
        double minimum = 0,
        double maximum = 100,
        bool isActive = true) =>
        new(value) { Minimum = minimum, Maximum = maximum, IsActive = isActive };

    /// <summary>
    /// 徽章（WinUI 2.8 的 <c>InfoBadge</c>）。
    /// </summary>
    /// <param name="value">数字；<c>-1</c> 是官方"圆点"那一档（不显示数字）。</param>
    /// <param name="icon">图标（<c>FontIcon</c> / <c>BitmapIcon</c>）；给了它就不显示数字。</param>
    /// <param name="badgeStyle">
    /// 预设样式：<c>"Informational"</c> / <c>"Success"</c> / <c>"Warning"</c> /
    /// <c>"Critical"</c> / <c>"Attention"</c>。
    /// </param>
    /// <remarks>
    /// <b>它挂在别人身上，不是一个独立站位的控件。</b>官方用法是把它放进某个
    /// 容器的角落（<c>Grid</c> 的右上角、列表项的右侧），靠对齐与外边距定位——
    /// 它没有"自己是谁的徽章"这层关系，位置完全由布局说了算。
    /// </remarks>
    public static InfoBadgeElement InfoBadge(
        int value = -1,
        Element? icon = null,
        string badgeStyle = "Informational") =>
        new() { Value = value, Icon = icon, BadgeStyle = badgeStyle };

    /// <summary>图片。<paramref name="source"/> 支持 ms-appx:///、http(s) 与相对路径。</summary>
    /// <param name="stretch">
    /// 拉伸方式（XAML 的 <c>Stretch</c>）：<c>"None"</c> / <c>"Fill"</c> /
    /// <c>"Uniform"</c> / <c>"UniformToFill"</c>。给不了就留 null（控件默认
    /// <see cref="Stretch.Uniform"/>）。
    /// </param>
    /// <remarks>
    /// <c>Width</c> / <c>Height</c> / <c>Stretch</c> 放在<b>尾部</b>：加进来之前写的
    /// <c>Image("…")</c> 一行都不用改，而元素上这三个槽位 handler 一直都认。
    /// </remarks>
    public static ImageElement Image(
        string source,
        double? width = null,
        double? height = null,
        string? stretch = null) =>
        new(source) { Width = width, Height = height, Stretch = stretch };

    // ── 集合与导航 ─────────────────────────────────────────────

    public static ListViewElement ListView(params Element?[] items) =>
        new(FilterChildren(items));

    public static ListViewElement ListView(
        Optional<int> selectedIndex,
        Action<int>? onSelectedIndexChanged,
        params Element?[] items) =>
        new(FilterChildren(items))
        {
            SelectedIndex = selectedIndex,
            OnSelectedIndexChanged = onSelectedIndexChanged,
        };

    /// <summary>带 <c>SelectionMode</c> 的重载：改模式会把选中态一起牵动，这个参数
    /// 让示例能直接取证那一发（见第九道契约）。</summary>
    public static ListViewElement ListView(
        Optional<int> selectedIndex,
        Action<int>? onSelectedIndexChanged,
        ListViewSelectionMode selectionMode,
        params Element?[] items) =>
        new(FilterChildren(items))
        {
            SelectedIndex = selectedIndex,
            OnSelectedIndexChanged = onSelectedIndexChanged,
            SelectionMode = selectionMode,
        };

    public static GridViewElement GridView(params Element?[] items) =>
        new(FilterChildren(items));

    public static GridViewElement GridView(
        Optional<int> selectedIndex,
        Action<int>? onSelectedIndexChanged,
        params Element?[] items) =>
        new(FilterChildren(items))
        {
            SelectedIndex = selectedIndex,
            OnSelectedIndexChanged = onSelectedIndexChanged,
        };

    public static NavigationViewElement NavigationView(
        Element? content,
        params NavigationViewItemData[] menuItems) =>
        new(content, menuItems ?? Array.Empty<NavigationViewItemData>());
}
