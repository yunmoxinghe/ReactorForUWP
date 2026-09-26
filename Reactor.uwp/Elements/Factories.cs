using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// DSL 入口：using static Microsoft.UI.Reactor.Factories;
/// 工厂方法只产出 Element 记录（虚拟树），不创建真实控件。
/// </summary>
public static class Factories
{
    public static TextBlockElement TextBlock(string text) => new(text);

    public static ButtonElement Button(string label, Action? onClick = null) =>
        new(label, onClick);

    public static StackPanelElement VStack(params Element?[] children) =>
        new(Orientation.Vertical, children);

    public static StackPanelElement HStack(params Element?[] children) =>
        new(Orientation.Horizontal, children);

    public static InfoBarElement InfoBar(
        string message,
        MuxControls.InfoBarSeverity severity = MuxControls.InfoBarSeverity.Informational) =>
        new(message, severity);

    /// <summary>嵌入一个无 props 的子组件。</summary>
    public static ComponentElement Component<T>() where T : Component, new() =>
        new(typeof(T));

    /// <summary>嵌入一个带 props 的子组件。</summary>
    public static ComponentElement<TProps> Component<T, TProps>(TProps props)
        where T : Component<TProps>, new() =>
        new(typeof(T), props);

    public static TextBoxElement TextBox(
        Optional<string> value = default,
        Action<string>? onChanged = null,
        string? placeholderText = null,
        string? header = null) =>
        new(value, onChanged, placeholderText) { Header = header };

    public static CheckBoxElement CheckBox(
        Optional<bool?> isChecked = default,
        Action<bool>? onIsCheckedChanged = null,
        string? label = null) =>
        new(isChecked, onIsCheckedChanged, label);

    public static SliderElement Slider(
        Optional<double> value = default,
        double min = 0,
        double max = 100,
        Action<double>? onValueChanged = null) =>
        new(value, min, max, onValueChanged);

    public static ScrollViewerElement ScrollViewer(Element? content = null) =>
        new(content);
}
