using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 页签容器的工厂方法（<see cref="Factories"/> 的 partial 延续）。
/// 方法名沿用控件名，与官方 Dsl 的命名习惯一致。
/// </summary>
public static partial class Factories
{
    /// <summary>一个页签项。</summary>
    public static TabElement Tab(
        string? header = null,
        Element? headerIcon = null,
        bool isClosable = false) =>
        new(header)
        {
            HeaderIcon = headerIcon,
            IsClosable = isClosable,
        };

    /// <summary>页签容器：页签条 + 单个内容区。</summary>
    public static TabViewElement TabView(
        IReadOnlyList<TabElement> tabs,
        Optional<int> selectedIndex = default,
        Action<int>? onSelectedIndexChanged = null,
        Element? content = null,
        bool isAddTabButtonVisible = false,
        Action? onAddTabClick = null,
        MuxControls.TabViewWidthMode tabWidthMode = MuxControls.TabViewWidthMode.Equal,
        MuxControls.TabViewCloseButtonOverlayMode closeButtonOverlayMode =
            MuxControls.TabViewCloseButtonOverlayMode.Auto) =>
        new(tabs, selectedIndex, onSelectedIndexChanged)
        {
            Content = content,
            IsAddTabButtonVisible = isAddTabButtonVisible,
            OnAddTabClick = onAddTabClick,
            TabWidthMode = tabWidthMode,
            CloseButtonOverlayMode = closeButtonOverlayMode,
        };

    /// <summary>多个页签的便捷重载（选中与控制参数在前、页签在最后，便于尾随列举）。</summary>
    public static TabViewElement TabView(
        Optional<int> selectedIndex,
        Action<int>? onSelectedIndexChanged,
        Element? content,
        params TabElement[] tabs) =>
        TabView(tabs, selectedIndex, onSelectedIndexChanged, content);

    /// <summary>枢轴的一页：表头 + 这一页自己的内容。</summary>
    public static PivotItemElement PivotItem(string? header = null, Element? content = null) =>
        new(header) { Content = content };

    /// <summary>枢轴容器：横向滑动切换的若干页，每页各自持有内容。</summary>
    public static PivotElement Pivot(
        IReadOnlyList<PivotItemElement> items,
        Optional<int> selectedIndex = default,
        Action<int>? onSelectedIndexChanged = null,
        string? title = null) =>
        new(items, selectedIndex, onSelectedIndexChanged) { Title = title };

    /// <summary>多页的便捷重载（选中与控制参数在前、页在最后，便于尾随列举）。</summary>
    public static PivotElement Pivot(
        Optional<int> selectedIndex,
        Action<int>? onSelectedIndexChanged,
        params PivotItemElement[] items) =>
        Pivot(items, selectedIndex, onSelectedIndexChanged);
}
