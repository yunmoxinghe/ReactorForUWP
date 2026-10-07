using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>Canvas</c>：子元素自己报坐标，容器不做任何排列。
/// </summary>
/// <remarks>
/// <para>
/// 坐标是<b>附加属性</b>，写在子元素身上而不是容器身上：
/// <c>.Canvas(left: …, top: …, zIndex: …)</c> 对应 XAML 的
/// <c>Canvas.Left</c> / <c>Canvas.Top</c> / <c>Canvas.ZIndex</c>。
/// 挂在别的容器下（比如 <c>VStack</c>）会被静默忽略——那一格没有落点。
/// </para>
/// <para>
/// <b>它不参与布局测量</b>：给子元素的可用尺寸是无限大，所以子元素不会被挤小、
/// 不会换行，只按坐标摆。要"跟着窗口变"的布局别用这个——
/// 用了就得自己算坐标（本例的坐标就是 state，改 state 就挪位置）。
/// </para>
/// </remarks>
public sealed class CanvasBasic : Component
{
    public override Element Render()
    {
        var (moved, setMoved) = UseState(false);

        return VStack(12,
            HStack(8,
                Button(moved ? "挪回去" : "挪到右下", () => setMoved(!moved)),
                TextBlock(moved
                        ? "蓝块在 (130, 70)，压过红块——两者重叠区域由 ZIndex 决定谁在上面"
                        : "蓝块在 (40, 24)，与红块不重叠")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            // Canvas 不会自动撑开：不写死高度它就是 0 高，什么都看不见。
            Canvas(
                    Block("红", Colors.Tomato).Canvas(left: 20, top: 20, zIndex: 1),
                    Block("绿", Colors.MediumSeaGreen).Canvas(left: 190, top: 40, zIndex: 1),
                    Block("蓝", Colors.SteelBlue)
                        .Canvas(left: moved ? 130 : 40, top: moved ? 70 : 24, zIndex: moved ? 9 : 0))
                .Height(150)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            TextBlock("三块都是 80×80。红与蓝在 moved 状态下重叠：蓝的 ZIndex 被提到 9，"
                      + "于是压在红上面；挪回去时蓝的 ZIndex 落回 0，压的就成了红。")
                .Caption()
                .Subtle()
                .Wrap());
    }

    private static Element Block(string label, Color color) =>
        Border(TextBlock(label)
                .Foreground(Colors.White)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center))
            .Background(color)
            .Width(80)
            .Height(80);
}
