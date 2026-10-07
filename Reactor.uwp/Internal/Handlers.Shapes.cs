using Microsoft.UI.Reactor;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 三种形状的公共部分：<b>描边</b>（对齐官方 <c>Shape</c> 基类上那九个属性）。
/// </summary>
/// <remarks>
/// <para>
/// 为什么做成泛型基类而不是三份抄写：<c>Shape</c> 的描边属性<b>官方自己就放在基类上</b>，
/// 三个派生类一个都没改语义。抄三遍的代价不是今天的敲键盘，而是将来"改一处漏两处"。
/// 每个形状自己多出来的那几样（<c>Fill</c> / <c>RadiusX</c> / 端点坐标）仍写在各自
/// 的 handler 里——它们只属于那一个形状。
/// </para>
/// <para>
/// <b>没有受控值。</b>形状上没有任何"用户能改、还要回传"的属性（描边与填充都是
/// 单向下发），所以这里没有 <c>EchoGuard</c> 那套登记——第九道契约管的是
/// "有回执通道的控件"，形状不在它的视野里。
/// </para>
/// </remarks>
internal abstract class ShapeHandler<TElement, TShape> : ElementHandler<TElement, TShape>
    where TElement : ShapeElement
    where TShape : Shape, new()
{
    protected override TShape Mount(Reconciler reconciler, TElement element)
    {
        var shape = new TShape();
        ApplyStroke(shape, null, element);
        return shape;
    }

    protected override void Update(
        Reconciler reconciler,
        TElement oldElement,
        TElement newElement,
        TShape control) =>
        ApplyStroke(control, oldElement, newElement);

    private static void ApplyStroke(TShape shape, TElement? oldElement, TElement newElement)
    {
        // 引用类型的描边色按引用比：每轮 new 一个 Brush 就每帧重绘一次，
        // 这是 PropWriter 那条规矩的又一个落点（见该类注释）。
        PropWriter.SetRef(oldElement?.Stroke, newElement.Stroke, value => shape.Stroke = value);

        PropWriter.Set(oldElement?.StrokeThickness, newElement.StrokeThickness, value =>
        {
            if (value is { } thickness)
            {
                shape.StrokeThickness = thickness;
            }
        });

        // 虚线段长按<b>内容</b>比（double[] 的默认比较器是引用，每轮新数组会
        // 让描边每帧重建一次），所以这里不走 PropWriter.Set。
        if (PropWriter.IsMounting ||
            !Seq.SequenceEqual(oldElement?.StrokeDashArray, newElement.StrokeDashArray))
        {
            if (newElement.StrokeDashArray is { Length: > 0 } dashes)
            {
                shape.StrokeDashArray = ToDoubleCollection(dashes);
            }
        }

        PropWriter.Set(oldElement?.StrokeDashOffset, newElement.StrokeDashOffset, value =>
        {
            if (value is { } offset)
            {
                shape.StrokeDashOffset = offset;
            }
        });

        PropWriter.Set(oldElement?.StrokeDashCap, newElement.StrokeDashCap, value =>
        {
            if (value is { } cap)
            {
                shape.StrokeDashCap = cap;
            }
        });

        PropWriter.Set(oldElement?.StrokeStartLineCap, newElement.StrokeStartLineCap, value =>
        {
            if (value is { } cap)
            {
                shape.StrokeStartLineCap = cap;
            }
        });

        PropWriter.Set(oldElement?.StrokeEndLineCap, newElement.StrokeEndLineCap, value =>
        {
            if (value is { } cap)
            {
                shape.StrokeEndLineCap = cap;
            }
        });

        PropWriter.Set(oldElement?.StrokeLineJoin, newElement.StrokeLineJoin, value =>
        {
            if (value is { } join)
            {
                shape.StrokeLineJoin = join;
            }
        });

        PropWriter.Set(oldElement?.StrokeMiterLimit, newElement.StrokeMiterLimit, value =>
        {
            if (value is { } limit)
            {
                shape.StrokeMiterLimit = limit;
            }
        });
    }

    /// <summary>
    /// <c>double[]</c> → 官方的 <c>DoubleCollection</c>。
    /// 元素那一侧收数组（声明式好写），控件这一侧要的是 WinRT 集合。
    /// </summary>
    private static DoubleCollection ToDoubleCollection(double[] dashes)
    {
        var collection = new DoubleCollection();

        foreach (var dash in dashes)
        {
            collection.Add(dash);
        }

        return collection;
    }
}

/// <summary>
/// 椭圆 / 正圆（对应 <see cref="Ellipse"/>）。填充与 <c>Stretch</c> 是它自己多出来的。
/// </summary>
internal sealed class EllipseHandler : ShapeHandler<EllipseElement, Ellipse>
{
    protected override Ellipse Mount(Reconciler reconciler, EllipseElement element)
    {
        var shape = base.Mount(reconciler, element);
        ApplyFill(shape, null, element);
        return shape;
    }

    protected override void Update(
        Reconciler reconciler,
        EllipseElement oldElement,
        EllipseElement newElement,
        Ellipse control)
    {
        base.Update(reconciler, oldElement, newElement, control);
        ApplyFill(control, oldElement, newElement);
    }

    private static void ApplyFill(Ellipse shape, EllipseElement? oldElement, EllipseElement newElement)
    {
        PropWriter.SetRef(oldElement?.Fill, newElement.Fill, value => shape.Fill = value);

        PropWriter.Set(oldElement?.Stretch, newElement.Stretch, value =>
        {
            if (value is { } stretch)
            {
                shape.Stretch = stretch;
            }
        });
    }
}

/// <summary>
/// 矩形（对应 <see cref="Rectangle"/>）。圆角由 <c>RadiusX</c> / <c>RadiusY</c> 决定——
/// 官方这里是<b>椭圆弧的两个半径</b>，不是 <c>Border</c> 那个四角分设的
/// <c>CornerRadius</c>。
/// </summary>
internal sealed class RectangleHandler : ShapeHandler<RectangleElement, Rectangle>
{
    protected override Rectangle Mount(Reconciler reconciler, RectangleElement element)
    {
        var shape = base.Mount(reconciler, element);
        ApplyShape(shape, null, element);
        return shape;
    }

    protected override void Update(
        Reconciler reconciler,
        RectangleElement oldElement,
        RectangleElement newElement,
        Rectangle control)
    {
        base.Update(reconciler, oldElement, newElement, control);
        ApplyShape(control, oldElement, newElement);
    }

    private static void ApplyShape(
        Rectangle shape, RectangleElement? oldElement, RectangleElement newElement)
    {
        PropWriter.SetRef(oldElement?.Fill, newElement.Fill, value => shape.Fill = value);

        PropWriter.Set(oldElement?.Stretch, newElement.Stretch, value =>
        {
            if (value is { } stretch)
            {
                shape.Stretch = stretch;
            }
        });

        PropWriter.Set(oldElement?.RadiusX, newElement.RadiusX, value =>
        {
            if (value is { } x)
            {
                shape.RadiusX = x;
            }
        });

        PropWriter.Set(oldElement?.RadiusY, newElement.RadiusY, value =>
        {
            if (value is { } y)
            {
                shape.RadiusY = y;
            }
        });
    }
}

/// <summary>
/// 直线（对应 <see cref="Line"/>）。它的几何由两个端点定，描边之外没有别的可见属性。
/// </summary>
internal sealed class LineHandler : ShapeHandler<LineElement, Line>
{
    protected override Line Mount(Reconciler reconciler, LineElement element)
    {
        var shape = base.Mount(reconciler, element);
        ApplyGeometry(shape, null, element);
        return shape;
    }

    protected override void Update(
        Reconciler reconciler,
        LineElement oldElement,
        LineElement newElement,
        Line control)
    {
        base.Update(reconciler, oldElement, newElement, control);
        ApplyGeometry(control, oldElement, newElement);
    }

    private static void ApplyGeometry(Line shape, LineElement? oldElement, LineElement newElement)
    {
        PropWriter.Set(oldElement?.X1, newElement.X1, value =>
        {
            if (value is { } x1)
            {
                shape.X1 = x1;
            }
        });

        PropWriter.Set(oldElement?.Y1, newElement.Y1, value =>
        {
            if (value is { } y1)
            {
                shape.Y1 = y1;
            }
        });

        PropWriter.Set(oldElement?.X2, newElement.X2, value =>
        {
            if (value is { } x2)
            {
                shape.X2 = x2;
            }
        });

        PropWriter.Set(oldElement?.Y2, newElement.Y2, value =>
        {
            if (value is { } y2)
            {
                shape.Y2 = y2;
            }
        });
    }
}
