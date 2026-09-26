using System;
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
}

/// <summary>
/// 修饰数据。修饰符扩展方法通过 record 的 <c>with</c> 拷贝写入，
/// 在 mount/update 时才应用到真实控件上。
/// </summary>
public sealed record ElementModifiers
{
    public Thickness? Margin { get; init; }
    public Thickness? Padding { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public HorizontalAlignment? HorizontalAlignment { get; init; }
    public VerticalAlignment? VerticalAlignment { get; init; }
    public double? FontSize { get; init; }
    public Brush? Foreground { get; init; }
    public Color? ForegroundColor { get; init; }
    public bool? IsEnabled { get; init; }
    public string? AutomationName { get; init; }
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
