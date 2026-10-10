using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>RelativePanel</c>：子元素之间、子元素与面板之间互相定位，不用行列。
/// </summary>
/// <remarks>
/// <para>
/// 关系写在子元素身上：<c>.Relative(below: 0, alignLeftWithPanel: true)</c>。
/// 分两类——
/// <list type="bullet">
///   <item>贴面板（<c>AlignXxxWithPanel</c>）：布尔，只问面板边界。</item>
///   <item>贴兄弟（<c>Below</c> / <c>RightOf</c> / <c>AlignLeftWith</c> …）：
///         填的是<b>同层子元素的下标</b>，不是 XAML 里的 <c>x:Name</c>。</item>
/// </list>
/// 声明式树里的元素没有名字可给，下标是这里唯一能稳定指向"另一个子元素"的东西；
/// handler 落下去的时候把它换成真正的兄弟控件，与 XAML 标记编译器把名字解析成
/// 对象引用是同一个结果。
/// </para>
/// <para>
/// 每轮渲染都按当前这一份<b>全量重落</b>（没设的项落回默认），所以"去掉一条关系"
/// 是真的会失效，不会留下上一轮的旧值——下面那个按钮就是拿掉③的 <c>Below</c>，
/// 它会立刻弹回默认位置（左上角）。
/// </para>
/// </remarks>
public sealed class RelativePanelBasic : Component
{
    public override Element Render()
    {
        var (linked, setLinked) = UseState(true);

        return VStack(12,
            HStack(8,
                Button(linked ? "拿掉③的 Below" : "把③挂回①下面", () => setLinked(!linked)),
                TextBlock(linked ? "③ 在①的正下方" : "③ 没有任何关系 → 回到默认位置（左上角）")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            RelativePanel(
                    Tag("①", Colors.SteelBlue)
                        .Relative(alignLeftWithPanel: true, alignTopWithPanel: true),

                    Tag("②", Colors.MediumSeaGreen)
                        .Relative(rightOf: 0, alignTopWith: 0)
                        .Margin(8, 0, 0, 0),

                    Tag("③", Colors.Tomato)
                        .Relative(alignLeftWith: 0, below: linked ? 0 : null)
                        .Margin(0, 8, 0, 0),

                    Tag("④", Colors.MediumPurple)
                        .Relative(alignRightWithPanel: true, alignBottomWithPanel: true),

                    Tag("⑤", Colors.DarkOrange)
                        .Relative(alignHorizontalCenterWithPanel: true, alignBottomWithPanel: true))
                // 官方给的是固定宽度：RelativePanel 自己没有"内容宽度"，
                // 不写死它，「贴右边」「贴底」那几条关系就没有边界可贴。
                .Width(300)
                .Height(220)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            // 五块统一 50×50（对齐官方）。写清关系的那句话挪到这里当图例：
            // 塞进 50×50 会撑出边框，几何反而看不清。
            TextBlock("① 贴左上　② 在①右边、与①顶对齐（Margin 8,0,0,0）　"
                      + "③ 在①下面（Margin 0,8,0,0）　④ 贴右下　⑤ 水平居中、贴底")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("下标指的是<b>同层子元素的位置</b>（从 0 数），不是控件类型也不是名字；"
                      + "越界（删了那一格却忘了改关系）按「这条关系不成立」处理，不抛异常。")
                .Caption()
                .Subtle()
                .Wrap());
    }

    private static Element Tag(string text, Color color) =>
        Border(TextBlock(text)
                .Foreground(Colors.White)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center))
            .Background(color)
            .Size(50, 50);
}
