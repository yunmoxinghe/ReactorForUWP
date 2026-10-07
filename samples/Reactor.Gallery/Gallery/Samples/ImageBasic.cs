using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>Image</c>：同一张源图，三种 <c>Stretch</c>。
/// </summary>
/// <remarks>
/// <c>Stretch</c> 决定的是"图的尺寸与容器尺寸不一致时怎么办"，
/// 这三个名字看着像一回事，差别只在<b>溢出与留白归谁</b>：
/// <list type="bullet">
///   <item><c>None</c>：原图尺寸，容器装不下就直接溢出（不缩放）。</item>
///   <item><c>Uniform</c>：等比缩放<b>整张都放进</b>容器，短边留白（默认值）。</item>
///   <item><c>UniformToFill</c>：等比缩放<b>填满</b>容器，长边被裁掉。</item>
/// </list>
/// 三格都套在同一个 120×120 的框里，旁边写着各自的取舍，比分开看三个图清楚。
/// <para>
/// <b>尺寸要么给全、要么都不给。</b>只给 <c>Width</c> 不给 <c>Height</c> 时，
/// <c>Uniform</c> 会按宽度算出高度，通常正是想要的；反过来在
/// <c>ScrollViewer</c> 里（它会用<b>无限宽</b>去测量内容）不给宽度的话，
/// <c>Stretch</c> 会退化——看上去就是"设了但没生效"。
/// </para>
/// </remarks>
public sealed class ImageBasic : Component
{
    private const string Source = "ms-appx:///Assets/Square150x150Logo.scale-100.png";

    public override Element Render()
    {
        return VStack(12,
            TextBlock("源图 150×150，框 120×120").Caption().Subtle(),

            HStack(12,
                Cell("None", "原图尺寸溢出", Image(Source, stretch: "None")),
                Cell("Uniform", "整张放进，留白", Image(Source, stretch: "Uniform")),
                Cell("UniformToFill", "填满，长边裁掉", Image(Source, stretch: "UniformToFill"))),

            TextBlock("另两种常用形态：").Caption().Subtle(),
            HStack(12,
                VStack(4,
                    TextBlock("只给宽度").Caption().Subtle(),
                    Image(Source, width: 64)),
                VStack(4,
                    TextBlock("Fill（拉伸变形）").Caption().Subtle(),
                    Border(Image(Source, 96, 48, stretch: "Fill"))
                        .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"), 1))));
    }

    private static Element Cell(string title, string note, Element image) =>
        VStack(4,
            TextBlock(title).Caption().Subtle(),
            Border(image)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"), 1)
                .Width(120)
                .Height(120),
            TextBlock(note).Wrap().MaxWidth(120).Caption().Subtle());
}
