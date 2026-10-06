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

    private static readonly HashSet<string> UnsupportedInputTypes = new(StringComparer.Ordinal);

    public static void Apply(UIElement native, ElementModifiers mods)
    {
        ApplyTabOrder(native, mods);
        ApplyContextFlyout(native, mods);
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

    private static void ApplyContextFlyout(UIElement native, ElementModifiers mods) =>
        WriteRef(() => native.ContextFlyout, mods.ContextFlyout, v => native.ContextFlyout = v);

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
        entry.Spec = spec;

        if (ReferenceEquals(spec.OnInvoked, entry.Spec.OnInvoked) && entry.Handler is not null)
        {
            return;
        }

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
        if (mods.FocusOnMount is not true || FocusRequested.ContainsKey(native))
        {
            return;
        }

        // Focus 是 Control 的方法（UIElement 没有）：TextBlock 之类声明了就只能忽略。
        if (native is not Control control)
        {
            WarnOnce(native, "FocusOnMount");
            return;
        }

        FocusRequested.Set(native, true);

        if (native is FrameworkElement { IsLoaded: true })
        {
            control.Focus(FocusState.Programmatic);
            return;
        }

        // 挂载阶段控件还没进树，此时 Focus 必然失败 → 等 Loaded。
        if (native is not FrameworkElement framework)
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
