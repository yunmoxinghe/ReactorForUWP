using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 列表框：<c>ListBox</c> 的选中三档，以及"同一个受控下标在每一档里各是什么意思"。
/// </summary>
/// <remarks>
/// <para>
/// <c>ListBox</c> 与 <c>ListView</c> 看着像，但<b>基类不同</b>：它只是
/// <c>Selector</c>——没有 <c>Header</c>、没有 <c>ItemClick</c>（点一下就是"选中"，
/// 没有第二条通道），选中模式用的是 <see cref="SelectionMode"/> 而不是
/// <c>ListViewSelectionMode</c>。所以这里是另一条 handler 路径。
/// </para>
/// <para>
/// <b>多档里 <c>SelectedIndex</c> 只报"第一个"。</b>官方 <c>Selector</c> 的定义
/// 如此：它是"当前项"，不是"全部选中项的集合"。多选时想知道全部选中项，
/// 官方给的是 <c>SelectedItems</c>——那是控件持有的<b>活集合</b>，
/// 与本框架对 <c>CalendarView.SelectedDates</c> 的处理同一个理由：<b>只出不进</b>，
/// 本版没包。所以下面多选那两档只演示"回调会给什么"。
/// </para>
/// </remarks>
public sealed class ListBoxBasic : Component
{
    private static readonly string[] Names =
    {
        "打开库存", "写入日志", "刷新缓存", "重建索引", "校验签名",
    };

    public override Element Render()
    {
        var (index, setIndex) = UseState(0);
        var (trace, setTrace) = UseState("（还没点过）");

        return VStack(12,
            TextBlock("单选（默认）：受控 SelectedIndex").Body(),
            ListBox(
                Optional<int>.Of(index),
                i =>
                {
                    setIndex(i);
                    setTrace(i < 0 ? "单选 → 被清空了" : $"单选 → 第 {i} 项：{Names[i]}");
                },
                ForEach(Names, name => TextBlock(name)))
                .Height(150),
            HStack(12,
                Button("清空选择", () => setIndex(-1)),
                Button("选回第 2 项", () => setIndex(1))),
            TextBlock($"state 里现在是 {index}；写回去那一趟由框架认成回声，不会又回调一次。")
                .Caption().Subtle().Wrap(),

            TextBlock("多选（Multiple）").Body(),
            ListBox(
                Optional<int>.Unset,
                i => setTrace($"多选 → 回调给的是下标 {i}"),
                SelectionMode.Multiple,
                ForEach(Names, name => TextBlock(name)))
                .Height(140),
            TextBlock("Ctrl 点可以加选 / 减选；回调只给一个下标，不是集合。")
                .Caption().Subtle().Wrap(),

            TextBlock("扩展多选（Extended）").Body(),
            ListBox(
                Optional<int>.Unset,
                i => setTrace($"扩展 → 回调给的是下标 {i}"),
                SelectionMode.Extended,
                ForEach(Names, name => TextBlock(name)))
                .Height(140),
            TextBlock("与 Multiple 的差别在手势：Ctrl 加选、Shift 选一段。")
                .Caption().Subtle().Wrap(),

            TextBlock($"最后一次回调：{trace}").Caption().Subtle().Wrap());
    }
}
