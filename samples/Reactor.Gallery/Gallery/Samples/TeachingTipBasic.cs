using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using MuxControls = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>TeachingTip</c>：挂在某个控件旁边的一段说明，<b>受控</b>的 <c>IsOpen</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b><c>targetIndex</c> 是"同层第几个"，不是"哪个元素"。</b>XAML 里写
/// <c>Target="{x:Bind 那个按钮}"</c> 靠的是 <c>x:Name</c>；声明式树里的元素没有
/// 名字，能指向它的只有它在父容器里的位置。本例那个按钮是 <c>VStack</c> 的第 1 个
/// 子元素（第 0 个是上面那行说明），所以填 <c>1</c>。
/// </para>
/// <para>
/// <b>受控在这里看得最清楚的一点：可以拒绝关闭。</b>把「锁住展开」打开之后，
/// 点提示上的关闭按钮 / 点空白处，<c>OnIsOpenChanged</c> 收到的 <c>false</c>
/// 会被丢掉——state 还是 <c>true</c>，于是下一轮又把它写回成展开。
/// 这正是"受控"与"给了个初始值"的区别。
/// </para>
/// </remarks>
public sealed class TeachingTipBasic : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);
        var (locked, setLocked) = UseState(false);
        var (trace, setTrace) = UseState("（还没动过）");

        return VStack(12,
            TextBlock("下面这个按钮是目标；提示挂在它旁边，尾巴指着它。")
                .Caption().Subtle().Wrap(),

            // ↓ 同层下标 1 —— 提示的 Target 指向它
            Button(open ? "收起提示" : "显示提示", () => setOpen(!open)),

            TeachingTip(
                    title: "这一步是干什么的",
                    subtitle: "教学提示（受控 IsOpen）",
                    child: TextBlock("正文是任意元素树——它是 ContentControl。")
                        .Wrap(),
                    isOpen: open,
                    targetIndex: 1,
                    preferredPlacement: MuxControls.TeachingTipPlacementMode.Bottom,
                    actionButtonText: "知道了",
                    closeButtonText: "关闭",
                    onIsOpenChanged: value =>
                    {
                        setTrace(value ? "被打开了" : "被关掉了（可能是轻 dismiss）");

                        // 锁住时把"关掉"这一个回执丢掉：state 不变，下一轮写回成展开。
                        if (!locked)
                        {
                            setOpen(value);
                        }
                    },
                    onActionButtonClick: () =>
                    {
                        setTrace("点了主按钮");
                        setOpen(false);
                    },
                    onCloseButtonClick: () => setTrace("点了关闭按钮")),

            HStack(8,
                Button(locked ? "锁住展开：开" : "锁住展开：关", () => setLocked(!locked)),
                TextBlock($"state 里 IsOpen = {open}")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            TextBlock($"最后一次回执：{trace}").Caption().Subtle().Wrap(),

            TextBlock("换 preferredPlacement 只改「优先挂在哪一侧」——放不下时由官方自己挪，"
                      + "不是我们算的。")
                .Caption().Subtle().Wrap());
    }
}
