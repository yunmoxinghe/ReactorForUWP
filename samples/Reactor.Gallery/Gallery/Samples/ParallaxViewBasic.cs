using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using MuxControls = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>ParallaxView</c>：参照另一处滚动的进度，把自己这片内容错开一点（WinUI 2 真控件）。
/// </summary>
/// <remarks>
/// <para>
/// <b>它要两个东西，不是一个。</b><c>Child</c> 是"被错开的那片内容"（本元素唯一的
/// 子槽位），<c>Source</c> 是"参照谁的滚动"——官方 <c>ParallaxView.Source</c>
/// 通常是<b>兄弟节点上那个 <c>ScrollViewer</c></b>，不在自己的子树里。
/// 声明式树里没有 <c>x:Name</c> 可以引用，所以这里填的是
/// <b>同层下标</b>（<c>sourceIndex</c>）：下面 <c>Grid</c> 里第 0 个是视差层、
/// 第 1 个是滚动区，于是写 <c>sourceIndex: 1</c>。
/// </para>
/// <para>
/// <b>下标指错不会崩，只会"看不出效果"。</b>视差靠订阅源头的滚动进度工作，
/// 官方取的是"源头或其子树里的滚动宿主"；取不到就是安静地不动，既不报错也不抛。
/// 所以这里越界时给 <c>null</c> 而不是抛——与 <c>TeachingTip</c> 的
/// <c>targetIndex</c> 是同一条规矩。
/// </para>
/// <para>
/// <b>它没有受控属性。</b>能被用户改动的只有"滚动到哪儿"，而那是
/// <c>Source</c> 的状态，不是它的。
/// </para>
/// </remarks>
public sealed class ParallaxViewBasic : Component
{
    private static readonly string[] Lines =
    {
        "视差不是动画，是把两个滚动量按比例错开：",
        "后面那几块彩色方块跟着这段文字一起滚，但滚得慢一点。",
        "",
        "官方 ParallaxView.Source 要的是「参照谁的滚动」，通常是兄弟节点上的",
        "ScrollViewer。XAML 里靠 x:Name 引用它；声明式树里没有名字可给，",
        "所以这里填同层下标（第 0 个是视差层，第 1 个是滚动区）。",
        "",
        "verticalShift 是「最多错开多少像素」；",
        "verticalSourceOffsetKind 决定这个「多少」是整段滚动全程、还是每滚一屏。",
        "",
        "再往下滚一点，看它怎么被夹住。",
        "再往下滚一点，看它怎么被夹住。",
        "再往下滚一点，看它怎么被夹住。",
        "再往下滚一点，看它怎么被夹住。",
        "到底了。",
    };

    private static readonly string[] Kinds = { "Absolute（整段全程）", "Relative（每滚一屏）" };

    /// <summary>
    /// Header 那行白字压在一层半透明黑上（官方列表背景 <c>#80000000</c> 那一档）：
    /// 不垫这一层，白字在浅色主题下等于没写。
    /// </summary>
    private static readonly Brush HeaderBackdrop =
        new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0));

    public override Element Render()
    {
        var (shift, setShift) = UseState(500.0);
        var (kindIndex, setKindIndex) = UseState(0);

        var kind = kindIndex == 1
            ? MuxControls.ParallaxSourceOffsetKind.Relative
            : MuxControls.ParallaxSourceOffsetKind.Absolute;

        return VStack(12,
            TextBlock("视差视图").Body(),

            Grid(
                    new[] { "*" },
                    new[] { "*" },

                    // 第 0 个：视差层（在后面）
                    ParallaxView(
                            child: Border(
                                    VStack(10,
                                        Block(60, "AccentFillColorDefaultBrush"),
                                        Block(90, "AccentFillColorSecondaryBrush"),
                                        Block(40, "AccentFillColorTertiaryBrush"),
                                        Block(120, "AccentFillColorDisabledBrush")))
                                .Padding(16),
                            sourceIndex: 1,
                            verticalShift: shift,
                            verticalSourceOffsetKind: kind)
                        .Height(320),

                    // 第 1 个：滚动区（在前面，也是视差参照的源头）
                    ScrollViewer(
                            VStack(8,
                                // 官方把这段白字放在 ListView.Header 里，跟着内容一起滚。
                                Border(
                                        TextBlock("滚动这段内容，看后面那层怎么错开")
                                            .MaxWidth(280)
                                            .FontSize(28)
                                            .Foreground(Colors.White)
                                            .Wrap()
                                            .HAlign(HorizontalAlignment.Center))
                                    .Background(HeaderBackdrop)
                                    .Padding(12, 8)
                                    .HAlign(HorizontalAlignment.Center),
                                ForEach(Lines, (line, _) =>
                                    TextBlock(line).Body().Wrap())))
                        .Padding(20, 12)
                        .Height(320))
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            HStack(16,
                VStack(4,
                    TextBlock($"错开量 verticalShift = {shift:0} px").Caption(),
                    Slider(
                        value: shift,
                        min: 0,
                        max: 500,
                        onValueChanged: value => setShift(value)))
                    .Width(240),

                VStack(4,
                    TextBlock("错开量的口径").Caption(),
                    ComboBox(
                        items: Kinds,
                        selectedIndex: kindIndex,
                        onSelectedIndexChanged: setKindIndex)
                        .AutomationName("错开量的口径")))
                .VAlign(VerticalAlignment.Center),

            TextBlock("把 verticalShift 拉到 0 就等于关掉视差 —— 那时候两片内容"
                      + "以同样速度滚，看不出错开。视差层在滚动区后面，所以文字始终可读。")
                .Caption()
                .Wrap());
    }

    private static Element Block(double height, string brushKey) =>
        Border(null)
            .Height(height)
            .Background(ThemeResource.Brush(brushKey));
}
