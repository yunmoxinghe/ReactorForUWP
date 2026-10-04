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
/// <para>
/// <b>身份模型（2026-10 修正）</b>：已挂载项以 <b>item key</b> 为主键，下标只当布局位置。
/// 早先按下标存 <c>Dictionary&lt;int, ...&gt;</c>，数据源在头部插入一项时下标整体位移，
/// 已挂载的项会全部拿错内容——不是「虚拟化细节 bug」，而是身份模型错了。
/// 现在插入/删除只挪位置（<c>Canvas.SetTop</c>）+ 内容 Patch，控件复用。
/// <c>ItemKey</c> 为 null 时退化为下标身份，行为与修正前一致。
/// </para>
/// </remarks>
internal sealed class VirtualizingListHandler : ElementHandler<VirtualizingListElement, ScrollViewer>
{
    /// <summary>一个已挂载的项：物理控件 + 它当前绑定的身份与位置。</summary>
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
        public Canvas Canvas = null!;
        public VirtualizingListElement Element = null!;

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
            state.Canvas.Children.Remove(mounted.Native);
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
            state.Canvas.Children.Add(native);
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

    /// <summary>诊断用：当前实际挂载的项数（应远小于总项数）。</summary>
    internal static int RealizedCount(ScrollViewer scroll) =>
        States.TryGetValue(scroll, out var state) ? state.ByKey.Count : 0;
}
