using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Media;
using WuControls = Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  视图切换与滑动补完（一个 UWP 原生 + 三个 WinUI 2 真控件）
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 语义缩放（对应 UWP 原生的 <see cref="WuControls.SemanticZoom"/>）：
/// 同一批数据的"细看"与"总览"两种视图，捏合 / 点缩小键在两者之间切换。
/// </summary>
/// <remarks>
/// <para>
/// <b>两个槽位，且类型不是任意元素。</b>官方 <c>ZoomedInView</c> /
/// <c>ZoomedOutView</c> 的声明类型是 <c>ISemanticZoomInformation</c>——
/// 缩放这件事要靠"这一组里当前是哪一项"这个信息才能对上，所以槽位必须自己报得出
/// 这个信息。实务上实现了它的只有 <c>ListView</c> / <c>GridView</c>（<c>Hub</c>
/// 也算，本库没包）。给了别的（例如一个 <c>Grid</c>）就<b>留空并留痕</b>，
/// 不是抛——与 <c>RelativePanelHandler.Sibling</c> 处理"下标越界"是同一条规矩。
/// </para>
/// <para>
/// <b><c>IsZoomedInViewActive</c> 是受控的。</b>写它会触发一次<b>带动画</b>的视图
/// 切换，回执 <c>ViewChangeCompleted</c> 因此是<b>异步</b>来的：写完那一刻
/// <c>CancelIfUnconsumed</c> 就把登记撤了，那一发晚到的事件会走到
/// <c>NotExpected</c> —— 多回调一次。而它的值等于刚写进去的受控值，
/// <c>setState</c> 同值不重渲染，多出来的只是一次空转。这正是 <c>EchoGuard</c>
/// 那条"宁可多回调一次，也不能吞掉真实用户操作"的既定取舍，不是例外。
/// </para>
/// <para>
/// <c>ToggleActiveView()</c> 不暴露，理由与 <see cref="RefreshContainerElement"/>
/// 那边一致：声明式树里没有拿控件句柄的地方，需要它时走 <c>Native()</c>。
/// </para>
/// </remarks>
public sealed record SemanticZoomElement(
    Element? ZoomedInView = null,
    Element? ZoomedOutView = null) : Element
{
    /// <summary>
    /// 当前是不是停在"细看"那一侧。<b>受控</b>：给了值才受控，<c>null</c> 就是"不管它"。
    /// </summary>
    public bool? IsZoomedInViewActive { get; init; }

    /// <summary>能不能换视图（官方 <c>CanChangeViews</c>，默认开）。</summary>
    public bool CanChangeViews { get; init; } = true;

    /// <summary>要不要显示那个缩小键（官方 <c>IsZoomOutButtonEnabled</c>，默认开）。</summary>
    public bool IsZoomOutButtonEnabled { get; init; } = true;

    /// <summary>切换完成了（官方 <c>ViewChangeCompleted</c>）。参数就是新的 <c>IsZoomedInViewActive</c>。</summary>
    public Action<bool>? OnIsZoomedInViewActiveChanged { get; init; }
}

/// <summary>
/// 分页指示器（对应 WinUI 2 的 <see cref="MuxControls.PipsPager"/>）：
/// 一排小点表示"第几页 / 共几页"，外加前后翻页两个按钮。
/// </summary>
/// <remarks>
/// <para>
/// <b>它本身不装内容。</b>官方 <c>PipsPager</c> 只是"当前第几页"的指示与操作入口，
/// 页面内容由外面自己摆（常见搭档是 <see cref="FlipViewElement"/>）。所以元素上
/// 没有"子项"这类槽位——有子项的反而是外面那层。
/// </para>
/// <para>
/// <b>受控 <c>SelectedPageIndex</c>。</b>官方这个属性名字叫
/// <c>SelectedPageIndex</c>（<b>不是</b> <c>SelectedIndex</c>），回执事件反倒叫
/// <c>SelectedIndexChanged</c>——两个名字不对称，这里是照官方抄的，不是笔误。
/// 写它会同步抛事件，靠 <c>EchoGuard</c> 认回声（形同 <c>ColorPicker.Color</c>）。
/// </para>
/// <para>
/// <b>页数变化会把选中夹回范围里</b>（把 10 页改成 3 页时，停在第 8 页会被拉回
/// 边界内），夹出来的值事先不知道 —— 与 <c>NumberBox</c> 的 <c>Minimum</c> /
/// <c>Maximum</c> 同形，所以那两个写入罩进静默窗。
/// </para>
/// </remarks>
public sealed record PipsPagerElement : Element
{
    /// <summary>
    /// 一共几页（官方 <c>NumberOfPages</c>）。<c>-1</c> 是官方默认，意思是"不限"。
    /// </summary>
    public int NumberOfPages { get; init; } = -1;

    /// <summary>一次最多画几个点（官方 <c>MaxVisiblePips</c>，官方默认 5）。</summary>
    public int MaxVisiblePips { get; init; } = 5;

    /// <summary>横排还是竖排（官方 <c>Orientation</c>）。</summary>
    public WuControls.Orientation Orientation { get; init; } = WuControls.Orientation.Horizontal;

    /// <summary>上一页那个按钮什么时候出现（官方 <c>PreviousButtonVisibility</c>）。</summary>
    public MuxControls.PipsPagerButtonVisibility PreviousButtonVisibility { get; init; } =
        MuxControls.PipsPagerButtonVisibility.Visible;

    /// <summary>下一页那个按钮什么时候出现（官方 <c>NextButtonVisibility</c>）。</summary>
    public MuxControls.PipsPagerButtonVisibility NextButtonVisibility { get; init; } =
        MuxControls.PipsPagerButtonVisibility.Visible;

    /// <summary>
    /// 当前第几页（官方 <c>SelectedPageIndex</c>，从 0 起）。<b>受控</b>：
    /// 给了值才受控，<c>null</c> 就是"不管它"。
    /// </summary>
    public int? SelectedPageIndex { get; init; }

    /// <summary>翻页了（官方 <c>SelectedIndexChanged</c>）。参数就是新的 <c>SelectedPageIndex</c>。</summary>
    public Action<int>? OnSelectedPageIndexChanged { get; init; }
}

/// <summary>
/// 滑动容器（对应 WinUI 2 的 <see cref="MuxControls.SwipeControl"/>）：
/// 内容上轻轻一滑，从边上露出几条命令。
/// </summary>
/// <remarks>
/// <para>
/// <b>四个方向各一组命令，每组是官方的一个 <c>SwipeItems</c> 对象。</b>
/// 所以元素上这四个槽位收的是 <see cref="SwipeItemsData"/>（一组项 + 一个模式），
/// 而不是散装的项列表——<c>Mode</c>（滑到底直接执行 / 只是露出来等一下）长在
/// <c>SwipeItems</c> 上，不长在单个项上，摊平就丢了这个层次。
/// </para>
/// <para>
/// <b>命令组整组重建，不逐项 patch。</b>与 <see cref="CommandBarElement"/> 同一个
/// 取舍：命令项只有文字 / 图标 / 回调三种数据，没有需要跨帧保留的状态。
/// </para>
/// <para>
/// <c>Close()</c> 不暴露（命令式方法，声明式树里没有拿句柄的地方）。
/// </para>
/// </remarks>
public sealed record SwipeControlElement : Element
{
    /// <summary>被滑动的那片内容（它是 <c>ContentControl</c>，可以是任意元素树）。</summary>
    public Element? Content { get; init; }

    /// <summary>从左往右滑露出的命令。</summary>
    public SwipeItemsData? Left { get; init; }

    /// <summary>从右往左滑露出的命令。</summary>
    public SwipeItemsData? Right { get; init; }

    /// <summary>从上往下滑露出的命令。</summary>
    public SwipeItemsData? Top { get; init; }

    /// <summary>从下往上滑露出的命令。</summary>
    public SwipeItemsData? Bottom { get; init; }
}

/// <summary>
/// 滑动命令的<b>一组</b>（对应官方 <c>SwipeItems</c>）：几个项 + 一个模式。
/// </summary>
/// <param name="Items">这一组里的命令项。</param>
/// <param name="Mode">
/// 滑到头的含义：直接执行（<c>Execute</c>）还是只露出来等一下（<c>Reveal</c>，官方默认）。
/// </param>
public sealed record SwipeItemsData(
    IReadOnlyList<SwipeItemData> Items,
    MuxControls.SwipeMode Mode = MuxControls.SwipeMode.Reveal);

/// <summary>
/// 视差视图（对应 WinUI 2 的 <see cref="MuxControls.ParallaxView"/>）：
/// 参照另一处滚动的进度，把自己这片内容错开一点点。
/// </summary>
/// <remarks>
/// <para>
/// <b>它要两个东西，不是一个。</b><c>Child</c> 是"被错开的那片内容"（本元素唯一的
/// 子槽位），<c>Source</c> 是"参照谁的滚动"——官方 <c>ParallaxView.Source</c>
/// 通常是<b>兄弟节点上那个 <c>ScrollViewer</c></b>，不在自己的子树里。
/// 声明式树里没有 <c>x:Name</c> 可以引用，所以这里沿用
/// <see cref="TeachingTipElement"/> 与 <see cref="RelativePanelElement"/> 那条
/// 已经用过的规矩：<b>填同层下标</b>（<see cref="SourceIndex"/>），
/// 进了可视树（<c>Loaded</c>）之后再换成真的兄弟控件。
/// </para>
/// <para>
/// <b>Source 不是 <c>ScrollViewer</c> 时它是安静的。</b>视差靠订阅源头的滚动进度
/// 工作，官方取的是"源头或其子树里的滚动宿主"；取不到就是不动，既不报错也不
/// 抛。所以下标指错不会崩，只会"看不出效果"——这也是为什么下标越界时这里
/// 留空而不抛（与 <c>RelativePanelHandler.Sibling</c> 同一条规矩）。
/// </para>
/// <para>
/// <b>没有受控属性。</b>它上面能被用户改动的只有"滚动到哪儿"，而那是
/// <c>Source</c> 的状态，不是它的；属性全是我们写、控件读。
/// <c>RefreshAutomaticVerticalOffsets()</c> 这类命令式方法同样不暴露。
/// </para>
/// </remarks>
public sealed record ParallaxViewElement(Element? Child = null) : Element
{
    /// <summary>
    /// 参照谁的滚动：<b>同层第几个兄弟</b>（从 0 起），通常是那个
    /// <c>ScrollViewer</c>。<c>null</c> = 不设 <c>Source</c>，那时它不动。
    /// </summary>
    public int? SourceIndex { get; init; }

    /// <summary>上下错开的最大距离（官方 <c>VerticalShift</c>，单位 px）。</summary>
    public double VerticalShift { get; init; } = 50;

    /// <summary>左右错开的最大距离（官方 <c>HorizontalShift</c>，单位 px）。</summary>
    public double HorizontalShift { get; init; }

    /// <summary>
    /// <c>VerticalShift</c> 是"整段滚动全程错开这么多"还是"每滚一屏错开这么多"
    /// （官方 <c>VerticalSourceOffsetKind</c>：<c>Absolute</c> / <c>Relative</c>）。
    /// </summary>
    public MuxControls.ParallaxSourceOffsetKind VerticalSourceOffsetKind { get; init; } =
        MuxControls.ParallaxSourceOffsetKind.Absolute;

    /// <summary>横向同上（官方 <c>HorizontalSourceOffsetKind</c>）。</summary>
    public MuxControls.ParallaxSourceOffsetKind HorizontalSourceOffsetKind { get; init; } =
        MuxControls.ParallaxSourceOffsetKind.Absolute;

    /// <summary>竖向错开量相对自身高度的上限（官方 <c>MaxVerticalShiftRatio</c>，0~1）。</summary>
    public double MaxVerticalShiftRatio { get; init; } = 1.0;

    /// <summary>横向同上（官方 <c>MaxHorizontalShiftRatio</c>）。</summary>
    public double MaxHorizontalShiftRatio { get; init; } = 1.0;

    /// <summary>错开量是否夹在上限内（官方 <c>IsVerticalShiftClamped</c>，默认开）。</summary>
    public bool IsVerticalShiftClamped { get; init; } = true;

    /// <summary>横向同上（官方 <c>IsHorizontalShiftClamped</c>）。</summary>
    public bool IsHorizontalShiftClamped { get; init; } = true;
}

/// <summary>
/// 滑动命令里的一项（对应官方 <c>SwipeItem</c>）。
/// </summary>
/// <remarks>
/// 它与菜单项同形：<b>是挂在滑动容器上的子部件，不是独立站位的可视树节点</b>。
/// 官方 <c>SwipeItem</c> 继承 <c>DependencyObject</c> 而不是 <c>UIElement</c>，
/// 所以它的图标收 <c>IconSource</c>（<c>FontIconSource</c> 那一族）而不是
/// <c>IconElement</c>——与 <c>TabViewItem.IconSource</c> 同一个槽位类型，
/// 别和 <c>AppBarButton.Icon</c> 混。
/// </remarks>
public sealed record SwipeItemData
{
    /// <summary>文字说明（官方 <c>Text</c>）。</summary>
    public string? Text { get; init; }

    /// <summary>图标：放 <see cref="FontIconElement"/> / <see cref="BitmapIconElement"/>。</summary>
    public Element? Icon { get; init; }

    /// <summary>背景（官方 <c>Background</c>）。常见的"删除"就是在这里给红色。</summary>
    public Brush? Background { get; init; }

    /// <summary>前景（官方 <c>Foreground</c>）。</summary>
    public Brush? Foreground { get; init; }

    /// <summary>
    /// 滑到头之后容器怎么办（官方 <c>BehaviorOnInvoked</c>）：
    /// 自动（<c>Auto</c>，官方默认）/ 收回去（<c>Close</c>）/ 保持露出（<c>RemainOpen</c>）。
    /// </summary>
    public MuxControls.SwipeBehaviorOnInvoked BehaviorOnInvoked { get; init; } =
        MuxControls.SwipeBehaviorOnInvoked.Auto;

    /// <summary>被点了（官方 <c>Invoked</c>）。</summary>
    public Action? OnInvoked { get; init; }
}
