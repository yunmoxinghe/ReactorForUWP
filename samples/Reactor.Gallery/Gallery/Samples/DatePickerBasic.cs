using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>DatePicker</c>：年月日三个下拉，受控 <c>Date</c>。
/// </summary>
/// <remarks>
/// 受控那一档是"给值 + 给回调"：<c>date</c> 由 state 回写，与 <c>Slider</c> /
/// <c>RatingControl</c> 同形。
/// <para>
/// <b>不传 <c>date</c> 就是非受控</b>：那时框架一个字都不写这个属性，控件保持
/// 自己的初值。想要"打开就是今天"，显式给一个值——这是调用方的决定。
/// </para>
/// <para>
/// <c>minYear</c> / <c>maxYear</c> 只比<b>年份</b>那一段（官方就这么定的）：
/// 给 <c>2020-01-01</c> 当最小值，卡的是"年份 ≥ 2020"，不是"日期 ≥ 2020-01-01"。
/// </para>
/// </remarks>
public sealed class DatePickerBasic : Component
{
    public override Element Render()
    {
        var (date, setDate) = UseState(DateTimeOffset.Now);
        var (showDay, setShowDay) = UseState(true);

        return VStack(14,
            TextBlock("受控").Body(),
            DatePicker(Optional<DateTimeOffset>.Of(date), setDate, header: "选一天"),
            TextBlock($"当前 {date:yyyy-MM-dd}").Caption().Subtle(),

            TextBlock("三栏各自可藏").Body(),
            CheckBox(Optional<bool?>.Of(showDay), setShowDay, "显示「日」那一栏"),
            DatePicker(
                Optional<DateTimeOffset>.Of(date),
                setDate,
                header: "同一份 state，藏掉「日」",
                dayVisible: showDay),
            TextBlock("藏掉「日」之后值还在（只是不显示、也不能改），月份里的哪一天仍是原来那一天。")
                .Caption().Subtle(),

            TextBlock("藏掉「年」那一栏").Body(),
            DatePicker(
                Optional<DateTimeOffset>.Of(date),
                setDate,
                header: "yearVisible: false",
                yearVisible: false),
            TextBlock("三栏各自独立，藏哪栏都不动另外两栏；年份看不见了，值里的年份仍在。"
                      + "（官方那一档还带了 DayFormat，本版没接这个属性。）")
                .Caption().Subtle().Wrap(),

            TextBlock("限年份").Body(),
            DatePicker(
                Optional<DateTimeOffset>.Of(date),
                setDate,
                header: "只给 2020–2029",
                minYear: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                maxYear: new DateTimeOffset(2029, 12, 31, 0, 0, 0, TimeSpan.Zero)),
            TextBlock("区间只比年份，所以 2029-12-31 之后那一年整年都选不了。").Caption().Subtle(),

            TextBlock("换一套日历").Body(),
            DatePicker(
                Optional<DateTimeOffset>.Of(date),
                setDate,
                header: "回历（HijriCalendar）",
                calendarIdentifier: "HijriCalendar"),
            TextBlock("同一天，换日历之后年月日的<b>数字</b>就变了（2026 年公历落在回历 1447 年）——"
                + "换的是表示法，不是那一天本身。").Caption().Subtle().Wrap(),

            TextBlock("三栏竖排").Body(),
            DatePicker(
                Optional<DateTimeOffset>.Of(date),
                setDate,
                header: "Orientation = Vertical",
                orientation: Orientation.Vertical),
            TextBlock("只是三栏的排布方向变了，值与回调都不受影响。").Caption().Subtle(),

            TextBlock("非受控（不给 date）").Body(),
            DatePicker(header: "框架不写这个属性"),
            TextBlock("改它不会回调（没有回调可给），state 里也就没有它。").Caption().Subtle());
    }
}
