using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 顺序堆叠：<c>StackPanel</c>（<c>VStack</c> / <c>HStack</c>）。
/// </summary>
/// <remarks>
/// 官方那页的三件事在这里各有一段：<b>方向</b>、<b>间距</b>、<b>子元素对齐</b>。
/// <list type="bullet">
///   <item>方向：<c>VStack</c> 竖排、<c>HStack</c> 横排。两者是同一个
///         <c>StackPanel</c> 的两种 <c>Orientation</c>，没有第三种。</item>
///   <item>间距：<c>spacing</c> 是<b>面板自己</b>的属性（官方 <c>Spacing</c>），
///         不是让每个子项背一个 Margin。给子项加 Margin 也能隔开，但那样
///         "这一组内部统一间隔多少"就散在 N 个地方了。</item>
///   <item>对齐：<c>StackPanel</c> 给子项的可用尺寸是<b>沿堆叠方向无限</b>的
///         （竖排时高度无限、横排时宽度无限）——这是它和 <c>Grid</c> 最实质的区别。
///         所以"沿堆叠方向"的对齐（竖排时的 <c>VAlign</c>）<b>没有意义</b>，
///         只有"垂直于堆叠方向"那一条（竖排时的 <c>HAlign</c>）才看得出效果。</item>
/// </list>
/// <para>
/// 本库把它做成两个工厂方法而不是 <c>StackPanel(orientation: …)</c>：
/// 竖排与横排是两个长得完全不同的写法，读代码时要先找到那一个参数才知道
/// 整体走向；<c>VStack</c> / <c>HStack</c> 让这个信息落在最前面。
/// </para>
/// </remarks>
public sealed class StackPanelBasic : Component
{
    // ── 色块用的刷子做成静态字段 ───────────────────────────────
    // 刷子按「引用」比：每轮渲染 new 一支就是一次真的重绘，写内联会每帧重画。
    private static readonly SolidColorBrush Red = new(Colors.Red);
    private static readonly SolidColorBrush Blue = new(Colors.Blue);
    private static readonly SolidColorBrush Green = new(Colors.Green);
    private static readonly SolidColorBrush Yellow = new(Colors.Yellow);

    public override Element Render()
    {
        var (spacing, setSpacing) = UseState(8.0);

        return VStack(20,
            TextBlock("竖排（VStack）：spacing 由下面那根滑杆给").Body(),

            // 官方那一段：四个 40×40 纯色块，间隔由面板自己的 Spacing 决定。
            VStack(spacing,
                Rectangle().Fill(Red).Size(40, 40),
                Rectangle().Fill(Blue).Size(40, 40),
                Rectangle().Fill(Green).Size(40, 40),
                Rectangle().Fill(Yellow).Size(40, 40)),

            HStack(8,
                Slider(
                    value: spacing,
                    min: 0,
                    max: 40,
                    onValueChanged: value => setSpacing(value))
                    .Width(240),
                TextBlock($"spacing = {spacing:0}（面板自己的属性，不是每个子项背一个 Margin）")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            TextBlock("横排（HStack），spacing = 12").Body(),

            HStack(12,
                Border(TextBlock("左").Padding(12))
                    .Background(ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"))
                    .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),
                Border(TextBlock("中").Padding(12))
                    .Background(ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"))
                    .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),
                Border(TextBlock("右").Padding(12))
                    .Background(ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"))
                    .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"))),

            TextBlock("垂直方向上的对齐（竖排时只有横向对齐看得出来）").Body(),

            // 竖排时子项在"垂直于堆叠方向"（横向）上可以左 / 中 / 右 / 拉伸；
            // 纵向的 VAlign 给了也没效果——可用高度本来就是无限的。
            VStack(8,
                Border(TextBlock("左对齐").Padding(12))
                    .HAlign(HorizontalAlignment.Left)
                    .Background(ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"))
                    .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),
                Border(TextBlock("居中").Padding(12))
                    .HAlign(HorizontalAlignment.Center)
                    .Background(ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"))
                    .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),
                Border(TextBlock("拉伸（默认）").Padding(12))
                    .HAlign(HorizontalAlignment.Stretch)
                    .Background(ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"))
                    .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"))),

            TextBlock("沿堆叠方向的可用尺寸是无限的：竖排时给子项 VAlign 看不出差别，"
                      + "这也是「一段会一直长下去的内容」不能直接塞进 ScrollViewer 之外的地方的原因。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
