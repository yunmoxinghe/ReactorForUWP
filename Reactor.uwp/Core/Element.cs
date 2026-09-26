using System;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// UI 节点的轻量、不可变描述（虚拟元素）。
/// Element 只持有描述数据，永远不直接接触真实控件；
/// 真实控件的创建与更新由 Reconciler 负责。
/// </summary>
public abstract record Element
{
    /// <summary>跨重渲染保持节点身份的可选键（类似 React 的 key）。</summary>
    public string? Key { get; init; }

    /// <summary>布局/外观修饰（边距、尺寸、对齐等），由链式扩展方法填充。</summary>
    public ElementModifiers? Modifiers { get; init; }

    /// <summary>
    /// 字符串可直接当元素用（等价于 <c>Factories.TextBlock(text)</c>）。
    /// 对齐官方 Reactor 的语法糖：<c>VStack("标题", Button("确定"))</c>。
    /// </summary>
    public static implicit operator Element(string text) =>
        Microsoft.UI.Reactor.Factories.TextBlock(text);
}

/// <summary>
/// 修饰数据。修饰符扩展方法通过 record 的 <c>with</c> 拷贝写入，
/// 在 mount/update 时才应用到真实控件上。
/// </summary>
/// <remarks>对齐官方 Microsoft.UI.Reactor：非 sealed，并提供 Merge 以便修饰符叠加。</remarks>
public record ElementModifiers
{
    public Thickness? Margin { get; init; }
    public Thickness? Padding { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? MinWidth { get; init; }
    public double? MinHeight { get; init; }
    public double? MaxWidth { get; init; }
    public double? MaxHeight { get; init; }
    public HorizontalAlignment? HorizontalAlignment { get; init; }
    public VerticalAlignment? VerticalAlignment { get; init; }
    public double? FontSize { get; init; }
    public Brush? Foreground { get; init; }
    public Color? ForegroundColor { get; init; }
    public Brush? Background { get; init; }
    public Color? BackgroundColor { get; init; }
    public Brush? BorderBrush { get; init; }
    public Thickness? BorderThickness { get; init; }
    public double? Opacity { get; init; }
    public bool? IsVisible { get; init; }
    public bool? IsEnabled { get; init; }
    public string? AutomationName { get; init; }
    public string? AutomationId { get; init; }
    public string? ToolTip { get; init; }
    public BackdropKind? Backdrop { get; init; }

    /// <summary>本元素向其子树提供的 Context 值（见 <see cref="Context{T}"/>）。</summary>
    public IReadOnlyDictionary<ContextBase, object?>? ContextValues { get; init; }

    /// <summary>合并另一组修饰符：以 other 的非 null 值为准（官方同语义）。</summary>
    public ElementModifiers Merge(ElementModifiers other) => new()
    {
        Margin = other.Margin ?? Margin,
        Padding = other.Padding ?? Padding,
        Width = other.Width ?? Width,
        Height = other.Height ?? Height,
        MinWidth = other.MinWidth ?? MinWidth,
        MinHeight = other.MinHeight ?? MinHeight,
        MaxWidth = other.MaxWidth ?? MaxWidth,
        MaxHeight = other.MaxHeight ?? MaxHeight,
        HorizontalAlignment = other.HorizontalAlignment ?? HorizontalAlignment,
        VerticalAlignment = other.VerticalAlignment ?? VerticalAlignment,
        FontSize = other.FontSize ?? FontSize,
        Foreground = other.Foreground ?? Foreground,
        ForegroundColor = other.ForegroundColor ?? ForegroundColor,
        Background = other.Background ?? Background,
        BackgroundColor = other.BackgroundColor ?? BackgroundColor,
        BorderBrush = other.BorderBrush ?? BorderBrush,
        BorderThickness = other.BorderThickness ?? BorderThickness,
        Opacity = other.Opacity ?? Opacity,
        IsVisible = other.IsVisible ?? IsVisible,
        IsEnabled = other.IsEnabled ?? IsEnabled,
        AutomationName = other.AutomationName ?? AutomationName,
        AutomationId = other.AutomationId ?? AutomationId,
        ToolTip = other.ToolTip ?? ToolTip,
        Backdrop = other.Backdrop ?? Backdrop,
        ContextValues = other.ContextValues ?? ContextValues,
    };
}

/// <summary>组件元素：描述一个子组件的类型与 props，实例由 Reconciler 创建并维护。</summary>
public record ComponentElement(
    Type ComponentType,
    object? Props = null) : Element;

/// <summary>强类型组件元素：允许通过 record with 语法修改 props。</summary>
public sealed record ComponentElement<TProps>(
    Type ComponentType,
    TProps Props) : ComponentElement(ComponentType, Props)
{
    public new TProps Props
    {
        get => (TProps)base.Props!;
        init => base.Props = value;
    }
}
