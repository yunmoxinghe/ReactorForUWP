using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 选择类控件：<c>ComboBox</c> / <c>RadioButtons</c> 的下标受控写法。
/// </summary>
/// <remarks>
/// 两个控件都按下标定值：<c>-1</c> 是"什么都没选"，用
/// <c>Optional&lt;int&gt;.Of(-1)</c> 显式清空（不传则是"不管它"，控件保持现状）。
/// <para>
/// 单选按钮要成组就用 <c>RadioButtons</c>（WinUI 2 的原生分组控件）：
/// 键盘上下方向键在组内移动这一条由官方模板提供，自己拿一排
/// <c>RadioButton</c> 拼是拼不出来的。
/// </para>
/// <para>
/// <c>ComboBox</c> 还有"可编辑"那一档（<c>IsEditable</c>）：收起时那个框变成
/// 输入框，可以打字。要注意<b>打的字不等于选中项</b>——官方把文本放在另一个属性上，
/// 我们这一侧能观察的仍然是下标；打的字与哪一项匹配由控件自己算。
/// </para>
/// </remarks>
public sealed class ComboBoxAndRadio : Component
{
    private static readonly string[] Colors = { "红色", "绿色", "蓝色" };

    private static readonly string[] Priorities = { "低", "中", "高" };

    public override Element Render()
    {
        var (color, setColor) = UseState(0);
        var (priority, setPriority) = UseState(1);
        var (search, setSearch) = UseState(true);

        return VStack(16,
            HStack(24,
                VStack(6,
                    ComboBox(Colors, Optional<int>.Of(color), setColor)
                        .AutomationName("基本下拉"),
                    TextBlock($"选中第 {color} 项：{Colors[color]}").Caption().Subtle()),
                VStack(6,
                    RadioButtons(Priorities, Optional<int>.Of(priority), setPriority),
                    TextBlock($"当前优先级：{Priorities[priority]}").Caption().Subtle())),

            TextBlock("可编辑的下拉").Caption().Subtle(),
            VStack(6,
                ComboBox(
                    Colors,
                    Optional<int>.Of(color),
                    setColor,
                    isEditable: true,
                    isTextSearchEnabled: search)
                    .AutomationName("可编辑的下拉"),
                TextBlock($"下标仍然是 {color}：打的字与哪一项匹配，是控件自己算的")
                    .Caption().Subtle(),
                ToggleSwitch(Optional<bool>.Of(search), setSearch, onContent: "敲字自动跳转", offContent: "敲字不跳转"),

                TextBlock("「敲字自动跳转」是另一个开关（IsTextSearchEnabled），"
                          + "它<b>在可编辑模式下也生效</b>——关掉它只是不再自动跳到匹配项，"
                          + "列表与当前下标都不动。")
                    .Wrap()
                    .Caption()
                    .Subtle()));
    }
}
