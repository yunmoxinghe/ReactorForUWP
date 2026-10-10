using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>PasswordBox</c>：密码框。
/// </summary>
/// <remarks>
/// <b>与 TextBox 的分工是"内容要不要被看见"，不是"能不能编辑"。</b>
/// 明文切换按钮、复制被禁，都由官方控件自己管，这里一个都不用写——所以这个
/// 样例短到几乎只有两行。
/// <para>
/// 唯一想改的是<b>遮罩字符</b>（<c>PasswordChar</c>）时才需要传参：它只显示层生效，
/// <c>Password</c> 里存的始终是明文，给多字符也只取头一个。
/// </para>
/// <para>
/// 值照旧是<b>受控</b>的：<c>Optional&lt;string&gt;.Of(pwd)</c> + 回调回写。
/// 注意回写之后 state 里就<b>真的持有一份明文</b>——真实应用里别把它
/// 落进日志或 LocalSettings，这个样例只是把长度显示出来。
/// </para>
/// </remarks>
public sealed class PasswordBoxBasic : Component
{
    public override Element Render()
    {
        var (pwd, setPwd) = UseState(string.Empty);

        return VStack(10,
            PasswordBox(
                Optional<string>.Of(pwd),
                setPwd,
                placeholderText: "随便输点什么",
                header: "密码")
                .Width(300),

            TextBlock($"已输入 {pwd.Length} 个字符（内容不上屏）").Caption().Subtle(),

            TextBlock("遮罩、明文切换按钮、禁止复制，都是官方控件自带的行为——"
                      + "这不是 TextBox 换个样式，是另一个控件。")
                .Wrap()
                .Caption()
                .Subtle(),

            TextBlock("换个遮罩字符").Caption().Subtle(),
            PasswordBox(
                Optional<string>.Of(pwd),
                setPwd,
                placeholderText: "同一个 state，另一副面具",
                header: "星号掩码",
                passwordChar: "*")
                .Width(250)
                .Margin(right: 8),
            TextBlock("遮罩只在<b>显示层</b>：两个框共用一份 state，"
                      + "换的是面具不是内容——「Password」里存的始终是明文。")
                .Wrap()
                .Caption()
                .Subtle());
    }
}
