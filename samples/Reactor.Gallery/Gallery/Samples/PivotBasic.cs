using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>Pivot</c>：横向滑动切换的若干页，<b>每页各自持有内容</b>。
/// </summary>
/// <remarks>
/// 这个样例的重点是那句"各自持有"——与第一页输入框配合着看：
/// 输入几个字 → 滑到第二页 → 滑回第一页，<b>字还在</b>。
/// 若像某些写法那样只在容器上留一份内容、切换时整段重建，滑回来就是空的。
/// <para>
/// 也请留意切换的<b>手感</b>：表头与内容是跟着手势一起横着平移的。这件事由
/// 官方 <c>Pivot</c> 模板提供，不是这里写出来的——自己拼"页签条 + 内容区"
/// 做不出这个过渡。
/// </para>
/// <para>
/// 官方 <c>Pivot</c> 只 realize 当前页与左右邻页，其余处于未加载状态；
/// 页数很多时这也正是它的本事所在（对比"虚拟化长列表"那条目）。
/// </para>
/// </remarks>
public sealed class PivotBasic : Component
{
    private static readonly string[] PageNames = { "概述", "开关", "数值" };

    public override Element Render()
    {
        var (index, setIndex) = UseState(0);

        return VStack(12,
            Pivot(
                selectedIndex: Optional<int>.Of(index),
                onSelectedIndexChanged: setIndex,
                title: "三页示例",

                items: new[]
                {
                    PivotItem("概述",
                        VStack(10,
                            TextBlock("这一页有个输入框：写点什么，然后翻到别的页再翻回来。").Wrap(),
                            TextBox(placeholderText: "随便写几个字，切走再切回来看看", header: "保留输入"),
                            TextBlock("内容留在原地，是因为每一页的内容都挂在自己的 PivotItem 上。")
                                .Caption().Subtle().Wrap())),

                    PivotItem("开关",
                        VStack(10,
                            CheckBox(Optional<bool?>.Of(true), label: "一个复选项"),
                            ToggleSwitch(Optional<bool>.Of(false), header: "一个开关"),
                            TextBlock("官方 Pivot 只保留当前与相邻页的内容，其余处于未加载状态。")
                                .Caption().Subtle().Wrap())),

                    PivotItem("数值",
                        VStack(10,
                            TextBlock("拖动看看").Caption(),
                            Slider(Optional<double>.Of(42), 0, 100),
                            ProgressBar(42),
                            TextBlock("三页各自的状态互不影响——它们本来就是三棵树。")
                                .Caption().Subtle().Wrap())),
                })
                .MinHeight(210),

            TextBlock($"当前第 {index + 1} 页：{PageNames[index]}").Caption().Subtle());
    }
}
