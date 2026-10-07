using Microsoft.UI.Reactor.Core;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    // ── 状态与信息 ─────────────────────────────────────────────

    /// <summary>
    /// 下拉刷新容器：给一段<b>能滚</b>的内容套上刷新。
    /// </summary>
    /// <remarks>
    /// 子元素必须自己能滚（通常是 <c>ScrollViewer</c> 或 <c>ListView</c>）——
    /// 官方靠"内容滚到头了还在往下拉"判定刷新，塞不滚动的内容进去永远拉不出来。
    /// </remarks>
    public static RefreshContainerElement RefreshContainer(
        Element? child,
        MuxControls.RefreshPullDirection pullDirection = MuxControls.RefreshPullDirection.TopToBottom,
        System.Action<RefreshTicket>? onRefreshRequested = null) =>
        new(child) { PullDirection = pullDirection, OnRefreshRequested = onRefreshRequested };
}
