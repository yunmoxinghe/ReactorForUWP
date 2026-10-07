using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>ToolTip</c>：悬停 / 聚焦时冒出来的那行说明。
/// </summary>
/// <remarks>
/// <para>
/// <b>它不是控件树上的一个节点，是挂在别人身上的一个槽位。</b>写法是
/// <c>Button("保存").ToolTip(...)</c>：被修饰的元素才是宿主，气泡落在官方的附加
/// 属性 <c>ToolTipService.ToolTip</c> 上——与右键浮出层
/// （<c>.ContextMenu(...)</c>）同形，都不进可视树。
/// </para>
/// <para>
/// <b>两条入口对应官方 XAML 的两种写法</b>，不是同一个旋钮的两个名字：
/// <list type="bullet">
///   <item><c>.ToolTip("文本")</c> —— 特性语法 <c>ToolTipService.ToolTip="…"</c>，
///         只能给字符串，没有子树。</item>
///   <item><c>.ToolTip(元素)</c> —— 属性元素语法
///         <c>&lt;ToolTipService.ToolTip&gt;…&lt;/ToolTipService.ToolTip&gt;</c>，
///         能给任意内容（官方画廊里"图文混排的提示"就是这一档）。</item>
/// </list>
/// </para>
/// <para>
/// <b>没有 <c>IsOpen</c>。</b>气泡的开合由指针与焦点驱动；声明式写一个 <c>true</c>
/// 之后它会自己收起、下一轮又被写回成 <c>true</c>，于是每帧都在"拉开—收起"。
/// 这与 <c>TeachingTip</c>（程序控制为主，做成受控）不同，是刻意不包。
/// </para>
/// </remarks>
public sealed class ToolTipBasic : Component
{
    private const string Source = "ms-appx:///Assets/Square150x150Logo.scale-100.png";

    /// <summary>
    /// <c>PlacementMode</c> 有五档；<c>Mouse</c> 是官方默认值（跟着指针、在指针上方居中），
    /// 其余四档钉在宿主的<b>那一侧</b>。
    /// </summary>
    private static readonly string[] ModeNames = { "Mouse", "Top", "Bottom", "Left", "Right" };

    private static readonly PlacementMode[] Modes =
    {
        PlacementMode.Mouse,
        PlacementMode.Top,
        PlacementMode.Bottom,
        PlacementMode.Left,
        PlacementMode.Right,
    };

    public override Element Render()
    {
        var (mode, setMode) = UseState(0);
        var (trace, setTrace) = UseState("（还没弹出过）");

        return VStack(16,
            TextBlock("纯文本（鼠标悬停 / 键盘聚焦都会弹）").Caption().Subtle(),
            HStack(12,
                Button("按钮").ToolTip("点它什么也不会发生"),
                Border(TextBlock("这段不是按钮，是文本——文本也能挂提示").Wrap())
                    .ToolTip("提示不只给按钮用")),

            TextBlock("图文混排（内容是一棵子树）").Caption().Subtle(),
            HStack(12,
                Button("直接给一段内容")
                    .ToolTip(HStack(8,
                        Image(Source, 24, 24),
                        TextBlock("这一档对应官方画廊里的「图文混排的提示」").Wrap())),

                Button("用 Tip() 造，顺带定方位")
                    .ToolTip(Tip(
                        content: HStack(8,
                            FontIcon("\uE8A5"),
                            TextBlock("给了 Tip() 就能同时定 Placement 与回执").Wrap()),
                        placement: PlacementMode.Bottom,
                        onOpened: () => setTrace("图文那条弹出来了"),
                        onClosed: () => setTrace("图文那条收起了")))),

            TextBlock("方位（Placement）").Caption().Subtle(),
            RadioButtons(ModeNames, mode, setMode),
            Button($"弹在 {ModeNames[mode]}")
                .ToolTip(Tip($"这个气泡被钉在「{ModeNames[mode]}」", placement: Modes[mode])),

            TextBlock("偏移与快捷键").Caption().Subtle(),
            HStack(12,
                Button("离远一点")
                    .ToolTip(Tip("往下挪 28px", placement: PlacementMode.Bottom, verticalOffset: 28)),

                // 快捷键会不会被系统并进提示串，由系统决定（WinUI 3 会拼成
                // 「保存 (Ctrl+S)」）；这里把两种按钮并排摆出来，让你自己对照，
                // 而不是在代码里替它下断言。
                Button("带 Ctrl+S 的按钮")
                    .CtrlShortcut(VirtualKey.S)
                    .ToolTip("保存")),

            TextBlock($"回执：{trace}").Caption().Subtle().Wrap(),

            TextBlock("提示里读到的不是气泡自己的状态，而是「谁弹了、谁收了」——"
                    + "气泡的开合由指针与焦点驱动，不是受控属性。")
                .Caption().Subtle().Wrap());
    }
}
