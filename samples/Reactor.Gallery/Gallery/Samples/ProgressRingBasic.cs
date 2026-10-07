using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>ProgressRing</c>：一个圈，说"在忙"。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>ProgressBar</c> 同一个判据：<b><c>Value</c> 为 null 就是不确定</b>
/// （一直转的圈），给了值就是按 <c>Minimum</c> / <c>Maximum</c> 转满一段。
/// </para>
/// <para>
/// 多出来的一个是 <c>IsActive</c>：<b>关掉它圆圈就整个消失</b>，不是"停在原处"。
/// 这一点常被误当成暂停——要暂停请把 <c>Value</c> 停住别动，或者换
/// <c>ProgressBar</c> 的 <c>ShowPaused</c>（那个有"暂停"这一档视觉）。
/// </para>
/// <para>
/// 用的是 <b>WinUI 2</b> 的 <c>ProgressRing</c> 而不是 UWP 原生那个：
/// 原生版只有 <c>IsActive</c>、压根不支持确定进度，官方 WinUI 3 Gallery 里
/// 那个能显示百分比的圈就是 WinUI 版的。
/// </para>
/// </remarks>
public sealed class ProgressRingBasic : Component
{
    public override Element Render()
    {
        var (value, setValue) = UseState(35.0);
        var (active, setActive) = UseState(true);

        return VStack(12,
            HStack(8,
                Button("+10", () => setValue(value >= 100 ? 0 : value + 10)),
                Button(active ? "停掉（IsActive = false）" : "转起来", () => setActive(!active)),
                TextBlock($"{value:F0} / 100")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            HStack(24,
                Cell("不确定（Value = null）", ProgressRing(null, isActive: active)),
                Cell("确定进度", ProgressRing(value, isActive: active)),
                Cell("换个量程（0…1）", ProgressRing(0.65, maximum: 1, isActive: active))),

            TextBlock("IsActive = false 时圆圈<b>整个消失</b>，位置留空——"
                      + "它不是暂停，是「我不转了」。本例三个圈同时受它控制，"
                      + "可以顺便看清「消失」留下的是多大的空档。")
                .Caption()
                .Subtle()
                .Wrap());
    }

    private static Element Cell(string label, Element ring) =>
        VStack(6,
            TextBlock(label).Caption().Subtle(),
            ring.Width(64).Height(64));
}
