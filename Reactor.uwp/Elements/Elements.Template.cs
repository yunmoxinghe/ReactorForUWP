using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  链接与图标（对齐官方 HyperlinkButtonElement / IconData）
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 超链接按钮。Content 可以是文本，也可以是任意元素
/// （模板里就是 <c>HyperlinkButton &gt; StackPanel(FontIcon + TextBlock)</c>）。
/// </summary>
public sealed record HyperlinkButtonElement(
    string? Label = null,
    Element? Content = null,
    string? NavigateUri = null,
    Action? OnClick = null) : Element;

/// <summary>字体图标（Segoe Fluent Icons 的 glyph，如 <c>"\uE80F"</c>）。</summary>
/// <remarks>
/// 官方 Reactor 用 <c>FontIconData</c> 描述图标，再由宿主决定落到哪个属性；
/// 这里直接对应 <see cref="FontIcon"/> 控件，既能当 NavigationViewItem.Icon 用，
/// 也能当作普通内容（IconElement 继承自 FrameworkElement）。
/// </remarks>
public sealed record FontIconElement(string Glyph) : Element
{
    public string? FontFamily { get; init; }
    public double? FontSize { get; init; }
}

/// <summary>位图图标（模板里关于页用它在 SettingsExpander 头显示应用图标）。</summary>
public sealed record BitmapIconElement(string UriSource) : Element
{
    public bool ShowAsMonochrome { get; init; } = true;
    public double? Width { get; init; }
    public double? Height { get; init; }
}

// ════════════════════════════════════════════════════════════════════
//  面包屑导航
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// WinUI 2 的 BreadcrumbBar。
/// </summary>
/// <remarks>
/// 官方 Reactor（Element.cs §15）用的是 <c>BreadcrumbBarItemData[]</c> →
/// <c>ItemsSource = items.Select(i =&gt; i.Label)</c>。这里沿用同一思路：
/// <see cref="Items"/> 是字符串路径，直接当 ItemsSource 交给控件，
/// 由控件自带的 ItemTemplate 渲染（视觉与 XAML 版本一致）。
/// </remarks>
public sealed record BreadcrumbBarElement(IReadOnlyList<string> Items) : Element
{
    public Action<int>? OnItemClicked { get; init; }

    /// <summary>
    /// 条目文字的字号。null = 用 <see cref="ItemStyleKey"/> 里的值。
    /// </summary>
    /// <remarks>
    /// 模板 <c>MainPage.xaml</c> 是在 <c>ItemTemplate</c> 里用
    /// <c>TitleTextBlockStyle</c> 定的 28px，没单独写字号——所以一般不填这个，
    /// 让样式说话。填了就写进 <c>ItemTemplate</c> 里那个
    /// <c>TextBlock</c> 的 <c>FontSize</c> 属性（本地值压过样式里的同名字号）。
    /// </remarks>
    public double? ItemFontSize { get; init; }

    /// <summary>
    /// 条目文字的命名样式键（自定义样式表 → 应用资源字典）。
    /// 模板用的是 <c>TitleTextBlockStyle</c>（28px Semibold）。
    /// </summary>
    /// <remarks>
    /// 落到 <c>ItemTemplate</c> 里那个 <c>TextBlock</c> 的
    /// <c>Style="{StaticResource ...}"</c>——和模板 XAML 一模一样的位置。
    /// <para>
    /// <b>别想着"设到 BreadcrumbBar 上让它继承"</b>：<c>BreadcrumbBarItem</c>
    /// 的默认样式硬设了 <c>FontSize</c> / <c>FontFamily</c> / <c>FontWeight</c>，
    /// 继承链到条目就断了，设多大都不生效。
    /// </para>
    /// <para>
    /// <b>更别想着"把 TextBlock 当 item 喂进去"</b>：<c>UIElement</c> 放进集合时
    /// 就已经拿了父，<c>ItemsRepeater</c> 再挂一次就是第二个父 →
    /// 首次布局 0x800F1000 "Element is already the child of another element."
    /// </para>
    /// </remarks>
    public string? ItemStyleKey { get; init; }
}

// ════════════════════════════════════════════════════════════════════
//  WinUI 2 展开器
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// WinUI 2 的 Expander：可折叠的分组容器。
/// </summary>
/// <remarks>
/// WinUI 2 的 Expander 只有 Header + Content（没有 WinUI 3 的 Items / HeaderIcon），
/// 因此 <see cref="HeaderIcon"/> 与 <see cref="Header"/> 会被组合成一个横向
/// StackPanel 作为 Header 内容——视觉上等价于 WinUI 3 的 HeaderIcon。
/// </remarks>
public sealed record ExpanderElement(
    string? Header = null,
    Element? Content = null) : Element
{
    public bool IsExpanded { get; init; }
    public Element? HeaderIcon { get; init; }
}

// ════════════════════════════════════════════════════════════════════
//  CommunityToolkit 设置卡片（WinUI 设置页的事实标准容器）
// ════════════════════════════════════════════════════════════════════

/// <summary>SettingsCard 的内容对齐方式（对应 Toolkit 的 ContentAlignment）。</summary>
public enum SettingsCardContentAlignment
{
    /// <summary>内容靠右（默认，适合 ComboBox / ToggleSwitch 这类窄控件）。</summary>
    Right = 0,

    /// <summary>内容靠左撑开（适合 RadioButtons 这类需要横向空间的控件）。</summary>
    Left = 1,

    /// <summary>内容竖排到卡片下方。</summary>
    Vertical = 2,
}

/// <summary>CommunityToolkit 的 SettingsCard：一行设置项（头 + 描述 + 图标 + 内容）。</summary>
public sealed record SettingsCardElement(
    string? Header = null,
    Element? Content = null) : Element
{
    public string? Description { get; init; }
    public Element? HeaderIcon { get; init; }
    public SettingsCardContentAlignment ContentAlignment { get; init; }
    public bool IsEnabled { get; init; } = true;
    public Action? OnClick { get; init; }
}

/// <summary>
/// CommunityToolkit 的 SettingsExpander：可展开的设置卡片，
/// <see cref="Items"/> 里的每一项成为展开区里的一张 SettingsCard。
/// </summary>
public sealed record SettingsExpanderElement(
    string? Header = null,
    Element? Content = null,
    IReadOnlyList<Element?>? Items = null) : Element
{
    public string? Description { get; init; }
    public Element? HeaderIcon { get; init; }
    public bool IsExpanded { get; init; }
    public SettingsCardContentAlignment ContentAlignment { get; init; }
}

// ════════════════════════════════════════════════════════════════════
//  页面容器
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 虚拟化列表：只为可视区域（含上下缓冲）创建真实控件，滚出视野即卸载。
/// </summary>
/// <remarks>
/// <b>为什么不用官方那条路</b>：官方 Reactor 用
/// <c>ItemsRepeater</c> + 自定义 <c>IElementFactory</c>。但 WinUI <b>2</b> 的 C# 投影把
/// <c>IElementFactory</c> 标记为 internal（编译期 CS0122），UWP 侧无法自行实现，
/// 而 ItemsRepeater 的自定义工厂又必须走这个接口。所以在 UWP 上改为自建虚拟化：
/// ScrollViewer + Canvas 占位 + 按滚动位置挂载/卸载视窗内的项。
/// <para>
/// <b>约束</b>：项等高（<see cref="ItemHeight"/>）。这是 Canvas 定位方案能做对的前提，
/// 也是设置页/日志/列表这类长列表的常见形态；不等高的场景仍用
/// <see cref="ListViewElement"/>（非虚拟化）。
/// </para>
/// </remarks>
public sealed record VirtualizingListElement(
    IReadOnlyList<object?> Items,
    Func<object?, int, Element> ItemTemplate,
    double ItemHeight) : Element
{
    /// <summary>容器高度。null = 撑满父容器（不设 Height）。</summary>
    public double? Height { get; init; }

    /// <summary>视窗外额外渲染的项数（上下各 <c>Buffer</c> 项），用于减少快速滚动时的白块。</summary>
    public int Buffer { get; init; } = 4;

    /// <summary>
    /// 项的稳定身份（React identity）。null = 退化为用下标当身份。
    /// </summary>
    /// <remarks>
    /// 下标天生不稳定：在头部插入一项，后面所有项的下标全部 +1，而它们的身份并没变。
    /// 给了选择器之后，插入/删除只挪位置，已挂载的控件继续复用、内容走 Patch。
    /// 选择器返回 null 的项按退化处理；同一批里出现重复 key 时，这几项退化为下标身份。
    /// </remarks>
    public Func<object?, object?>? ItemKey { get; init; }
}

/// <summary>
/// 页面切换过渡。对应 XAML 里"页面入场"那几种观感。
/// </summary>
/// <remarks>
/// 这三个值分别对应 <c>Frame.Navigate</c> 的三种
/// <c>NavigationTransitionInfo</c>——正是模板"切页有动画"的观感来源
/// （MSDN：<c>Frame</c> 自动使用 <c>NavigationThemeTransition</c>，默认即页面刷新）。
/// <para>
/// <b>关键前提</b>：官方导航过渡只由 <c>Frame.Navigate</c> 驱动，直接给
/// <c>Frame.Content</c> 赋值不会播放。<see cref="FrameElement"/> 因此走真正的
/// <c>Navigate</c>，页面元素再注入到这次导航创建的 <c>Page</c> 里。
/// </para>
/// </remarks>
public enum PageTransition
{
    /// <summary>无过渡（内容瞬间替换）。</summary>
    None = 0,

    /// <summary>淡入 + 轻微上移（默认，页面入场的通用观感）。</summary>
    Entrance = 1,

    /// <summary>淡入 + 从下方较大距离上移（"钻入"层级感）。</summary>
    DrillIn = 2,

    /// <summary>淡入 + 从右侧滑入（适合"前进"）。</summary>
    SlideFromRight = 3,

    /// <summary>
    /// 淡入 + 从左侧滑入（"返回"的方向）。<c>Frame.GoBack()</c> 用的就是反向过渡，
    /// 返回时用它才和 XAML 版本同一个观感。
    /// </summary>
    SlideFromLeft = 4,
}

/// <summary>
/// 内容页容器，对应 XAML 的 <see cref="Frame"/>。
/// </summary>
/// <remarks>
/// 这里的定位是"内容区宿主"而非真正的导航栈：页面切换由组件状态驱动
/// （<c>UseState</c> 存当前页），协调器负责把新的页面元素树 patch 进去。
/// 视觉上与模板的 <c>ContentFrame</c> 一致（Frame 本身没有外观）。
/// <para>
/// <b>过渡动画</b>：页面切换走官方 <c>Frame.Navigate(typeof(Page), null, infoOverride)</c>，
/// 与模板 <c>ContentFrame.Navigate(typeof(HomePage))</c> 完全同一条路径——
/// 过渡由 Frame 自带的 <c>NavigationThemeTransition</c> 驱动，
/// <see cref="PageTransition"/> 只负责挑 <c>NavigationTransitionInfo</c>。
/// 建出来的页面元素树注入这次导航创建的 <c>Page.Content</c>。
/// 只有当新旧页面<b>换了新的原生控件</b>（元素类型不同）时才会重建根元素；
/// 同类型元素会被就地 patch，此时不播是对的（内容没换，本就不该播）。
/// </para>
/// </remarks>
public sealed record FrameElement(Element? Content) : Element
{
    /// <summary>页面切换过渡，默认 <see cref="PageTransition.Entrance"/>。</summary>
    public PageTransition Transition { get; init; } = PageTransition.Entrance;

    /// <summary>
    /// 当前页面栈的深度（<c>1</c> = 只有起始页）。
    /// </summary>
    /// <remarks>
    /// 传了它（<c>&gt;= 0</c>）就启用<b>官方导航栈</b>：深度比上一次小就是返回，
    /// 走官方 <c>Frame.GoBack()</c>；否则走 <c>Frame.Navigate(...)</c>——
    /// 与 XAML 模板的 <c>ContentFrame.GoBack()</c> / <c>Navigate()</c> 完全同构，
    /// 反向过渡、返回音效、BackStack 出栈全由 XAML 自己驱动。
    /// <para>
    /// <b>为什么不一律走 Navigate 来"模拟"返回</b>：<c>GoBack()</c> 与
    /// <c>Navigate()</c> 在 XAML 里是两条不同路径，返回有独立的过渡方向和
    /// <see cref="Windows.UI.Xaml.ElementSoundKind.GoBack"/> 音效（设计文档
    /// 「返回导航」一节明写）。用 Navigate 模拟的话，这些差异一个都拿不到。
    /// </para>
    /// <para>
    /// 负数（默认 <c>-1</c>）表示"不参与导航栈"：一律走 Navigate，且每次都会清掉
    /// BackStack（因为没人 GoBack，留着就是泄漏）。给不需要返回语义的页面容器用。
    /// </para>
    /// </remarks>
    public int StackDepth { get; init; } = -1;
}
