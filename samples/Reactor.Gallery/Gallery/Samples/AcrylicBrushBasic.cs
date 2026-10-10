using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 亚克力：<c>AcrylicBrush</c>（<c>Windows.UI.Xaml.Media.AcrylicBrush</c>）。
/// </summary>
/// <remarks>
/// 官方那页的三件事：<b>应用内亚克力</b>、<b>不同的着色浓度</b>、
/// <b>fallback 那一层纯色</b>。
/// <list type="bullet">
///   <item><b>它是刷子，不是元素</b>：收它的地方是 <c>Background</c> 那一类属性
///         （本库 <c>AcrylicBrush(...)</c> 造出来的是真的
///         <c>Windows.UI.Xaml.Media.AcrylicBrush</c>，不是"像亚克力的纯色"）。</item>
///   <item><b>这一块是"应用内亚克力"</b>（<c>BackgroundSource = Backdrop</c>，官方默认）：
///         它模糊的是<b>窗口内、它后面那层 XAML 内容</b>。想透出桌面
///         （<c>HostBackdrop</c>）是宿主窗口的事，不是某个属性上的刷子能决定的，
///         所以工厂方法<b>不收</b> <c>BackgroundSource</c> 参数（与官方一致）。</item>
///   <item><b>要它真的生效，后面必须有东西</b>：亚克力取"背后那层"做模糊，
///         后面什么都没画时看到的就是 <c>fallbackColor</c> 那一层纯色
///         ——不是 bug，是它唯一的素材没了。这正是本页底下那几块彩色形状要铺在那里的原因。</item>
///   <item><c>fallbackColor</c> 与 <c>tintLuminosityOpacity</c> <b>给了才写</b>：
///         两个都是"不设就用控件自己的默认"，写死一个值进去等于替官方做默认值决策。</item>
/// </list>
/// </remarks>
public sealed class AcrylicBrushBasic : Component
{
    // ── 刷子按「引用」比：每轮 new 一支就是一次真的重绘 ────────────
    // 本页面所有刷子都放在静态字段里，写内联会变成"每帧重画一遍"。

    private static readonly Brush Light20 = AcrylicBrush(Colors.White, 0.2, fallbackColor: Colors.LightGray);
    private static readonly Brush Light50 = AcrylicBrush(Colors.White, 0.5, fallbackColor: Colors.LightGray);
    private static readonly Brush Light80 = AcrylicBrush(Colors.White, 0.8, fallbackColor: Colors.LightGray);
    private static readonly Brush SteelBlue = AcrylicBrush(Colors.SteelBlue, 0.6, fallbackColor: Colors.SteelBlue);
    private static readonly Brush Orchid = AcrylicBrush(Colors.DarkOrchid, 0.6, fallbackColor: Colors.DarkOrchid);

    // ── 底图那三块：形状 + 纯色 ────────────────────────────────
    // 三块互相错开，是为了让亚克力糊出来的结果「看得出位移」：
    // 拿一整片纯色当底，糊出来还是一整片纯色，等于没演示。
    private static readonly SolidColorBrush Aqua = new(Colors.Aqua);
    private static readonly SolidColorBrush Magenta = new(Colors.Magenta);
    private static readonly SolidColorBrush Yellow = new(Colors.Yellow);

    public override Element Render() =>
        VStack(20,
            TextBlock("铺一层有色内容做底（亚克力模糊的就是它）").Body(),

            // 亚克力糊的是"它后面那层"。官方那套底图就是这份素材：
            // 左上 100×200 的 Aqua 长条、正中 152×152 的 Magenta 正圆、右下 80×100 的 Yellow 块。
            // 三块互相错开，糊出来的结果才看得出位移；外层 200 高、最窄 320。
            Grid(
                new[] { GridSize.Star() },
                new[] { GridSize.Px(200) },
                Rectangle()
                    .Fill(Aqua)
                    .Size(100, 200)
                    .HAlign(HorizontalAlignment.Left)
                    .VAlign(VerticalAlignment.Top),
                Ellipse()
                    .Fill(Magenta)
                    .Size(152, 152)
                    .Center(),
                Rectangle()
                    .Fill(Yellow)
                    .Size(80, 100)
                    .HAlign(HorizontalAlignment.Right)
                    .VAlign(VerticalAlignment.Bottom))
                .MinWidth(320),

            TextBlock("三档着色浓度（tintOpacity 0.2 / 0.5 / 0.8）").Body(),

            HStack(12,
                Panel("0.2", Light20),
                Panel("0.5", Light50),
                Panel("0.8", Light80)),

            TextBlock("带颜色的 tint（蓝色，浓度 0.6）").Body(),

            HStack(12,
                Panel("蓝色 tint", SteelBlue),
                Panel("洋红 tint", Orchid)),

            TextBlock("tintOpacity 说的是「着色层有多不透明」：越接近 1，底下那层东西"
                      + "越看不清、越像一块实心的有色板；越接近 0，越像一层薄雾。"
                      + "它<b>不是</b>模糊半径 —— 模糊强度由官方按这一档材质定，本库不另给旋钮。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("fallbackColor 是「后面没东西可糊时」看到的那一层纯色："
                      + "把上面那三块彩色形状去掉，这几块就会显示成各自的 fallbackColor —— "
                      + "看到纯色时先查后面有没有内容，别急着改刷子。")
                .Caption()
                .Subtle()
                .Wrap());

    private static Element Panel(string label, Brush brush) =>
        Border(
                TextBlock(label).Body())
            .Background(brush)
            .Padding(16, 24)
            .Width(160)
            .VAlign(VerticalAlignment.Center)
            .HAlign(HorizontalAlignment.Center);
}
