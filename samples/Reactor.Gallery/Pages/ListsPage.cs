using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

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

        // 受控列表的取证入口：换一次数据源，看回调次数量涨不涨。
        // 在修复之前，"只留前 3 项"会让 Selector 自己把选中变成 -1 并抛一发事件，
        // 那一发会被当成用户输入回调出去（计数凭空 +1、选中态被清掉）。
        var (shrunk, setShrunk) = UseState(false);
        var (hits, setHits) = UseState(0);
        var shown = shrunk ? Fruits.Take(3).ToArray() : Fruits;

        // 第二类取证入口：改 SelectionMode 同样会把选中态牵动
        // （ListViewBase_SelectionMode → OnSelectionModeChanged，源码注释原文
        // "will update all Selection related properties"）。切到「禁止选中」时
        // 控件会自己清一次选中，那一发不该冒成用户输入。
        var (noSelection, setNoSelection) = UseState(false);

        // 第三类取证入口：换菜单项 / 切显示模式。
        //
        // NavigationView 的菜单由 repeater 承载，重建的后果落在 repeater 加载完那一刻
        // （release/2.8 dev/NavigationView/NavigationView.cpp，
        // OnSelectionModelSelectionChanged 的注释原文：
        // "SelectionModel's selectedIndex state will get properly updated after the
        // repeater finishes loading"）。
        // 所以抑制必须用<b>持续标记</b>：用 using 那种时间窗会在我们返回那一刻就关掉，
        // 而那一发事件还没到。时间窗版本的表现就是这两下让回调计数凭空 +1。
        var (navTop, setNavTop) = UseState(false);
        var (navShort, setNavShort) = UseState(false);
        var (navIndex, setNavIndex) = UseState(0);
        var (navHits, setNavHits) = UseState(0);
        var navItems = (navShort ? Fruits.Take(3) : Fruits)
            .Select(f => new NavigationViewItemData(f))
            .ToArray();

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
                    i =>
                    {
                        setHits(hits + 1);
                        setListIndex(i);
                    },
                    noSelection ? ListViewSelectionMode.None : ListViewSelectionMode.Single,
                    shown.Select(f => TextBlock(f)).ToArray()).Height(180),
                HStack(8,
                    Button(shrunk ? "显示全部 8 项" : "只留前 3 项", () => setShrunk(!shrunk)),
                    Button(noSelection ? "切回单选" : "切到禁止选中", () => setNoSelection(!noSelection)),
                    Button("回调计数归零", () => setHits(0))),
                // 名字按 <c>shown</c> 取，不按下标直接索引 <c>Fruits</c>：
                // 下标是"当前显示的是第几个"，不是"八个水果里的第几个"。今天只留前 3 项
                // 恰好是前缀，两种写法结果一样；一旦换成筛选（非前缀），直接索引就会
                // 报出另一条的名字——而且不崩，只是名字不对，很难往这边想。
                TextBlock($"ListView 选中：{(listIndex >= 0 && listIndex < shown.Length ? shown[listIndex] : "无")}").Caption(),
                TextBlock($"OnSelectedIndexChanged 回调次数：{hits}（换数据源 / 改模式都不该涨）").Caption(),

                TextBlock("GridView：网格排布").FontSize(16),
                GridView(
                    Optional<int>.Of(gridIndex),
                    setGridIndex,
                    Fruits.Select(f => TextBlock(f)).ToArray()).Height(140),
                TextBlock($"GridView 选中：{(gridIndex < 0 ? "无" : Fruits[gridIndex])}").Caption(),

                TextBlock("NavigationView：换菜单 / 切显示模式").FontSize(16),
                // 名字同样取自<b>当前这份菜单</b>（<c>navItems</c>），不是按下标直取
                // <c>Fruits</c>：下标是"菜单里的第几个"，不是"八个水果里的第几个"。
                // 今天是取前缀才两种写法同结果，一旦换成任意筛选就会念错名字。
                NavigationView(
                    TextBlock($"菜单选中：{(navIndex >= 0 && navIndex < navItems.Length ? navItems[navIndex].Content : "无")}"),
                    navItems,
                    navTop ? NavPaneDisplayMode.Top : NavPaneDisplayMode.Left,
                    navIndex,
                    i =>
                    {
                        setNavHits(navHits + 1);
                        setNavIndex(i);
                    },
                    header: "重建取证").Height(200),
                HStack(8,
                    Button(navShort ? "恢复 8 项菜单" : "只留前 3 项菜单", () => setNavShort(!navShort)),
                    Button(navTop ? "切回左侧导航" : "切到顶部导航", () => setNavTop(!navTop)),
                    Button("导航回调计数归零", () => setNavHits(0))),
                TextBlock($"NavigationView 回调次数：{navHits}（换菜单 / 切模式都不该涨）").Caption()
            ).Padding(16));
    }
}
