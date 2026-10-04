using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// <see cref="NativeElement"/> 的宿主：外面套一层 <c>Border</c> 当占位，
/// 真正的控件挂在 <c>Border.Child</c> 上。
/// </summary>
/// <remarks>
/// 为什么不直接返回 factory 造出来的控件：handler 契约要求 Mount 返回的控件类型固定
/// （<c>TControl</c>），而调用方每次可能给完全不同的控件类型。用 Border 当稳定的壳，
/// 换控件只需换 Child，元素类型不变、不会触发整棵子树重建。
/// <para>
/// 内部更新不归 Reactor 管：只有 <see cref="NativeElement.Token"/> 变化才重建，
/// 其余轮次一律不动——否则每次外层重渲染都会把压测列表重建一遍。
/// </para>
/// </remarks>
internal sealed class NativeHandler : ElementHandler<NativeElement, Border>
{
    protected override Border Mount(Reconciler reconciler, NativeElement element) =>
        new() { Child = element.Factory(), Tag = element };

    protected override void Update(
        Reconciler reconciler,
        NativeElement oldElement,
        NativeElement newElement,
        Border control)
    {
        if (Equals(oldElement.Token, newElement.Token))
        {
            return;
        }

        Dispose(control, oldElement);
        control.Child = newElement.Factory();
        control.Tag = newElement;
    }

    protected override void Unmount(Reconciler reconciler, Border control)
    {
        // Unmount 拿不到这一轮的元素，清理回调走 Mount/Update 时记在 Tag 上的那一份。
        if (control.Tag is NativeElement element)
        {
            Dispose(control, element);
        }

        control.Child = null;
    }

    /// <summary>换控件 / 卸载前先让调用方收摊（解事件、停 timer），再摘出可视树。</summary>
    private static void Dispose(Border control, NativeElement element)
    {
        if (control.Child is { } child)
        {
            element.OnDispose?.Invoke(child);
        }
    }
}
