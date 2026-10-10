using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
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
    // ── 色块用的刷子做成静态字段 ───────────────────────────────
    // 刷子按「引用」比：每轮渲染 new 一支就是一次真的重绘，写内联会每帧重画。
    private static readonly SolidColorBrush Red = new(Colors.Red);
    private static readonly SolidColorBrush Blue = new(Colors.Blue);
    private static readonly SolidColorBrush Green = new(Colors.Green);
    private static readonly SolidColorBrush Yellow = new(Colors.Yellow);

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

            TextBlock("官方那张 3×3 定长网格：轨道 50 / 50 / 50，容器 240×160 灰底").Body(),

            Grid(
                new[] { GridSize.Px(50), GridSize.Px(50), GridSize.Px(50) },
                new[] { GridSize.Px(50), GridSize.Px(50), GridSize.Px(50) },
                Rectangle().Fill(Red).Size(50, 50).Grid(row: 0, column: 0),
                Rectangle().Fill(Blue).Size(50, 50).Grid(row: 1, column: 0),
                Rectangle().Fill(Green).Size(50, 50).Grid(row: 0, column: 1),
                Rectangle().Fill(Yellow).Size(50, 50).Grid(row: 1, column: 1))
                .Background(Colors.Gray)
                .Size(240, 160),

            TextBlock("定长轨道（Px）与 Auto / Star 的差别就在这儿：轨道尺寸先定死，"
                      + "右边与下边多出来的那一块没人占 —— 那片灰就是没被占的空间。")
                .Caption()
                .Subtle()
                .Wrap(),

            Border(
                    TextBlock("Border：边框 + 背景 + 圆角，装任何东西")
                        .Padding(12))
                .WithBorder(
                    ThemeResource.Brush("CardStrokeColorDefaultBrush"), 1));
}
