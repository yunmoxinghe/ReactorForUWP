using System;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 按钮族补齐：<c>ToggleButton</c> / <c>RepeatButton</c> / <c>ToggleSplitButton</c>。
/// </summary>
/// <remarks>
/// WinUI 3 Gallery 的「Buttons」一页列了 <c>Button</c> / <c>CheckBox</c> /
/// <c>DropDownButton</c> / <c>HyperlinkButton</c> / <c>RadioButton</c> /
/// <c>RadioButtons</c> / <c>RepeatButton</c> / <c>SplitButton</c> /
/// <c>ToggleButton</c> / <c>ToggleSplitButton</c> 十个；此前缺的正是这三个。
/// <c>ToggleButton</c> / <c>RepeatButton</c> 是 UWP 原生，
/// <c>ToggleSplitButton</c> 是 WinUI 2——都是真控件。
/// </remarks>
public static partial class Factories
{
    /// <summary>
    /// 会"按下就保持"的按钮。
    /// </summary>
    /// <param name="label">按钮文字。</param>
    /// <param name="content">任意内容（给了就优先于 <paramref name="label"/>）。</param>
    /// <param name="isChecked">选中态。不传非受控；传 <c>bool?</c> 受控，<c>null</c> 是中间态。</param>
    /// <param name="onIsCheckedChanged">状态变化回调（中间态不回调）。</param>
    /// <param name="isEnabled">禁用态。</param>
    public static ToggleButtonElement ToggleButton(
        string? label = null,
        Element? content = null,
        Optional<bool?> isChecked = default,
        Action<bool>? onIsCheckedChanged = null,
        bool? isEnabled = null) =>
        new(label)
        {
            Content = content,
            IsChecked = isChecked,
            OnIsCheckedChanged = onIsCheckedChanged,
            IsEnabled = isEnabled,
        };

    /// <summary>
    /// 按住不放会连续触发点击的按钮。
    /// </summary>
    /// <param name="label">按钮文字。</param>
    /// <param name="content">任意内容（给了就优先于 <paramref name="label"/>）。</param>
    /// <param name="onClick">点击回调，按住时会被反复调用。</param>
    /// <param name="delay">按住多久之后开始连发（毫秒）。</param>
    /// <param name="interval">连发间隔（毫秒）。</param>
    /// <param name="isEnabled">禁用态。</param>
    public static RepeatButtonElement RepeatButton(
        string? label = null,
        Element? content = null,
        Action? onClick = null,
        int? delay = null,
        int? interval = null,
        bool? isEnabled = null) =>
        new(label)
        {
            Content = content,
            OnClick = onClick,
            Delay = delay,
            Interval = interval,
            IsEnabled = isEnabled,
        };

    /// <summary>
    /// 会"按下就保持"的拆分按钮：左半执行并锁住按下态，右半展开菜单。
    /// </summary>
    /// <param name="label">按钮文字。</param>
    /// <param name="content">任意内容（给了就优先于 <paramref name="label"/>）。</param>
    /// <param name="isChecked">选中态，规则同 <see cref="ToggleButton"/>。</param>
    /// <param name="onIsCheckedChanged">选中态变化回调（官方 <c>IsCheckedChanged</c>）。</param>
    /// <param name="onClick">左半边的点击回调（官方 <c>Click</c>）。</param>
    /// <param name="flyout">右半边展开的浮出层，一般给 <see cref="MenuFlyout"/>。</param>
    public static ToggleSplitButtonElement ToggleSplitButton(
        string? label = null,
        Element? content = null,
        Optional<bool?> isChecked = default,
        Action<bool>? onIsCheckedChanged = null,
        Action? onClick = null,
        Element? flyout = null) =>
        new(label)
        {
            Content = content,
            IsChecked = isChecked,
            OnIsCheckedChanged = onIsCheckedChanged,
            OnClick = onClick,
            Flyout = flyout,
        };
}
