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
            TextBlockElement text => new TextBlock { Text = text.Text },
            ButtonElement button => BuildButton(button),
            StackPanelElement stack => BuildStack(stack),
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

            void DoRerender()
            {
                if (!node.IsMounted)
                {
                    return;
                }

                node.Instance.BeginRender();
                var nextElement = node.Instance.Render();
                node.Instance.EndRender();

                if (node.CurrentElement is null || wrapper.Child is not UIElement childNative ||
                    !CanPatch(node.CurrentElement, nextElement))
                {
                    wrapper.Child = Build(nextElement);
                }
                else
                {
                    Patch(childNative, node.CurrentElement, nextElement);
                }

                node.CurrentElement = nextElement;
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

    /// <summary>重渲染组件子树（在 UI 线程上调用）。</summary>
    private void RerenderComponent(ComponentNode node, Border wrapper)
    {
        if (!node.IsMounted)
        {
            return;
        }

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
                text.Text = nextText.Text;
                break;

            case Button button when next is ButtonElement nextButton:
                button.Content = nextButton.Label;
                RebindButtonClick(button, nextButton.OnClick);
                break;

            case StackPanel panel when next is StackPanelElement nextStack:
                panel.Orientation = nextStack.Orientation;
                PatchChildren(panel,
                    ((StackPanelElement)old).Children, nextStack.Children);
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
                    ((ScrollViewerElement)old).Content,
                    nextScroll.Content);
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

    private StackPanel BuildStack(StackPanelElement stack)
    {
        var panel = new StackPanel { Orientation = stack.Orientation };
        foreach (var child in NonNull(stack.Children))
        {
            panel.Children.Add(Build(child));
        }

        return panel;
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

        if (scroll.Content is not null)
        {
            native.Content = Build(scroll.Content);
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
        StackPanel panel,
        IReadOnlyList<Element?> oldChildren,
        IReadOnlyList<Element?> nextChildren)
    {
        var oldKids = NonNull(oldChildren);
        var newKids = NonNull(nextChildren);

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

        for (var i = 0; i < oldKids.Count; i++)
        {
            var el = oldKids[i];
            var native = panel.Children[i];

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
                UnmountNative(child, oldKids[i]);
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
    }

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
