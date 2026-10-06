using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 密码框。以前这个位置只能拿 <c>TextBox</c> 顶替——没有掩码、没有"显示密码"
/// 按钮、读屏也不是 Password 角色。这里就是真的 <see cref="PasswordBox"/>。
/// </summary>
internal sealed class PasswordBoxHandler : ElementHandler<PasswordBoxElement, PasswordBox>
{
    /// <summary>受控密码的回声抑制（写法与 TextBox 一致）。</summary>
    private static readonly EchoGuard PasswordEcho = new();

    private static readonly WeakTable<PasswordBox, RoutedEventHandler> Handlers = new();

    protected override PasswordBox Mount(Reconciler reconciler, PasswordBoxElement element)
    {
        var native = new PasswordBox
        {
            PlaceholderText = element.PlaceholderText ?? string.Empty,
            Header = element.Header,
        };

        if (element.MaxLength is { } max)
        {
            native.MaxLength = max;
        }

        if (element.IsPasswordRevealButtonEnabled is { } reveal)
        {
            native.IsPasswordRevealButtonEnabled = reveal;
        }

        if (element.Value.HasValue)
        {
            native.Password = element.Value.Value ?? string.Empty;
        }

        Rebind(native, element.OnChanged);
        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        PasswordBoxElement oldElement,
        PasswordBoxElement newElement,
        PasswordBox control)
    {
        PropWriter.Set(
            oldElement.PlaceholderText,
            newElement.PlaceholderText,
            value => control.PlaceholderText = value ?? string.Empty);
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);

        if (newElement.MaxLength is { } max && control.MaxLength != max)
        {
            // 这一笔是"改区间把受控值夹走"那一族的又一个成员：
            // 收紧 MaxLength 时，已有的 Password 到底会不会被控件截短，
            // **没有源码可查**（Windows.UI.Xaml 那一支不开源，WinUI 2 的 PasswordBox
            // 就是它；能查到的文档只说它约束输入，没说会不会改已有内容）。
            // 于是按第 14 节定的同一条原则处理——不猜，开窗：
            // 窗内没等到事件时代价是零，漏罩则是一发假回调。
            using (PasswordEcho.Silence(control))
            {
                control.MaxLength = max;
            }
        }

        if (newElement.IsPasswordRevealButtonEnabled is { } reveal &&
            control.IsPasswordRevealButtonEnabled != reveal)
        {
            control.IsPasswordRevealButtonEnabled = reveal;
        }

        if (newElement.Value.HasValue)
        {
            var value = newElement.Value.Value ?? string.Empty;
            if (!string.Equals(control.Password, value, StringComparison.Ordinal))
            {
                PasswordEcho.Expect(control, value);
                control.Password = value;

                // Rebind(control, null) 会先退订再 return（本文件 Rebind:91-101），
                // 回调为空时这一发没人领 → 撤销登记，别留成陈旧期望。
                PasswordEcho.CancelIfUnconsumed(control);
            }
        }

        Rebind(control, newElement.OnChanged);
    }

    protected override void Unmount(Reconciler reconciler, PasswordBox control)
    {
        PasswordEcho.Forget(control);
        Rebind(control, null);
    }

    private static void Rebind(PasswordBox control, Action<string>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.PasswordChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        RoutedEventHandler handler = (_, _) =>
        {
            var value = control.Password;
            if (PasswordEcho.Consume(control, value))
            {
                return;
            }

            callback(value);
        };

        control.PasswordChanged += handler;
        Handlers.Set(control, handler);
    }
}

/// <summary>
/// 带建议的输入框（真 <see cref="AutoSuggestBox"/>）。
/// </summary>
/// <remarks>
/// <b>建议列表怎么喂。</b>直接给 <c>ItemsSource</c> 赋 <c>List&lt;string&gt;</c>
/// 在 AOT 下过不了 <c>ItemsSourceView</c>（会被拒，甚至 fast-fail）；所以用与
/// 面包屑同一个已验证的技巧：<c>ItemsControl.Items</c> 是 WinRT 自己的
/// <c>IObservableVector&lt;IInspectable&gt;</c>，拿它当向量载体。
/// <para>
/// 内容相同就一个字节都不动：<c>Clear()</c> 会让内部列表把条目全拆了重建，
/// 每轮重渲染清一次等于一直在重建。
/// </para>
/// </remarks>
internal sealed class AutoSuggestBoxHandler : ElementHandler<AutoSuggestBoxElement, AutoSuggestBox>
{
    private static readonly EchoGuard TextEcho = new();

    /// <summary>控件 → 建议向量载体（不进可视树，只当向量使）。</summary>
    private static readonly WeakTable<AutoSuggestBox, ItemsControl> Carriers = new();

    private static readonly WeakTable<AutoSuggestBox, (
        Windows.Foundation.TypedEventHandler<AutoSuggestBox, AutoSuggestBoxTextChangedEventArgs>? Text,
        Windows.Foundation.TypedEventHandler<AutoSuggestBox, AutoSuggestBoxQuerySubmittedEventArgs>? Query,
        Windows.Foundation.TypedEventHandler<AutoSuggestBox, AutoSuggestBoxSuggestionChosenEventArgs>? Chosen)>
        Handlers = new();

    protected override AutoSuggestBox Mount(Reconciler reconciler, AutoSuggestBoxElement element)
    {
        var native = new AutoSuggestBox
        {
            PlaceholderText = element.PlaceholderText ?? string.Empty,
            Header = element.Header,
        };

        if (element.Text.HasValue)
        {
            native.Text = element.Text.Value ?? string.Empty;
        }

        var carrier = new ItemsControl();
        SyncSuggestions(carrier.Items, element.Suggestions);
        native.ItemsSource = carrier.Items;
        Carriers.Set(native, carrier);

        Rebind(native, element);
        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        AutoSuggestBoxElement oldElement,
        AutoSuggestBoxElement newElement,
        AutoSuggestBox control)
    {
        PropWriter.Set(
            oldElement.PlaceholderText,
            newElement.PlaceholderText,
            value => control.PlaceholderText = value ?? string.Empty);
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);

        if (newElement.Text.HasValue)
        {
            var text = newElement.Text.Value ?? string.Empty;
            if (!string.Equals(control.Text, text, StringComparison.Ordinal))
            {
                TextEcho.Expect(control, text);
                control.Text = text;

                // 同上：Rebind 里 OnTextChanged 为空就不挂 TextChanged
                // （本文件 Rebind:249-266），这一发等不到回声，撤销它。
                TextEcho.CancelIfUnconsumed(control);
            }
        }

        if (Carriers[control] is { } carrier)
        {
            SyncSuggestions(carrier.Items, newElement.Suggestions);
        }

        Rebind(control, newElement);
    }

    protected override void Unmount(Reconciler reconciler, AutoSuggestBox control)
    {
        TextEcho.Forget(control);
        Rebind(control, null);
        Carriers.Remove(control);
    }

    private static void SyncSuggestions(ItemCollection target, IReadOnlyList<string>? items)
    {
        var list = items ?? Array.Empty<string>();

        if (target.Count == list.Count)
        {
            var same = true;
            for (var i = 0; i < list.Count; i++)
            {
                if (target[i] is not string existing || existing != list[i])
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        target.Clear();
        foreach (var item in list)
        {
            target.Add(item);
        }
    }

    private static void Rebind(AutoSuggestBox control, AutoSuggestBoxElement? element)
    {
        if (Handlers[control] is { } existing)
        {
            if (existing.Text is { } t) control.TextChanged -= t;
            if (existing.Query is { } q) control.QuerySubmitted -= q;
            if (existing.Chosen is { } c) control.SuggestionChosen -= c;
            Handlers.Remove(control);
        }

        if (element is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<AutoSuggestBox, AutoSuggestBoxTextChangedEventArgs>? text = null;
        Windows.Foundation.TypedEventHandler<AutoSuggestBox, AutoSuggestBoxQuerySubmittedEventArgs>? query = null;
        Windows.Foundation.TypedEventHandler<AutoSuggestBox, AutoSuggestBoxSuggestionChosenEventArgs>? chosen = null;

        if (element.OnTextChanged is { } onText)
        {
            text = (_, _) =>
            {
                var value = control.Text;
                if (TextEcho.Consume(control, value))
                {
                    return;
                }

                onText(value);
            };
            control.TextChanged += text;
        }

        if (element.OnQuerySubmitted is { } onQuery)
        {
            query = (_, e) => onQuery(e.QueryText ?? string.Empty);
            control.QuerySubmitted += query;
        }

        if (element.OnSuggestionChosen is { } onChosen)
        {
            chosen = (_, e) =>
            {
                if (e.SelectedItem is string item)
                {
                    onChosen(item);
                }
            };
            control.SuggestionChosen += chosen;
        }

        Handlers.Set(control, (text, query, chosen));
    }
}

/// <summary>
/// 数字输入框（WinUI 2 的真 <see cref="MuxControls.NumberBox"/>）。
/// </summary>
/// <remarks>
/// 空值 = <see cref="double.NaN"/>（官方语义，见元素注释）。比对数值时必须用
/// <c>NaN</c> 感知的比较，否则"空 → 空"会被判成变化、每轮写一次。
/// </remarks>
internal sealed class NumberBoxHandler : ElementHandler<NumberBoxElement, MuxControls.NumberBox>
{
    private static readonly EchoGuard ValueEcho = new();

    private static readonly WeakTable<MuxControls.NumberBox,
        Windows.Foundation.TypedEventHandler<MuxControls.NumberBox,
            MuxControls.NumberBoxValueChangedEventArgs>> Handlers = new();

    protected override MuxControls.NumberBox Mount(Reconciler reconciler, NumberBoxElement element)
    {
        var native = new MuxControls.NumberBox { Header = element.Header };
        ApplyRange(native, element);

        if (element.Value.HasValue)
        {
            native.Value = element.Value.Value;
        }

        Rebind(native, element.OnValueChanged);
        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        NumberBoxElement oldElement,
        NumberBoxElement newElement,
        MuxControls.NumberBox control)
    {
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);

        // ApplyRange 里写 Minimum / Maximum 会把 Value 夹到新区间，那一发 ValueChanged
        // 抛在**旧订阅还挂着**的时候（Rebind 在下面才退订）。夹出来的值只有控件知道
        // （NumberBox 的 CoerceValue：越界且 ValidationMode == InvalidInputOverwritten
        // 时 Value(Minimum) 或 Value(Maximum)），而且改两个边界可能各夹一次 ——
        // 一次 Expect 装不下两发，所以用静默窗而不是登记期望值。
        // 第八道契约守着这两处：删掉这个窗就会红。
        using (ValueEcho.Silence(control))
        {
            ApplyRange(control, newElement);
        }

        // 同 Slider：登记/下发都用夹取后的值（RangePolicy 的注释里有
        // NumberBox 的 CoerceValue 源码依据）。NaN 表示"空"，不参与夹取。
        if (newElement.Value.HasValue)
        {
            var target = RangePolicy.Coerce(newElement.Value.Value, control.Minimum, control.Maximum);

            if (!SameValue(control.Value, target))
            {
                ValueEcho.Expect(control, target);
                control.Value = target;

                // 回调为空时订阅不存在（Rebind:412-434），这一发没人领 → 撤销登记。
                ValueEcho.CancelIfUnconsumed(control);
            }
        }

        Rebind(control, newElement.OnValueChanged);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.NumberBox control)
    {
        ValueEcho.Forget(control);
        Rebind(control, null);
    }

    private static void ApplyRange(MuxControls.NumberBox control, NumberBoxElement element)
    {
        if (element.Min is { } min && !SameValue(control.Minimum, min))
        {
            control.Minimum = min;
        }

        if (element.Max is { } max && !SameValue(control.Maximum, max))
        {
            control.Maximum = max;
        }

        if (element.SmallChange is { } small && !SameValue(control.SmallChange, small))
        {
            control.SmallChange = small;
        }

        if (element.LargeChange is { } large && !SameValue(control.LargeChange, large))
        {
            control.LargeChange = large;
        }

        if (element.SpinButtonPlacementMode is { } spin && control.SpinButtonPlacementMode != spin)
        {
            control.SpinButtonPlacementMode = spin;
        }

        if (element.IsWrapEnabled is { } wrap && control.IsWrapEnabled != wrap)
        {
            control.IsWrapEnabled = wrap;
        }

        if (element.DecimalPlaces is { } places)
        {
            // DecimalFormatter 在 Windows.Globalization.NumberFormatting，
            // 这里按需建一次：只在声明了小数位数时才碰 NumberFormatter。
            control.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
            {
                FractionDigits = places,
            };
        }
    }

    /// <summary>NaN 感知的数值相等（NumberBox 用 NaN 表示"空"）。</summary>
    private static bool SameValue(double a, double b) =>
        double.IsNaN(a) && double.IsNaN(b) || Math.Abs(a - b) < 1e-9;

    private static void Rebind(MuxControls.NumberBox control, Action<double>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.ValueChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<MuxControls.NumberBox,
            MuxControls.NumberBoxValueChangedEventArgs> handler = (_, e) =>
            {
                if (ValueEcho.Consume(control, e.NewValue))
                {
                    return;
                }

                callback(e.NewValue);
            };

        control.ValueChanged += handler;
        Handlers.Set(control, handler);
    }
}
