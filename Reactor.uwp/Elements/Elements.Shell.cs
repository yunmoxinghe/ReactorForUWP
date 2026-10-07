using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 分栏外壳，对应 UWP 原生的 <c>Windows.UI.Xaml.Controls.SplitView</c>。
/// </summary>
/// <remarks>
/// <b>两个槽位，不是一个。</b><c>Pane</c>（侧边面板）与 <c>Content</c>（主内容区）
/// 是官方控件上的两个独立属性，不是"面板是第一个子元素"那种隐式约定。
/// 所以这里也显式留两个槽位，顺序固定为 <c>(pane, content)</c>。
/// <para>
/// <c>IsPaneOpen</c> 是<b>非受控</b>的：元素上<b>没有</b>对应的回调。
/// 官方 <c>SplitView</c> 的开合事件是 <c>PaneOpening</c> / <c>PaneClosing</c> /
/// <c>PaneOpened</c> / <c>PaneClosed</c> 四个，前两个带"能不能取消"的语义
/// （<c>SplitViewPaneClosingEventArgs.Cancel</c>），我们一个都没订阅——
/// 与 <c>NavigationView.IsPaneOpen</c> 同一条规矩：拿不到回执的属性就不装成受控。
/// <b>后果要说清</b>：用户用轻扫把面板关掉后，state 里那个 <c>true</c> 还在，
/// 于是"再点一次按钮"不会重开（值没变，写不下去）。要跟着用户走就把面板设成
/// <c>Overlay</c> 之外的模式并自己订阅开合，或者干脆用
/// <c>NavigationView</c>（它自带开合按钮与受控选中）。
/// </para>
/// </remarks>
public sealed record SplitViewElement(Element? Pane = null, Element? Content = null) : Element
{
    /// <summary>面板是否展开（XAML 的 <c>IsPaneOpen</c>）。非受控，见类型注释。</summary>
    public bool IsPaneOpen { get; init; }

    /// <summary>面板的显示形态（XAML 的 <c>DisplayMode</c>）：覆盖 / 紧凑 / 并排。</summary>
    public SplitViewDisplayMode DisplayMode { get; init; } = SplitViewDisplayMode.Overlay;

    /// <summary>展开时面板宽度。</summary>
    public double OpenPaneLength { get; init; } = 320;

    /// <summary>紧凑态下面板宽度。</summary>
    public double CompactPaneLength { get; init; } = 48;

    /// <summary>面板在左还是在右。</summary>
    public SplitViewPanePlacement PanePlacement { get; init; } = SplitViewPanePlacement.Left;

    /// <summary>
    /// 面板那一块的背景（<c>SplitView.PaneBackground</c>）。收的是
    /// <see cref="Windows.UI.Xaml.Media.Brush"/> 实例，不是元素。
    /// </summary>
    /// <remarks>
    /// 这个属性<b>以前是不装的</b>：它是 <c>Brush</c>，而元素那一侧当时没有刷子的
    /// 声明式表达，装一个"只能靠 <c>Native()</c> 才能填"的槽位等于装一个假旋钮。
    /// 现在有了 <c>Factories.AcrylicBrush(...)</c>（也可以直接
    /// <c>new SolidColorBrush(...)</c>），它才真的能从声明式写出来，所以这里补上。
    /// <para>
    /// 注意它说的是<b>面板那一块</b>的背景，不是整个控件：主内容区仍是
    /// <c>Background</c>（通用修饰器）。两者是官方的两个不同属性。
    /// </para>
    /// </remarks>
    public Windows.UI.Xaml.Media.Brush? PaneBackground { get; init; }
}

/// <summary>
/// 命令条，对应 UWP 原生的 <c>Windows.UI.Xaml.Controls.CommandBar</c>。
/// </summary>
/// <remarks>
/// 它继承 <c>ContentControl</c>，所以既有 <c>Content</c>（命令条下面那片区域），
/// 又有 <c>PrimaryCommands</c> / <c>SecondaryCommands</c> 两组命令。
/// <para>
/// 命令项（<see cref="AppBarButtonElement"/> / <see cref="AppBarSeparatorElement"/>）
/// 与 <c>MenuFlyout</c> 里的菜单项同形：<b>它们是挂在命令条上的子部件</b>，
/// 不是独立站位的可视树节点（放进 <c>PrimaryCommands</c> 之后就不能再进
/// <c>Panel.Children</c>）。所以走"就地物化"，不进协调器。
/// </para>
/// <para>
/// <c>SecondaryCommands</c> 里的项会收进那个 <c>...</c> 溢出菜单；放哪一项由
/// 官方的溢出算法决定（<c>IsDynamicOverflowEnabled</c>），不由我们决定。
/// </para>
/// </remarks>
public sealed record CommandBarElement(
    Element? Content = null,
    IReadOnlyList<Element?>? Primary = null,
    IReadOnlyList<Element?>? Secondary = null) : Element
{
    /// <summary>标签相对图标的位置（XAML 的 <c>DefaultLabelPosition</c>）。</summary>
    public CommandBarDefaultLabelPosition DefaultLabelPosition { get; init; } =
        CommandBarDefaultLabelPosition.Right;

    /// <summary>
    /// 那个 <c>...</c> 溢出按钮的可见性（XAML 的 <c>OverflowButtonVisibility</c>）。
    /// </summary>
    /// <remarks>
    /// 默认 <see cref="CommandBarOverflowButtonVisibility.Auto"/>：<b>没有会溢出的
    /// 命令时它自己藏起来</b>。设成 <see cref="CommandBarOverflowButtonVisibility.Visible"/>
    /// 只是"一直显示"，<b>不会凭空造出溢出项</b>——里面空就是空。
    /// </remarks>
    public CommandBarOverflowButtonVisibility OverflowButtonVisibility { get; init; } =
        CommandBarOverflowButtonVisibility.Auto;

    /// <summary>命令条是否常驻（XAML 的 <c>IsSticky</c>）：不常驻时点空白处会收起。</summary>
    public bool IsSticky { get; init; }
}

/// <summary>
/// 命令条上的一个按钮，对应 <c>Windows.UI.Xaml.Controls.AppBarButton</c>。
/// </summary>
/// <remarks>
/// 它是真的 <c>AppBarButton</c>（继承 <c>ButtonBase</c>），因此图标槽收的是
/// <c>IconElement</c>（<c>FontIcon</c> / <c>BitmapIcon</c>）——与菜单项那边同一个类型，
/// 与 <c>InfoBadge</c> 收 <c>IconSource</c> 不同，别混。
/// </remarks>
public sealed record AppBarButtonElement(string? Label = null) : Element
{
    /// <summary>图标（<c>FontIcon</c> / <c>BitmapIcon</c>），对应官方 <c>Icon</c>。</summary>
    public Element? Icon { get; init; }

    /// <summary>点击回调，对应官方 <c>Click</c>。</summary>
    public Action? OnClick { get; init; }

    /// <summary>禁用态（XAML 的 <c>IsEnabled</c>）。</summary>
    public bool? IsEnabled { get; init; }
}

/// <summary>命令条上的分隔线，对应 <c>Windows.UI.Xaml.Controls.AppBarSeparator</c>。</summary>
public sealed record AppBarSeparatorElement() : Element;

/// <summary>
/// 命令条上"按下去就保持"的按钮，对应 UWP 原生的
/// <c>Windows.UI.Xaml.Controls.AppBarToggleButton</c>。
/// </summary>
/// <remarks>
/// <para>
/// 它<b>继承 UWP 的 <c>ToggleButton</c></b>（不像 <c>ToggleSplitButton</c> 那样是
/// WinUI 2 另起的炉灶），所以回执通道就是 <c>Checked</c> / <c>Unchecked</c>，
/// 受控写法与 <see cref="ToggleButtonElement"/> 完全一致。
/// </para>
/// <para>
/// <b>它是命令条上的子部件，随命令组整体重建。</b>与
/// <see cref="AppBarButtonElement"/> 同一个理由（见
/// <c>AppBarCommands</c>）：一个 <c>UIElement</c> 不能既在
/// <c>PrimaryCommands</c> 里又在某个 <c>Panel</c> 下。所以这里没有
/// <c>Update</c> 那条路——每轮重渲染拿到的是<b>新建</b>的原生按钮，
/// 受控下发也就退化成"造的时候就写对 + 挂好回执"，
/// <c>EchoGuard</c> 登记在刚造出来的那个实例上。
/// </para>
/// </remarks>
public sealed record AppBarToggleButtonElement(string? Label = null) : Element
{
    /// <summary>图标（<c>FontIcon</c> / <c>BitmapIcon</c> / <c>SymbolIcon</c>），对应官方 <c>Icon</c>。</summary>
    public Element? Icon { get; init; }

    /// <summary>
    /// 选中态。<see cref="Optional{T}.Unset"/> 为非受控；传 <c>bool?</c> 则受控，
    /// 其中 <c>null</c> 是三态的"中间态"（<c>IsThreeState</c> 打开时才有意义）。
    /// </summary>
    public Optional<bool?> IsChecked { get; init; }

    /// <summary>状态变化回调。与 <c>ToggleButton</c> 一致：中间态（<c>null</c>）不回调。</summary>
    public Action<bool>? OnIsCheckedChanged { get; init; }

    /// <summary>禁用态（XAML 的 <c>IsEnabled</c>）。</summary>
    public bool? IsEnabled { get; init; }
}

/// <summary>
/// 命令条浮层，对应 WinUI 2 的 <c>Microsoft.UI.Xaml.Controls.CommandBarFlyout</c>。
/// </summary>
/// <remarks>
/// <para>
/// 它是 <c>FlyoutBase</c> 的子类（<b>不是 <c>UIElement</c></b>），所以与
/// <see cref="MenuFlyoutElement"/> 同一条规矩：<b>元素是描述，由宿主就地物化</b>，
/// 走不了协调器。挂法有两种：给 <see cref="DropDownButtonElement.Flyout"/> /
/// <see cref="SplitButtonElement.Flyout"/>（点开），或给 <c>ContextMenu</c>
/// 修饰器（右键 / 长按弹出，对应 XAML 的
/// <c>&lt;UIElement.ContextFlyout&gt;&lt;CommandBarFlyout&gt;…&lt;/CommandBarFlyout&gt;</c>）。
/// </para>
/// <para>
/// <b>两组命令，不是一个列表。</b><c>PrimaryCommands</c> 横排、直接可见；
/// <c>SecondaryCommands</c> 收进那个 <c>...</c> 溢出菜单——与
/// <see cref="CommandBarElement"/> 的两组同形，所以装的也是
/// <see cref="AppBarButtonElement"/> / <see cref="AppBarSeparatorElement"/> /
/// <see cref="AppBarToggleButtonElement"/>。
/// </para>
/// <para>
/// <b><c>AlwaysExpanded</c> 是官方属性，不是"把溢出菜单摘掉"。</b>打开之后
/// 次要命令平铺在主要命令旁边而不是收进 <c>...</c>——要"没有次要命令"
/// 就把 <see cref="Secondary"/> 留空。
/// </para>
/// </remarks>
public sealed record CommandBarFlyoutElement(
    IReadOnlyList<Element?>? Primary = null,
    IReadOnlyList<Element?>? Secondary = null) : Element
{
    /// <summary>
    /// 次要命令平铺展开、不收进溢出菜单（XAML 的 <c>AlwaysExpanded</c>）。
    /// 默认是 <c>false</c>。
    /// </summary>
    public bool AlwaysExpanded { get; init; }
}

/// <summary>
/// 文本命令条，对应 WinUI 2 的 <c>Microsoft.UI.Xaml.Controls.TextCommandBarFlyout</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>它是 <see cref="CommandBarFlyoutElement"/> 的亲兄弟，不是另一种东西。</b>
/// 官方 <c>TextCommandBarFlyout</c> 继承 <c>CommandBarFlyout</c>：
/// 两组命令、<c>AlwaysExpanded</c> 与它完全同形；多出来的是"它认得文本控件"——
/// 挂在文本控件上之后，<b>剪贴板那几条命令（剪切 / 复制 / 粘贴 / 全选 / 撤销）
/// 由它自己按目标的状态填</b>，而且会随选区变化调整可用状态。
/// 这份"自己填"是它唯一的额外能力，也是不该自己手写一份的理由
/// （手写的那份不会跟着"有没有选中文本"变灰）。
/// </para>
/// <para>
/// <b>挂法与别处不同：走 <c>SelectionFlyout</c> 槽位。</b>官方给文本控件留了
/// <c>SelectionFlyout</c> 属性（选中文本时弹出）与 <c>ContextFlyout</c>
/// （右键弹出）两个槽位，这个控件就是为前者设计的——
/// 于是元素上多了一个 <c>.SelectionFlyout(...)</c> 修饰器，与
/// <c>ContextMenu</c> 并列（见 <c>ElementExtensions.Input</c>）。
/// 给别的控件用没有意义：那些控件没有 <c>SelectionFlyout</c> 属性。
/// </para>
/// <para>
/// <see cref="Primary"/> / <see cref="Secondary"/> 里加的是<b>自定义</b>命令
/// （例如"加粗"），官方那几条剪贴板命令由控件自己补，不会挤掉你给的
/// ——顺序是「你给的在前、它补的在后」。
/// </para>
/// </remarks>
public sealed record TextCommandBarFlyoutElement(
    IReadOnlyList<Element?>? Primary = null,
    IReadOnlyList<Element?>? Secondary = null) : Element
{
    /// <summary>
    /// 次要命令平铺展开、不收进溢出菜单（XAML 的 <c>AlwaysExpanded</c>）。
    /// 默认是 <c>false</c>。
    /// </summary>
    public bool AlwaysExpanded { get; init; }
}

/// <summary>
/// 菜单栏，对应 WinUI 2 的 <c>Microsoft.UI.Xaml.Controls.MenuBar</c>。
/// </summary>
/// <remarks>
/// 与 <c>CommandBar</c> 的区别是<b>形态</b>而不是能力：菜单栏是横排的若干组
/// <c>文件 / 编辑 / 视图</c>，每组点开一个浮出菜单；命令条是竖排图标 + 溢出菜单。
/// 官方 <c>MenuBar</c> 常用于窗口顶部（配合 <c>MenuBarItem</c>）。
/// <para>
/// 每一组的菜单项<b>复用 <see cref="MenuItem"/> / <see cref="MenuSeparator"/></b>：
/// 官方 <c>MenuBarItem.Items</c> 收的正是 <c>MenuFlyoutItemBase</c>，
/// 与 <c>MenuFlyout.Items</c> 同一个类型。这样"菜单项怎么写"只有一份写法，
/// 不用为了换个容器再学一遍。
/// </para>
/// </remarks>
public sealed record MenuBarElement(IReadOnlyList<MenuBarItemData> Items) : Element;

/// <summary>菜单栏里的一组（对应 <c>MenuBarItem</c>）：一个标题 + 一组菜单项。</summary>
/// <param name="Title">组标题（官方 <c>MenuBarItem.Title</c>）。</param>
/// <param name="Items">这一组里的菜单项，放 <see cref="MenuItem"/> / <see cref="MenuSeparator"/>。</param>
public sealed record MenuBarItemData(string Title, IReadOnlyList<Element?> Items);
