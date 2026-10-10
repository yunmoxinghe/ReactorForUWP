using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 下拉选择：<c>ComboBox</c> 的下标受控写法。
/// </summary>
/// <remarks>
/// 官方那页的四件事：<b>基本的下拉</b>、<b>可编辑</b>、<b>敲字自动跳转</b>、
/// <b>没有选中项</b>。
/// <list type="bullet">
///   <item>按下标定值：<c>-1</c> 是"什么都没选"。用
///         <c>Optional&lt;int&gt;.Of(-1)</c> <b>显式清空</b>；不传（<c>Unset</c>）
///         是"这一轮不管它"，控件保持现状——两者不是一回事。</item>
///   <item><b>可编辑那一档打的字不等于选中项。</b>官方把文本放在另一个属性上，
///         我们这一侧能观察的仍然是下标；打的字与哪一项匹配由控件自己算。</item>
///   <item><c>isTextSearchEnabled</c> 是<b>另一个开关</b>：敲字时跳到匹配的项。
///         它在可编辑模式下<b>也生效</b>——关掉它只是不再自动跳，列表与下标都不动。</item>
///   <item>下拉的每一项官方是 <c>ComboBoxItem</c>；本库收 <c>string[]</c>
///         （声明式一侧"这一组选项"最常见的形态就是一组字符串）。</item>
///   <item><c>placeholderText</c> 是"没选时框里的灰字"，只在下标为 <c>-1</c>
///         时才露出来。它是<b>提示</b>不是一项：选不了，也不会进列表。</item>
/// </list>
/// </remarks>
public sealed class ComboBoxBasic : Component
{
    private static readonly string[] Colors = { "红色", "绿色", "蓝色", "黄色", "紫色" };

    public override Element Render()
    {
        var (color, setColor) = UseState(0);
        var (search, setSearch) = UseState(true);

        return VStack(16,
            TextBlock("基本下拉（受控）").Body(),

            // 官方例 1 就是这三个属性一起给：Width=200 + Header + PlaceholderText。
            // 灰字只有"什么都没选"时才露出来 —— 点下面的「清空」就能看到它。
            ComboBox(
                Colors,
                Optional<int>.Of(color),
                setColor,
                header: "颜色",
                placeholderText: "选一个颜色")
                .AutomationName("基本下拉")
                .Width(200),
            TextBlock($"选中第 {color} 项：{Colors[color]}").Caption().Subtle(),

            TextBlock("清空选中项（-1）").Body(),

            HStack(12,
                Button("清空", () => setColor(-1)),
                TextBlock(color < 0 ? "当前：什么都没选" : $"当前：{Colors[color]}")
                    .Caption()
                    .Subtle()),

            TextBlock("可编辑 + 敲字自动跳转").Body(),

            VStack(6,
                ComboBox(
                    Colors,
                    Optional<int>.Of(color),
                    setColor,
                    isEditable: true,
                    isTextSearchEnabled: search)
                    .AutomationName("可编辑的下拉")
                    .Width(200),
                ToggleSwitch(Optional<bool>.Of(search), setSearch,
                    onContent: "敲字自动跳转", offContent: "敲字不跳转"),
                TextBlock("在框里打字：下标会随着匹配项变化，但打的字本身<b>不是</b>选中项，"
                          + "两段文本住在官方的两个不同属性上。")
                    .Caption()
                    .Subtle()
                    .Wrap()),

            TextBlock("关掉「敲字自动跳转」只是不再跳到匹配项 —— 列表内容与当前下标都不动，"
                      + "它是另一个开关（IsTextSearchEnabled），不是可编辑的一部分。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
