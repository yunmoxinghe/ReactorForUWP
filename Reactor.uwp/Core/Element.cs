using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
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

    /// <summary>
    /// 提示气泡的<b>内容</b>（对应 <c>ToolTipService.ToolTip</c> 属性元素语法那一档）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ToolTip"/>（字符串）是官方 XAML 的两种写法，不是两个旋钮：
    /// 一个走特性语法 <c>ToolTipService.ToolTip="文本"</c>，一个走属性元素语法
    /// <c>&lt;ToolTipService.ToolTip&gt;…子树…&lt;/ToolTipService.ToolTip&gt;</c>。
    /// 两个都给了以<b>这一条</b>为准（它表达力更强）。详见
    /// <see cref="ToolTipElement"/> 的说明。
    /// </remarks>
    public Element? ToolTipContent { get; init; }

    public BackdropKind? Backdrop { get; init; }

    /// <summary>
    /// 本地化标识，对应 XAML 的 <c>x:Uid</c>：挂载时按 <c>Uid.Property</c>
    /// 查资源文件（<c>Strings/&lt;语言&gt;/Resources.resw</c>）并套到控件上。
    /// </summary>
    /// <remarks>
    /// <c>x:Uid</c> 是<b>编译期</b>指令（XAML 编译器生成
    /// <c>ResourceLoader.GetString("Uid/Text")</c> 的赋值代码），运行时没有
    /// <c>Uid</c> 属性可查，所以纯代码建控件必须自己补这一步——见
    /// <c>Internal/Localization.cs</c> 的说明。
    /// </remarks>
    public string? Uid { get; init; }

    /// <summary>
    /// 投影（XAML 的 <c>UIElement.Shadow</c>）。传 <c>ThemeShadow</c> 拿到
    /// WinUI 2 的主题投影；注意投影要<b>有人接收</b>（把内容放进
    /// <c>ThemeShadow.Receivers</c> 或让元素抬到 Z 轴上），否则看不出效果。
    /// </summary>
    public Shadow? Shadow { get; init; }

    /// <summary>
    /// 该控件的控件级声音策略（XAML 的附加属性 <c>ElementSoundMode</c>）。
    /// 全局开关是 <see cref="Windows.UI.Xaml.ElementSoundPlayer.State"/>，
    /// 这里是<b>单个控件</b>的覆盖：例如某个按钮设
    /// <see cref="Windows.UI.Xaml.ElementSoundMode.Off"/> 就只有它不响。
    /// </summary>
    public ElementSoundMode? ElementSoundMode { get; init; }

    // ── 键盘可达性 ──────────────────────────────────────────────
    //
    // 这一段以前是空的：TabIndex / KeyboardAccelerator / KeyDown / ContextFlyout /
    // AccessKey 一个都没暴露，于是"声明式做出来的 UI 键盘走不通、读屏读不出"——
    // 控件是真的（Windows.UI.Xaml.*），但 XAML 里最基础的那批可达性属性在这套
    // API 上没有对应物。补齐原则是<b>照搬 XAML 的同名属性</b>，不做"等价替代"。

    /// <summary>Tab 顺序（XAML 的 <c>TabIndex</c>）。只在 <c>Control</c> 上有。</summary>
    public int? TabIndex { get; init; }

    /// <summary>是否参与 Tab 导航（XAML 的 <c>IsTabStop</c>）。只在 <c>Control</c> 上有。</summary>
    public bool? IsTabStop { get; init; }

    /// <summary>
    /// 指针交互时是否自动取焦点（XAML 的 <c>AllowFocusOnInteraction</c>）。
    /// 文本框一类控件默认 true，按钮一类默认 false。只在 <c>Control</c> 上有。
    /// </summary>
    public bool? AllowFocusOnInteraction { get; init; }

    /// <summary>
    /// 控件挂载并完成首次 <c>Loaded</c> 之后请求一次焦点。
    /// </summary>
    /// <remarks>
    /// <b>为什么不是挂载时立刻 Focus。</b><c>Focus</c> 要求控件已在可视树里并已
    /// 完成布局；在 mount 阶段调必然返回 false（控件还没挂上去）。所以这里挂在
    /// <c>Loaded</c> 上、只触发一次。等价于 XAML 里在 <c>Loaded</c> 处理器里写
    /// <c>control.Focus(FocusState.Programmatic)</c>。
    /// </remarks>
    public bool? FocusOnMount { get; init; }

    /// <summary>
    /// 焦点令牌：<b>它变了就请求一次焦点</b>（同样走
    /// <c>control.Focus(FocusState.Programmatic)</c>）。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要一个"令牌"而不是一个命令。</b>焦点是控件的<b>状态</b>，不是属性：
    /// XAML 里没有 <c>IsFocused</c> 可写（<c>Focus()</c> 是方法，且返回值取决于
    /// 那一刻控件在不在树里、可见不可见）。声明式描述里能给的只有"我希望此刻
    /// 它在焦点上"，而这个希望每帧都在——如果建成"每帧调一次 Focus"，
    /// 用户点走焦点后下一次重渲染又会被抢回来。
    /// <para>
    /// 所以这里把它做成<b>边沿触发</b>：只有当令牌与上一次处理过的不同时才请求一次。
    /// 想要"再聚焦一次"就把令牌 +1。等价的 XAML 写法是代码后置里那个
    /// <c>searchBox.Focus(FocusState.Programmatic)</c>（WinUI 3 Gallery 的 Ctrl+F
    /// 正是这么做的），这里只是把"什么时候调"搬到声明式这边。
    /// </para>
    /// </remarks>
    public int? FocusToken { get; init; }

    /// <summary>
    /// 访问键（XAML 的 <c>AccessKey</c>，Alt+字符 触发）。<c>UIElement</c> 上就有。
    /// </summary>
    public string? AccessKey { get; init; }

    /// <summary>
    /// 右键/长按弹出的浮出层（XAML 的 <c>ContextFlyout</c>，通常是 <c>MenuFlyout</c>）。
    /// </summary>
    /// <remarks>
    /// 收的是<b>已经造好的原生实例</b>。要声明式地写一份菜单用
    /// <see cref="ContextMenu"/>——两个槽位互斥，都给了以
    /// <see cref="ContextMenu"/> 为准。
    /// </remarks>
    public FlyoutBase? ContextFlyout { get; init; }

    /// <summary>
    /// 右键/长按弹出的<b>声明式菜单</b>（XAML 的
    /// <c>&lt;UIElement.ContextFlyout&gt;&lt;MenuFlyout&gt;…&lt;/MenuFlyout&gt;</c>）。
    /// </summary>
    /// <remarks>
    /// <c>MenuFlyout</c> 不是 <c>UIElement</c>，走不了协调器，所以这里存的是
    /// <b>元素描述</b>（<c>MenuFlyoutElement</c>），由
    /// <c>Internal/InputApplier</c> 就地物化——与
    /// <c>RichTextBlock</c> / <c>Paragraph</c> / <c>Run</c> 同一条规矩。
    /// </remarks>
    public Element? ContextMenu { get; init; }

    /// <summary>
    /// 选中文本时弹出的<b>声明式浮出层</b>（XAML 的
    /// <c>&lt;TextBox.SelectionFlyout&gt;&lt;TextCommandBarFlyout/&gt;</c>）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ContextMenu"/> 是<b>两个不同的槽位</b>：后者是右键 / 长按，
    /// 这里是"选中文字之后"——官方为文本控件专门留的，所以也只有文本控件
    /// （<c>TextBox</c> / <c>RichEditBox</c> / <c>TextBlock</c> / <c>RichTextBlock</c>）
    /// 有这个属性，给别的控件写会留一条痕并被忽略。
    /// <para>
    /// 一般给 <see cref="Microsoft.UI.Reactor.TextCommandBarFlyoutElement"/>：
    /// 剪贴板那几条命令由官方控件自己按选区状态填。
    /// </para>
    /// </remarks>
    public Element? SelectionFlyout { get; init; }

    /// <summary>
    /// 键盘快捷键（XAML 的 <c>&lt;UIElement.KeyboardAccelerators&gt;
    /// &lt;KeyboardAccelerator Key=… Modifiers=…/&gt;</c>）。
    /// </summary>
    /// <remarks>
    /// 声明式描述，由框架建成真的 <see cref="KeyboardAccelerator"/> 并加进控件的
    /// <c>KeyboardAccelerators</c> 集合（不是"自己监听按键然后手动派发"——那属于
    /// 替代实现）。集合按 <c>Key + Modifiers + IsEnabled</c> 做结构比对：
    /// 声明没变就不动它，回调变了只换委托、不重建集合。
    /// </remarks>
    public IReadOnlyList<KeyboardAcceleratorSpec>? KeyboardAccelerators { get; init; }

    /// <summary>按键按下（XAML 的 <c>KeyDown</c> 事件）。</summary>
    public Action<KeyRoutedEventArgs>? OnKeyDown { get; init; }

    /// <summary>按键抬起（XAML 的 <c>KeyUp</c> 事件）。</summary>
    public Action<KeyRoutedEventArgs>? OnKeyUp { get; init; }

    // ── 无障碍 AutomationProperties ─────────────────────────────

    /// <summary>读屏补充描述（<c>AutomationProperties.HelpText</c>）。</summary>
    public string? AutomationHelpText { get; init; }

    /// <summary>完整描述（<c>AutomationProperties.FullDescription</c>）。</summary>
    public string? AutomationFullDescription { get; init; }

    /// <summary>项状态（<c>AutomationProperties.ItemStatus</c>，如"已下载"）。</summary>
    public string? AutomationItemStatus { get; init; }

    /// <summary>项类型（<c>AutomationProperties.ItemType</c>，如"邮件"）。</summary>
    public string? AutomationItemType { get; init; }

    /// <summary>层级（<c>AutomationProperties.Level</c>，1 起）。</summary>
    public int? AutomationLevel { get; init; }

    /// <summary>集合内位置（<c>AutomationProperties.PositionInSet</c>，1 起）。</summary>
    public int? AutomationPositionInSet { get; init; }

    /// <summary>集合总数（<c>AutomationProperties.SizeOfSet</c>）。</summary>
    public int? AutomationSizeOfSet { get; init; }

    /// <summary>实时区域策略（<c>AutomationProperties.LiveSetting</c>）。</summary>
    public AutomationLiveSetting? AutomationLiveSetting { get; init; }

    /// <summary>
    /// 是否对读屏隐藏（<c>AutomationProperties.AccessibilityView</c>）。
    /// 纯装饰性元素应设 <see cref="Peers.AccessibilityView.Raw"/>。
    /// </summary>
    public AccessibilityView? AutomationAccessibilityView { get; init; }

    /// <summary>
    /// 等价 XAML 的 <c>AutomationProperties.AcceleratorKey</c>：只用于<b>告知</b>
    /// 读屏这个控件有哪个快捷键，<b>不会</b>真的注册快捷键（注册用
    /// <see cref="KeyboardAccelerators"/>）。
    /// </summary>
    public string? AutomationAcceleratorKey { get; init; }

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
    /// 超出容器时怎么截断（作用于 TextBlock）。只给 <c>MaxLines</c> 不给它，
    /// 超出的行是<b>被裁掉</b>而不是"…"。
    /// </summary>
    public TextTrimming? TextTrimming { get; init; }

    /// <summary>
    /// 彩色字形（emoji 那类）按彩色画还是按单色画（作用于 TextBlock）。
    /// 默认 <c>true</c>；关掉之后 emoji 会退化成单色轮廓。
    /// </summary>
    public bool? IsColorFontEnabled { get; init; }

    /// <summary>字距，单位 1/1000 em（作用于 TextBlock / Control）。</summary>
    public int? CharacterSpacing { get; init; }

    /// <summary>
    /// Grid 附加位置（行/列/跨行/跨列），由 <c>.Grid(row: …)</c> 写入，
    /// 只有直接挂在 <c>Grid</c> 下的子元素会被应用。
    /// </summary>
    public Microsoft.UI.Reactor.GridAttached? Grid { get; init; }

    /// <summary>
    /// 在 <c>Canvas</c> 里的绝对坐标（<c>Canvas.Left</c> / <c>Top</c> / <c>ZIndex</c>），
    /// 由 <c>.Canvas(left: …)</c> 写入，只有直接挂在 <c>Canvas</c> 下的子元素会被应用。
    /// </summary>
    public Microsoft.UI.Reactor.CanvasAttached? Canvas { get; init; }

    /// <summary>
    /// 在 <c>VariableSizedWrapGrid</c> 里占几格，由 <c>.WrapSpan(rowSpan: …)</c> 写入。
    /// 该面板只认跨格、不认行列号，所以这里没有 Row / Column。
    /// </summary>
    public Microsoft.UI.Reactor.WrapSpanAttached? WrapSpan { get; init; }

    /// <summary>
    /// 在 <c>RelativePanel</c> 里的相对关系，由 <c>.Relative(below: …)</c> 写入。
    /// 兄弟类字段存的是同层子元素的<b>下标</b>（见 <see cref="Microsoft.UI.Reactor.RelativeAttached"/>）。
    /// </summary>
    public Microsoft.UI.Reactor.RelativeAttached? Relative { get; init; }

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
        ToolTipContent = other.ToolTipContent ?? ToolTipContent,
        Backdrop = other.Backdrop ?? Backdrop,
        Uid = other.Uid ?? Uid,
        Shadow = other.Shadow ?? Shadow,
        ElementSoundMode = other.ElementSoundMode ?? ElementSoundMode,
        TabIndex = other.TabIndex ?? TabIndex,
        IsTabStop = other.IsTabStop ?? IsTabStop,
        AllowFocusOnInteraction = other.AllowFocusOnInteraction ?? AllowFocusOnInteraction,
        FocusOnMount = other.FocusOnMount ?? FocusOnMount,
        FocusToken = other.FocusToken ?? FocusToken,
        AccessKey = other.AccessKey ?? AccessKey,
        ContextFlyout = other.ContextFlyout ?? ContextFlyout,
        ContextMenu = other.ContextMenu ?? ContextMenu,
        SelectionFlyout = other.SelectionFlyout ?? SelectionFlyout,
        KeyboardAccelerators = other.KeyboardAccelerators ?? KeyboardAccelerators,
        OnKeyDown = other.OnKeyDown ?? OnKeyDown,
        OnKeyUp = other.OnKeyUp ?? OnKeyUp,
        AutomationHelpText = other.AutomationHelpText ?? AutomationHelpText,
        AutomationFullDescription = other.AutomationFullDescription ?? AutomationFullDescription,
        AutomationItemStatus = other.AutomationItemStatus ?? AutomationItemStatus,
        AutomationItemType = other.AutomationItemType ?? AutomationItemType,
        AutomationLevel = other.AutomationLevel ?? AutomationLevel,
        AutomationPositionInSet = other.AutomationPositionInSet ?? AutomationPositionInSet,
        AutomationSizeOfSet = other.AutomationSizeOfSet ?? AutomationSizeOfSet,
        AutomationLiveSetting = other.AutomationLiveSetting ?? AutomationLiveSetting,
        AutomationAccessibilityView = other.AutomationAccessibilityView ?? AutomationAccessibilityView,
        AutomationAcceleratorKey = other.AutomationAcceleratorKey ?? AutomationAcceleratorKey,
        StyleKey = other.StyleKey ?? StyleKey,
        TextWrapping = other.TextWrapping ?? TextWrapping,
        FontWeight = other.FontWeight ?? FontWeight,
        TextAlignment = other.TextAlignment ?? TextAlignment,
        MaxLines = other.MaxLines ?? MaxLines,
        TextTrimming = other.TextTrimming ?? TextTrimming,
        IsColorFontEnabled = other.IsColorFontEnabled ?? IsColorFontEnabled,
        CharacterSpacing = other.CharacterSpacing ?? CharacterSpacing,
        IsTitleBar = other.IsTitleBar ?? IsTitleBar,
        RequestedTheme = other.RequestedTheme ?? RequestedTheme,
        OwnsTitleBar = other.OwnsTitleBar ?? OwnsTitleBar,
        Grid = other.Grid ?? Grid,
        Canvas = other.Canvas ?? Canvas,
        WrapSpan = other.WrapSpan ?? WrapSpan,
        Relative = other.Relative ?? Relative,
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
