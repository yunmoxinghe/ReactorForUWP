using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  布局补完（全部 UWP 原生面板 / 容器，无一是自绘）
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 子元素在 <see cref="CanvasElement"/> 里的绝对位置（对齐官方 <c>GridAttached</c>
/// 的写法：位置写在子元素身上，容器只认这一份）。
/// </summary>
/// <remarks>
/// 对应 XAML 的 <c>Canvas.Left</c> / <c>Canvas.Top</c> / <c>Canvas.ZIndex</c>
/// 三个<b>附加属性</b>。只有直接挂在 <c>Canvas(...)</c> 下的子元素会被应用。
/// </remarks>
public record CanvasAttached(double Left = 0, double Top = 0, int ZIndex = 0);

/// <summary>
/// 子元素在 <c>VariableSizedWrapGrid</c> 里占几格（对齐官方 <c>GridAttached</c>
/// 的写法）。它只认跨格，不认行列号——格子位置由换行顺序自己决定。
/// </summary>
public record WrapSpanAttached(int RowSpan = 1, int ColumnSpan = 1);

/// <summary>
/// 子元素在 <see cref="RelativePanelElement"/> 里的相对位置。
/// </summary>
/// <remarks>
/// <para>
/// 分两类：贴面板（<c>AlignXxxWithPanel</c>）与贴兄弟（<c>Below</c> /
/// <c>RightOf</c> / <c>AlignLeftWith</c> …）。前者是 <see cref="bool"/>，
/// 后者在本框架里是<b>同层子元素的下标</b>，不是 XAML 里的 <c>x:Name</c>——
/// 声明式树里的元素没有名字可给，下标是这里唯一能稳定指向"另一个子元素"的东西。
/// handler 落下去的时候把它换成真正的兄弟控件，与 XAML 标记编译器把名字解析成
/// 对象引用是同一个结果。
/// </para>
/// <para>
/// 每一轮重渲染都会按当前这一份全量重落（不设的项落回默认值），所以"去掉一条
/// 关系"是生效的，不会留下上一轮的旧值。
/// </para>
/// </remarks>
public sealed record RelativeAttached
{
    public bool AlignLeftWithPanel { get; init; }

    public bool AlignTopWithPanel { get; init; }

    public bool AlignRightWithPanel { get; init; }

    public bool AlignBottomWithPanel { get; init; }

    public bool AlignHorizontalCenterWithPanel { get; init; }

    public bool AlignVerticalCenterWithPanel { get; init; }

    /// <summary>放在第 N 个兄弟的上方。</summary>
    public int? Above { get; init; }

    /// <summary>放在第 N 个兄弟的下方。</summary>
    public int? Below { get; init; }

    /// <summary>放在第 N 个兄弟的左边。</summary>
    public int? LeftOf { get; init; }

    /// <summary>放在第 N 个兄弟的右边。</summary>
    public int? RightOf { get; init; }

    /// <summary>左边缘与第 N 个兄弟对齐。</summary>
    public int? AlignLeftWith { get; init; }

    /// <summary>上边缘与第 N 个兄弟对齐。</summary>
    public int? AlignTopWith { get; init; }

    /// <summary>右边缘与第 N 个兄弟对齐。</summary>
    public int? AlignRightWith { get; init; }

    /// <summary>下边缘与第 N 个兄弟对齐。</summary>
    public int? AlignBottomWith { get; init; }

    /// <summary>水平中心与第 N 个兄弟对齐。</summary>
    public int? AlignHorizontalCenterWith { get; init; }

    /// <summary>垂直中心与第 N 个兄弟对齐。</summary>
    public int? AlignVerticalCenterWith { get; init; }
}

/// <summary>
/// 绝对定位画布（对应 <see cref="Canvas"/>）：子元素自己报坐标，容器不做任何排列。
/// </summary>
/// <remarks>
/// 官方 <c>Canvas</c> 不参与布局测量——它给子元素的可用尺寸是<b>无限大</b>，
/// 所以子元素不会被挤小也不会换行，只按 <c>Canvas.Left</c> / <c>Canvas.Top</c>
/// 摆。想要"跟着窗口变"的布局别用这个。
/// </remarks>
public sealed record CanvasElement(IReadOnlyList<Element?> Children) : Element;

/// <summary>
/// 缩放容器（对应 <see cref="Viewbox"/>）：把<b>一个</b>子元素整体缩放到可用空间里。
/// </summary>
/// <remarks>
/// 它是本族里唯一的<b>单子元素</b>容器，走协调器的
/// <c>PatchSingleChild</c> 通用路径（已在 <c>SingleChildAccessor</c> 登记——
/// <c>Viewbox</c> 继承 <c>FrameworkElement</c>，不是 <c>ContentControl</c>，
/// 不登记就会每轮重建整棵子树）。
/// </remarks>
public sealed record ViewboxElement(Element? Child) : Element
{
    /// <summary>缩放策略（默认 <see cref="Stretch.Uniform"/>）。</summary>
    public Stretch? Stretch { get; init; }

    /// <summary>只允许放大 / 只允许缩小（默认 <see cref="StretchDirection.Both"/>）。</summary>
    public StretchDirection? StretchDirection { get; init; }
}

/// <summary>
/// 不等大小换行网格（对应 <see cref="VariableSizedWrapGrid"/>）：
/// 按 <c>ItemWidth</c> / <c>ItemHeight</c> 排格子，单个子项可以跨行跨列。
/// </summary>
public sealed record VariableSizedWrapGridElement(IReadOnlyList<Element?> Children) : Element
{
    /// <summary>排布方向（决定先填满行还是先填满列）。</summary>
    public Orientation? Orientation { get; init; }

    public double? ItemWidth { get; init; }

    public double? ItemHeight { get; init; }

    /// <summary>另一条轴上最多排几格。</summary>
    public int? MaximumRowsOrColumns { get; init; }
}

/// <summary>
/// 相对布局面板（对应 <see cref="RelativePanel"/>）：子元素之间、子元素与面板之间
/// 互相定位，而不是靠行列。
/// </summary>
public sealed record RelativePanelElement(IReadOnlyList<Element?> Children) : Element
{
    public Thickness? Padding { get; init; }

    public Brush? Background { get; init; }
}
