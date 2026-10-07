using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 把元素修饰符里的<b>键盘可达性 / 无障碍 / 投影 / 控件级声音</b>落到真实控件上。
/// </summary>
/// <remarks>
/// <para>
/// 这一段以前整体缺失：控件是真的 <c>Windows.UI.Xaml.*</c>，但 XAML 里最基础的
/// 那批属性（<c>TabIndex</c> / <c>KeyboardAccelerators</c> / <c>KeyDown</c> /
/// <c>ContextFlyout</c> / <c>AccessKey</c> / <c>AutomationProperties.*</c> /
/// <c>UIElement.Shadow</c> / 附加属性 <c>ElementSoundMode</c>）在这套 API 上没有
/// 对应物——于是"声明式做出的界面键盘走不通、读屏读不出"。
/// </para>
/// <para>
/// 全部走<b>同名映射</b>：建真的 <see cref="Windows.UI.Xaml.Input.KeyboardAccelerator"/>
/// 加进 <c>UIElement.KeyboardAccelerators</c>，而不是"自己监听按键再手动派发"
/// （那才是替代实现）。
/// </para>
/// <para>
/// <b>状态一律用弱键表</b>（<see cref="WeakTable{TKey, TValue}"/>）：需要记住
/// "上次挂了哪个委托 / 上次建了哪几个快捷键"才能做 diff 与解绑，但这些状态必须
/// 跟着控件一起消失，否则又是一处泄漏。
/// </para>
/// </remarks>
internal static class InputApplier
{
    private sealed class AccelEntry
    {
        public KeyboardAcceleratorSpec Spec = null!;
        public Windows.UI.Xaml.Input.KeyboardAccelerator Native = null!;
        public TypedEventHandler<
            Windows.UI.Xaml.Input.KeyboardAccelerator,
            KeyboardAcceleratorInvokedEventArgs>? Handler;
    }

    private static readonly WeakTable<UIElement, List<AccelEntry>> Accelerators = new();
    private static readonly WeakTable<UIElement, KeyEventHandler> KeyDownHandlers = new();
    private static readonly WeakTable<UIElement, KeyEventHandler> KeyUpHandlers = new();

    /// <summary>"已经请求过焦点"的控件——<c>FocusOnMount</c> 只做一次。</summary>
    private static readonly WeakTable<UIElement, bool> FocusRequested = new();

    /// <summary>每个控件<b>上次处理过的焦点令牌</b>——<c>FocusToken</c> 靠它判边沿。</summary>
    private static readonly WeakTable<UIElement, int> FocusTokens = new();

    private static readonly HashSet<string> UnsupportedInputTypes = new(StringComparer.Ordinal);

    public static void Apply(Reconciler reconciler, UIElement native, ElementModifiers mods)
    {
        ApplyTabOrder(native, mods);
        ApplyContextFlyout(reconciler, native, mods);
        ApplySelectionFlyout(reconciler, native, mods);
        ApplyKeyboardAccelerators(native, mods);
        ApplyKeyEvents(native, mods);
        ApplyFocus(native, mods);
        ApplyAutomation(native, mods);
        ApplyShadow(native, mods);
        ApplyElementSoundMode(native, mods);
    }

    // ── Tab 顺序 ────────────────────────────────────────────────

    private static void ApplyTabOrder(UIElement native, ElementModifiers mods)
    {
        // AccessKey 在 UIElement 上（TextBlock 也能用），其余三个只在 Control 上。
        WriteRef(() => native.AccessKey, mods.AccessKey, v => native.AccessKey = v);

        if (native is not Control control)
        {
            if (mods.TabIndex is not null || mods.IsTabStop is not null ||
                mods.AllowFocusOnInteraction is not null)
            {
                WarnOnce(native, "TabIndex / IsTabStop / AllowFocusOnInteraction");
            }

            return;
        }

        if (mods.TabIndex is { } index)
        {
            SetStruct(() => control.TabIndex, index, v => control.TabIndex = v);
        }

        if (mods.IsTabStop is { } tabStop)
        {
            SetStruct(() => control.IsTabStop, tabStop, v => control.IsTabStop = v);
        }

        if (mods.AllowFocusOnInteraction is { } allowFocus)
        {
            SetStruct(
                () => control.AllowFocusOnInteraction,
                allowFocus,
                v => control.AllowFocusOnInteraction = v);
        }
    }

    // ── 右键浮出层 ──────────────────────────────────────────────

    /// <summary>
    /// 右键浮出层。两种来源：<see cref="ElementModifiers.ContextMenu"/>（声明式，
    /// 优先）与 <see cref="ElementModifiers.ContextFlyout"/>（已造好的原生实例）。
    /// </summary>
    /// <remarks>
    /// 声明式那份<b>是不是</b>每次重渲染都重新物化，要看它是哪一种：
    /// <c>MenuFlyout</c>（装"项"）是重建——它不在可视树里，逐项 patch + 逐项
    /// 解绑旧回调要为每个菜单项维护一张"元素 ↔ 原生"的表，为一段转瞬即逝的
    /// 浮出层做这套记账不划算（详见 <see cref="MenuFlyouts"/> 的注释）。
    /// 代价是重渲染时如果菜单正开着，下一次弹出会用到新的那份——已经弹出的
    /// 那一份不受影响。
    /// <para>
    /// <see cref="FlyoutElement"/>（装一棵<b>子树</b>）是例外：它<b>就地 patch</b>，
    /// 由 <see cref="ContentFlyouts"/> 走协调器管——因为它的内容里可以有正在
    /// 输入的 <c>TextBox</c>，重建会把那点状态每轮抹掉一次。
    /// </para>
    /// </remarks>
    private static void ApplyContextFlyout(
        Reconciler reconciler, UIElement native, ElementModifiers mods)
    {
        if (mods.ContextMenu is { } menu)
        {
            native.ContextFlyout = FlyoutSlot.Of(reconciler, null, menu, native.ContextFlyout);
            return;
        }

        WriteRef(() => native.ContextFlyout, mods.ContextFlyout, v => native.ContextFlyout = v);
    }

    /// <summary>
    /// 选中文本时弹出的浮出层（<c>SelectionFlyout</c>）。
    /// </summary>
    /// <remarks>
    /// <b>这个属性不在 <c>UIElement</c> 上</b>：官方只给"能选文本的控件"留了它，
    /// 于是这里按类型分派，落到各自那份<b>同名</b>属性上（与
    /// <c>ElementSoundMode</c> 那一处是同一种形状：不是附加属性，
    /// 是几个类型各声明了一份）。认不出的类型留痕忽略——
    /// 静默改成"什么都没有"会让"菜单怎么点都不弹"变成没有痕迹的问题。
    /// </remarks>
    private static void ApplySelectionFlyout(
        Reconciler reconciler, UIElement native, ElementModifiers mods)
    {
        if (mods.SelectionFlyout is not { } flyout)
        {
            return;
        }

        switch (native)
        {
            case TextBox box:
                box.SelectionFlyout = FlyoutSlot.Of(reconciler, null, flyout, box.SelectionFlyout);
                break;

            case RichEditBox rich:
                rich.SelectionFlyout = FlyoutSlot.Of(reconciler, null, flyout, rich.SelectionFlyout);
                break;

            case TextBlock text:
                text.SelectionFlyout = FlyoutSlot.Of(reconciler, null, flyout, text.SelectionFlyout);
                break;

            case RichTextBlock richText:
                richText.SelectionFlyout = FlyoutSlot.Of(reconciler, null, flyout, richText.SelectionFlyout);
                break;

            default:
                WarnOnce(native, "SelectionFlyout");
                break;
        }
    }

    // ── 键盘快捷键 ──────────────────────────────────────────────

    private static void ApplyKeyboardAccelerators(UIElement native, ElementModifiers mods)
    {
        var specs = mods.KeyboardAccelerators;
        var entries = Accelerators[native];

        if (specs is null || specs.Count == 0)
        {
            if (entries is { Count: > 0 })
            {
                foreach (var entry in entries)
                {
                    Detach(entry);
                }

                native.KeyboardAccelerators.Clear();
                Accelerators.Remove(native);
            }

            return;
        }

        // 定义没变（Key + Modifiers + IsEnabled 逐一相同）就复用，只换回调。
        if (entries is { Count: > 0 } && entries.Count == specs.Count)
        {
            var same = true;
            for (var i = 0; i < specs.Count; i++)
            {
                if (!entries[i].Spec.SameDefinition(specs[i]))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                for (var i = 0; i < specs.Count; i++)
                {
                    Rebind(entries[i], specs[i]);
                }

                return;
            }
        }

        if (entries is { Count: > 0 })
        {
            foreach (var entry in entries)
            {
                Detach(entry);
            }

            native.KeyboardAccelerators.Clear();
        }

        var built = new List<AccelEntry>(specs.Count);
        foreach (var spec in specs)
        {
            var entry = new AccelEntry
            {
                Spec = spec,
                Native = new Windows.UI.Xaml.Input.KeyboardAccelerator
                {
                    Key = spec.Key,
                    Modifiers = spec.Modifiers,
                    IsEnabled = spec.IsEnabled,
                },
            };

            Rebind(entry, spec);
            built.Add(entry);
            native.KeyboardAccelerators.Add(entry.Native);
        }

        Accelerators.Set(native, built);
    }

    private static void Rebind(AccelEntry entry, KeyboardAcceleratorSpec spec)
    {
        // 比较要在赋值之前做。
        //
        // 反例（曾经就是）：先写 `entry.Spec = spec` 再比较——两边成了同一个对象，
        // 比较恒真，于是只要 handler 挂上过一次就永远提前返回，回调再也跟不上新闭包。
        // 症状是"第一次按快捷键有效，之后按同一个键没反应"：闭包捕获的是挂载那一帧的值
        // （比如 FocusToken 的自增基数停在 0），后续每按一次都算出同一个结果，state 不动。
        // 守住它的那道契约见 tests 的 CallbackRebindComparesBeforeAssign。
        if (entry.Handler is not null && ReferenceEquals(spec.OnInvoked, entry.Spec.OnInvoked))
        {
            return;
        }

        entry.Spec = spec;

        if (entry.Handler is { } old)
        {
            entry.Native.Invoked -= old;
            entry.Handler = null;
        }

        if (spec.OnInvoked is { } invoke)
        {
            TypedEventHandler<
                Windows.UI.Xaml.Input.KeyboardAccelerator,
                KeyboardAcceleratorInvokedEventArgs> handler = (_, _) => invoke();
            entry.Native.Invoked += handler;
            entry.Handler = handler;
        }
    }

    private static void Detach(AccelEntry entry)
    {
        if (entry.Handler is { } handler)
        {
            entry.Native.Invoked -= handler;
            entry.Handler = null;
        }
    }

    // ── KeyDown / KeyUp ─────────────────────────────────────────

    private static void ApplyKeyEvents(UIElement native, ElementModifiers mods)
    {
        RebindKeyEvent(native, KeyDownHandlers, mods.OnKeyDown, (h) => native.KeyDown += h, (h) => native.KeyDown -= h);
        RebindKeyEvent(native, KeyUpHandlers, mods.OnKeyUp, (h) => native.KeyUp += h, (h) => native.KeyUp -= h);
    }

    private static void RebindKeyEvent(
        UIElement native,
        WeakTable<UIElement, KeyEventHandler> table,
        Action<KeyRoutedEventArgs>? callback,
        Action<KeyEventHandler> add,
        Action<KeyEventHandler> remove)
    {
        var current = table[native];

        if (callback is null)
        {
            if (current is { } stale)
            {
                remove(stale);
                table.Remove(native);
            }

            return;
        }

        // 回调换了（闭包捕获了新 state）就换委托：+= 新的、-= 旧的。
        if (current is { } existing)
        {
            remove(existing);
        }

        KeyEventHandler handler = (_, e) => callback(e);
        add(handler);
        table.Set(native, handler);
    }

    // ── 焦点 ────────────────────────────────────────────────────

    private static void ApplyFocus(UIElement native, ElementModifiers mods)
    {
        var wantMountFocus = mods.FocusOnMount is true && !FocusRequested.ContainsKey(native);

        // 令牌那条是"边沿触发"：与上次处理过的值不同才算一次请求，
        // 相同就当没这回事（用户点走了焦点不该在下次重渲染时被抢回来）。
        var tokenChanged = mods.FocusToken is { } token &&
            (!FocusTokens.TryGetValue(native, out var last) || last != token);

        // Focus 是 Control 的方法（UIElement 没有）：TextBlock 之类声明了就只能忽略。
        if (native is not Control control)
        {
            if (wantMountFocus || tokenChanged)
            {
                WarnOnce(native, wantMountFocus ? "FocusOnMount" : "FocusToken");
            }

            return;
        }

        if (!wantMountFocus && !tokenChanged)
        {
            return;
        }

        if (wantMountFocus)
        {
            FocusRequested.Set(native, true);
        }

        if (mods.FocusToken is { } value)
        {
            FocusTokens.Set(native, value);
        }

        // 挂载阶段控件还没进树，此时 Focus 必然失败 → 等 Loaded。
        if (native is not FrameworkElement framework || framework.IsLoaded)
        {
            control.Focus(FocusState.Programmatic);
            return;
        }

        RoutedEventHandler? once = null;
        once = (_, _) =>
        {
            framework.Loaded -= once;
            control.Focus(FocusState.Programmatic);
        };
        framework.Loaded += once;
    }

    // ── AutomationProperties ────────────────────────────────────

    private static void ApplyAutomation(UIElement native, ElementModifiers mods)
    {
        WriteRef(() => AutomationProperties.GetHelpText(native), mods.AutomationHelpText,
            v => AutomationProperties.SetHelpText(native, v));
        WriteRef(() => AutomationProperties.GetFullDescription(native), mods.AutomationFullDescription,
            v => AutomationProperties.SetFullDescription(native, v));
        WriteRef(() => AutomationProperties.GetItemStatus(native), mods.AutomationItemStatus,
            v => AutomationProperties.SetItemStatus(native, v));
        WriteRef(() => AutomationProperties.GetItemType(native), mods.AutomationItemType,
            v => AutomationProperties.SetItemType(native, v));
        WriteRef(() => AutomationProperties.GetAcceleratorKey(native), mods.AutomationAcceleratorKey,
            v => AutomationProperties.SetAcceleratorKey(native, v));

        if (mods.AutomationLevel is { } level)
        {
            SetStruct(() => AutomationProperties.GetLevel(native), level,
                v => AutomationProperties.SetLevel(native, v));
        }

        if (mods.AutomationPositionInSet is { } position)
        {
            SetStruct(() => AutomationProperties.GetPositionInSet(native), position,
                v => AutomationProperties.SetPositionInSet(native, v));
        }

        if (mods.AutomationSizeOfSet is { } size)
        {
            SetStruct(() => AutomationProperties.GetSizeOfSet(native), size,
                v => AutomationProperties.SetSizeOfSet(native, v));
        }

        if (mods.AutomationLiveSetting is { } live)
        {
            SetStruct(() => AutomationProperties.GetLiveSetting(native), live,
                v => AutomationProperties.SetLiveSetting(native, v));
        }

        if (mods.AutomationAccessibilityView is { } view)
        {
            SetStruct(() => AutomationProperties.GetAccessibilityView(native), view,
                v => AutomationProperties.SetAccessibilityView(native, v));
        }
    }

    // ── 投影与控件级声音 ────────────────────────────────────────

    private static void ApplyShadow(UIElement native, ElementModifiers mods) =>
        WriteRef(() => native.Shadow, mods.Shadow, v => native.Shadow = v);

    private static void ApplyElementSoundMode(UIElement native, ElementModifiers mods)
    {
        if (mods.ElementSoundMode is not { } mode)
        {
            return;
        }

        // ElementSoundMode <b>不是</b>附加属性，而是几个类型<b>各自</b>声明的实例
        // 属性：Control / Hyperlink / ContentLink / FlyoutBase 各有一份（winmd 里
        // 就是 Control_ElementSoundMode、Hyperlink_ElementSoundMode …）。
        // 所以这里按类型分别落到对应实例属性上，没有统一入口。
        switch (native)
        {
            case Control control:
                SetStruct(() => control.ElementSoundMode, mode, v => control.ElementSoundMode = v);
                break;
            default:
                WarnOnce(native, "ElementSoundMode");
                break;
        }
    }

    // ── 小工具 ──────────────────────────────────────────────────

    /// <summary>值类型属性的 diff-and-write（挂载期无条件写，理由同 Reconciler 那边）。</summary>
    private static void SetStruct<T>(Func<T> read, T value, Action<T> write)
        where T : struct
    {
        if (!PropWriter.IsMounting && Equals(read(), value))
        {
            return;
        }

        write(value);
    }

    /// <summary>引用类型属性的 diff-and-write。</summary>
    private static void WriteRef<T>(Func<T?> read, T? value, Action<T?> write)
        where T : class
    {
        if (value is null)
        {
            return;
        }

        if (!PropWriter.IsMounting && ReferenceEquals(read(), value))
        {
            return;
        }

        write(value);
    }

    private static void WarnOnce(UIElement native, string what)
    {
        var key = native.GetType().FullName + "|" + what;
        if (!UnsupportedInputTypes.Add(key))
        {
            return;
        }

        Reactor.Uwp.Hosting.ReactorLog.Warn(Reactor.Uwp.Hosting.ReactorLogChannel.Patch, $"{native.GetType().Name} 上 {what} 不可用（该类型没有对应属性），已忽略");
    }
}
