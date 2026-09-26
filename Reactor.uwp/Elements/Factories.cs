using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// DSL 入口：using static Microsoft.UI.Reactor.Factories;
/// 工厂方法只产出 Element 记录（虚拟树），不创建真实控件。
/// </summary>
/// <remarks>对齐官方 Microsoft.UI.Reactor：partial 静态类，方法名/参数名尽量一致。</remarks>
public static partial class Factories
{
    public static TextBlockElement TextBlock(string content) => new(content);

    public static ButtonElement Button(string label, Action? onClick = null) =>
        new(label, onClick);

    public static StackElement VStack(params Element?[] children) =>
        new(Orientation.Vertical, FilterChildren(children));

    public static StackElement VStack(double spacing, params Element?[] children) =>
        new(Orientation.Vertical, FilterChildren(children)) { Spacing = spacing };

    public static StackElement HStack(params Element?[] children) =>
        new(Orientation.Horizontal, FilterChildren(children));

    public static StackElement HStack(double spacing, params Element?[] children) =>
        new(Orientation.Horizontal, FilterChildren(children)) { Spacing = spacing };

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

    public static ScrollViewerElement ScrollViewer(Element? child = null) =>
        new(child);

    // ── 组合子（对齐官方 Dsl） ─────────────────────────────────

    /// <summary>不渲染任何内容的哨兵元素。</summary>
    public static EmptyElement Empty() => EmptyElement.Instance;

    /// <summary>把多个子元素打包成一个元素（渲染为裸 Grid）。</summary>
    public static GroupElement Group(params Element?[] children) =>
        new(FilterChildren(children));

    /// <summary>条件成立才渲染，否则返回 Empty。</summary>
    public static Element When(bool condition, Func<Element> then) =>
        condition ? then() : EmptyElement.Instance;

    /// <summary>条件渲染，可带 else 分支。</summary>
    public static Element If(bool condition, Func<Element> then, Func<Element>? otherwise = null) =>
        condition ? then() : (otherwise?.Invoke() ?? EmptyElement.Instance);

    /// <summary>把集合映射成一组子元素。</summary>
    public static GroupElement ForEach<T>(IEnumerable<T> items, Func<T, Element?> render) =>
        new(FilterChildren(items.Select(render).ToArray()));

    /// <summary>把集合映射成一组子元素（带索引）。</summary>
    public static GroupElement ForEach<T>(IEnumerable<T> items, Func<T, int, Element?> render) =>
        new(FilterChildren(items.Select(render).ToArray()));

    // ── Thickness 助手（对齐官方 Factories.Thick） ─────────────

    public static Thickness Thick(double uniform) => new(uniform);

    public static Thickness Thick(double horizontal, double vertical) =>
        new(horizontal, vertical, horizontal, vertical);

    public static Thickness Thick(double left, double top, double right, double bottom) =>
        new(left, top, right, bottom);

    /// <summary>过滤掉 null 子元素（官方 FilterChildren 同语义）。</summary>
    private static IReadOnlyList<Element?> FilterChildren(Element?[] children) =>
        children.Where(c => c is not null).ToArray();
}
