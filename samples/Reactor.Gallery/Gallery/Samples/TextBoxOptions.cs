using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>TextBox</c> 的"形态"那一族：多行 / 只读 / 拼写检查 / 长度上限。
/// </summary>
/// <remarks>
/// <para>
/// <b>多行是<b>两个</b>属性凑出来的。</b><c>acceptsReturn</c> 只管回车键的语义
/// （换行而不是"确认"），要让长文本真的折行还得再给 <c>.Wrap()</c>
/// ——它落到官方的 <c>TextBox.TextWrapping</c>。只给一个会出现"能换行但文字
/// 仍然一行顶到头"或"折行但回车被当成提交"的半吊子形态。
/// </para>
/// <para>
/// <b>只读与禁用不是一回事。</b><c>isReadOnly</c> 下文本<b>仍可选中、复制</b>，
/// 只是改不动；<c>.IsEnabled(false)</c> 会把选中复制一起禁掉。
/// 想"展示一段可复制的文本"用只读。
/// </para>
/// <para>
/// <b>官方画廊里那个"清除按钮"（<c>ClearButtonVisibility</c>）本库没有。</b>
/// 实测 UWP 的 <c>Windows.Foundation.UniversalApiContract.winmd</c> 里<b>根本没有</b>
/// <c>ClearButtonVisibility</c> / <c>TextBoxClearButtonVisibility</c> 这两个名字
/// （grep 整个 winmd 零命中）——它是 WinUI 3 才加的属性。装一个"能写但官方没有"
/// 的旋钮只会让人以为它该生效，所以按一贯规矩：不装，走 <c>Native()</c>。
/// </para>
/// </remarks>
public sealed class TextBoxOptions : Component
{
    public override Element Render()
    {
        var (shortText, setShortText) = UseState("");

        return VStack(12,
            TextBlock("多行").Caption().Subtle(),
            TextBox(
                    header: "AcceptsReturn + .Wrap()",
                    placeholderText: "回车换行，长文本折行")
                .Wrap()
                .Height(80),

            TextBlock("只读（仍可选中复制）").Caption().Subtle(),
            TextBox("这段改不动，但可以选中复制", header: "IsReadOnly", isReadOnly: true),

            TextBlock("拼写检查").Caption().Subtle(),
            VStack(6,
                TextBox(header: "开着（官方默认）", placeholderText: "输一个错字看看"),
                TextBox(header: "关掉", placeholderText: "红波浪线不再出现", isSpellCheckEnabled: false)),

            TextBlock("长度上限").Caption().Subtle(),
            TextBox(
                Optional<string>.Of(shortText),
                setShortText,
                header: "MaxLength = 5",
                placeholderText: "最多五个字",
                maxLength: 5),
            TextBlock($"已输入 {shortText.Length} / 5").Caption().Subtle(),

            TextBlock("这四样都不是受控属性：用户改不动它们，所以本库不为它们做回声抑制。")
                .Caption().Subtle().Wrap());
    }
}
