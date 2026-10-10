using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// UWP 原生 <c>Popup</c>：一块盖在最上层的任意内容，受控 <c>IsOpen</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>单槽容器：登记一次就够了。</b>它继承 <c>FrameworkElement</c>、内容在
/// <c>Child</c> 上，既不是 <c>ContentControl</c> 也不是 <c>Border</c>，
/// 所以在 <c>SingleChildAccessor</c> 里给 <c>Popup</c> 登记一个访问器即可
/// （同 <c>Viewbox</c> / <c>ParallaxView</c>，见 <c>ElementHandlerRegistry</c>）。
/// 那条路的 patch 侧与卸载侧现在都问同一张表，因此这里<b>不再</b>需要
/// 手写递归卸载——写过一次，但那是给"卸载侧少一级"这个公共路径的洞打补丁：
/// 洞已经收进单一真源（见 <c>SingleChildAccessor</c> 的类注释），补丁就必须跟着撤，
/// 否则同一次卸载会走两遍。
/// </para>
/// <para>
/// <b><c>Opened</c> / <c>Closed</c> 都接到同一个受控回调上。</b>官方这两个事件
/// 说的是同一件事的两半，这里把它们合成元素上那一个 <c>OnIsOpenChanged</c>
/// （与 <c>TeachingTip</c> 那条同形），并且<b>只在一个方法里加工回执</b>——
/// 一个守卫拆成两处 <c>Consume</c> 会让回声契约对"少了其中一条"失去视力，
/// 详见 <c>RebindIsOpen</c> 的注释。
/// </para>
/// <para>
/// 下面那行注释是给静态检查看的：<c>Child</c> 是<b>子槽</b>而不是配置属性，
/// 它的更新走 <c>PatchSingleChild</c> 那条通用路径，所以不会出现在
/// <c>Update</c> 里——<c>PropertyDriftTests</c> 认这个登记，没有它就会报警。
/// </para>
/// </remarks>
// MOUNT-ONLY: Child
internal sealed class PopupHandler : ElementHandler<PopupElement, Popup>
{
    private static readonly EchoGuard IsOpenEcho = new();

    private static readonly WeakTable<Popup, EventHandler<object>> Openeds = new();

    private static readonly WeakTable<Popup, EventHandler<object>> Closeds = new();

    private static readonly WeakTable<Popup, RoutedEventHandler> Loadeds = new();

    /// <summary>最近一次下发的槽位（目标下标 + 内容元素）。</summary>
    private static readonly WeakTable<Popup, (int? TargetIndex, Element? Child)> Slots = new();

    protected override Popup Mount(Reconciler reconciler, PopupElement element)
    {
        var control = new Popup
        {
            IsLightDismissEnabled = element.IsLightDismissEnabled,
        };

        ApplyProps(control, null, element);

        if (element.IsOpen is { } open)
        {
            control.IsOpen = open;
        }

        if (element.Child is not null)
        {
            control.Child = reconciler.Build(element.Child);
        }

        Slots.Set(control, (element.TargetIndex, element.Child));

        // 一次性订阅，理由与 TeachingTipHandler 一致：Target 要等进了可视树
        // 才问得出 Parent（Mount 那一刻控件还没挂上去）。
        RoutedEventHandler loaded = (_, _) => ApplyTarget(control, Slots[control].TargetIndex);
        control.Loaded += loaded;
        Loadeds.Set(control, loaded);

        RebindIsOpen(control, element.OnIsOpenChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        PopupElement oldElement,
        PopupElement newElement,
        Popup control)
    {
        // 罩静默窗的理由与 TeachingTip 那条一字不差：这些属性理论上牵不动
        // IsOpen，但"窗没等到事件"的代价是零，漏罩则是一发假回调。
        using (IsOpenEcho.Silence(control))
        {
            ApplyProps(control, oldElement, newElement);
            ApplyTarget(control, newElement.TargetIndex);
        }

        reconciler.PatchSingleChild(control, oldElement.Child, newElement.Child);
        Slots.Set(control, (newElement.TargetIndex, newElement.Child));

        RebindIsOpen(control, newElement.OnIsOpenChanged);

        if (newElement.IsOpen is not { } target || control.IsOpen == target)
        {
            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 Popup{CtlId.Tag(control)}: {control.IsOpen} → {target}");

        IsOpenEcho.Expect(control, target);
        control.IsOpen = target;

        // 回调为空时订阅不存在，这一发没人领 → 撤销登记。
        IsOpenEcho.CancelIfUnconsumed(control);
    }

    protected override void Unmount(Reconciler reconciler, Popup control)
    {
        IsOpenEcho.Forget(control);

        if (Loadeds[control] is { } loaded)
        {
            control.Loaded -= loaded;
            Loadeds.Remove(control);
        }

        if (Openeds[control] is { } opened)
        {
            control.Opened -= opened;
            Openeds.Remove(control);
        }

        if (Closeds[control] is { } closed)
        {
            control.Closed -= closed;
            Closeds.Remove(control);
        }

        // 槽里的子树由 UnmountTree 的通用路径递归 —— 这里只收本类型的静态状态。
        Slots.Remove(control);
    }

    protected override Element? SingleChildOf(PopupElement element) => element.Child;

    /// <summary>
    /// 把"同层第几个"换成真正的兄弟控件。
    /// </summary>
    /// <remarks>
    /// 与 <c>TeachingTipHandler.ApplyTarget</c> 同形：父容器还没确定就跳过
    /// （那时 <c>Loaded</c> 会再来一次），下标越界给 null。
    /// </remarks>
    private static void ApplyTarget(Popup popup, int? index)
    {
        if (index is not { } i || popup.Parent is not Panel panel)
        {
            return;
        }

        popup.PlacementTarget = i >= 0 && i < panel.Children.Count ? panel.Children[i] as FrameworkElement : null;
    }

    private static void ApplyProps(Popup popup, PopupElement? oldElement, PopupElement newElement)
    {
        PropWriter.Set(
            oldElement?.IsLightDismissEnabled ?? default,
            newElement.IsLightDismissEnabled,
            value => popup.IsLightDismissEnabled = value);

        // 这三个都是"给了才写"：官方各自有默认值，抄一个数字进来就是把
        // 一个可能随版本调整的值当成契约（与 NavigationView 的两个响应式阈值
        // 同一条纪律）。
        PropWriter.Set(
            oldElement?.ShouldConstrainToRootBounds,
            newElement.ShouldConstrainToRootBounds,
            value =>
            {
                if (value is { } constrain)
                {
                    popup.ShouldConstrainToRootBounds = constrain;
                }
            });

        PropWriter.Set(
            oldElement?.HorizontalOffset,
            newElement.HorizontalOffset,
            value =>
            {
                if (value is { } dx)
                {
                    popup.HorizontalOffset = dx;
                }
            });

        PropWriter.Set(
            oldElement?.VerticalOffset,
            newElement.VerticalOffset,
            value =>
            {
                if (value is { } dy)
                {
                    popup.VerticalOffset = dy;
                }
            });

        PropWriter.Set(
            oldElement?.DesiredPlacement,
            newElement.DesiredPlacement,
            value =>
            {
                if (value is { } placement)
                {
                    popup.DesiredPlacement = placement;
                }
            });
    }

    /// <summary>
    /// <c>Opened</c> 与 <c>Closed</c> 合成一个受控出口。
    /// </summary>
    /// <remarks>
    /// <b>两个事件 → 一处回执加工。</b>它们说的是同一件事的两半（"开了" / "关了"），
    /// 这里合成元素上那一个 <c>OnIsOpenChanged</c>。之所以要合成到同一个方法里，
    /// 除了避免两个 lambda 各写一遍，还有一条硬的：本库的回声契约按
    /// <b>守卫 + 消费点</b>记账，一个守卫拆成两处 <c>Consume</c> 之后，
    /// 抹掉其中任何一处都不会让那条契约报警（另一处还在替它答到）——
    /// 于是"少了一条回执"这件事<b>在静态检查里是看不见的</b>。
    /// 集中到 <see cref="Feedback"/> 一处，这条契约才又盯得住它。
    /// </remarks>
    private static void RebindIsOpen(Popup control, Action<bool>? callback)
    {
        if (Openeds[control] is { } existingOpened)
        {
            control.Opened -= existingOpened;
            Openeds.Remove(control);
        }

        if (Closeds[control] is { } existingClosed)
        {
            control.Closed -= existingClosed;
            Closeds.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        EventHandler<object> opened = (_, _) => Feedback(control, callback, true);
        EventHandler<object> closed = (_, _) => Feedback(control, callback, false);

        control.Opened += opened;
        control.Closed += closed;
        Openeds.Set(control, opened);
        Closeds.Set(control, closed);
    }

    /// <summary>
    /// <c>IsOpen</c> 唯一的回执加工点：认得出回声就吞掉，认不出才往外报。
    /// </summary>
    private static void Feedback(Popup control, Action<bool> callback, bool value)
    {
        if (IsOpenEcho.Consume(control, value))
        {
            return;
        }

        callback(value);
    }
}
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
        // 两个 pane 都由协调器收（见下面的 ExtraSlotsOf）——这里只管本类型自己的状态。
        Slots.Remove(control);

        if (Handlers[control] is { } existing)
        {
            control.ModeChanged -= existing;
            Handlers.Remove(control);
        }
    }

    /// <summary>
    /// <c>Pane1</c> / <c>Pane2</c>：与 <c>SplitView</c> 同形的两个独立槽位。
    /// </summary>
    /// <remarks>
    /// 它们既不在主槽上（这个类型没有 <c>SingleChildOf</c>），也进不了
    /// <c>ChildrenOf</c>——所以由这里报给协调器，卸载时跟着一起递归；
    /// 以前是 <c>Unmount</c> 里手写的，现在归到通用路径那一处。
    /// </remarks>
    protected override IReadOnlyList<(UIElement Native, Element? Element)> ExtraSlotsOf(
        MuxControls.TwoPaneView control)
    {
        if (!Slots.TryGetValue(control, out var slots))
        {
            return Array.Empty<(UIElement, Element?)>();
        }

        var list = new List<(UIElement, Element?)>(2);

        if (control.Pane1 is { } pane1)
        {
            list.Add((pane1, slots.Pane1));
        }

        if (control.Pane2 is { } pane2)
        {
            list.Add((pane2, slots.Pane2));
        }

        return list;
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
