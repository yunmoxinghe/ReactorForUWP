using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 勾选：<c>CheckBox</c> 的受控写法。
/// </summary>
/// <remarks>
/// 官方那页的三个形态：<b>二态</b>、<b>三态</b>、<b>带自定义内容</b>。
/// <list type="bullet">
///   <item>值是 <c>bool?</c>：<c>null</c> 即官方的中间态（用于"部分子项已勾选"）。</item>
///   <item><b>本库的回调签名是 <c>Action&lt;bool&gt;</c></b>，收不了 <c>null</c>：
///         三态那一档因此<b>只出不进</b>——可以给一个 <c>null</c> 让它显示为中间态，
///         但用户点下去之后回传的是 <c>true</c>，没有"回到中间态"的通道。
///         这不是漏了，是官方的事件（<c>Checked</c> / <c>Unchecked</c> / <c>Indeterminate</c>）
///         本来就是三个独立事件，要完整表达三态就得收三个回调；
///         二态是绝大多数场景，本库就只开到二态这一档。</item>
///   <item>与 <c>ToggleSwitch</c> 的区别是<b>语义</b>不是皮肤：前者表示
///         "选中 / 参与某一项"，后者表示"某项设置开或关且立即生效"。
///         屏幕阅读器念出来的也不同，别互相替代。</item>
/// </list>
/// </remarks>
public sealed class CheckBoxBasic : Component
{
    public override Element Render()
    {
        var (agreed, setAgreed) = UseState(false);
        var (subscribe, setSubscribe) = UseState(true);

        return VStack(12,
            TextBlock("二态（受控）").Body(),

            CheckBox(
                Optional<bool?>.Of(agreed),
                setAgreed,
                "我同意此条款"),
            TextBlock(agreed ? "已勾选" : "未勾选").Caption().Subtle(),

            TextBlock("带说明文字的那一档：本库只收字符串").Body(),

            VStack(2,
                CheckBox(
                    Optional<bool?>.Of(subscribe),
                    setSubscribe,
                    "订阅更新通知"),
                // 官方那档子项说明用的是 Margin 24（不是 Padding）： Margin 让文字与
                // 勾选框左边的对齐关系跟官方一致，Padding 会把整块内容往右推更多。
                TextBlock("每周一封，随时可以退订").Caption().Subtle().Margin(24, 0, 0, 0)),

            TextBlock("官方的 CheckBox 是 ContentControl，内容可以是任意元素树；"
                      + "本库的 <c>label</c> 只收字符串 —— 要塞一整块内容（带图标、带说明行）"
                      + "走 <c>Native()</c> 或直接用 SettingsCard 那套。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("三态：本库只给到二态那一档").Body(),

            // 值给 null = 官方的中间态；不给回调，于是它"只显示、不回传"。
            CheckBox(Optional<bool?>.Of(null), null, "部分子项已勾选（中间态）"),

            TextBlock("官方的三态是 Checked / Unchecked / Indeterminate <b>三个</b>独立事件，"
                      + "「回到中间态」要走第三个；本库的回调是 Action&lt;bool&gt;，"
                      + "只能表达前两个 —— 于是三态这一档只出不进，不假装受控。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
