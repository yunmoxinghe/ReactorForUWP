using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 径向渐变：<c>RadialGradientBrush</c>（<c>Microsoft.UI.Xaml.Media
/// .RadialGradientBrush</c>）。
/// </summary>
/// <remarks>
/// 官方那页的三件事：<b>圆心到边缘的两色</b>、<b>几何旋钮（Center / RadiusX /
/// RadiusY）</b>、<b>超出范围怎么补（SpreadMethod）</b>。
/// <list type="bullet">
///   <item><b>它是 WinUI 2 的类型，不是 UWP 原生的</b>：官方把径向渐变排在 WinUI
///         那一侧（UWP 的 <c>Media</c> 命名空间里只有线性的
///         <c>LinearGradientBrush</c>）。反过来它的<b>成员全是 UWP 的</b>：
///         停靠点是 <c>Windows.UI.Xaml.Media.GradientStop</c>，
///         <c>SpreadMethod</c> / <c>MappingMode</c> 也是 UWP 那两个枚举，
///         只有刷子自己是 WinUI 的。</item>
///   <item><b>每一支刷子都在静态字段里。</b>刷子按<b>引用</b>比：每轮渲染
///         <c>new</c> 一支就是一次真的重绘，写内联就会每帧重画一遍。</item>
///   <item><b><c>GradientStops</c> 是只读的</b>（winmd 里没有 setter，赋新的集合
///         编不过），所以工厂方法往控件自己那份集合里 <c>Add</c>。</item>
///   <item><b><c>InterpolationSpace</c> 的类型是
///         <c>Windows.UI.Composition.CompositionColorSpace</c></b>——WinUI 借用了
///         合成层那个枚举，值是 <c>Rgb</c> / <c>Hsl</c>（不是"线性 vs sRGB"）。
///         <c>Hsl</c> 绕着色相环走，两个端点色相差大时中间会出现第三种颜色。</item>
///   <item><b>Offset 说的是比例</b>：0 是圆心、1 是半径末端，两端之间按线性插值。
///         要"某一格特别突出"就多给几个停靠点（<c>SpreadMethod</c> 只管超出
///         部分，不管中间怎么分）。</item>
/// </list>
/// </remarks>
public sealed class RadialGradientBrushBasic : Component
{
    // ── 静态字段：每轮 new 一支刷子就是一次真的重绘 ──────────────

    private static readonly Brush Sun = RadialGradientBrush(
        new[] { (0.0, Colors.Gold), (1.0, Colors.DarkOrange) });

    // 官方那一档写全了：Center / GradientOrigin / RadiusX / RadiusY /
    // MappingMode / SpreadMethod 六项一起给，方块也是官方的 200×200。
    private static readonly Brush OffCenter = RadialGradientBrush(
        new[] { (0.0, Colors.Yellow), (1.0, Colors.Blue) },
        center: new Windows.Foundation.Point(0.25, 0.25),
        gradientOrigin: new Windows.Foundation.Point(0.5, 0.25),
        radiusX: 0.5,
        radiusY: 0.5,
        mappingMode: BrushMappingMode.RelativeToBoundingBox,
        spreadMethod: GradientSpreadMethod.Pad);

    private static readonly Brush Squashed = RadialGradientBrush(
        new[] { (0.0, Colors.Cyan), (1.0, Colors.DarkSlateBlue) },
        radiusX: 0.9,
        radiusY: 0.25);

    private static readonly Brush Pad = RadialGradientBrush(
        new[] { (0.0, Colors.Red), (0.5, Colors.Yellow) },
        radiusX: 0.35,
        radiusY: 0.35,
        spreadMethod: GradientSpreadMethod.Pad);

    private static readonly Brush Reflect = RadialGradientBrush(
        new[] { (0.0, Colors.Red), (0.5, Colors.Yellow) },
        radiusX: 0.35,
        radiusY: 0.35,
        spreadMethod: GradientSpreadMethod.Reflect);

    private static readonly Brush Repeat = RadialGradientBrush(
        new[] { (0.0, Colors.Red), (0.5, Colors.Yellow) },
        radiusX: 0.35,
        radiusY: 0.35,
        spreadMethod: GradientSpreadMethod.Repeat);

    private static readonly Brush InPixels = RadialGradientBrush(
        new[] { (0.0, Colors.HotPink), (1.0, Colors.MediumPurple) },
        radiusX: 60,
        radiusY: 40,
        mappingMode: BrushMappingMode.Absolute);

    private static readonly Brush ByRgb = RadialGradientBrush(
        new[] { (0.0, Colors.Red), (1.0, Colors.Blue) },
        interpolationSpace: CompositionColorSpace.Rgb);

    private static readonly Brush ByHsl = RadialGradientBrush(
        new[] { (0.0, Colors.Red), (1.0, Colors.Blue) },
        interpolationSpace: CompositionColorSpace.Hsl);

    public override Element Render() =>
        VStack(20,
            TextBlock("基础：圆心 → 边缘").Body(),
            Tile(Sun),

            TextBlock("官方那一档（写全六个旋钮，容器也用官方的 200×200）").Body(),
            Tile(OffCenter, 200),

            TextBlock("Center 挪到 (0.25,0.25)、GradientOrigin 在 (0.5,0.25)、半径都是 0.5、"
                      + "SpreadMethod = Pad —— 圆心偏了，但末端之外的部分仍由端点色一路铺到底。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("RadiusX / RadiusY 不同 → 椭圆而不是正圆").Body(),
            Tile(Squashed),

            TextBlock("SpreadMethod：超出末端那一路白会不会补")
                .Body(),
            HStack(12, Tile(Pad), Tile(Reflect), Tile(Repeat)),

            TextBlock("三个名字，一件事：<b>Pad</b> 用最后一个颜色一路铺到底；"
                      + "<b>Reflect</b> 把渐变<b>倒过来</b>再铺一段（接得上）；"
                      + "<b>Repeat</b> 从头<b>再来一遍</b>（边界处会看到一条硬线）。"
                      + "差别只出现在「末端之后」，里面那一段三种都一样。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("MappingMode = Absolute → 上面那些数字按像素算").Body(),
            Tile(InPixels),

            TextBlock("默认是 <b>RelativeToBoundingBox</b>：RadiusX = 0.5 意思是"
                      + "「半个宽度」，方块换尺寸时渐变跟着变。换成 <b>Absolute</b> 之后"
                      + "那些数字才是像素——同一个半径在不同大小的方块里看起来不一样大。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("InterpolationSpace：Rgb 按数值插、Hsl 绕色相环走").Body(),
            HStack(12, Tile(ByRgb), Tile(ByHsl)),

            TextBlock("红到蓝这两端：<b>Rgb</b> 中间是灰扑扑的紫色，"
                      + "<b>Hsl</b> 会绕出青绿与黄（因为它在色相环上转了一圈）。"
                      + "它不是「哪个更准」，是「想要中间是暗的还是亮的」。")
                .Caption()
                .Subtle()
                .Wrap());

    /// <summary>
    /// 一格渐变方块。<paramref name="size"/> 给了就是正方形（官方那一档是 200×200），
    /// 不给就是默认那格的 140×110。
    /// </summary>
    private static Element Tile(Brush brush, double? size = null) =>
        Border(TextBlock(string.Empty))
            .Background(brush)
            .Size(size ?? 140, size ?? 110);
}
