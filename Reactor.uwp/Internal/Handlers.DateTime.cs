using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// UWP 原生的 <c>DatePicker</c>：受控 <c>Date</c>。
/// </summary>
/// <remarks>
/// 形与 <c>Slider.Value</c> / <c>RatingControl.Value</c> 一致：写 <c>Date</c> 会同步抛
/// <c>DateChanged</c>，那一发的作者是我们，靠 <see cref="EchoGuard"/> 认下来。
/// <para>
/// <b>区间属性（<c>MinYear</c> / <c>MaxYear</c>）必须罩窗。</b>改它们会把当前
/// <c>Date</c> 夹到新区间里，那一发 <c>DateChanged</c> 抛在<b>旧订阅还挂着</b>的时候
/// （<c>Rebind</c> 在最后才换回调），而且我们事先不知道会被夹成哪一天，
/// 没法用 <c>Expect</c> 去配它——与 <c>Slider</c> 的 <c>Minimum</c> / <c>Maximum</c>
/// 同一个形状，同一套解法（静默窗）。
/// </para>
/// <para>
/// 其余属性（标题 / 三栏可见性）理论上牵不动 <c>Date</c>，但这里一并罩进窗里：
/// 窗在"永远没等到事件"时的代价是零，漏罩则是一发假回调，代价不对称。
/// </para>
/// </remarks>
internal sealed class DatePickerHandler : ElementHandler<DatePickerElement, DatePicker>
{
    private static readonly EchoGuard DateEcho = new();

    private static readonly WeakTable<DatePicker, EventHandler<DatePickerValueChangedEventArgs>>
        Handlers = new();

    protected override DatePicker Mount(Reconciler reconciler, DatePickerElement element)
    {
        var control = new DatePicker
        {
            Header = element.Header,
            DayVisible = element.DayVisible,
            MonthVisible = element.MonthVisible,
            YearVisible = element.YearVisible,
            Orientation = element.Orientation,
        };

        if (element.CalendarIdentifier is { } calendar)
        {
            control.CalendarIdentifier = calendar;
        }

        if (element.MinYear is { } min)
        {
            control.MinYear = min;
        }

        if (element.MaxYear is { } max)
        {
            control.MaxYear = max;
        }

        if (element.Date.HasValue)
        {
            control.Date = element.Date.Value;
        }

        Rebind(control, element.OnDateChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        DatePickerElement oldElement,
        DatePickerElement newElement,
        DatePicker control)
    {
        using (DateEcho.Silence(control))
        {
            PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);
            PropWriter.Set(oldElement.DayVisible, newElement.DayVisible, value => control.DayVisible = value);
            PropWriter.Set(oldElement.MonthVisible, newElement.MonthVisible, value => control.MonthVisible = value);
            PropWriter.Set(oldElement.YearVisible, newElement.YearVisible, value => control.YearVisible = value);
            PropWriter.Set(oldElement.MinYear, newElement.MinYear, value => control.MinYear = value ?? default);
            PropWriter.Set(oldElement.MaxYear, newElement.MaxYear, value => control.MaxYear = value ?? default);

            // 换日历会把当前 Date 换算到新日历上（2026 年公历 ≈ 1447 年回历），
            // 与 MinYear / MaxYear 同一个"会把受控值夹走"的家族，同一个窗。
            PropWriter.Set(
                oldElement.CalendarIdentifier,
                newElement.CalendarIdentifier,
                value => control.CalendarIdentifier = value ?? string.Empty);
            PropWriter.Set(
                oldElement.Orientation,
                newElement.Orientation,
                value => control.Orientation = value);
        }

        if (!newElement.Date.HasValue)
        {
            Rebind(control, newElement.OnDateChanged);
            return;
        }

        var target = newElement.Date.Value;

        if (control.Date == target)
        {
            Rebind(control, newElement.OnDateChanged);
            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 DatePicker{CtlId.Tag(control)}: {control.Date:d} → {target:d}");

        DateEcho.Expect(control, target);
        control.Date = target;

        // 回调为空时订阅不存在，这一发没人领 → 撤销登记。
        DateEcho.CancelIfUnconsumed(control);

        Rebind(control, newElement.OnDateChanged);
    }

    protected override void Unmount(Reconciler reconciler, DatePicker control)
    {
        DateEcho.Forget(control);
        Rebind(control, null);
    }

    private static void Rebind(DatePicker control, Action<DateTimeOffset>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.DateChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        EventHandler<DatePickerValueChangedEventArgs> handler = (_, args) =>
            {
                if (DateEcho.Consume(control, args.NewDate))
                {
                    return;
                }

                callback(args.NewDate);
            };

        control.DateChanged += handler;
        Handlers.Set(control, handler);
    }
}

/// <summary>
/// UWP 原生的 <c>TimePicker</c>：受控 <c>Time</c>。
/// </summary>
/// <remarks>
/// 与 <see cref="DatePickerHandler"/> 完全同形，只是受控值的类型换成了
/// <see cref="TimeSpan"/>（"一天里的哪一刻"，不含日期）。
/// <para>
/// <c>MinuteIncrement</c> 会把当前 <c>Time</c> 吸附到新的步长上（选了 15 分钟一档，
/// 13:07 会被拉成 13:00 或 13:15），那一发 <c>TimeChanged</c> 同样是我们写的；
/// 罩窗的理由与 <c>DatePicker</c> 的 <c>MinYear</c> 那条一致。
/// </para>
/// </remarks>
internal sealed class TimePickerHandler : ElementHandler<TimePickerElement, TimePicker>
{
    private static readonly EchoGuard TimeEcho = new();

    private static readonly WeakTable<TimePicker, EventHandler<TimePickerValueChangedEventArgs>>
        Handlers = new();

    protected override TimePicker Mount(Reconciler reconciler, TimePickerElement element)
    {
        var control = new TimePicker { Header = element.Header };

        if (element.MinuteIncrement is { } increment)
        {
            control.MinuteIncrement = increment;
        }

        if (element.ClockIdentifier is { } clock && clock.Length > 0)
        {
            control.ClockIdentifier = clock;
        }

        if (element.Time.HasValue)
        {
            control.Time = element.Time.Value;
        }

        Rebind(control, element.OnTimeChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        TimePickerElement oldElement,
        TimePickerElement newElement,
        TimePicker control)
    {
        using (TimeEcho.Silence(control))
        {
            PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);
            PropWriter.Set(
                oldElement.MinuteIncrement,
                newElement.MinuteIncrement,
                value => control.MinuteIncrement = value ?? 1);
            PropWriter.Set(
                oldElement.ClockIdentifier,
                newElement.ClockIdentifier,
                value => control.ClockIdentifier = value ?? string.Empty);
        }

        if (!newElement.Time.HasValue)
        {
            Rebind(control, newElement.OnTimeChanged);
            return;
        }

        var target = newElement.Time.Value;

        if (control.Time == target)
        {
            Rebind(control, newElement.OnTimeChanged);
            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 TimePicker{CtlId.Tag(control)}: {control.Time} → {target}");

        TimeEcho.Expect(control, target);
        control.Time = target;

        TimeEcho.CancelIfUnconsumed(control);

        Rebind(control, newElement.OnTimeChanged);
    }

    protected override void Unmount(Reconciler reconciler, TimePicker control)
    {
        TimeEcho.Forget(control);
        Rebind(control, null);
    }

    private static void Rebind(TimePicker control, Action<TimeSpan>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.TimeChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        EventHandler<TimePickerValueChangedEventArgs> handler = (_, args) =>
            {
                if (TimeEcho.Consume(control, args.NewTime))
                {
                    return;
                }

                callback(args.NewTime);
            };

        control.TimeChanged += handler;
        Handlers.Set(control, handler);
    }
}

/// <summary>
/// UWP 原生的 <c>CalendarDatePicker</c>：受控 <c>Date</c>（<b>可空</b>）。
/// </summary>
/// <remarks>
/// 与 <see cref="DatePickerHandler"/> 的差别只有一个：这里的 <c>Date</c> 是
/// <c>DateTimeOffset?</c>，<c>null</c> 表示"没有值"。于是登记与回读都要按可空走：
/// <c>Expect</c> 装的是 <c>DateTimeOffset?</c>（为 <c>null</c> 时装箱成 <c>null</c>），
/// 回读的 <c>args.NewDate</c> 同样是 <c>DateTimeOffset?</c>，两边能对上。
/// <para>
/// 判定"值没变"用 <see cref="Nullable.Equals{T}"/>：直接 <c>==</c> 在两边都是
/// <c>null</c> 时也是 <c>true</c>，但那样会把"从有值改成空"和"本来就是空"
/// 两种情形混在一起，日志读不出来。
/// </para>
/// </remarks>
internal sealed class CalendarDatePickerHandler
    : ElementHandler<CalendarDatePickerElement, CalendarDatePicker>
{
    private static readonly EchoGuard DateEcho = new();

    private static readonly WeakTable<CalendarDatePicker,
        Windows.Foundation.TypedEventHandler<CalendarDatePicker,
            CalendarDatePickerDateChangedEventArgs>> Handlers = new();

    protected override CalendarDatePicker Mount(
        Reconciler reconciler,
        CalendarDatePickerElement element)
    {
        var control = new CalendarDatePicker
        {
            Header = element.Header,
            IsTodayHighlighted = element.IsTodayHighlighted,
        };

        if (element.PlaceholderText is { } placeholder)
        {
            control.PlaceholderText = placeholder;
        }

        if (element.MinDate is { } min)
        {
            control.MinDate = min;
        }

        if (element.MaxDate is { } max)
        {
            control.MaxDate = max;
        }

        if (element.Date.HasValue)
        {
            control.Date = element.Date.Value;
        }

        Rebind(control, element.OnDateChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        CalendarDatePickerElement oldElement,
        CalendarDatePickerElement newElement,
        CalendarDatePicker control)
    {
        using (DateEcho.Silence(control))
        {
            PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);
            PropWriter.Set(
                oldElement.PlaceholderText,
                newElement.PlaceholderText,
                value => control.PlaceholderText = value ?? string.Empty);
            PropWriter.Set(
                oldElement.IsTodayHighlighted,
                newElement.IsTodayHighlighted,
                value => control.IsTodayHighlighted = value);
            PropWriter.Set(oldElement.MinDate, newElement.MinDate, value => control.MinDate = value ?? default);
            PropWriter.Set(oldElement.MaxDate, newElement.MaxDate, value => control.MaxDate = value ?? default);
        }

        if (!newElement.Date.HasValue)
        {
            Rebind(control, newElement.OnDateChanged);
            return;
        }

        var target = newElement.Date.Value;

        if (Nullable.Equals(control.Date, target))
        {
            Rebind(control, newElement.OnDateChanged);
            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 CalendarDatePicker{CtlId.Tag(control)}: {control.Date:d} → {target:d}");

        DateEcho.Expect(control, target);
        control.Date = target;

        DateEcho.CancelIfUnconsumed(control);

        Rebind(control, newElement.OnDateChanged);
    }

    protected override void Unmount(Reconciler reconciler, CalendarDatePicker control)
    {
        DateEcho.Forget(control);
        Rebind(control, null);
    }

    private static void Rebind(CalendarDatePicker control, Action<DateTimeOffset?>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.DateChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<CalendarDatePicker, CalendarDatePickerDateChangedEventArgs>
            handler = (_, args) =>
            {
                if (DateEcho.Consume(control, args.NewDate))
                {
                    return;
                }

                callback(args.NewDate);
            };

        control.DateChanged += handler;
        Handlers.Set(control, handler);
    }
}

/// <summary>
/// UWP 原生的 <c>CalendarView</c>：整张月历。<b>选中值非受控</b>。
/// </summary>
/// <remarks>
/// 见 <see cref="CalendarViewElement"/> 那里写的理由：<c>SelectedDates</c> 是控件
/// 持有的<b>活集合</b>，要受控就得每轮往里增删并对账，而没有回执通道能让我们
/// 把两份状态对齐到可判定。所以这里<b>不写</b>它，只把结果送出去。
/// <para>
/// <b>配置属性只在挂载时写。</b><c>DisplayMode</c> / <c>SelectionMode</c> /
/// <c>MinDate</c> / <c>MaxDate</c> 动的是"整张月历怎么摆"，而它们每一笔都可能牵动
/// 选中集合并抛 <c>SelectedDatesChanged</c>（换 <c>SelectionMode</c> 会清选中，
/// 收紧 <c>MaxDate</c> 会把区间外的选中日期摘掉）。要边跑边改就得再建一套
/// "这一发是不是我们写的"的抑制，而这里既然没有受控值，那套机制没有落点——
/// 于是这里<b>不写</b>：<c>Update</c> 只换回调。真要在运行中改配置，换一个
/// <c>key</c> 让控件重建（重建时配置在 <c>Mount</c> 里自然生效，代价是选中态归零，
/// 而选中态本来就是非受控的）。
/// </para>
/// <para>
/// 下面这行是给静态检查看的：<c>Mount</c> 里写了、<c>Update</c> 却不认的那些属性
/// 必须逐个登记，否则 <c>PropertyDriftTests</c> 会报警。让它合法的理由写在上面
/// 那一段里——改动这些配置会牵动选中集合，而这里没有受控值可落。
/// </para>
/// </remarks>
// MOUNT-ONLY: CalendarIdentifier MaxDate MinDate DisplayMode SelectionMode IsTodayHighlighted
    internal sealed class CalendarViewHandler : ElementHandler<CalendarViewElement, CalendarView>
{
    private static readonly WeakTable<CalendarView,
        Windows.Foundation.TypedEventHandler<CalendarView,
            CalendarViewSelectedDatesChangedEventArgs>> Handlers = new();

    protected override CalendarView Mount(Reconciler reconciler, CalendarViewElement element)
    {
        var control = new CalendarView
        {
            DisplayMode = element.DisplayMode,
            SelectionMode = element.SelectionMode,
            IsTodayHighlighted = element.IsTodayHighlighted,
        };

        if (element.MinDate is { } min)
        {
            control.MinDate = min;
        }

        if (element.MaxDate is { } max)
        {
            control.MaxDate = max;
        }

        if (element.CalendarIdentifier is { } calendar)
        {
            control.CalendarIdentifier = calendar;
        }

        Rebind(control, element.OnSelectedDatesChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        CalendarViewElement oldElement,
        CalendarViewElement newElement,
        CalendarView control)
    {
        // 配置属性不在这里写（见类注释）：这里只把回调换成捕获了新 state 的闭包。
        Rebind(control, newElement.OnSelectedDatesChanged);
    }

    // 摘除就地写在 Unmount 里（不转调 Rebind）：卸载路径要能一眼看出
    // "这张表在哪儿摘"，而不是顺着回调再跳一层。
    protected override void Unmount(Reconciler reconciler, CalendarView control)
    {
        if (Handlers[control] is { } existing)
        {
            control.SelectedDatesChanged -= existing;
        }

        Handlers.Remove(control);
    }

    private static void Rebind(
        CalendarView control,
        Action<IReadOnlyList<DateTimeOffset>>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.SelectedDatesChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<CalendarView, CalendarViewSelectedDatesChangedEventArgs>
            handler = (_, _) =>
            {
                // 给快照不给活集合：把控件内部那个 IList 交出去等于把它的状态
                // 交给调用方，任其被改。
                callback(new List<DateTimeOffset>(control.SelectedDates));
            };

        control.SelectedDatesChanged += handler;
        Handlers.Set(control, handler);
    }
}
