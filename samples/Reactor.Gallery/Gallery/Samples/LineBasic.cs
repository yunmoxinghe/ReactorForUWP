using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 直线：<c>Line</c>。
/// </summary>
/// <remarks>
/// 官方那页把 <c>Line</c> 与 <c>Shape</c> 分在两条：因为<b>它的几何由两个端点定死</b>，
/// 与椭圆 / 矩形的那套"尺寸 + 拉伸"不是同一回事。
/// <list type="bullet">
///   <item><b>不给 <c>Width</c> / <c>Height</c></b>：占多大是
///         <c>(x1, y1)</c> 到 <c>(x2, y2)</c> 算出来的。给了也没用，
///         本库因此也不在它身上提供 <c>Stretch</c>——撑不撑都是那一条。</item>
///   <item><b>没有 <c>Fill</c></b>：两个端点之间是一条线，没有"内部"可填。
///         要一条粗线请加 <c>StrokeThickness</c>，那才是官方给的旋钮。</item>
///   <item><b>端帽是两个属性</b>（<c>StrokeStartLineCap</c> / <c>StrokeEndLineCap</c>）：
///         两端可以不一样，官方 <c>Shape</c> 基类上的 <c>StrokeDashCap</c>
///         只管虚线<b>每一小段</b>的两端。</item>
///   <item>描边那一套（颜色、粗细、虚线段长、虚线偏移）与椭圆、矩形<b>完全同形</b>，
///         因为官方把它们放在 <c>Shape</c> 基类上。</item>
/// </list>
/// </remarks>
public sealed class LineBasic : Component
{
    /// <summary>
    /// 线的颜色。<b>做成静态字段而不是每轮 <c>new</c></b>：刷子按引用比，
    /// 每轮重渲染造一支新刷子就是一次真的重绘。
    /// </summary>
    private static readonly SolidColorBrush LineBrush = new(Colors.DimGray);

    /// <summary>
    /// 官方那一档的描边色：<b>SteelBlue</b>。做成静态字段：刷子按<b>引用</b>比，
    /// 每轮 <c>new</c> 一支就是一次真的重绘。
    /// </summary>
    private static readonly SolidColorBrush SteelBlue = new(Colors.SteelBlue);

    public override Element Render() =>
        VStack(16,
            TextBlock("基础档（官方）：SteelBlue 描边").Body(),
            Line(0, 8, 220, 8).Stroke(SteelBlue).StrokeThickness(4),

            TextBlock("官方这一档只给 Stroke —— Line 没有「内部」可填，本库因此也没给它装"
                      + "Fill 这个旋钮。要一条粗线就加 StrokeThickness，那才是官方给的开关。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("基本：两个端点定几何").Body(),
            Line(0, 8, 220, 8).Stroke(LineBrush).StrokeThickness(2),

            TextBlock("两端各有一个端帽（可以不一样）").Body(),
            Line(0, 8, 220, 8)
                .Stroke(LineBrush)
                .StrokeThickness(8)
                .StrokeStartLineCap(PenLineCap.Round)
                .StrokeEndLineCap(PenLineCap.Triangle),

            TextBlock("虚线：段长 [6,4]").Body(),
            Line(0, 8, 220, 8).Stroke(LineBrush).StrokeThickness(2).StrokeDashArray(6, 4),

            TextBlock("虚线偏移（DashOffset = 3：整条虚线往前挪半格）").Body(),
            Line(0, 8, 220, 8)
                .Stroke(LineBrush)
                .StrokeThickness(2)
                .StrokeDashArray(6, 4)
                .StrokeDashOffset(3),

            TextBlock("Line 没有 Fill（一条线没有「内部」），也不认 Width / Height —— "
                      + "它的几何由两个端点算出来。要一条粗线就加 StrokeThickness。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("虚线的段长按<b>内容</b>比（[6,4] 与下一轮新写的 [6,4] 算同一份），"
                      + "所以每轮重渲染都会 new 一个数组的写法不会让描边每帧重建。")
                .Caption()
                .Subtle()
                .Wrap());
}
