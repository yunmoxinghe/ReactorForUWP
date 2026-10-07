using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 下拉刷新容器（对应 WinUI 2 的 <see cref="MuxControls.RefreshContainer"/>）。
/// </summary>
/// <remarks>
/// <para>
/// 它是单子元素容器且继承 <c>ContentControl</c>，所以子内容走
/// <c>PatchSingleChild</c> 那条通用路径——这里没有"两个槽位"这类要自己管的事，
/// 卸载也由协调器的 <c>UnmountTree</c> 认 <c>SingleChildOf</c> 自动递归下去。
/// </para>
/// <para>
/// 唯一要自己记账的是 <c>RefreshRequested</c> 这个事件：先摘旧的再挂新的
/// （不摘的话每轮重渲染多挂一个，一次下拉触发 N 次刷新），卸载时<b>就地摘表</b>
/// 而不是转调 <c>Rebind</c>——卸载路径要能一眼看出"这张表在哪儿摘"。
/// </para>
/// <para>
/// 它<b>没有受控值</b>：用户能改的只有"往哪个方向拉"，而这个属性没有回执事件，
/// 写了也不会有人来回话。所以这里没有 <c>EchoGuard</c>，写入一律"值变了才写"。
/// </para>
/// </remarks>
internal sealed class RefreshContainerHandler
    : ElementHandler<RefreshContainerElement, MuxControls.RefreshContainer>
{
    private static readonly WeakTable<MuxControls.RefreshContainer,
            Windows.Foundation.TypedEventHandler<
                MuxControls.RefreshContainer, MuxControls.RefreshRequestedEventArgs>>
        Requests = new();

    protected override MuxControls.RefreshContainer Mount(
        Reconciler reconciler, RefreshContainerElement element)
    {
        var control = new MuxControls.RefreshContainer { PullDirection = element.PullDirection };

        if (element.Child is not null)
        {
            control.Content = reconciler.Build(element.Child);
        }

        Rebind(control, element.OnRefreshRequested);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        RefreshContainerElement oldElement,
        RefreshContainerElement newElement,
        MuxControls.RefreshContainer control)
    {
        PropWriter.Set(
            oldElement.PullDirection,
            newElement.PullDirection,
            value => control.PullDirection = value);

        reconciler.PatchSingleChild(control, oldElement.Child, newElement.Child);

        // 回调换成捕获了新 state 的闭包（与别处 Rebind 同一条规矩）。
        Rebind(control, newElement.OnRefreshRequested);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.RefreshContainer control)
    {
        if (Requests[control] is { } existing)
        {
            control.RefreshRequested -= existing;
            Requests.Remove(control);
        }
    }

    protected override Element? SingleChildOf(RefreshContainerElement element) => element.Child;

    /// <summary>先摘旧的、再挂新的。<paramref name="callback"/> 为 null 就是只摘不挂。</summary>
    private static void Rebind(
        MuxControls.RefreshContainer control, Action<RefreshTicket>? callback)
    {
        if (Requests[control] is { } existing)
        {
            control.RefreshRequested -= existing;
            Requests.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<
            MuxControls.RefreshContainer, MuxControls.RefreshRequestedEventArgs> handler =
            (_, args) => callback(new RefreshTicket(args.GetDeferral()));

        control.RefreshRequested += handler;
        Requests.Set(control, handler);
    }
}
