using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 连续值：<c>Slider</c>。
/// </summary>
/// <remarks>
/// 官方那页的几种形态：<b>基本</b>、<b>带标题与范围</b>、<b>刻度</b>、<b>竖直</b>、
/// <b>反向</b>。
/// <list type="bullet">
///   <item><b>拖动是连续回调</b>：<c>ValueChanged</c> 在拖动期间连续触发，
///         所以回调里必须是廉价的动作——绑到一个昂贵操作（重渲染一大棵子树、
///         发一次网络请求）会让拖动整段卡住。这里只改一行文字，直接 setState。</item>
///   <item><b>画刻度与吸附到刻度是两件事</b>：<c>tickFrequency</c> +
///         <c>tickPlacement</c> 只管"画出来"；要让滑块停在刻度上另给
///         <c>snapsTo</c>（官方默认是不吸附）。</item>
///   <item><c>isDirectionReversed</c> 只改"值往哪边增大"，
///         <b>不动</b> <c>Min</c> / <c>Max</c> / <c>Value</c> 本身。</item>
///   <item>受控与非受控的分界同样是那个 <c>Optional</c>：不传 = 控件自己持有值。</item>
/// </list>
/// </remarks>
public sealed class SliderBasic : Component
{
    public override Element Render()
    {
        var (volume, setVolume) = UseState(30.0);
        var (tick, setTick) = UseState(50.0);
        var (vertical, setVertical) = UseState(40.0);

        return VStack(20,
            TextBlock("基本（受控，0–100）").Body(),
            Slider(Optional<double>.Of(volume), 0, 100, setVolume, header: "音量")
                .Width(200),
            TextBlock($"音量：{volume:F0}%").Caption().Subtle(),

            TextBlock("刻度：画出来 ≠ 吸附上去").Body(),
            Slider(
                    Optional<double>.Of(tick),
                    0, 100, setTick,
                    tickFrequency: 25,
                    tickPlacement: TickPlacement.BottomRight,
                    snapsTo: SliderSnapsTo.Ticks)
                .Width(200),
            TextBlock($"值：{tick:F0}（snapsTo = Ticks，所以只会停在 0 / 25 / 50 / 75 / 100）")
                .Caption()
                .Subtle(),

            TextBlock("竖直").Body(),
            HStack(16,
                Slider(
                    Optional<double>.Of(vertical),
                    0, 100, setVertical,
                    orientation: Orientation.Vertical,
                    // 官方那一档是 100×100 且带两侧刻度：竖滑的可视区域由 Width/Height
                    // 一起决定，只给 Height 会得到一条又细又长的轨道。
                    tickFrequency: 10,
                    tickPlacement: TickPlacement.Outside)
                    .Size(100, 100),
                TextBlock($"值：{vertical:F0}").Caption().Subtle()),

            TextBlock("反向（IsDirectionReversed）：只改「值往哪边增大」").Body(),
            Slider(Optional<double>.Of(volume), 0, 100, setVolume, isDirectionReversed: true)
                .Width(200),
            TextBlock("Min / Max / 当前值都没变，变的只是滑块往哪边走 —— "
                      + "「左右反了」要改的是这个开关，不是把 Min 和 Max 对调。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("拖动期间 ValueChanged 是<b>连续</b>来的：回调里只做廉价动作，"
                      + "否则拖动会整段卡住。这个示例只改一行文字。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
