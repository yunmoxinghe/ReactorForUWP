using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 输入类控件的工厂（<see cref="Factories"/> 的 partial 延续）。
/// 这三个以前缺得很明显：做登录框只能用 <c>TextBox</c> 顶替密码框，
/// 做搜索框只能自己拼 <c>TextBox</c> + <c>ListView</c> 假装建议列表——
/// 都是替代实现。现在都落成真控件。
/// </summary>
public static partial class Factories
{
    /// <summary>密码框（真 <see cref="PasswordBox"/>）。</summary>
    /// <param name="passwordChar">掩码字符（只取第一个；默认官方圆点）。</param>
    public static PasswordBoxElement PasswordBox(
        Optional<string> value = default,
        Action<string>? onChanged = null,
        string? placeholderText = null,
        string? header = null,
        string? passwordChar = null) =>
        new(value, onChanged, placeholderText) { Header = header, PasswordChar = passwordChar };

    /// <summary>带建议的输入框（真 <see cref="AutoSuggestBox"/>）。</summary>
    /// <param name="onTextChanged">文本变化（官方 <c>TextChanged</c>）。</param>
    /// <param name="onQuerySubmitted">提交查询（官方 <c>QuerySubmitted</c>：回车或点候选）。</param>
    /// <param name="onSuggestionChosen">选中某个候选（官方 <c>SuggestionChosen</c>）。</param>
    /// <remarks>
    /// 回调参数放在<b>尾部</b>，加进来之前写的调用点一行都不用改。
    /// <para>
    /// <b>点候选会连带再抛一次 <c>QuerySubmitted</c></b>（官方就这么设计的：
    /// <c>SuggestionChosen</c> → <c>QuerySubmitted</c>，后者带
    /// <c>ChosenSuggestion</c>）。两个事件都原样抛出，谁都不替谁做决定——
    /// 需要"点候选直接跳转、回车才看结果列表"这种区分的调用方自己记一下
    /// 上一次 chosen 的是什么。
    /// </para>
    /// </remarks>
    public static AutoSuggestBoxElement AutoSuggestBox(
        Optional<string> text = default,
        IReadOnlyList<string>? suggestions = null,
        string? placeholderText = null,
        string? header = null,
        Action<string>? onTextChanged = null,
        Action<string>? onQuerySubmitted = null,
        Action<string>? onSuggestionChosen = null,
        Element? queryIcon = null,
        bool updateTextOnSelect = true) =>
        new(text, suggestions)
        {
            PlaceholderText = placeholderText,
            Header = header,
            UpdateTextOnSelect = updateTextOnSelect,
            OnTextChanged = onTextChanged,
            OnQuerySubmitted = onQuerySubmitted,
            OnSuggestionChosen = onSuggestionChosen,
            QueryIcon = queryIcon,
        };

    /// <summary>数字输入框（WinUI 2 的真 <see cref="MuxControls.NumberBox"/>）。</summary>
    public static NumberBoxElement NumberBox(
        Optional<double> value = default,
        Action<double>? onValueChanged = null,
        string? header = null,
        double? min = null,
        double? max = null,
        double? smallChange = null,
        double? largeChange = null,
        MuxControls.NumberBoxSpinButtonPlacementMode? spinButtonPlacementMode = null,
        bool? isWrapEnabled = null,
        int? decimalPlaces = null,
        bool? acceptsExpression = null,
        MuxControls.NumberBoxValidationMode? validationMode = null) =>
        new(value, onValueChanged)
        {
            Header = header,
            Min = min,
            Max = max,
            SmallChange = smallChange,
            LargeChange = largeChange,
            SpinButtonPlacementMode = spinButtonPlacementMode,
            IsWrapEnabled = isWrapEnabled,
            DecimalPlaces = decimalPlaces,
            AcceptsExpression = acceptsExpression,
            ValidationMode = validationMode,
        };
}
