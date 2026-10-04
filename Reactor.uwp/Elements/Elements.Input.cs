using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 密码框，对应 <see cref="PasswordBox"/>。
/// </summary>
/// <param name="Value">受控密码；<see cref="Optional{T}.Unset"/> = 非受控。</param>
/// <param name="OnChanged">用户输入回调（受控时由它驱动 setState）。</param>
/// <param name="PlaceholderText">占位提示（XAML 的 <c>PlaceholderText</c>）。</param>
/// <remarks>
/// 补这个控件的原因很实在：以前只有 <c>TextBox</c>，要做登录框只能拿它顶替——
/// 那是替代实现（没有密码掩码、没有显示密码按钮、也没有"密码"语义的无障碍角色）。
/// 这里是真的 <see cref="PasswordBox"/>。
/// </remarks>
public sealed record PasswordBoxElement(
    Optional<string> Value = default,
    Action<string>? OnChanged = null,
    string? PlaceholderText = null) : Element
{
    public string? Header { get; init; }

    /// <summary>最大长度（0 = 不限）。</summary>
    public int? MaxLength { get; init; }

    /// <summary>是否显示"显示密码"按钮（XAML 的 <c>IsPasswordRevealButtonEnabled</c>）。</summary>
    public bool? IsPasswordRevealButtonEnabled { get; init; }
}

/// <summary>
/// 带建议的输入框，对应 <see cref="AutoSuggestBox"/>。
/// </summary>
/// <param name="Text">受控文本；<see cref="Optional{T}.Unset"/> = 非受控。</param>
/// <param name="Suggestions">候选列表（字符串）。</param>
/// <remarks>
/// <b>三个回调都是官方事件</b>（<c>TextChanged</c> / <c>QuerySubmitted</c> /
/// <c>SuggestionChosen</c>），只是把事件参数收成了常用的那一个值——
/// 需要 <c>AutoSuggestBoxTextChangedEventArgs.Reason</c>（区分"用户输入"与
/// "程序选了候选"）时用 <c>Native()</c> 逃生舱拿真实控件。
/// </remarks>
public sealed record AutoSuggestBoxElement(
    Optional<string> Text = default,
    IReadOnlyList<string>? Suggestions = null) : Element
{
    public string? PlaceholderText { get; init; }
    public string? Header { get; init; }

    /// <summary>文本变化（官方 <c>TextChanged</c>）。参数是当前文本。</summary>
    public Action<string>? OnTextChanged { get; init; }

    /// <summary>
    /// 提交查询（官方 <c>QuerySubmitted</c>：回车或点候选）。参数是查询文本。
    /// </summary>
    public Action<string>? OnQuerySubmitted { get; init; }

    /// <summary>
    /// 选中某个候选（官方 <c>SuggestionChosen</c>）。参数是该项（这里是字符串）。
    /// </summary>
    public Action<string>? OnSuggestionChosen { get; init; }
}

/// <summary>
/// 数字输入框，对应 WinUI 2 的 <see cref="MuxControls.NumberBox"/>。
/// </summary>
/// <param name="Value">受控数值；<see cref="Optional{T}.Unset"/> = 非受控。</param>
/// <param name="OnValueChanged">数值变化回调（官方 <c>ValueChanged</c>）。</param>
/// <remarks>
/// <b>空值就是 <see cref="double.NaN"/></b>：NumberBox 的 <c>Value</c> 是 double，
/// 没有"未设置"这一档，官方用 NaN 表示空（<c>NumberBoxValueChangedEventArgs</c>
/// 里新值为 NaN 即用户清空）。这里原样映射，不做 "double?" 包装——
/// 包装会在每次读写时引入一次"到底是 null 还是 NaN"的转换歧义。
/// </remarks>
public sealed record NumberBoxElement(
    Optional<double> Value = default,
    Action<double>? OnValueChanged = null) : Element
{
    public string? Header { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }

    /// <summary>步进步长（上下按钮 / 方向键）。</summary>
    public double? SmallChange { get; init; }

    /// <summary>大幅步长（PageUp / PageDown）。</summary>
    public double? LargeChange { get; init; }

    /// <summary>加减按钮的显示方式（XAML 的 <c>SpinButtonPlacementMode</c>）。</summary>
    public MuxControls.NumberBoxSpinButtonPlacementMode? SpinButtonPlacementMode { get; init; }

    /// <summary>到边界后是否回绕（XAML 的 <c>IsWrapEnabled</c>）。</summary>
    public bool? IsWrapEnabled { get; init; }

    /// <summary>小数位数（<c>NumberFormatter</c> 的简化入口，null = 不限）。</summary>
    public int? DecimalPlaces { get; init; }
}
