using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>RefreshContainer</c>：给一段<b>能滚</b>的内容套上"下拉刷新"。
/// </summary>
/// <remarks>
/// <para>
/// <b>里面的内容必须自己能滚。</b>官方靠"内容已经滚到头了还在往下拉"判定刷新，
/// 塞一个不滚动的东西进去，是永远拉不出刷新的——这是它最常见的"没反应"的原因。
/// 本例放的是一个限高 <c>ScrollViewer</c>。
/// </para>
/// <para>
/// <b>刷新是可延迟的</b>：<c>OnRefreshRequested</c> 拿到一张
/// <see cref="RefreshTicket"/>，取了之后那个转圈的可视化器就一直转，
/// 直到 <see cref="RefreshTicket.Complete"/>。刷新几乎总是异步的，
/// 不延迟的话手一松它就收回去了。本例为了不引入定时器，取到就立刻完成——
/// 真实用法是"先去做事，做完了再 Complete"。
/// </para>
/// <para>
/// <c>PullDirection</c> 换的是"往哪个方向拉"。除了默认的
/// <c>TopToBottom</c>，另外三个方向都要内容<b>沿那个轴能滚</b>才拉得动，
/// 本例内容是纵向的，所以切成横向的那两档在这里拉不出来——不是坏了。
/// </para>
/// </remarks>
public sealed class RefreshContainerBasic : Component
{
    public override Element Render()
    {
        var (times, setTimes) = UseState(0);
        var (direction, setDirection) = UseState(MuxControls.RefreshPullDirection.TopToBottom);

        return VStack(12,
            HStack(8,
                Button($"方向：{direction}", () => setDirection(Next(direction))),
                TextBlock($"已经刷新 {times} 次")
                    .Caption().Subtle().VAlign(VerticalAlignment.Center)),

            RefreshContainer(
                    child: ScrollViewer(
                            child: VStack(6, Enumerable.Range(1, 24)
                                .Select(i => TextBlock($"第 {i} 行：把这块内容拉到顶再继续往下拽。"))
                                .ToArray()),
                            verticalScrollBar: ScrollBarVisibility.Auto)
                        .Height(220)
                        .MinWidth(200),
                    pullDirection: direction,
                    onRefreshRequested: ticket =>
                    {
                        setTimes(times + 1);

                        // 真实用法：这里发起异步请求，回调里再 ticket.Complete()。
                        // 本例立刻完成，于是可视化器只是短暂地转一下。
                        ticket.Complete();
                    })
                .Height(220)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            TextBlock("换成 LeftToRight / RightToLeft 之后，在这块纵向内容上是拉不出刷新的——"
                      + "那两档要内容能横向滚。横向内容配 BottomToTop 同理。")
                .Caption()
                .Subtle()
                .Wrap());
    }

    private static MuxControls.RefreshPullDirection Next(MuxControls.RefreshPullDirection current) =>
        current switch
        {
            MuxControls.RefreshPullDirection.TopToBottom => MuxControls.RefreshPullDirection.BottomToTop,
            MuxControls.RefreshPullDirection.BottomToTop => MuxControls.RefreshPullDirection.LeftToRight,
            MuxControls.RefreshPullDirection.LeftToRight => MuxControls.RefreshPullDirection.RightToLeft,
            _ => MuxControls.RefreshPullDirection.TopToBottom,
        };
}
