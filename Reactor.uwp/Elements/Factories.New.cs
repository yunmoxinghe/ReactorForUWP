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

    public static ComboBoxElement ComboBox(
        string[] items,
        Optional<int> selectedIndex = default,
        Action<int>? onSelectedIndexChanged = null) =>
        new(items, selectedIndex, onSelectedIndexChanged);

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

    public static RadioButtonsElement RadioButtons(
        string[] items,
        Optional<int> selectedIndex = default,
        Action<int>? onSelectedIndexChanged = null) =>
        new(items, selectedIndex, onSelectedIndexChanged);

    // ── 进度与媒体 ─────────────────────────────────────────────

    /// <summary>进度条。<paramref name="value"/> 为 null 表示不确定进度。</summary>
    public static ProgressElement Progress(double? value = null) => new(value);

    /// <summary><see cref="Progress"/> 的别名（与 ProgressBar 控件同名，便于查找）。</summary>
    public static ProgressElement ProgressBar(double? value = null) => new(value);

    /// <summary>进度环。<paramref name="value"/> 为 null 表示不确定进度。</summary>
    public static ProgressRingElement ProgressRing(double? value = null) => new(value);

    /// <summary>图片。<paramref name="source"/> 支持 ms-appx:///、http(s) 与相对路径。</summary>
    public static ImageElement Image(string source) => new(source);

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
