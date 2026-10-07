using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>NavigationView</c>：WinUI 的标准导航外壳（左侧菜单 + 内容区）。
/// </summary>
/// <remarks>
/// <para>
/// <b>两条通道，别混。</b><c>OnSelectedIndexChanged</c> 是"选中项变了"；
/// <c>OnItemInvoked</c> 是"被点了一下"——点<b>当前已选中</b>的那一项时只有后者会来。
/// 官方设置项（<c>IsSettingsVisible</c> 那个）不在菜单列表里，以 <c>-1</c> 回调。
/// </para>
/// <para>
/// <b>选中是受控的</b>：<c>selectedIndex</c> 给值 + 给回调，写回去那一趟由框架
/// 认成回声。菜单的<b>开合</b>（<c>IsPaneOpen</c>）则刻意<b>非受控</b>——
/// 官方没有给我们一个能对齐两份状态的回执通道，不装成受控是为了不让
/// "点了汉堡键没反应"变成谜题。
/// </para>
/// </remarks>
public sealed class NavigationViewBasic : Component
{
    private static readonly string[] Titles = { "首页", "列表", "关于" };

    private static readonly string[] Glyphs = { "\uE80F", "\uE8FD", "\uE946" };

    private static readonly NavPaneDisplayMode[] Modes =
    {
        NavPaneDisplayMode.Left,
        NavPaneDisplayMode.Top,
        NavPaneDisplayMode.LeftCompact,
        NavPaneDisplayMode.Auto,
    };

    public override Element Render()
    {
        var (index, setIndex) = UseState(0);
        var (modeAt, setModeAt) = UseState(0);
        var (trace, setTrace) = UseState("（还没点过）");
        var (toggle, setToggle) = UseState(true);

        return VStack(12,
            TextBlock("画廊自己的外壳就是它；这里把四种 PaneDisplayMode 摆出来看差别。")
                .Caption().Subtle().Wrap(),

            NavigationView(
                    content: VStack(8,
                        TextBlock(index >= 0 ? $"内容区：{Titles[index]}" : "内容区：（设置项没有内容）"),
                        TextBlock("内容可以是任意元素树，不只是一个文本。")
                            .Caption().Subtle().Wrap())
                        .Padding(16),
                    menuItems: Titles
                        .Select((title, i) => new NavigationViewItemData(title, Glyphs[i], title))
                        .ToArray(),
                    paneDisplayMode: Modes[modeAt],
                    selectedIndex: index,
                    onSelectedIndexChanged: i =>
                    {
                        setIndex(i);
                        setTrace($"选中项变成 {i}");
                    },
                    onItemInvoked: i => setTrace(
                        i < 0 ? "点了设置项（回调 -1）" : $"点了第 {i} 项（含当前已选中项）"),
                    isSettingsVisible: true,
                    header: "示例导航",
                    isPaneToggleButtonVisible: toggle,
                    paneTitle: "示例导航",
                    // 刻意把"切紧凑"的门槛抬高：窗口收窄时更早变成图标条。
                    // 只给这一个，另一个留 null（用控件自己的默认）。
                    compactModeThresholdWidth: 900)
                .Height(260)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            HStack(8,
                Button($"形态：{Modes[modeAt]}", () => setModeAt((modeAt + 1) % Modes.Length)),
                Button("选回第 0 项", () => setIndex(0)),
                Button(toggle ? "藏起汉堡键" : "显示汉堡键", () => setToggle(!toggle))),

            TextBlock($"最后一次回执：{trace}").Caption().Subtle().Wrap(),

            TextBlock("汉堡键藏起来<b>不等于</b>面板锁死：轻扫（触屏）与顶部模式下的入口仍能把"
                      + "它拉出来——这两个属性管的是「那个键在不在」，不是「能不能开合」。")
                .Caption().Subtle().Wrap(),
            TextBlock("这里还把 compactModeThresholdWidth 抬到 900：窗口收窄时会<b>更早</b>折成图标条。"
                      + "另一个阈值留 null（用控件自己的默认）——本库不代抄一个会随版本变的官方断点数字。")
                .Caption().Subtle().Wrap());
    }
}
