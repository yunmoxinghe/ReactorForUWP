using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  枢轴（对应 UWP 原生的 Pivot / PivotItem）
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 枢轴页，对应 UWP 原生的 <c>PivotItem</c>：一个表头 + 一份内容。
/// </summary>
/// <remarks>
/// 与 <see cref="TabElement"/> 的关键差别是<b>这里带内容</b>。官方 <c>Pivot</c> 的
/// 卖点就是"内容跟着手势一起横向滑"，而这个效果的前提是每一页的内容<b>各自挂在
/// 自己的 <c>PivotItem.Content</c> 上</b>——若像 TabView 那样只在容器上留一份内容，
/// 滑动期间内容不会跟着动，做出来的就不是 Pivot 了。
/// <para>
/// <c>PivotItem</c> 继承 <c>ContentControl</c>，<c>Content</c> 是<b>内容槽</b>
/// 而不是集合，所以不存在"一个元素两个父"那件事（<see cref="TabViewElement"/>
/// 的注释里记着那道坑的形状）。代价是内容树有 N 份——Pivot 官方自己也只
/// realize 当前页与左右邻页，其余处于未加载状态，这与"每页一份描述"是对齐的。
/// </para>
/// </remarks>
public sealed record PivotItemElement(string? Header = null) : Element
{
    /// <summary>这一页的内容。</summary>
    public Element? Content { get; init; }
}

/// <summary>
/// 枢轴容器，对应 UWP 原生的 <c>Pivot</c>。
/// </summary>
/// <param name="Items">各页（表头 + 内容）。</param>
/// <param name="SelectedIndex">受控选中下标。<see cref="Optional{T}.Unset"/> = 非受控。</param>
/// <param name="OnSelectedIndexChanged">选中变化回调。</param>
/// <remarks>
/// <b>原生控件，不是自绘。</b>横向滑动手势、表头随手指滚动、<c>Ctrl+Tab</c> /
/// 左右方向键切页、只保留当前与相邻页内容这套卸载策略、<c>Pivot</c> 角色的
/// 自动化对等，全部由官方模板提供——这些恰恰是自己拼一个"页签条 + 内容区"
/// 做不出来的部分。
/// </remarks>
public sealed record PivotElement(
    IReadOnlyList<PivotItemElement> Items,
    Optional<int> SelectedIndex = default,
    Action<int>? OnSelectedIndexChanged = null) : Element
{
    /// <summary>
    /// 左上角那个大标题（官方 <c>Pivot.Title</c>）。
    /// </summary>
    /// <remarks>
    /// 官方类型是 <c>object</c>（可以放任意内容），这里只收字符串：
    /// 放进来的若是元素树，就得由协调器再管一棵与页面生命周期不同步的子树，
    /// 而标题这个位置实际上也只需要一行文字。
    /// </remarks>
    public string? Title { get; init; }
}
