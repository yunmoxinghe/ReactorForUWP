using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 密码框，对应 <see cref="PasswordBox"/>。
/// </summary>
/// <param name="Value">受控密码；<see cref="Optional{T}.Unset"/> = 非受控。</param>
/// <param name="OnChanged">用户输入回调（受控时由它驱动 setState）。</param>
/// <param name="PlaceholderText">占位提示（XAML 的 <c>PlaceholderText</c>）。</param>
/// <remarks>
/// 补这个控件的原因很实在：以前只有 <c>TextBox</c>，要做登录框只能拿它顶替——
/// 那是替代实现（没有密码掩码、没有显示密码按钮、也没有"密码"语义的无障碍角色）。
/// 这里是真的 <see cref="PasswordBox"/>。
/// </remarks>
public sealed record PasswordBoxElement(
    Optional<string> Value = default,
    Action<string>? OnChanged = null,
    string? PlaceholderText = null) : Element
{
    public string? Header { get; init; }

    /// <summary>最大长度（0 = 不限）。</summary>
    public int? MaxLength { get; init; }

    /// <summary>是否显示"显示密码"按钮（XAML 的 <c>IsPasswordRevealButtonEnabled</c>）。</summary>
    public bool? IsPasswordRevealButtonEnabled { get; init; }

    /// <summary>
    /// 掩码字符（XAML 的 <c>PasswordChar</c>），默认官方的圆点 <c>●</c>。
    /// </summary>
    /// <remarks>
    /// <b>只取第一个字符</b>：官方属性是 <c>string</c> 但只用一个字符，给了
    /// 多字符它取头一个。所以传 <c>"*"</c> 得到星号掩码，传 <c>"abc"</c> 得到
    /// 三个 <c>a</c>——不是报错，是"按第一个字符铺满"。
    /// <para>
    /// 它<b>不改</b> <c>Password</c> 的内容：<c>Password</c> 里存的始终是明文，
    /// 掩码只在显示层。这一点和 <c>MaxLength</c> 不一样（后者可能动到已有内容，
    /// 所以那一处开了静默窗）。
    /// </para>
    /// </remarks>
    public string? PasswordChar { get; init; }
}

/// <summary>
/// 带建议的输入框，对应 <see cref="AutoSuggestBox"/>。
/// </summary>
/// <param name="Text">受控文本；<see cref="Optional{T}.Unset"/> = 非受控。</param>
/// <param name="Suggestions">候选列表（字符串）。</param>
/// <remarks>
/// <b>三个回调都是官方事件</b>（<c>TextChanged</c> / <c>QuerySubmitted</c> /
/// <c>SuggestionChosen</c>），只是把事件参数收成了常用的那一个值——
/// 需要 <c>AutoSuggestBoxTextChangedEventArgs.Reason</c>（区分"用户输入"与
/// "程序选了候选"）时用 <c>Native()</c> 逃生舱拿真实控件。
/// </remarks>
public sealed record AutoSuggestBoxElement(
    Optional<string> Text = default,
    IReadOnlyList<string>? Suggestions = null) : Element
{
    public string? PlaceholderText { get; init; }
    public string? Header { get; init; }

    /// <summary>
    /// 点了候选之后要不要把它填进输入框（官方 <c>UpdateTextOnSelect</c>）。
    /// </summary>
    /// <remarks>
    /// 默认 <c>true</c>。关掉它，点候选<b>仍然会抛</b>
    /// <see cref="OnQuerySubmitted"/>（带 <c>ChosenSuggestion</c>），只是框里的字不变——
    /// 适合"输入是筛选条件、候选是跳转目标"那种用法。
    /// </remarks>
    public bool UpdateTextOnSelect { get; init; } = true;

    /// <summary>文本变化（官方 <c>TextChanged</c>）。参数是当前文本。</summary>
    public Action<string>? OnTextChanged { get; init; }

    /// <summary>
    /// 提交查询（官方 <c>QuerySubmitted</c>：回车或点候选）。参数是查询文本。
    /// </summary>
    public Action<string>? OnQuerySubmitted { get; init; }

    /// <summary>
    /// 选中某个候选（官方 <c>SuggestionChosen</c>）。参数是该项（这里是字符串）。
    /// </summary>
    public Action<string>? OnSuggestionChosen { get; init; }

    /// <summary>
    /// 框里那个搜索图标（XAML 的 <c>QueryIcon</c>）。收 <c>FontIcon</c> /
    /// <c>BitmapIcon</c> / <c>SymbolIcon</c>。
    /// </summary>
    /// <remarks>
    /// 官方这个属性的类型是 <c>IconElement</c>（<b>不是</b> <c>IconSource</c>）——
    /// 与 <c>InfoBadge.IconSource</c>、<c>TabViewItem.IconSource</c> 那两处不一样，
    /// 它是真的可视元素，所以这里不再做"元素 → Source"的翻译，直接物化。
    /// <para>
    /// 它是<b>内容槽</b>而不是值：元素每帧都是新的，按"引用变了才换"逐帧比较会
    /// 每帧换一次实例（与图标那一族的既定写法一致，见 <c>Handlers.Icons.cs</c>）。
    /// </para>
    /// </remarks>
    public Element? QueryIcon { get; init; }
}

/// <summary>
/// 数字输入框，对应 WinUI 2 的 <see cref="MuxControls.NumberBox"/>。
/// </summary>
/// <param name="Value">受控数值；<see cref="Optional{T}.Unset"/> = 非受控。</param>
/// <param name="OnValueChanged">数值变化回调（官方 <c>ValueChanged</c>）。</param>
/// <remarks>
/// <b>空值就是 <see cref="double.NaN"/></b>：NumberBox 的 <c>Value</c> 是 double，
/// 没有"未设置"这一档，官方用 NaN 表示空（<c>NumberBoxValueChangedEventArgs</c>
/// 里新值为 NaN 即用户清空）。这里原样映射，不做 "double?" 包装——
/// 包装会在每次读写时引入一次"到底是 null 还是 NaN"的转换歧义。
/// </remarks>
public sealed record NumberBoxElement(
    Optional<double> Value = default,
    Action<double>? OnValueChanged = null) : Element
{
    public string? Header { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }

    /// <summary>步进步长（上下按钮 / 方向键）。</summary>
    public double? SmallChange { get; init; }

    /// <summary>大幅步长（PageUp / PageDown）。</summary>
    public double? LargeChange { get; init; }

    /// <summary>加减按钮的显示方式（XAML 的 <c>SpinButtonPlacementMode</c>）。</summary>
    public MuxControls.NumberBoxSpinButtonPlacementMode? SpinButtonPlacementMode { get; init; }

    /// <summary>到边界后是否回绕（XAML 的 <c>IsWrapEnabled</c>）。</summary>
    public bool? IsWrapEnabled { get; init; }

    /// <summary>小数位数（<c>NumberFormatter</c> 的简化入口，null = 不限）。</summary>
    public int? DecimalPlaces { get; init; }

    /// <summary>
    /// 接受表达式（官方 <c>AcceptsExpression</c>）：输入 <c>1+2*3</c> 这类算式，
    /// 失焦时算出结果填回去。
    /// </summary>
    /// <remarks>
    /// 它是"官方替你算"，不是"本框架帮你 parse"：算式由 WinUI 的
    /// <c>NumberBox</c> 自己求值（走的是它内部那套计算器），算不出来时按
    /// <see cref="ValidationMode"/> 处置。这条属性开了之后，<c>ValueChanged</c>
    /// 回调里拿到的<b>已经是算完的数值</b>。
    /// </remarks>
    public bool? AcceptsExpression { get; init; }

    /// <summary>
    /// 输入不合法时怎么办（官方 <c>ValidationMode</c>）。
    /// </summary>
    /// <remarks>
    /// <c>InvalidInputOverwritten</c>（默认）：越界 / 非法的值被改写成边界值或
    /// <c>NaN</c>；<c>Disabled</c>：什么都不做，<c>Value</c> 保持原样、
    /// 也不回调——于是"输入了 999 但界面还是 100"会真的发生，这是官方给的
    /// 那一档，不是 bug。
    /// </remarks>
    public MuxControls.NumberBoxValidationMode? ValidationMode { get; init; }
}
