using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
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

    /// <summary>
    /// 任意内容的按钮（XAML 里 <c>&lt;Button&gt;&lt;StackPanel&gt;…&lt;/StackPanel&gt;&lt;/Button&gt;</c>
    /// 那一种）。带图标的按钮走这个重载。
    /// </summary>
    public static ButtonElement Button(Element content, Action? onClick = null) =>
        new(string.Empty, onClick) { Content = content };

    public static StackElement VStack(params Element?[] children) =>
        new(Orientation.Vertical, FilterChildren(children));

    public static StackElement VStack(double spacing, params Element?[] children) =>
        new(Orientation.Vertical, FilterChildren(children)) { Spacing = spacing };

    public static StackElement HStack(params Element?[] children) =>
        new(Orientation.Horizontal, FilterChildren(children));

    public static StackElement HStack(double spacing, params Element?[] children) =>
        new(Orientation.Horizontal, FilterChildren(children)) { Spacing = spacing };

    /// <param name="isIconVisible">左侧等级图标显不显示（默认显示）。</param>
    /// <param name="title">标题（消息上方那行加粗的短句），不给就是不显示。</param>
    /// <param name="isClosable">能不能被用户关掉（右上角 ×），官方默认 <c>false</c>。</param>
    /// <param name="isOpen">
    /// 初始开合。<b>种子值</b>：只在挂载时写一次，之后归控件自己——
    /// 用户关掉之后不会被下一轮重渲染重新打开。
    /// </param>
    public static InfoBarElement InfoBar(
        string message,
        MuxControls.InfoBarSeverity severity = MuxControls.InfoBarSeverity.Informational,
        bool isIconVisible = true,
        string? title = null,
        bool isClosable = false,
        bool isOpen = true) =>
        new(message, severity)
        {
            IsIconVisible = isIconVisible,
            Title = title,
            IsClosable = isClosable,
            IsOpen = isOpen,
        };

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
        string? header = null,
        bool? acceptsReturn = null,
        bool? isSpellCheckEnabled = null,
        int? maxLength = null,
        bool? isReadOnly = null) =>
        new(value, onChanged, placeholderText)
        {
            Header = header,
            AcceptsReturn = acceptsReturn,
            IsSpellCheckEnabled = isSpellCheckEnabled,
            MaxLength = maxLength,
            IsReadOnly = isReadOnly,
        };

    public static CheckBoxElement CheckBox(
        Optional<bool?> isChecked = default,
        Action<bool>? onIsCheckedChanged = null,
        string? label = null) =>
        new(isChecked, onIsCheckedChanged, label);

    public static SliderElement Slider(
        Optional<double> value = default,
        double min = 0,
        double max = 100,
        Action<double>? onValueChanged = null,
        string? header = null,
        Orientation orientation = Orientation.Horizontal,
        double? stepFrequency = null,
        double? tickFrequency = null,
        TickPlacement? tickPlacement = null,
        SliderSnapsTo? snapsTo = null,
        bool? isThumbToolTipEnabled = null,
        bool isDirectionReversed = false) =>
        new(value, min, max, onValueChanged)
        {
            Header = header,
            Orientation = orientation,
            StepFrequency = stepFrequency,
            TickFrequency = tickFrequency,
            TickPlacement = tickPlacement,
            SnapsTo = snapsTo,
            IsThumbToolTipEnabled = isThumbToolTipEnabled,
            IsDirectionReversed = isDirectionReversed,
        };

    // 默认值必须与 ScrollViewerElement 一致（横向 Disabled）：工厂与元素两处
    // 默认值不一致会让"默认构造"的行为随调用点漂移——曾经这里默认 Auto，
    // 于是页面型滚动容器又退化成用无限宽测量内容，卡片宽度随最宽子项漂移。
    public static ScrollViewerElement ScrollViewer(
        Element? child = null,
        ScrollBarVisibility horizontalScrollBar = ScrollBarVisibility.Disabled,
        ScrollBarVisibility verticalScrollBar = ScrollBarVisibility.Auto,
        ScrollMode horizontalScroll = ScrollMode.Enabled,
        ScrollMode verticalScroll = ScrollMode.Enabled,
        HorizontalAlignment horizontalContent = HorizontalAlignment.Left,
        ZoomMode zoom = ZoomMode.Disabled) =>
        new(child, horizontalScrollBar, verticalScrollBar, horizontalScroll, verticalScroll, horizontalContent, zoom);

    // ── 组合子（对齐官方 Dsl） ─────────────────────────────────

    /// <summary>不渲染任何内容的哨兵元素。</summary>
    public static EmptyElement Empty() => EmptyElement.Instance;

    /// <summary>
    /// 一组子元素。<b>它有两种落法，取决于它被用在哪个位置</b>——这一点必须读清楚，
    /// 否则会写出"看着对、跑起来全叠在一起"的界面。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>作为一整棵树的根 / 直接返回时</b>：渲染成一层<b>裸 Grid</b>，
    /// 所有子元素落在同一个格子 (0,0) 上<b>互相覆盖</b>。这正是 MainPage 那块
    /// 自绘标题栏压在内容区上面要的效果。
    /// </para>
    /// <para>
    /// <b>作为别人的子项传进另一个容器时</b>：会被 <c>FilterChildren</c>
    /// <b>摊平</b>——它那几个子元素直接变成父容器的子元素，不再多套一层 Grid。
    /// <see cref="ForEach{T}(IEnumerable{T}, Func{T, Element?})"/> 正是靠这条
    /// 把 N 个映射结果变成父容器的 N 个子项（列表的 N 项），而不是"一个装着
    /// N 个互相重叠子元素的 Grid"。
    /// </para>
    /// <para>
    /// 摊平之前 <c>VStack(20, ForEach(cats, Section))</c> 拿到的是 1 个 Grid，
    /// 里面 N 个分区全叠在 (0,0)；<c>GridView(ForEach(items, Card))</c> 更是只有
    /// <b>1 项</b>——选中下标永远是 0。这两种都是寂静的错误：编译得过、
    /// 也不抛异常，只是界面不对。
    /// </para>
    /// </remarks>
    public static GroupElement Group(params Element?[] children) =>
        new(FilterChildren(children));

    /// <summary>条件成立才渲染，否则返回 Empty。</summary>
    public static Element When(bool condition, Func<Element> then) =>
        condition ? then() : EmptyElement.Instance;

    /// <summary>条件渲染，可带 else 分支。</summary>
    public static Element If(bool condition, Func<Element> then, Func<Element>? otherwise = null) =>
        condition ? then() : (otherwise?.Invoke() ?? EmptyElement.Instance);

    /// <summary>
    /// 把集合映射成一组子元素。<b>传进容器后会被摊平成 N 个子项</b>
    /// （见 <see cref="Group"/> 的说明：这一步不做，`VStack` 里会互相覆盖、
    /// 列表里会只剩 1 项）。
    /// </summary>
    public static GroupElement ForEach<T>(IEnumerable<T> items, Func<T, Element?> render) =>
        new(FilterChildren(items.Select(render).ToArray()));

    /// <summary>把集合映射成一组子元素（带索引）。同样会被摊平。</summary>
    public static GroupElement ForEach<T>(IEnumerable<T> items, Func<T, int, Element?> render) =>
        new(FilterChildren(items.Select(render).ToArray()));

    // ── Thickness 助手（对齐官方 Factories.Thick） ─────────────

    public static Thickness Thick(double uniform) => new(uniform);

    public static Thickness Thick(double horizontal, double vertical) =>
        new(horizontal, vertical, horizontal, vertical);

    public static Thickness Thick(double left, double top, double right, double bottom) =>
        new(left, top, right, bottom);

    /// <summary>
    /// 过滤掉 null 子元素，并把<see cref="GroupElement"/>摊平一层
    /// （官方 FilterChildren 只做前半件事）。
    /// </summary>
    /// <remarks>
    /// <b>为什么要摊平。</b><c>Group</c> 渲染成的是一层裸 Grid：它作为一整棵树的
    /// 根时是"覆盖容器"（MainPage 的自绘标题栏就靠它），但作为<b>子项</b>传进来时
    /// 谁要的都不是"再套一层 Grid"——要的是它那几个子元素本身。
    /// 不摊平的后果是寂静的：<c>VStack(8, ForEach(...))</c> 里 N 个元素全叠在
    /// Grid 的 (0,0)，<c>ListView(ForEach(...))</c> 更是只拿到 <b>1 项</b>，
    /// 选中下标永远是 0。编译得过、不抛异常、只是界面不对——这种错误最难发现，
    /// 所以摊平放在这里（唯一的子项入口），而不是指望每个调用点记得展开。
    /// </remarks>
    private static IReadOnlyList<Element?> FilterChildren(Element?[] children)
    {
        var result = new List<Element?>(children.Length);

        foreach (var child in children)
        {
            switch (child)
            {
                case null:
                    break;

                // 摊平是递归的：Group 套 Group（ForEach 里再 ForEach）也要摊到底，
                // 否则套两层又变回那个重叠的 Grid。
                case GroupElement group:
                    result.AddRange(Flatten(group.Children));
                    break;

                default:
                    result.Add(child);
                    break;
            }
        }

        return result;
    }

    private static IEnumerable<Element?> Flatten(IEnumerable<Element?> children)
    {
        foreach (var child in children)
        {
            switch (child)
            {
                case null:
                    break;

                case GroupElement group:
                    foreach (var inner in Flatten(group.Children))
                    {
                        yield return inner;
                    }

                    break;

                default:
                    yield return child;
                    break;
            }
        }
    }
}
