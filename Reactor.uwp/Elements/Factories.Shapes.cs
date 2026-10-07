using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    /// <summary>
    /// 椭圆 / 正圆（对齐官方 <c>Ellipse()</c>）。
    /// </summary>
    /// <remarks>
    /// 尺寸由通用修饰器给（<c>.Width(80).Height(80)</c>）——官方 Ellipse 没有半径属性，
    /// 正圆 = 宽高相等的椭圆。填充与描边走 <c>.Fill(...)</c> / <c>.Stroke(...)</c>。
    /// </remarks>
    public static EllipseElement Ellipse() => new();

    /// <summary>
    /// 矩形（对齐官方 <c>Rectangle()</c>）。圆角走 <c>.Radius(x, y)</c>。
    /// </summary>
    public static RectangleElement Rectangle() => new();

    /// <summary>
    /// 直线（对齐官方 <c>Line(x1, y1, x2, y2)</c>：四个端点坐标都是必填——
    /// 一条线的意义就是它在哪儿，给默认值等于让它悄悄画在左上角）。
    /// </summary>
    /// <remarks>
    /// <c>Line</c> 只有描边没有填充：给 <c>.Stroke(...)</c> 与
    /// <c>.StrokeThickness(...)</c> 才看得见。
    /// </remarks>
    public static LineElement Line(double x1, double y1, double x2, double y2) =>
        new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
}
