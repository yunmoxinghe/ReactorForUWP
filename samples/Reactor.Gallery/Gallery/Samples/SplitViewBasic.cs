using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>SplitView</c>：一个侧边面板 + 一片主内容区，是 UWP 原生控件。
/// </summary>
/// <remarks>
/// <b>四种 <c>DisplayMode</c> 的差别，只有切一遍才看得明白：</b>
/// <list type="bullet">
///   <item><c>Overlay</c>：展开时<b>盖在</b>内容上，内容宽度不变。</item>
///   <item><c>CompactOverlay</c>：收起时留一条窄边（<c>CompactPaneLength</c>），展开时盖上去。</item>
///   <item><c>Inline</c>：展开时<b>挤开</b>内容，内容变窄。</item>
///   <item><c>CompactInline</c>：收起留窄边，展开时挤开内容。</item>
/// </list>
/// 前两个"盖"、后两个"挤"，自己拼布局很难同时做对这两件事——这是套真控件的理由。
/// <para>
/// <b><c>IsPaneOpen</c> 是非受控的。</b>元素上没有对应回调，官方那四个开合事件
/// （<c>PaneOpening</c> / <c>PaneClosing</c> / <c>PaneOpened</c> / <c>PaneClosed</c>）
/// 一个都没订阅。所以这里靠自己的按钮改 state 来开合：用户用轻扫把面板关掉之后，
/// state 里那个 <c>true</c> 不会变，再点一次"收起面板"不会重开（值没变，写不下去）。
/// 要跟着用户走请改用 <c>NavigationView</c>——它自带开合按钮与受控选中。
/// </para>
/// </remarks>
public sealed class SplitViewBasic : Component
{
    private static readonly string[] Modes = { "Overlay", "CompactOverlay", "Inline", "CompactInline" };

    private static SplitViewDisplayMode ModeOf(int index) => index switch
    {
        1 => SplitViewDisplayMode.CompactOverlay,
        2 => SplitViewDisplayMode.Inline,
        3 => SplitViewDisplayMode.CompactInline,
        _ => SplitViewDisplayMode.Overlay,
    };

    public override Element Render()
    {
        var (open, setOpen) = UseState(true);
        var (mode, setMode) = UseState(1);

        return VStack(12,
            HStack(8,
                Button(open ? "收起面板" : "展开面板", () => setOpen(!open)),
                TextBlock("形态").Caption().Subtle().VAlign(VerticalAlignment.Center)),

            ComboBox(
                items: Modes,
                selectedIndex: Optional<int>.Of(mode),
                onSelectedIndexChanged: setMode)
                .AutomationName("形态"),

            SplitView(
                    // 标题那两个 Margin 照官方：面板头 60,12,0,0（左边 60 是留给
                    // 汉堡按钮那条 48px 窄边的），内容头 12,12,0,0。
                    pane: VStack(8,
                        TextBlock("面板内容").Body().Margin(60, 12, 0, 0),
                        Button("首页", () => { }).Margin(12, 0, 0, 0),
                        Button("浏览", () => { }).Margin(12, 0, 0, 0),
                        Button("设置", () => { }).Margin(12, 0, 0, 0)),
                    content: VStack(8,
                        TextBlock("主内容区").Body(),
                        TextBlock($"当前形态：{Modes[mode]}；面板：{(open ? "展开" : "收起")}。")
                            .Caption().Subtle().Wrap(),
                        TextBlock("切到 Inline / CompactInline 再展开面板，能看见这一块被挤窄；" +
                                  "切到 Overlay / CompactOverlay，能看见面板盖在这一块上面。")
                            .Caption().Subtle().Wrap())
                        .Margin(12, 12, 0, 0),
                    isPaneOpen: open,
                    displayMode: ModeOf(mode),
                    openPaneLength: 320,
                    compactPaneLength: 48)
                .Height(300)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            TextBlock("面板与主内容是两个独立的槽位（Pane / Content），不是「面板是第一个子元素」。")
                .Caption().Subtle().Wrap());
    }
}
