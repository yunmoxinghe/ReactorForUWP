using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 带边框的背景块：<c>Border</c>。
/// </summary>
/// <remarks>
/// 官方那页演示的三件事：<b>只给描边</b>、<b>描边 + 背景</b>、<b>四边不等厚</b>。
/// <list type="bullet">
///   <item><c>Border</c> 是<b>只能装一个子元素</b>的装饰器（<c>Decorator</c>）：
///         它自己不排版，把内容原样摆出来，然后在周围加那一圈。</item>
///   <item><b>描边色收的是刷子</b>（<c>Brush</c>）不是颜色字符串：
///         这里用 <c>ThemeResource.Brush(...)</c> 取主题资源——切主题时它自己换色，
///         写死一个 <c>Color</c> 就等于替官方决定了"深色模式下边框是什么颜色"。</item>
///   <item><b>圆角不在这个样例里。</b>本库没有给 <c>Border</c> 装
///         <c>CornerRadius</c> 修饰器：官方的 <c>CornerRadius</c> 是 <c>Border</c>
///         自己的属性，而"卡片该圆多少"在 WinUI 2 里由卡片类控件（
///         <c>SettingsCard</c> 等）的模板定，逐块手写会和主题里的那一档打架。
///         真要给某一块单独圆角，用 <c>Native()</c> 拿到真控件再设。</item>
///   <item><c>Padding</c> 是"内容与那条边之间的距离"，<b>不是</b>外边距：
///         它让背景与描边把内容包进去；要"块与块之间留白"用父容器的
///         <c>spacing</c> 或子项的 <c>Margin</c>。</item>
/// </list>
/// </remarks>
public sealed class BorderBasic : Component
{
    /// <summary>
    /// 官方那一档写死的金色描边。<b>做成静态字段</b>：刷子按<b>引用</b>比，
    /// 每轮 <c>new</c> 一支就是一次真的重绘。
    /// </summary>
    private static readonly SolidColorBrush Gold = new(Colors.Gold);

    public override Element Render() =>
        VStack(20,
            TextBlock("只有描边").Body(),

            Border(
                    TextBlock("一段被圈起来的文字").Padding(12))
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            TextBlock("描边 + 背景 + 内边距").Body(),

            Border(
                    TextBlock("背景色与描边色都取主题资源：切主题时两块一起换").Padding(16))
                .Background(ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"))
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            TextBlock("四边不等厚（左 8、上 1、右 1、下 1）").Body(),

            Border(
                    VStack(4,
                        TextBlock("强调的那一侧").Body(),
                        TextBlock("官方 Thickness 的顺序是 左 / 上 / 右 / 下，"
                                  + "与 CSS 的 上 / 右 / 下 / 左 不一样 —— 抄错顺序是最常见的一个坑。")
                            .Caption()
                            .Subtle()
                            .Wrap())
                        .Padding(12))
                .Background(ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"))
                .BorderThickness(new Thickness(8, 1, 1, 1))
                .BorderBrush(ThemeResource.Brush("AccentFillColorDefaultBrush")),

            TextBlock("写死颜色那一档（官方原样：白底 + 金色 2px 边 + 黑字）").Body(),

            // 白底 + 金色 2px 边 + 内文 Margin(8,5) / FontSize(18)：官方这一档的原样。
            Border(
                    TextBlock("Text inside a border")
                        .Margin(8, 5)
                        .FontSize(18)
                        .Foreground(Colors.Black))
                .Background(Colors.White)
                .WithBorder(Gold, 2),

            TextBlock("这一段是本页唯一写死颜色的地方，留它是为了照出「写死」的代价："
                      + "切到深色模式，白底金边不会跟着变，它还是浅色主题那一套。"
                      + "上面那几段取主题资源就是为了避开这件事。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("Border 只装一个子元素：要并排放两样东西，得先在里面套一个 "
                      + "VStack / HStack，而不是往 Border 里塞第二项。")
                .Caption()
                .Subtle()
                .Wrap());
}
