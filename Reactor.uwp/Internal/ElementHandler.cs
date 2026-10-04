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

        // 进度与媒体
        Register<ProgressElement, ProgressHandler>();
        Register<ProgressRingElement, ProgressRingHandler>();
        Register<ImageElement, ImageHandler>();

        // 集合
        Register<ListViewElement, ListViewHandler>();
        Register<GridViewElement, GridViewHandler>();
        Register<NavigationViewElement, NavigationViewHandler>();

        // WinUI 模板向：链接 / 图标 / 面包屑 / 展开器 / 设置卡片 / 页面容器
        Register<HyperlinkButtonElement, HyperlinkButtonHandler>();
        Register<FontIconElement, FontIconHandler>();
        Register<BitmapIconElement, BitmapIconHandler>();
        Register<BreadcrumbBarElement, BreadcrumbBarHandler>();
        Register<ExpanderElement, ExpanderHandler>();
        Register<SettingsCardElement, SettingsCardHandler>();
        Register<SettingsExpanderElement, SettingsExpanderHandler>();
        Register<FrameElement, FrameHandler>();
        Register<VirtualizingListElement, VirtualizingListHandler>();

        // 逃生舱：把一棵原生控件树挂进 Reactor 布局（Native()）
        Register<NativeElement, NativeHandler>();

        // 第三方容器的单子元素访问器（不改这里就会每轮重建整棵子树）
        SingleChildAccessor.Register<ToolkitControls.SettingsExpander>(control => (
            () => control.Content as UIElement,
            value => control.Content = value!));
    }
}
