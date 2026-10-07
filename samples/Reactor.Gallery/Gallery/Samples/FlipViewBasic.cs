using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 翻页视图：<c>FlipView</c> 一次显示一项，"当前第几页"就是受控的 <c>SelectedIndex</c>。
/// </summary>
/// <remarks>
/// <para>
/// 官方 <c>FlipView</c> 是 <c>Selector</c> 的派生——所以它<b>有回声问题</b>：
/// 把受控值写回去会同步抛一次 <c>SelectionChanged</c>。那一趟由框架认成回声吞掉，
/// 否则"点下一页"会变成"回调 → 改 state → 再写回 → 再回调"的死循环。
/// 左侧「受控控件诊断」那页能实时看到吞了几刀。
/// </para>
/// <para>
/// 它<b>没有</b> <c>SelectionMode</c>：一次只选一项是它自己定的，不是可调参数。
/// 项内容是<b>任意元素树</b>（这里是色块 + 文字），不是字符串——这一点与
/// <c>ComboBox</c> 那种"给一组字符串"的用法不同。
/// </para>
/// </remarks>
public sealed class FlipViewBasic : Component
{
    private static readonly (string Name, Windows.UI.Color Color, string Note)[] Pages =
    {
        ("起草", Windows.UI.Colors.SteelBlue, "第一页：先写，别管好不好看。"),
        ("自检", Windows.UI.Colors.SeaGreen, "第二页：写完读一遍，读出声。"),
        ("交付", Windows.UI.Colors.IndianRed, "第三页：这时候才轮到别人看。"),
    };

    public override Element Render()
    {
        var (index, setIndex) = UseState(0);

        var page = index >= 0 && index < Pages.Length ? Pages[index] : Pages[0];

        return VStack(12,
            FlipView(
                Optional<int>.Of(index),
                setIndex,
                ForEach(Pages, item =>
                    Border(
                        VStack(8,
                            TextBlock(item.Name).Title(),
                            TextBlock(item.Note).Wrap())
                            .Padding(24))
                        .Background(item.Color)
                        .Padding(12)
                        .AutomationName($"第 {item.Name} 页")))
                .Height(180),

            HStack(12,
                Button("上一页", () => setIndex(index <= 0 ? Pages.Length - 1 : index - 1)),
                Button("下一页", () => setIndex((index + 1) % Pages.Length)),
                TextBlock($"第 {index + 1} / {Pages.Length} 页 · {page.Name}")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            TextBlock("左右方向键、鼠标滚轮、触摸滑动都能翻——手势是官方控件自带的，"
                      + "回调走的是同一个 SelectedIndex。")
                .Caption().Subtle().Wrap());
    }
}
