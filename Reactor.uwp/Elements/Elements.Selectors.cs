using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  Selector 一族：ListBox 与 FlipView
//
//  这两家与 ListView / GridView 是同一条选中契约（Selector.SelectedIndex），
//  但基类不同：ListView / GridView 是 ListViewBase（带 Header、IsItemClickEnabled、
//  以及另一套 ListViewSelectionMode 枚举），ListBox / FlipView 只是 Selector。
//  所以这里另起一个 handler 基类（Internal/Handlers.Selectors.cs），
//  不去改 ItemsViewHandler 那条已经验过的路。
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 列表框，对应 <see cref="ListBox"/>。
/// </summary>
/// <remarks>
/// 与 <c>ListView</c> 的差别不在"长什么样"而在**基类**：<c>ListBox</c> 是
/// <c>Selector</c> 的直接派生，没有 <c>Header</c>、没有 <c>IsItemClickEnabled</c>
/// （它压根没有 <c>ItemClick</c> 事件——选一项就是选一项，点一下就是选一下），
/// 选中模式用的是 <see cref="SelectionMode"/> 而不是 <c>ListViewSelectionMode</c>。
/// 官方 Gallery 里这一页正是拿它演示"同一份数据，选中语义三档各不相同"。
/// </remarks>
public sealed record ListBoxElement(IReadOnlyList<Element?> Items) : Element
{
    /// <summary>选中项下标。默认 Unset；<c>Optional&lt;int&gt;.Of(-1)</c> 表示清空选择。</summary>
    public Optional<int> SelectedIndex { get; init; } = default;

    public Action<int>? OnSelectedIndexChanged { get; init; }

    /// <summary>
    /// 选中模式（<see cref="SelectionMode"/>：<c>Single</c> / <c>Multiple</c> /
    /// <c>Extended</c>）。改它会把已有选中态一起牵动，与 <c>ListView</c> 那条同形。
    /// </summary>
    public SelectionMode SelectionMode { get; init; } = SelectionMode.Single;
}

/// <summary>
/// 翻页视图，对应 <see cref="FlipView"/>：一次只显示一项，左右翻。
/// </summary>
/// <remarks>
/// 官方 <c>FlipView</c> 是 <c>Selector</c> 的派生，所以"当前第几页"就是
/// <c>SelectedIndex</c>——这一点和 <c>ListBox</c> 是同一条通道，
/// 也就意味着它<b>有回声问题</b>（写回会同步抛 <c>SelectionChanged</c>）。
/// 它<b>没有</b> <c>SelectionMode</c>：一次只能选一项是它自己定的，不由调用方调。
/// </remarks>
public sealed record FlipViewElement(IReadOnlyList<Element?> Items) : Element
{
    /// <summary>当前页下标。默认 Unset；<c>Optional&lt;int&gt;.Of(-1)</c> 表示不选中。</summary>
    public Optional<int> SelectedIndex { get; init; } = default;

    public Action<int>? OnSelectedIndexChanged { get; init; }
}
