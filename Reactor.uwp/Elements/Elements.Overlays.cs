using System;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls.Primitives;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  浮层与双窗格补完（两个都是 WinUI 2 真控件）
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 教学提示（对应 WinUI 2 的 <see cref="MuxControls.TeachingTip"/>）：
/// 挂在某个控件旁边的一段说明，带一个指向它的小尾巴。
/// </summary>
/// <remarks>
/// <para>
/// <b><c>Target</c> 为什么是"同层下标"。</b>XAML 里写
/// <c>Target="{x:Bind 那个按钮}"</c>，靠的是 <c>x:Name</c> 给出的对象引用；
/// 声明式树里的元素<b>没有名字</b>，能稳定指向"另一个元素"的只有它在父容器里
/// 的位置。所以这里填<b>同层子元素的下标</b>——与 <see cref="RelativeAttached"/>
/// 处理兄弟关系是同一个形状、同一个理由，落点时再换成真正的兄弟控件。
/// </para>
/// <para>
/// <b><c>IsOpen</c> 是受控的。</b>轻 dismiss（点空白处 / Esc）与关闭按钮都是用户
/// 在改 <c>IsOpen</c>，那一发由官方的 <c>Closed</c> 回执，写回时靠
/// <c>EchoGuard</c> 认下来（与 <c>DatePicker.Date</c> 同形）。<c>Closing</c>
/// 那条通道<b>不暴露</b>：它带 <c>Cancel</c> 与 <c>Deferral</c>（"先别关，我还没
/// 做完"），而声明式树下没有能拦住一次关闭的地方——需要它时走 <c>Native()</c>。
/// </para>
/// </remarks>
public sealed record TeachingTipElement : Element
{
    /// <summary>标题（官方 <c>Title</c>）。</summary>
    public string? Title { get; init; }

    /// <summary>副标题（官方 <c>Subtitle</c>）。</summary>
    public string? Subtitle { get; init; }

    /// <summary>正文（它是 <c>ContentControl</c>，内容是任意元素树）。</summary>
    public Element? Child { get; init; }

    /// <summary>
    /// 是否展开。<b>受控</b>：给了值才受控，<c>null</c> 就是"不管它"。
    /// </summary>
    public bool? IsOpen { get; init; }

    /// <summary>指向<b>同层第几个</b>子元素（见类型注释）。不填就是没有目标。</summary>
    public int? TargetIndex { get; init; }

    /// <summary>优先挂在目标的哪一侧（官方 <c>PreferredPlacement</c>）。</summary>
    public MuxControls.TeachingTipPlacementMode? PreferredPlacement { get; init; }

    /// <summary>点空白处 / Esc 是否关掉它（官方 <c>IsLightDismissEnabled</c>，默认开）。</summary>
    public bool IsLightDismissEnabled { get; init; } = true;

    /// <summary>主按钮文字（官方 <c>ActionButtonContent</c>）。给了才有那个按钮。</summary>
    public string? ActionButtonText { get; init; }

    /// <summary>关闭按钮文字（官方 <c>CloseButtonContent</c>）。给了才有那个按钮。</summary>
    public string? CloseButtonText { get; init; }

    /// <summary>
    /// 展开状态变了。参数就是新的 <c>IsOpen</c>——
    /// 用户关掉它是 <c>false</c>，除此之外没有别的来源。
    /// </summary>
    public Action<bool>? OnIsOpenChanged { get; init; }

    /// <summary>点了主按钮（官方 <c>ActionButtonClick</c>）。</summary>
    public Action? OnActionButtonClick { get; init; }

    /// <summary>点了关闭按钮（官方 <c>CloseButtonClick</c>）。</summary>
    public Action? OnCloseButtonClick { get; init; }
}

/// <summary>
/// 双窗格视图（对应 WinUI 2 的 <see cref="MuxControls.TwoPaneView"/>）：
/// 两块内容按可用宽度（或高度）决定<b>并排</b>还是<b>只留一块</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>两个槽位，不是一个。</b><c>Pane1</c> / <c>Pane2</c> 是官方控件上的两个独立
/// 属性（与 <see cref="SplitViewElement"/> 的 <c>Pane</c> / <c>Content</c> 同形）。
/// 哪一块留下由 <c>PanePriority</c> 与两个 <c>…ModeConfiguration</c> 决定，
/// <b>不是由我们决定</b>——把不想要的那一块留空等于放弃双窗格。
/// </para>
/// <para>
/// <b><c>Mode</c> 只出不进。</b>官方的 <c>TwoPaneView.Mode</c> 是<b>只读</b>属性
/// （由可用尺寸算出来），唯一的出口是 <c>ModeChanged</c>。所以元素上没有
/// "当前模式"这个可写字段——这与 <c>NavigationView.IsPaneOpen</c> 那条"拿不到
/// 回执就不装成受控"是同一条规矩的另一半：<b>压根不可写的属性更不能装</b>。
/// </para>
/// <para>
/// <c>ToggleActiveView()</c> 之类的命令式方法不暴露，理由与
/// <see cref="RefreshContainerElement"/> 那边一致：声明式树里没有拿控件句柄的地方。
/// </para>
/// </remarks>
public sealed record TwoPaneViewElement(Element? Pane1 = null, Element? Pane2 = null) : Element
{
    /// <summary>空间只够一块时留哪一块（官方 <c>PanePriority</c>）。</summary>
    public MuxControls.TwoPaneViewPriority PanePriority { get; init; } =
        MuxControls.TwoPaneViewPriority.Pane1;

    /// <summary>宽形态下两块怎么摆（官方 <c>WideModeConfiguration</c>）。</summary>
    public MuxControls.TwoPaneViewWideModeConfiguration WideModeConfiguration { get; init; } =
        MuxControls.TwoPaneViewWideModeConfiguration.LeftRight;

    /// <summary>高形态下两块怎么摆（官方 <c>TallModeConfiguration</c>）。</summary>
    public MuxControls.TwoPaneViewTallModeConfiguration TallModeConfiguration { get; init; } =
        MuxControls.TwoPaneViewTallModeConfiguration.TopBottom;

    /// <summary>宽形态下 Pane1 的长度（官方 <c>Pane1Length</c>）。<c>null</c> = 用官方默认值。</summary>
    public GridLength? Pane1Length { get; init; }

    /// <summary>宽形态下 Pane2 的长度（官方 <c>Pane2Length</c>）。<c>null</c> = 用官方默认值。</summary>
    public GridLength? Pane2Length { get; init; }

    /// <summary>至少多宽才算"宽形态"（官方 <c>MinWideModeWidth</c>）。</summary>
    public double MinWideModeWidth { get; init; } = 641;

    /// <summary>至少多高才算"高形态"（官方 <c>MinTallModeHeight</c>）。</summary>
    public double MinTallModeHeight { get; init; } = 641;

    /// <summary>形态变了（官方 <c>ModeChanged</c>）。参数就是新的 <c>TwoPaneViewMode</c>。</summary>
    public Action<MuxControls.TwoPaneViewMode>? OnModeChanged { get; init; }
}

// ════════════════════════════════════════════════════════════════════
//  提示气泡
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 提示气泡，对应 UWP 原生 <c>Windows.UI.Xaml.Controls.ToolTip</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>它不是控件树上的一个节点，是挂在别人身上的一个槽位。</b>用法是
/// <c>Button("保存").ToolTip(Tip("把改动写进磁盘"))</c>——被修饰的那个元素才是
/// 宿主，落点走官方的附加属性 <c>ToolTipService.ToolTip</c>（与
/// <see cref="ContextMenu"/> 那一路同形：都不进可视树）。
/// </para>
/// <para>
/// <b>两条内容入口不是"同一个旋钮的两个名字"，是官方 XAML 的两种写法。</b>
/// <c>ToolTipService.ToolTip="文本"</c>（特性语法，只能给字符串）与
/// <c>&lt;ToolTipService.ToolTip&gt;…子树…&lt;/ToolTipService.ToolTip&gt;</c>
/// （属性元素语法，能给任意内容）。这里照抄这个分界：
/// <c>.ToolTip(...)</c> 那两个重载一一对应，
/// <see cref="Text"/> 收字符串、<see cref="Content"/> 收子树。
/// </para>
/// <para>
/// <b><c>Placement</c> 只在这里给，不在宿主元素上再开一份。</b>官方同时有
/// <c>ToolTip.Placement</c> 与附加属性 <c>ToolTipService.Placement</c>，二者等价；
/// 这里选气泡自己那份——气泡的位置属于气泡，开两个入口会让"它该弹在哪儿"
/// 有两个答案。
/// </para>
/// <para>
/// <b>没有 <c>IsOpen</c>。</b>官方有这个属性，但气泡的开合<b>由指针与焦点驱动</b>：
/// 声明式写一个 <c>true</c> 之后它会自己收起，下一轮渲染又把 <c>true</c> 写回去，
/// 于是每帧都在"拉开—收起"。这与 <c>TeachingTip</c> 不同——那个是"程序控制为主"
/// 才做成受控。需要手动开合时走 <c>Native()</c>。
/// </para>
/// </remarks>
public sealed record ToolTipElement(Element? Content = null) : Element
{
    /// <summary>
    /// 纯文本气泡（对应 <c>ToolTipService.ToolTip="…"</c>）。
    /// 与 <see cref="Content"/> 二选一：都给了以 <see cref="Content"/> 为准。
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// 弹出方位（官方 <c>Placement</c>，默认值就是官方默认值 <c>Mouse</c>）。
    /// </summary>
    /// <remarks>
    /// <c>Mouse</c> = 跟着指针、在指针上方居中；其余四个钉在宿主的<b>那一侧</b>
    /// （<c>Top</c> / <c>Bottom</c> / <c>Left</c> / <c>Right</c>）。
    /// 与 <see cref="FlyoutElement.Placement"/> 是<b>两个不同的枚举</b>——
    /// 一个是 <c>PlacementMode</c>、一个是 <c>FlyoutPlacementMode</c>，别串。
    /// </remarks>
    public PlacementMode Placement { get; init; } = PlacementMode.Mouse;

    /// <summary>与指针（或宿主）的水平距离（官方 <c>HorizontalOffset</c>）。</summary>
    public double? HorizontalOffset { get; init; }

    /// <summary>与指针（或宿主）的垂直距离（官方 <c>VerticalOffset</c>）。</summary>
    public double? VerticalOffset { get; init; }

    /// <summary>弹出来了（官方 <c>Opened</c>）。</summary>
    public Action? OnOpened { get; init; }

    /// <summary>收起了（官方 <c>Closed</c>）。</summary>
    public Action? OnClosed { get; init; }
}
