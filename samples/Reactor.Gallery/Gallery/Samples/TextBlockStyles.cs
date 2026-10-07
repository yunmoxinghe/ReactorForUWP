using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 文本排版：<c>TextBlock</c> 配字号 / 字重 / 换行修饰。
/// </summary>
/// <remarks>
/// <c>TextBlock</c> 落到屏幕上就是 WinUI 那个真控件，所以 <c>.Caption()</c> /
/// <c>.Title()</c> 这些修饰不是自定的字号常量，而是套 XAML 同名命名样式
/// （<c>CaptionTextBlockStyle</c> / <c>TitleTextBlockStyle</c> …）：
/// 换主题、改字号设置时的行为与 XAML 版本一致。
/// </remarks>
public sealed class TextBlockStyles : Component
{
    public override Element Render() =>
        VStack(8,
            TextBlock("标题 Title").Title(),
            TextBlock("大标题 TitleLarge").TitleLarge(),
            TextBlock("副标题 Subtitle").Subtitle(),
            TextBlock("正文加粗 BodyStrong").BodyStrong(),
            TextBlock("正文 Body").Body(),
            TextBlock("辅助文字 Caption（次要信息用这一档）").Caption(),
            TextBlock("弱化辅助文字 Subtle + Caption").Caption().Subtle(),
            TextBlock("换行演示：这一段刻意写得很长，用来看打开折行之后一行放不下时是怎么排到下一行的，超过两行则截断。")
                .Wrap()
                .MaxLines(2)
                .Caption());
}
