using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>下拉框。</summary>
internal sealed class ComboBoxHandler : ElementHandler<ComboBoxElement, ComboBox>
{
    private static readonly WeakTable<ComboBox, Action<int>?> Callbacks = new();

    /// <summary><c>SelectedIndex</c> 受控：写回会触发 SelectionChanged，需要回声抑制。</summary>
    private static readonly EchoGuard SelectionEcho = new();

    protected override ComboBox Mount(Reconciler reconciler, ComboBoxElement element)
    {
        var combo = new ComboBox
        {
            Header = element.Header,
            PlaceholderText = element.PlaceholderText ?? string.Empty,
        };

        foreach (var item in element.Items ?? Array.Empty<string>())
        {
            combo.Items.Add(item);
        }

        if (element.SelectedIndex.HasValue)
        {
            combo.SelectedIndex = element.SelectedIndex.Value;
        }

        Rebind(combo, element.OnSelectedIndexChanged);
        return combo;
    }

    protected override void Update(
        Reconciler reconciler,
        ComboBoxElement oldElement,
        ComboBoxElement newElement,
        ComboBox control)
    {
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);
        PropWriter.Set(
            oldElement.PlaceholderText,
            newElement.PlaceholderText,
            value => control.PlaceholderText = value ?? string.Empty);

        if (!PropWriter.SequenceEqual(oldElement.Items, newElement.Items))
        {
            var previous = control.SelectedIndex;
            control.Items.Clear();
            foreach (var item in newElement.Items ?? Array.Empty<string>())
            {
                control.Items.Add(item);
            }

            if (previous >= 0 && previous < control.Items.Count)
            {
                control.SelectedIndex = previous;
            }
        }

        if (newElement.SelectedIndex.HasValue && control.SelectedIndex != newElement.SelectedIndex.Value)
        {
            SelectionEcho.Expect(control, newElement.SelectedIndex.Value);
            control.SelectedIndex = newElement.SelectedIndex.Value;
        }

        Rebind(control, Guard(control, newElement.OnSelectedIndexChanged));
    }

    protected override void Unmount(Reconciler reconciler, ComboBox control)
    {
        SelectionEcho.Forget(control);
        Callbacks.Remove(control);
    }

    /// <summary>把用户回调包一层：回读值 == 框架刚写入的值 → 是回声，不回调。</summary>
    private static Action<int>? Guard(ComboBox control, Action<int>? callback) =>
        callback is null
            ? null
            : value =>
            {
                if (SelectionEcho.Consume(control, value))
                {
                    return;
                }

                callback(value);
            };

    private static void Rebind(ComboBox control, Action<int>? callback)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = null;
            control.SelectionChanged += (s, _) =>
            {
                var cb = (ComboBox)s;
                if (Callbacks.TryGetValue(cb, out var current))
                {
                    current?.Invoke(cb.SelectedIndex);
                }
            };
        }

        Callbacks[control] = callback;
    }
}

/// <summary>开关（UWP 的 Toggled 事件是唯一的变更通知，没有 IsOnChanged）。</summary>
internal sealed class ToggleSwitchHandler : ElementHandler<ToggleSwitchElement, ToggleSwitch>
{
    private static readonly WeakTable<ToggleSwitch, Action<bool>?> Callbacks = new();

    /// <summary><c>IsOn</c> 受控：写回会触发 Toggled，需要回声抑制。</summary>
    private static readonly EchoGuard ToggleEcho = new();

    protected override ToggleSwitch Mount(Reconciler reconciler, ToggleSwitchElement element)
    {
        var toggle = new ToggleSwitch
        {
            Header = element.Header,
            OnContent = element.OnContent,
            OffContent = element.OffContent,
        };

        if (element.IsOn.HasValue)
        {
            toggle.IsOn = element.IsOn.Value;
        }

        Rebind(toggle, element.OnIsOnChanged);
        return toggle;
    }

    protected override void Update(
        Reconciler reconciler,
        ToggleSwitchElement oldElement,
        ToggleSwitchElement newElement,
        ToggleSwitch control)
    {
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);
        PropWriter.Set(oldElement.OnContent, newElement.OnContent, value => control.OnContent = value);
        PropWriter.Set(oldElement.OffContent, newElement.OffContent, value => control.OffContent = value);

        if (newElement.IsOn.HasValue && control.IsOn != newElement.IsOn.Value)
        {
            ToggleEcho.Expect(control, newElement.IsOn.Value);
            control.IsOn = newElement.IsOn.Value;
        }

        Rebind(control, Guard(control, newElement.OnIsOnChanged));
    }

    protected override void Unmount(Reconciler reconciler, ToggleSwitch control)
    {
        ToggleEcho.Forget(control);
        Callbacks.Remove(control);
    }

    private static Action<bool>? Guard(ToggleSwitch control, Action<bool>? callback) =>
        callback is null
            ? null
            : value =>
            {
                if (ToggleEcho.Consume(control, value))
                {
                    return;
                }

                callback(value);
            };

    private static void Rebind(ToggleSwitch control, Action<bool>? callback)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = null;
            control.Toggled += (s, _) =>
            {
                var toggle = (ToggleSwitch)s;
                if (Callbacks.TryGetValue(toggle, out var current))
                {
                    current?.Invoke(toggle.IsOn);
                }
            };
        }

        Callbacks[control] = callback;
    }
}

/// <summary>单选按钮：Checked / Unchecked 两个事件合成为 bool 回调。</summary>
internal sealed class RadioButtonHandler : ElementHandler<RadioButtonElement, RadioButton>
{
    private static readonly WeakTable<RadioButton, Action<bool>?> Callbacks = new();

    /// <summary><c>IsChecked</c> 受控：写回会触发 Checked/Unchecked，需要回声抑制。</summary>
    private static readonly EchoGuard CheckEcho = new();

    protected override RadioButton Mount(Reconciler reconciler, RadioButtonElement element)
    {
        var radio = new RadioButton { Content = element.Label };

        if (element.GroupName is { } group)
        {
            radio.GroupName = group;
        }

        if (element.IsChecked.HasValue)
        {
            radio.IsChecked = element.IsChecked.Value;
        }

        Rebind(radio, element.OnIsCheckedChanged);
        return radio;
    }

    protected override void Update(
        Reconciler reconciler,
        RadioButtonElement oldElement,
        RadioButtonElement newElement,
        RadioButton control)
    {
        PropWriter.Set(oldElement.Label, newElement.Label, value => control.Content = value);

        if (newElement.GroupName is { } group && oldElement.GroupName != group)
        {
            control.GroupName = group;
        }

        if (newElement.IsChecked.HasValue && control.IsChecked != newElement.IsChecked.Value)
        {
            CheckEcho.Expect(control, newElement.IsChecked.Value);
            control.IsChecked = newElement.IsChecked.Value;
        }

        Rebind(control, Guard(control, newElement.OnIsCheckedChanged));
    }

    protected override void Unmount(Reconciler reconciler, RadioButton control)
    {
        CheckEcho.Forget(control);
        Callbacks.Remove(control);
    }

    private static Action<bool>? Guard(RadioButton control, Action<bool>? callback) =>
        callback is null
            ? null
            : value =>
            {
                if (CheckEcho.Consume(control, value))
                {
                    return;
                }

                callback(value);
            };

    private static void Rebind(RadioButton control, Action<bool>? callback)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = null;
            control.Checked += (s, _) => Invoke((RadioButton)s, true);
            control.Unchecked += (s, _) => Invoke((RadioButton)s, false);
        }

        Callbacks[control] = callback;
    }

    private static void Invoke(RadioButton control, bool value)
    {
        if (Callbacks.TryGetValue(control, out var current))
        {
            current?.Invoke(value);
        }
    }
}

/// <summary>WinUI 2 的 RadioButtons 分组控件。</summary>
internal sealed class RadioButtonsHandler : ElementHandler<RadioButtonsElement, MuxControls.RadioButtons>
{
    private static readonly WeakTable<MuxControls.RadioButtons, Action<int>?> Callbacks = new();

    /// <summary><c>SelectedIndex</c> 受控：写回会触发 SelectionChanged，需要回声抑制。</summary>
    private static readonly EchoGuard SelectionEcho = new();

    protected override MuxControls.RadioButtons Mount(
        Reconciler reconciler,
        RadioButtonsElement element)
    {
        var control = new MuxControls.RadioButtons { Header = element.Header };

        foreach (var item in element.Items ?? Array.Empty<string>())
        {
            control.Items.Add(item);
        }

        if (element.SelectedIndex.HasValue)
        {
            control.SelectedIndex = element.SelectedIndex.Value;
        }

        Rebind(control, element.OnSelectedIndexChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        RadioButtonsElement oldElement,
        RadioButtonsElement newElement,
        MuxControls.RadioButtons control)
    {
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);

        if (!PropWriter.SequenceEqual(oldElement.Items, newElement.Items))
        {
            control.Items.Clear();
            foreach (var item in newElement.Items ?? Array.Empty<string>())
            {
                control.Items.Add(item);
            }
        }

        if (newElement.SelectedIndex.HasValue && control.SelectedIndex != newElement.SelectedIndex.Value)
        {
            SelectionEcho.Expect(control, newElement.SelectedIndex.Value);
            control.SelectedIndex = newElement.SelectedIndex.Value;
        }

        Rebind(control, Guard(control, newElement.OnSelectedIndexChanged));
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.RadioButtons control)
    {
        SelectionEcho.Forget(control);
        Callbacks.Remove(control);
    }

    private static Action<int>? Guard(MuxControls.RadioButtons control, Action<int>? callback) =>
        callback is null
            ? null
            : value =>
            {
                if (SelectionEcho.Consume(control, value))
                {
                    return;
                }

                callback(value);
            };

    private static void Rebind(MuxControls.RadioButtons control, Action<int>? callback)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = null;
            control.SelectionChanged += (s, _) =>
            {
                var rb = (MuxControls.RadioButtons)s;
                if (Callbacks.TryGetValue(rb, out var current))
                {
                    current?.Invoke(rb.SelectedIndex);
                }
            };
        }

        Callbacks[control] = callback;
    }
}

/// <summary>
/// 进度条。Value 为 null 表示不确定进度。
/// 与 ProgressRing 一样统一用 WinUI 2 的控件（<c>Microsoft.UI.Xaml.Controls.ProgressBar</c>），
/// 保证两者视觉风格一致、且都支持确定进度；UWP 原生 ProgressBar 的样式在
/// WinUI 2 主题资源下会退化成旧版外观。
/// </summary>
internal sealed class ProgressHandler : ElementHandler<ProgressElement, MuxControls.ProgressBar>
{
    protected override MuxControls.ProgressBar Mount(Reconciler reconciler, ProgressElement element)
    {
        var bar = new MuxControls.ProgressBar
        {
            Minimum = element.Minimum,
            Maximum = element.Maximum,
            IsIndeterminate = element.IsIndeterminate,
            ShowError = element.ShowError,
            ShowPaused = element.ShowPaused,
        };

        if (element.Value is { } value)
        {
            bar.Value = value;
        }

        return bar;
    }

    protected override void Update(
        Reconciler reconciler,
        ProgressElement oldElement,
        ProgressElement newElement,
        MuxControls.ProgressBar control)
    {
        PropWriter.Set(oldElement.Minimum, newElement.Minimum, value => control.Minimum = value);
        PropWriter.Set(oldElement.Maximum, newElement.Maximum, value => control.Maximum = value);
        PropWriter.Set(
            oldElement.IsIndeterminate,
            newElement.IsIndeterminate,
            value => control.IsIndeterminate = value);
        PropWriter.Set(oldElement.ShowError, newElement.ShowError, value => control.ShowError = value);
        PropWriter.Set(oldElement.ShowPaused, newElement.ShowPaused, value => control.ShowPaused = value);

        if (newElement.Value is { } value && control.Value != value)
        {
            control.Value = value;
        }
    }
}

/// <summary>
/// 进度环。UWP 原生 ProgressRing 只有 IsActive、不支持确定进度，
/// 因此这里用 WinUI 2 的 ProgressRing（同官方一样支持 Value/Min/Max）。
/// </summary>
internal sealed class ProgressRingHandler : ElementHandler<ProgressRingElement, MuxControls.ProgressRing>
{
    protected override MuxControls.ProgressRing Mount(Reconciler reconciler, ProgressRingElement element)
    {
        var ring = new MuxControls.ProgressRing
        {
            Minimum = element.Minimum,
            Maximum = element.Maximum,
            IsActive = element.IsActive,
            IsIndeterminate = element.IsIndeterminate,
        };

        if (element.Value is { } value)
        {
            ring.Value = value;
        }

        return ring;
    }

    protected override void Update(
        Reconciler reconciler,
        ProgressRingElement oldElement,
        ProgressRingElement newElement,
        MuxControls.ProgressRing control)
    {
        PropWriter.Set(oldElement.Minimum, newElement.Minimum, value => control.Minimum = value);
        PropWriter.Set(oldElement.Maximum, newElement.Maximum, value => control.Maximum = value);
        PropWriter.Set(oldElement.IsActive, newElement.IsActive, value => control.IsActive = value);
        PropWriter.Set(
            oldElement.IsIndeterminate,
            newElement.IsIndeterminate,
            value => control.IsIndeterminate = value);

        if (newElement.Value is { } value && control.Value != value)
        {
            control.Value = value;
        }
    }
}

/// <summary>图片。Source 是字符串，运行时解析为 Uri；非法 URI 静默忽略。</summary>
internal sealed class ImageHandler : ElementHandler<ImageElement, Image>
{
    protected override Image Mount(Reconciler reconciler, ImageElement element)
    {
        var image = new Image { Source = CreateSource(element.Source) };

        if (element.Width is { } width)
        {
            image.Width = width;
        }

        if (element.Height is { } height)
        {
            image.Height = height;
        }

        if (ParseStretch(element.Stretch) is { } stretch)
        {
            image.Stretch = stretch;
        }

        return image;
    }

    protected override void Update(
        Reconciler reconciler,
        ImageElement oldElement,
        ImageElement newElement,
        Image control)
    {
        if (!string.Equals(oldElement.Source, newElement.Source, StringComparison.Ordinal))
        {
            control.Source = CreateSource(newElement.Source);
        }

        PropWriter.Set(oldElement.Width, newElement.Width, value =>
        {
            if (value is { } width)
            {
                control.Width = width;
            }
        });

        PropWriter.Set(oldElement.Height, newElement.Height, value =>
        {
            if (value is { } height)
            {
                control.Height = height;
            }
        });

        PropWriter.Set(
            ParseStretch(oldElement.Stretch),
            ParseStretch(newElement.Stretch),
            value =>
            {
                if (value is { } stretch)
                {
                    control.Stretch = stretch;
                }
            });
    }

    private static Stretch? ParseStretch(string? value) =>
        value is not null && Enum.TryParse<Stretch>(value, true, out var parsed) ? parsed : null;

    private static ImageSource? CreateSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        try
        {
            var uri = source.Contains("://", StringComparison.Ordinal)
                ? new Uri(source)
                : new Uri("ms-appx:///" + source.TrimStart('/'));

            return new BitmapImage(uri);
        }
        catch (Exception)
        {
            // 对齐官方：非法 URI 静默忽略（Source 保持为空）。
            return null;
        }
    }
}

/// <summary>ListView / GridView 的公共行为（Items 为元素数组）。</summary>
internal abstract class ItemsViewHandler<TElement, TControl> : ElementHandler<TElement, TControl>
    where TElement : Element
    where TControl : ListViewBase
{
    private static readonly WeakTable<TControl, (Action<int>? Selection, Action<int>? Click)> Callbacks = new();

    protected abstract IReadOnlyList<Element?> ItemsOf(TElement element);
    protected abstract Optional<int> SelectedIndexOf(TElement element);
    protected abstract Action<int>? SelectionCallbackOf(TElement element);
    protected abstract Action<int>? ItemClickCallbackOf(TElement element);
    protected abstract ListViewSelectionMode ModeOf(TElement element);
    protected abstract string? HeaderOf(TElement element);

    protected override void Update(
        Reconciler reconciler,
        TElement oldElement,
        TElement newElement,
        TControl control)
    {
        var oldHeader = HeaderOf(oldElement);
        var newHeader = HeaderOf(newElement);
        if (!Equals(oldHeader, newHeader))
        {
            control.Header = newHeader;
        }

        var oldMode = ModeOf(oldElement);
        var newMode = ModeOf(newElement);
        if (oldMode != newMode)
        {
            control.SelectionMode = newMode;
        }

        var clickEnabled = ItemClickCallbackOf(newElement) is not null;
        if (control.IsItemClickEnabled != clickEnabled)
        {
            control.IsItemClickEnabled = clickEnabled;
        }

        reconciler.PatchItems(control, ItemsOf(oldElement), ItemsOf(newElement));

        if (SelectedIndexOf(newElement).HasValue &&
            control.SelectedIndex != SelectedIndexOf(newElement).Value)
        {
            control.SelectedIndex = SelectedIndexOf(newElement).Value;
        }

        Rebind(control, SelectionCallbackOf(newElement), ItemClickCallbackOf(newElement));
    }

    protected void Initialize(Reconciler reconciler, TControl control, TElement element)
    {
        control.Header = HeaderOf(element);
        control.SelectionMode = ModeOf(element);
        control.IsItemClickEnabled = ItemClickCallbackOf(element) is not null;

        foreach (var item in ItemsOf(element))
        {
            if (item is null)
            {
                continue;
            }

            control.Items.Add(reconciler.Build(item));
        }

        if (SelectedIndexOf(element).HasValue)
        {
            control.SelectedIndex = SelectedIndexOf(element).Value;
        }

        Rebind(control, SelectionCallbackOf(element), ItemClickCallbackOf(element));
    }

    protected override void Unmount(Reconciler reconciler, TControl control) => Callbacks.Remove(control);

    private static void Rebind(TControl control, Action<int>? selection, Action<int>? click)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = (null, null);
            control.SelectionChanged += (s, _) =>
            {
                var view = (TControl)s;
                if (Callbacks.TryGetValue(view, out var current))
                {
                    current.Selection?.Invoke(view.SelectedIndex);
                }
            };

            control.ItemClick += (s, e) =>
            {
                var view = (TControl)s;
                if (Callbacks.TryGetValue(view, out var current) && e.ClickedItem is UIElement clicked)
                {
                    current.Click?.Invoke(view.Items.IndexOf(clicked));
                }
            };
        }

        Callbacks[control] = (selection, click);
    }
}

internal sealed class ListViewHandler : ItemsViewHandler<ListViewElement, ListView>
{
    protected override ListView Mount(Reconciler reconciler, ListViewElement element)
    {
        var view = new ListView();
        Initialize(reconciler, view, element);
        return view;
    }

    protected override IReadOnlyList<Element?> ItemsOf(ListViewElement element) => element.Items;
    protected override Optional<int> SelectedIndexOf(ListViewElement element) => element.SelectedIndex;
    protected override Action<int>? SelectionCallbackOf(ListViewElement element) => element.OnSelectedIndexChanged;
    protected override Action<int>? ItemClickCallbackOf(ListViewElement element) => element.OnItemClick;
    protected override ListViewSelectionMode ModeOf(ListViewElement element) => element.SelectionMode;
    protected override string? HeaderOf(ListViewElement element) => element.Header;
}

internal sealed class GridViewHandler : ItemsViewHandler<GridViewElement, GridView>
{
    protected override GridView Mount(Reconciler reconciler, GridViewElement element)
    {
        var view = new GridView();
        Initialize(reconciler, view, element);
        return view;
    }

    protected override IReadOnlyList<Element?> ItemsOf(GridViewElement element) => element.Items;
    protected override Optional<int> SelectedIndexOf(GridViewElement element) => element.SelectedIndex;
    protected override Action<int>? SelectionCallbackOf(GridViewElement element) => element.OnSelectedIndexChanged;
    protected override Action<int>? ItemClickCallbackOf(GridViewElement element) => element.OnItemClick;
    protected override ListViewSelectionMode ModeOf(GridViewElement element) => element.SelectionMode;
    protected override string? HeaderOf(GridViewElement element) => element.Header;
}

/// <summary>WinUI 2 的 NavigationView：左侧菜单 + 内容区 + 返回按钮。</summary>
internal sealed class NavigationViewHandler : ElementHandler<NavigationViewElement, MuxControls.NavigationView>
{
    private static readonly WeakTable<
        MuxControls.NavigationView,
        (Action<int>? Selection, Action<int>? Invoked, Action? Back)> Callbacks = new();

    /// <summary>SelectedItem 受控：写回会触发 SelectionChanged，需要回声抑制。</summary>
    private static readonly EchoGuard SelectionEcho = new();

    protected override MuxControls.NavigationView Mount(
        Reconciler reconciler,
        NavigationViewElement element)
    {
        var nav = new MuxControls.NavigationView
        {
            Header = element.Header,
            IsPaneOpen = element.IsPaneOpen,
            IsSettingsVisible = element.IsSettingsVisible,
            IsBackButtonVisible = element.IsBackButtonVisible
                ? MuxControls.NavigationViewBackButtonVisible.Visible
                : MuxControls.NavigationViewBackButtonVisible.Collapsed,
            IsBackEnabled = element.IsBackEnabled,
            AlwaysShowHeader = element.AlwaysShowHeader,
            PaneDisplayMode = ToPaneMode(element.PaneDisplayMode),
        };

        ApplyMenuItems(nav, element.MenuItems);

        if (element.SelectedIndex >= 0 && element.SelectedIndex < nav.MenuItems.Count)
        {
            // 首次写入同样会触发 SelectionChanged（控件把它当"选中项变了"），
            // 不登记就会在启动瞬间回调一次 → 可能把初始页改掉。
            SelectionEcho.Expect(nav, element.SelectedIndex);
            nav.SelectedItem = nav.MenuItems[element.SelectedIndex];
        }

        nav.Content = element.Content is null ? null : reconciler.Build(element.Content);

        Rebind(nav, element.OnSelectedIndexChanged, element.OnItemInvoked, element.OnBackRequested);
        return nav;
    }

    protected override void Update(
        Reconciler reconciler,
        NavigationViewElement oldElement,
        NavigationViewElement newElement,
        MuxControls.NavigationView control)
    {
        // 只写真正变化的属性：NavigationView 会按用户操作自己开合 pane，
        // 每帧无条件写回 IsPaneOpen 会把用户的开合状态强行拽回来 → 导航条反复开合闪烁。
        if (!Equals(oldElement.Header, newElement.Header))
        {
            control.Header = newElement.Header;
        }

        if (oldElement.IsPaneOpen != newElement.IsPaneOpen)
        {
            control.IsPaneOpen = newElement.IsPaneOpen;
        }

        if (oldElement.IsSettingsVisible != newElement.IsSettingsVisible)
        {
            control.IsSettingsVisible = newElement.IsSettingsVisible;
        }

        if (oldElement.IsBackButtonVisible != newElement.IsBackButtonVisible)
        {
            control.IsBackButtonVisible = newElement.IsBackButtonVisible
                ? MuxControls.NavigationViewBackButtonVisible.Visible
                : MuxControls.NavigationViewBackButtonVisible.Collapsed;
        }

        if (oldElement.IsBackEnabled != newElement.IsBackEnabled)
        {
            control.IsBackEnabled = newElement.IsBackEnabled;
        }

        if (oldElement.PaneDisplayMode != newElement.PaneDisplayMode)
        {
            control.PaneDisplayMode = ToPaneMode(newElement.PaneDisplayMode);
        }

        if (oldElement.AlwaysShowHeader != newElement.AlwaysShowHeader)
        {
            control.AlwaysShowHeader = newElement.AlwaysShowHeader;
        }

        if (!SameMenuItems(oldElement.MenuItems, newElement.MenuItems))
        {
            ApplyMenuItems(control, newElement.MenuItems);
        }

        // 仅当 element 侧的选中项真的变了才回写控件，避免每次 patch 都重置
        // SelectedItem → 触发 SelectionChanged → setState 的回环（表现为导航条闪烁）。
        if (oldElement.SelectedIndex != newElement.SelectedIndex &&
            newElement.SelectedIndex >= 0 &&
            newElement.SelectedIndex < control.MenuItems.Count)
        {
            SelectionEcho.Expect(control, newElement.SelectedIndex);
            control.SelectedItem = control.MenuItems[newElement.SelectedIndex];
        }

        reconciler.PatchSingleChild(control, oldElement.Content, newElement.Content);
        Rebind(
            control,
            newElement.OnSelectedIndexChanged is null
                ? null
                : Guard(control, newElement.OnSelectedIndexChanged),
            newElement.OnItemInvoked,
            newElement.OnBackRequested);
    }

    /// <summary>SelectionChanged 回调包装：回读下标 == 框架刚写入的下标 → 是回声，不回调。</summary>
    private static Action<int>? Guard(MuxControls.NavigationView control, Action<int> callback) =>
        value =>
        {
            if (SelectionEcho.Consume(control, value))
            {
                return;
            }

            callback(value);
        };

    protected override Element? SingleChildOf(NavigationViewElement element) => element.Content;

    protected override void Unmount(Reconciler reconciler, MuxControls.NavigationView control)
    {
        SelectionEcho.Forget(control);
        Callbacks.Remove(control);
    }

    private static void ApplyMenuItems(
        MuxControls.NavigationView nav,
        IReadOnlyList<NavigationViewItemData> items)
    {
        nav.MenuItems.Clear();

        var list = items ?? Array.Empty<NavigationViewItemData>();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not { } data)
            {
                continue;
            }

            var item = new MuxControls.NavigationViewItem
            {
                Content = data.Content,
                Tag = i,
            };

            if (data.Icon is { } glyph)
            {
                item.Icon = new FontIcon { Glyph = glyph };
            }

            nav.MenuItems.Add(item);
        }
    }

    private static bool SameMenuItems(
        IReadOnlyList<NavigationViewItemData>? a,
        IReadOnlyList<NavigationViewItemData>? b)
    {
        a ??= Array.Empty<NavigationViewItemData>();
        b ??= Array.Empty<NavigationViewItemData>();
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static MuxControls.NavigationViewPaneDisplayMode ToPaneMode(NavPaneDisplayMode mode) =>
        mode switch
        {
            NavPaneDisplayMode.Left => MuxControls.NavigationViewPaneDisplayMode.Left,
            NavPaneDisplayMode.LeftMinimal => MuxControls.NavigationViewPaneDisplayMode.LeftMinimal,
            NavPaneDisplayMode.Top => MuxControls.NavigationViewPaneDisplayMode.Top,
            NavPaneDisplayMode.Auto => MuxControls.NavigationViewPaneDisplayMode.Auto,
            _ => MuxControls.NavigationViewPaneDisplayMode.LeftCompact,
        };

    private static void Rebind(
        MuxControls.NavigationView control,
        Action<int>? selection,
        Action<int>? invoked,
        Action? back)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = (null, null, null);

            control.SelectionChanged += (_, args) =>
            {
                if (args.SelectedItemContainer is MuxControls.NavigationViewItem item &&
                    item.Tag is int index &&
                    Callbacks.TryGetValue(control, out var current))
                {
                    current.Selection?.Invoke(index);
                }
            };

            // ItemInvoked 与 SelectionChanged 的区别：点已选中项也会触发，
            // 模板用它做"重复点击主页 → 回到主页"，SelectionChanged 不会回调。
            control.ItemInvoked += (_, args) =>
            {
                if (!Callbacks.TryGetValue(control, out var current))
                {
                    return;
                }

                var index = args.InvokedItemContainer is MuxControls.NavigationViewItem item &&
                            item.Tag is int tagged
                    ? tagged
                    : -1; // Settings 项没有我们写入的 Tag

                current.Invoked?.Invoke(index);
            };

            control.BackRequested += (_, _) =>
            {
                if (Callbacks.TryGetValue(control, out var current))
                {
                    current.Back?.Invoke();
                }
            };
        }

        Callbacks[control] = (selection, invoked, back);
    }
}
