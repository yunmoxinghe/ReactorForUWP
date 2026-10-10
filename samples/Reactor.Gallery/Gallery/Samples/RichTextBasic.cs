using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 富文本：<c>RichTextBlock</c> + <c>Paragraph</c> + <c>Run</c>。
/// </summary>
/// <remarks>
/// 一行里多段异色文本，就是 <c>Run</c> 的用武之地——语法高亮本质上是同一件事
/// （画廊的源码框用的也是这三个类型，只是由扫描器自动切出来的）。
/// <para>
/// 用一串 <c>TextBlock</c> 横着拼做不到三件事：文本跨元素连续可选、复制出来带换行、
/// UIA 看到的是一段完整文本而不是一堆碎片。最后一条直接影响屏幕阅读器的朗读连贯性。
/// </para>
/// </remarks>
public sealed class RichTextBasic : Component
{
    public override Element Render() =>
        VStack(12,
            RichTextBlock(
                Paragraph(Run("I am a RichTextBlock."))),

            RichTextBlock(
                Paragraph(
                    Run("这一段由三段拼在一起：", bold: true),
                    Run("第一段是强调色。")
                        .Foreground(ThemeResource.Brush("AccentFillColorDefaultBrush")),
                    Run("第二段回到默认前景色，"),
                    Run("第三段是斜体。", italic: true)),
                Paragraph(24.0,
                    Run("带缩进的第二段——靠 Paragraph 的左边距，而不是在一串 Run 前面塞空格。"))));
}
