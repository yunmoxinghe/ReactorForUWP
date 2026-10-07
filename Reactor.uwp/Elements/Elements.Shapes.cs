using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  形状（UWP 原生 Windows.UI.Xaml.Shapes，不是自绘也不是位图）
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 三种形状的<b>描边</b>公共部分（对齐官方 Microsoft.UI.Reactor 的
/// <c>EllipseElement</c> / <c>RectangleElement</c> / <c>LineElement</c> 那一族）。
/// </summary>
/// <remarks>
/// <para>
/// 为什么把描边收在基类里：<c>Shape</c> 的描边九个属性是<b>官方自己就放在基类上</b>的
/// （<c>Windows.UI.Xaml.Shapes.Shape</c>），三个派生类一个都没改它们的语义。
/// 抄三遍就会在"改一处漏两处"上反复付账。
/// </para>
/// <para>
/// <b>填充（<c>Fill</c>）不在这里。</b>官方 <c>Shape.Fill</c> 确实也在基类上，但
/// <c>Line</c> 只有两个端点、没有"内部"可填——给它一个 <c>Fill</c> 旋钮等于给一个
/// 写了永远不动的开关。所以 <c>Fill</c> 只长在真的有面积的那两个（椭圆 / 矩形）身上。
/// </para>
/// <para>
/// <c>Stretch</c> 同理：它说的是"几何怎么撑满可用尺寸"，<c>Line</c> 的几何由端点
/// 坐标定死，撑不撑都是那一条，所以也不在基类上。
/// </para>
/// </remarks>
public abstract record ShapeElement : Element
{
    /// <summary>描边颜色（<c>Shape.Stroke</c>）。不给就没有描边，不是"黑的"。</summary>
    public Brush? Stroke { get; init; }

    /// <summary>描边粗细（<c>Shape.StrokeThickness</c>），单位像素。</summary>
    public double? StrokeThickness { get; init; }

    /// <summary>
    /// 虚线段长（<c>Shape.StrokeDashArray</c>）。交替给"实 / 虚"的长度：
    /// <c>[4, 2]</c> = 画 4 空 2 循环。
    /// </summary>
    /// <remarks>
    /// 元素这边收 <c>double[]</c>（官方那头要的是 <c>DoubleCollection</c>，由 handler 转）。
    /// 比较按<b>内容</b>而不是引用——每轮重渲染都 <c>new</c> 一个数组的话，
    /// 按引用比会让描边每帧重建一次。
    /// </remarks>
    public double[]? StrokeDashArray { get; init; }

    /// <summary>虚线从几何起点偏移多少开始画（<c>Shape.StrokeDashOffset</c>）。</summary>
    public double? StrokeDashOffset { get; init; }

    /// <summary>每一段虚线<b>两端</b>的端帽形状（<c>Shape.StrokeDashCap</c>）。</summary>
    public PenLineCap? StrokeDashCap { get; init; }

    /// <summary>整条线<b>起点</b>的端帽（<c>Shape.StrokeStartLineCap</c>）。</summary>
    public PenLineCap? StrokeStartLineCap { get; init; }

    /// <summary>整条线<b>终点</b>的端帽（<c>Shape.StrokeEndLineCap</c>）。</summary>
    public PenLineCap? StrokeEndLineCap { get; init; }

    /// <summary>拐角怎么接（<c>Shape.StrokeLineJoin</c>）。只在有拐角的形状上看得出来。</summary>
    public PenLineJoin? StrokeLineJoin { get; init; }

    /// <summary>
    /// 尖角允许伸多长（<c>Shape.StrokeMiterLimit</c>），超过就退化成斜接。
    /// 只有 <see cref="StrokeLineJoin"/> 为 <c>Miter</c> 时才起作用。
    /// </summary>
    public double? StrokeMiterLimit { get; init; }
}

/// <summary>
/// 椭圆 / 正圆（对应 <see cref="Windows.UI.Xaml.Shapes.Ellipse"/>）。
/// </summary>
/// <remarks>
/// 正圆就是"宽 == 高"的椭圆：<see cref="Windows.UI.Xaml.Shapes.Ellipse"/> 官方没有
/// 半径属性，形状由 <c>Width</c> / <c>Height</c> 决定（走通用修饰器
/// <c>.Width(...)</c> / <c>.Height(...)</c>）。
/// </remarks>
public sealed record EllipseElement : ShapeElement
{
    /// <summary>内部填充（<c>Shape.Fill</c>）。不给就是空心。</summary>
    public Brush? Fill { get; init; }

    /// <summary>几何怎么撑满可用尺寸（<c>Shape.Stretch</c>）。默认 <see cref="Stretch.None"/>。</summary>
    public Stretch? Stretch { get; init; }
}

/// <summary>
/// 矩形（对应 <see cref="Windows.UI.Xaml.Shapes.Rectangle"/>）。
/// </summary>
public sealed record RectangleElement : ShapeElement
{
    /// <summary>内部填充（<c>Shape.Fill</c>）。不给就是空心。</summary>
    public Brush? Fill { get; init; }

    /// <summary>几何怎么撑满可用尺寸（<c>Shape.Stretch</c>）。默认 <see cref="Stretch.None"/>。</summary>
    public Stretch? Stretch { get; init; }

    /// <summary>圆角横向半径（<c>Rectangle.RadiusX</c>）。圆角矩形就是它加 <see cref="RadiusY"/>。</summary>
    public double? RadiusX { get; init; }

    /// <summary>圆角纵向半径（<c>Rectangle.RadiusY</c>）。</summary>
    public double? RadiusY { get; init; }
}

/// <summary>
/// 直线（对应 <see cref="Windows.UI.Xaml.Shapes.Line"/>）。
/// </summary>
/// <remarks>
/// <b>它只有描边、没有填充。</b>几何由两个端点定，所以"描边颜色"是它唯一可见的东西：
/// 给 <see cref="ShapeElement.Stroke"/> 与 <see cref="ShapeElement.StrokeThickness"/>，
/// 不给就是一条不存在的线（<c>Line</c> 的默认 <c>Stroke</c> 是 null）。
/// </remarks>
public sealed record LineElement : ShapeElement
{
    /// <summary>起点 X（<c>Line.X1</c>）。</summary>
    public double? X1 { get; init; }

    /// <summary>起点 Y（<c>Line.Y1</c>）。</summary>
    public double? Y1 { get; init; }

    /// <summary>终点 X（<c>Line.X2</c>）。</summary>
    public double? X2 { get; init; }

    /// <summary>终点 Y（<c>Line.Y2</c>）。</summary>
    public double? Y2 { get; init; }
}
