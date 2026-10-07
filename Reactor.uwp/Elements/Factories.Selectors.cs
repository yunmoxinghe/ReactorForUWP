using System;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    // ── ListBox ───────────────────────────────────────────────

    /// <summary>列表框：只给内容。</summary>
    public static ListBoxElement ListBox(params Element?[] items) =>
        new(FilterChildren(items));

    /// <summary>列表框：受控选中下标。</summary>
    public static ListBoxElement ListBox(
        Optional<int> selectedIndex,
        Action<int>? onSelectedIndexChanged,
        params Element?[] items) =>
        new(FilterChildren(items))
        {
            SelectedIndex = selectedIndex,
            OnSelectedIndexChanged = onSelectedIndexChanged,
        };

    /// <summary>
    /// 列表框：带 <see cref="SelectionMode"/> 的重载。改模式会把选中态一起牵动，
    /// 这个参数让示例能直接取证那一发（与 <c>ListView</c> 那条同形）。
    /// </summary>
    public static ListBoxElement ListBox(
        Optional<int> selectedIndex,
        Action<int>? onSelectedIndexChanged,
        SelectionMode selectionMode,
        params Element?[] items) =>
        new(FilterChildren(items))
        {
            SelectedIndex = selectedIndex,
            OnSelectedIndexChanged = onSelectedIndexChanged,
            SelectionMode = selectionMode,
        };

    // ── FlipView ──────────────────────────────────────────────

    /// <summary>翻页视图：只给页面。</summary>
    public static FlipViewElement FlipView(params Element?[] items) =>
        new(FilterChildren(items));

    /// <summary>翻页视图：受控当前页。</summary>
    public static FlipViewElement FlipView(
        Optional<int> selectedIndex,
        Action<int>? onSelectedIndexChanged,
        params Element?[] items) =>
        new(FilterChildren(items))
        {
            SelectedIndex = selectedIndex,
            OnSelectedIndexChanged = onSelectedIndexChanged,
        };
}
