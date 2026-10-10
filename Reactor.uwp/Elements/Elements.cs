using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>文本节点。参数名对齐官方 Reactor（Content，不是 Text）。</summary>
public sealed record TextBlockElement(string Content) : Element;

/// <summary>
/// 按钮节点：点击回调直接以声明方式携带。
/// </summary>
/// <remarks>
/// <b><see cref="Content"/> 与 <see cref="Label"/> 二选一</b>，
/// 对应 XAML 里同一个 <c>Button.Content</c> 的两种写法：
/// <c>&lt;Button Content="文本"/&gt;</c> 与
/// <c>&lt;Button&gt;&lt;StackPanel&gt;…&lt;/StackPanel&gt;&lt;/Button&gt;</c>。
/// 后者是带图标按钮的官方形态（WinUI Gallery 那颗 "Content with icon" 就是这么写的），
/// 因为 <c>Button</c> 本来就是 <c>ContentControl</c>。
/// </remarks>
public sealed record ButtonElement(string Label, Action? OnClick = null) : Element
{
    /// <summary>任意内容（图标 + 文字等）。给了它之后 <see cref="Label"/> 不再生效。</summary>
    public Element? Content { get; init; }
}

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
    MuxControls.InfoBarSeverity Severity = MuxControls.InfoBarSeverity.Informational) : Element
{
    /// <summary>
    /// 左侧那个等级图标显不显示（XAML 的 <c>IsIconVisible</c>，官方默认 <c>true</c>）。
    /// </summary>
    /// <remarks>
    /// 它<b>只管图标</b>：关掉之后文字照常显示，条子不会变窄——图标那一列是
    /// 模板里的固定槽位，隐藏的是内容不是格子。想让消息顶到左边，官方给的
    /// 手段是 <c>InfoBar</c> 上没有的（要改模板），别指望这一个开关。
    /// </remarks>
    public bool IsIconVisible { get; init; } = true;

    /// <summary>
    /// 标题：消息上方那行加粗的短句（XAML 的 <c>Title</c>）。
    /// </summary>
    /// <remarks>
    /// 官方 InfoBar 示例里几乎<b>条条都有</b>标题，本版之前缺这一项，
    /// 所以画廊对不上官方那一排的形态。给 <c>null</c> 就是"不显示标题"
    /// （官方模板里标题那一块是折叠的，不是留白）。
    /// </remarks>
    public string? Title { get; init; }

    /// <summary>
    /// 开合。<b>它是"种子值"，不是受控值</b>：只在挂载那一刻写一次，之后归控件自己。
    /// </summary>
    /// <remarks>
    /// 用户按了关闭按钮之后，<c>IsOpen</c> 会变成 <c>false</c>；这时候下一轮重渲染
    /// <b>不该</b>把它重新打开——那不是"状态同步"，那是把用户的操作撤回。
    /// 因此这里<b>不进 <c>Update</c></b>（见 <c>InfoBarHandler</c> 的注释）。
    /// 默认 <c>true</c>：不给就是"显示出来"。
    /// </remarks>
    public bool IsOpen { get; init; } = true;

    /// <summary>
    /// 用户能不能把它关掉（右上角那个 ×，XAML 的 <c>IsClosable</c>，官方默认 <c>false</c>）。
    /// </summary>
    public bool IsClosable { get; init; }
}

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

    /// <summary>
    /// 回车是"换行"还是"确认"（官方 <c>AcceptsReturn</c>）。
    /// </summary>
    /// <remarks>
    /// <b>它是"多行输入框"这个形态的开关</b>，但只管回车键的语义：想让长文本真的
    /// 折行还得再给 <c>.Wrap()</c>（<c>TextWrapping</c> 修饰器，落点是官方的
    /// <c>TextBox.TextWrapping</c>）。两个一起给才是官方画廊里那个"多行输入框"。
    /// </remarks>
    public bool? AcceptsReturn { get; init; }

    /// <summary>要不要拼写检查（官方 <c>IsSpellCheckEnabled</c>）。默认由控件决定（开）。</summary>
    public bool? IsSpellCheckEnabled { get; init; }

    /// <summary>最多几个字符（官方 <c>MaxLength</c>）。0 = 不限，这也是官方默认值。</summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// 只读（官方 <c>IsReadOnly</c>）。
    /// </summary>
    /// <remarks>
    /// 只读与"不给 <c>OnChanged</c>"是两件事：前者<b>仍可选中、复制</b>，只是改不动；
    /// 后者是完全不管文本。想"展示一段可复制的文本"用这个，别用禁用
    /// （<c>.IsEnabled(false)</c> 会连选中复制一起禁掉）。
    /// </remarks>
    public bool? IsReadOnly { get; init; }
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
    Action<double>? OnValueChanged = null) : Element
{
    /// <summary>标题（官方 <c>Header</c>）。给了才显示——不给时控件不占那一行。</summary>
    public string? Header { get; init; }

    /// <summary>
    /// 横向还是竖向（官方 <c>Orientation</c>，默认横向）。
    /// </summary>
    /// <remarks>
    /// 竖滑时<b>必须给一个高度</b>（<c>.Height(...)</c>）：官方控件在竖向形态下按
    /// 可用高度拉伸，放进 <c>VStack</c> 这种"按内容收缩"的容器里会被压成几条像素，
    /// 症状很像"滑块消失了"。
    /// </remarks>
    public Orientation Orientation { get; init; } = Orientation.Horizontal;

    /// <summary>
    /// 键盘（方向键）与"点轨道"的步长（官方 <c>StepFrequency</c>，来自 <c>RangeBase</c>）。
    /// 拖动<b>不</b>受它限制——那是 <see cref="SnapsTo"/> 的事。
    /// </summary>
    public double? StepFrequency { get; init; }

    /// <summary>每隔多少画一个刻度（官方 <c>TickFrequency</c>）。0 = 不画。</summary>
    public double? TickFrequency { get; init; }

    /// <summary>刻度画在轨道内侧还是外侧（官方 <c>TickPlacement</c>）。</summary>
    public TickPlacement? TickPlacement { get; init; }

    /// <summary>
    /// 拖动时吸附到什么（官方 <c>SnapsTo</c>）。
    /// </summary>
    /// <remarks>
    /// <c>StepValues</c> 按 <see cref="StepFrequency"/> 跳（默认 1），
    /// <c>Ticks</c> 按 <see cref="TickFrequency"/> 画出来的那些刻度跳。
    /// 官方默认是 <c>StepValues</c>，所以"给了刻度但拖动仍然连续"是默认行为，
    /// 要"拖一下跳一格"得把这一项显式设成 <c>Ticks</c>。
    /// </remarks>
    public SliderSnapsTo? SnapsTo { get; init; }

    /// <summary>
    /// 值增大往哪边走（官方 <c>IsDirectionReversed</c>）。
    /// </summary>
    /// <remarks>
    /// 换的是<b>值增大的方向</b>，不是"当前值"：横向滑块默认左小右大，打开它变成
    /// 右小左大（竖向则是下小上大）。<c>Min</c> / <c>Max</c> 与 <c>Value</c>
    /// 本身一个都不动。
    /// </remarks>
    public bool IsDirectionReversed { get; init; }

    /// <summary>拖动时拇指上那个数值气泡要不要出现（官方 <c>IsThumbToolTipEnabled</c>）。</summary>
    public bool? IsThumbToolTipEnabled { get; init; }
}

/// <summary>
/// 滚动容器，对应 <see cref="ScrollViewer"/>。参数名对齐官方（Child）。
/// </summary>
/// <remarks>
/// <b>横向滚动会改变子元素的测量宽度，所以这里默认关掉。</b>
/// 只要横向可滚动（<c>HorizontalScrollBarVisibility</c> 不是 <c>Disabled</c>），
/// ScrollViewer 就用<b>无限宽</b>去测量内容 → 子元素的 <c>Stretch</c> 退化为
/// "按内容收缩"，宽度取决于最宽的子项。
/// 典型症状：设置页里折叠时卡片只有 ~220px，展开 SettingsExpander 后里面的
/// ItemsRepeater 也按无限宽测量，卡片栈瞬间跳到 <c>MaxWidth</c>（1000px）。
/// <para>
/// 默认值对齐原生/XAML：裸写 <c>&lt;ScrollViewer&gt;</c> 拿到的是
/// <c>VerticalScrollBarVisibility=Auto</c> + <c>HorizontalScrollBarVisibility=Disabled</c>。
/// 参考实现 microsoft-ui-reactor 在纵向虚拟列表（<c>LazyStackLifecycle</c>）里也是
/// 按方向显式禁用另一轴：
/// <c>HorizontalScrollBarVisibility = orientation == Horizontal ? Auto : Disabled</c>。
/// 所以"页面型滚动容器"直接 <c>ScrollViewer(...)</c> 即可，需要横向滚动时再显式传
/// <c>horizontalScrollBar: Auto</c>。
/// </para>
/// </remarks>
public sealed record ScrollViewerElement(
    Element? Child = null,
    ScrollBarVisibility HorizontalScrollBar = ScrollBarVisibility.Disabled,
    ScrollBarVisibility VerticalScrollBar = ScrollBarVisibility.Auto,
    ScrollMode HorizontalScroll = ScrollMode.Enabled,
    ScrollMode VerticalScroll = ScrollMode.Enabled,
    /// <summary>
    /// 内容的横向对齐，对应 XAML 的 <c>ScrollViewer.HorizontalContentAlignment</c>
    /// （原生默认 <see cref="HorizontalAlignment.Left"/>）。
    /// 模板的设置页显式写 <c>Stretch</c>：内容块自己用 <c>MaxWidth</c> 限宽，
    /// 由它自己的 <c>HorizontalAlignment=Center</c> 居中，容器侧要放开拉伸。
    /// </summary>
    HorizontalAlignment HorizontalContent = HorizontalAlignment.Left,
    /// <summary>
    /// 能不能用捏合 / Ctrl+滚轮缩放（XAML 的 <c>ZoomMode</c>，官方默认 <c>Disabled</c>）。
    /// </summary>
    /// <remarks>
    /// <b>开 <c>Enabled</c> 之前先想清楚内容是什么</b>：缩放只作用在
    /// <c>Content</c> 这一个子元素上，而缩放后的尺寸由它自己的测量决定——
    /// 内容是"按可用宽度铺开"的面板时，放大只会让它溢出、缩小才会真的变小。
    /// 官方 Gallery 里那一档演示的是缩放<b>图片</b>，不是缩放面板。
    /// </remarks>
    ZoomMode Zoom = ZoomMode.Disabled) : Element;
