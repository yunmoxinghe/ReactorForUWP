using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>PipsPager</c>：一排小点表示"第几页 / 共几页"，外加前后翻页两个按钮。
/// </summary>
/// <remarks>
/// <para>
/// <b>它自己不装内容。</b>官方 <c>PipsPager</c> 只是指示与操作入口，页面内容由外面
/// 自己摆——下面那张色块就是"外面"，它跟着 <c>SelectedPageIndex</c> 换颜色。
/// </para>
/// <para>
/// <b>属性名与事件名不对称，是照官方抄的。</b>属性叫
/// <c>SelectedPageIndex</c>（不是 <c>SelectedIndex</c>），回执事件反倒叫
/// <c>SelectedIndexChanged</c>。
/// </para>
/// <para>
/// <b>改页数会把选中夹回范围里。</b>从 10 页改成 3 页时若停在第 8 页，控件会自己
/// 夹到边界内——夹出来的值事先不知道，所以那几个写入罩进静默窗。本例在改页数的
/// 同时自己也夹一次 state，两边就对得上。
/// </para>
/// </remarks>
public sealed class PipsPagerBasic : Component
{
    public override Element Render()
    {
        var (page, setPage) = UseState(0);
        var (pages, setPages) = UseState(10);
        var (orientation, setOrientation) = UseState(Orientation.Horizontal);

        return VStack(12,
            TextBlock("点小点或前后按钮翻页；下面那块颜色是「外面」的内容，它跟着受控值走。")
                .Caption().Subtle().Wrap(),

            Border(
                    VStack(4,
                        TextBlock($"第 {page + 1} 页").Body(),
                        TextBlock($"共 {pages} 页").Caption().Subtle())
                        .VAlign(VerticalAlignment.Center)
                        .HAlign(HorizontalAlignment.Center))
                .Background(PageColor(page))
                .Height(96)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            PipsPager(
                    numberOfPages: pages,
                    maxVisiblePips: 8,
                    orientation: orientation,
                    selectedPageIndex: page,
                    onSelectedPageIndexChanged: setPage)
                .Margin(0, 12, 0, 0)
                .HAlign(HorizontalAlignment.Center),

            HStack(8,
                Button(pages == 10 ? "页数：10 → 3" : "页数：3 → 10",
                    () =>
                    {
                        var next = pages == 10 ? 3 : 10;
                        setPages(next);

                        // state 自己也要夹一次：不夹的话 state 停在 8、控件被夹到 2，
                        // 下一轮受控写回又把控件拽回 8（那才是"点了没反应"）。
                        if (page > next - 1)
                        {
                            setPage(next - 1);
                        }
                    }),
                Button(orientation == Orientation.Horizontal ? "横排 → 竖排" : "竖排 → 横排",
                    () => setOrientation(orientation == Orientation.Horizontal
                        ? Orientation.Vertical
                        : Orientation.Horizontal))),

            TextBlock("受控值给 null 就是「不管它」：那时点小点照样翻页，只是 state 不知道。")
                .Caption().Subtle().Wrap());
    }

    private static Color PageColor(int page)
    {
        var hue = (page * 47) % 360;
        return Hsv(hue);
    }

    /// <summary>HSV → RGB（只做色相，饱和度与亮度固定，够演示用）。</summary>
    private static Color Hsv(double hue)
    {
        const double s = 0.45;
        const double v = 0.92;
        var c = v * s;
        var x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
        var m = v - c;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromArgb(
            255,
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255));
    }
}
