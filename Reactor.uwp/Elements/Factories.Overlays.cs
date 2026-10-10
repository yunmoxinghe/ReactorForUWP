using System;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls.Primitives;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    // ── 浮层与双窗格 ─────────────────────────────────────────────

    /// <summary>
    /// 浮层容器：一块盖在最上层的任意内容（自带这套皮的是 <c>Flyout</c> /
    /// <c>ContentDialog</c>，它不是）。
    /// </summary>
    /// <param name="child">里面的内容（任意元素树）。</param>
    /// <param name="isOpen">是否展开（<b>受控</b>：给了值才受控，null = 不管它）。</param>
    /// <param name="isLightDismissEnabled">点外面 / Esc 是否关掉它（官方默认<b>关</b>）。</param>
    /// <param name="shouldConstrainToRootBounds">
    /// 是否约束在窗口内（<c>false</c> 才允许浮出窗口之外；null = 用官方默认）。
    /// </param>
    /// <param name="horizontalOffset">相对目标（或窗口左上角）的水平偏移。</param>
    /// <param name="verticalOffset">相对目标（或窗口左上角）的垂直偏移。</param>
    /// <param name="targetIndex">指向<b>同层第几个</b>子元素（见 <see cref="PopupElement"/>）。</param>
    /// <param name="desiredPlacement">想挂在目标的哪一侧（是"期望"，不是"结果"）。</param>
    /// <param name="onIsOpenChanged">展开状态变了（参数就是新的 <c>IsOpen</c>）。</param>
    public static PopupElement Popup(
        Element? child = null,
        bool? isOpen = null,
        bool isLightDismissEnabled = false,
        bool? shouldConstrainToRootBounds = null,
        double? horizontalOffset = null,
        double? verticalOffset = null,
        int? targetIndex = null,
        PopupPlacementMode? desiredPlacement = null,
        Action<bool>? onIsOpenChanged = null) =>
        new()
        {
            Child = child,
            IsOpen = isOpen,
            IsLightDismissEnabled = isLightDismissEnabled,
            ShouldConstrainToRootBounds = shouldConstrainToRootBounds,
            HorizontalOffset = horizontalOffset,
            VerticalOffset = verticalOffset,
            TargetIndex = targetIndex,
            DesiredPlacement = desiredPlacement,
            OnIsOpenChanged = onIsOpenChanged,
        };

    /// <summary>
    /// 教学提示：挂在某个控件旁边的一段说明。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <param name="subtitle">副标题。</param>
    /// <param name="child">正文（它是 <c>ContentControl</c>，可以是任意元素树）。</param>
    /// <param name="isOpen">是否展开（<b>受控</b>：给了值才受控，null = 不管它）。</param>
    /// <param name="targetIndex">指向<b>同层第几个</b>子元素（见 <see cref="TeachingTipElement"/>）。</param>
    /// <param name="preferredPlacement">优先挂在哪一侧。</param>
    /// <param name="isLightDismissEnabled">点空白处 / Esc 是否关掉它。</param>
    /// <param name="actionButtonText">主按钮文字；给了才有那个按钮。</param>
    /// <param name="closeButtonText">关闭按钮文字；给了才有那个按钮。</param>
    /// <param name="onIsOpenChanged">展开状态变了（参数就是新的 <c>IsOpen</c>）。</param>
    /// <param name="onActionButtonClick">点了主按钮。</param>
    /// <param name="onCloseButtonClick">点了关闭按钮。</param>
    public static TeachingTipElement TeachingTip(
        string? title = null,
        string? subtitle = null,
        Element? child = null,
        bool? isOpen = null,
        int? targetIndex = null,
        MuxControls.TeachingTipPlacementMode? preferredPlacement = null,
        bool isLightDismissEnabled = true,
        string? actionButtonText = null,
        string? closeButtonText = null,
        Action<bool>? onIsOpenChanged = null,
        Action? onActionButtonClick = null,
        Action? onCloseButtonClick = null) =>
        new()
        {
            Title = title,
            Subtitle = subtitle,
            Child = child,
            IsOpen = isOpen,
            TargetIndex = targetIndex,
            PreferredPlacement = preferredPlacement,
            IsLightDismissEnabled = isLightDismissEnabled,
            ActionButtonText = actionButtonText,
            CloseButtonText = closeButtonText,
            OnIsOpenChanged = onIsOpenChanged,
            OnActionButtonClick = onActionButtonClick,
            OnCloseButtonClick = onCloseButtonClick,
        };

    /// <summary>
    /// 双窗格视图：两块内容按可用尺寸决定并排还是只留一块。
    /// </summary>
    /// <param name="pane1">第一块（第一个槽位）。</param>
    /// <param name="pane2">第二块（第二个槽位）。</param>
    /// <param name="panePriority">空间只够一块时留哪一块。</param>
    /// <param name="wideModeConfiguration">宽形态下两块怎么摆。</param>
    /// <param name="tallModeConfiguration">高形态下两块怎么摆。</param>
    /// <param name="pane1Length">Pane1 的长度（null = 用官方默认值）。</param>
    /// <param name="pane2Length">Pane2 的长度（null = 用官方默认值）。</param>
    /// <param name="minWideModeWidth">至少多宽才算"宽形态"。</param>
    /// <param name="minTallModeHeight">至少多高才算"高形态"。</param>
    /// <param name="onModeChanged">形态变了（参数就是新的 <c>TwoPaneViewMode</c>）。</param>
    public static TwoPaneViewElement TwoPaneView(
        Element? pane1 = null,
        Element? pane2 = null,
        MuxControls.TwoPaneViewPriority panePriority = MuxControls.TwoPaneViewPriority.Pane1,
        MuxControls.TwoPaneViewWideModeConfiguration wideModeConfiguration =
            MuxControls.TwoPaneViewWideModeConfiguration.LeftRight,
        MuxControls.TwoPaneViewTallModeConfiguration tallModeConfiguration =
            MuxControls.TwoPaneViewTallModeConfiguration.TopBottom,
        GridLength? pane1Length = null,
        GridLength? pane2Length = null,
        double minWideModeWidth = 641,
        double minTallModeHeight = 641,
        Action<MuxControls.TwoPaneViewMode>? onModeChanged = null) =>
        new(pane1, pane2)
        {
            PanePriority = panePriority,
            WideModeConfiguration = wideModeConfiguration,
            TallModeConfiguration = tallModeConfiguration,
            Pane1Length = pane1Length,
            Pane2Length = pane2Length,
            MinWideModeWidth = minWideModeWidth,
            MinTallModeHeight = minTallModeHeight,
            OnModeChanged = onModeChanged,
        };

    // ── 提示气泡 ────────────────────────────────────────────────

    /// <summary>
    /// 提示气泡：给一个宿主元素挂上"悬停 / 聚焦时冒出来的那行说明"。
    /// </summary>
    /// <param name="text">纯文本（官方 <c>ToolTipService.ToolTip="…"</c> 那一档）。</param>
    /// <param name="content">
    /// 任意内容（官方 <c>&lt;ToolTipService.ToolTip&gt;…&lt;/ToolTipService.ToolTip&gt;</c>
    /// 那一档，比如一个 <c>HStack(Image + TextBlock)</c>）。两个都给了以它为准。
    /// </param>
    /// <param name="placement">弹在指针旁还是宿主的上 / 下 / 左 / 右。</param>
    /// <param name="horizontalOffset">与指针（或宿主）的水平距离。</param>
    /// <param name="verticalOffset">与指针（或宿主）的垂直距离。</param>
    /// <param name="onOpened">弹出来了。</param>
    /// <param name="onClosed">收起了。</param>
    /// <remarks>
    /// 名字叫 <c>Tip</c> 而不叫 <c>ToolTip</c>：后者已经被 <c>.ToolTip(...)</c>
    /// 修饰器占着（<c>Factories</c> 是静态导入的，两个同名的东西会让
    /// <c>Button("x").ToolTip(ToolTip("y"))</c> 这种写法读起来像在自我指涉）。
    /// </remarks>
    public static ToolTipElement Tip(
        string? text = null,
        Element? content = null,
        PlacementMode placement = PlacementMode.Mouse,
        double? horizontalOffset = null,
        double? verticalOffset = null,
        Action? onOpened = null,
        Action? onClosed = null) =>
        new(content)
        {
            Text = text,
            Placement = placement,
            HorizontalOffset = horizontalOffset,
            VerticalOffset = verticalOffset,
            OnOpened = onOpened,
            OnClosed = onClosed,
        };
}
