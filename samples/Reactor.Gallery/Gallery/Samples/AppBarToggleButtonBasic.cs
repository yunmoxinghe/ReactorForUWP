using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>AppBarToggleButton</c>：命令条上"按下去就保持"的那个按钮。
/// </summary>
/// <remarks>
/// <para>
/// <b>它是真的 <c>ToggleButton</c> 子类。</b>官方 <c>AppBarToggleButton</c> 继承
/// UWP 的 <c>ToggleButton</c>（不像 <c>ToggleSplitButton</c> 那样是 WinUI 2 另起的
/// 炉灶），所以回执就是 <c>Checked</c> / <c>Unchecked</c>，受控写法与
/// <c>ToggleButton</c> 一致。
/// </para>
/// <para>
/// <b>受控写法在这里长什么样。</b>命令项随命令组<b>整体重建</b>（见
/// <c>CommandBarHandler.ApplyCommands</c>），每轮拿到的都是新建的原生按钮，
/// 所以"写受控值"与"写初始值"是同一件事——但<b>写下去仍会同步抛事件</b>，
/// 新实例也不认识这一发是自己写的，所以订阅要先挂、受控值后写，中间靠
/// <c>EchoGuard</c> 认回声。少了那一层，下面"加粗 / 斜体"会在每次重渲染时
/// 自己回调一次（表现为计数乱跳）。
/// </para>
/// <para>
/// <b>试着点一下"加粗"再点别的按钮。</b>计数只在<b>用户</b>点击时 +1；
/// 由 state 下发引起的那一次不会算进来。
/// </para>
/// </remarks>
public sealed class AppBarToggleButtonBasic : Component
{
    public override Element Render()
    {
        var (bold, setBold) = UseState(false);
        var (italic, setItalic) = UseState(false);
        var (sigma, setSigma) = UseState(false);
        var (picks, setPicks) = UseState(0);

        var preview = TextBlock(
                $"加粗：{(bold ? "开" : "关")}　　斜体：{(italic ? "开" : "关")}　　"
                + $"求和：{(sigma ? "开" : "关")}")
            .Body();

        if (bold)
        {
            preview = preview.Bold();
        }

        return VStack(12,
            TextBlock("命令条上的开关按钮").Body(),

            CommandBar(
                    content: Border(
                            VStack(6,
                                preview,
                                TextBlock($"用户点出来的次数：{picks}").Caption()))
                        .Padding(16)
                        .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),
                    primary: new Element?[]
                    {
                        AppBarToggleButton("加粗", "\uE8DD", bold, value =>
                        {
                            setBold(value);
                            setPicks(picks + 1);
                        }),
                        AppBarToggleButton("斜体", "\uE8DB", italic, value =>
                        {
                            setItalic(value);
                            setPicks(picks + 1);
                        }),
                        // 图标也可以指定字体：官方那一档是 Candara 的 Σ。
                        AppBarToggleButton("求和", FontIcon("Σ", fontFamily: "Candara"), sigma,
                            value =>
                            {
                                setSigma(value);
                                setPicks(picks + 1);
                            }),
                        AppBarSeparator(),
                        AppBarButton("右对齐", SymbolIcon(Symbol.AlignRight),
                            () => setPicks(picks + 1)),
                    },
                    secondary: new Element?[]
                    {
                        // 不给回调 = 不受控：按得下去，但 state 不知情，
                        // 下一次重渲染会把它按回元素上写死的那个值。
                        AppBarToggleButton("只读模式", "\uE7B3", true),
                    }),

            TextBlock("「只读模式」在次命令区里，且没有给回调 —— 它仍然按得下去，"
                      + "但 state 不知道，下一次重渲染就会把它按回元素上写死的那个值。"
                      + "「右对齐」演示的是 SymbolIcon 也能进图标槽，"
                      + "「求和」演示的是 FontIcon 连字体一起指定（官方那一档的 Candara Σ）。")
                .Caption()
                .Wrap());
    }
}
