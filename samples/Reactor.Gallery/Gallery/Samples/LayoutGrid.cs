using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 二维布局：<c>Grid</c> 的行列 + <c>Border</c>。
/// </summary>
/// <remarks>
/// 行列用 <see cref="GridSize"/> 声明（<c>Auto</c> / <c>Star()</c> / <c>Px(double)</c>），
/// 子元素用 <c>.Grid(row:, column:)</c> 落位——与 XAML 的 <c>Grid.Row</c> /
/// <c>Grid.Column</c> 附加属性一一对应。
/// <para>
/// <b><c>Auto</c> 与 <c>Star</c> 的取舍</b>：<c>Auto</c> 按内容收缩（表单左侧的标签列
/// 用它）；<c>Star</c> 分剩余空间（主内容区用它）。两个都给 <c>Star</c> 就按比例分。
/// </para>
/// </remarks>
public sealed class LayoutGrid : Component
{
    public override Element Render() =>
        VStack(12,
            Grid(
                new[] { GridSize.Auto, GridSize.Star() },
                new[] { GridSize.Auto, GridSize.Auto },
                TextBlock("用户名")
                    .Grid(row: 0, column: 0)
                    .VAlign(VerticalAlignment.Center)
                    .Padding(0, 8, 12, 8),
                TextBlock("alice")
                    .Grid(row: 0, column: 1)
                    .VAlign(VerticalAlignment.Center),
                TextBlock("邮箱")
                    .Grid(row: 1, column: 0)
                    .VAlign(VerticalAlignment.Center)
                    .Padding(0, 8, 12, 8),
                TextBlock("alice@example.com")
                    .Grid(row: 1, column: 1)
                    .VAlign(VerticalAlignment.Center)),

            Border(
                    TextBlock("Border：边框 + 背景 + 圆角，装任何东西")
                        .Padding(12))
                .WithBorder(
                    ThemeResource.Brush("CardStrokeColorDefaultBrush"), 1));
}
