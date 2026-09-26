using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 等高项的虚拟化列表：ScrollViewer + Canvas 占位 + 按滚动位置挂载/卸载视窗内的项。
/// </summary>
/// <remarks>
/// 官方 Reactor 走 <c>ItemsRepeater</c> + 自定义 <c>IElementFactory</c>；
/// 但 WinUI <b>2</b> 的 C# 投影把该接口设为 internal（CS0122），UWP 侧无法自行实现，
/// 而 ItemsRepeater 的自定义工厂必须走这个接口——所以在 UWP 上改为自建虚拟化。
/// <para>
/// 与官方实现的差异（有意简化）：官方 <c>ElementFactory&lt;T&gt;</c> 有回收池、
/// 滚动锚定、keyed diff（700+ 行）；这里只做"视窗内挂载 + 出窗卸载"，
/// 已挂载项的内容变化走正常的 <c>Patch</c> 路径。
/// </para>
/// </remarks>
internal sealed class VirtualizingListHandler : ElementHandler<VirtualizingListElement, ScrollViewer>
{
    private sealed class State
    {
        public Reconciler Reconciler = null!;
        public Canvas Canvas = null!;
        public VirtualizingListElement Element = null!;

        /// <summary>已挂载的项：下标 → (元素描述, 真实控件)。</summary>
        public Dictionary<int, (Element Element, UIElement Native)> Realized { get; } = new();
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

        var canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Stretch };
        scroll.Content = canvas;

        var state = new State { Reconciler = reconciler, Canvas = canvas, Element = element };
        States[scroll] = state;

        // 首次布局完成时 Viewport 才有值；之后靠 ViewChanged / SizeChanged 维持。
        scroll.Loaded += (_, _) => Refresh(scroll);
        scroll.SizeChanged += (_, _) => Refresh(scroll);
        scroll.ViewChanged += (_, _) => Refresh(scroll);

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

        foreach (var pair in state.Realized.Values)
        {
            reconciler.UnmountNative(pair.Native, pair.Element);
        }

        state.Realized.Clear();
        States.Remove(control);
    }

    private static void Refresh(ScrollViewer scroll)
    {
        if (!States.TryGetValue(scroll, out var state))
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

        state.Canvas.Height = count * itemHeight;
        if (viewportWidth > 0)
        {
            state.Canvas.Width = viewportWidth;
        }

        var top = scroll.VerticalOffset;
        var first = Math.Max(0, (int)Math.Floor(top / itemHeight) - buffer);
        var last = Math.Min(count - 1, (int)Math.Ceiling((top + viewportHeight) / itemHeight) + buffer);

        // 1) 卸载滚出视窗（或已不存在）的项
        var stale = state.Realized.Keys.Where(i => i < first || i > last || i >= count).ToList();
        foreach (var index in stale)
        {
            var pair = state.Realized[index];
            state.Reconciler.UnmountNative(pair.Native, pair.Element);
            state.Canvas.Children.Remove(pair.Native);
            state.Realized.Remove(index);
        }

        // 2) 挂载/更新视窗内的项
        for (var i = first; i <= last; i++)
        {
            var nextElement = element.ItemTemplate(items[i], i);

            if (state.Realized.TryGetValue(i, out var existing))
            {
                if (!Equals(existing.Element, nextElement))
                {
                    state.Reconciler.Patch(existing.Native, existing.Element, nextElement);
                    state.Realized[i] = (nextElement, existing.Native);
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
            state.Canvas.Children.Add(native);
            state.Realized[i] = (nextElement, native);
        }

        // 3) 视口宽度变化时把已有项一起拉宽（Canvas 内的子元素不会自动拉伸）
        if (viewportWidth > 0)
        {
            foreach (var pair in state.Realized.Values)
            {
                if (pair.Native is FrameworkElement fe && Math.Abs(fe.Width - viewportWidth) > 0.5)
                {
                    fe.Width = viewportWidth;
                }
            }
        }
    }

    /// <summary>诊断用：当前实际挂载的项数（应远小于总项数）。</summary>
    internal static int RealizedCount(ScrollViewer scroll) =>
        States.TryGetValue(scroll, out var state) ? state.Realized.Count : 0;
}
