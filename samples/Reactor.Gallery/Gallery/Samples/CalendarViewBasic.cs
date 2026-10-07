using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Wux = Windows.UI.Xaml.Controls;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>CalendarView</c>：一整张月历。
/// </summary>
/// <remarks>
/// <b>选中值是<b>非受控</b>的，这是本版唯一一个"只出不进"的控件。</b>
/// 官方的选中集合是 <c>CalendarView.SelectedDates</c>——一个<b>控件自己持有</b>的活
/// <c>IList</c>。要受控就得每轮往里增删并对账，还要处理多选；而"集合里现在有什么"
/// 与"state 里现在有什么"这两份状态，没有回执通道能让我们对齐到可判定。
/// 所以框架<b>不写</b>它：只把选中结果通过回调送出去。
/// <para>
/// 代价要说清：把 state 里的日期再传回 <c>CalendarView</c>，框架<b>不会</b>把它
/// 同步进月历的高亮。要"从外部改选中"就换 <c>CalendarDatePicker</c>（它的
/// <c>Date</c> 是受控的单个值）。
/// </para>
/// <para>
/// 另外：配置属性（<c>DisplayMode</c> / <c>SelectionMode</c> / <c>MinDate</c> /
/// <c>MaxDate</c>）<b>只在挂载时写</b>，因为改它们会牵动选中集合并抛事件。
/// 要在运行中换，给控件换一个 <c>key</c> 让它重建。
/// </para>
/// </remarks>
public sealed class CalendarViewBasic : Component
{
    public override Element Render()
    {
        var (picked, setPicked) = UseState<IReadOnlyList<DateTimeOffset>>(Array.Empty<DateTimeOffset>());

        return VStack(14,
            TextBlock("单选").Body(),
            CalendarView(setPicked),
            TextBlock(picked.Count == 0
                    ? "还没选"
                    : "选了 " + string.Join("、", picked.Select(d => d.ToString("yyyy-MM-dd"))))
                .Caption().Subtle(),

            TextBlock("多选（按住 Ctrl / Shift 拖选）").Body(),
            CalendarView(setPicked, selectionMode: Wux.CalendarViewSelectionMode.Multiple),
            TextBlock($"两份月历共用一份 state，共 {picked.Count} 天。回调给的是快照，"
                      + "不是控件内部那个活集合——改它不会影响月历。").Caption().Subtle(),

            TextBlock("十年视图").Body(),
            CalendarView(displayMode: Wux.CalendarViewDisplayMode.Decade),
            TextBlock("点年份进年视图，点月份进月视图——三级下钻是控件自己的行为。")
                .Caption().Subtle());
    }
}
