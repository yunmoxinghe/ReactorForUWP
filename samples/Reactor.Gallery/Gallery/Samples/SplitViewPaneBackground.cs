using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>SplitView.PaneBackground</c>：给面板那一块单独配一支刷子（这里用亚克力）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么这个槽位在有了 <c>AcrylicBrush(...)</c> 之后才装。</b>它是
/// <see cref="Brush"/> 而不是元素：声明式一侧没有"刷子"这个东西的时候，
/// 这个属性只能靠 <c>Native()</c> 填——那等于装一个写不了值的旋钮，所以之前
/// 一直没暴露。现在 <c>Factories.AcrylicBrush(...)</c> 把刷子补齐了，它才成为
/// 一个真能从声明式写出来的属性。
/// </para>
/// <para>
/// <b>亚克力"没生效"最常见的误会：它取的是<b>它后面那层</b>。</b>
/// <c>Overlay</c> 形态下面板<b>盖在</b>内容上，它后面有东西，模糊就看得出；
/// 换到 <c>Inline</c>（面板与内容并排）之后，面板背后是页面背景这一层纯色，
/// 于是看到的只是 <c>fallbackColor</c>——不是亚克力坏了，是它没有素材了。
/// 这里因此把形态定死在 <c>Overlay</c>：这一页演示的是那一层模糊，不是形态。
/// </para>
/// <para>
/// 主内容区与面板是两个不同的背景：面板这一块是 <c>PaneBackground</c>，
/// 主内容区是通用修饰器 <c>.Background(...)</c>——官方两个属性，各管一块。
/// </para>
/// </remarks>
public sealed class SplitViewPaneBackground : Component
{
    /// <summary>
    /// 主内容区的彩色块。<b>刷子预先造好而不是每轮 <c>new</c></b>：刷子按引用比，
    /// 每轮重渲染造一支新刷子就是一次真的重绘（见 <c>PropWriter.SetRef</c>）。
    /// </summary>
    private static readonly Brush[] Swatches =
    {
        new SolidColorBrush(Colors.Tomato),
        new SolidColorBrush(Colors.Orange),
        new SolidColorBrush(Colors.Gold),
        new SolidColorBrush(Colors.MediumSeaGreen),
        new SolidColorBrush(Colors.SteelBlue),
        new SolidColorBrush(Colors.MediumPurple),
    };

    /// <summary>面板的两种背景，同样预先造好（换一次按钮就重渲染一次，别每轮换一支刷子）。</summary>
    private static readonly Brush AcrylicPane =
        AcrylicBrush(Colors.White, tintOpacity: 0.6, fallbackColor: Colors.LightGray);

    private static readonly Brush SolidPane = new SolidColorBrush(Colors.LightGray);

    public override Element Render()
    {
        var (open, setOpen) = UseState(true);
        var (acrylic, setAcrylic) = UseState(true);

        var pane = acrylic ? AcrylicPane : SolidPane;

        return VStack(12,
            HStack(8,
                Button(open ? "收起面板" : "展开面板", () => setOpen(!open)),
                Button(acrylic ? "换成纯色" : "换成亚克力", () => setAcrylic(!acrylic)),
                TextBlock(acrylic ? "亚克力（tintOpacity = 0.6）" : "纯色（LightGray）")
                    .Caption()
                    .Subtle()
                    .VAlign(Windows.UI.Xaml.VerticalAlignment.Center)),

            SplitView(
                    pane: VStack(8,
                        TextBlock("面板").Body(),
                        TextBlock("这一块的背景是 PaneBackground。")
                            .Caption()
                            .Subtle()
                            .Wrap(),
                        Button("面板里的按钮", () => { }))
                        .Padding(12),
                    content: HStack(Array.ConvertAll(Swatches, Swatch))
                        .Padding(16),
                    isPaneOpen: open,
                    // 定死 Overlay：面板要盖在内容上，亚克力才有"后面那层"可取。
                    displayMode: SplitViewDisplayMode.Overlay,
                    openPaneLength: 200,
                    paneBackground: pane)
                .Height(160)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            TextBlock("主内容区那几块彩色矩形就是亚克力的素材：面板盖上去之后，"
                      + "透过面板看到的模糊程度由 tintOpacity 决定（0 = 全透，1 = 全遮）。"
                      + "点「换成纯色」对照一次，差别最直观。")
                .Caption()
                .Subtle()
                .Wrap());
    }

    private static Element Swatch(Brush brush) =>
        Rectangle()
            .Fill(brush)
            .Radius(6)
            .Size(56, 96);
}
