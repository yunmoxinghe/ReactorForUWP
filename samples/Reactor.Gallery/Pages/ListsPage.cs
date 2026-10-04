using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Samples.Pages;

/// <summary>
/// 列表：ListView / GridView / ForEach。
/// </summary>
/// <remarks>
/// 这几个是"真实控件"路线：每一项都是真的 ListViewItem。项数少（几十以内）时最省心，
/// 上千项请改用下一页的 <c>VirtualizingList</c>。
/// </remarks>
public sealed class ListsPage : Component
{
    private static readonly string[] Fruits =
    {
        "苹果", "香蕉", "橙子", "葡萄", "西瓜", "草莓", "芒果", "桃子",
    };

    public override Element Render()
    {
        var (listIndex, setListIndex) = UseState(-1);
        var (gridIndex, setGridIndex) = UseState(-1);

        // ForEach 返回 GroupElement（渲染成裸 Grid），可以直接塞进 VStack。
        var chips = ForEach(Fruits, (fruit, i) =>
            Border(TextBlock($"{i + 1}. {fruit}")).Padding(6, 2, 6, 2));

        return ScrollViewer(
            VStack(12,
                TextBlock("列表").FontSize(20),

                TextBlock("ForEach：把集合映射成一组元素").FontSize(16),
                VStack(4, chips),

                TextBlock("ListView：带选中项").FontSize(16),
                // 列表放在垂直 Stack 里会按内容撑开高度，显式限高避免选中时高度抖动。
                ListView(
                    Optional<int>.Of(listIndex),
                    setListIndex,
                    Fruits.Select(f => TextBlock(f)).ToArray()).Height(180),
                TextBlock($"ListView 选中：{(listIndex < 0 ? "无" : Fruits[listIndex])}").Caption(),

                TextBlock("GridView：网格排布").FontSize(16),
                GridView(
                    Optional<int>.Of(gridIndex),
                    setGridIndex,
                    Fruits.Select(f => TextBlock(f)).ToArray()).Height(140),
                TextBlock($"GridView 选中：{(gridIndex < 0 ? "无" : Fruits[gridIndex])}").Caption()
            ).Padding(16));
    }
}
