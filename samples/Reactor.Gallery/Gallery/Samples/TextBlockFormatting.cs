using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>TextBlock</c> 的"字怎么排"那一族：截断 / 彩色字形 / 字距。
/// </summary>
/// <remarks>
/// <para>
/// <b>截断要两个修饰器凑齐。</b><c>.MaxLines(n)</c> 只说"最多排 n 行"，
/// 超出的部分<b>默认是直接裁掉</b>（行被切一半、没有提示）；要不要显示"…"由
/// <c>.TextTrimming(...)</c> 单独决定。只给前者会得到"文字断得莫名奇妙"，
/// 只给后者则永远不触发（因为没有行数上限，文字会一直往下排）。
/// 上一页那条"超过两行则截断"的就是只给了 <c>MaxLines</c> 的样子，可以对着看。
/// </para>
/// <para>
/// <b>截断需要"宽度被限住"才看得见。</b>放进 <c>VStack</c> 这种按内容撑开的容器，
/// 文字想排几行就排几行，永远不会超出——所以下面每一条都显式给了 <c>Width</c>。
/// </para>
/// <para>
/// <c>.ColorFont(false)</c> 关的是<b>彩色字形</b>（emoji 那类按彩色绘制的字形）。
/// 关掉之后 emoji 会退化成单色轮廓。这只影响"字体自带多色图层"的字符，
/// 对普通汉字与拉丁字母没有可见差别。
/// </para>
/// </remarks>
public sealed class TextBlockFormatting : Component
{
    private const string Long =
        "这一段刻意写得很长，用来看宽度被限住之后多行文本是怎么被处理掉的："
        + "不给截断则一直往下排，给了 MaxLines 则排到指定行数为止，"
        + "再配 TextTrimming 才会在断口显示省略号。";

    public override Element Render() =>
        VStack(14,
            TextBlock("截断：三种组合").Body(),

            VStack(6,
                TextBlock("只给 MaxLines(2)：断口是硬裁的，没有省略号。")
                    .Caption().Subtle(),
                TextBlock(Long).Wrap().MaxLines(2).Width(320)),

            VStack(6,
                TextBlock("MaxLines(2) + TextTrimming：断口显示「…」。")
                    .Caption().Subtle(),
                TextBlock(Long)
                    .Wrap()
                    .MaxLines(2)
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .Width(320)),

            VStack(6,
                TextBlock("不限行数 + TextTrimming：单行的省略号（默认不折行时就是这个形状）。")
                    .Caption().Subtle(),
                TextBlock(Long)
                    .TextTrimming(TextTrimming.WordEllipsis)
                    .Width(320)),

            TextBlock("彩色字形与字距").Body(),
            HStack(28,
                VStack(4,
                    TextBlock("默认（彩色）").Caption().Subtle(),
                    TextBlock("🌍 🐺 ✅ 🎉").FontSize(24)),
                VStack(4,
                    TextBlock("ColorFont(false)").Caption().Subtle(),
                    TextBlock("🌍 🐺 ✅ 🎉").FontSize(24).ColorFont(false))),

            VStack(4,
                TextBlock("字距 0 / 100 / -40（单位 1/1000 em）").Caption().Subtle(),
                TextBlock("字距 字距 字距").FontSize(18).CharacterSpacing(0),
                TextBlock("字距 字距 字距").FontSize(18).CharacterSpacing(100),
                TextBlock("字距 字距 字距").FontSize(18).CharacterSpacing(-40)),

            TextBlock("字距是「每两个字之间多塞多少」，不是缩放字号，也不是行距。")
                .Caption().Subtle().Wrap());
}
