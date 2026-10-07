using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 非受控的 <c>TextBox</c>：<b>不给值</b>，让控件自己持有文本。
/// </summary>
/// <remarks>
/// 受控与否的分界线就是那个 <c>Optional&lt;string&gt;</c>：
/// 不传（<c>default</c> = <see cref="Optional{T}.Unset"/>）表示"这一轮不指定值"，
/// 控件于是完全由用户自己管；传了值它就是受控的（见下一个示例）。
/// <para>
/// 这是有意区分的，不是偷懒：<b>把非受控用途写成受控是要出事的</b>——每轮渲染都把
/// 当前值写回控件，而重渲染可能由任何一次无关的状态变更触发，于是输入焦点、光标
/// 位置、输入法组合串都会被那次写回冲掉。典型表现是"打着字光标跳到行尾"。
/// </para>
/// </remarks>
public sealed class TextBoxBasic : Component
{
    public override Element Render() =>
        VStack(8,
            TextBox(placeholderText: "占位提示文本", header: "非受控输入框"),
            TextBox("有初始值，但仍然非受控", header: "初始值"),
            TextBlock("试着输入：控件自己管文本，组件不感知，也不会有一只看不见的手把值写回去。")
                .Caption()
                .Subtle());
}
