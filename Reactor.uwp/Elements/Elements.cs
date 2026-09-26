using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>文本节点。参数名对齐官方 Reactor（Content，不是 Text）。</summary>
public sealed record TextBlockElement(string Content) : Element;

/// <summary>按钮节点：点击回调直接以声明方式携带。</summary>
public sealed record ButtonElement(string Label, Action? OnClick = null) : Element;

/// <summary>
/// 线性布局容器，对应 <see cref="StackPanel"/>。
/// 类型名对齐官方 Reactor（StackElement，不是 StackPanelElement）。
/// </summary>
public sealed record StackElement(
    Orientation Orientation,
    IReadOnlyList<Element?> Children) : Element
{
    /// <summary>子元素间距（UWP 的 StackPanel.Spacing）。null = 不改动。</summary>
    public double? Spacing { get; init; }
}

/// <summary>不产生任何真实控件的占位元素（对齐官方 EmptyElement 哨兵）。</summary>
public sealed record EmptyElement : Element
{
    public static readonly EmptyElement Instance = new();
}

/// <summary>不额外引入布局策略的多子元素容器，对应一个裸 Grid。</summary>
public sealed record GroupElement(IReadOnlyList<Element?> Children) : Element;

/// <summary>WinUI 2 的 InfoBar，用于验证 XamlControlsResources 纯代码加载链路。</summary>
public sealed record InfoBarElement(
    string Message,
    MuxControls.InfoBarSeverity Severity = MuxControls.InfoBarSeverity.Informational) : Element;

/// <summary>
/// 文本输入框。Value 默认 <see cref="Optional{T}.Unset"/>（非受控，控件自主持有文本）；
/// 传入任意 string（隐式装箱）即成受控，用户输入经 OnChanged 回调驱动组件状态回写。
/// </summary>
public sealed record TextBoxElement(
    Optional<string> Value = default,
    Action<string>? OnChanged = null,
    string? PlaceholderText = null) : Element
{
    public string? Header { get; init; }
}

/// <summary>
/// 复选框。IsChecked 为 <see cref="Optional{T}.Unset"/> 时非受控；
/// 传入 bool?（含 null = 三态中间态）则受控。OnIsCheckedChanged 只在选中/取消时回调。
/// </summary>
public sealed record CheckBoxElement(
    Optional<bool?> IsChecked = default,
    Action<bool>? OnIsCheckedChanged = null,
    string? Label = null) : Element;

/// <summary>滑块。Value 为 <see cref="Optional{T}.Unset"/> 时非受控，传入 double 则受控。</summary>
public sealed record SliderElement(
    Optional<double> Value = default,
    double Min = 0,
    double Max = 100,
    Action<double>? OnValueChanged = null) : Element;

/// <summary>滚动容器，对应 <see cref="ScrollViewer"/>。参数名对齐官方（Child）。</summary>
public sealed record ScrollViewerElement(Element? Child = null) : Element;
