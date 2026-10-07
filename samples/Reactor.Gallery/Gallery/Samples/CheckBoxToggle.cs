using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 二态与三态：<c>CheckBox</c> / <c>ToggleSwitch</c> 的受控写法。
/// </summary>
/// <remarks>
/// <c>CheckBox</c> 的值是 <c>bool?</c>：<c>null</c> 是官方的"中间态"（三态 checkbox，
/// 用于"部分子项勾选"）。这里用二态，所以回调签名是 <c>Action&lt;bool&gt;</c>。
/// <para>
/// <b><c>ToggleSwitch</c> 与 <c>CheckBox</c> 不是同一个东西的两套皮肤</b>：
/// 前者表示"某项设置开/关且立即生效"，后者表示"选中/参与某一项"。
/// 语义不同，屏幕阅读器念出来的也不同，别互相替代。
/// </para>
/// </remarks>
public sealed class CheckBoxToggle : Component
{
    public override Element Render()
    {
        var (agreed, setAgreed) = UseState(false);
        var (autoUpdate, setAutoUpdate) = UseState(true);

        return VStack(10,
            CheckBox(
                Optional<bool?>.Of(agreed),
                setAgreed,
                "我同意此条款"),
            TextBlock(agreed ? "已勾选" : "未勾选").Caption().Subtle(),

            ToggleSwitch(
                Optional<bool>.Of(autoUpdate),
                setAutoUpdate,
                onContent: "开",
                offContent: "关",
                header: "自动更新"),
            TextBlock(autoUpdate ? "开关会把它自己的状态回调给组件" : "已关闭")
                .Caption()
                .Subtle());
    }
}
