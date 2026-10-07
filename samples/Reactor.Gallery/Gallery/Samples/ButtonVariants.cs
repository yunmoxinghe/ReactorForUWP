using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
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
        HStack(8,
            Button("标准按钮", () => { }),
            Button("强调按钮", () => { }).Accent(),
            Button("禁用", () => { }).Disabled(),
            Button(
                HStack(6,
                    FontIcon("\uE896"),
                    TextBlock("带图标的内容")),
                () => { }).AutomationName("带图标的按钮"));
}
