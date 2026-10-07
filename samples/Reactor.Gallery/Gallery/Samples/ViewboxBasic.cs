using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>Viewbox</c>：把<b>一个</b>子元素整体缩放到可用空间里。
/// </summary>
/// <remarks>
/// <para>
/// 它是布局族里唯一的<b>单子元素</b>容器——要缩放一整片内容，先把那片内容装进
/// <c>VStack</c> 再塞进去（官方的 <c>Viewbox.Child</c> 只收一个 <c>UIElement</c>）。
/// </para>
/// <para>
/// 两个旋钮各管一件事：
/// <list type="bullet">
///   <item><c>Stretch</c>：<b>怎么缩放</b>。<c>Uniform</c> 保持比例塞进去（可能留白）；
///         <c>UniformToFill</c> 保持比例填满（可能裁掉）；<c>Fill</c> 拉满（变形）；
///         <c>None</c> 原样不缩。</item>
///   <item><c>StretchDirection</c>：<b>允许往哪个方向缩</b>。
///         <c>UpOnly</c> 只放大、<c>DownOnly</c> 只缩小、<c>Both</c> 都行。</item>
/// </list>
/// 本例每一格的可用空间固定（150×84），内容固定（一行 28 号字），
/// 所以四档 <c>Stretch</c> 的差别一眼就能看出来。
/// </para>
/// </remarks>
public sealed class ViewboxBasic : Component
{
    private static readonly (Stretch Stretch, string Note)[] Modes =
    {
        (Stretch.Uniform, "保持比例、整份塞进去（左右留白）"),
        (Stretch.UniformToFill, "保持比例、填满（上下被裁）"),
        (Stretch.Fill, "拉满可用空间（字被拉宽）"),
        (Stretch.None, "原样，不缩放（超出就裁）"),
    };

    public override Element Render()
    {
        var (downOnly, setDownOnly) = UseState(false);

        return VStack(12,
            HStack(8,
                Button(downOnly ? "StretchDirection = Both" : "StretchDirection = DownOnly",
                    () => setDownOnly(!downOnly)),
                TextBlock(downOnly
                        ? "两个方向都允许"
                        : "只允许缩小——内容比可用空间小时就不再放大，保持原尺寸")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            HStack(12,
                Modes.Select(mode => Cell(mode.Stretch, mode.Note, downOnly)).ToArray()),

            TextBlock("注意「None」那格：内容原样摆放，超出 Viewbox 边界的部分被裁掉——"
                      + "这不是 bug，官方 Stretch.None 的定义就是不做任何缩放。")
                .Caption()
                .Subtle()
                .Wrap());
    }

    private static Element Cell(Stretch stretch, string note, bool downOnly) =>
        VStack(4,
            Viewbox(
                    TextBlock("Aa").FontSize(28).Foreground(Colors.SteelBlue),
                    stretch: stretch,
                    stretchDirection: downOnly ? StretchDirection.DownOnly : StretchDirection.Both)
                .Width(150)
                .Height(84)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),
            TextBlock(stretch.ToString()).Caption(),
            TextBlock(note).Caption().Subtle().Wrap().Width(150));
}
