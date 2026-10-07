using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 内容型 <c>Flyout</c>：浮出层里装的不是"项"，是<b>一整棵子树</b>。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="MenusBasic"/> 里那个 <c>MenuFlyout</c> 的区别只有一条，但这一条
/// 决定了实现方式：<b>菜单装的是项</b>（文本 + 图标 + 回调，没有跨帧状态），
/// <b>浮出层装的是子树</b>——里面可以有一个正在输入、带着光标的
/// <c>TextBox</c>。所以浮出层<b>不会</b>像菜单那样"每次重渲染整体重建"，
/// 而是就地 patch：这个样例里每敲一个字都会重渲染一次，输入框里的光标
/// 与内容都还在（整体重建的话每敲一下都会被抹掉）。
/// </para>
/// <para>
/// <b>没有受控的 <c>IsOpen</c>。</b>官方打开一个浮出层要 <c>ShowAt(目标)</c>，
/// 而目标正是挂它的那个按钮——这一步框架替你做了。元素上没有"目标"这个槽位
/// 可填，也就没有可判定的受控落点，想知道它开了 / 关了用
/// <c>onOpened</c> / <c>onClosed</c>。
/// </para>
/// </remarks>
public sealed class FlyoutBasic : Component
{
    public override Element Render()
    {
        var (draft, setDraft) = UseState(string.Empty);
        var (kept, setKept) = UseState(string.Empty);
        var (log, setLog) = UseState("（还没开过）");

        return VStack(14,
            TextBlock("内容型浮出层").Body(),

            HStack(12,
                DropDownButton("填写备注…",
                    Flyout(
                        VStack(10,
                            TextBlock("这里是一棵子树：可以排版、可以放输入框。").Caption().Subtle().Wrap(),
                            TextBox(draft, setDraft, placeholderText: "写点什么…")
                                .Width(220),
                            Button("记下来", () => setKept(draft)))
                            .Padding(12),
                        placement: FlyoutPlacementMode.Bottom,
                        onOpened: () => setLog("已打开"),
                        onClosed: () => setLog("已收起"))),

                Button("清掉草稿", () => setDraft(string.Empty))),

            TextBlock($"浮出层：{log}").Caption().Subtle(),
            TextBlock($"草稿（每次输入都在，说明它是被 patch 的、不是被重建的）：{draft}")
                .Caption()
                .Subtle()
                .Wrap(),
            TextBlock($"已记下：{kept}").Caption().Subtle(),

            Border(
                    TextBlock("在这块区域上点右键 —— 出来的也是一个内容型浮出层").Body())
                .Padding(16)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"))
                .ContextMenu(
                    Flyout(
                        VStack(8,
                            TextBlock("右键出来的内容").Body(),
                            TextBlock("同一个 Flyout 元素，两种挂法：按钮的 Flyout 槽位，"
                                      + "或者 ContextMenu 修饰器。")
                                .Caption()
                                .Subtle()
                                .Wrap())
                            .Padding(12),
                        placement: FlyoutPlacementMode.RightEdgeAlignedTop)),

            TextBlock("placement 决定它往哪个方向弹（Bottom / Right / Top 等，官方一共十来种）；"
                      + "showMode 里那个 Standard（要显式关）本库不演示——关掉它的 Hide() 是命令式方法，"
                      + "不在声明式 API 里，演示它等于做一个关不上的浮出层。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
