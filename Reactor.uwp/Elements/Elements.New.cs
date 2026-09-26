using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  Grid 布局（对齐官方 GridDefinition / GridAttached / GridElement）
// ════════════════════════════════════════════════════════════════════

/// <summary>Grid 的行列定义：强类型 <see cref="GridSize"/> 轨道。</summary>
public record GridDefinition(GridSize[] Columns, GridSize[] Rows);

/// <summary>子元素在 Grid 里的附加位置（对齐官方 GridAttached）。</summary>
public record GridAttached(int Row = 0, int Column = 0, int RowSpan = 1, int ColumnSpan = 1);

/// <summary>二维布局容器，对应 <see cref="Grid"/>（对齐官方 GridElement）。</summary>
public sealed record GridElement(
    GridDefinition Definition,
    IReadOnlyList<Element?> Children) : Element
{
    public double RowSpacing { get; init; }
    public double ColumnSpacing { get; init; }
}

/// <summary>带背景/边框的单子元素容器（对齐官方 BorderElement）。</summary>
public sealed record BorderElement(Element? Child) : Element
{
    public double? CornerRadius { get; init; }
    public Brush? Background { get; init; }
    public Brush? BorderBrush { get; init; }
    public Thickness? BorderThickness { get; init; }
    public Thickness? Padding { get; init; }
}

// ════════════════════════════════════════════════════════════════════
//  输入与选择（对齐官方同名元素）
// ════════════════════════════════════════════════════════════════════

/// <summary>下拉框。SelectedIndex 默认 Unset（非受控，-1 用 <c>Optional&lt;int&gt;.Of(-1)</c>）。</summary>
public sealed record ComboBoxElement(
    string[] Items,
    Optional<int> SelectedIndex = default,
    Action<int>? OnSelectedIndexChanged = null) : Element
{
    public string? PlaceholderText { get; init; }
    public string? Header { get; init; }
}

/// <summary>开关。IsOn 默认 Unset（非受控）。</summary>
public sealed record ToggleSwitchElement(
    Optional<bool> IsOn = default,
    Action<bool>? OnIsOnChanged = null,
    string? OnContent = null,
    string? OffContent = null) : Element
{
    public string? Header { get; init; }
}

/// <summary>单选按钮。IsChecked 默认 Unset（非受控）。</summary>
public sealed record RadioButtonElement(
    string Label,
    Optional<bool> IsChecked = default,
    Action<bool>? OnIsCheckedChanged = null,
    string? GroupName = null) : Element;

/// <summary>WinUI 2 的 RadioButtons 分组控件。</summary>
public sealed record RadioButtonsElement(
    string[] Items,
    Optional<int> SelectedIndex = default,
    Action<int>? OnSelectedIndexChanged = null) : Element
{
    public string? Header { get; init; }
}

// ════════════════════════════════════════════════════════════════════
//  进度与媒体
// ════════════════════════════════════════════════════════════════════

/// <summary>进度条（对应 ProgressBar）。<c>Value</c> 为 null 表示不确定进度。</summary>
public sealed record ProgressElement(double? Value = null) : Element
{
    public bool IsIndeterminate => Value is null;
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;
    public bool ShowError { get; init; }
    public bool ShowPaused { get; init; }
}

/// <summary>进度环。<c>Value</c> 为 null 表示不确定进度。</summary>
public sealed record ProgressRingElement(double? Value = null) : Element
{
    public bool IsIndeterminate => Value is null;
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;
    public bool IsActive { get; init; } = true;
}

/// <summary>图片。Source 为字符串（ms-appx / http / 相对路径），运行时解析为 Uri。</summary>
public sealed record ImageElement(string Source) : Element
{
    public double? Width { get; init; }
    public double? Height { get; init; }
    public string? Stretch { get; init; }
}

// ════════════════════════════════════════════════════════════════════
//  集合与导航
// ════════════════════════════════════════════════════════════════════

/// <summary>列表视图（对应 ListView）。Items 为元素数组，每一项成为一个 ListViewItem。</summary>
public sealed record ListViewElement(IReadOnlyList<Element?> Items) : Element
{
    /// <summary>选中项下标。默认 Unset；<c>Optional&lt;int&gt;.Of(-1)</c> 表示清空选择。</summary>
    public Optional<int> SelectedIndex { get; init; } = default;

    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<int>? OnItemClick { get; init; }
    public ListViewSelectionMode SelectionMode { get; init; } = ListViewSelectionMode.Single;
    public string? Header { get; init; }
}

/// <summary>网格视图（对应 GridView，横向铺开的列表）。</summary>
public sealed record GridViewElement(IReadOnlyList<Element?> Items) : Element
{
    public Optional<int> SelectedIndex { get; init; } = default;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<int>? OnItemClick { get; init; }
    public ListViewSelectionMode SelectionMode { get; init; } = ListViewSelectionMode.Single;
    public string? Header { get; init; }
}

/// <summary>导航视图的菜单项数据（对齐官方 NavigationViewItemData）。</summary>
public sealed record NavigationViewItemData(string Content, string? Icon = null, string? Tag = null);

/// <summary>菜单显示方式（对齐 WinUI 的 <c>NavigationViewPaneDisplayMode</c>）。</summary>
public enum NavPaneDisplayMode
{
    /// <summary>左侧窄条（图标），展开后覆盖内容 — 默认。</summary>
    LeftCompact = 0,

    /// <summary>左侧常驻展开菜单（WinUI 设置类 App 的标准形态）。</summary>
    Left = 1,

    /// <summary>左侧极窄（只有图标，无文字标签）。</summary>
    LeftMinimal = 2,

    /// <summary>顶部横向菜单。</summary>
    Top = 3,

    /// <summary>自动（宽窗口 Left，窄窗口 LeftCompact）。</summary>
    Auto = 4,
}

/// <summary>WinUI 2 的 NavigationView：左侧菜单 + 内容区。</summary>
public sealed record NavigationViewElement(
    Element? Content,
    IReadOnlyList<NavigationViewItemData> MenuItems) : Element
{
    public string? Header { get; init; }
    public bool IsPaneOpen { get; init; } = true;
    public bool IsBackButtonVisible { get; init; }
    public bool IsSettingsVisible { get; init; } = true;
    public int SelectedIndex { get; init; }
    public Action<int>? OnSelectedIndexChanged { get; init; }

    /// <summary>菜单显示方式。默认 <see cref="NavPaneDisplayMode.Left"/>（对齐 WinUI 模板）。</summary>
    public NavPaneDisplayMode PaneDisplayMode { get; init; } = NavPaneDisplayMode.Left;

    /// <summary>返回按钮是否可用（配合 <see cref="OnBackRequested"/> 实现返回栈）。</summary>
    public bool IsBackEnabled { get; init; }

    /// <summary>点击返回按钮。模板里对应 <c>NavView_BackRequested</c> → <c>Frame.GoBack()</c>。</summary>
    public Action? OnBackRequested { get; init; }

    /// <summary>
    /// 菜单项被点击（含 Settings 项以 -1 回调）。
    /// 与 <see cref="OnSelectedIndexChanged"/> 的区别：即使点击的是当前已选中项也会触发，
    /// 对应模板的 <c>NavView_ItemInvoked</c>。
    /// </summary>
    public Action<int>? OnItemInvoked { get; init; }

    /// <summary>是否始终显示页头（Header）。</summary>
    public bool AlwaysShowHeader { get; init; }
}
