using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// UWP 原生的 <c>ToggleButton</c>：受控 <c>IsChecked</c>。
/// </summary>
/// <remarks>
/// 与 <c>CheckBox</c> 同一套形状（<c>IsChecked</c> + <c>Checked</c> / <c>Unchecked</c>），
/// 差别只是外观。受控写法因此也照抄 <c>CheckBoxHandler</c>：先罩窗写非受控属性、
/// 再 <c>Expect</c> 后写受控值、最后换回调。
/// <para>
/// 回执是 <b>两个</b>事件（<c>Checked</c> / <c>Unchecked</c>），所以"先摘后挂"也是
/// 摘两个挂两个——只摘一个的话，每轮重渲染会在另一个事件上叠一层。
/// </para>
/// </remarks>
internal sealed class ToggleButtonHandler : ElementHandler<ToggleButtonElement, ToggleButton>
{
    private static readonly EchoGuard CheckEcho = new();

    private static readonly WeakTable<ToggleButton, (RoutedEventHandler? Checked, RoutedEventHandler? Unchecked)>
        Handlers = new();

    protected override ToggleButton Mount(Reconciler reconciler, ToggleButtonElement element)
    {
        var control = new ToggleButton();

        if (element.IsEnabled is { } enabled)
        {
            control.IsEnabled = enabled;
        }

        ApplyContent(reconciler, control, null, element);

        // 顺序同 CheckBoxHandler：先挂带闸的订阅，再写受控值。
        Rebind(control, element.OnIsCheckedChanged);

        if (element.IsChecked.HasValue)
        {
            control.IsChecked = element.IsChecked.Value;
        }

        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        ToggleButtonElement oldElement,
        ToggleButtonElement newElement,
        ToggleButton control)
    {
        using (CheckEcho.Silence(control))
        {
            ApplyContent(reconciler, control, oldElement, newElement);
            PropWriter.Set(
                oldElement.IsEnabled, newElement.IsEnabled, value => control.IsEnabled = value ?? true);
        }

        if (newElement.IsChecked.HasValue && control.IsChecked != newElement.IsChecked.Value)
        {
            CheckEcho.Expect(control, newElement.IsChecked.Value);
            control.IsChecked = newElement.IsChecked.Value;

            // 回调为空时 Checked / Unchecked 已被退订，没人领的登记要撤销。
            CheckEcho.CancelIfUnconsumed(control);
        }

        Rebind(control, newElement.OnIsCheckedChanged);
    }

    protected override void Unmount(Reconciler reconciler, ToggleButton control)
    {
        CheckEcho.Forget(control);
        Rebind(control, null);
    }

    protected override Element? SingleChildOf(ToggleButtonElement element) => element.Content;

    private static void ApplyContent(
        Reconciler reconciler,
        ToggleButton control,
        ToggleButtonElement? oldElement,
        ToggleButtonElement newElement)
    {
        if (newElement.Content is not null || oldElement?.Content is not null)
        {
            reconciler.PatchSingleChild(control, oldElement?.Content, newElement.Content);
            return;
        }

        PropWriter.Set(oldElement?.Label, newElement.Label, value => control.Content = value);
    }

    private static void Rebind(ToggleButton control, Action<bool>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            if (existing.Checked is { } checkedHandler)
            {
                control.Checked -= checkedHandler;
            }

            if (existing.Unchecked is { } uncheckedHandler)
            {
                control.Unchecked -= uncheckedHandler;
            }

            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        RoutedEventHandler onChecked = (_, _) => Fire(control, callback, true);
        RoutedEventHandler onUnchecked = (_, _) => Fire(control, callback, false);

        control.Checked += onChecked;
        control.Unchecked += onUnchecked;
        Handlers.Set(control, (onChecked, onUnchecked));
    }

    /// <summary>
    /// 两个事件共用<b>一个</b> <c>Consume</c>：回声登记对每个受控站点只有一份，
    /// "勾上"与"取消"是同一份登记的两种取值，不是两个站点。写成两份的话，
    /// 其中一份被改坏时另一份还在——契约里那条"逐行变异"就抓不到它。
    /// </summary>
    private static void Fire(ToggleButton control, Action<bool> callback, bool value)
    {
        if (CheckEcho.Consume(control, value))
        {
            return;
        }

        callback(value);
    }
}

/// <summary>
/// UWP 原生的 <c>RepeatButton</c>：按住不放会连续触发 <c>Click</c>。
/// </summary>
/// <remarks>
/// <b>这里一个 <c>EchoGuard</c> 都没有</b>，因为这里压根没有受控值：
/// 它继承 <c>ButtonBase</c>，只有 <c>Click</c>，没有选中态。
/// "按住连发"是控件自己的事（<c>Delay</c> / <c>Interval</c> 决定节奏），
/// 我们只是把每一次 <c>Click</c> 原样交给回调——
/// 所以回调会被<b>反复</b>调用，这是这个控件的全部意义，不是 bug。
/// </remarks>
internal sealed class RepeatButtonHandler : ElementHandler<RepeatButtonElement, RepeatButton>
{
    private static readonly WeakTable<RepeatButton, RoutedEventHandler> Clicks = new();

    protected override RepeatButton Mount(Reconciler reconciler, RepeatButtonElement element)
    {
        var control = new RepeatButton();

        if (element.Delay is { } delay)
        {
            control.Delay = delay;
        }

        if (element.Interval is { } interval)
        {
            control.Interval = interval;
        }

        if (element.IsEnabled is { } enabled)
        {
            control.IsEnabled = enabled;
        }

        ApplyContent(reconciler, control, null, element);
        RebindClick(control, element.OnClick);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        RepeatButtonElement oldElement,
        RepeatButtonElement newElement,
        RepeatButton control)
    {
        ApplyContent(reconciler, control, oldElement, newElement);

        PropWriter.Set(oldElement.Delay, newElement.Delay, value => control.Delay = value ?? 0);
        PropWriter.Set(oldElement.Interval, newElement.Interval, value => control.Interval = value ?? 0);
        PropWriter.Set(oldElement.IsEnabled, newElement.IsEnabled, value => control.IsEnabled = value ?? true);

        RebindClick(control, newElement.OnClick);
    }

    // 摘除就地写在 Unmount 里（不转调 RebindClick）：卸载路径要能一眼看出
    // "这张表在哪儿摘"，而不是顺着回调再跳一层。
    protected override void Unmount(Reconciler reconciler, RepeatButton control)
    {
        if (Clicks[control] is { } existing)
        {
            control.Click -= existing;
        }

        Clicks.Remove(control);
    }

    protected override Element? SingleChildOf(RepeatButtonElement element) => element.Content;

    private static void ApplyContent(
        Reconciler reconciler,
        RepeatButton control,
        RepeatButtonElement? oldElement,
        RepeatButtonElement newElement)
    {
        if (newElement.Content is not null || oldElement?.Content is not null)
        {
            reconciler.PatchSingleChild(control, oldElement?.Content, newElement.Content);
            return;
        }

        PropWriter.Set(oldElement?.Label, newElement.Label, value => control.Content = value);
    }

    private static void RebindClick(RepeatButton control, Action? onClick)
    {
        if (Clicks[control] is { } existing)
        {
            control.Click -= existing;
            Clicks.Remove(control);
        }

        if (onClick is null)
        {
            return;
        }

        RoutedEventHandler handler = (_, _) => onClick();
        control.Click += handler;
        Clicks.Set(control, handler);
    }
}

/// <summary>
/// WinUI 2 的 <c>ToggleSplitButton</c>：受控 <c>IsChecked</c> + <c>Click</c> + 菜单。
/// </summary>
/// <remarks>
/// 它是 <see cref="ToggleButtonHandler"/> 与 <c>SplitButtonHandler</c> 的合体，
/// 所以两边的规矩各取一份：受控值那一份照抄 <c>ToggleButton</c>（只是回执事件换成
/// <c>IsCheckedChanged</c>），点击与浮出层那一份照抄 <c>SplitButton</c>。
/// <para>
/// 回执只有<b>一个</b>事件（<c>IsCheckedChanged</c>），而它带的是"变成什么"
/// 而不是"勾上了/取消了"——这里直接回读 <c>control.IsChecked</c>，
/// 与 <c>CheckBox</c> 用两个事件分别带 <c>true</c> / <c>false</c> 是两种风格，
/// 因为官方给的就是两种。
/// </para>
/// </remarks>
internal sealed class ToggleSplitButtonHandler
    : ElementHandler<ToggleSplitButtonElement, MuxControls.ToggleSplitButton>
{
    private static readonly EchoGuard CheckEcho = new();

    private static readonly WeakTable<MuxControls.ToggleSplitButton,
        Windows.Foundation.TypedEventHandler<MuxControls.ToggleSplitButton,
            MuxControls.ToggleSplitButtonIsCheckedChangedEventArgs>> Handlers = new();

    private static readonly WeakTable<MuxControls.ToggleSplitButton, Action> Clicks = new();

    protected override MuxControls.ToggleSplitButton Mount(
        Reconciler reconciler,
        ToggleSplitButtonElement element)
    {
        var control = new MuxControls.ToggleSplitButton();

        ApplyContent(reconciler, control, null, element);
        ApplyFlyout(reconciler, control, null, element.Flyout);
        RebindClick(control, element.OnClick);

        // 顺序同上：先挂带闸的订阅，再写受控值。
        Rebind(control, element.OnIsCheckedChanged);

        if (element.IsChecked.HasValue && element.IsChecked.Value is { } initial)
        {
            control.IsChecked = initial;
        }

        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        ToggleSplitButtonElement oldElement,
        ToggleSplitButtonElement newElement,
        MuxControls.ToggleSplitButton control)
    {
        using (CheckEcho.Silence(control))
        {
            ApplyContent(reconciler, control, oldElement, newElement);
            ApplyFlyout(reconciler, control, oldElement.Flyout, newElement.Flyout);
        }

        // 官方 ToggleSplitButton.IsChecked 是 <b>不可空</b> 的 bool（没有三态），
        // 所以元素上那个 bool? 里的 null 在这里落到 false，而不是"保持原样"。
        if (newElement.IsChecked.HasValue &&
            newElement.IsChecked.Value is { } target &&
            control.IsChecked != target)
        {
            CheckEcho.Expect(control, target);
            control.IsChecked = target;

            CheckEcho.CancelIfUnconsumed(control);
        }

        RebindClick(control, newElement.OnClick);
        Rebind(control, newElement.OnIsCheckedChanged);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.ToggleSplitButton control)
    {
        CheckEcho.Forget(control);

        if (ClickHandlers.TryGetValue(control, out var bound) && bound is { } attached)
        {
            control.Click -= attached;
            ClickHandlers.Remove(control);
        }

        Clicks.Remove(control);
        Rebind(control, null);

        // 浮出层里的子树不在可视树里，UnmountTree 递归不到（见 ContentFlyouts）。
        ContentFlyouts.Retire(reconciler, control.Flyout);
    }

    protected override Element? SingleChildOf(ToggleSplitButtonElement element) => element.Content;

    private static void ApplyContent(
        Reconciler reconciler,
        MuxControls.ToggleSplitButton control,
        ToggleSplitButtonElement? oldElement,
        ToggleSplitButtonElement newElement)
    {
        if (newElement.Content is not null || oldElement?.Content is not null)
        {
            reconciler.PatchSingleChild(control, oldElement?.Content, newElement.Content);
            return;
        }

        PropWriter.Set(oldElement?.Label, newElement.Label, value => control.Content = value);
    }

    private static void ApplyFlyout(
        Reconciler reconciler,
        MuxControls.ToggleSplitButton control,
        Element? oldFlyout,
        Element? newFlyout) =>
        control.Flyout = FlyoutSlot.Of(reconciler, oldFlyout, newFlyout, control.Flyout);

    /// <summary>点击回调：先摘后挂（与 <c>SplitButtonHandler</c> 同形）。</summary>
    private static void RebindClick(MuxControls.ToggleSplitButton control, Action? onClick)
    {
        // 先摘旧的：委托是闭包，只能靠存下来的那一份解绑。
        if (ClickHandlers.TryGetValue(control, out var bound) && bound is { } attached)
        {
            control.Click -= attached;
            ClickHandlers.Remove(control);
        }

        if (onClick is null)
        {
            Clicks.Remove(control);
            return;
        }

        Clicks.Set(control, onClick);

        // 回调里用<b>订阅时那个引用</b>（<c>control</c>）查表，不用回调给的
        // <c>sender</c>：WinRT 不保证同一原生对象每次都给同一个托管包装，
        // 拿 sender 查按控件建的表会查不到，表现就是"点了没反应"。
        // 依据见 RadioButtonsHandler.Handlers 字段的注释。
        Windows.Foundation.TypedEventHandler<MuxControls.SplitButton, MuxControls.SplitButtonClickEventArgs> handler =
            (s, args) => Clicks[control]?.Invoke();

        control.Click += handler;
        ClickHandlers.Set(control, handler);
    }

    /// <summary>每个按钮上当前挂着的点击委托（闭包，必须存下来才能 <c>-=</c>）。</summary>
    private static readonly WeakTable<
        MuxControls.ToggleSplitButton,
        Windows.Foundation.TypedEventHandler<MuxControls.SplitButton, MuxControls.SplitButtonClickEventArgs>?> ClickHandlers = new();

    private static void Rebind(MuxControls.ToggleSplitButton control, Action<bool>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.IsCheckedChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<MuxControls.ToggleSplitButton,
            MuxControls.ToggleSplitButtonIsCheckedChangedEventArgs> handler = (_, _) =>
            {
                if (CheckEcho.Consume(control, control.IsChecked))
                {
                    return;
                }

                if (control.IsChecked is { } value)
                {
                    callback(value);
                }
            };

        control.IsCheckedChanged += handler;
        Handlers.Set(control, handler);
    }
}
