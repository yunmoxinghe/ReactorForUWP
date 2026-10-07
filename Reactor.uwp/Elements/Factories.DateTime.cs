using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 日期与时间：<c>DatePicker</c> / <c>TimePicker</c> / <c>CalendarDatePicker</c> /
/// <c>CalendarView</c>。对应 WinUI 3 Gallery 里「Date &amp; time」那一整页。
/// </summary>
/// <remarks>
/// 四个都是 <c>Windows.UI.Xaml.Controls</c> 的 UWP 原生控件（WinUI 2 没有另做一套），
/// 所以这里没有 <c>MuxControls</c> 别名——<b>别为了"看起来像 WinUI"去换控件</b>，
/// Gallery 里那一页在这几个控件上指的就是 UWP 的这几个。
/// <para>
/// 命名沿用官方：<c>DatePicker</c> / <c>TimePicker</c> / <c>CalendarDatePicker</c> /
/// <c>CalendarView</c>，参数名也取自官方属性名。
/// </para>
/// </remarks>
public static partial class Factories
{
    /// <summary>
    /// 年月日三个下拉的日期选择。
    /// </summary>
    /// <param name="date">当前日期。不传 = 非受控（见 <see cref="DatePickerElement"/>）。</param>
    /// <param name="onDateChanged">日期变化回调。</param>
    /// <param name="header">标题。</param>
    /// <param name="dayVisible">是否显示"日"那一栏。</param>
    /// <param name="monthVisible">是否显示"月"那一栏。</param>
    /// <param name="yearVisible">是否显示"年"那一栏。</param>
    /// <param name="minYear">最早年份（只比年份那一段）。</param>
    /// <param name="maxYear">最晚年份（只比年份那一段）。</param>
    public static DatePickerElement DatePicker(
        Optional<DateTimeOffset> date = default,
        Action<DateTimeOffset>? onDateChanged = null,
        string? header = null,
        bool dayVisible = true,
        bool monthVisible = true,
        bool yearVisible = true,
        DateTimeOffset? minYear = null,
        DateTimeOffset? maxYear = null,
        string? calendarIdentifier = null,
        Orientation orientation = Orientation.Horizontal) =>
        new(date, onDateChanged)
        {
            Header = header,
            DayVisible = dayVisible,
            MonthVisible = monthVisible,
            YearVisible = yearVisible,
            MinYear = minYear,
            MaxYear = maxYear,
            CalendarIdentifier = calendarIdentifier,
            Orientation = orientation,
        };

    /// <summary>
    /// 时间选择。
    /// </summary>
    /// <param name="time">当前时刻，<see cref="TimeSpan"/> 语义（当天零点到这一刻的长度）。</param>
    /// <param name="onTimeChanged">时间变化回调。</param>
    /// <param name="header">标题。</param>
    /// <param name="minuteIncrement">分钟那栏的步长；不传用官方默认。</param>
    /// <param name="clockIdentifier"><c>"12HourClock"</c> 或 <c>"24HourClock"</c>；不传跟随系统区域。</param>
    public static TimePickerElement TimePicker(
        Optional<TimeSpan> time = default,
        Action<TimeSpan>? onTimeChanged = null,
        string? header = null,
        int? minuteIncrement = null,
        string? clockIdentifier = null) =>
        new(time, onTimeChanged)
        {
            Header = header,
            MinuteIncrement = minuteIncrement,
            ClockIdentifier = clockIdentifier,
        };

    /// <summary>
    /// 带日历弹出层的日期选择（一个输入框，点开一张月历）。
    /// </summary>
    /// <param name="date">当前日期。<b>可空</b>：传 <c>null</c> 表示"没有值"。</param>
    /// <param name="onDateChanged">日期变化回调，<c>null</c> 表示被清空了。</param>
    /// <param name="header">标题。</param>
    /// <param name="placeholderText">还没有值时显示的占位文字。</param>
    /// <param name="minDate">最早日期。</param>
    /// <param name="maxDate">最晚日期。</param>
    /// <param name="isTodayHighlighted">是否高亮"今天"。</param>
    public static CalendarDatePickerElement CalendarDatePicker(
        Optional<DateTimeOffset?> date = default,
        Action<DateTimeOffset?>? onDateChanged = null,
        string? header = null,
        string? placeholderText = null,
        DateTimeOffset? minDate = null,
        DateTimeOffset? maxDate = null,
        bool isTodayHighlighted = true) =>
        new(date, onDateChanged)
        {
            Header = header,
            PlaceholderText = placeholderText,
            MinDate = minDate,
            MaxDate = maxDate,
            IsTodayHighlighted = isTodayHighlighted,
        };

    /// <summary>
    /// 一整张月历。
    /// </summary>
    /// <remarks>
    /// <b>选中值非受控</b>：只把结果通过 <paramref name="onSelectedDatesChanged"/> 送出去，
    /// 框架从不往回写。理由见 <see cref="CalendarViewElement"/>。
    /// </remarks>
    /// <param name="onSelectedDatesChanged">选中集合变化的回调，给的是快照。</param>
    /// <param name="displayMode">月 / 年 / 十年。</param>
    /// <param name="selectionMode">无 / 单选 / 多选。</param>
    /// <param name="isTodayHighlighted">是否高亮"今天"。</param>
    /// <param name="minDate">最早日期。</param>
    /// <param name="maxDate">最晚日期。</param>
    public static CalendarViewElement CalendarView(
        Action<IReadOnlyList<DateTimeOffset>>? onSelectedDatesChanged = null,
        CalendarViewDisplayMode displayMode = CalendarViewDisplayMode.Month,
        CalendarViewSelectionMode selectionMode = CalendarViewSelectionMode.Single,
        bool isTodayHighlighted = true,
        DateTimeOffset? minDate = null,
        DateTimeOffset? maxDate = null,
        string? calendarIdentifier = null) =>
        new(onSelectedDatesChanged)
        {
            DisplayMode = displayMode,
            SelectionMode = selectionMode,
            IsTodayHighlighted = isTodayHighlighted,
            MinDate = minDate,
            MaxDate = maxDate,
            CalendarIdentifier = calendarIdentifier,
        };
}
