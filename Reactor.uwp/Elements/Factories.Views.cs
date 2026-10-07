using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Media;
using WuControls = Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    // ── 视图切换与滑动 ───────────────────────────────────────────

    /// <summary>
    /// 语义缩放：同一批数据的"细看"与"总览"两种视图。
    /// </summary>
    /// <param name="zoomedInView">细看那一侧（必须是 <c>ListView</c> / <c>GridView</c>，见 <see cref="SemanticZoomElement"/>）。</param>
    /// <param name="zoomedOutView">总览那一侧（同上）。</param>
    /// <param name="isZoomedInViewActive">停在"细看"那一侧吗（<b>受控</b>：给了值才受控，null = 不管它）。</param>
    /// <param name="canChangeViews">能不能换视图。</param>
    /// <param name="isZoomOutButtonEnabled">要不要显示那个缩小键。</param>
    /// <param name="onIsZoomedInViewActiveChanged">切换完成了（参数是新的 <c>IsZoomedInViewActive</c>）。</param>
    public static SemanticZoomElement SemanticZoom(
        Element? zoomedInView = null,
        Element? zoomedOutView = null,
        bool? isZoomedInViewActive = null,
        bool canChangeViews = true,
        bool isZoomOutButtonEnabled = true,
        Action<bool>? onIsZoomedInViewActiveChanged = null) =>
        new(zoomedInView, zoomedOutView)
        {
            IsZoomedInViewActive = isZoomedInViewActive,
            CanChangeViews = canChangeViews,
            IsZoomOutButtonEnabled = isZoomOutButtonEnabled,
            OnIsZoomedInViewActiveChanged = onIsZoomedInViewActiveChanged,
        };

    /// <summary>
    /// 分页指示器：一排小点 + 前后翻页两个按钮。<b>它自己不装内容</b>。
    /// </summary>
    /// <param name="numberOfPages">一共几页（-1 = 官方默认的"不限"）。</param>
    /// <param name="maxVisiblePips">一次最多画几个点。</param>
    /// <param name="orientation">横排还是竖排。</param>
    /// <param name="previousButtonVisibility">上一页按钮什么时候出现。</param>
    /// <param name="nextButtonVisibility">下一页按钮什么时候出现。</param>
    /// <param name="selectedPageIndex">当前第几页（<b>受控</b>：给了值才受控，null = 不管它）。</param>
    /// <param name="onSelectedPageIndexChanged">翻页了（参数是新的 <c>SelectedPageIndex</c>）。</param>
    public static PipsPagerElement PipsPager(
        int numberOfPages = -1,
        int maxVisiblePips = 5,
        WuControls.Orientation orientation = WuControls.Orientation.Horizontal,
        MuxControls.PipsPagerButtonVisibility previousButtonVisibility =
            MuxControls.PipsPagerButtonVisibility.Visible,
        MuxControls.PipsPagerButtonVisibility nextButtonVisibility =
            MuxControls.PipsPagerButtonVisibility.Visible,
        int? selectedPageIndex = null,
        Action<int>? onSelectedPageIndexChanged = null) =>
        new()
        {
            NumberOfPages = numberOfPages,
            MaxVisiblePips = maxVisiblePips,
            Orientation = orientation,
            PreviousButtonVisibility = previousButtonVisibility,
            NextButtonVisibility = nextButtonVisibility,
            SelectedPageIndex = selectedPageIndex,
            OnSelectedPageIndexChanged = onSelectedPageIndexChanged,
        };

    /// <summary>
    /// 滑动容器：内容上轻轻一滑，从边上露出几条命令。
    /// </summary>
    /// <param name="content">被滑动的那片内容。</param>
    /// <param name="left">从左往右滑露出的命令（<see cref="SwipeItems"/>）。</param>
    /// <param name="right">从右往左滑露出的命令。</param>
    /// <param name="top">从上往下滑露出的命令。</param>
    /// <param name="bottom">从下往上滑露出的命令。</param>
    public static SwipeControlElement SwipeControl(
        Element? content = null,
        SwipeItemsData? left = null,
        SwipeItemsData? right = null,
        SwipeItemsData? top = null,
        SwipeItemsData? bottom = null) =>
        new()
        {
            Content = content,
            Left = left,
            Right = right,
            Top = top,
            Bottom = bottom,
        };

    /// <summary>
    /// 滑动命令的<b>一组</b>：滑到头直接执行（<c>Execute</c>）还是只露出来（<c>Reveal</c>）。
    /// </summary>
    /// <remarks>
    /// <c>Mode</c> 长在这<b>一组</b>上，不长在单个项上（官方 <c>SwipeItems.Mode</c>）——
    /// 所以它走这个工厂的参数，不是 <see cref="SwipeItem"/> 的参数。
    /// </remarks>
    /// <param name="mode">滑到头的含义。</param>
    /// <param name="items">这一组里的命令项。</param>
    public static SwipeItemsData SwipeItems(
        MuxControls.SwipeMode mode,
        params SwipeItemData[] items) =>
        new(items, mode);

    /// <summary>
    /// 视差视图：参照 <paramref name="sourceIndex"/> 那个兄弟的滚动进度，把
    /// <paramref name="child"/> 错开一点。
    /// </summary>
    /// <param name="child">被错开的那片内容（唯一子槽位）。</param>
    /// <param name="sourceIndex">参照谁的滚动：<b>同层第几个兄弟</b>（0 起），通常是那个 <c>ScrollViewer</c>。</param>
    /// <param name="verticalShift">上下错开的最大距离（px）。</param>
    /// <param name="horizontalShift">左右错开的最大距离（px）。</param>
    /// <param name="verticalSourceOffsetKind">竖向错开量是"全程"还是"每滚一屏"。</param>
    /// <param name="horizontalSourceOffsetKind">横向同上。</param>
    /// <param name="maxVerticalShiftRatio">竖向错开量相对自身高度的上限（0~1）。</param>
    /// <param name="maxHorizontalShiftRatio">横向同上。</param>
    public static ParallaxViewElement ParallaxView(
        Element? child = null,
        int? sourceIndex = null,
        double verticalShift = 50,
        double horizontalShift = 0,
        MuxControls.ParallaxSourceOffsetKind verticalSourceOffsetKind =
            MuxControls.ParallaxSourceOffsetKind.Absolute,
        MuxControls.ParallaxSourceOffsetKind horizontalSourceOffsetKind =
            MuxControls.ParallaxSourceOffsetKind.Absolute,
        double maxVerticalShiftRatio = 1.0,
        double maxHorizontalShiftRatio = 1.0) =>
        new(child)
        {
            SourceIndex = sourceIndex,
            VerticalShift = verticalShift,
            HorizontalShift = horizontalShift,
            VerticalSourceOffsetKind = verticalSourceOffsetKind,
            HorizontalSourceOffsetKind = horizontalSourceOffsetKind,
            MaxVerticalShiftRatio = maxVerticalShiftRatio,
            MaxHorizontalShiftRatio = maxHorizontalShiftRatio,
        };

    /// <summary>
    /// 滑动命令里的一项。
    /// </summary>
    /// <param name="text">文字说明。</param>
    /// <param name="icon">图标：放 <c>FontIcon</c> / <c>BitmapIcon</c>（落到 <c>IconSource</c> 上）。</param>
    /// <param name="background">背景（"删除"常在这里给红色）。</param>
    /// <param name="foreground">前景。</param>
    /// <param name="behaviorOnInvoked">滑到头之后容器怎么办。</param>
    /// <param name="onInvoked">被点了。</param>
    public static SwipeItemData SwipeItem(
        string? text = null,
        Element? icon = null,
        Brush? background = null,
        Brush? foreground = null,
        MuxControls.SwipeBehaviorOnInvoked behaviorOnInvoked =
            MuxControls.SwipeBehaviorOnInvoked.Auto,
        Action? onInvoked = null) =>
        new()
        {
            Text = text,
            Icon = icon,
            Background = background,
            Foreground = foreground,
            BehaviorOnInvoked = behaviorOnInvoked,
            OnInvoked = onInvoked,
        };
}
