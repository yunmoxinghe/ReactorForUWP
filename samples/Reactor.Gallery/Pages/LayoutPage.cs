using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Samples.Pages;

/// <summary>
/// 布局：Grid / Border / Stack 间距 / 对齐。
/// </summary>
/// <remarks>
/// <c>Grid</c> 的行列用 <see cref="GridSize"/> 声明，子元素用 <c>.Grid(row:, column:)</c>
/// 占位——和 XAML 的 <c>Grid.Row</c> / <c>Grid.Column</c> 附加属性一一对应。
/// </remarks>
public sealed class LayoutPage : Component
{
    public override Element Render()
    {
        return ScrollViewer(
            VStack(12,
                TextBlock("布局").FontSize(20),

                TextBlock("Grid 表单：两列（Auto + 星号）").FontSize(16),
                Grid(
                    new[] { GridSize.Auto, GridSize.Star() },
                    new[] { GridSize.Auto, GridSize.Auto },
                    TextBlock("姓名").Grid(row: 0, column: 0),
                    TextBox(placeholderText: "请输入姓名").Grid(row: 0, column: 1),
                    TextBlock("邮箱").Grid(row: 1, column: 0),
                    TextBox(placeholderText: "name@example.com").Grid(row: 1, column: 1)),

                TextBlock("Border：圆角 + 背景 + 内边距").FontSize(16),
                new BorderElement(
                    VStack(4,
                        TextBlock("带边框的容器").BodyStrong(),
                        TextBlock("CornerRadius / Background / Padding 都是普通的 CLR 属性，直接给值即可。").Wrap()))
                {
                    CornerRadius = 8,
                    Background = new SolidColorBrush(Colors.LightGray),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = Thick(1),
                    Padding = Thick(12),
                },

                TextBlock("对齐与间距").FontSize(16),
                HStack(8,
                    Button("左").HAlign(Windows.UI.Xaml.HorizontalAlignment.Left),
                    Button("中").HAlign(Windows.UI.Xaml.HorizontalAlignment.Center),
                    Button("右").HAlign(Windows.UI.Xaml.HorizontalAlignment.Right)),
                TextBlock("上面三个按钮在等宽的水平 Stack 里，各自声明了自己的对齐方式。").Caption()
            ).Padding(16));
    }
}
