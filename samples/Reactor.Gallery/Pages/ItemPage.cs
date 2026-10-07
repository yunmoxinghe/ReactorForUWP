using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

/// <summary>详情页 props：看哪一项、怎么退回。</summary>
public sealed record ItemPageProps(string ItemId, Action Back);

/// <summary>
/// 某一个控件的详情页：<b>上面是其全部样例的预览，下面挨着各自的源码</b>。
/// </summary>
/// <remarks>
/// 这个布局对应 WinUI 3 Gallery 的 ItemPage：<c>sample + code</c> 成对出现。
/// 之所以要成对而不是"样例都在上面、源码统一放最后"，是因为人在找的是
/// "这段效果是怎么写出来的"——两者贴在一起时不需要来回跳。
/// <para>
/// 有些样例是整页形态（<c>写法指南</c>分类）：它们自带滚动容器，
/// 于是预览区给一个固定高度让它内部滚。这些页面不像小型样例那样可以塞进
/// 一张卡片里，但它们的价值恰恰是"完整的页面长什么样"。
/// </para>
/// </remarks>
public sealed class ItemPage : Component<ItemPageProps>
{
    public override Element Render()
    {
        var item = SampleIndex.FindItem(Props.ItemId);

        if (item is null)
        {
            return VStack(12,
                Button("返回", Props.Back),
                TextBlock($"找不到示例：{Props.ItemId}").Wrap());
        }

        var category = SampleIndex.FindCategoryOf(item);

        // 官方对应那张卡是<b>可选的</b>：没登记过就不显示，而不是显示一张空卡。
        var api = ApiMap.Find(item.Id);

        return ScrollViewer(
            VStack(18,
                BreadcrumbBar(
                    new[] { category?.Title ?? "全部示例", item.Title },
                    onItemClicked: index => { if (index == 0) { Props.Back(); } }),

                VStack(4,
                    TextBlock(item.Title).TitleLarge(),
                    When(item.Description is not null,
                        () => TextBlock(item.Description!).Wrap().Subtitle())),

                When(api is not null, () => ApiCard(api!)),

                ForEach(item.Samples,
                    sample => Component<SampleSection, SampleSectionProps>(new SampleSectionProps(sample))))
                .Padding(24, 20, 24, 32));
    }

    /// <summary>
    /// 「官方对应」卡：这个样例背后那个控件在官方叫什么、文档在哪。
    /// </summary>
    /// <remarks>
    /// 对应 WinUI 3 Gallery 详情页顶部的文档入口。它回答的是读者看完样例之后
    /// 紧接着会问的那句"这个控件官方还提供什么"——把答案放在页面顶部而不是
    /// 塞在源码里，是因为源码讲的是"我们这边怎么写"，而这里讲的是"官方那边
    /// 是什么"，两者不是同一件事。
    /// <para>
    /// 类型名用<b>等宽</b>：全限定名带点号，比例字体下 <c>l</c> 与 <c>I</c>、
    /// <c>rn</c> 与 <c>m</c> 容易看错，而且这一段是要被选中复制去搜的。
    /// </para>
    /// </remarks>
    private static Element ApiCard(ApiReference reference)
    {
        // 第一个是"主角"（见 ApiReference.Apis 的约定），加粗以区分配套的那一批。
        var blocks = reference.Apis
            .Select((name, index) => Paragraph(Run(name, bold: index == 0)))
            .ToArray();

        var isWinUi = reference.Apis[0].StartsWith("Microsoft.UI", StringComparison.Ordinal);

        return Border(
                VStack(6,
                    TextBlock("官方对应").BodyStrong(),

                    CodeBlock(blocks),

                    When(isWinUi,
                        () => TextBlock(
                                "文档页指向 WinUI 3 的同名类型：WinUI 2 的文档已并入 Windows App SDK 文档站，"
                                + "UWP 侧用的是同名、API 形状基本一致的那一个。")
                            .Caption().Subtle().Wrap()),

                    HyperlinkButton(
                        HStack(6,
                            FontIcon("\uE71B", fontSize: 14).VAlign(VerticalAlignment.Center),
                            TextBlock("在 learn.microsoft.com 上查看").VAlign(VerticalAlignment.Center)),
                        navigateUri: reference.Docs)
                        .AutomationName($"官方文档：{reference.Apis[0]}")
                        .Padding(0)))
            .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"), 1)
            .Background(ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"))
            .Padding(14, 12, 14, 12);
    }
}

/// <summary>单个样例的 props。internal：<see cref="SampleCase"/> 只在画廊内部流通。</summary>
internal sealed record SampleSectionProps(SampleCase Sample);

/// <summary>
/// <b>一个样例</b>：标题行（右侧一个预览主题按钮）+ 预览区 + 可折叠的源码区。
/// </summary>
/// <remarks>
/// <b>为什么要单独成一个组件，而不是 ItemPage 里的一个私有方法。</b>
/// "预览现在切成哪一色"是<b>这个样例自己</b>的状态。写在 ItemPage 里就得为
/// 每个样例备一份 state，而样例数由索引决定、逐条不同——hook 数目却必须
/// 每帧一致。拆成组件之后每个实例各自持一份，条目加多少个都不影响别人。
/// 这也是 <see cref="SampleIndex"/> 里那句"每个样例嵌一个组件"的同一个理由。
/// <para>
/// <b>预览主题为什么可以单独切。</b><c>RequestedTheme</c> 只沿树向下继承，
/// 设在预览区那一个 <c>Border</c> 上，子树里的控件就按那一档去解析主题资源，
/// 而外壳、导航条、页面背景仍是应用主题。一个亚克力/半透明控件在深浅两色下的
/// 观感差别，只有这样才能并排看——整个应用换主题会连外壳一起变，反而看不出
/// 差别在哪。
/// </para>
/// </remarks>
internal sealed class SampleSection : Component<SampleSectionProps>
{
    private static readonly string[] ThemeNames = { "跟随应用", "浅色", "深色" };

    /// <summary>亮度 / 太阳 / 月亮——按钮上显示的就是"当前这一档"。</summary>
    private static readonly string[] ThemeGlyphs = { "\uE793", "\uE706", "\uE708" };

    /// <summary>
    /// 预览区的底色。<b>必须显式给</b>：预览区切成深色时里面的文字会变白，
    /// 而背景如果还是跟着应用主题走的那一块浅底，就成了白底白字。
    /// </summary>
    /// <remarks>
    /// 前两档用实色（不随应用主题变，否则"切成浅色"这个动作没有意义）；
    /// "跟随应用"那一档用 <see cref="ThemeResource"/> 的<b>活引用</b>画笔，
    /// 于是应用换主题时预览照旧跟着换。
    /// </remarks>
    private static readonly Brush LightPreview = new SolidColorBrush(Colors.White);
    private static readonly Brush DarkPreview = new SolidColorBrush(Color.FromArgb(255, 0x20, 0x20, 0x20));

    public override Element Render()
    {
        var sample = Props.Sample;
        var (mode, setMode) = UseState(0);
        var next = (mode + 1) % ThemeNames.Length;

        return VStack(8,
            Grid(
                new[] { "*", "Auto" },
                new[] { "Auto" },
                VStack(2,
                    TextBlock(sample.Title).Title(),
                    When(sample.Description is not null,
                        () => TextBlock(sample.Description!).Wrap().Caption().Subtle()))
                    .Grid(column: 0),

                Button(FontIcon(ThemeGlyphs[mode]), () => setMode(next))
                    .AutomationName($"预览主题：{ThemeNames[mode]}（切到{ThemeNames[next]}）")
                    .ToolTip($"预览主题：{ThemeNames[mode]}，点一下切到{ThemeNames[next]}")
                    .Padding(8, 5, 8, 5)
                    .VAlign(VerticalAlignment.Top)
                    .Grid(column: 1)),

            // 预览区：给一张带边框的底，让"这是真在运行的控件"这件事看得出来。
            Border(
                    ScrollViewer(sample.Build())
                        .Padding(16)
                        .MinHeight(120))
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"), 1)
                .Background(PreviewBackgroundOf(mode))
                .Theme(ToElementTheme(mode)),

            // 源码用 Expander 装：<b>折叠状态由控件自己持有</b>（IsExpanded 在这一版
            // 是 defaultValue 语义，不是受控属性），所以这里不需要为它备 state——
            // 想看就展开，翻到下一个样例时上一个仍保持你留下的样子。
            Expander(
                header: "源码",
                content: Component<SampleCodePresenter, CodePresenterProps>(
                    new CodePresenterProps(sample.Title, sample.SourcePath)),
                isExpanded: true));
    }

    private static Brush PreviewBackgroundOf(int mode) => mode switch
    {
        1 => LightPreview,
        2 => DarkPreview,
        _ => ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"),
    };

    /// <summary>0 跟随应用 / 1 浅色 / 2 深色。</summary>
    private static ElementTheme ToElementTheme(int mode) => mode switch
    {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
