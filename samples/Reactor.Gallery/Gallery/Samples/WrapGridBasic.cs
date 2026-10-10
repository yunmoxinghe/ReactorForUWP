using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>VariableSizedWrapGrid</c>：按固定格子排，单个子项可以跨行跨列。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>Grid</c> 的差别就两个字：<b>没有行列号</b>。格子位置由换行顺序自己决定，
/// 子元素只能说"我占几格"——<c>.WrapSpan(rowSpan: …, columnSpan: …)</c>，
/// 对应 XAML 的 <c>VariableSizedWrapGrid.RowSpan</c> / <c>ColumnSpan</c>。
/// 这也是它不复用 <c>.Grid(row: …)</c> 那份附加位置的原因：Row / Column 在这里
/// 没有落点，写出来会被静默忽略。
/// </para>
/// <para>
/// 另两个旋钮：<c>ItemWidth</c> / <c>ItemHeight</c> 定格子大小，
/// <c>MaximumRowsOrColumns</c> 定<b>另一条轴</b>上最多排几格
/// （Horizontal 时它是最大列数，Vertical 时是最大行数）。
/// 格子尺寸是死的——它不会像 <c>GridView</c> 那样按内容自适应。
/// </para>
/// </remarks>
public sealed class WrapGridBasic : Component
{
    private static readonly (string Label, Color Color, int RowSpan, int ColumnSpan)[] Cells =
    {
        ("1×1", Colors.SteelBlue, 1, 1),
        ("2×2", Colors.Tomato, 2, 2),
        ("1×1", Colors.MediumSeaGreen, 1, 1),
        ("1×2", Colors.DarkOrange, 1, 2),
        ("2×1", Colors.MediumPurple, 2, 1),
        ("1×1", Colors.DimGray, 1, 1),
        ("1×1", Colors.Teal, 1, 1),
    };

    public override Element Render()
    {
        var (vertical, setVertical) = UseState(false);
        var (max, setMax) = UseState(3);

        return VStack(12,
            HStack(8,
                Button(vertical ? "改成 Horizontal" : "改成 Vertical", () => setVertical(!vertical)),
                Button(max == 3 ? "最多 6 格" : "最多 3 格", () => setMax(max == 3 ? 6 : 3)),
                TextBlock($"Orientation = {(vertical ? "Vertical" : "Horizontal")}；"
                          + $"MaximumRowsOrColumns = {max}")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            WrapGrid(
                    itemWidth: 44,
                    itemHeight: 44,
                    maximumRowsOrColumns: max,
                    orientation: vertical ? Orientation.Vertical : Orientation.Horizontal,
                    children: Cells.Select(cell => Block(cell).WrapSpan(cell.RowSpan, cell.ColumnSpan))
                        .ToArray())
                // 官方给的是固定容器宽度：不写死它就会一路撑满父容器，
                // 「最多 3 格」这件事在宽容器里根本看不出来。
                .Width(400)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            TextBlock("跨格的块会把后面的块挤到下一处空位——官方不会重排已放下的项，"
                      + "所以顺序变了结果就变了，这不是布局 bug。")
                .Caption()
                .Subtle()
                .Wrap());
    }

    private static Element Block((string Label, Color Color, int RowSpan, int ColumnSpan) cell) =>
        Border(TextBlock(cell.Label)
                .Foreground(Colors.White)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center))
            .Background(cell.Color);
}
