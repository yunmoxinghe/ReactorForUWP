using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  树：TreeView（WinUI 2 真控件）
//
//  这一族与别处最大的不同：**它的数据结构不是元素树**。
//  TreeView 拿的是 TreeViewNode 的树（每个节点一个 Content 对象 + Children），
//  不是 ItemsControl 那种"N 个子元素"。所以节点的描述类型 TreeNodeElement
//  不继承 Element —— 它不是 UI 节点描述，是**数据**。
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 树上的一个节点。<b>它不是 <see cref="Element"/></b>：它是喂给
/// <c>TreeView</c> 的一条数据，不是 UI 节点描述。
/// </summary>
/// <remarks>
/// <para>
/// 节点内容这里只收 <see cref="Text"/>（一段字符串）。<c>TreeViewNode.Content</c>
/// 收的是 <c>object</c>，塞 <c>UIElement</c> 也能显示，但节点树是<b>就地物化</b>的
/// （见 <c>TreeViewHandler</c>）：真塞一棵元素子树进去，那棵子树就住在协调器视野
/// 之外，卸载时没人替它跑 cleanup。所以这里不提供那条路——要富内容就走
/// <c>ItemsSource</c> + <c>ItemTemplate</c>，那是官方给的路，本版没包。
/// </para>
/// </remarks>
public sealed record TreeNodeElement(string Text)
{
    /// <summary>初始是否展开。<b>只在建树时生效</b>——之后用户怎么点是用户的事。</summary>
    public bool Expanded { get; init; }

    public IReadOnlyList<TreeNodeElement> Children { get; init; } = Array.Empty<TreeNodeElement>();
}

/// <summary>
/// 树形视图，对应 WinUI 2 的 <c>TreeView</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>选中是本版唯一"结构上就无法受控"的槽位，所以这里不假装受控。</b>
/// WinUI 2 的 <c>TreeView.SelectedItem</c> 与 <c>TreeView.SelectedNode</c> 都
/// <b>只有 getter</b>（对照 <c>Microsoft.UI.Xaml.xml</c>：这两个名字没有配套的
/// <c>...Property</c> 依赖属性条目，而 <c>SelectionMode</c> 有），
/// <c>TreeViewNode</c> 也没有 <c>IsSelected</c>。也就是说：**没有任何一个可编程的
/// 入口能把"选中第 N 项"写进去**。既然写不进去，"受控"就没有落点；
/// 而"受控"这件事一旦装出来，代价是静默的——回调不来，谁也不知道。
/// 于是这里的做法是：选中<b>只出不进</b>，由 <see cref="OnItemInvoked"/> 往外报
/// （<c>TreeView</c> 没有 <c>SelectionChanged</c> 事件，<c>ItemInvoked</c> 是唯一的
/// 出口）——与 <c>CalendarView</c> 那份"活集合只出不进"是同一条规矩。
/// </para>
/// <para>
/// <b>节点树在挂载时物化。</b>改 <c>SelectionMode</c> 会牵动选中，改节点结构更是
/// 整棵树重来；既然选中本来就不可写，那"边跑边改结构"唯一能兑现的代价是
/// <b>展开态归零</b>。所以这里不建增量补丁：运行中要换结构就换一个 <c>key</c>
/// 让控件重建（与 <c>CalendarView</c> 那份"配置只在挂载时写"同一个理由）。
/// </para>
/// </remarks>
public sealed record TreeViewElement(IReadOnlyList<TreeNodeElement> Nodes) : Element
{
    /// <summary>选中模式：<c>Single</c> / <c>Multiple</c> / <c>None</c>。</summary>
    public MuxControls.TreeViewSelectionMode SelectionMode { get; init; } =
        MuxControls.TreeViewSelectionMode.Single;

    /// <summary>某一项被"执行"（点击或回车）。参数是被执行节点的 <see cref="TreeNodeElement.Text"/>。</summary>
    public Action<string?>? OnItemInvoked { get; init; }

    /// <summary>某个节点即将展开。参数是该节点的文本——展开<b>不可写</b>，这里只是通知。</summary>
    public Action<string?>? OnNodeExpanding { get; init; }

    /// <summary>某个节点已经折叠。参数是该节点的文本。</summary>
    public Action<string?>? OnNodeCollapsed { get; init; }
}
