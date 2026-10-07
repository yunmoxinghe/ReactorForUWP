using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 受控的 <c>TextBox</c>：给值 + 给回调，输入由组件状态回写。
/// </summary>
/// <remarks>
/// 受控属性这一块是整个框架最容易写错的地方之一，错起来的样子都很眼熟：
/// 输入被吞、粘贴后被覆盖、连输两次丢一个字。框架侧由「回声抑制」统一处理
/// （受控写回触发的那次 <c>TextChanged</c> 被判为框架自己的回声并吞掉），
/// 于是写法上只需做一件事：<b>把回调里的值如实写进 state</b>。
/// <para>
/// 这里刻意把字数也显示出来：<c>derived</c> 的值不留 state，
/// 重渲染时由当前值算出即可——留了就有两份真相。
/// </para>
/// </remarks>
public sealed class TextBoxControlled : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState(string.Empty);

        return VStack(8,
            TextBox(
                Optional<string>.Of(text),
                setText,
                placeholderText: "输入内容会回写到组件状态",
                header: "受控输入框"),
            TextBlock($"当前值：{text}").Body(),
            TextBlock($"字数：{text.Length}").Caption().Subtle());
    }
}
