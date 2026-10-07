using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>RichEditBox</c>：带格式的文本编辑。<b>文本只出不进。</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>它官方没有 <c>Text</c> 属性。</b>文本住在 <c>Document</c>
/// （<c>Windows.UI.Text.ITextDocument</c>）里，读走 <c>GetText</c>——所以本元素
/// 也<b>不提供受控文本</b>：<c>initialText</c> 只在挂载时写一次，之后由
/// <c>onTextChanged</c> 只出不进。
/// </para>
/// <para>
/// 为什么不装成受控：写进去 <c>"abc"</c>、读出来 <c>"abc\r"</c>——末尾那个
/// <c>\r</c> 是文档结构而不是用户输入的文本。剥掉它只是我们自造的归一化，
/// 控件并不认这份约定；把"受控"建立在这样一条规则上，"值没变"永远判不成立。
/// 与 <c>CalendarView</c> 那条"没有可判定的落点就不装成受控"是同一条规矩。
/// </para>
/// </remarks>
public sealed class RichEditBoxBasic : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("在这里随便打几个字。");
        var (times, setTimes) = UseState(0);
        var (readOnly, setReadOnly) = UseState(false);

        return VStack(12,
            TextBlock("文本只出不进（下面那几个开关是受控的，文本不是）").Body(),
            RichEditBox(
                    initialText: "在这里随便打几个字。",
                    onTextChanged: value =>
                    {
                        setText(value);
                        setTimes(times + 1);
                    },
                    header: "内容",
                    placeholderText: "打点什么进去",
                    isReadOnly: readOnly)
                .Height(160),

            TextBlock($"state 里 {text.Length} 个字符；回调过 {times} 次")
                .Caption().Subtle().Wrap(),

            HStack(8,
                Button(readOnly ? "只读：开" : "只读：关", () => setReadOnly(!readOnly)),
                TextBlock("只读仍可选中复制 —— 与「不给回调」不是一回事。")
                    .Caption().Subtle().Wrap()),

            TextBlock($"现在的内容：{text.Replace("\r", " ⏎ ")}")
                .Caption().Subtle().Wrap(),

            TextBlock("想整篇换掉就把这块内容换掉重建（或走 Native()）："
                      + "state → 控件这个方向在本版没有接通，这是刻意的，不是漏的。")
                .Caption().Subtle().Wrap());
    }
}
