using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 把 <see cref="Element"/> 描述树映射到真实 XAML 控件：
/// mount 时按 Element 类型创建控件；update 时同类型就地改属性、
/// 容器子节点按位置对齐复用，类型不同才替换。
/// 每个 ReactorHost 持有一个实例（保存事件处理器映射）。
/// </summary>
internal sealed class Reconciler
{
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

    private readonly Dictionary<Button, RoutedEventHandler> _buttonClicks = new();
    private readonly Dictionary<Border, ComponentNode> _componentNodes = new();
    private readonly Dictionary<TextBox, TextChangedEventHandler> _textChanged = new();
    private readonly Dictionary<CheckBox, RoutedEventHandler> _checkBoxChecked = new();
    private readonly Dictionary<CheckBox, RoutedEventHandler> _checkBoxUnchecked = new();
    private readonly Dictionary<Slider, RangeBaseValueChangedEventHandler> _sliderValueChanged = new();

    public UIElement Build(Element element)
    {
        UIElement native = element switch
        {
            TextBlockElement text => new TextBlock { Text = text.Content },
            ButtonElement button => BuildButton(button),
            StackElement stack => BuildStack(stack),
            // EmptyElement 在官方实现里对应 null（不产生控件）；UWP 侧 Build 必须返回
            // UIElement，因此退化为一个零尺寸的 Grid 占位。
            EmptyElement => new Grid(),
            GroupElement group => BuildGroup(group),
            InfoBarElement infoBar => new MuxControls.InfoBar
            {
                Message = infoBar.Message,
                Severity = infoBar.Severity,
                IsOpen = true
            },
            TextBoxElement textBox => BuildTextBox(textBox),
            CheckBoxElement checkBox => BuildCheckBox(checkBox),
            SliderElement slider => BuildSlider(slider),
            ScrollViewerElement scroll => BuildScroll(scroll),
            ComponentElement comp => BuildComponent(comp),
            _ => throw new NotSupportedException(
                $"不支持的元素类型: {element.GetType().FullName}")
        };

        ApplyModifiers(native, element.Modifiers);
        return native;
    }

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

                    node.Instance.BeginRender();
                    var nextElement = node.Instance.Render();
                    node.Instance.EndRender();

                    if (node.CurrentElement is null ||
                        wrapper.Child is not UIElement childNative ||
                        !CanPatch(node.CurrentElement, nextElement))
                    {
                        wrapper.Child = Build(nextElement);
                    }
                    else
                    {
                        Patch(childNative, node.CurrentElement, nextElement);
                    }

                    node.CurrentElement = nextElement;
                });
            }
        }

        component.Context.RequestRerender = RequestComponentRerender;
        component.Context.IsMountedCheck = () => node.IsMounted;

        // 首次渲染子组件
        node.Instance.BeginRender();
        var childElement = node.Instance.Render();
        node.Instance.EndRender();

        wrapper.Child = Build(childElement);
        node.CurrentElement = childElement;

        return wrapper;
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
            wrapper.Child = newWrapper.Child;
            if (newWrapper.Child is not null)
            {
                // 转移注册表
                if (_componentNodes.TryGetValue(newWrapper, out var newNode))
                {
                    _componentNodes.Remove(newWrapper);
                    newNode.NativeRoot = wrapper;
                    _componentNodes[wrapper] = newNode;
                }
            }
            return;
        }

        if (node.ComponentType != nextComp.ComponentType)
        {
            // 组件类型变化：整子树卸载重挂
            node.IsMounted = false;
            node.Context.RunCleanups();
            _componentNodes.Remove(wrapper);

            var newWrapper = BuildComponent(nextComp);
            wrapper.Child = newWrapper.Child;
            if (_componentNodes.TryGetValue(newWrapper, out var newNode))
            {
                _componentNodes.Remove(newWrapper);
                newNode.NativeRoot = wrapper;
                _componentNodes[wrapper] = newNode;
            }
            return;
        }

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

    /// <summary>
    /// 执行一轮渲染 / patch，并把 <see cref="_inRenderPass"/> 置位，
    /// 使过程中同步触发的状态更新被推迟而不是重入。
    /// </summary>
    private void RunPass(Action pass)
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

        RunPass(() => RerenderComponentCore(node, wrapper));
    }

    private void RerenderComponentCore(ComponentNode node, Border wrapper)
    {
        node.Instance.BeginRender();
        var nextElement = node.Instance.Render();
        node.Instance.EndRender();

        if (node.CurrentElement is null || wrapper.Child is not UIElement childNative ||
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
    /// 将旧描述对应的真实控件就地更新为新描述。
    /// 调用方需保证 old/next 是同一 Element 记录类型。
    /// </summary>
    public void Patch(UIElement native, Element old, Element next)
    {
        switch (native)
        {
            case TextBlock text when next is TextBlockElement nextText:
                text.Text = nextText.Content;
                break;

            case Button button when next is ButtonElement nextButton:
                button.Content = nextButton.Label;
                RebindButtonClick(button, nextButton.OnClick);
                break;

            case StackPanel panel when next is StackElement nextStack:
                panel.Orientation = nextStack.Orientation;
                if (nextStack.Spacing is { } spacing)
                {
                    panel.Spacing = spacing;
                }

                PatchChildren(panel, ((StackElement)old).Children, nextStack.Children);
                break;

            case Grid grid when next is GroupElement nextGroup:
                PatchChildren(grid, ((GroupElement)old).Children, nextGroup.Children);
                break;

            case Grid when next is EmptyElement:
                // 占位元素，无需更新。
                break;

            case MuxControls.InfoBar infoBar when next is InfoBarElement nextInfo:
                infoBar.Message = nextInfo.Message;
                infoBar.Severity = nextInfo.Severity;
                infoBar.IsOpen = true;
                break;

            case TextBox textBox when next is TextBoxElement nextTextBox:
                textBox.PlaceholderText = nextTextBox.PlaceholderText;
                textBox.Header = nextTextBox.Header;
                if (nextTextBox.Value.HasValue && textBox.Text != nextTextBox.Value.Value)
                {
                    textBox.Text = nextTextBox.Value.Value ?? string.Empty;
                }
                RebindTextChanged(textBox, nextTextBox.OnChanged);
                break;

            case CheckBox checkBox when next is CheckBoxElement nextCheck:
                checkBox.Content = nextCheck.Label;
                if (nextCheck.IsChecked.HasValue && checkBox.IsChecked != nextCheck.IsChecked.Value)
                {
                    checkBox.IsChecked = nextCheck.IsChecked.Value;
                }
                RebindCheckBox(checkBox, nextCheck.OnIsCheckedChanged);
                break;

            case Slider slider when next is SliderElement nextSlider:
                slider.Minimum = nextSlider.Min;
                slider.Maximum = nextSlider.Max;
                if (nextSlider.Value.HasValue && slider.Value != nextSlider.Value.Value)
                {
                    slider.Value = nextSlider.Value.Value;
                }
                RebindSlider(slider, nextSlider.OnValueChanged);
                break;

            case ScrollViewer scrollViewer when next is ScrollViewerElement nextScroll:
                PatchScrollContent(
                    scrollViewer,
                    ((ScrollViewerElement)old).Child,
                    nextScroll.Child);
                break;

            case Border wrapper when next is ComponentElement nextComp:
                PatchComponent(wrapper, nextComp);
                break;

            default:
                throw new NotSupportedException(
                    $"无法更新控件 {native.GetType().FullName} -> {next.GetType().FullName}");
        }

        ApplyModifiers(native, next.Modifiers);
    }

    /// <summary>两个描述是否可以复用同一个真实控件（目前按记录类型判断）。</summary>
    public static bool CanPatch(Element? old, Element? next) =>
        old is not null && next is not null && old.GetType() == next.GetType();

    private Button BuildButton(ButtonElement button)
    {
        var native = new Button { Content = button.Label };
        RebindButtonClick(native, button.OnClick);
        return native;
    }

    private StackPanel BuildStack(StackElement stack)
    {
        var panel = new StackPanel { Orientation = stack.Orientation };
        if (stack.Spacing is { } spacing)
        {
            panel.Spacing = spacing;
        }

        foreach (var child in NonNull(stack.Children))
        {
            panel.Children.Add(Build(child));
        }

        return panel;
    }

    /// <summary>GroupElement 渲染为裸 Grid：不引入额外布局策略。</summary>
    private Grid BuildGroup(GroupElement group)
    {
        var grid = new Grid();
        foreach (var child in NonNull(group.Children))
        {
            grid.Children.Add(Build(child));
        }

        return grid;
    }

    private TextBox BuildTextBox(TextBoxElement textBox)
    {
        var native = new TextBox
        {
            PlaceholderText = textBox.PlaceholderText,
            Header = textBox.Header,
        };

        if (textBox.Value.HasValue)
        {
            native.Text = textBox.Value.Value ?? string.Empty;
        }

        RebindTextChanged(native, textBox.OnChanged);
        return native;
    }

    private CheckBox BuildCheckBox(CheckBoxElement checkBox)
    {
        var native = new CheckBox { Content = checkBox.Label };

        if (checkBox.IsChecked.HasValue)
        {
            native.IsChecked = checkBox.IsChecked.Value;
        }

        RebindCheckBox(native, checkBox.OnIsCheckedChanged);
        return native;
    }

    private Slider BuildSlider(SliderElement slider)
    {
        var native = new Slider
        {
            Minimum = slider.Min,
            Maximum = slider.Max,
        };

        if (slider.Value.HasValue)
        {
            native.Value = slider.Value.Value;
        }

        RebindSlider(native, slider.OnValueChanged);
        return native;
    }

    private ScrollViewer BuildScroll(ScrollViewerElement scroll)
    {
        var native = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        if (scroll.Child is not null)
        {
            native.Content = Build(scroll.Child);
        }

        return native;
    }

    /// <summary>就地更新 ScrollViewer 的单个 Content（同类型就地 patch，否则卸载重建）。</summary>
    private void PatchScrollContent(ScrollViewer scrollViewer, Element? oldContent, Element? newContent)
    {
        if (newContent is null)
        {
            if (scrollViewer.Content is UIElement existing)
            {
                UnmountNative(existing, oldContent!);
                scrollViewer.Content = null;
            }

            return;
        }

        if (scrollViewer.Content is not UIElement childNative)
        {
            scrollViewer.Content = Build(newContent);
        }
        else if (oldContent is not null && CanPatch(oldContent, newContent))
        {
            Patch(childNative, oldContent, newContent);
        }
        else
        {
            if (oldContent is not null)
            {
                UnmountNative(childNative, oldContent);
            }

            scrollViewer.Content = Build(newContent);
        }
    }

    private void PatchChildren(
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

        // 收集所有需要保留的原生控件（key 匹配 + 位置对齐）
        var toRemove = new HashSet<UIElement>(panel.Children);
        var newNatives = new UIElement?[newKids.Count];

        // 处理 key 匹配的子节点
        foreach (var (key, newEl, newIdx) in newKeyed)
        {
            if (oldKeyed.TryGetValue(key, out var oldPair))
            {
                oldKeyed.Remove(key); // 一个 old key 只匹配一次，重复 key 走新增分支，避免同一原生控件复用两次导致越界
                toRemove.Remove(oldPair.Native);

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
                toRemove.Remove(oldPair.Native);

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

        // 卸载并移除所有未被复用的控件（真正删除/替换的，detach 是必要的）
        for (var i = panel.Children.Count - 1; i >= 0; i--)
        {
            var child = panel.Children[i];
            if (toRemove.Contains(child))
            {
                if (elementByNative.TryGetValue(child, out var oldElement))
                {
                    UnmountNative(child, oldElement);
                }

                panel.Children.RemoveAt(i);
            }
        }

        // 就地重排：复用控件保持原位（不 detach，保留焦点/状态），
        // 新建控件插入到目标位置，仅在顺序真正变化时才移动复用控件。
        for (var newIdx = 0; newIdx < newNatives.Length; newIdx++)
        {
            var target = newNatives[newIdx];
            if (target is null)
            {
                continue;
            }

            if (newIdx < panel.Children.Count && ReferenceEquals(panel.Children[newIdx], target))
            {
                continue; // 目标已在正确位置
            }

            if (panel.Children.Contains(target))
            {
                // 复用控件，位置需要移动
                panel.Children.Remove(target);
                panel.Children.Insert(newIdx, target);
            }
            else
            {
                // 新建控件
                panel.Children.Insert(newIdx, target);
            }
        }

        // 收尾一致性校验：面板最终必须恰好是 newNatives。
        // 走到这里若数量不符，说明上面哪一步漏了，宁可整体重建也不要带着错位继续。
        if (panel.Children.Count != newNatives.Length)
        {
            Hosting.ReactorApplication.Trace(
                $"[reactor] 重排后数量不符，重建: natives={panel.Children.Count} " +
                $"expected={newNatives.Length} panel={panel.GetType().Name}");
            RebuildChildren(panel, oldKids, newKids);
        }
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

        switch (native)
        {
            case Panel panel when ChildrenOf(element) is { } kids:
                var list = NonNull(kids);
                var count = Math.Min(panel.Children.Count, list.Count);
                for (var i = 0; i < count; i++)
                {
                    UnmountTree(panel.Children[i], list[i]);
                }

                break;

            case ScrollViewer scrollViewer
                when element is ScrollViewerElement scrollElement &&
                     scrollViewer.Content is UIElement content:
                UnmountTree(content, scrollElement.Child);
                break;
        }
    }

    /// <summary>取容器中子元素描述（Stack / Group 才有）。</summary>
    private static IReadOnlyList<Element?>? ChildrenOf(Element? element) => element switch
    {
        StackElement stack => stack.Children,
        GroupElement group => group.Children,
        _ => null,
    };

    /// <summary>卸载原生控件：解绑事件、清理组件节点。</summary>
    private void UnmountNative(UIElement native, Element element)
    {
        switch (native)
        {
            case Button button:
                RebindButtonClick(button, null);
                break;

            case Border wrapper when _componentNodes.TryGetValue(wrapper, out var node):
                node.IsMounted = false;
                node.Context.RunCleanups();
                _componentNodes.Remove(wrapper);
                break;

            case TextBox textBox:
                RebindTextChanged(textBox, null);
                break;

            case CheckBox checkBox:
                RebindCheckBox(checkBox, null);
                break;

            case Slider slider:
                RebindSlider(slider, null);
                break;
        }
    }

    private void RebindTextChanged(TextBox textBox, Action<string>? onChanged)
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

    private void RebindCheckBox(CheckBox checkBox, Action<bool>? onIsCheckedChanged)
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

    private void RebindSlider(Slider slider, Action<double>? onValueChanged)
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

    private void RebindButtonClick(Button button, Action? onClick)
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

    private static void ApplyModifiers(UIElement native, ElementModifiers? modifiers)
    {
        if (modifiers is null || native is not FrameworkElement framework)
        {
            return;
        }

        if (modifiers.Margin is { } margin)
        {
            framework.Margin = margin;
        }

        if (modifiers.Width is { } width)
        {
            framework.Width = width;
        }

        if (modifiers.Height is { } height)
        {
            framework.Height = height;
        }

        if (modifiers.HorizontalAlignment is { } horizontal)
        {
            framework.HorizontalAlignment = horizontal;
        }

        if (modifiers.VerticalAlignment is { } vertical)
        {
            framework.VerticalAlignment = vertical;
        }

        if (modifiers.MinWidth is { } minWidth)
        {
            framework.MinWidth = minWidth;
        }

        if (modifiers.MinHeight is { } minHeight)
        {
            framework.MinHeight = minHeight;
        }

        if (modifiers.MaxWidth is { } maxWidth)
        {
            framework.MaxWidth = maxWidth;
        }

        if (modifiers.MaxHeight is { } maxHeight)
        {
            framework.MaxHeight = maxHeight;
        }

        if (modifiers.IsVisible is { } isVisible)
        {
            framework.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        if (modifiers.Opacity is { } opacity)
        {
            framework.Opacity = opacity;
        }

        if (modifiers.ToolTip is { } toolTip)
        {
            ToolTipService.SetToolTip(native, toolTip);
        }

        if (modifiers.AutomationId is { } automationId)
        {
            AutomationProperties.SetAutomationId(native, automationId);
        }

        ApplyBackground(native, modifiers);
        ApplyBorder(native, modifiers);

        if (modifiers.FontSize is { } fontSize)
        {
            switch (native)
            {
                case Control control:
                    control.FontSize = fontSize;
                    break;
                case TextBlock text:
                    text.FontSize = fontSize;
                    break;
            }
        }

        if (modifiers.Foreground is { } foreground)
        {
            ApplyForeground(native, foreground);
        }
        else if (modifiers.ForegroundColor is { } foregroundColor)
        {
            ApplyForeground(native, new SolidColorBrush(foregroundColor));
        }

        if (modifiers.IsEnabled is { } isEnabled && native is Control enabledControl)
        {
            enabledControl.IsEnabled = isEnabled;
        }

        if (modifiers.AutomationName is { } automationName)
        {
            AutomationProperties.SetName(native, automationName);
        }

        if (modifiers.Padding is { } padding)
        {
            switch (native)
            {
                case Control control:
                    control.Padding = padding;
                    break;
                case TextBlock text:
                    text.Padding = padding;
                    break;
            }
        }
    }

    /// <summary>Background 只有部分控件类型有该属性（Control / Panel / Border）。</summary>
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
                control.Background = brush;
                break;
            case Panel panel:
                panel.Background = brush;
                break;
            case Border border:
                border.Background = brush;
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
                    control.BorderBrush = borderBrush;
                    break;
                case Border border:
                    border.BorderBrush = borderBrush;
                    break;
            }
        }

        if (modifiers.BorderThickness is { } borderThickness)
        {
            switch (native)
            {
                case Control control:
                    control.BorderThickness = borderThickness;
                    break;
                case Border border:
                    border.BorderThickness = borderThickness;
                    break;
            }
        }
    }

    private static void ApplyForeground(UIElement native, Brush brush)
    {
        switch (native)
        {
            case Control control:
                control.Foreground = brush;
                break;
            case TextBlock text:
                text.Foreground = brush;
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
