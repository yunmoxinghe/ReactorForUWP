using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>CalendarDatePicker</c>：一个输入框 + 一张可弹出的月历，受控 <c>Date</c>。
/// </summary>
/// <remarks>
/// 与 <c>DatePicker</c> 的差别是<b>交互形态</b>（点开月历 vs 三个下拉），
/// 但有一个<b>类型上的</b>差别更要看清：这里的 <c>Date</c> 是<b>可空</b>的
/// <c>DateTimeOffset?</c>——官方给的正是可空，因为输入框可以<b>没有值</b>
/// （显示为 <c>PlaceholderText</c>），而三个下拉的 <c>DatePicker</c> 没有"空"这个状态。
/// <para>
/// 于是回调签名也是 <c>Action&lt;DateTimeOffset?&gt;</c>：拿到 <c>null</c> 表示
/// "清空了"，不是"出错了"。
/// </para>
/// </remarks>
public sealed class CalendarDatePickerBasic : Component
{
    public override Element Render()
    {
        var (date, setDate) = UseState<DateTimeOffset?>(null);

        return VStack(14,
            TextBlock("受控（可空）").Body(),
            CalendarDatePicker(
                Optional<DateTimeOffset?>.Of(date),
                setDate,
                header: "选一天",
                placeholderText: "还没有选"),
            TextBlock(date is null
                    ? "还没有选（state 里是 null）"
                    : $"选了 {date.Value:yyyy-MM-dd}")
                .Caption().Subtle(),

            TextBlock("清空").Body(),
            Button("把上面的值清成空", () => setDate(null)),
            TextBlock("官方的「清空」是输入框上那个 ×，走的是同一个回调——拿到的就是 null。")
                .Caption().Subtle(),

            TextBlock("限区间（真正的日期区间，不是只比年份）").Body(),
            CalendarDatePicker(
                Optional<DateTimeOffset?>.Of(date),
                setDate,
                header: "本月之内",
                placeholderText: "选一天",
                minDate: new DateTimeOffset(DateTimeOffset.Now.Year, DateTimeOffset.Now.Month, 1, 0, 0, 0, TimeSpan.Zero),
                maxDate: new DateTimeOffset(DateTimeOffset.Now.Year, DateTimeOffset.Now.Month, 1, 0, 0, 0, TimeSpan.Zero)
                    .AddMonths(1).AddDays(-1)),
            TextBlock("与 DatePicker 的 minYear / maxYear 不同：这里比的是完整日期。")
                .Caption().Subtle());
    }
}
