using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 形状专用的链式修饰符（<see cref="ShapeElement"/> 那一族）。
/// </summary>
/// <remarks>
/// <b>为什么逐个类型各写一份，而不是在基类上写一份返回 <c>ShapeElement</c>。</b>
/// 官方 API 的形状是 <c>EllipseElement.Fill(Brush) → EllipseElement</c>：
/// 链式之后<b>仍然拿到具体类型</b>。若在基类上写一份，<c>Ellipse().Fill(...)</c>
/// 之后静态类型就退化成 <c>ShapeElement</c>，随后 <c>.Radius(...)</c> 这类
/// 只长在矩形上的方法就调不到了——而"先填色还是先设圆角"本该无所谓。
/// 类型退化这条路的代价是把「写法顺序」变成一个隐藏约束，所以这里按类型展开。
/// <para>
/// 收的是 <see cref="Brush"/> <b>实例</b>：声明式一侧没有"颜色字符串"这一类
/// 隐式转换（官方 XAML 的 <c>Fill="Red"</c> 靠的是标记扩展），要纯色就
/// <c>new SolidColorBrush(Colors.Red)</c>、要跟主题走就
/// <c>ThemeResource.Brush("AccentFillColorDefaultBrush")</c>。
/// </para>
/// </remarks>
public static partial class ElementExtensions
{
    // ── 椭圆 ──────────────────────────────────────────────────────

    /// <summary>内部填充（<c>Shape.Fill</c>）。</summary>
    public static EllipseElement Fill(this EllipseElement el, Brush brush) =>
        el with { Fill = brush };

    /// <summary>几何怎么撑满可用尺寸（<c>Shape.Stretch</c>）。</summary>
    public static EllipseElement Stretch(this EllipseElement el, Stretch stretch) =>
        el with { Stretch = stretch };

    /// <summary>描边颜色。</summary>
    public static EllipseElement Stroke(this EllipseElement el, Brush brush) =>
        el with { Stroke = brush };

    /// <summary>描边粗细（像素）。</summary>
    public static EllipseElement StrokeThickness(this EllipseElement el, double thickness) =>
        el with { StrokeThickness = thickness };

    /// <summary>虚线段长（交替的实 / 虚长度）。</summary>
    public static EllipseElement StrokeDashArray(this EllipseElement el, params double[] dashes) =>
        el with { StrokeDashArray = dashes };

    /// <summary>虚线起点偏移。</summary>
    public static EllipseElement StrokeDashOffset(this EllipseElement el, double offset) =>
        el with { StrokeDashOffset = offset };

    /// <summary>每段虚线两端的端帽。</summary>
    public static EllipseElement StrokeDashCap(this EllipseElement el, PenLineCap cap) =>
        el with { StrokeDashCap = cap };

    /// <summary>整条线起点的端帽。</summary>
    public static EllipseElement StrokeStartLineCap(this EllipseElement el, PenLineCap cap) =>
        el with { StrokeStartLineCap = cap };

    /// <summary>整条线终点的端帽。</summary>
    public static EllipseElement StrokeEndLineCap(this EllipseElement el, PenLineCap cap) =>
        el with { StrokeEndLineCap = cap };

    /// <summary>拐角怎么接。</summary>
    public static EllipseElement StrokeLineJoin(this EllipseElement el, PenLineJoin join) =>
        el with { StrokeLineJoin = join };

    /// <summary>尖角允许伸多长（只在 <c>StrokeLineJoin = Miter</c> 时起作用）。</summary>
    public static EllipseElement StrokeMiterLimit(this EllipseElement el, double limit) =>
        el with { StrokeMiterLimit = limit };

    // ── 矩形 ──────────────────────────────────────────────────────

    /// <summary>内部填充（<c>Shape.Fill</c>）。</summary>
    public static RectangleElement Fill(this RectangleElement el, Brush brush) =>
        el with { Fill = brush };

    /// <summary>几何怎么撑满可用尺寸（<c>Shape.Stretch</c>）。</summary>
    public static RectangleElement Stretch(this RectangleElement el, Stretch stretch) =>
        el with { Stretch = stretch };

    /// <summary>
    /// 圆角半径（<c>RadiusX</c> / <c>RadiusY</c>）。只给一个就是正圆角。
    /// </summary>
    /// <remarks>
    /// 不叫 <c>CornerRadius</c>：官方 <c>Rectangle</c> 用的是<b>椭圆弧的两个半径</b>
    /// （<c>RadiusX</c> / <c>RadiusY</c>），不是 <c>Border</c> 那个四角分别设的
    /// <c>CornerRadius</c>。名字照抄官方，免得让人以为这里能单独设某一个角。
    /// </remarks>
    public static RectangleElement Radius(this RectangleElement el, double x, double? y = null) =>
        el with { RadiusX = x, RadiusY = y ?? x };

    /// <summary>描边颜色。</summary>
    public static RectangleElement Stroke(this RectangleElement el, Brush brush) =>
        el with { Stroke = brush };

    /// <summary>描边粗细（像素）。</summary>
    public static RectangleElement StrokeThickness(this RectangleElement el, double thickness) =>
        el with { StrokeThickness = thickness };

    /// <summary>虚线段长（交替的实 / 虚长度）。</summary>
    public static RectangleElement StrokeDashArray(this RectangleElement el, params double[] dashes) =>
        el with { StrokeDashArray = dashes };

    /// <summary>虚线起点偏移。</summary>
    public static RectangleElement StrokeDashOffset(this RectangleElement el, double offset) =>
        el with { StrokeDashOffset = offset };

    /// <summary>每段虚线两端的端帽。</summary>
    public static RectangleElement StrokeDashCap(this RectangleElement el, PenLineCap cap) =>
        el with { StrokeDashCap = cap };

    /// <summary>整条线起点的端帽。</summary>
    public static RectangleElement StrokeStartLineCap(this RectangleElement el, PenLineCap cap) =>
        el with { StrokeStartLineCap = cap };

    /// <summary>整条线终点的端帽。</summary>
    public static RectangleElement StrokeEndLineCap(this RectangleElement el, PenLineCap cap) =>
        el with { StrokeEndLineCap = cap };

    /// <summary>拐角怎么接。</summary>
    public static RectangleElement StrokeLineJoin(this RectangleElement el, PenLineJoin join) =>
        el with { StrokeLineJoin = join };

    /// <summary>尖角允许伸多长（只在 <c>StrokeLineJoin = Miter</c> 时起作用）。</summary>
    public static RectangleElement StrokeMiterLimit(this RectangleElement el, double limit) =>
        el with { StrokeMiterLimit = limit };

    // ── 直线 ──────────────────────────────────────────────────────

    /// <summary>描边颜色。<c>Line</c> 只有描边，不给它就是一条看不见的线。</summary>
    public static LineElement Stroke(this LineElement el, Brush brush) =>
        el with { Stroke = brush };

    /// <summary>描边粗细（像素）。</summary>
    public static LineElement StrokeThickness(this LineElement el, double thickness) =>
        el with { StrokeThickness = thickness };

    /// <summary>虚线段长（交替的实 / 虚长度）。</summary>
    public static LineElement StrokeDashArray(this LineElement el, params double[] dashes) =>
        el with { StrokeDashArray = dashes };

    /// <summary>虚线起点偏移。</summary>
    public static LineElement StrokeDashOffset(this LineElement el, double offset) =>
        el with { StrokeDashOffset = offset };

    /// <summary>每段虚线两端的端帽。</summary>
    public static LineElement StrokeDashCap(this LineElement el, PenLineCap cap) =>
        el with { StrokeDashCap = cap };

    /// <summary>整条线起点的端帽。</summary>
    public static LineElement StrokeStartLineCap(this LineElement el, PenLineCap cap) =>
        el with { StrokeStartLineCap = cap };

    /// <summary>整条线终点的端帽。</summary>
    public static LineElement StrokeEndLineCap(this LineElement el, PenLineCap cap) =>
        el with { StrokeEndLineCap = cap };
}
