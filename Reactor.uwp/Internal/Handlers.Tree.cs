using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// WinUI 2 的 <c>TreeView</c>：节点是一棵 <c>TreeViewNode</c> 的数据树，不是元素树。
/// </summary>
/// <remarks>
/// <para>
/// <b>本类不接判据簇，也不持 <c>EchoGuard</c>——因为这里一个受控槽位都没有。</b>
/// WinUI 2 的 <c>TreeView.SelectedItem</c> / <c>SelectedNode</c> 都只有 getter，
/// <c>TreeViewNode</c> 也没有 <c>IsSelected</c>；"选中第 N 项"根本写不进去。
/// 没有写入就没有回声，"受控"也就无从兑现——<b>不装</b>是这里唯一诚实的做法。
/// 选中只通过 <c>ItemInvoked</c> 往外报（<c>TreeView</c> 没有
/// <c>SelectionChanged</c> 事件，这是官方给的唯一出口）。
/// </para>
/// <para>
/// <b>节点树与 <c>SelectionMode</c> 都在挂载期写。</b>它们动的是"整棵树怎么摆"，
/// 而这一族连一个可写的选中槽位都没有，于是"边跑边改"唯一能兑现的代价是
/// <b>展开态归零</b>（节点一重建，用户手动展开的那些就回到声明值）。
/// 与 <c>CalendarView</c> 那份"配置只在挂载时写"是同一个理由、同一种处理：
/// 运行中要换结构就换 <c>key</c> 让控件重建。
/// </para>
/// <para>
/// <b>节点内容是字符串</b>（<see cref="TreeNodeElement.Text"/>）。
/// <c>TreeViewNode.Content</c> 收的是 <c>object</c>，塞 <c>UIElement</c> 也能显示，
/// 但那些元素就住在协调器视野之外——没人替它们跑 cleanup。所以这条路不提供。
/// </para>
/// </remarks>
internal sealed class TreeViewHandler : ElementHandler<TreeViewElement, MuxControls.TreeView>
{
    private static readonly WeakTable<MuxControls.TreeView, TreeCallbacks> Callbacks = new();

    protected override MuxControls.TreeView Mount(Reconciler reconciler, TreeViewElement element)
    {
        var control = new MuxControls.TreeView { SelectionMode = element.SelectionMode };

        foreach (var node in element.Nodes)
        {
            control.RootNodes.Add(BuildNode(node));
        }

        Rebind(control, element);

        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        TreeViewElement oldElement,
        TreeViewElement newElement,
        MuxControls.TreeView control)
    {
        // 节点树与 SelectionMode 都不在这里写（见类注释）：这里只换回调。
        Rebind(control, newElement);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.TreeView control)
    {
        Callbacks.Remove(control);
    }

    /// <summary>把一个节点（连同它的子树）物化成 <c>TreeViewNode</c>。</summary>
    private static MuxControls.TreeViewNode BuildNode(TreeNodeElement element)
    {
        var node = new MuxControls.TreeViewNode
        {
            Content = element.Text,
            IsExpanded = element.Expanded,
        };

        foreach (var child in element.Children)
        {
            node.Children.Add(BuildNode(child));
        }

        return node;
    }

    private static void Rebind(MuxControls.TreeView control, TreeViewElement element)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks.Set(control, new TreeCallbacks());

            control.ItemInvoked += (s, args) =>
            {
                if (s is MuxControls.TreeView tree && Callbacks.TryGetValue(tree, out var box))
                {
                    // 节点的 Content 就是我们放进去的那个 Text，所以能直接对回来。
                    box.ItemInvoked?.Invoke(args.InvokedItem as string);
                }
            };

            control.Expanding += (s, args) =>
            {
                if (s is MuxControls.TreeView tree && Callbacks.TryGetValue(tree, out var box))
                {
                    box.Expanding?.Invoke(args.Node?.Content as string);
                }
            };

            control.Collapsed += (s, args) =>
            {
                if (s is MuxControls.TreeView tree && Callbacks.TryGetValue(tree, out var box))
                {
                    box.Collapsed?.Invoke(args.Node?.Content as string);
                }
            };
        }

        // 整只盒子换掉（理由同 <c>SelectorHandler</c>：取出来改字段，在源码扫描里
        // 会长成一处属性写入，而它根本不是控件属性）。
        Callbacks.Set(control, new TreeCallbacks
        {
            ItemInvoked = element.OnItemInvoked,
            Expanding = element.OnNodeExpanding,
            Collapsed = element.OnNodeCollapsed,
        });
    }

    /// <summary>三个通知通道的存放盒（引用类型：三个都可能同时是 null）。</summary>
    private sealed class TreeCallbacks
    {
        public Action<string?>? ItemInvoked { get; init; }
        public Action<string?>? Expanding { get; init; }
        public Action<string?>? Collapsed { get; init; }
    }
}
