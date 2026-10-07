using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// WinUI 2 的 <c>TeachingTip</c>：挂在某个控件旁边的一段说明，受控 <c>IsOpen</c>。
/// </summary>
/// <remarks>
/// <para>
/// 它是 <c>ContentControl</c>，所以正文走 <c>PatchSingleChild</c> 那条通用路径，
/// 卸载也由协调器的 <c>UnmountTree</c> 顺着 <c>SingleChildOf</c> 递归下去。
/// 要自己记账的只有三件事：受控的 <c>IsOpen</c>、"指向哪个兄弟"的目标解析、
/// 以及三个事件（<c>Closed</c> / <c>ActionButtonClick</c> / <c>CloseButtonClick</c>）。
/// </para>
/// <para>
/// <b>目标为什么挂在 <c>Loaded</c> 上。</b><c>Target</c> 要的是<b>兄弟控件</b>，
/// 而 <c>Mount</c> 那一刻控件还没进可视树（协调器是"先造、再加进父容器"），
/// 问不到 <c>Parent</c>、也就数不出兄弟。所以这里在 <c>Loaded</c> 里补一次：
/// 那时父容器已经确定，<c>panel.Children[下标]</c> 就是那个兄弟。
/// 下标存在 <see cref="Slots"/> 里，<c>Update</c> 改了目标也能重新落一遍。
/// </para>
/// <para>
/// <c>Closing</c> 那条通道没有接（带 <c>Cancel</c> 与 <c>Deferral</c>）：
/// 声明式树下没有能拦住一次关闭的地方，需要它时走 <c>Native()</c>。
/// </para>
/// </remarks>
internal sealed class TeachingTipHandler : ElementHandler<TeachingTipElement, MuxControls.TeachingTip>
{
    private static readonly EchoGuard IsOpenEcho = new();

    private static readonly WeakTable<MuxControls.TeachingTip,
        Windows.Foundation.TypedEventHandler<
            MuxControls.TeachingTip, MuxControls.TeachingTipClosedEventArgs>> Closeds = new();

    private static readonly WeakTable<MuxControls.TeachingTip,
        Windows.Foundation.TypedEventHandler<MuxControls.TeachingTip, object>> Actions = new();

    private static readonly WeakTable<MuxControls.TeachingTip,
        Windows.Foundation.TypedEventHandler<MuxControls.TeachingTip, object>> Closes = new();

    private static readonly WeakTable<MuxControls.TeachingTip, RoutedEventHandler> Loadeds = new();

    /// <summary>最近一次下发的槽位（目标下标 + 正文元素）。</summary>
    private static readonly WeakTable<MuxControls.TeachingTip, (int? TargetIndex, Element? Child)> Slots = new();

    protected override MuxControls.TeachingTip Mount(Reconciler reconciler, TeachingTipElement element)
    {
        var control = new MuxControls.TeachingTip
        {
            Title = element.Title ?? string.Empty,
            Subtitle = element.Subtitle ?? string.Empty,
            IsLightDismissEnabled = element.IsLightDismissEnabled,
        };

        ApplyProps(control, null, element);

        if (element.IsOpen is { } open)
        {
            control.IsOpen = open;
        }

        if (element.Child is not null)
        {
            control.Content = reconciler.Build(element.Child);
        }

        Slots.Set(control, (element.TargetIndex, element.Child));

        // 一次性的：目标要等进了可视树才认得出（见类注释）。
        // Mount 每个实例只跑一次，Unmount 里配一次 -= —— 所以是"一次性订阅"。
        RoutedEventHandler loaded = (_, _) => ApplyTarget(control, Slots[control].TargetIndex);
        control.Loaded += loaded;
        Loadeds.Set(control, loaded);

        RebindClosed(control, element.OnIsOpenChanged);
        RebindAction(control, element.OnActionButtonClick);
        RebindClose(control, element.OnCloseButtonClick);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        TeachingTipElement oldElement,
        TeachingTipElement newElement,
        MuxControls.TeachingTip control)
    {
        // 这两批写入都罩进静默窗：它们理论上牵不动 IsOpen，但"窗没等到事件"的代价
        // 是零，漏罩则是一发假回调——代价不对称（与 DatePicker 的 Header 同一条理由）。
        using (IsOpenEcho.Silence(control))
        {
            ApplyProps(control, oldElement, newElement);

            // 下标可能换了，也可能上一轮还没进树（那时这次才第一次问到 Parent）。
            ApplyTarget(control, newElement.TargetIndex);
        }

        reconciler.PatchSingleChild(control, oldElement.Child, newElement.Child);
        Slots.Set(control, (newElement.TargetIndex, newElement.Child));

        if (newElement.IsOpen is not { } target)
        {
            RebindClosed(control, newElement.OnIsOpenChanged);
            RebindAction(control, newElement.OnActionButtonClick);
            RebindClose(control, newElement.OnCloseButtonClick);
            return;
        }

        if (control.IsOpen == target)
        {
            RebindClosed(control, newElement.OnIsOpenChanged);
            RebindAction(control, newElement.OnActionButtonClick);
            RebindClose(control, newElement.OnCloseButtonClick);
            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 TeachingTip{CtlId.Tag(control)}: {control.IsOpen} → {target}");

        IsOpenEcho.Expect(control, target);
        control.IsOpen = target;

        // 回调为空时订阅不存在，这一发没人领 → 撤销登记。
        IsOpenEcho.CancelIfUnconsumed(control);

        RebindClosed(control, newElement.OnIsOpenChanged);
        RebindAction(control, newElement.OnActionButtonClick);
        RebindClose(control, newElement.OnCloseButtonClick);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.TeachingTip control)
    {
        IsOpenEcho.Forget(control);

        if (Loadeds[control] is { } loaded)
        {
            control.Loaded -= loaded;
            Loadeds.Remove(control);
        }

        // 三个事件就地摘（不转调 Rebind(null)）：卸载路径要能一眼看出
        // "这张表在哪儿摘"，而不是顺着回调再跳一层。
        if (Closeds[control] is { } closed)
        {
            control.Closed -= closed;
            Closeds.Remove(control);
        }

        if (Actions[control] is { } action)
        {
            control.ActionButtonClick -= action;
            Actions.Remove(control);
        }

        if (Closes[control] is { } close)
        {
            control.CloseButtonClick -= close;
            Closes.Remove(control);
        }

        Slots.Remove(control);
    }

    protected override Element? SingleChildOf(TeachingTipElement element) => element.Child;

    /// <summary>
    /// 把"同层第几个"换成真正的兄弟控件。
    /// </summary>
    /// <remarks>
    /// 父容器还没确定（<c>Mount</c> 之后、进树之前）就跳过——那时 <c>Parent</c>
    /// 是 null，<c>Loaded</c> 会再来一次。下标越界给 null：官方收到 null 就是
    /// "没有目标"，比抛异常或随便挑一个强（与 <c>RelativePanelHandler.Sibling</c>
    /// 同一条规矩）。
    /// </remarks>
    private static void ApplyTarget(MuxControls.TeachingTip tip, int? index)
    {
        if (index is not { } i || tip.Parent is not Panel panel)
        {
            return;
        }

        tip.Target = i >= 0 && i < panel.Children.Count ? panel.Children[i] as FrameworkElement : null;
    }

    private static void ApplyProps(
        MuxControls.TeachingTip tip,
        TeachingTipElement? oldElement,
        TeachingTipElement newElement)
    {
        PropWriter.Set(
            oldElement?.Title, newElement.Title, value => tip.Title = value ?? string.Empty);
        PropWriter.Set(
            oldElement?.Subtitle, newElement.Subtitle, value => tip.Subtitle = value ?? string.Empty);
        PropWriter.Set(
            oldElement?.IsLightDismissEnabled ?? default,
            newElement.IsLightDismissEnabled,
            value => tip.IsLightDismissEnabled = value);
        PropWriter.Set(
            oldElement?.PreferredPlacement,
            newElement.PreferredPlacement,
            value =>
            {
                if (value is { } placement)
                {
                    tip.PreferredPlacement = placement;
                }
            });
        PropWriter.Set(
            oldElement?.ActionButtonText,
            newElement.ActionButtonText,
            value => tip.ActionButtonContent = value);
        PropWriter.Set(
            oldElement?.CloseButtonText,
            newElement.CloseButtonText,
            value => tip.CloseButtonContent = value);
    }

    /// <summary>
    /// <c>Closed</c> 是 <c>IsOpen</c> 的回执通道：参数固定是 <c>false</c>
    /// （"关了"是它唯一会说的话），回声配对也按这个值来。
    /// </summary>
    private static void RebindClosed(MuxControls.TeachingTip control, Action<bool>? callback)
    {
        if (Closeds[control] is { } existing)
        {
            control.Closed -= existing;
            Closeds.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<
                MuxControls.TeachingTip, MuxControls.TeachingTipClosedEventArgs> handler =
            (_, _) =>
            {
                if (IsOpenEcho.Consume(control, false))
                {
                    return;
                }

                callback(false);
            };

        control.Closed += handler;
        Closeds.Set(control, handler);
    }

    private static void RebindAction(MuxControls.TeachingTip control, Action? callback)
    {
        if (Actions[control] is { } existing)
        {
            control.ActionButtonClick -= existing;
            Actions.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<MuxControls.TeachingTip, object> handler =
            (_, _) => callback();

        control.ActionButtonClick += handler;
        Actions.Set(control, handler);
    }

    private static void RebindClose(MuxControls.TeachingTip control, Action? callback)
    {
        if (Closes[control] is { } existing)
        {
            control.CloseButtonClick -= existing;
            Closes.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<MuxControls.TeachingTip, object> handler =
            (_, _) => callback();

        control.CloseButtonClick += handler;
        Closes.Set(control, handler);
    }
}

/// <summary>
/// WinUI 2 的 <c>TwoPaneView</c>：两块内容按可用尺寸决定并排还是只留一块。
/// </summary>
/// <remarks>
/// <para>
/// <b>两个槽位都自己管。</b>它继承 <c>Control</c>（不是 <c>ContentControl</c>），
/// 而通用路径里没有"第二个槽位"这个概念——与 <c>SplitViewHandler</c> 管
/// <c>Pane</c> 是同一个形状、同一个理由。卸载也要自己走一遍：协调器的
/// <c>UnmountTree</c> 只从 <c>ContentControl</c> / <c>Border</c> / <c>Panel</c>
/// 里往下递归，不认这两个槽位，槽位里的组件 cleanup 全都跑不到。
/// </para>
/// <para>
/// <b><c>Mode</c> 只出不进。</b>官方的 <c>Mode</c> 是<b>只读</b>的（由可用尺寸算），
/// 唯一出口是 <c>ModeChanged</c>，所以这里一个"当前模式"的字段都没有——
/// 不写就没回声问题，也用不着 <c>EchoGuard</c>。
/// </para>
/// </remarks>
internal sealed class TwoPaneViewHandler : ElementHandler<TwoPaneViewElement, MuxControls.TwoPaneView>
{
    private static readonly WeakTable<MuxControls.TwoPaneView,
        Windows.Foundation.TypedEventHandler<MuxControls.TwoPaneView, object>> Handlers = new();

    /// <summary>最近一次下发的两个槽位元素（卸载时逐个递归用）。</summary>
    private static readonly WeakTable<MuxControls.TwoPaneView, (Element? Pane1, Element? Pane2)> Slots = new();

    protected override MuxControls.TwoPaneView Mount(Reconciler reconciler, TwoPaneViewElement element)
    {
        var control = new MuxControls.TwoPaneView
        {
            PanePriority = element.PanePriority,
            WideModeConfiguration = element.WideModeConfiguration,
            TallModeConfiguration = element.TallModeConfiguration,
            MinWideModeWidth = element.MinWideModeWidth,
            MinTallModeHeight = element.MinTallModeHeight,
        };

        ApplyProps(control, null, element);
        ApplyPane(reconciler, control, null, element.Pane1, slot: 1);
        ApplyPane(reconciler, control, null, element.Pane2, slot: 2);
        Slots.Set(control, (element.Pane1, element.Pane2));
        Rebind(control, element.OnModeChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        TwoPaneViewElement oldElement,
        TwoPaneViewElement newElement,
        MuxControls.TwoPaneView control)
    {
        ApplyProps(control, oldElement, newElement);
        ApplyPane(reconciler, control, oldElement.Pane1, newElement.Pane1, slot: 1);
        ApplyPane(reconciler, control, oldElement.Pane2, newElement.Pane2, slot: 2);
        Slots.Set(control, (newElement.Pane1, newElement.Pane2));
        Rebind(control, newElement.OnModeChanged);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.TwoPaneView control)
    {
        if (Slots.TryGetValue(control, out var slots))
        {
            if (control.Pane1 is UIElement pane1)
            {
                reconciler.UnmountNative(pane1, slots.Pane1 ?? EmptyElement.Instance);
            }

            if (control.Pane2 is UIElement pane2)
            {
                reconciler.UnmountNative(pane2, slots.Pane2 ?? EmptyElement.Instance);
            }
        }

        Slots.Remove(control);

        if (Handlers[control] is { } existing)
        {
            control.ModeChanged -= existing;
            Handlers.Remove(control);
        }
    }

    private static void ApplyProps(
        MuxControls.TwoPaneView control,
        TwoPaneViewElement? oldElement,
        TwoPaneViewElement newElement)
    {
        PropWriter.Set(
            oldElement?.PanePriority ?? default,
            newElement.PanePriority,
            value => control.PanePriority = value);
        PropWriter.Set(
            oldElement?.WideModeConfiguration ?? default,
            newElement.WideModeConfiguration,
            value => control.WideModeConfiguration = value);
        PropWriter.Set(
            oldElement?.TallModeConfiguration ?? default,
            newElement.TallModeConfiguration,
            value => control.TallModeConfiguration = value);

        if (Math.Abs((oldElement?.MinWideModeWidth ?? double.MinValue) - newElement.MinWideModeWidth) > 1e-6)
        {
            control.MinWideModeWidth = newElement.MinWideModeWidth;
        }

        if (Math.Abs((oldElement?.MinTallModeHeight ?? double.MinValue) - newElement.MinTallModeHeight) > 1e-6)
        {
            control.MinTallModeHeight = newElement.MinTallModeHeight;
        }

        // GridLength 是值类型、没有"没给"这一档，所以只在真的给了才写。
        if (newElement.Pane1Length is { } length1)
        {
            control.Pane1Length = length1;
        }

        if (newElement.Pane2Length is { } length2)
        {
            control.Pane2Length = length2;
        }
    }

    /// <summary>一个槽位：能就地 patch 就 patch，类型换了才重建。</summary>
    private static void ApplyPane(
        Reconciler reconciler,
        MuxControls.TwoPaneView control,
        Element? oldPane,
        Element? newPane,
        int slot)
    {
        var current = slot == 1 ? control.Pane1 : control.Pane2;

        if (newPane is null)
        {
            if (current is not null)
            {
                reconciler.UnmountNative(current, oldPane ?? EmptyElement.Instance);

                if (slot == 1)
                {
                    control.Pane1 = null;
                }
                else
                {
                    control.Pane2 = null;
                }
            }

            return;
        }

        if (current is null)
        {
            var built = reconciler.Build(newPane);

            if (slot == 1)
            {
                control.Pane1 = built;
            }
            else
            {
                control.Pane2 = built;
            }

            return;
        }

        if (oldPane is not null && Reconciler.CanPatch(oldPane, newPane))
        {
            reconciler.Patch(current, oldPane, newPane);
            return;
        }

        if (oldPane is not null)
        {
            reconciler.UnmountNative(current, oldPane);
        }

        var rebuilt = reconciler.Build(newPane);

        if (slot == 1)
        {
            control.Pane1 = rebuilt;
        }
        else
        {
            control.Pane2 = rebuilt;
        }
    }

    private static void Rebind(
        MuxControls.TwoPaneView control, Action<MuxControls.TwoPaneViewMode>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.ModeChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<MuxControls.TwoPaneView, object> handler =
            (_, _) => callback(control.Mode);

        control.ModeChanged += handler;
        Handlers.Set(control, handler);
    }
}
