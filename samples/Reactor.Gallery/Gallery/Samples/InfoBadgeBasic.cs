using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>InfoBadge</c>：贴在别的控件角落上的一个小圆点 / 数字 / 图标。
/// </summary>
/// <remarks>
/// <b>它没有"自己是谁的徽章"这层关系。</b>位置完全由布局说了算——官方用法是把它
/// 丢进某个容器（<c>Grid</c> 的一角、列表项的右侧），靠对齐与外边距定位。
/// 想让它跟着某个控件走，就把它们放进同一个容器，别指望徽章自己去找主人。
/// <para>
/// <c>Value = -1</c> 是官方的"圆点"那一档（不显示数字），不是本仓库的哨兵值。
/// 给了 <c>icon</c> 就不再显示数字——两种形态是互相替换的。
/// </para>
/// <para>
/// 预设样式（<c>badgeStyle</c>）取自 WinUI 的 XAML 资源（<c>SuccessBadgeStyle</c>
/// 那几个），因此<b>宿主必须加载 <c>XamlControlsResources</c></b>；没加载时徽章
/// 仍是默认外观（就是 Informational 那一档），只是换不了色。
/// </para>
/// </remarks>
public sealed class InfoBadgeBasic : Component
{
    public override Element Render()
    {
        return VStack(14,
            TextBlock("数字与圆点").Caption().Subtle(),
            HStack(12,
                Cell("圆点（Value = -1）", InfoBadge()),
                Cell("数字 5", InfoBadge(5)),
                Cell("数字 99+", InfoBadge(99)),
                Cell("图标", InfoBadge(icon: FontIcon("\uE8BD")))),

            TextBlock("预设样式").Caption().Subtle(),
            HStack(12,
                Cell("Informational", InfoBadge(7)),
                Cell("Success", InfoBadge(7, badgeStyle: "Success")),
                Cell("Warning", InfoBadge(7, badgeStyle: "Warning")),
                Cell("Critical", InfoBadge(7, badgeStyle: "Critical")),
                Cell("Attention", InfoBadge(7, badgeStyle: "Attention"))),

            TextBlock("挂在别人身上（放进同一个 Grid 的右上角）").Caption().Subtle(),
            Grid(
                new[] { "Auto", "Auto" },
                new[] { "Auto", "Auto" },
                FontIcon("\uE77B", fontSize: 24).Grid(row: 0, column: 0, rowSpan: 2),
                InfoBadge(3, badgeStyle: "Critical")
                    .HAlign(HorizontalAlignment.Right)
                    .VAlign(VerticalAlignment.Top)
                    .Grid(row: 0, column: 1)));
    }

    private static Element Cell(string label, Element badge) =>
        VStack(4,
            badge,
            TextBlock(label).Caption().Subtle());
}
