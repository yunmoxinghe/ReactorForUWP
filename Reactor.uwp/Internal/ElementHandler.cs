using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 元素 → 真实控件的处理契约（对齐官方
/// <c>Microsoft.UI.Reactor.Core.V1Protocol.IElementHandler&lt;TElement, TControl&gt;</c>）。
/// 每种 Element 记录类型注册一个 handler，协调器只做查表分发，
/// 新增元素不再需要改动协调器里的任何一处 switch。
/// </summary>
internal interface IElementHandler
{
    /// <summary>创建真实控件并完成首次挂载（含子元素的递归构建）。</summary>
    UIElement Mount(Reconciler reconciler, Element element, Action requestRerender);

    /// <summary>就地更新：同类型复用控件。类型/控件不匹配时返回 false。</summary>
    bool TryUpdate(
        Reconciler reconciler,
        Element oldElement,
        Element newElement,
        UIElement control,
        Action requestRerender);

    /// <summary>解绑事件、释放该类型持有的资源。</summary>
    void Unmount(Reconciler reconciler, UIElement control);

    /// <summary>该元素的子元素描述（非容器返回 null）。</summary>
    IReadOnlyList<Element?>? ChildrenOf(Element element);

    /// <summary>单子元素容器（Border / ScrollViewer / NavigationView）的唯一子元素。</summary>
    Element? SingleChildOf(Element element);

    /// <summary>子元素应当挂到哪个 Panel 上（ScrollViewer/Border 这类单子元素容器返回 null）。</summary>
    Panel? PanelOf(UIElement control);

    /// <summary>把子元素上的容器附加属性（Grid 行列等）落到真实控件上。</summary>
    void AttachChild(UIElement parent, UIElement child, Element childElement);
}

/// <summary>
/// handler 基类：把弱类型分发收敛成强类型回调，
/// 具体元素只需实现 <see cref="Mount"/> / <see cref="Update"/>。
/// </summary>
internal abstract class ElementHandler<TElement, TControl> : IElementHandler
    where TElement : Element
    where TControl : UIElement
{
    protected abstract TControl Mount(Reconciler reconciler, TElement element);

    protected virtual void Update(
        Reconciler reconciler,
        TElement oldElement,
        TElement newElement,
        TControl control)
    {
    }

    protected virtual void Unmount(Reconciler reconciler, TControl control)
    {
    }

    protected virtual IReadOnlyList<Element?>? ChildrenOf(TElement element) => null;

    protected virtual Element? SingleChildOf(TElement element) => null;

    protected virtual Panel? PanelOf(TControl control) => null;

    public virtual void AttachChild(UIElement parent, UIElement child, Element childElement)
    {
    }

    UIElement IElementHandler.Mount(Reconciler reconciler, Element element, Action requestRerender) =>
        Mount(reconciler, (TElement)element);

    bool IElementHandler.TryUpdate(
        Reconciler reconciler,
        Element oldElement,
        Element newElement,
        UIElement control,
        Action requestRerender)
    {
        // 控件类型与元素类型不匹配（例如同一位置换了元素类型）时不就地更新，
        // 交由调用方重建，避免 InvalidCastException。
        if (control is not TControl typed ||
            oldElement is not TElement typedOld ||
            newElement is not TElement typedNew)
        {
            return false;
        }

        Update(reconciler, typedOld, typedNew, typed);
        return true;
    }

    void IElementHandler.Unmount(Reconciler reconciler, UIElement control)
    {
        if (control is TControl typed)
        {
            Unmount(reconciler, typed);
        }
    }

    IReadOnlyList<Element?>? IElementHandler.ChildrenOf(Element element) =>
        element is TElement typed ? ChildrenOf(typed) : null;

    Element? IElementHandler.SingleChildOf(Element element) =>
        element is TElement typed ? SingleChildOf(typed) : null;

    Panel? IElementHandler.PanelOf(UIElement control) =>
        control is TControl typed ? PanelOf(typed) : null;
}

/// <summary>
/// 单子元素容器的"读写单个子内容"访问器注册表。
/// </summary>
/// <remarks>
/// <see cref="Reconciler.PatchSingleChild"/> 默认只认识 <c>ContentControl</c> 与
/// <c>Border</c>。第三方容器（如 Toolkit 的 SettingsExpander——它不继承
/// ContentControl，但有 <c>Content</c>）必须在这里登记，否则每轮重渲染都会
/// 走"卸载 + 整棵子树重建"的兜底分支：能跑，但状态丢失、性能差、日志刷屏。
/// 无反射、按具体类型查表，AOT 友好。
/// </remarks>
internal static class SingleChildAccessor
{
    private static readonly Dictionary<Type, Func<object, (Func<UIElement?> Getter, Action<UIElement?> Setter)>>
        Accessors = new();

    public static void Register<TNative>(
        Func<TNative, (Func<UIElement?> Getter, Action<UIElement?> Setter)> factory)
        where TNative : class =>
        Accessors[typeof(TNative)] = container => factory((TNative)container);

    public static bool TryGet(
        object container,
        out Func<UIElement?> getter,
        out Action<UIElement?> setter)
    {
        if (Accessors.TryGetValue(container.GetType(), out var factory))
        {
            (getter, setter) = factory(container);
            return true;
        }

        getter = null!;
        setter = null!;
        return false;
    }
}

/// <summary>
/// 全局元素 handler 注册表（官方 <c>ControlRegistry</c> 的简化版）。
/// 按 Element 类型查表、first-wins；无反射、无 <c>MakeGenericType</c>，
/// 因此对 AOT / trimming 友好。
/// </summary>
internal static class ElementHandlerRegistry
{
    private static readonly Dictionary<Type, IElementHandler> Handlers = new();

    /// <summary>注册一个 handler。重复注册同一元素类型是静默 no-op（对齐官方 first-wins）。</summary>
    public static void Register<TElement, THandler>()
        where TElement : Element
        where THandler : IElementHandler, new()
    {
        if (!Handlers.ContainsKey(typeof(TElement)))
        {
            Handlers[typeof(TElement)] = new THandler();
        }
    }

    public static bool TryGet(Type elementType, out IElementHandler handler) =>
        Handlers.TryGetValue(elementType, out handler!);

    /// <summary>注册全部内置元素。新增元素时只需要在这里加一行。</summary>
    public static void RegisterBuiltIns()
    {
        // 文本与基础控件
        Register<TextBlockElement, TextBlockHandler>();
        Register<ButtonElement, ButtonHandler>();

        // MenuFlyout / MenuItem / MenuSeparator 不在这里注册：它们不是 UIElement，
        // 走不了协调器（见 Internal/Handlers.Menus.cs 的注释）。
        Register<DropDownButtonElement, DropDownButtonHandler>();
        Register<SplitButtonElement, SplitButtonHandler>();
        Register<TextBoxElement, TextBoxHandler>();
        Register<CheckBoxElement, CheckBoxHandler>();
        Register<SliderElement, SliderHandler>();
        Register<InfoBarElement, InfoBarHandler>();

        // 布局
        Register<StackElement, StackHandler>();
        Register<GroupElement, GroupHandler>();
        Register<EmptyElement, EmptyHandler>();
        Register<GridElement, GridHandler>();
        Register<BorderElement, BorderHandler>();
        Register<ScrollViewerElement, ScrollViewerHandler>();

        // 输入与选择
        Register<ComboBoxElement, ComboBoxHandler>();
        Register<ToggleSwitchElement, ToggleSwitchHandler>();
        Register<RadioButtonElement, RadioButtonHandler>();
        Register<RadioButtonsElement, RadioButtonsHandler>();

        // 输入类补齐：密码框 / 带建议的输入框 / 数字框（以前只能拿 TextBox 顶替）
        Register<PasswordBoxElement, PasswordBoxHandler>();
        Register<AutoSuggestBoxElement, AutoSuggestBoxHandler>();
        Register<NumberBoxElement, NumberBoxHandler>();

        // 进度与媒体
        Register<ProgressElement, ProgressHandler>();
        Register<ProgressRingElement, ProgressRingHandler>();
        Register<InfoBadgeElement, InfoBadgeHandler>();
        Register<ImageElement, ImageHandler>();

        // 集合
        Register<ListViewElement, ListViewHandler>();
        Register<GridViewElement, GridViewHandler>();
        Register<NavigationViewElement, NavigationViewHandler>();

        // WinUI 模板向：链接 / 图标 / 面包屑 / 展开器 / 设置卡片 / 页面容器
        Register<HyperlinkButtonElement, HyperlinkButtonHandler>();
        Register<FontIconElement, FontIconHandler>();
        Register<BitmapIconElement, BitmapIconHandler>();
        Register<ImageIconElement, ImageIconHandler>();
        Register<BreadcrumbBarElement, BreadcrumbBarHandler>();
        Register<ExpanderElement, ExpanderHandler>();
        Register<SettingsCardElement, SettingsCardHandler>();
        Register<SettingsExpanderElement, SettingsExpanderHandler>();
        Register<FrameElement, FrameHandler>();
        Register<VirtualizingListElement, VirtualizingListHandler>();

        // 页签容器 / 富文本：页签条与语法高亮代码块（Gallery 的源码展示要用）
        Register<TabViewElement, TabViewHandler>();
        Register<PivotElement, PivotHandler>();
        Register<RichTextBlockElement, RichTextBlockHandler>();

        // 外壳与命令：分栏外壳 / 命令条 / 菜单栏
        // （MenuBarItem 不是独立元素——它是 MenuBarElement 里的一组数据，见该类注释）
        Register<SplitViewElement, SplitViewHandler>();
        Register<CommandBarElement, CommandBarHandler>();
        Register<MenuBarElement, MenuBarHandler>();

        // 反馈类：评分与人物头像
        Register<RatingElement, RatingControlHandler>();
        Register<PersonPictureElement, PersonPictureHandler>();

        // 日期与时间：四个都是 UWP 原生控件（WinUI 2 没有另做一套）
        Register<DatePickerElement, DatePickerHandler>();
        Register<TimePickerElement, TimePickerHandler>();
        Register<CalendarDatePickerElement, CalendarDatePickerHandler>();
        Register<CalendarViewElement, CalendarViewHandler>();

        // 按钮族补齐：会保持按下的按钮 / 按住连发的按钮 / 会保持按下的拆分按钮
        Register<ToggleButtonElement, ToggleButtonHandler>();
        Register<RepeatButtonElement, RepeatButtonHandler>();
        Register<ToggleSplitButtonElement, ToggleSplitButtonHandler>();

        // 集合补完：列表框与翻页视图（都只是 Selector，走另一条 handler 基类），
        // 以及树（节点是数据树，不是元素树）
        Register<ListBoxElement, ListBoxHandler>();
        Register<FlipViewElement, FlipViewHandler>();
        Register<TreeViewElement, TreeViewHandler>();

        // 布局补完：绝对定位 / 单子元素缩放 / 不等大小换行网格 / 相对布局
        // 前三个的"位置"写在子元素身上（附加属性），走 AttachChild 那条钩子；
        // RelativePanel 要指向兄弟，落在孩子们都造好之后的整体重落（见该类注释）
        Register<CanvasElement, CanvasHandler>();
        Register<ViewboxElement, ViewboxHandler>();
        Register<VariableSizedWrapGridElement, VariableSizedWrapGridHandler>();
        Register<RelativePanelElement, RelativePanelHandler>();

        // 状态与信息补完：下拉刷新容器（WinUI 2 真控件，单子元素容器）
        Register<RefreshContainerElement, RefreshContainerHandler>();

        // 浮层与双窗格补完：教学提示（受控 IsOpen，目标填同层下标）与双窗格
        // （两个槽位都自己管；它的 Mode 是只读的，所以没有受控值）
        Register<TeachingTipElement, TeachingTipHandler>();
        Register<TwoPaneViewElement, TwoPaneViewHandler>();

        // 取值与富文本补完：受控 Color 的取色器、文本住在 Document 里的富文本编辑框
        Register<ColorPickerElement, ColorPickerHandler>();
        Register<RichEditBoxElement, RichEditBoxHandler>();

        // 视图切换与滑动补完：语义缩放（两个槽位要过 ISemanticZoomInformation 检查）、
        // 分页指示器（受控 SelectedPageIndex）、滑动命令容器（四组命令 + 单子内容）
        Register<SemanticZoomElement, SemanticZoomHandler>();
        Register<PipsPagerElement, PipsPagerHandler>();
        Register<SwipeControlElement, SwipeControlHandler>();
        Register<ParallaxViewElement, ParallaxViewHandler>();

        // 形状：椭圆 / 矩形 / 直线（UWP 原生 Windows.UI.Xaml.Shapes，走
        // ShapeHandler 那个泛型基类：描边那一套在官方 Shape 基类上，不抄三遍）
        Register<EllipseElement, EllipseHandler>();
        Register<RectangleElement, RectangleHandler>();
        Register<LineElement, LineHandler>();

        // 逃生舱：把一棵原生控件树挂进 Reactor 布局（Native()）
        Register<NativeElement, NativeHandler>();

        // 第三方容器的单子元素访问器（不改这里就会每轮重建整棵子树）
        SingleChildAccessor.Register<ToolkitControls.SettingsExpander>(control => (
            () => control.Content as UIElement,
            value => control.Content = value!));

        // SplitView 也是单子元素容器（继承 Control，不是 ContentControl），
        // 不登记的话它的 Content 每轮都走"卸载 + 整棵子树重建"那条兜底分支。
        SingleChildAccessor.Register<Windows.UI.Xaml.Controls.SplitView>(control => (
            () => control.Content as UIElement,
            value => control.Content = value!));

        // Viewbox 也是：它继承 FrameworkElement（不是 ContentControl），
        // 子内容在 Child 上而不是 Content 上。
        SingleChildAccessor.Register<Windows.UI.Xaml.Controls.Viewbox>(control => (
            () => control.Child,
            value => control.Child = value));

        // ParallaxView 同理（WinUI 2 的 FrameworkElement，子内容在 Child 上）。
        SingleChildAccessor.Register<Microsoft.UI.Xaml.Controls.ParallaxView>(control => (
            () => control.Child,
            value => control.Child = value));
    }
}
