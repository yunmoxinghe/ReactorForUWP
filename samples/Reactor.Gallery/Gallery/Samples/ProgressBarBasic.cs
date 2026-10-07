using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>ProgressBar</c>：一条横条，说"进行到哪儿了"。
/// </summary>
/// <remarks>
/// <para>
/// 两种形态由 <b><c>Value</c> 是不是 null</b> 决定，不是另有一个开关：
/// 给了值就是确定进度（按 <c>Minimum</c> / <c>Maximum</c> 填一段），
/// 给 null 就是不确定进度（一条来回扫的横条，不知道还要多久）。
/// 这是官方 <c>IsIndeterminate</c> 的语义，本框架把它收进 <c>Value</c> 的可空里——
/// 两种形态本来互斥，分成两个字段就会出现"既给了值又说不确定"这种没有答案的组合。
/// </para>
/// <para>
/// 另两个状态位：<c>ShowError</c>（变红，表示出错了）与
/// <c>ShowPaused</c>（变黄，表示暂停了）。它们是<b>叠加</b>在进度之上的状态，
/// 不是另一种进度——所以是可以和确定 / 不确定任一形态同时出现的。
/// </para>
/// </remarks>
public sealed class ProgressBarBasic : Component
{
    public override Element Render()
    {
        var (value, setValue) = UseState(30.0);
        var (error, setError) = UseState(false);
        var (paused, setPaused) = UseState(false);

        return VStack(12,
            HStack(8,
                Button("+10", () => setValue(value >= 100 ? 0 : value + 10)),
                Button("归零", () => setValue(0)),
                Button(error ? "取消出错" : "标记出错", () => setError(!error)),
                Button(paused ? "取消暂停" : "标记暂停", () => setPaused(!paused)),
                TextBlock($"{value:F0} / 100")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            TextBlock("确定进度").Caption().Subtle(),
            ProgressBar(value, showError: error, showPaused: paused),

            TextBlock("不确定进度（Value 给 null）").Caption().Subtle(),
            ProgressBar(null, showError: error, showPaused: paused),

            TextBlock("换个量程：Minimum = 0、Maximum = 1，值填 0.42").Caption().Subtle(),
            ProgressBar(0.42, maximum: 1),

            TextBlock("不确定进度没有「完成」的概念，也别把它当成「0%」——"
                      + "它表达的是「在动、但不知道还要多久」。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
