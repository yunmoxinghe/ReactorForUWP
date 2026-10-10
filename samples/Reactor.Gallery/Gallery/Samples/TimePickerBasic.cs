using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>TimePicker</c>：时刻选择，受控 <c>Time</c>。
/// </summary>
/// <remarks>
/// <b><c>Time</c> 是 <see cref="TimeSpan"/>，不是 <c>DateTime</c></b>：官方控件上
/// 没有"哪一天"这个概念，只有"一天里的哪一刻"——<c>13:45</c> 就是
/// <c>new TimeSpan(13, 45, 0)</c>（当天零点到这一刻的长度）。
/// 想与 <c>DatePicker</c> 拼成一个完整时刻，要自己把两个值合起来。
/// <para>
/// <c>minuteIncrement</c> 是分钟那栏的步长：设成 15 之后，分钟只允许 0 / 15 / 30 / 45，
/// 而且<b>改这个步长会把当前值吸附过去</b>（13:07 会被拉成整档）。
/// 那一发 <c>TimeChanged</c> 的作者是我们，handler 里罩了静默窗，不会冒成用户输入。
/// </para>
/// </remarks>
public sealed class TimePickerBasic : Component
{
    public override Element Render()
    {
        var (time, setTime) = UseState(new TimeSpan(13, 45, 0));

        return VStack(8,
            TextBlock("裸控件（不带 header）").Body(),
            TimePicker(Optional<TimeSpan>.Of(time), setTime),
            TextBlock("官方第一档就是一个「什么都不给」的 TimePicker：没有 header，"
                      + "也没有标题那一刻——受控归受控，外观上它就只有三个框。")
                .Caption().Subtle().Wrap(),

            TextBlock("受控（24 小时制）").Body(),
            TimePicker(
                Optional<TimeSpan>.Of(time),
                setTime,
                header: "选一个时刻",
                clockIdentifier: "24HourClock"),
            TextBlock($"当前 {time.Hours:D2}:{time.Minutes:D2}").Caption().Subtle(),

            TextBlock("十二小时制（带上午 / 下午）").Body(),
            TimePicker(
                Optional<TimeSpan>.Of(time),
                setTime,
                header: "同一份 state",
                clockIdentifier: "12HourClock"),
            TextBlock("换制式不改值：同一份 state 在两个控件上显示成两种写法。").Caption().Subtle(),

            TextBlock("十五分钟一档").Body(),
            TimePicker(
                Optional<TimeSpan>.Of(time),
                setTime,
                header: "minuteIncrement: 15",
                minuteIncrement: 15),
            TextBlock("把上面两个控件里的值改成 13:07 之类的零头，再回头看这一栏："
                      + "它会被吸附到整档上。").Caption().Subtle());
    }
}
