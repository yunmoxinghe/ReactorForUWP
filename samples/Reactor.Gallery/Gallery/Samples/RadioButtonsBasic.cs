using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 单选：<c>RadioButton</c> 与 <c>RadioButtons</c>。
/// </summary>
/// <remarks>
/// 官方那页的两件事：<b>自己拿 <c>RadioButton</c> 拼一组</b>、<b>用
/// <c>RadioButtons</c> 这个分组控件</b>。
/// <list type="bullet">
///   <item><c>RadioButtons</c>（WinUI 2）是<b>官方的分组控件</b>：键盘上下方向键
///         在组内移动、焦点只落在当前选中那一项（不是逐项 Tab 过去）——这一条由
///         官方模板提供，自己拿一排 <c>RadioButton</c> 拼是拼不出来的。
///         成组就用它。</item>
///   <item><c>RadioButton</c> 靠 <c>groupName</c> 成组（或直接放进同一个父容器）：
///         适合"这一组散在表单的不同位置"这种不常见的形态。</item>
///   <item>两者都按下标定值 / 按布尔受控，与 <c>ComboBox</c> 同一条纪律：
///         <c>Optional</c> 不传 = 这一轮不管它。</item>
/// </list>
/// </remarks>
public sealed class RadioButtonsBasic : Component
{
    private static readonly string[] Priorities = { "低", "中", "高" };

    public override Element Render()
    {
        var (priority, setPriority) = UseState(1);
        var (plan, setPlan) = UseState("按月");

        return VStack(16,
            TextBlock("RadioButtons：官方的分组控件（受控）").Body(),

            // 官方那页给这一档带 Header（整组上方那行小字），它归分组控件自己，
            // 不是里面某一颗按钮的 Content —— 别把标题写成第一颗按钮的文字。
            RadioButtons(Priorities, Optional<int>.Of(priority), setPriority, header: "优先级"),
            TextBlock($"当前优先级：{Priorities[priority]}").Caption().Subtle(),

            TextBlock("RadioButton：自己成组（groupName）").Body(),

            VStack(6,
                RadioButton("按月", Optional<bool>.Of(plan == "按月"),
                    onIsCheckedChanged: v => Pick(v, "按月"), groupName: "plan"),
                RadioButton("按年", Optional<bool>.Of(plan == "按年"),
                    onIsCheckedChanged: v => Pick(v, "按年"), groupName: "plan")),
            TextBlock($"当前：{plan}").Caption().Subtle(),

            TextBlock("方向键在组内上下移动、焦点只落在选中项 —— 这些是 RadioButtons "
                      + "（WinUI 2 那个分组控件）的模板给的，自己排一排 RadioButton 拼不出来。"
                      + "散在表单不同位置时才用 groupName。")
                .Caption()
                .Subtle()
                .Wrap());

        // 单选那一组会逐项回调（被取消的那条也来一次 false），只认 true。
        void Pick(bool picked, string name)
        {
            if (picked)
            {
                setPlan(name);
            }
        }
    }
}
