using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 把 <see cref="Element"/> 描述树映射到真实 XAML 控件：
/// mount 时按元素类型创建控件；update 时同类型就地改属性、
/// 容器子节点按位置对齐复用，类型不同才替换。
/// 每个 ReactorHost 持有一个实例（保存事件处理器映射）。
/// </summary>
/// <remarks>
/// 元素 → 控件的映射全部走 <see cref="ElementHandlerRegistry"/> 查表，
/// 新增元素只需加一个 handler + 一行注册，不需要改动本文件的任何 switch。
/// </remarks>
internal sealed class Reconciler
{
    private static readonly Action NoopRerender = () => { };

    /// <summary>已经报过"没有 Padding 属性"的原生类型（见 <see cref="ApplyPadding"/>）。</summary>
    private static readonly HashSet<Type> UnsupportedPaddingTypes = new();

    static Reconciler()
    {
        ElementHandlerRegistry.RegisterBuiltIns();
    }

    /// <summary>
    /// 是否正处于一轮渲染 / patch 中。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要它</b>：patch 过程中会给真实控件赋值（<c>TextBox.Text</c>、
    /// <c>Button.Content</c>、移除子控件…），XAML 可能同步回调控件事件
    /// （典型是 TextChanged）。如果回调里调用 setState 立刻重渲染，
    /// 内外两层 patch 会交错执行：内层基于"外层还没改完"的原生树做增删，
    /// 外层收尾时再插一次，同一个 Panel 就会多出一个原生子控件，
    /// 之后下一次 patch 按 element 数量索引就会
    /// <c>ArgumentOutOfRangeException</c>。
    /// 因此渲染过程中到来的重渲染请求一律推迟到本轮结束之后。
    /// </remarks>
    private bool _inRenderPass;

    /// <summary>
    /// 当前是否处于一轮渲染 / patch 中（宿主与子组件共用同一把锁）。
    /// </summary>
    /// <remarks>
    /// 必须由宿主在整轮渲染时也置位：否则宿主驱动渲染期间，控件事件同步回调
    /// 触发的子组件状态更新会被判定为"非重入"而立刻执行，内外两层 patch 交错。
    /// </remarks>
    internal bool InRenderPass => _inRenderPass;

    /// <summary>遍历期生效的 Context 作用域（对齐官方 Reconciler._contextScope）。</summary>
    private readonly ContextScope _contextScope = new();

    private readonly Dictionary<Button, RoutedEventHandler> _buttonClicks = new();
    private readonly Dictionary<Border, ComponentNode> _componentNodes = new();
    private readonly Dictionary<TextBox, TextChangedEventHandler> _textChanged = new();
    private readonly Dictionary<CheckBox, RoutedEventHandler> _checkBoxChecked = new();
    private readonly Dictionary<CheckBox, RoutedEventHandler> _checkBoxUnchecked = new();
    private readonly Dictionary<Slider, RangeBaseValueChangedEventHandler> _sliderValueChanged = new();

    /// <summary>构建失败、被替换成占位块的原生控件（见 <see cref="Build"/>）。</summary>
    private readonly HashSet<UIElement> _failedBuilds = new();

    // ── 元素 → 控件 分发 ────────────────────────────────────────

    internal UIElement Build(Element element)
    {
        using var scope = PushContext(element);

        // 挂载期：修饰值一律无条件写成本地值（原因见 PropWriter 类顶注释）。
        // 必须包住 handler.Mount——子元素是在它里面递归 Build 的。
        using var mount = PropWriter.BeginMount();

        // 诊断期：崩溃前最后一个 build 的元素就是嫌疑区域
        // （原生异常没有托管堆栈，只能靠这个序列反推）。
        Hosting.ReactorApplication.Trace($"[reactor] build {element.GetType().Name}");

        UIElement native;
        if (element is ComponentElement comp)
        {
            native = BuildComponent(comp);
        }
        else if (ElementHandlerRegistry.TryGet(element.GetType(), out var handler))
        {
            try
            {
                native = handler.Mount(this, element, NoopRerender);
            }
            catch (Exception ex)
            {
                // 一个元素的 WinRT 投影踩雷（典型是集合/对象属性不被接受）不该拖垮整个进程：
                // UWP 里未处理异常 = 进程直接终止，用户只看到"应用崩溃"。
                // 这里降级成可见的占位块 + 日志，页面其余部分照常渲染。
                Hosting.ReactorApplication.Trace(
                    $"[reactor] 元素构建失败 {element.GetType().Name}: {ex.Message}");
                _failedBuilds.Add(native = BuildFailure(element.GetType().Name, ex.Message));
            }
        }
        else
        {
            throw new NotSupportedException(
                $"不支持的元素类型: {element.GetType().FullName}");
        }

        ApplyModifiers(native, element.Modifiers);
        return native;
    }

    private static UIElement BuildFailure(string elementName, string message) =>
        new Border
        {
            Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Windows.UI.Colors.OrangeRed) { Opacity = 0.15 },
            Child = new TextBlock
            {
                Text = $"[构建失败] {elementName}: {message}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Windows.UI.Colors.OrangeRed),
            },
        };

    /// <summary>
    /// 构建图标（SettingsCard / Expander 的 <c>HeaderIcon</c> 只接受
    /// <see cref="IconElement"/>，不能塞任意 UIElement）。
    /// 非图标元素返回 null，宿主静默降级为无图标。
    /// </summary>
    internal static IconElement? BuildIcon(Reconciler reconciler, Element? element) =>
        element is null ? null : reconciler.Build(element) as IconElement;

    /// <summary>
    /// 将旧描述对应的真实控件就地更新为新描述。
    /// 调用方需保证 old/next 是同一 Element 记录类型。
    /// </summary>
    internal void Patch(UIElement native, Element old, Element next)
    {
        using var scope = PushContext(next);

        try
        {
            PatchCore(native, old, next);
        }
        catch (Exception ex)
        {
            // patch 期异常同样会终止进程，而堆栈里只有 handler 内部帧，
            // 看不出是哪一类元素；这里补一条定位日志再原样抛出。
            Hosting.ReactorApplication.Trace(
                $"[reactor] patch 失败: {old.GetType().Name} -> {next.GetType().Name} " +
                $"(native={native.GetType().Name}) - {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    private void PatchCore(UIElement native, Element old, Element next)
    {
        if (_failedBuilds.Contains(native))
        {
            // 上一次构建就失败过，占位块不是真实控件，没法就地 patch。
            // 保持占位（已记日志），避免二次抛异常把进程带崩。
            return;
        }

        if (next is ComponentElement nextComp)
        {
            if (native is not Border wrapper)
            {
                throw new NotSupportedException(
                    $"组件元素必须挂在 Border 上，实际是 {native.GetType().FullName}");
            }

            PatchComponent(wrapper, nextComp);
        }
        else if (ElementHandlerRegistry.TryGet(next.GetType(), out var handler) &&
                 handler.TryUpdate(this, old, next, native, NoopRerender))
        {
            // handler 已就地完成更新
        }
        else
        {
            throw new NotSupportedException(
                $"无法更新控件 {native.GetType().FullName} -> {next.GetType().FullName}");
        }

        ApplyModifiers(native, next.Modifiers);
    }

    /// <summary>两个描述是否可以复用同一个真实控件（先按记录类型，再看组件类型）。</summary>
    /// <remarks>
    /// 光看记录类型对组件元素是不够的：两个无 props 的 <see cref="ComponentElement"/>
    /// 记录类型完全相同，但 <c>ComponentType</c> 可以完全不同（切页就是这种场景）。
    /// 这里必须判成"不能复用"，让 Frame 走导航重建；否则会拿 A 页面的节点去
    /// patch 成 B 页面（曾因此在 TransferNode 里抛 E_INVALIDARG 导致后半截菜单全打不开）。
    /// </remarks>
    internal static bool CanPatch(Element? old, Element? next) =>
        old is not null
        && next is not null
        && old.GetType() == next.GetType()
        && (old is not ComponentElement oldComp
            || next is not ComponentElement nextComp
            || oldComp.ComponentType == nextComp.ComponentType);

    // ── 供 handler 递归使用的内部入口 ────────────────────────────

    /// <summary>构建 Panel 的全部子节点，并应用容器附加属性（Grid 行列等）。</summary>
    internal void BuildChildren(Panel panel, Element parentElement, IReadOnlyList<Element?> children)
    {
        foreach (var child in NonNull(children))
        {
            var childNative = Build(child);
            panel.Children.Add(childNative);
            AttachChild(parentElement, panel, childNative, child);
        }
    }

    /// <summary>就地 patch Panel 的子节点序列。</summary>
    internal void PatchPanelChildren(
        Panel panel,
        Element parentElement,
        IReadOnlyList<Element?> oldChildren,
        IReadOnlyList<Element?> nextChildren)
    {
        PatchChildrenCore(panel, oldChildren, nextChildren);

        // 附加属性（Grid 行列）每次都要重刷：子节点可能被重排/复用。
        var kids = NonNull(nextChildren);
        var count = Math.Min(panel.Children.Count, kids.Count);
        for (var i = 0; i < count; i++)
        {
            AttachChild(parentElement, panel, panel.Children[i], kids[i]);
        }
    }

    /// <summary>
    /// 单子元素容器（ContentControl / Border）的子节点 patch：
    /// 同类型就地 patch，不同类型或结构变化则卸载重建。
    /// </summary>
    internal void PatchSingleChild(UIElement container, Element? oldChild, Element? newChild)
    {
        Func<UIElement?> getter;
        Action<UIElement?> setter;

        if (container is ContentControl contentControl)
        {
            getter = () => contentControl.Content as UIElement;
            setter = value => contentControl.Content = value;
        }
        else if (container is Border border)
        {
            getter = () => border.Child;
            setter = value => border.Child = value;
        }
        else if (SingleChildAccessor.TryGet(container, out getter, out setter))
        {
            // 第三方容器（Toolkit SettingsExpander 等）自己登记的访问器，见该类的注释。
        }
        else
        {
            // 未知容器类型：静默 return 会让"整棵子树永远不更新"这类问题极难发现，
            // 这里显式留痕。
            Hosting.ReactorApplication.Trace(
                $"[reactor] 单子元素容器不支持就地更新: {container.GetType().FullName}");
            return;
        }

        var current = getter();

        if (newChild is null)
        {
            if (current is not null)
            {
                UnmountNative(current, oldChild ?? EmptyElement.Instance);
                setter(null);
            }

            return;
        }

        if (current is null)
        {
            setter(Build(newChild));
            return;
        }

        if (oldChild is not null && CanPatch(oldChild, newChild))
        {
            Patch(current, oldChild, newChild);
            return;
        }

        if (oldChild is not null)
        {
            UnmountNative(current, oldChild);
        }

        setter(Build(newChild));
    }

    /// <summary>ItemsControl（ListView / GridView）的条目 patch。</summary>
    internal void PatchItems(
        ItemsControl control,
        IReadOnlyList<Element?> oldItems,
        IReadOnlyList<Element?> newItems)
    {
        var oldKids = NonNull(oldItems);
        var newKids = NonNull(newItems);

        var canPatchInPlace = oldKids.Count == newKids.Count && control.Items.Count == oldKids.Count;
        if (canPatchInPlace)
        {
            for (var i = 0; i < oldKids.Count; i++)
            {
                if (!CanPatch(oldKids[i], newKids[i]))
                {
                    canPatchInPlace = false;
                    break;
                }
            }
        }

        if (canPatchInPlace)
        {
            for (var i = 0; i < oldKids.Count; i++)
            {
                if (control.Items[i] is UIElement native)
                {
                    Patch(native, oldKids[i], newKids[i]);
                }
            }

            return;
        }

        for (var i = control.Items.Count - 1; i >= 0; i--)
        {
            if (control.Items[i] is UIElement native)
            {
                UnmountNative(native, i < oldKids.Count ? oldKids[i] : EmptyElement.Instance);
            }
        }

        control.Items.Clear();
        foreach (var kid in newKids)
        {
            control.Items.Add(Build(kid));
        }
    }

    private void AttachChild(Element parentElement, UIElement parent, UIElement child, Element childElement)
    {
        if (childElement.Modifiers?.Grid is null)
        {
            return;
        }

        if (ElementHandlerRegistry.TryGet(parentElement.GetType(), out var handler))
        {
            handler.AttachChild(parent, child, childElement);
        }
    }

    // ── Context 作用域 ──────────────────────────────────────────

    /// <summary>进入带 <c>Provide</c> 值的元素时压栈，离开时弹栈。</summary>
    private IDisposable PushContext(Element element)
    {
        var values = element.Modifiers?.ContextValues;
        if (values is null || values.Count == 0)
        {
            return NullScope.Instance;
        }

        return new ContextPop(_contextScope, _contextScope.Push(values));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class ContextPop : IDisposable
    {
        private readonly ContextScope _scope;
        private readonly int _count;
        private bool _disposed;

        public ContextPop(ContextScope scope, int count)
        {
            _scope = scope;
            _count = count;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _scope.Pop(_count);
        }
    }

    // ── 组件 ────────────────────────────────────────────────────

    /// <summary>挂载一个子组件：创建 Border 锚点、ComponentNode，并独立渲染子树。</summary>
    private Border BuildComponent(ComponentElement compElement)
    {
        var component = (Component)Activator.CreateInstance(compElement.ComponentType)!;

        if (compElement.Props is not null && component is IPropsReceiver receiver)
        {
            receiver.SetProps(compElement.Props);
        }

        var wrapper = new Border();
        var node = new ComponentNode
        {
            ComponentType = compElement.ComponentType,
            Props = compElement.Props,
            Instance = component,
            NativeRoot = wrapper,
        };

        _componentNodes[wrapper] = node;

        // 子组件独立重渲染：只 patch 自己的 Border.Child
        // 必须在 UI 线程执行；跨线程调用通过 Dispatcher marshal
        void RequestComponentRerender()
        {
            if (!node.IsMounted)
            {
                return;
            }

            // 一轮 patch 还没跑完时又来了状态更新：必须推迟。
            // 若在此处同步重渲染，内外两层 patch 会交错修改同一棵原生树，
            // 导致原生子控件数量与 element 子节点数量错位（随后索引越界崩溃）。
            if (_inRenderPass)
            {
                _ = wrapper.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal, DoRerender);
                return;
            }

            if (wrapper.Dispatcher.HasThreadAccess)
            {
                DoRerender();
            }
            else
            {
                _ = wrapper.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal, DoRerender);
            }

            // 子组件自己的一轮重渲染。整段包在 RunPass 里，
            // 这样 patch 途中由控件事件同步触发的状态更新会被识别为"重入"并推迟。
            void DoRerender()
            {
                if (!node.IsMounted)
                {
                    return;
                }

                RunPass(() =>
                {
                    if (!node.IsMounted)
                    {
                        return;
                    }

                    RenderComponentTree(node, wrapper);
                });
            }
        }

        component.Context.RequestRerender = RequestComponentRerender;
        component.Context.IsMountedCheck = () => node.IsMounted;

        // 首次渲染子组件：先把祖先提供的 Context 固化到节点上，
        // 之后（可能异步）重渲染时仍然能读到。
        node.ContextScope.ReplaceWith(_contextScope);
        RenderComponentTree(node, wrapper);

        return wrapper;
    }

    /// <summary>渲染（或重渲染）一个组件的子树，并把结果落到 wrapper.Child。</summary>
    private void RenderComponentTree(ComponentNode node, Border wrapper)
    {
        node.Instance.BeginRender(node.ContextScope);
        var nextElement = node.Instance.Render();
        node.Instance.EndRender();

        if (node.CurrentElement is null ||
            wrapper.Child is not UIElement childNative ||
            !CanPatch(node.CurrentElement, nextElement))
        {
            // 整子树替换前，先清理旧子树（事件解绑 + 嵌套组件 cleanup）
            if (wrapper.Child is UIElement oldChild && node.CurrentElement is not null)
            {
                UnmountNative(oldChild, node.CurrentElement);
            }

            wrapper.Child = Build(nextElement);
        }
        else
        {
            Patch(childNative, node.CurrentElement, nextElement);
        }

        node.CurrentElement = nextElement;
    }

    /// <summary>
    /// 就地更新子组件：同类型则更新 props（按 ShouldUpdate 决定是否重渲染），
    /// 不同类型则整子树卸载重挂。父组件重渲染时调用。
    /// </summary>
    private void PatchComponent(Border wrapper, ComponentElement nextComp)
    {
        if (!_componentNodes.TryGetValue(wrapper, out var node))
        {
            // 防御：组件节点丢失（不应发生），重建
            var newWrapper = BuildComponent(nextComp);
            TransferNode(newWrapper, wrapper);
            return;
        }

        if (node.ComponentType != nextComp.ComponentType)
        {
            // 组件类型变化：整子树卸载重挂
            node.IsMounted = false;
            node.Context.RunCleanups();
            _componentNodes.Remove(wrapper);

            var newWrapper = BuildComponent(nextComp);
            TransferNode(newWrapper, wrapper);
            return;
        }

        // 父树可能新增/修改了 Provide，先把最新的 Context 固化下来
        node.ContextScope.ReplaceWith(_contextScope);

        // 同类型：更新 props，按 ShouldUpdate 决定是否重渲染子树
        var oldProps = node.Props;
        node.Props = nextComp.Props;

        if (node.Instance is IPropsReceiver receiver)
        {
            // Component<TProps>：UpdateProps 内部更新 Props 并调 ShouldUpdate
            if (receiver.UpdateProps(nextComp.Props))
            {
                RerenderComponent(node, wrapper);
            }
        }
        else if (!Equals(oldProps, nextComp.Props))
        {
            // 无 props 组件：props 变化即重渲染（简化策略）
            RerenderComponent(node, wrapper);
        }
        // props 相等：跳过重渲染（子组件只响应自己的 state）
    }

    /// <summary>把重建出来的组件节点挂回原 wrapper（保持视觉树位置不变）。</summary>
    /// <remarks>
    /// <b>必须先摘后挂</b>：XAML 不允许一个控件同时挂在两个父节点下，
    /// 直接写 <c>wrapper.Child = newWrapper.Child</c> 会抛 0x80070057
    /// （E_INVALIDARG，"Value does not fall within the expected range"）。
    /// </remarks>
    private void TransferNode(Border newWrapper, Border wrapper)
    {
        var child = newWrapper.Child;
        newWrapper.Child = null;
        wrapper.Child = child;

        if (_componentNodes.TryGetValue(newWrapper, out var newNode))
        {
            _componentNodes.Remove(newWrapper);
            newNode.NativeRoot = wrapper;
            _componentNodes[wrapper] = newNode;
        }
    }

    /// <summary>
    /// 执行一轮渲染 / patch，并把 <see cref="_inRenderPass"/> 置位，
    /// 使过程中同步触发的状态更新被推迟而不是重入。
    /// </summary>
    internal void RunPass(Action pass)
    {
        var wasInPass = _inRenderPass;
        _inRenderPass = true;
        try
        {
            pass();
        }
        finally
        {
            _inRenderPass = wasInPass;
        }
    }

    /// <summary>重渲染组件子树（在 UI 线程上调用）。</summary>
    private void RerenderComponent(ComponentNode node, Border wrapper)
    {
        if (!node.IsMounted)
        {
            return;
        }

        RunPass(() => RenderComponentTree(node, wrapper));
    }

    // ── 子节点 diff ─────────────────────────────────────────────

    private void PatchChildrenCore(
        Panel panel,
        IReadOnlyList<Element?> oldChildren,
        IReadOnlyList<Element?> nextChildren)
    {
        var oldKids = NonNull(oldChildren);
        var newKids = NonNull(nextChildren);

        // 前置一致性检查：原生子控件必须与上一次渲染的 element 子节点一一对应。
        // 一旦数量对不上说明原生树已经和 element 树错位，继续按位置索引必然越界，
        // 这里直接退化为整段重建（宁可丢焦点，也不能崩）。
        if (panel.Children.Count != oldKids.Count)
        {
            Hosting.ReactorApplication.Trace(
                $"[reactor] 子节点错位，重建: natives={panel.Children.Count} " +
                $"old={oldKids.Count} new={newKids.Count} panel={panel.GetType().Name}");
            RebuildChildren(panel, oldKids, newKids);
            return;
        }

        // 快速路径：子节点序列结构完全一致（数量、每位置类型、每位置 Key）。
        // 此时仅就地 Patch，绝不动 Children 集合 —— 否则 Clear/Re-Add 会把
        // 正在交互的控件（TextBox 焦点、Slider 拖动）detach，导致状态丢失。
        if (oldKids.Count == newKids.Count && panel.Children.Count == oldKids.Count)
        {
            var structurallySame = true;
            for (var i = 0; i < oldKids.Count; i++)
            {
                if (!CanPatch(oldKids[i], newKids[i]) ||
                    !Equals(oldKids[i].Key, newKids[i].Key))
                {
                    structurallySame = false;
                    break;
                }
            }

            if (structurallySame)
            {
                for (var i = 0; i < oldKids.Count; i++)
                {
                    Patch(panel.Children[i], oldKids[i], newKids[i]);
                }

                return;
            }
        }

        // 慢路径（结构/顺序变化）：最小 keyed diff。
        // 有 Key 的子节点按 Key 匹配，无 Key 的按位置对齐。
        // 不做 LIS / 最小编辑距离，仅支持同类型重排（A,B → B,A）。
        var oldKeyed = new Dictionary<string, (Element Element, UIElement Native)>();
        var oldUnkeyed = new List<(Element Element, UIElement Native)>();

        // 原生控件 → 它对应的 element：卸载时用，避免用"面板下标"去索引
        // element 列表（两者数量一旦对不上就是越界崩溃）。
        var elementByNative = new Dictionary<UIElement, Element>();

        for (var i = 0; i < oldKids.Count; i++)
        {
            var el = oldKids[i];
            var native = panel.Children[i];
            elementByNative[native] = el;

            if (el.Key is { } key)
            {
                oldKeyed[key] = (el, native);
            }
            else
            {
                oldUnkeyed.Add((el, native));
            }
        }

        var newKeyed = new List<(string Key, Element Element, int NewIndex)>();
        var newUnkeyed = new List<(Element Element, int NewIndex)>();

        for (var i = 0; i < newKids.Count; i++)
        {
            var el = newKids[i];
            if (el.Key is { } key)
            {
                newKeyed.Add((key, el, i));
            }
            else
            {
                newUnkeyed.Add((el, i));
            }
        }

        var newNatives = new UIElement?[newKids.Count];

        // 处理 key 匹配的子节点
        foreach (var (key, newEl, newIdx) in newKeyed)
        {
            if (oldKeyed.TryGetValue(key, out var oldPair))
            {
                oldKeyed.Remove(key); // 一个 old key 只匹配一次，重复 key 走新增分支，避免同一原生控件复用两次导致越界

                if (CanPatch(oldPair.Element, newEl))
                {
                    Patch(oldPair.Native, oldPair.Element, newEl);
                    newNatives[newIdx] = oldPair.Native;
                }
                else
                {
                    // 类型变化：卸载旧控件，创建新控件
                    UnmountNative(oldPair.Native, oldPair.Element);
                    newNatives[newIdx] = Build(newEl);
                }
            }
            else
            {
                // 新增 key：创建新控件
                newNatives[newIdx] = Build(newEl);
            }
        }

        // 处理无 key 的子节点（按位置对齐）
        for (var i = 0; i < newUnkeyed.Count; i++)
        {
            var (newEl, newIdx) = newUnkeyed[i];

            if (i < oldUnkeyed.Count)
            {
                var oldPair = oldUnkeyed[i];

                if (CanPatch(oldPair.Element, newEl))
                {
                    Patch(oldPair.Native, oldPair.Element, newEl);
                    newNatives[newIdx] = oldPair.Native;
                }
                else
                {
                    UnmountNative(oldPair.Native, oldPair.Element);
                    newNatives[newIdx] = Build(newEl);
                }
            }
            else
            {
                newNatives[newIdx] = Build(newEl);
            }
        }

        // 目标顺序（与 newKids 一一对应）。理论上不会有 null，这里兜底补建；
        // 同一个原生控件绝不能落在两个位置上 —— XAML 会抛"已有逻辑父级"。
        var finalOrder = new List<UIElement>(newNatives.Length);
        var used = new HashSet<UIElement>();
        for (var i = 0; i < newNatives.Length; i++)
        {
            var target = newNatives[i];
            if (target is null || !used.Add(target))
            {
                target = Build(newKids[i]);
                newNatives[i] = target;
                used.Add(target);
            }

            finalOrder.Add(target);
        }

        // 移除不再需要的控件：按"目标需求的份数"保留，
        // 富余的重复副本（原生树已错位的残留）也会被一并清掉。
        var needed = new Dictionary<UIElement, int>();
        foreach (var target in finalOrder)
        {
            needed[target] = needed.TryGetValue(target, out var n) ? n + 1 : 1;
        }

        for (var i = panel.Children.Count - 1; i >= 0; i--)
        {
            var child = panel.Children[i];
            if (needed.TryGetValue(child, out var remaining) && remaining > 0)
            {
                needed[child] = remaining - 1;
                continue;
            }

            UnmountNative(
                child,
                elementByNative.TryGetValue(child, out var staleElement)
                    ? staleElement
                    : EmptyElement.Instance);
            panel.Children.RemoveAt(i);
        }

        // 就地重排：处理完位置 i 后 panel.Children[0..i] 恒等于 finalOrder[0..i]，
        // 后续步骤只在下标 >= i+1 处增删，不会破坏已固定的前缀 → 必然收敛。
        // 复用控件仅在顺序真的变化时才被 Remove/Insert（否则原地不动，保住焦点）。
        for (var i = 0; i < finalOrder.Count; i++)
        {
            var target = finalOrder[i];
            var current = IndexOfNative(panel, target);
            if (current == i)
            {
                continue;
            }

            if (current >= 0)
            {
                panel.Children.RemoveAt(current);
            }

            panel.Children.Insert(Math.Min(i, panel.Children.Count), target);
        }

        // 收尾一致性校验：面板最终必须恰好是 finalOrder。
        // 走到这里若数量不符，说明上面哪一步漏了，宁可整体重建也不要带着错位继续。
        if (panel.Children.Count != finalOrder.Count)
        {
            Hosting.ReactorApplication.Trace(
                $"[reactor] 重排后数量不符，重建: natives={panel.Children.Count} " +
                $"expected={finalOrder.Count} panel={panel.GetType().Name} " +
                $"old=[{Describe(oldKids)}] new=[{Describe(newKids)}]");
            RebuildChildren(panel, oldKids, newKids);
        }
    }

    private static int IndexOfNative(Panel panel, UIElement target)
    {
        for (var i = 0; i < panel.Children.Count; i++)
        {
            if (ReferenceEquals(panel.Children[i], target))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>把 element 序列描述成便于比对的字符串（类型 + Key），仅用于诊断。</summary>
    private static string Describe(IReadOnlyList<Element> elements)
    {
        var parts = new string[elements.Count];
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            parts[i] = element.Key is { } key
                ? element.GetType().Name + "#" + key
                : element.GetType().Name;
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// 兜底路径：整段卸载 + 重建子节点。仅在原生树与 element 树已错位时调用。
    /// </summary>
    private void RebuildChildren(
        Panel panel,
        IReadOnlyList<Element> oldKids,
        IReadOnlyList<Element> newKids)
    {
        for (var i = panel.Children.Count - 1; i >= 0; i--)
        {
            var child = panel.Children[i];
            UnmountNative(child, i < oldKids.Count ? oldKids[i] : EmptyElement.Instance);
            panel.Children.RemoveAt(i);
        }

        foreach (var kid in newKids)
        {
            panel.Children.Add(Build(kid));
        }
    }

    /// <summary>
    /// 卸载整棵子树：解绑控件事件，并让子树里每个子组件都进入 unmounted。
    /// </summary>
    /// <remarks>
    /// 只解绑顶层控件是不够的：容器里的 <c>ComponentElement</c> 对应的
    /// <see cref="ComponentNode"/> 会一直留在注册表里、IsMounted 仍为 true，
    /// 于是这些已经被移出视觉树的组件还会响应状态更新、继续 patch 一棵游离的树。
    /// </remarks>
    private void UnmountTree(UIElement native, Element? element)
    {
        if (native is Border wrapper && _componentNodes.TryGetValue(wrapper, out var node))
        {
            // UnmountNative 会把 node 从注册表摘掉，先留住它当前渲染的 element。
            var child = node.CurrentElement;
            UnmountNative(native, element ?? EmptyElement.Instance);
            if (wrapper.Child is UIElement componentChild)
            {
                UnmountTree(componentChild, child);
            }

            return;
        }

        UnmountNative(native, element ?? EmptyElement.Instance);

        if (element is null)
        {
            return;
        }

        var handler = FindHandler(element);

        if (native is Panel panel && handler?.ChildrenOf(element) is { } kids)
        {
            var list = NonNull(kids);
            var count = Math.Min(panel.Children.Count, list.Count);
            for (var i = 0; i < count; i++)
            {
                UnmountTree(panel.Children[i], list[i]);
            }

            return;
        }

        var singleChild = handler?.SingleChildOf(element);
        if (singleChild is not null)
        {
            var content = native switch
            {
                ContentControl contentControl => contentControl.Content as UIElement,
                Border border => border.Child,
                _ => null,
            };

            if (content is not null)
            {
                UnmountTree(content, singleChild);
            }
        }
    }

    private static IElementHandler? FindHandler(Element element) =>
        ElementHandlerRegistry.TryGet(element.GetType(), out var handler) ? handler : null;

    /// <summary>卸载原生控件：解绑事件、清理组件节点。</summary>
    internal void UnmountNative(UIElement native, Element element)
    {
        if (native is Border wrapper && _componentNodes.TryGetValue(wrapper, out var node))
        {
            node.IsMounted = false;
            node.Context.RunCleanups();
            _componentNodes.Remove(wrapper);
            return;
        }

        FindHandler(element)?.Unmount(this, native);
    }

    // ── 事件重绑（由 handler 调用） ──────────────────────────────

    internal void RebindTextChanged(TextBox textBox, Action<string>? onChanged)
    {
        if (_textChanged.TryGetValue(textBox, out var existing))
        {
            textBox.TextChanged -= existing;
            _textChanged.Remove(textBox);
        }

        if (onChanged is null)
        {
            return;
        }

        TextChangedEventHandler handler = (s, _) => onChanged(((TextBox)s).Text);
        textBox.TextChanged += handler;
        _textChanged[textBox] = handler;
    }

    internal void RebindCheckBox(CheckBox checkBox, Action<bool>? onIsCheckedChanged)
    {
        if (_checkBoxChecked.TryGetValue(checkBox, out var existingChecked))
        {
            checkBox.Checked -= existingChecked;
            _checkBoxChecked.Remove(checkBox);
        }

        if (_checkBoxUnchecked.TryGetValue(checkBox, out var existingUnchecked))
        {
            checkBox.Unchecked -= existingUnchecked;
            _checkBoxUnchecked.Remove(checkBox);
        }

        if (onIsCheckedChanged is null)
        {
            return;
        }

        RoutedEventHandler checkedHandler = (_, _) => onIsCheckedChanged(true);
        RoutedEventHandler uncheckedHandler = (_, _) => onIsCheckedChanged(false);
        checkBox.Checked += checkedHandler;
        checkBox.Unchecked += uncheckedHandler;
        _checkBoxChecked[checkBox] = checkedHandler;
        _checkBoxUnchecked[checkBox] = uncheckedHandler;
    }

    internal void RebindSlider(Slider slider, Action<double>? onValueChanged)
    {
        if (_sliderValueChanged.TryGetValue(slider, out var existing))
        {
            slider.ValueChanged -= existing;
            _sliderValueChanged.Remove(slider);
        }

        if (onValueChanged is null)
        {
            return;
        }

        RangeBaseValueChangedEventHandler handler = (_, e) => onValueChanged(e.NewValue);
        slider.ValueChanged += handler;
        _sliderValueChanged[slider] = handler;
    }

    internal void RebindButtonClick(Button button, Action? onClick)
    {
        if (_buttonClicks.TryGetValue(button, out var existing))
        {
            button.Click -= existing;
            _buttonClicks.Remove(button);
        }

        if (onClick is null)
        {
            return;
        }

        RoutedEventHandler handler = (_, _) => onClick();
        button.Click += handler;
        _buttonClicks[button] = handler;
    }

    // ── 修饰符 ──────────────────────────────────────────────────

    private static void ApplyModifiers(UIElement native, ElementModifiers? modifiers)
    {
        if (modifiers is null || native is not FrameworkElement framework)
        {
            return;
        }

        // 官方 OneWay 的 diff-and-write。这里没有"上一轮 modifiers"可比对，
        // 所以走 SetLive 语义（与控件当前值比）——效果与拿旧元素 diff 完全相同：
        // 写同值不产生任何变化通知，也就不会触发重绘/重排。
        WriteIfChanged(() => framework.Margin, modifiers.Margin, value => framework.Margin = value);
        WriteIfChanged(() => framework.Width, modifiers.Width, value => framework.Width = value);
        WriteIfChanged(() => framework.Height, modifiers.Height, value => framework.Height = value);
        WriteIfChanged(
            () => framework.HorizontalAlignment,
            modifiers.HorizontalAlignment,
            value => framework.HorizontalAlignment = value);
        WriteIfChanged(
            () => framework.VerticalAlignment,
            modifiers.VerticalAlignment,
            value => framework.VerticalAlignment = value);
        WriteIfChanged(() => framework.MinWidth, modifiers.MinWidth, value => framework.MinWidth = value);
        WriteIfChanged(() => framework.MinHeight, modifiers.MinHeight, value => framework.MinHeight = value);
        WriteIfChanged(() => framework.MaxWidth, modifiers.MaxWidth, value => framework.MaxWidth = value);
        WriteIfChanged(() => framework.MaxHeight, modifiers.MaxHeight, value => framework.MaxHeight = value);
        WriteIfChanged(() => framework.Opacity, modifiers.Opacity, value => framework.Opacity = value);

        if (modifiers.IsVisible is { } isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            WriteIfChanged(() => framework.Visibility, visibility, value => framework.Visibility = value);
        }

        WriteRefIfChanged<object>(
            () => ToolTipService.GetToolTip(native),
            modifiers.ToolTip,
            value => ToolTipService.SetToolTip(native, value));
        WriteRefIfChanged(
            () => AutomationProperties.GetAutomationId(native),
            modifiers.AutomationId,
            value => AutomationProperties.SetAutomationId(native, value));
        WriteRefIfChanged(
            () => AutomationProperties.GetName(native),
            modifiers.AutomationName,
            value => AutomationProperties.SetName(native, value));

        ApplyBackground(native, modifiers);
        ApplyBorder(native, modifiers);
        ApplyStyle(framework, modifiers);
        ApplyTextAppearance(native, modifiers);
        ApplyTitleBar(framework, modifiers);

        if (modifiers.RequestedTheme is { } theme)
        {
            WriteIfChanged(() => framework.RequestedTheme, theme, value => framework.RequestedTheme = value);
        }

        if (modifiers.FontSize is { } fontSize)
        {
            switch (native)
            {
                case Control control:
                    WriteIfChanged(() => control.FontSize, fontSize, value => control.FontSize = value);
                    break;
                case TextBlock text:
                    WriteIfChanged(() => text.FontSize, fontSize, value => text.FontSize = value);
                    break;
                case FontIcon icon:
                    // FontIcon 是 IconElement（既不是 Control 也不是 TextBlock），
                    // 但它有 FontSize：漏掉这一档时 .FontSize() 会被静默丢弃。
                    WriteIfChanged(() => icon.FontSize, fontSize, value => icon.FontSize = value);
                    break;
            }
        }

        if (modifiers.Foreground is { } foreground)
        {
            ApplyForeground(native, foreground);
        }
        else if (modifiers.ForegroundColor is { } foregroundColor)
        {
            // 颜色 → Brush：颜色不变就复用控件上已有的 Brush，绝不每帧 new。
            // 新实例赋进依赖属性 = 一次真实变化 = 一次重绘（大面积前景色尤其明显）。
            switch (native)
            {
                case Control control:
                    PropWriter.SetColorBrush(control, Control.ForegroundProperty, foregroundColor);
                    break;
                case TextBlock text:
                    PropWriter.SetColorBrush(text, TextBlock.ForegroundProperty, foregroundColor);
                    break;
            }
        }

        if (modifiers.IsEnabled is { } isEnabled && native is Control enabledControl)
        {
            WriteIfChanged(() => enabledControl.IsEnabled, isEnabled, value => enabledControl.IsEnabled = value);
        }

        if (modifiers.Padding is { } padding)
        {
            ApplyPadding(native, padding);
        }
    }

    /// <summary>
    /// Padding 落在哪些原生类型上：<b>不是所有元素都有这个属性</b>。
    /// </summary>
    /// <remarks>
    /// UWP 里 <c>Padding</c> 不是 <see cref="FrameworkElement"/> 的属性，而是各自声明：
    /// <see cref="Control"/> / <see cref="TextBlock"/> / <see cref="Border"/> /
    /// <see cref="ContentPresenter"/> 有，面板里只有 <see cref="Grid"/>、
    /// <see cref="StackPanel"/>、<see cref="RelativePanel"/> 有（<see cref="Panel"/>
    /// 基类<b>没有</b>，<c>Canvas</c> / <c>Viewbox</c> 也没有）。
    /// 之前这里只写了 Control / TextBlock，于是 <c>HStack(...).Padding(20,16,20,0)</c>
    /// 这类"面板内边距"被静默丢弃——模板里面包屑栏的上/左内边距就是这么没的。
    /// 剩下的类型（Canvas 等）不是"写错了"而是"根本没有该属性"，按类型只报一次日志，
    /// 避免每帧刷屏也不至于无声无息。
    /// </remarks>
    private static void ApplyPadding(UIElement native, Thickness padding)
    {
        switch (native)
        {
            case Control control:
                WriteIfChanged(() => control.Padding, padding, value => control.Padding = value);
                break;
            case TextBlock text:
                WriteIfChanged(() => text.Padding, padding, value => text.Padding = value);
                break;
            case Border border:
                WriteIfChanged(() => border.Padding, padding, value => border.Padding = value);
                break;
            case ContentPresenter presenter:
                WriteIfChanged(() => presenter.Padding, padding, value => presenter.Padding = value);
                break;
            case StackPanel stack:
                WriteIfChanged(() => stack.Padding, padding, value => stack.Padding = value);
                break;
            case Grid grid:
                WriteIfChanged(() => grid.Padding, padding, value => grid.Padding = value);
                break;
            case RelativePanel relative:
                WriteIfChanged(() => relative.Padding, padding, value => relative.Padding = value);
                break;
            default:
                if (UnsupportedPaddingTypes.Add(native.GetType()))
                {
                    Hosting.ReactorApplication.Trace(
                        $"[reactor] 该类型没有 Padding 属性，.Padding() 被忽略: {native.GetType().FullName}");
                }

                break;
        }
    }

    /// <summary>值类型属性的 diff-and-write：与控件当前值相同就不写。</summary>
    private static void WriteIfChanged<T>(Func<T> read, T? value, Action<T> write)
        where T : struct
    {
        if (value is not { } typed)
        {
            return;
        }

        // 挂载期无条件写：控件刚 new 出来时读到的是依赖属性默认值（默认样式还没
        // 应用），"声明值 == 默认值"会被 diff 判成没变而跳过，随后样式值接管。
        if (!PropWriter.IsMounting && Equals(read(), typed))
        {
            return;
        }

        write(typed);
    }

    /// <summary>引用类型属性的 diff-and-write（按值比较：字符串等内容相等即视为未变）。</summary>
    private static void WriteRefIfChanged<T>(Func<T?> read, T? value, Action<T> write)
        where T : class
    {
        if (value is not { } typed)
        {
            return;
        }

        if (!PropWriter.IsMounting && Equals(read(), typed))
        {
            return;
        }

        write(typed);
    }

    // 命名样式：XAML 的 Style="{StaticResource …}" 等价物。
    // 每次（含 update）都执行，所以条件切换样式在就地更新路径上也生效——
    // 官方 Reactor 走 OnMount，只在首次挂载时应用。
    private static void ApplyStyle(FrameworkElement framework, ElementModifiers modifiers)
    {
        if (modifiers.StyleKey is not { } key)
        {
            return;
        }

        if (StyleSheet.Resolve(key) is not { } style)
        {
            return;
        }

        // 引用比较即可：同一 Style 实例重复赋值会触发一次多余的样式重应用，
        // 在列表里表现为滚动时的闪烁。
        if (!ReferenceEquals(framework.Style, style))
        {
            framework.Style = style;
        }
    }

    // 自定义标题栏拖拽区：XAML 里由 Window.Current.SetTitleBar(...) 完成。
    // 重复设置是幂等的，所以不做引用比较。
    private static void ApplyTitleBar(FrameworkElement framework, ElementModifiers modifiers)
    {
        if (modifiers.IsTitleBar == true)
        {
            Window.Current?.SetTitleBar(framework);
        }
    }

    private static void ApplyTextAppearance(UIElement native, ElementModifiers modifiers)
    {
        if (modifiers.TextWrapping is { } wrapping)
        {
            switch (native)
            {
                case TextBlock textBlock:
                    WriteIfChanged(
                        () => textBlock.TextWrapping, wrapping, value => textBlock.TextWrapping = value);
                    break;
                case TextBox textBox:
                    WriteIfChanged(
                        () => textBox.TextWrapping, wrapping, value => textBox.TextWrapping = value);
                    break;
                case RichTextBlock richText:
                    WriteIfChanged(
                        () => richText.TextWrapping, wrapping, value => richText.TextWrapping = value);
                    break;
            }
        }

        if (modifiers.FontWeight is { } weight)
        {
            switch (native)
            {
                case TextBlock textBlock:
                    WriteIfChanged(
                        () => textBlock.FontWeight, weight, value => textBlock.FontWeight = value);
                    break;
                case Control control:
                    WriteIfChanged(
                        () => control.FontWeight, weight, value => control.FontWeight = value);
                    break;
            }
        }

        if (modifiers.TextAlignment is { } alignment)
        {
            switch (native)
            {
                case TextBlock textBlock:
                    WriteIfChanged(
                        () => textBlock.TextAlignment, alignment, value => textBlock.TextAlignment = value);
                    break;
                case TextBox textBox:
                    WriteIfChanged(
                        () => textBox.TextAlignment, alignment, value => textBox.TextAlignment = value);
                    break;
            }
        }

        if (modifiers.MaxLines is { } maxLines && native is TextBlock limited)
        {
            WriteIfChanged(() => limited.MaxLines, maxLines, value => limited.MaxLines = value);
        }
    }

    /// <summary>Background 只有部分控件类型有该属性（Control / Panel / Border）。</summary>
    /// <remarks>
    /// Brush 是引用类型：内容一样但每次 <c>new</c> 都是新实例，无条件赋值 = 一次真实的
    /// 依赖属性变化 = 一次重绘。所以这里一律"内容不同才写"。
    /// </remarks>
    private static void ApplyBackground(UIElement native, ElementModifiers modifiers)
    {
        Brush? brush = modifiers.Background;
        if (brush is null && modifiers.BackgroundColor is { } color)
        {
            brush = new SolidColorBrush(color);
        }

        if (brush is null)
        {
            return;
        }

        switch (native)
        {
            case Control control:
                PropWriter.SetBrush(control, Control.BackgroundProperty, brush);
                break;
            case Panel panel:
                PropWriter.SetBrush(panel, Panel.BackgroundProperty, brush);
                break;
            case Border border:
                PropWriter.SetBrush(border, Border.BackgroundProperty, brush);
                break;
        }
    }

    private static void ApplyBorder(UIElement native, ElementModifiers modifiers)
    {
        if (modifiers.BorderBrush is { } borderBrush)
        {
            switch (native)
            {
                case Control control:
                    PropWriter.SetBrush(control, Control.BorderBrushProperty, borderBrush);
                    break;
                case Border border:
                    PropWriter.SetBrush(border, Border.BorderBrushProperty, borderBrush);
                    break;
            }
        }

        if (modifiers.BorderThickness is { } borderThickness)
        {
            switch (native)
            {
                case Control control:
                    WriteIfChanged(
                        () => control.BorderThickness,
                        borderThickness,
                        value => control.BorderThickness = value);
                    break;
                case Border border:
                    WriteIfChanged(
                        () => border.BorderThickness,
                        borderThickness,
                        value => border.BorderThickness = value);
                    break;
            }
        }
    }

    private static void ApplyForeground(UIElement native, Brush brush)
    {
        switch (native)
        {
            case Control control:
                PropWriter.SetBrush(control, Control.ForegroundProperty, brush);
                break;
            case TextBlock text:
                PropWriter.SetBrush(text, TextBlock.ForegroundProperty, brush);
                break;
        }
    }

    private static IReadOnlyList<Element> NonNull(IReadOnlyList<Element?> children)
    {
        var result = new List<Element>(children.Count);
        foreach (var child in children)
        {
            if (child is not null)
            {
                result.Add(child);
            }
        }

        return result;
    }
}
