using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.Foundation;
using Windows.UI.Xaml;
using WuControls = Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// UWP 原生的 <c>SemanticZoom</c>：同一批数据的"细看"与"总览"两种视图，受控
/// <c>IsZoomedInViewActive</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>两个槽位都自己管。</b>它继承 <c>Control</c>（不是 <c>ContentControl</c>），
/// 且两个槽位的类型是 <c>ISemanticZoomInformation</c> 而不是 <c>UIElement</c>——
/// 通用路径里既没有"第二个槽位"、也没有"槽位要过一道接口检查"这个概念，
/// 所以与 <c>TwoPaneViewHandler</c> 同形：自己 build、自己 patch、卸载时自己递归一遍。
/// </para>
/// <para>
/// <b>槽位类型不对时留空并留痕，不抛。</b>官方靠 <c>ISemanticZoomInformation</c>
/// 才能把"总览里点中的那一项"翻译成"细看里滚到哪儿"，<c>Grid</c> 之类的容器
/// 报不出这个信息，塞进去只会得到一个永远切不动的 <c>SemanticZoom</c>。
/// 留痕比静默吞掉好，比抛好——抛会把整页炸掉。
/// </para>
/// <para>
/// <b>受控那一发是异步的。</b>写 <c>IsZoomedInViewActive</c> 触发的是一次带动画的
/// 视图切换，<c>ViewChangeCompleted</c> 因此晚于这次调用：<c>CancelIfUnconsumed</c>
/// 会把登记撤掉，那一发晚到的事件落到 <c>NotExpected</c> → 多回调一次。它的值等于
/// 刚写进去的受控值，<c>setState</c> 同值不重渲染，多出来的是一次空转——
/// 这是 <c>EchoGuard</c> 那条"宁可多回调一次"的既定取舍，不是这里的特例。
/// </para>
/// </remarks>
internal sealed class SemanticZoomHandler : ElementHandler<SemanticZoomElement, WuControls.SemanticZoom>
{
    private static readonly EchoGuard ActiveEcho = new();

    private static readonly WeakTable<WuControls.SemanticZoom,
        WuControls.SemanticZoomViewChangedEventHandler> Handlers = new();

    /// <summary>最近一次真正挂上的两个槽位元素（卸载时逐个递归用）。</summary>
    private static readonly WeakTable<WuControls.SemanticZoom, (Element? ZoomedIn, Element? ZoomedOut)> Slots =
        new();

    protected override WuControls.SemanticZoom Mount(Reconciler reconciler, SemanticZoomElement element)
    {
        var control = new WuControls.SemanticZoom
        {
            CanChangeViews = element.CanChangeViews,
            IsZoomOutButtonEnabled = element.IsZoomOutButtonEnabled,
        };

        var zoomedIn = ApplyView(reconciler, control, null, element.ZoomedInView, slot: 1);
        var zoomedOut = ApplyView(reconciler, control, null, element.ZoomedOutView, slot: 2);
        Slots.Set(control, (zoomedIn, zoomedOut));

        // 与 ColorPicker 同一条理由：这次写入发生在订阅之前，没人听见。
        if (element.IsZoomedInViewActive is { } active)
        {
            control.IsZoomedInViewActive = active;
        }

        Rebind(control, element.OnIsZoomedInViewActiveChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        SemanticZoomElement oldElement,
        SemanticZoomElement newElement,
        WuControls.SemanticZoom control)
    {
        // 这两个开关理论上牵不动 IsZoomedInViewActive，但"窗没等到事件"的代价是
        // 零，漏罩则是一发假回调——代价不对称（与 ColorPicker 那批开关同一条理由）。
        using (ActiveEcho.Silence(control))
        {
            PropWriter.Set(
                oldElement.CanChangeViews,
                newElement.CanChangeViews,
                value => control.CanChangeViews = value);
            PropWriter.Set(
                oldElement.IsZoomOutButtonEnabled,
                newElement.IsZoomOutButtonEnabled,
                value => control.IsZoomOutButtonEnabled = value);
        }

        var zoomedIn = ApplyView(reconciler, control, oldElement.ZoomedInView, newElement.ZoomedInView, slot: 1);
        var zoomedOut = ApplyView(
            reconciler, control, oldElement.ZoomedOutView, newElement.ZoomedOutView, slot: 2);
        Slots.Set(control, (zoomedIn, zoomedOut));

        if (newElement.IsZoomedInViewActive is not { } target)
        {
            Rebind(control, newElement.OnIsZoomedInViewActiveChanged);
            return;
        }

        if (control.IsZoomedInViewActive == target)
        {
            Rebind(control, newElement.OnIsZoomedInViewActiveChanged);
            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 SemanticZoom{CtlId.Tag(control)}: " +
            $"{(control.IsZoomedInViewActive ? "细看" : "总览")} → {(target ? "细看" : "总览")}");

        ActiveEcho.Expect(control, target);
        control.IsZoomedInViewActive = target;

        // 这一发的回执是异步的，多半在写完时还没到 → 撤销登记（理由见类注释）。
        ActiveEcho.CancelIfUnconsumed(control);

        Rebind(control, newElement.OnIsZoomedInViewActiveChanged);
    }

    protected override void Unmount(Reconciler reconciler, WuControls.SemanticZoom control)
    {
        ActiveEcho.Forget(control);

        if (Slots.TryGetValue(control, out var slots))
        {
            if (control.ZoomedInView is UIElement zoomedIn)
            {
                reconciler.UnmountNative(zoomedIn, slots.ZoomedIn ?? EmptyElement.Instance);
            }

            if (control.ZoomedOutView is UIElement zoomedOut)
            {
                reconciler.UnmountNative(zoomedOut, slots.ZoomedOut ?? EmptyElement.Instance);
            }
        }

        Slots.Remove(control);

        if (Handlers[control] is { } existing)
        {
            control.ViewChangeCompleted -= existing;
            Handlers.Remove(control);
        }
    }

    /// <summary>
    /// 一个槽位：能就地 patch 就 patch，类型换了才重建。
    /// </summary>
    /// <returns>真正挂进槽位的那个元素；类型不合格时是 <c>null</c>（已留痕）。</returns>
    private static Element? ApplyView(
        Reconciler reconciler,
        WuControls.SemanticZoom control,
        Element? oldView,
        Element? newView,
        int slot)
    {
        // 槽位的声明类型是 ISemanticZoomInformation，能进来的实际类型一定是
        // UIElement（ListView / GridView），所以这里直接当 UIElement 用。
        var current = (slot == 1 ? control.ZoomedInView : control.ZoomedOutView) as UIElement;

        if (newView is null)
        {
            if (current is not null)
            {
                reconciler.UnmountNative(current, oldView ?? EmptyElement.Instance);
                SetView(control, slot, null);
            }

            return null;
        }

        if (current is null)
        {
            return Attach(reconciler, control, newView, slot);
        }

        if (oldView is not null && Reconciler.CanPatch(oldView, newView))
        {
            reconciler.Patch(current, oldView, newView);
            return newView;
        }

        reconciler.UnmountNative(current, oldView ?? EmptyElement.Instance);
        return Attach(reconciler, control, newView, slot);
    }

    private static Element? Attach(
        Reconciler reconciler,
        WuControls.SemanticZoom control,
        Element view,
        int slot)
    {
        var built = reconciler.Build(view);

        // 接口检查（理由见类注释）：不合格的整棵卸掉，槽位留空。
        if (built is not WuControls.ISemanticZoomInformation info)
        {
            ReactorApplication.Trace(
                $"[reactor] SemanticZoom 的两个槽位只收 ListView / GridView" +
                $"（实现了 ISemanticZoomInformation 的那几个），" +
                $"收到 {view.GetType().Name}，已忽略");
            reconciler.UnmountNative(built, view);
            return null;
        }

        SetView(control, slot, info);
        return view;
    }

    private static void SetView(
        WuControls.SemanticZoom control, int slot, WuControls.ISemanticZoomInformation? view)
    {
        if (slot == 1)
        {
            control.ZoomedInView = view;
        }
        else
        {
            control.ZoomedOutView = view;
        }
    }

    private static void Rebind(WuControls.SemanticZoom control, Action<bool>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.ViewChangeCompleted -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        WuControls.SemanticZoomViewChangedEventHandler handler =
            (_, _) =>
            {
                if (ActiveEcho.Consume(control, control.IsZoomedInViewActive))
                {
                    return;
                }

                callback(control.IsZoomedInViewActive);
            };

        control.ViewChangeCompleted += handler;
        Handlers.Set(control, handler);
    }
}

/// <summary>
/// WinUI 2 的 <c>PipsPager</c>：一排小点 + 前后翻页两个按钮，受控
/// <c>SelectedPageIndex</c>。
/// </summary>
/// <remarks>
/// <para>
/// 形与 <c>ColorPicker.Color</c> 一致：写 <c>SelectedPageIndex</c> 会同步抛
/// <c>SelectedIndexChanged</c>，那一发的作者是我们，靠 <c>EchoGuard</c> 认下来。
/// </para>
/// <para>
/// <b>页数 / 可见点数那几个写入罩进静默窗。</b>把 10 页改成 3 页时，停在第 8 页会被
/// 控件夹回边界内——与 <c>NumberBox</c> 的 <c>Minimum</c> / <c>Maximum</c> 同形：
/// 夹出来的值只有控件自己知道，没法用 <c>Expect</c> 配它。
/// </para>
/// </remarks>
internal sealed class PipsPagerHandler : ElementHandler<PipsPagerElement, MuxControls.PipsPager>
{
    private static readonly EchoGuard PageEcho = new();

    private static readonly WeakTable<MuxControls.PipsPager,
        TypedEventHandler<MuxControls.PipsPager, MuxControls.PipsPagerSelectedIndexChangedEventArgs>>
        Handlers = new();

    protected override MuxControls.PipsPager Mount(Reconciler reconciler, PipsPagerElement element)
    {
        var control = new MuxControls.PipsPager
        {
            NumberOfPages = element.NumberOfPages,
            MaxVisiblePips = element.MaxVisiblePips,
            Orientation = element.Orientation,
            PreviousButtonVisibility = element.PreviousButtonVisibility,
            NextButtonVisibility = element.NextButtonVisibility,
        };

        if (element.SelectedPageIndex is { } index)
        {
            control.SelectedPageIndex = index;
        }

        Rebind(control, element.OnSelectedPageIndexChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        PipsPagerElement oldElement,
        PipsPagerElement newElement,
        MuxControls.PipsPager control)
    {
        // 会把受控值夹走的那一批 + 纯粹的排版开关：罩窗（理由见类注释）。
        using (PageEcho.Silence(control))
        {
            PropWriter.Set(
                oldElement.NumberOfPages,
                newElement.NumberOfPages,
                value => control.NumberOfPages = value);
            PropWriter.Set(
                oldElement.MaxVisiblePips,
                newElement.MaxVisiblePips,
                value => control.MaxVisiblePips = value);
            PropWriter.Set(
                oldElement.Orientation,
                newElement.Orientation,
                value => control.Orientation = value);
            PropWriter.Set(
                oldElement.PreviousButtonVisibility,
                newElement.PreviousButtonVisibility,
                value => control.PreviousButtonVisibility = value);
            PropWriter.Set(
                oldElement.NextButtonVisibility,
                newElement.NextButtonVisibility,
                value => control.NextButtonVisibility = value);
        }

        if (newElement.SelectedPageIndex is not { } target)
        {
            Rebind(control, newElement.OnSelectedPageIndexChanged);
            return;
        }

        if (control.SelectedPageIndex == target)
        {
            Rebind(control, newElement.OnSelectedPageIndexChanged);
            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 PipsPager{CtlId.Tag(control)}: {control.SelectedPageIndex} → {target}");

        PageEcho.Expect(control, target);
        control.SelectedPageIndex = target;

        // 回调为空时订阅不存在，这一发没人领 → 撤销登记。
        PageEcho.CancelIfUnconsumed(control);

        Rebind(control, newElement.OnSelectedPageIndexChanged);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.PipsPager control)
    {
        PageEcho.Forget(control);
        Rebind(control, null);
    }

    private static void Rebind(MuxControls.PipsPager control, Action<int>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.SelectedIndexChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        TypedEventHandler<MuxControls.PipsPager, MuxControls.PipsPagerSelectedIndexChangedEventArgs> handler =
            (_, _) =>
            {
                if (PageEcho.Consume(control, control.SelectedPageIndex))
                {
                    return;
                }

                callback(control.SelectedPageIndex);
            };

        control.SelectedIndexChanged += handler;
        Handlers.Set(control, handler);
    }
}

/// <summary>
/// WinUI 2 的 <c>SwipeControl</c>：内容上轻轻一滑，从边上露出几条命令。
/// </summary>
/// <remarks>
/// <para>
/// 它是 <c>ContentControl</c>，所以内容走 <c>PatchSingleChild</c> 那条通用路径，
/// 卸载也由协调器的 <c>UnmountTree</c> 顺着 <c>SingleChildOf</c> 递归下去。
/// 要自己管的只有四组命令。
/// </para>
/// <para>
/// <b>命令整组重建，不逐项 patch。</b>与 <c>CommandBarHandler.ApplyCommands</c>
/// 同一个取舍、同一条理由：命令项只有文字 / 图标 / 回调三种数据，没有需要跨帧
/// 保留的状态。所以每次都挂在<b>新建</b>的 <c>SwipeItem</c> 上，不存在"旧回调没解绑"。
/// </para>
/// </remarks>
internal sealed class SwipeControlHandler : ElementHandler<SwipeControlElement, MuxControls.SwipeControl>
{
    protected override MuxControls.SwipeControl Mount(Reconciler reconciler, SwipeControlElement element)
    {
        var control = new MuxControls.SwipeControl();
        ApplyItems(() => control.LeftItems, value => control.LeftItems = value, element.Left);
        ApplyItems(() => control.RightItems, value => control.RightItems = value, element.Right);
        ApplyItems(() => control.TopItems, value => control.TopItems = value, element.Top);
        ApplyItems(() => control.BottomItems, value => control.BottomItems = value, element.Bottom);
        reconciler.PatchSingleChild(control, null, element.Content);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        SwipeControlElement oldElement,
        SwipeControlElement newElement,
        MuxControls.SwipeControl control)
    {
        ApplyItems(() => control.LeftItems, value => control.LeftItems = value, newElement.Left);
        ApplyItems(() => control.RightItems, value => control.RightItems = value, newElement.Right);
        ApplyItems(() => control.TopItems, value => control.TopItems = value, newElement.Top);
        ApplyItems(() => control.BottomItems, value => control.BottomItems = value, newElement.Bottom);
        reconciler.PatchSingleChild(control, oldElement.Content, newElement.Content);
    }

    protected override Element? SingleChildOf(SwipeControlElement element) => element.Content;

    /// <summary>
    /// 一组命令：换一个新的 <c>SwipeItems</c>（官方 <c>Mode</c> 长在它身上，
    /// 所以换组而不是往旧组里塞）。没有命令就把那一侧清成 <c>null</c>。
    /// </summary>
    private static void ApplyItems(
        Func<MuxControls.SwipeItems?> get,
        Action<MuxControls.SwipeItems?> set,
        SwipeItemsData? group)
    {
        if (group is null)
        {
            set(null);
            return;
        }

        var items = new MuxControls.SwipeItems { Mode = group.Mode };

        foreach (var data in group.Items)
        {
            items.Add(CreateItem(data));
        }

        set(items);
    }

    private static MuxControls.SwipeItem CreateItem(SwipeItemData data)
    {
        var item = new MuxControls.SwipeItem
        {
            Text = data.Text ?? string.Empty,
            BehaviorOnInvoked = data.BehaviorOnInvoked,
        };

        if (data.Icon is { } icon && IconSources.From(icon) is { } source)
        {
            item.IconSource = source;
        }

        if (data.Background is { } background)
        {
            item.Background = background;
        }

        if (data.Foreground is { } foreground)
        {
            item.Foreground = foreground;
        }

        if (data.OnInvoked is { } onInvoked)
        {
            item.Invoked += (_, _) => onInvoked();
        }

        return item;
    }
}

/// <summary>
/// WinUI 2 的 <c>ParallaxView</c>：参照另一处滚动的进度，把自己这片内容错开一点。
/// </summary>
/// <remarks>
/// <para>
/// <b>唯一的子槽位在 <c>Child</c> 上。</b>它继承 <c>FrameworkElement</c>，
/// 不是 <c>ContentControl</c>，所以在 <c>SingleChildAccessor</c> 里登记过之后
/// 才能走 <c>PatchSingleChild</c> 那条通用路径（不登记就每轮重建整棵子树，
/// 与 <c>Viewbox</c> 同形）。
/// </para>
/// <para>
/// <b><c>Source</c> 要等进了可视树才认得出。</b>它通常是<b>兄弟节点</b>上那个
/// <c>ScrollViewer</c>，而 <c>Mount</c> 那一刻父容器还没定（协调器是"先造、
/// 再加进父容器"），问不到 <c>Parent</c>、也就数不出兄弟。所以与
/// <c>TeachingTipHandler</c> 用同一招：把"同层第几个"记在
/// <see cref="Slots"/> 里，<c>Loaded</c> 时再换成真的兄弟控件；
/// <c>Update</c> 改了下标也能重新落一遍。
/// </para>
/// <para>
/// <b>这里一个 <c>EchoGuard</c> 都没有。</b>它上面能被用户改动的只有
/// "滚动到哪儿"，而那是 <c>Source</c> 的状态，不是它的；属性全是我们写、
/// 控件读，写下去不会冒成用户输入。
/// </para>
/// </remarks>
internal sealed class ParallaxViewHandler : ElementHandler<ParallaxViewElement, MuxControls.ParallaxView>
{
    private static readonly WeakTable<MuxControls.ParallaxView, RoutedEventHandler> Loadeds = new();

    /// <summary>最近一次下发的 <c>Source</c> 下标。</summary>
    private static readonly WeakTable<MuxControls.ParallaxView, int?> Slots = new();

    protected override MuxControls.ParallaxView Mount(Reconciler reconciler, ParallaxViewElement element)
    {
        var control = new MuxControls.ParallaxView();
        ApplyProps(control, null, element);

        if (element.Child is not null)
        {
            control.Child = reconciler.Build(element.Child);
        }

        Slots.Set(control, element.SourceIndex);

        // 一次性的：参照的那个兄弟要等进了可视树才数得出来（见类注释）。
        // Mount 每个实例只跑一次，Unmount 里配一次 -=。
        RoutedEventHandler loaded = (_, _) => ApplySource(control, Slots[control]);
        control.Loaded += loaded;
        Loadeds.Set(control, loaded);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        ParallaxViewElement oldElement,
        ParallaxViewElement newElement,
        MuxControls.ParallaxView control)
    {
        ApplyProps(control, oldElement, newElement);

        // 下标可能换了，也可能上一轮还没进树（那时这次才第一次问到 Parent）。
        ApplySource(control, newElement.SourceIndex);

        reconciler.PatchSingleChild(control, oldElement.Child, newElement.Child);
        Slots.Set(control, newElement.SourceIndex);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.ParallaxView control)
    {
        if (Loadeds[control] is { } loaded)
        {
            control.Loaded -= loaded;
            Loadeds.Remove(control);
        }

        Slots.Remove(control);
    }

    protected override Element? SingleChildOf(ParallaxViewElement element) => element.Child;

    /// <summary>
    /// 把"同层第几个"换成真正的兄弟控件。
    /// </summary>
    /// <remarks>
    /// 父容器还没确定就跳过——那时 <c>Parent</c> 是 null，<c>Loaded</c> 会再来一次。
    /// 下标越界给 null：官方收到 null 就是"没有参照"，于是它安静地不动，
    /// 比抛异常或随便挑一个强（与 <c>TeachingTipHandler.ApplyTarget</c> 同一条规矩）。
    /// </remarks>
    private static void ApplySource(MuxControls.ParallaxView control, int? index)
    {
        if (index is not { } i || control.Parent is not WuControls.Panel panel)
        {
            return;
        }

        control.Source = i >= 0 && i < panel.Children.Count ? panel.Children[i] : null;
    }

    private static void ApplyProps(
        MuxControls.ParallaxView control,
        ParallaxViewElement? oldElement,
        ParallaxViewElement newElement)
    {
        PropWriter.Set(
            oldElement?.VerticalShift ?? double.MinValue,
            newElement.VerticalShift,
            value => control.VerticalShift = value);
        PropWriter.Set(
            oldElement?.HorizontalShift ?? double.MinValue,
            newElement.HorizontalShift,
            value => control.HorizontalShift = value);
        PropWriter.Set(
            oldElement?.VerticalSourceOffsetKind ?? default,
            newElement.VerticalSourceOffsetKind,
            value => control.VerticalSourceOffsetKind = value);
        PropWriter.Set(
            oldElement?.HorizontalSourceOffsetKind ?? default,
            newElement.HorizontalSourceOffsetKind,
            value => control.HorizontalSourceOffsetKind = value);
        PropWriter.Set(
            oldElement?.MaxVerticalShiftRatio ?? double.MinValue,
            newElement.MaxVerticalShiftRatio,
            value => control.MaxVerticalShiftRatio = value);
        PropWriter.Set(
            oldElement?.MaxHorizontalShiftRatio ?? double.MinValue,
            newElement.MaxHorizontalShiftRatio,
            value => control.MaxHorizontalShiftRatio = value);
        PropWriter.Set(
            oldElement?.IsVerticalShiftClamped ?? default,
            newElement.IsVerticalShiftClamped,
            value => control.IsVerticalShiftClamped = value);
        PropWriter.Set(
            oldElement?.IsHorizontalShiftClamped ?? default,
            newElement.IsHorizontalShiftClamped,
            value => control.IsHorizontalShiftClamped = value);
    }
}
