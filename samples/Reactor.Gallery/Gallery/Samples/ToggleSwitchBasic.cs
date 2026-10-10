using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 开关：<c>ToggleSwitch</c> 的受控写法。
/// </summary>
/// <remarks>
/// 官方那页的三件事：<b>带标题</b>、<b>开 / 关两档的文案</b>、<b>作为设置项的用法</b>。
/// <list type="bullet">
///   <item><c>onContent</c> / <c>offContent</c> 是"开关上那两个字"，
///         不是标题：<c>header</c> 才是标题（在开关<b>上方</b>）。
///         两者都给时先看到标题，这是设置页的常规形态。</item>
///   <item>语义是"这项设置开 / 关，<b>且立即生效</b>"：与 <c>CheckBox</c>
///         的"选中某一项"不是一回事。要"填完表单再点确定才生效"的是
///         <c>CheckBox</c>，不是这里。</item>
///   <item>受控写法与 <c>CheckBox</c> 同形：值 + 回调，经 state 回写。
///         不传值就是非受控（控件自己持有）。</item>
///   <item><b>本库不给 <c>isEnabled</c></b>（官方那页也没有"禁用的开关"这一档演示，
///         而 <c>ToggleSwitch</c> 的禁用在声明式一侧没有落点）。</item>
/// </list>
/// </remarks>
public sealed class ToggleSwitchBasic : Component
{
    public override Element Render()
    {
        var (autoUpdate, setAutoUpdate) = UseState(true);
        var (telemetry, setTelemetry) = UseState(false);

        return VStack(12,
            TextBlock("带标题 + 开 / 关文案").Body(),

            ToggleSwitch(
                Optional<bool>.Of(autoUpdate),
                setAutoUpdate,
                onContent: "开",
                offContent: "关",
                header: "自动更新"),
            TextBlock(autoUpdate ? "开关会把自己的状态回调给组件" : "已关闭")
                .Caption()
                .Subtle(),

            TextBlock("不给文案：只显示开关本身").Body(),

            // 官方第二例的形状：一个开关 + 右边一个跟着开 / 关转停的忙指示环（宽 32）。
            // 环只要 IsActive = false 就是"整个消失"，所以这里不用 Visibility。
            HStack(12,
                ToggleSwitch(
                    Optional<bool>.Of(telemetry),
                    setTelemetry,
                    header: "发送诊断数据"),
                ProgressRing(value: null, isActive: telemetry)
                    .Width(32)
                    .VAlign(VerticalAlignment.Center)),
            TextBlock($"诊断数据：{(telemetry ? "开" : "关")}").Caption().Subtle(),

            TextBlock("onContent / offContent 是开关上那两个字，header 才是它上方的标题 —— "
                      + "两处都给时才是设置页里那个标准形态。"
                      + "它是「立即生效」的那一类，与 CheckBox 的「选中某一项」语义不同。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
