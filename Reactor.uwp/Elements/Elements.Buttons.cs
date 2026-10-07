using System;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 会"按下就保持"的按钮，对应 UWP 原生的 <c>Windows.UI.Xaml.Controls.ToggleButton</c>。
/// </summary>
/// <remarks>
/// 与 <c>CheckBox</c> 之别是<b>外观</b>（按钮 vs 方框 + 勾），不是行为：
/// 两者都是 <c>IsChecked</c> + <c>Checked</c> / <c>Unchecked</c>，连三态
/// （<c>IsChecked = null</c>）都一样。所以受控写法照抄 <c>CheckBox</c> 那一份。
/// <para>
/// 什么时候用它、什么时候用 <c>CheckBox</c>：官方的用法是"开关立刻生效、且看得出来
/// 是按下的"（比如加粗），<c>CheckBox</c> 用于"勾上一项、稍后随表单一起提交"。
/// </para>
/// </remarks>
public sealed record ToggleButtonElement(string? Label = null) : Element
{
    /// <summary>任意内容（XAML 里 <c>&lt;ToggleButton&gt;&lt;…&gt;&lt;/ToggleButton&gt;</c> 那一种）。</summary>
    public Element? Content { get; init; }

    /// <summary>
    /// 选中态。<see cref="Optional{T}.Unset"/> 为非受控；传 <c>bool?</c> 则受控，
    /// 其中 <c>null</c> 是三态的"中间态"（<c>IsThreeState</c> 打开时才有意义）。
    /// </summary>
    public Optional<bool?> IsChecked { get; init; }

    /// <summary>状态变化回调。与 <c>CheckBox</c> 一致：中间态（<c>null</c>）不回调。</summary>
    public Action<bool>? OnIsCheckedChanged { get; init; }

    /// <summary>禁用态（XAML 的 <c>IsEnabled</c>）。用户改不动它，所以写它不算受控站点。</summary>
    public bool? IsEnabled { get; init; }
}

/// <summary>
/// 按住不放会连续触发的按钮，对应 UWP 原生的 <c>Windows.UI.Xaml.Controls.RepeatButton</c>。
/// </summary>
/// <remarks>
/// 它继承 <c>ButtonBase</c>，所以只有 <c>Click</c>，<b>没有</b>选中态——
/// 与 <see cref="ToggleButtonElement"/> 是两条不同的路。
/// <para>
/// <c>Delay</c> 是"按住多久之后开始连发"，<c>Interval</c> 是"之后每隔多久发一次"，
/// 两个都是毫秒。都留给官方默认值的时候，按住就是"先点一下，稍后开始连发"。
/// </para>
/// </remarks>
public sealed record RepeatButtonElement(string? Label = null) : Element
{
    /// <summary>任意内容（带图标的按钮走这里）。</summary>
    public Element? Content { get; init; }

    /// <summary>点击回调。按住时会被<b>反复</b>调用，这是这个控件的全部意义。</summary>
    public Action? OnClick { get; init; }

    /// <summary>按住多久之后开始连发（毫秒）。不传用官方默认。</summary>
    public int? Delay { get; init; }

    /// <summary>连发的间隔（毫秒）。不传用官方默认。</summary>
    public int? Interval { get; init; }

    /// <summary>禁用态（XAML 的 <c>IsEnabled</c>）。</summary>
    public bool? IsEnabled { get; init; }
}

/// <summary>
/// 会"按下就保持"的拆分按钮，对应 WinUI 2 的 <c>Microsoft.UI.Xaml.Controls.ToggleSplitButton</c>。
/// </summary>
/// <remarks>
/// 它把两件事合在一个控件上：左半边是
/// <see cref="SplitButtonElement"/>（点一下执行 + 右半边展开菜单），
/// 同时整个按钮带一个 <c>IsChecked</c>——按下左半边会把它<b>锁在按下状态</b>
/// （典型用法：编辑器里的"项目符号"，按下即生效、再按一次取消）。
/// <para>
/// 回执通道是 <c>IsCheckedChanged</c>（<b>不是</b> <c>Checked</c> / <c>Unchecked</c>）：
/// 它是 WinUI 2 的控件，不继承 UWP 的 <c>ToggleButton</c>，这两个事件它都没有。
/// 受控 <c>IsChecked</c> 的回声抑制与 <see cref="ToggleButtonElement"/> 同一套，
/// 只是换了个事件名。
/// </para>
/// </remarks>
public sealed record ToggleSplitButtonElement(string? Label = null) : Element
{
    /// <summary>任意内容（带图标的按钮走这里）。</summary>
    public Element? Content { get; init; }

    /// <summary>选中态，受控规则同 <see cref="ToggleButtonElement.IsChecked"/>。</summary>
    public Optional<bool?> IsChecked { get; init; }

    /// <summary>选中态变化回调（官方 <c>IsCheckedChanged</c>）。</summary>
    public Action<bool>? OnIsCheckedChanged { get; init; }

    /// <summary>左半边的点击回调，对应官方 <c>Click</c>（继承自 <c>SplitButton</c>）。</summary>
    public Action? OnClick { get; init; }

    /// <summary>右半边展开的浮出层，对应官方 <c>Flyout</c>，一般给 <see cref="MenuFlyoutElement"/>。</summary>
    public Element? Flyout { get; init; }
}
