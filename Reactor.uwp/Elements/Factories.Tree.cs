using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    // ── 树节点 ────────────────────────────────────────────────

    /// <summary>
    /// 树上的一个节点。<paramref name="expanded"/> 只是**初始**是否展开
    /// （见 <see cref="TreeNodeElement.Expanded"/>），之后用户怎么点是用户的事。
    /// </summary>
    public static TreeNodeElement TreeNode(
        string text,
        bool expanded = false,
        params TreeNodeElement[] children) =>
        new(text)
        {
            Expanded = expanded,
            Children = children is null ? Array.Empty<TreeNodeElement>() : children,
        };

    // ── TreeView ──────────────────────────────────────────────

    /// <summary>树：只给节点（选中不写回，见 <see cref="TreeViewElement"/> 的说明）。</summary>
    public static TreeViewElement TreeView(params TreeNodeElement[] nodes) =>
        new(Nodes(nodes));

    /// <summary>
    /// 树：带"项被执行"回调。这是<b>唯一</b>的选中出口——<c>TreeView</c> 没有
    /// <c>SelectionChanged</c> 事件，<c>ItemInvoked</c> 是官方给的那一个。
    /// </summary>
    public static TreeViewElement TreeView(
        Action<string?>? onItemInvoked,
        params TreeNodeElement[] nodes) =>
        new(Nodes(nodes)) { OnItemInvoked = onItemInvoked };

    /// <summary>树：同时接"展开 / 折叠"两个通知（两者都不可写，只是通知）。</summary>
    public static TreeViewElement TreeView(
        Action<string?>? onItemInvoked,
        Action<string?>? onNodeExpanding,
        Action<string?>? onNodeCollapsed,
        params TreeNodeElement[] nodes) =>
        new(Nodes(nodes))
        {
            OnItemInvoked = onItemInvoked,
            OnNodeExpanding = onNodeExpanding,
            OnNodeCollapsed = onNodeCollapsed,
        };

    /// <summary>树：指定选中模式（<c>Single</c> / <c>Multiple</c> / <c>None</c>）。</summary>
    public static TreeViewElement TreeView(
        MuxControls.TreeViewSelectionMode selectionMode,
        Action<string?>? onItemInvoked,
        params TreeNodeElement[] nodes) =>
        new(Nodes(nodes))
        {
            SelectionMode = selectionMode,
            OnItemInvoked = onItemInvoked,
        };

    private static IReadOnlyList<TreeNodeElement> Nodes(TreeNodeElement[]? nodes) =>
        nodes is null ? Array.Empty<TreeNodeElement>() : nodes;
}
