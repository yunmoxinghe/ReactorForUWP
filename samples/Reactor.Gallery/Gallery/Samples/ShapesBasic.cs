using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>Ellipse</c> / <c>Rectangle</c> / <c>Line</c>：画出来的几何。
/// </summary>
/// <remarks>
/// <para>
/// <b>形状不是控件。</b>三者都是 <c>Windows.UI.Xaml.Shapes.Shape</c> 的派生：
/// 没有模板、没有内容、不接收焦点、Tab 走不到它身上（UIA 里也不报 Button 之类）。
/// 它唯一做的事是"按这几个属性画一块几何"——要能点、能聚焦、能读屏，请用
/// <c>Button</c> 套一个形状当内容，而不是指望形状自己变成按钮。
/// </para>
/// <para>
/// <b>大小由尺寸属性决定，形状自己没有"内容尺寸"。</b>正圆就是宽高相等的椭圆
/// （官方 <c>Ellipse</c> 没有半径属性）；<c>Line</c> 反过来——它由两个端点定几何，
/// 不给 <c>Width</c> / <c>Height</c>，占多大是端点算出来的。
/// </para>
/// <para>
/// <b><c>Fill</c> 只有椭圆和矩形有。</b><c>Line</c> 是两个端点之间的一条线，
/// 没有"内部"可填，给它 <c>Fill</c> 等于给一个永远不动的旋钮，所以本库就没给
/// <c>LineElement</c> 装这个属性——要一条粗线请加 <c>StrokeThickness</c>。
/// </para>
/// <para>
/// <b>描边九件套在三个形状上是一模一样的</b>（官方把它们放在 <c>Shape</c> 基类上）：
/// 颜色、粗细、虚线段长、虚线偏移、三个端帽、拐角接法、尖角上限。
/// <c>Stretch</c> 只在有"几何怎么撑满可用尺寸"这回事的那两个上（<c>Line</c> 的
/// 几何由端点定死，撑不撑都是那一条）。
/// </para>
/// </remarks>
public sealed class ShapesBasic : Component
{
    /// <summary>
    /// 线的颜色。<b>做成静态字段而不是每轮 <c>new</c></b>：刷子按引用比，
    /// 每轮重渲染造一支新刷子就是一次真的重绘（见 <c>PropWriter.SetRef</c>）。
    /// </summary>
    private static readonly SolidColorBrush LineBrush = new(Colors.DimGray);

    public override Element Render()
    {
        var accent = ThemeResource.Brush("AccentFillColorDefaultBrush");

        return VStack(16,
            TextBlock("椭圆（正圆 = 宽高相等）").Body(),
            HStack(12,
                Cell("实心", Ellipse().Fill(accent).Size(64, 64)),
                Cell("只有描边", Ellipse().Stroke(accent).StrokeThickness(2).Size(64, 64)),
                Cell("虚线描边", Ellipse()
                    .Stroke(accent)
                    .StrokeThickness(2)
                    .StrokeDashArray(4, 3)
                    .Size(64, 64)),
                Cell("描边 + 端帽（虚线）", Ellipse()
                    .Stroke(accent)
                    .StrokeThickness(3)
                    .StrokeDashArray(1, 4)
                    .StrokeDashCap(PenLineCap.Round)
                    .Size(64, 64))),

            TextBlock("矩形（圆角是 RadiusX / RadiusY，不是 CornerRadius）").Body(),
            HStack(12,
                Cell("直角", Rectangle().Fill(accent).Size(64, 64)),
                Cell("圆角 12", Rectangle().Fill(accent).Radius(12).Size(64, 64)),
                Cell("圆角 + 描边", Rectangle()
                    .Stroke(accent)
                    .StrokeThickness(2)
                    .Radius(16)
                    .Size(64, 64)),
                Cell("两个半径不等", Rectangle()
                    .Fill(accent)
                    .Radius(24, 8)
                    .Size(64, 64))),

            TextBlock("直线（只有描边；端帽在两端各有一个属性）").Body(),
            VStack(8,
                Line(0, 8, 220, 8).Stroke(LineBrush).StrokeThickness(2),
                Line(0, 8, 220, 8)
                    .Stroke(LineBrush)
                    .StrokeThickness(8)
                    .StrokeStartLineCap(PenLineCap.Round)
                    .StrokeEndLineCap(PenLineCap.Triangle),
                Line(0, 8, 220, 8).Stroke(LineBrush).StrokeThickness(2).StrokeDashArray(6, 4),
                Line(0, 8, 220, 8)
                    .Stroke(LineBrush)
                    .StrokeThickness(2)
                    .StrokeDashArray(6, 4)
                    .StrokeDashOffset(3)),

            TextBlock("虚线的段长按<b>内容</b>比（[6,4] 与下一轮新写的 [6,4] 算同一份），"
                      + "所以每轮重渲染都会 new 一个数组的写法不会让描边每帧重建。")
                .Caption()
                .Subtle()
                .Wrap());
    }

    private static Element Cell(string title, Element shape) =>
        VStack(4,
            TextBlock(title).Caption().Subtle(),
            Border(shape.Center())
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"), 1)
                .Width(88)
                .Height(88));
}
