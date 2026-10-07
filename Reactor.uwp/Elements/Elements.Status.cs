using System;
using Microsoft.UI.Reactor.Core;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  状态与信息补完（下拉刷新容器，WinUI 2 真控件）
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 一次刷新请求的"回执单"。
/// </summary>
/// <remarks>
/// <para>
/// 官方的 <c>RefreshRequested</c> 是<b>可延迟</b>的：事件参数上取一个
/// <c>Deferral</c>，取了之后那个转圈的可视化器就一直转，直到调用
/// <c>Complete()</c>。这才是"下拉刷新"该有的形状——刷新几乎总是异步的，
/// 不延迟的话手一松它就收回去了。
/// </para>
/// <para>
/// 这里把它包成一个只能兑现一次的小对象，理由与 <c>RefreshContainer</c>
/// 那边"回调是声明式"一致：把 <c>Deferral</c> 直接递给用户代码，等于把
/// WinRT 对象泄漏进组件层，而且 ABA（回调里 Complete 两次）没有任何防线。
/// <b>没取 deferral 就 Complete 是安全的空操作</b>——官方允许"取了立刻完成"，
/// 也允许根本不取。
/// </para>
/// </remarks>
public sealed class RefreshTicket
{
    private Windows.Foundation.Deferral? _deferral;

    internal RefreshTicket(Windows.Foundation.Deferral? deferral) => _deferral = deferral;

    /// <summary>
    /// 刷新做完了。重复调用是安全的（第二次起什么都不做）——
    /// 事件可能被重入，而 <c>Deferral.Complete()</c> 只能兑现一次。
    /// </summary>
    public void Complete()
    {
        var deferral = _deferral;
        _deferral = null;

        deferral?.Complete();
    }
}

/// <summary>
/// 下拉刷新容器（对应 WinUI 2 的 <see cref="MuxControls.RefreshContainer"/>）：
/// 给<b>一段可滚动的内容</b>套上"下拉刷新"。
/// </summary>
/// <remarks>
/// <para>
/// 它是<b>单子元素容器</b>（继承 <c>ContentControl</c>），里面放的那一个子元素
/// 必须自己能滚——官方靠"内容已经滚到头了还在往下拉"来触发刷新，
/// 塞一个不滚动的东西进去是永远拉不出刷新的。
/// </para>
/// <para>
/// <b>可视化器（<c>Visualizer</c>）不在本元素的参数里。</b>官方不设时会自己造一个
/// 默认的，绝大多数用法就是要这个默认；要换样式请走 <c>Native()</c>——
/// 为它单独开一套元素不划算（它是一个只挂在 <c>RefreshContainer.Visualizer</c>
/// 上的子部件，与 <c>MenuFlyout</c> 同形，本来就不进协调器）。
/// </para>
/// <para>
/// <c>RequestRefresh()</c>（命令式触发一次刷新）同样没有暴露：声明式树里没有
/// 拿控件句柄的地方，硬塞一个进去就要为它养一张"元素 ↔ 控件"的表。
/// 需要它的时候走 <c>Native()</c>。
/// </para>
/// </remarks>
public sealed record RefreshContainerElement(Element? Child) : Element
{
    /// <summary>从哪个方向拉出刷新（默认 <see cref="MuxControls.RefreshPullDirection.TopToBottom"/>）。</summary>
    public MuxControls.RefreshPullDirection PullDirection { get; init; } =
        MuxControls.RefreshPullDirection.TopToBottom;

    /// <summary>
    /// 刷新被触发。拿到 <see cref="RefreshTicket"/> 后先去做事，做完了再
    /// <see cref="RefreshTicket.Complete"/>；不延迟也行（立刻 Complete 或不 Complete）。
    /// </summary>
    public Action<RefreshTicket>? OnRefreshRequested { get; init; }
}
