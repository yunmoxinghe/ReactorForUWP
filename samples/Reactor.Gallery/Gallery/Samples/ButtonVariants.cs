using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 按钮的三种常见变体：标准 / 强调（Accent） / 禁用。
/// </summary>
/// <remarks>
/// <b>强调按钮是官方那一个样式</b>，不是自己配一个蓝色背景：<c>.Accent()</c> 套的是
/// <c>AccentButtonStyle</c>，于是高对比度主题、指针态颜色、焦点矩形，全都跟着
/// 官方走。自己配色的版本在系统高对比度下会彻底看不见边框。
/// <para>
/// 按钮内容是任意元素（这里是图标 + 文字），因为它们都是 <c>ContentControl</c>。
/// </para>
/// </remarks>
public sealed class ButtonVariants : Component
{
    public override Element Render() =>
        // 官方那一排两颗样式按钮之间是 Spacing 16（本地原先用的 8 是自定值）。
        HStack(16,
            Button("标准按钮", () => { }),
            Button("强调按钮", () => { }).Accent(),
            // 官方第二颗用的是 SubtleButtonStyle：同样在 NamedStyles 里有现成的 .Subtle()。
            Button("弱化按钮", () => { }).Subtle(),
            Button("禁用", () => { }).Disabled(),
            Button(
                HStack(6,
                    FontIcon("\uE896"),
                    TextBlock("带图标的内容")),
                () => { }).AutomationName("带图标的按钮"),

            // 官方还有一档"会自动折行的按钮"：宽度由 MaxWidth 限住，换行发生在
            // 内容那个 TextBlock 上——按钮自己没有 TextWrapping 这个属性。
            Button(
                    TextBlock("按钮文字长到一行放不下时会自己折行，上限由外层决定。")
                        .TextWrapping(TextWrapping.WrapWholeWords))
                .MaxWidth(240));
}
