using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 浮层容器：<c>Popup</c>（<c>Windows.UI.Xaml.Controls.Primitives.Popup</c>）。
/// </summary>
/// <remarks>
/// 官方那页的三件事：<b>按钮开关</b>、<b>轻 dismiss</b>、<b>挂在目标旁边
///（Placement）</b>。
/// <list type="bullet">
///   <item><b>它是容器不是对话框。</b>没有白底、圆角、阴影那一身皮——那些是
///         <c>Flyout</c> / <c>ContentDialog</c> 的事。这里要自己画：内容外面裹一层
///         <c>Border</c>、背景色自己给，看到的才是「一块面板」。</item>
///   <item><b><c>IsOpen</c> 是受控的。</b>本页四个示例全都是给 state 而不是让它
///         自己开关——轻 dismiss 关掉之后那一发要经过官方 <c>Closed</c> 回执、
///         由 <c>EchoGuard</c> 认下来，state 才会跟着变 <c>false</c>。
///         「点了外面，界面却回不来」= 这一环断了，先把 <c>OnIsOpenChanged</c>
///         接上再看别的。</item>
///   <item><b><c>TargetIndex</c> 填的是「同层第几个」而不是名字</b>：声明式树里
///         没有 <c>x:Name</c>，能稳定指向另一个元素的只有它在父容器里的下标。
///         本页第三个示例里 Popup 与按钮同在一个 <c>HStack</c>，下标就是按钮的位置。</item>
///   <item><b><c>DesiredPlacement</c> 是「期望」不是「结果」</b>：挤不下时官方会自己
///         挪地方，而那个<b>实际结果</b>（<c>ActualPlacement</c>，只读）本元素不提供
///         ——两个读数摆在一起就会互相打架。</item>
/// </list>
/// </remarks>
public sealed class PopupBasic : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);
        var (dismissible, setDismissible) = UseState(false);
        var (anchored, setAnchored) = UseState(false);
        var (lastEvent, setLastEvent) = UseState("（还没有）");

        return VStack(20,
            TextBlock("按钮开关（受控 IsOpen）").Body(),

            // HStack 里下标 0 是按钮、下标 1 是 Popup —— targetIndex: 0 就是它前面那个。
            HStack(12,
                Button("打开浮层", () => setOpen(!open)),

                Popup(
                    Border(
                            VStack(8,
                                TextBlock("这块内容盖在最上层").Body().FontSize(16),
                                TextBlock("它没有背景、没有边框：外头这层 Border 是本样例自己裹的")
                                    .Caption()
                                    .Subtle()
                                    .Wrap(),
                                Button("关闭", () => setOpen(false))))
                        .Background(Windows.UI.Colors.WhiteSmoke)
                        .Padding(16)
                        .MinWidth(240),
                    isOpen: open,
                    targetIndex: 0,
                    desiredPlacement: PopupPlacementMode.Bottom,
                    onIsOpenChanged: next => setOpen(next))),

            TextBlock("浮层自己不带背景：<b>没有那层 Border 就只是一块透明区域</b>，"
                      + "看上去像「打开没反应」。先确认内容本身看得见，再谈别的原因。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("轻 dismiss：点外面 / Esc 就关掉").Body(),

            HStack(12,
                Button("打开（点外面关）", () => setDismissible(!dismissible)),

                Popup(
                    Border(
                            TextBlock("点到这块以外的地方我就关了").Body())
                        .Background(Windows.UI.Colors.LemonChiffon)
                        .Padding(16)
                        .Width(280),
                    isOpen: dismissible,
                    isLightDismissEnabled: true,
                    targetIndex: 0,
                    desiredPlacement: PopupPlacementMode.Bottom,
                    onIsOpenChanged: next =>
                    {
                        setLastEvent(next ? "Opened" : "Closed");
                        setDismissible(next);
                    })),

            TextBlock($"最后一次回执：{lastEvent}")
                .Caption()
                .Subtle(),

            TextBlock("<b><c>IsLightDismissEnabled</c> 官方默认是关的</b>（<c>Flyout</c> 那一边"
                      + "默认开）——不写这一句，点外面不会关。"
                      + "<b>回执只有一条通道</b>：官方的 <c>Opened</c> 与 <c>Closed</c> 说的是"
                      + "同一件事的两半，这里合成一个 <c>onIsOpenChanged</c>，参数就是新的值。"
                      + "不接它的话 light dismiss 之后 state 永远是 <c>true</c>，"
                      + "第二次就点不开了（下发 <c>true</c> 时控件已经是 <c>true</c>，值没变 → 不写）。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("挂在目标旁边（TargetIndex + DesiredPlacement）").Body(),

            HStack(24,
                Button("挂在下边", () => setAnchored(!anchored)),

                Popup(
                    Border(
                            VStack(6,
                                TextBlock("Bottom").Body(),
                                TextBlock("offsets 在这儿是相对目标的").Caption().Subtle()))
                        .Background(Windows.UI.Colors.AliceBlue)
                        .Padding(12),
                    isOpen: anchored,
                    isLightDismissEnabled: true,
                    // 目标 = 同层第 0 个（前面那个按钮）。
                    targetIndex: 0,
                    desiredPlacement: PopupPlacementMode.Bottom,
                    horizontalOffset: 12,
                    verticalOffset: 4,
                    onIsOpenChanged: next => setAnchored(next))),

            TextBlock("不给 targetIndex 时坐标参考<b>窗口左上角</b>（而不是最近那个按钮），"
                      + "这时 <c>DesiredPlacement</c> 会自动退化成 <c>Auto</c> 那一档。"
                      + "要「跟着按钮走」必须先给目标。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("<b>ShouldConstrainToRootBounds</b> 没在本页演示：它决定浮层能不能"
                      + "浮出窗口之外，而画廊窗口通常占满了可用区域——开和关看不出差别。"
                      + "需要它时给 <c>shouldConstrainToRootBounds: false</c>，"
                      + "默认按官方的 <c>true</c>，也就是裹在窗口内。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
