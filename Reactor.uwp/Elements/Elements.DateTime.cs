using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 日期选择（年月日三个下拉），对应 UWP 原生的 <c>Windows.UI.Xaml.Controls.DatePicker</c>。
/// </summary>
/// <remarks>
/// <b><c>Date</c> 是受控的</b>：写 <c>Date</c> 会同步抛 <c>DateChanged</c>，那一发的
/// 作者是我们，靠 <c>EchoGuard</c> 认下来——与 <c>Slider.Value</c> /
/// <c>RatingControl.Value</c> 同一个形状。
/// <para>
/// <b>不传 <c>date</c> 就是非受控</b>：那时我们<b>一个字都不写</b>这个属性，
/// 控件保持它自己的初值。想要"打开就是今天"，显式传
/// <c>DateTimeOffset.Now</c>——这是调用方的决定，不是框架的默认值。
/// </para>
/// <para>
/// <c>MinYear</c> / <c>MaxYear</c> 只比<b>年份</b>那一段（官方就这么定的），
/// 传 <c>DateTimeOffset.Now</c> 当"今年"不会把 1 月 1 日也卡进去。
/// </para>
/// </remarks>
public sealed record DatePickerElement(
    Optional<DateTimeOffset> Date = default,
    Action<DateTimeOffset>? OnDateChanged = null) : Element
{
    /// <summary>标题（XAML 的 <c>Header</c>）。</summary>
    public string? Header { get; init; }

    /// <summary>是否显示"日"那一栏。</summary>
    public bool DayVisible { get; init; } = true;

    /// <summary>是否显示"月"那一栏。</summary>
    public bool MonthVisible { get; init; } = true;

    /// <summary>是否显示"年"那一栏。</summary>
    public bool YearVisible { get; init; } = true;

    /// <summary>可选的最早年份。不传就不设（官方默认），只比年份那一段。</summary>
    public DateTimeOffset? MinYear { get; init; }

    /// <summary>可选的最晚年份。不传就不设（官方默认），只比年份那一段。</summary>
    public DateTimeOffset? MaxYear { get; init; }

    /// <summary>
    /// 用哪套日历（官方 <c>CalendarIdentifier</c>），如
    /// <c>"GregorianCalendar"</c> / <c>"HebrewCalendar"</c> / <c>"HijriCalendar"</c>。
    /// </summary>
    /// <remarks>
    /// null = 用系统区域设置那一套。<b>换它会把当前日期换算到新日历上</b>
    /// （2026 年公历 ≈ 1447 年回历），所以和 <see cref="MinYear"/> 一样罩静默窗。
    /// </remarks>
    public string? CalendarIdentifier { get; init; }

    /// <summary>
    /// 日 / 月 / 年三栏横排还是竖排（官方 <c>Orientation</c>）。
    /// </summary>
    public Orientation Orientation { get; init; } = Orientation.Horizontal;
}

/// <summary>
/// 时间选择（时分 + 上午/下午），对应 UWP 原生的 <c>Windows.UI.Xaml.Controls.TimePicker</c>。
/// </summary>
/// <remarks>
/// <c>Time</c> 用的是 <see cref="TimeSpan"/>（<b>当天零点到这一刻的长度</b>），
/// 不是 <c>DateTime</c>：官方控件上没有"哪一天"这个概念，只有"一天里的哪一刻"。
/// <c>13:45</c> 就是 <c>TimeSpan.FromHours(13).Add(TimeSpan.FromMinutes(45))</c>。
/// 想与 <c>DatePicker</c> 拼成一个完整时刻，要自己把两个值合起来。
/// <para>
/// 与非受控的边界同 <see cref="DatePickerElement"/>：不传 <c>time</c> 就不写这个属性。
/// </para>
/// </remarks>
public sealed record TimePickerElement(
    Optional<TimeSpan> Time = default,
    Action<TimeSpan>? OnTimeChanged = null) : Element
{
    /// <summary>标题（XAML 的 <c>Header</c>）。</summary>
    public string? Header { get; init; }

    /// <summary>分钟那栏的步长。不传就不设，用控件自己的默认（1 分钟一档）。</summary>
    public int? MinuteIncrement { get; init; }

    /// <summary>
    /// 时钟制式：<c>"12HourClock"</c>（带上午/下午）或 <c>"24HourClock"</c>。
    /// 不传就不设，跟随系统区域。
    /// </summary>
    public string? ClockIdentifier { get; init; }
}

/// <summary>
/// 带日历弹出层的日期选择，对应 UWP 原生的
/// <c>Windows.UI.Xaml.Controls.CalendarDatePicker</c>。
/// </summary>
/// <remarks>
/// 与 <see cref="DatePickerElement"/> 之别是<b>交互形态</b>，不是能力：
/// 这个是"一个输入框，点开一张月历"；那个是"年月日三个并排的下拉"。
/// <para>
/// <b><c>Date</c> 在这里是可空的</b>（<c>DateTimeOffset?</c>）——官方给的正是可空，
/// 因为输入框可以<b>没有值</b>（显示为 <c>PlaceholderText</c>），而三个下拉的
/// <c>DatePicker</c> 没有"空"这个状态。这个差别是从官方类型照抄来的，不是我们加的。
/// 于是回调签名也是 <c>Action&lt;DateTimeOffset?&gt;</c>：拿到 <c>null</c> 表示"清空了"。
/// </para>
/// </remarks>
public sealed record CalendarDatePickerElement(
    Optional<DateTimeOffset?> Date = default,
    Action<DateTimeOffset?>? OnDateChanged = null) : Element
{
    /// <summary>标题（XAML 的 <c>Header</c>）。</summary>
    public string? Header { get; init; }

    /// <summary>还没有值时显示的占位文字（官方 <c>PlaceholderText</c>）。</summary>
    public string? PlaceholderText { get; init; }

    /// <summary>可选的最早日期。不传就不设（官方默认）。</summary>
    public DateTimeOffset? MinDate { get; init; }

    /// <summary>可选的最晚日期。不传就不设（官方默认）。</summary>
    public DateTimeOffset? MaxDate { get; init; }

    /// <summary>是否高亮"今天"（官方 <c>IsTodayHighlighted</c>，默认开）。</summary>
    public bool IsTodayHighlighted { get; init; } = true;
}

/// <summary>
/// 一整张月历，对应 UWP 原生的 <c>Windows.UI.Xaml.Controls.CalendarView</c>。
/// </summary>
/// <remarks>
/// <b>选中值是<b>非受控</b>的，这是刻意的。</b>官方的选中集合是
/// <c>CalendarView.SelectedDates</c>（一个活的 <c>IList&lt;DateTimeOffset&gt;</c>），
/// 要"受控"就得每轮往那个集合里增删——而它是<b>控件自己持有的活集合</b>：
/// 我们写的每一笔都会再抛一次 <c>SelectedDatesChanged</c>，且"集合里现在有什么"
/// 与"state 里现在有什么"要逐项对账，还要处理"用户多选"这个官方能力
/// （<c>CalendarViewSelectionMode.Multiple</c>）。
/// 没有回执通道能让我们把这两份状态对齐到可判定，所以这里<b>不装</b>：
/// 只把选中结果通过回调送出去，从不往回写。
/// <para>
/// 代价要说清：state 里留一份选中日期，再把它传回 <c>CalendarView</c>，
/// 框架<b>不会</b>把它同步进月历的高亮。要"从外部改选中"就换
/// <see cref="CalendarDatePickerElement"/>（它的 <c>Date</c> 是受控的单个值）。
/// </para>
/// <para>
/// 回调给的是<b>快照</b>（<c>IReadOnlyList</c>），不是那个活集合：
/// 把活集合交出去等于把控件内部状态交给调用方，任其被改。
/// </para>
/// </remarks>
public sealed record CalendarViewElement(
    Action<IReadOnlyList<DateTimeOffset>>? OnSelectedDatesChanged = null) : Element
{
    /// <summary>月 / 年 / 十年（官方 <c>DisplayMode</c>）。</summary>
    public CalendarViewDisplayMode DisplayMode { get; init; } = CalendarViewDisplayMode.Month;

    /// <summary>无 / 单选 / 多选（官方 <c>SelectionMode</c>）。</summary>
    public CalendarViewSelectionMode SelectionMode { get; init; } = CalendarViewSelectionMode.Single;

    /// <summary>是否高亮"今天"（官方 <c>IsTodayHighlighted</c>，默认开）。</summary>
    public bool IsTodayHighlighted { get; init; } = true;

    /// <summary>可选的最早日期。不传就不设（官方默认）。</summary>
    public DateTimeOffset? MinDate { get; init; }

    /// <summary>可选的最晚日期。不传就不设（官方默认）。</summary>
    public DateTimeOffset? MaxDate { get; init; }

    /// <summary>
    /// 用哪套日历（官方 <c>CalendarIdentifier</c>）。null = 用系统区域设置那一套。
    /// </summary>
    /// <remarks>
    /// <b>只在挂载时写</b>（与 <see cref="DisplayMode"/> 同一批，理由见
    /// <c>CalendarViewHandler</c> 的类注释）：换日历会重算整个日期面板，
    /// 放进修 patch 通道就得为它单独登记，而它几乎只在初始化时定一次。
    /// </remarks>
    public string? CalendarIdentifier { get; init; }
}
