using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>ToggleButton</c>：按下就保持的按钮，受控 <c>IsChecked</c>。
/// </summary>
/// <remarks>
/// 与 <c>CheckBox</c> 的差别是<b>外观</b>（按钮 vs 方框 + 勾），不是行为：
/// 两者都是 <c>IsChecked</c> + <c>Checked</c> / <c>Unchecked</c>，连三态都一样。
/// 官方的用法分工是：<c>ToggleButton</c> 用于"按下立刻生效、且看得出来是按下的"
/// （比如编辑器里的加粗），<c>CheckBox</c> 用于"勾上一项、稍后随表单一起提交"。
/// <para>
/// <b>只读不要用"不给回调"来做。</b>不给回调之后按钮照样能按，只是按了没人理——
/// 用户看到的是"点了没反应"。要禁用就给 <c>isEnabled: false</c>。
/// </para>
/// </remarks>
public sealed class ToggleButtonBasic : Component
{
    public override Element Render()
    {
        var (bold, setBold) = UseState(false);

        return VStack(14,
            TextBlock("受控").Body(),
            ToggleButton("加粗", isChecked: Optional<bool?>.Of(bold), onIsCheckedChanged: setBold),
            TextBlock(bold ? "当前：按下（加粗生效）" : "当前：弹起").Caption().Subtle(),

            TextBlock("任意内容").Body(),
            ToggleButton(
                content: HStack(8,
                    FontIcon("\uE8DD"),
                    TextBlock("带图标的那一档")),
                isChecked: Optional<bool?>.Of(bold),
                onIsCheckedChanged: setBold),
            TextBlock("给了 content 就优先于 label：两个槽位都是官方的 Content，只是填法不同。")
                .Caption().Subtle(),

            TextBlock("禁用").Body(),
            ToggleButton("按不动", isChecked: Optional<bool?>.Of(true), isEnabled: false),
            TextBlock("禁用是 isEnabled，不是「不给回调」——后者只是「按了没人理」。")
                .Caption().Subtle());
    }
}
