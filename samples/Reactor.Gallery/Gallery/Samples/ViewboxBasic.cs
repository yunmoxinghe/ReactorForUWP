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
/// 本例每一格的可用空间由下面那根滑杆给（官方也是 <c>Width</c> / <c>Height</c>
/// 绑同一个值），内容固定（一行 28 号字），所以四档 <c>Stretch</c> 的差别
/// 一眼就能看出来。
/// </para>
/// </remarks>
public sealed class ViewboxBasic : Component
{
    // 官方复合内容里是一张小图；本地用生成的扇叶图标（透明背景，见 tools/parity/make_sample_media.py）。
    private const string Source = "ms-appx:///Assets/SampleMedia/slices.png";

    // ── 刷子做成静态字段 ───────────────────────────────────────
    // 刷子按「引用」比：每轮渲染 new 一支就是一次真的重绘，写内联会每帧重画。
    private static readonly SolidColorBrush Red = new(Colors.Red);
    private static readonly SolidColorBrush Blue = new(Colors.Blue);
    private static readonly SolidColorBrush Green = new(Colors.Green);
    private static readonly SolidColorBrush Yellow = new(Colors.Yellow);
    private static readonly SolidColorBrush Gray = new(Colors.Gray);

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
        var (size, setSize) = UseState(150.0);

        return VStack(12,
            HStack(8,
                Button(downOnly ? "StretchDirection = Both" : "StretchDirection = DownOnly",
                    () => setDownOnly(!downOnly)),
                TextBlock(downOnly
                        ? "两个方向都允许"
                        : "只允许缩小——内容比可用空间小时就不再放大，保持原尺寸")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            HStack(8,
                Slider(
                    value: size,
                    min: 40,
                    max: 360,
                    onValueChanged: value => setSize(value))
                    .Width(240),
                TextBlock($"可用空间 {size:0} × {size:0}（官方 Width / Height 绑的是同一个值）")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            HStack(12,
                Modes.Select(mode => Cell(mode.Stretch, mode.Note, downOnly, size)).ToArray()),

            TextBlock("注意「None」那格：内容原样摆放，超出 Viewbox 边界的部分被裁掉——"
                      + "这不是 bug，官方 Stretch.None 的定义就是不做任何缩放。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("官方那套复合内容：一圈 15px 灰边里装着色条 + 图 + 文字").Body(),

            HStack(12,
                Composite(downOnly).Size(size, size),
                TextBlock("整份内容（连那圈 15px 灰边一起）被当成一个整体缩放 —— 这是"
                          + "Viewbox 与 Border 的根本差别：Border 只在内容外面加一圈，"
                          + "Viewbox 改的是内容实际画出来多大。")
                    .Caption().Subtle().Wrap().MaxWidth(280)));
    }

    private static Element Cell(Stretch stretch, string note, bool downOnly, double size) =>
        VStack(4,
            Viewbox(
                    TextBlock("Aa").FontSize(28).Foreground(Colors.SteelBlue),
                    stretch: stretch,
                    stretchDirection: downOnly ? StretchDirection.DownOnly : StretchDirection.Both)
                .Size(size, size)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),
            TextBlock(stretch.ToString()).Caption(),
            TextBlock(note).Caption().Subtle().Wrap().Width(size));

    /// <summary>
    /// 官方那一档的复合内容：一整片（色条 + 图 + 文字）装进一圈灰边里。
    /// 官方 <c>Viewbox.Child</c> 只收<b>一个</b>子元素，所以要先在 <c>VStack</c> 里拼好。
    /// </summary>
    private static Element Composite(bool downOnly) =>
        Viewbox(
            child: Border(
                    VStack(
                        HStack(
                            Rectangle().Fill(Blue).Size(40, 10),
                            Rectangle().Fill(Green).Size(40, 10),
                            Rectangle().Fill(Red).Size(40, 10),
                            Rectangle().Fill(Yellow).Size(40, 10)),
                        Image(Source),
                        TextBlock("这是一段文字。").HAlign(HorizontalAlignment.Center)))
                .Background(Colors.DarkGray)
                .WithBorder(Gray, 15),
            stretch: Stretch.Uniform,
            stretchDirection: downOnly ? StretchDirection.DownOnly : StretchDirection.Both);
}
