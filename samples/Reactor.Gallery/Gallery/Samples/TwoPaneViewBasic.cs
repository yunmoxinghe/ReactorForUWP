using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using MuxControls = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>TwoPaneView</c>：两块内容按<b>可用尺寸</b>决定并排还是只留一块。
/// </summary>
/// <remarks>
/// <para>
/// <b>形态不是我们定的。</b>官方按 <c>MinWideModeWidth</c> / <c>MinTallModeHeight</c>
/// 与两块的配置（<c>WideModeConfiguration</c> / <c>TallModeConfiguration</c>）自己算；
/// 本例把两个门槛都压到 400，于是这个示例区稍微宽一点就进 <c>Wide</c>。
/// 把两块都设成 <c>SinglePane</c> 则永远只显示 <c>PanePriority</c> 那一块——
/// 那不是"坏了"，是"明确要求只留一块"。
/// </para>
/// <para>
/// <b><c>Mode</c> 只出不进。</b>官方的 <c>Mode</c> 是<b>只读</b>属性（由尺寸算出），
/// 唯一出口是 <c>ModeChanged</c>，所以元素上没有"当前形态"这个可写字段。
/// 下面那行读数是回调写进 state 的，不是我们写进控件的。
/// </para>
/// </remarks>
public sealed class TwoPaneViewBasic : Component
{
    public override Element Render()
    {
        var (mode, setMode) = UseState(MuxControls.TwoPaneViewMode.SinglePane);
        var (priority, setPriority) = UseState(MuxControls.TwoPaneViewPriority.Pane1);
        var (wide, setWide) = UseState(MuxControls.TwoPaneViewWideModeConfiguration.LeftRight);
        var (tall, setTall) = UseState(MuxControls.TwoPaneViewTallModeConfiguration.TopBottom);
        var (single, setSingle) = UseState(false);

        return VStack(12,
            TextBlock("拖动窗口宽度（或改下面的配置）看它什么时候从「并排」变成「只留一块」。")
                .Caption().Subtle().Wrap(),

            TwoPaneView(
                    pane1: Pane("Pane 1", "左 / 上"),
                    pane2: Pane("Pane 2", "右 / 下"),
                    panePriority: priority,
                    wideModeConfiguration: single
                        ? MuxControls.TwoPaneViewWideModeConfiguration.SinglePane
                        : wide,
                    tallModeConfiguration: single
                        ? MuxControls.TwoPaneViewTallModeConfiguration.SinglePane
                        : tall,
                    pane1Length: new GridLength(1, GridUnitType.Star),
                    pane2Length: new GridLength(1, GridUnitType.Star),
                    minWideModeWidth: 400,
                    minTallModeHeight: 400,
                    onModeChanged: setMode)
                .Height(200)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            TextBlock($"当前形态：{mode}（只读属性，由官方算出；这一行是回调写进 state 的）")
                .Caption().Subtle().Wrap(),

            HStack(8,
                Button(priority == MuxControls.TwoPaneViewPriority.Pane1 ? "留 Pane 1" : "留 Pane 2",
                    () => setPriority(priority == MuxControls.TwoPaneViewPriority.Pane1
                        ? MuxControls.TwoPaneViewPriority.Pane2
                        : MuxControls.TwoPaneViewPriority.Pane1)),
                Button($"宽：{wide}",
                    () => setWide(wide == MuxControls.TwoPaneViewWideModeConfiguration.LeftRight
                        ? MuxControls.TwoPaneViewWideModeConfiguration.RightLeft
                        : MuxControls.TwoPaneViewWideModeConfiguration.LeftRight)),
                Button($"高：{tall}",
                    () => setTall(tall == MuxControls.TwoPaneViewTallModeConfiguration.TopBottom
                        ? MuxControls.TwoPaneViewTallModeConfiguration.BottomTop
                        : MuxControls.TwoPaneViewTallModeConfiguration.TopBottom)),
                Button(single ? "两块：只留一块" : "两块：都显示", () => setSingle(!single))),

            TextBlock("两个槽位都是独立的（与 SplitView 的 Pane / Content 同形），"
                      + "哪一块留下由 PanePriority 决定，不由声明顺序决定。")
                .Caption().Subtle().Wrap());
    }

    private static Element Pane(string title, string hint) =>
        VStack(4,
            TextBlock(title).Body(),
            TextBlock(hint).Caption().Subtle())
            .Padding(16)
            .Background(ThemeResource.Brush("LayerFillColorDefaultBrush"));
}
