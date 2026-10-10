using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>ScrollViewer</c>：给一块内容加上滚动。
/// </summary>
/// <remarks>
/// <b>它只有一个子槽位</b>，不是容器面板——要滚一整片内容，就把那片内容先装进
/// <c>VStack</c> 再塞进去。官方也是这么用的（<c>ScrollViewer</c> 的内容属性是
/// <c>Content</c>，不是 <c>Children</c>）。
/// <para>
/// 两条滚动轴各自有两个开关，别混：
/// <list type="bullet">
///   <item><c>ScrollBarVisibility</c>：<b>滚动条</b>显不显示
///         （<c>Auto</c> = 需要时才出现，<c>Disabled</c> = 永远不出现但仍能滚）。</item>
///   <item><c>ScrollMode</c>：<b>能不能滚</b>（<c>Disabled</c> = 真的滚不动）。</item>
/// </list>
/// 把 <c>ScrollBarVisibility</c> 设成 <c>Disabled</c> 只是藏起滚动条——
/// 内容照样能滚（触摸 / 滚轮都行），这一点常被误当成"禁止滚动"。
/// </para>
/// </remarks>
public sealed class ScrollViewerBasic : Component
{
    private static readonly string[] Lines =
    {
        "第一行：这块区域被限高到 266，内容比它高，于是出现了滚动。",
        "第二行：ScrollViewer 只有一个子槽位，这里先把二十行装进 VStack 再塞进来。",
        "第三行：两条轴各有 ScrollBarVisibility（滚动条显不显示）与 ScrollMode（能不能滚）。",
        "第四行：默认横向是 Disabled——不是不能滚，是滚动条不出现。",
        "第五行：把它设成 Auto 就能看见横向滚动条。",
        "第六行：纵向默认 Auto，内容超出才出现滚动条，不超出时它是隐藏的。",
        "第七行：这一行的长度故意写得比容器宽一些，用来把横向滚动条逼出来看看效果。",
        "第八行：触摸滚动、滚轮滚动、键盘 PageUp / PageDown 都由官方控件提供。",
        "第九行：自己拼一个能滚的容器要处理的内容远不止「滚动」本身。",
        "第十行：到这一行为止，纵向刚好超出限高。",
    };

    public override Element Render()
    {
        var (bars, setBars) = UseState(false);

        return VStack(12,
            HStack(8,
                Button(bars ? "隐藏横向滚动条" : "显示横向滚动条", () => setBars(!bars)),
                TextBlock($"HorizontalScrollBar = {(bars ? "Auto" : "Disabled")}")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            ScrollViewer(
                    child: VStack(6, Lines.Select(line => TextBlock(line).Wrap()).ToArray()),
                    horizontalScrollBar: bars ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                    verticalScrollBar: ScrollBarVisibility.Auto)
                .Size(400, 266)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            TextBlock("注意：把横向滚动条设成 Disabled，横向仍然能滚——"
                      + "要真的禁止滚动请改 HorizontalScroll / VerticalScroll（ScrollMode）。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("缩放（ZoomMode）").Caption().Subtle(),
            // 缩放演示要用一张真的比视口大的图——应用 logo 150×150 放在这儿
            // 太小，放大缩小都看不出名堂；本地生成的高图 240×720 才演得起来。
            ScrollViewer(
                    child: Image("ms-appx:///Assets/SampleMedia/tall-cliff.png"),
                    horizontalScrollBar: ScrollBarVisibility.Auto,
                    verticalScrollBar: ScrollBarVisibility.Auto,
                    zoom: ZoomMode.Enabled)
                .Height(180)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),
            TextBlock("缩放只作用在<b>那一个子元素</b>上，所以官方演示的是缩放图片："
                      + "内容是「按可用宽度铺开」的面板时，放大只会让它溢出、"
                      + "缩小才会真的变小——那个差别常被误当成「缩放没生效」。")
                .Wrap()
                .Caption()
                .Subtle());
    }
}
