using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// 命名样式键。解析顺序：自定义样式表（<see cref="Microsoft.UI.Reactor.StyleSheet"/>）
    /// → 应用资源字典（<c>Application.Current.Resources</c>，含 WinUI 内置样式）。
    /// 由 <c>.ApplyStyle("CaptionTextBlockStyle")</c> 写入。
    /// </summary>
    public string? StyleKey { get; init; }

    /// <summary>文本换行（作用于 TextBlock / TextBox 等）。</summary>
    public TextWrapping? TextWrapping { get; init; }

    /// <summary>字重（作用于 TextBlock / Control）。</summary>
    public Windows.UI.Text.FontWeight? FontWeight { get; init; }

    /// <summary>文本对齐（作用于 TextBlock / TextBox）。</summary>
    public TextAlignment? TextAlignment { get; init; }

    /// <summary>
    /// 把该元素注册为窗口的自定义标题栏拖拽区（等价 XAML 里的
    /// <c>Window.Current.SetTitleBar(element)</c>）。
    /// 需要宿主事先调用 <c>ApplicationView.TitleBar.ExtendViewIntoTitleBar = true</c>。
    /// </summary>
    public bool? IsTitleBar { get; init; }

    /// <summary>该元素（及其子树）的请求主题。对应 XAML 的 <c>RequestedTheme</c>。</summary>
    public ElementTheme? RequestedTheme { get; init; }

    /// <summary>
    /// 根元素声明"我自己管理标题栏区域布局"：宿主不再自动加顶部 Padding。
    /// </summary>
    /// <remarks>
    /// 宿主默认把根容器下压一个标题栏高度（让背景材质铺满整窗）。
    /// 但像 WinUI 设置类模板那样的页面，标题栏区是页面自己的第一行
    /// （<c>Grid Height=32</c> + <c>SetTitleBar</c>），再叠加宿主的下压就变成 64px。
    /// 声明这个标记即可完全接管。
    /// </remarks>
    public bool? OwnsTitleBar { get; init; }

    /// <summary>最大行数（作用于 TextBlock）。</summary>
    public int? MaxLines { get; init; }

    /// <summary>
    /// Grid 附加位置（行/列/跨行/跨列），由 <c>.Grid(row: …)</c> 写入，
    /// 只有直接挂在 <c>Grid</c> 下的子元素会被应用。
    /// </summary>
    public Microsoft.UI.Reactor.GridAttached? Grid { get; init; }

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
        StyleKey = other.StyleKey ?? StyleKey,
        TextWrapping = other.TextWrapping ?? TextWrapping,
        FontWeight = other.FontWeight ?? FontWeight,
        TextAlignment = other.TextAlignment ?? TextAlignment,
        MaxLines = other.MaxLines ?? MaxLines,
        IsTitleBar = other.IsTitleBar ?? IsTitleBar,
        RequestedTheme = other.RequestedTheme ?? RequestedTheme,
        OwnsTitleBar = other.OwnsTitleBar ?? OwnsTitleBar,
        Grid = other.Grid ?? Grid,
        ContextValues = other.ContextValues ?? ContextValues,
    };
}

/// <summary>组件元素：描述一个子组件的类型与 props，实例由 Reconciler 创建并维护。</summary>
/// <remarks>
/// 构造参数与属性都要标 <see cref="DynamicallyAccessedMembersAttribute"/>：
/// <c>Reconciler</c> 读 <see cref="ComponentType"/> 后用 <c>Activator.CreateInstance</c>
/// 造组件实例，AOT/裁剪下只标一处不够——值是从参数流进字段的，ILC 会在赋值那步报
/// IL2069；而读取走的是属性，属性没标注则实参不满足形参要求（IL2072）。
/// 类型一旦被裁掉无参构造函数，运行时才炸，构建期看不出来。
///
/// 这里刻意不用 record 的位置参数写法：<c>record ComponentElement(Type X, ...)</c>
/// 的位置参数上 <c>[property: ...]</c> 是非法位置（编译器直接忽略整块，
/// 报 "'property' is not a valid attribute location"），只剩 <c>[param:]</c>，
/// 属性那条链路就断在编译器的静默忽略里。写成显式属性才能两处都标上。
/// </remarks>
public record ComponentElement : Element
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ComponentType { get; init; }

    public object? Props { get; init; }

    public ComponentElement(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type componentType,
        object? props = null)
    {
        ComponentType = componentType;
        Props = props;
    }
}

/// <summary>强类型组件元素：允许通过 record with 语法修改 props。</summary>
public sealed record ComponentElement<TProps> : ComponentElement
{
    public new TProps Props
    {
        get => (TProps)base.Props!;
        init => base.Props = value;
    }

    public ComponentElement(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type componentType,
        TProps props)
        : base(componentType, props)
    {
    }
}
