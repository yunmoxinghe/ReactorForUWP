using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>Slider</c> 的"轨道长什么样"那一族：刻度 / 吸附 / 方向 / 拇指气泡。
/// </summary>
/// <remarks>
/// <para>
/// <b>画刻度与吸附到刻度是<b>两件事</b>。</b><c>TickFrequency</c> 只决定"每隔多少
/// 画一个刻度"，<c>SnapsTo</c> 才决定"拖动时吸附到什么"。官方默认
/// <c>SnapsTo = StepValues</c>（按 <c>StepFrequency</c> 跳，默认 1），
/// 所以"给了 <c>TickFrequency</c> 但拖动仍然是连续的"是<b>默认行为</b>，
/// 不是没生效——要"拖一下跳一格"必须显式给 <c>SnapsTo = Ticks</c>。
/// 本页把这两档并排摆出来就是为了看清这个区别。
/// </para>
/// <para>
/// <b>竖滑必须给高度。</b>竖向形态下官方控件按可用高度拉伸，放进
/// <c>VStack</c> 这种"按内容收缩"的容器里会被压成几条像素，症状很像
/// "滑块消失了"。这里的 <c>.Height(160)</c> 是必需的，不是装饰。
/// </para>
/// </remarks>
public sealed class SliderTicks : Component
{
    public override Element Render()
    {
        var (plain, setPlain) = UseState(30.0);
        var (snapped, setSnapped) = UseState(30.0);
        var (shared, setShared) = UseState(40.0);

        return VStack(14,
            TextBlock("刻度：画出来 vs 拖上去").Caption().Subtle(),
            TextBlock("上面那条只画刻度（拖动连续），下面那条吸附到刻度（拖一下跳 10）。")
                .Caption().Subtle().Wrap(),

            Slider(Optional<double>.Of(plain), 0, 100, setPlain,
                header: "只画刻度",
                tickFrequency: 10,
                tickPlacement: TickPlacement.Outside),
            TextBlock($"当前 {plain:F0}（可以是 37 这种非整十的数）").Caption().Subtle(),

            Slider(Optional<double>.Of(snapped), 0, 100, setSnapped,
                header: "吸附到刻度",
                tickFrequency: 10,
                tickPlacement: TickPlacement.Outside,
                snapsTo: SliderSnapsTo.Ticks),
            TextBlock($"当前 {snapped:F0}（只会是 0 / 10 / 20 …）").Caption().Subtle(),

            TextBlock("竖向与拇指气泡").Caption().Subtle(),
            HStack(24,
                Slider(Optional<double>.Of(shared), 0, 100, setShared,
                    header: "竖滑",
                    orientation: Orientation.Vertical)
                    .Height(160),

                VStack(10,
                    TextBlock($"三个滑块共用同一个 state：{shared:F0}").Caption().Subtle(),

                    Slider(Optional<double>.Of(shared), 0, 100, setShared,
                        header: "关掉拇指气泡",
                        isThumbToolTipEnabled: false),

                    Slider(Optional<double>.Of(shared), 0, 100, setShared,
                        header: "方向键步长 25",
                        stepFrequency: 25))),

            TextBlock("值增大的方向").Caption().Subtle(),
            Slider(Optional<double>.Of(shared), 0, 100, setShared,
                header: "反过来了（右边是小值）",
                isDirectionReversed: true),
            TextBlock("换的是「值往哪边增大」，Min / Max / Value 一个都没动——"
                + "三个滑块仍然共用同一个 state，拖哪一个都是同一个数。")
                .Caption().Subtle().Wrap(),

            TextBlock("拖动是连续回调，别在回调里做重活——这里只改一行文字。")
                .Caption().Subtle().Wrap());
    }
}
