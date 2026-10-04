// 虚拟化列表的两种实现，默认走 ItemsRepeater（官方那条路），原生桥不可用时退到自绘。
//
// 【为什么以前是自绘】官方 Reactor 用 ItemsRepeater + 自定义 IElementFactory；
// 但 WinUI 2 的 C# 投影把该接口设为 internal（CS0122），C# 侧实现不了，于是
// UWP 上改成了 ScrollViewer + Canvas 的自绘虚拟化。后来用 C++/WinRT 写了
// Reactor.Uwp.Native.dll 把接口补上（见 Internal/NativeElementFactory.cs 文件头），
// 压测页（UwpApp/__FactoryProbe.cs，M1 档）跑通了，这条路才真正可用——
// 现在默认就是它，自绘只在桥不可用时（x86 没有预编译产物）兜底。
//
// 【下标从哪来 —— 别用 Data 反查】WinUI 2.8 的 ElementFactoryGetArgs 只有
// Data / Parent，没有 WinUI 3 的 Index。所以喂给 ItemsSource 的是**下标字符串**
// （"0" "1" "2" …），不是数据本身：按下标反查是唯一可恢复的（下标天然不重复），
// 而按数据内容反查在重复数据项上信息论上不可恢复。
// 顺带一个好处：数据对象（可能是任意 POCO）根本不跨 ABI，只有字符串过桥。
//
// 【为什么在 bind 里建内容，不在 ElementPrepared 里】bind 是 GetElement 的回调，
// 此刻容器还没挂到 XAML 树上，往里面塞子树是安全的；ElementPrepared 则是
// ItemsRepeater 布局过程中触发的结构变更，重入风险高得多。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 等高项的虚拟化列表：默认 <c>ItemsRepeater</c> + 原生元素工厂，
/// 桥不可用时退化为 ScrollViewer + Canvas 自绘。
/// </summary>
/// <remarks>
/// <para>
/// <b>身份模型</b>：<c>ItemsRepeater</c> 那条路上没有 keyed diff（WinUI 2 的
/// ItemsRepeater 不做按 key 复用，官方 Reactor 同样如此），
/// <see cref="VirtualizingListElement.ItemKey"/> 只在自绘回退路径上起作用。
/// 两条路都能保证"已挂载项的内容不错位"，差别是插入时的复用粒度。
/// </para>
/// <para>
/// 与官方实现的差异：官方 <c>ElementFactory&lt;T&gt;</c> 有滚动锚定等能力，
/// 这里只做"视窗内挂载 + 出窗回收"，已挂载项的内容变化走正常的 <c>Patch</c>。
/// </para>
/// </remarks>
internal sealed class VirtualizingListHandler : ElementHandler<VirtualizingListElement, ScrollViewer>
{
    private enum ListMode
    {
        /// <summary>官方路径：ItemsRepeater + 原生元素工厂。</summary>
        Repeater,

        /// <summary>兜底：ScrollViewer + Canvas 自绘虚拟化（桥不可用）。</summary>
        Canvas,
    }

    /// <summary>ItemsRepeater 路径上一个容器的租约：它当前绑的下标 + 内容。</summary>
    private sealed class Slot
    {
        public int Index = -1;
        public Element Element = null!;
        public UIElement Native = null!;
    }

    /// <summary>自绘路径上一个已挂载的项。</summary>
    /// <remarks>
    /// 三层身份必须分开，混用就是 bug：
    /// <list type="bullet">
    /// <item><see cref="Key"/> —— React 身份，来自 <c>ItemKey</c> 选择器，跨渲染稳定。</item>
    /// <item><see cref="Index"/> —— 布局位置，会随插入/删除整体位移。</item>
    /// <item><see cref="Native"/> —— 物理控件，可复用。</item>
    /// </list>
    /// </remarks>
    private sealed class Mounted
    {
        public object Key = null!;
        public int Index;
        public Element Element = null!;
        public UIElement Native = null!;
    }

    private sealed class State
    {
        public Reconciler Reconciler = null!;
        public VirtualizingListElement Element = null!;
        public ListMode Mode;

        // ── ItemsRepeater 路径 ──
        public ItemsControl? Carrier;
        public MuxControls.ItemsRepeater? Repeater;
        public RecyclingElementFactory? Factory;

        /// <summary>容器 → 它当前的内容（含池里等待复用的那些）。</summary>
        public Dictionary<UIElement, Slot> Slots { get; } = new();

        /// <summary>上次上报过的容器数（诊断去重用）。</summary>
        public int LastReported = -1;

        // ── 自绘路径 ──
        public Canvas? Canvas;

        /// <summary>已挂载的项：item key → 挂载记录。</summary>
        public Dictionary<object, Mounted> ByKey { get; } = new();
    }

    private static readonly Dictionary<ScrollViewer, State> States = new();

    protected override ScrollViewer Mount(Reconciler reconciler, VirtualizingListElement element)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        if (element.Height is { } height)
        {
            scroll.Height = height;
        }

        var state = new State { Reconciler = reconciler, Element = element };
        States[scroll] = state;

        if (TryMountRepeater(scroll, state, element))
        {
            state.Mode = ListMode.Repeater;
            Reactor.Uwp.Hosting.ReactorApplication.Trace(
                $"[reactor] 虚拟化 mount: ItemsRepeater 路径，共 {element.Items?.Count ?? 0} 项，行高 {element.ItemHeight}");
        }
        else
        {
            state.Mode = ListMode.Canvas;
            var canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Stretch };
            scroll.Content = canvas;
            state.Canvas = canvas;

            // 首次布局完成时 Viewport 才有值；之后靠 ViewChanged / SizeChanged 维持。
            scroll.Loaded += (_, _) => Refresh(scroll);
            scroll.SizeChanged += (_, _) => Refresh(scroll);
            scroll.ViewChanged += (_, _) => Refresh(scroll);
        }

        Refresh(scroll);
        return scroll;
    }

    protected override void Update(
        Reconciler reconciler,
        VirtualizingListElement oldElement,
        VirtualizingListElement newElement,
        ScrollViewer control)
    {
        if (!States.TryGetValue(control, out var state))
        {
            return;
        }

        state.Reconciler = reconciler;
        state.Element = newElement;

        if (newElement.Height is { } height)
        {
            control.Height = height;
        }

        Refresh(control);
    }

    protected override void Unmount(Reconciler reconciler, ScrollViewer control)
    {
        if (!States.TryGetValue(control, out var state))
        {
            return;
        }

        if (state.Factory is { } factory)
        {
            factory.Dispose();
        }

        foreach (var slot in state.Slots.Values)
        {
            reconciler.UnmountNative(slot.Native, slot.Element);
        }

        state.Slots.Clear();

        foreach (var mounted in state.ByKey.Values)
        {
            reconciler.UnmountNative(mounted.Native, mounted.Element);
        }

        state.ByKey.Clear();
        States.Remove(control);
    }

    private static void Refresh(ScrollViewer scroll)
    {
        if (!States.TryGetValue(scroll, out var state))
        {
            return;
        }

        if (state.Mode == ListMode.Repeater)
        {
            RefreshRepeater(scroll, state);
        }
        else
        {
            RefreshCanvas(scroll, state);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ItemsRepeater（默认）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 建 ItemsRepeater 那条路。原生桥不可用时返回 false，调用方退到自绘。
    /// </summary>
    private static bool TryMountRepeater(
        ScrollViewer scroll, State state, VirtualizingListElement element)
    {
        try
        {
            // 向量只装下标字符串（见文件头：按下标反查唯一可恢复，且数据不跨 ABI）。
            // 用原生向量（ItemsControl.Items）而不是托管集合：后者在 AOT 下过不了
            // 布局期的 ItemsSourceView（CsWinRT 建的 CCW 被拒，fail-fast）。
            var carrier = new ItemsControl();
            SyncCarrier(carrier.Items, element);

            var factory = new RecyclingElementFactory(
                create: () => new Border { VerticalAlignment = VerticalAlignment.Top },
                bind: (container, data, parent) => Bind(state, container, data),
                maxPool: 512);

            var repeater = new MuxControls.ItemsRepeater
            {
                Layout = new MuxControls.StackLayout(),
                ItemsSource = carrier.Items,
                ItemTemplate = factory.ItemTemplate,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
            };

            scroll.Content = repeater;

            state.Carrier = carrier;
            state.Repeater = repeater;
            state.Factory = factory;
            return true;
        }
        catch (Exception ex)
        {
            // 桥不可用（x86 没有预编译产物 / dll 没落到 AppX）就是这条路。
            // 记一次日志就够：每轮渲染都刷会把日志淹掉。
            Reactor.Uwp.Hosting.ReactorApplication.Trace(
                $"[reactor] 虚拟化: ItemsRepeater 路径不可用，退回自绘 — [{ex.GetType().Name}] {ex.Message}");

            state.Carrier = null;
            state.Repeater = null;
            state.Factory = null;
            return false;
        }
    }

    private static void RefreshRepeater(ScrollViewer scroll, State state)
    {
        if (state.Carrier is not { } carrier)
        {
            return;
        }

        SyncCarrier(carrier.Items, state.Element);

        // 诊断：realize 出来的容器数（应远小于总项数）。只在数字变化时打印，
        // 否则每轮渲染都写一行会把日志淹掉。
        if (state.Slots.Count != state.LastReported)
        {
            state.LastReported = state.Slots.Count;
            Reactor.Uwp.Hosting.ReactorApplication.Trace(
                $"[reactor] 虚拟化 realize: 容器 {state.Slots.Count} / 总 {carrier.Items.Count} 项");
        }

        // 已挂载（含池里待复用）的容器要按新数据重绑：元素记录是值相等的 record，
        // 内容没变时 Patch 内部自己会短路，白跑一趟的代价只是几次比较。
        // 数量级 = 一次 realize 的行数 + 池深，几十个，可以接受。
        foreach (var pair in state.Slots.ToArray())
        {
            if (pair.Value.Index < 0)
            {
                continue;
            }

            Bind(state, pair.Key, pair.Value.Index.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// 同步下标向量：只在尾部增删，内容（下标字符串）本身永不变。
    /// 改集合会触发 <c>IObservableVector</c> 通知，ItemsRepeater 自己增删条目。
    /// </summary>
    private static void SyncCarrier(ItemCollection target, VirtualizingListElement element)
    {
        var count = element.Items?.Count ?? 0;

        while (target.Count > count)
        {
            target.RemoveAt(target.Count - 1);
        }

        while (target.Count < count)
        {
            target.Add(target.Count.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// 把第 <paramref name="data"/>（下标字符串）项的内容填进容器。
    /// 新建与复用都走这里：复用时是 <c>Patch</c>，不是重建。
    /// </summary>
    private static void Bind(State state, UIElement container, object? data)
    {
        var index = ParseIndex(data);
        var items = state.Element.Items ?? Array.Empty<object?>();
        if (index < 0 || index >= items.Count)
        {
            return;
        }

        var itemHeight = state.Element.ItemHeight > 0 ? state.Element.ItemHeight : 32;
        var next = state.Element.ItemTemplate(items[index], index);

        if (container is FrameworkElement frame)
        {
            frame.Height = itemHeight;
            frame.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        if (state.Slots.TryGetValue(container, out var slot))
        {
            slot.Index = index;

            if (!Equals(slot.Element, next))
            {
                state.Reconciler.Patch(slot.Native, slot.Element, next);
                slot.Element = next;
            }

            return;
        }

        var native = state.Reconciler.Build(next);
        if (native is FrameworkElement sized)
        {
            sized.Height = itemHeight;
            sized.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        if (container is Border border)
        {
            border.Child = native;
        }

        state.Slots[container] = new Slot { Index = index, Element = next, Native = native };
    }

    private static int ParseIndex(object? data) =>
        data is string text &&
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index
            : -1;

    // ══════════════════════════════════════════════════════════════════
    //  自绘兜底（原生桥不可用时）
    // ══════════════════════════════════════════════════════════════════

    private static void RefreshCanvas(ScrollViewer scroll, State state)
    {
        if (state.Canvas is not { } canvas)
        {
            return;
        }

        var element = state.Element;
        var items = element.Items ?? Array.Empty<object?>();
        var count = items.Count;
        var itemHeight = element.ItemHeight > 0 ? element.ItemHeight : 32;
        var buffer = element.Buffer < 0 ? 0 : element.Buffer;

        // 占位高度决定滚动范围；宽度跟随视口，供横向拉伸的子元素使用。
        var viewportWidth = scroll.ViewportWidth > 0 ? scroll.ViewportWidth : scroll.ActualWidth;
        var viewportHeight = scroll.ViewportHeight > 0 ? scroll.ViewportHeight : scroll.ActualHeight;

        canvas.Height = count * itemHeight;
        if (viewportWidth > 0)
        {
            canvas.Width = viewportWidth;
        }

        var top = scroll.VerticalOffset;
        var first = Math.Max(0, (int)Math.Floor(top / itemHeight) - buffer);
        var last = Math.Min(count - 1, (int)Math.Ceiling((top + viewportHeight) / itemHeight) + buffer);

        // 1) 先确定视窗内每个下标的身份。
        //    key 由 ItemKey 选择器给出，与下标无关，因此插入/删除时已挂载项仍能被认出来。
        var keyOfIndex = VirtualKeys.Resolve(items, first, last, element.ItemKey);

        // 2) 卸载滚出视窗、或已从数据源中消失的项
        var keep = new HashSet<object>(keyOfIndex.Values);
        var stale = state.ByKey.Keys.Where(k => !keep.Contains(k)).ToList();
        foreach (var key in stale)
        {
            var mounted = state.ByKey[key];
            state.Reconciler.UnmountNative(mounted.Native, mounted.Element);
            canvas.Children.Remove(mounted.Native);
            state.ByKey.Remove(key);
        }

        // 3) 挂载/更新视窗内的项
        for (var i = first; i <= last; i++)
        {
            var key = keyOfIndex[i];
            var nextElement = element.ItemTemplate(items[i], i);

            if (state.ByKey.TryGetValue(key, out var mounted))
            {
                // 下标位移只挪位置，不重建控件：这正是「key ≠ index」要的效果。
                if (mounted.Index != i)
                {
                    Canvas.SetTop(mounted.Native, i * itemHeight);
                    mounted.Index = i;
                }

                if (!Equals(mounted.Element, nextElement))
                {
                    state.Reconciler.Patch(mounted.Native, mounted.Element, nextElement);
                    mounted.Element = nextElement;
                }

                continue;
            }

            var native = state.Reconciler.Build(nextElement);
            if (native is FrameworkElement sized)
            {
                sized.Height = itemHeight;
                if (viewportWidth > 0)
                {
                    sized.Width = viewportWidth;
                }

                sized.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            Canvas.SetTop(native, i * itemHeight);
            canvas.Children.Add(native);
            state.ByKey[key] = new Mounted { Key = key, Index = i, Element = nextElement, Native = native };
        }

        // 4) 视口宽度变化时把已有项一起拉宽（Canvas 内的子元素不会自动拉伸）
        if (viewportWidth > 0)
        {
            foreach (var mounted in state.ByKey.Values)
            {
                if (mounted.Native is FrameworkElement fe && Math.Abs(fe.Width - viewportWidth) > 0.5)
                {
                    fe.Width = viewportWidth;
                }
            }
        }
    }

    /// <summary>诊断用：当前实际挂着的项数（应远小于总项数）。</summary>
    internal static int RealizedCount(ScrollViewer scroll)
    {
        if (!States.TryGetValue(scroll, out var state))
        {
            return 0;
        }

        return state.Mode == ListMode.Repeater ? state.Slots.Count : state.ByKey.Count;
    }

    /// <summary>诊断用：走的是哪条路（"ItemsRepeater" / "Canvas 兜底"）。</summary>
    internal static string ModeName(ScrollViewer scroll) =>
        States.TryGetValue(scroll, out var state)
            ? state.Mode == ListMode.Repeater ? "ItemsRepeater" : "Canvas 兜底"
            : "未挂载";
}
