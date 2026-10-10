using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 图标：<c>FontIcon</c> / <c>BitmapIcon</c> / <c>ImageIcon</c>。
/// </summary>
/// <remarks>
/// 三者都是 <c>IconElement</c>（继承自 <c>FrameworkElement</c>），所以既能当菜单项图标，
/// 也能当普通内容。差别在"形从哪儿来"：
/// <list type="bullet">
///   <item><c>FontIcon</c>：一个<b>字形</b>（Segoe MDL2 Assets 字体里的码位），
///         跟随字号与前景色，切主题时自动换色。绝大多数界面图标用它。</item>
///   <item><c>BitmapIcon</c>（UWP 原生）：一张<b>位图</b>。<c>ShowAsMonochrome</c> 打开时
///         官方按"前景色"那一个通道去着色（类似蒙版），关闭时画原图颜色。</item>
///   <item><c>ImageIcon</c>（WinUI 2）：内部是一个 <c>Image</c>，
///         <b>按宿主给的尺寸缩放</b>、画原图颜色，不做单色化——
///         给"本来就是彩色的图"（应用图标、头像）用。</item>
/// </list>
/// <para>
/// <b><c>BitmapIcon</c> 不要设 <c>Width</c> / <c>Height</c>。</b>它不像 <c>Image</c>
/// 那样把位图<b>缩放</b>适配到给定尺寸，而是原图 1:1 画、超出就<b>裁</b>——
/// 给 48px 的图设 20×20，看到的是被裁秃的一小块，症状很像"图标缩没了"。
/// 缩放是宿主的事（<c>SettingsCard</c> 的 HeaderIcon 展示器等）。
/// </para>
/// <para>
/// 要"同一张图按尺寸缩放"就换 <c>ImageIcon</c>——这是它与 <c>BitmapIcon</c>
/// 最实用的一条区别：前者给多大就缩到多大，后者给多大就裁多大。
/// </para>
/// </remarks>
public sealed class IconsBasic : Component
{
    // 官方用 Slices.png（透明小图标）；本地由 tools/parity/make_sample_media.py 生成。
    // 别拿应用 logo 当"随便一个图标"——那是应用的身份标识，示例语义会跑偏。
    private const string Source = "ms-appx:///Assets/SampleMedia/slices.png";

    /// <summary>
    /// 图标字体。<b>显式给出来</b>：官方 <c>FontIcon</c> 的 <c>FontFamily</c>
    /// 默认是 <c>Segoe MDL2 Assets</c>，但那只是默认值——换了字体或换台机器，
    /// 同一个码位画出来的就是另一个字形。写清它是"这几个码位的含义"的一部分。
    /// </summary>
    private const string IconFont = "Segoe MDL2 Assets";

    public override Element Render() =>
        VStack(16,
            TextBlock("FontIcon：字形 + 字号").Body(),

            // 用 GridView 而不是自己拼网格：横向铺开 + 换行、键盘方向键在项间移动、
            // UIA 上的 item 结构，全由官方那套给出。
            GridView(ForEach(Glyphs, glyph =>
                VStack(4,
                    FontIcon(glyph.Code, fontFamily: IconFont, fontSize: 24),
                    TextBlock(glyph.Name).Caption().Subtle(),
                    TextBlock(glyph.Code).Caption().Subtle())
                    .Padding(12)
                    .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")))),

            TextBlock("字号不同，同一个码位就是不同大小的图标——不需要多套图。")
                .Caption()
                .Subtle(),

            // 官方 IconElement 那一页把 SymbolIcon 排在最前：它是"具名"的那一档，
            // Symbol 枚举就是 Segoe MDL2 Assets 里那批常用码位的一个名字清单。
            TextBlock("SymbolIcon：用官方 Symbol 枚举，不写码位").Body(),

            // 与官方那一页同形：IconElement 是"图标"不是"控件"，
            // 要点得动、Tab 得到，就得套一层宿主（这里用 Button）。
            HStack(20,
                VStack(4,
                    Button(SymbolIcon(Windows.UI.Xaml.Controls.Symbol.Home)),
                    TextBlock("Symbol.Home").Caption().Subtle()),
                VStack(4,
                    Button(SymbolIcon(Windows.UI.Xaml.Controls.Symbol.Find)),
                    TextBlock("Symbol.Find").Caption().Subtle()),
                VStack(4,
                    Button(SymbolIcon(Windows.UI.Xaml.Controls.Symbol.Setting)),
                    TextBlock("Symbol.Setting").Caption().Subtle()),
                VStack(4,
                    Button(SymbolIcon(Windows.UI.Xaml.Controls.Symbol.Copy)),
                    TextBlock("Symbol.Copy").Caption().Subtle()),
                VStack(4,
                    Button(SymbolIcon(Windows.UI.Xaml.Controls.Symbol.Refresh)),
                    TextBlock("Symbol.Refresh").Caption().Subtle())),

            TextBlock("SymbolIcon 与 FontIcon 是<b>同一件事的两种写法</b>："
                      + "官方 Symbol 枚举就是「Segoe MDL2 Assets 里那批常用码位」的具名清单。"
                      + "要一个不在这份清单里的码位，就用 FontIcon 写码位。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("BitmapIcon：位图与着色").Body(),
            HStack(20,
                VStack(4,
                    BitmapIcon(Source, showAsMonochrome: true),
                    TextBlock("ShowAsMonochrome = true").Caption().Subtle()),
                VStack(4,
                    BitmapIcon(Source, showAsMonochrome: false),
                    TextBlock("ShowAsMonochrome = false").Caption().Subtle())),

            // ImageIcon 是 WinUI 2 的另一个控件：内部是一个 Image，
            // 原色 + 按尺寸缩放 —— 与 BitmapIcon 的"1:1 画 + 单色化"是两回事。
            TextBlock("ImageIcon：同一张图，按尺寸缩放、保留原色").Body(),
            HStack(20,
                VStack(4,
                    ImageIcon(Source).Size(24, 24),
                    TextBlock("24 × 24").Caption().Subtle()),
                VStack(4,
                    ImageIcon(Source).Size(48, 48),
                    TextBlock("48 × 48").Caption().Subtle()),
                VStack(4,
                    ImageIcon(Source).Size(72, 72),
                    TextBlock("72 × 72（同一张源图）").Caption().Subtle())),

            TextBlock("同一个码位在菜单里就是 MenuItem 的 icon 参数；" +
                      "给它设 Width / Height 会裁图而不是缩放，别设。")
                .Caption()
                .Subtle()
                .Wrap());

    /// <summary>几个常用码位：挑的是"一眼能认出用途"的那几个。</summary>
    private static readonly Glyph[] Glyphs =
    {
        new("\uE80F", "主页"),
        new("\uE721", "搜索"),
        new("\uE713", "设置"),
        new("\uE8C8", "复制"),
        new("\uE74D", "删除"),
        new("\uE8B7", "刷新"),
        new("\uE8FD", "列表"),
        new("\uE894", "播放"),
        new("\uE7BA", "收藏"),
        new("\uE8A5", "文档"),
    };

    private sealed record Glyph(string Code, string Name);
}
