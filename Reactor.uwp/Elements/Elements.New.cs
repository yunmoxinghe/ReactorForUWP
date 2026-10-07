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

    /// <summary>
    /// 可编辑（XAML 的 <c>IsEditable</c>）：收起时那个框变成输入框，可以打字。
    /// </summary>
    /// <remarks>
    /// 打开它<b>不会</b>让 <c>SelectedIndex</c> 变成"文本"——官方把可编辑态的
    /// 文本放在另一个属性（<c>Text</c>）上，<c>SelectedIndex</c> 仍然是下标。
    /// 也就是说：打的字与列表里哪一项匹配，是<b>控件自己</b>去做的匹配，
    /// 我们这一侧能观察的仍然是 <c>SelectedIndex</c>。
    /// 本版没有把 <c>Text</c> 暴露成受控属性，理由与 <c>RichEditBox</c> 那处一样：
    /// "写进去的值"与"回读出来的值"怎么算相等没定，不定之前不装。
    /// </remarks>
    public bool IsEditable { get; init; }

    /// <summary>
    /// 敲字时跳到匹配的项（XAML 的 <c>IsTextSearchEnabled</c>，官方默认 <c>true</c>）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="IsEditable"/> 是两件事：这一项管的是"敲键盘会不会自动
    /// 跳到列表里匹配的那一项"，<b>可编辑模式下也生效</b>。关掉它只是不再自动跳，
    /// 列表与当前下标都不动。
    /// </remarks>
    public bool IsTextSearchEnabled { get; init; } = true;
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

/// <summary>
/// 徽章（WinUI 2.8 的 <c>InfoBadge</c>）：贴在别的控件角落上的一个小圆点 / 数字 / 图标。
/// </summary>
/// <remarks>
/// <b><c>Value = -1</c> 就是官方的"圆点"那一档</b>：不显示数字，只留一个点。
/// 这不是本仓库自己约定的哨兵值，是 <c>InfoBadge</c> 自己的语义（<c>Value</c>
/// 小于 0 时数字区折叠）。
/// <para>
/// <c>Icon</c> 与 <c>Value</c> 是<b>互相替换</b>的两种形态（给了图标就不显示数字），
/// 与官方的 <c>IconSource</c> / <c>Value</c> 两个属性对应。
/// </para>
/// </remarks>
public sealed record InfoBadgeElement : Element
{
    /// <summary>数字；<c>-1</c> = 圆点（不显示数字）。</summary>
    public int Value { get; init; } = -1;

    /// <summary>图标（<c>FontIcon</c> / <c>BitmapIcon</c>）。给了它就不再显示数字。</summary>
    public Element? Icon { get; init; }

    /// <summary>
    /// 预设样式：<c>"Informational"</c> / <c>"Success"</c> / <c>"Warning"</c> /
    /// <c>"Critical"</c> / <c>"Attention"</c>。认不出来时按 <c>Informational</c>。
    /// </summary>
    public string BadgeStyle { get; init; } = "Informational";
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

    /// <summary>
    /// 汉堡键在不在（官方 <c>IsPaneToggleButtonVisible</c>）。
    /// </summary>
    /// <remarks>
    /// 它与 <see cref="PaneDisplayMode"/> 是两件事：<b>这个键不在这条 pane 也不会
    /// 自动锁死</b>——用户仍能用轻扫或从<b>顶部模式下的"更多"入口</b>把它拉出来。
    /// 想真的不让开合，得自己把开合状态接住（本元素不代做）。
    /// </remarks>
    public bool IsPaneToggleButtonVisible { get; init; } = true;

    /// <summary>
    /// 面板标题（官方 <c>PaneTitle</c>）。显示在汉堡键旁边，<b>只在 pane 展开时可见</b>——
    /// 折叠成图标条后那一列被收掉，所以别把关键导航信息只放这里。
    /// </summary>
    public string? PaneTitle { get; init; }

    /// <summary>
    /// 窗口窄于这个宽度就切到紧凑（图标条）形态（官方 <c>CompactModeThresholdWidth</c>）。
    /// </summary>
    /// <remarks>
    /// <b>null = 不写，用控件自己的默认。</b>故意不在这里抄一个具体数字：这两个阈值是
    /// 官方<b>响应式断点</b>的一部分，会随模板与版本调整；抄进来就是把一个可能过期的
    /// 数字当成契约。真要定制断点时再显式给值。
    /// </remarks>
    public double? CompactModeThresholdWidth { get; init; }

    /// <summary>
    /// 窗口宽于这个宽度就切到展开（图标 + 文字并排）形态（官方
    /// <c>ExpandedModeThresholdWidth</c>）。null 的含义同
    /// <see cref="CompactModeThresholdWidth"/>。
    /// </summary>
    public double? ExpandedModeThresholdWidth { get; init; }

    /// <summary>
    /// 挂在导航条上的搜索框（对应 XAML 的
    /// <c>&lt;NavigationView.AutoSuggestBox&gt;&lt;AutoSuggestBox/&gt;&lt;/NavigationView.AutoSuggestBox&gt;</c>）。
    /// </summary>
    /// <remarks>
    /// <b>它是官方那个槽位，不是自己拼的一个 header。</b>WinUI 的 <c>NavigationView</c>
    /// 对这个属性有专门处理：把它塞进 <c>NavigationViewPaneSteam</c> 顶端那个专属容器，
    /// 并且<b>在 pane 折叠成图标条时自动隐藏</b>——这两件事写在 XAML 里由官方管，
    /// 想用"把 AutoSuggestBox 当 <see cref="Header"/> 元素塞进来"自己复刻，
    /// 就得自己盯着 <c>DisplayMode</c> 变化去显隐，且位置/易碎性和官方的不一样。
    /// <para>
    /// 因此这里只做了最小一件事：把元素构建出的控件交给这个属性。元素不是
    /// <c>AutoSuggestBox</c>（比如塞了别的控件）时官方属性无从接收，退化为忽略并留痕，
    /// 不抛异常——搜索框不是关键路径。
    /// </para>
    /// </remarks>
    public Element? SearchBox { get; init; }
}
