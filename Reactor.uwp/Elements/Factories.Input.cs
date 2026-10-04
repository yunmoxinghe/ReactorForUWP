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
    public static PasswordBoxElement PasswordBox(
        Optional<string> value = default,
        Action<string>? onChanged = null,
        string? placeholderText = null,
        string? header = null) =>
        new(value, onChanged, placeholderText) { Header = header };

    /// <summary>带建议的输入框（真 <see cref="AutoSuggestBox"/>）。</summary>
    public static AutoSuggestBoxElement AutoSuggestBox(
        Optional<string> text = default,
        IReadOnlyList<string>? suggestions = null,
        string? placeholderText = null,
        string? header = null) =>
        new(text, suggestions)
        {
            PlaceholderText = placeholderText,
            Header = header,
        };

    /// <summary>数字输入框（WinUI 2 的真 <see cref="MuxControls.NumberBox"/>）。</summary>
    public static NumberBoxElement NumberBox(
        Optional<double> value = default,
        Action<double>? onValueChanged = null,
        string? header = null,
        double? min = null,
        double? max = null) =>
        new(value, onValueChanged)
        {
            Header = header,
            Min = min,
            Max = max,
        };
}
