using System;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  取值与富文本补完（取色器是 WinUI 2 真控件，富文本编辑框是 UWP 原生控件）
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 取色器（对应 WinUI 2 的 <see cref="MuxControls.ColorPicker"/>）：受控 <c>Color</c>。
/// </summary>
/// <remarks>
/// <para>
/// 写 <c>Color</c> 会同步抛 <c>ColorChanged</c>，那一发的作者是我们，靠
/// <c>EchoGuard</c> 认下来——与 <c>DatePicker.Date</c> / <c>Slider.Value</c> 同形。
/// </para>
/// <para>
/// <b>几个"会把颜色夹走"的开关必须罩静默窗。</b>关掉 <c>IsAlphaEnabled</c> 会把
/// 当前颜色的 A 拉到 255；换 <c>ColorSpectrumComponents</c> 会把颜色投影到新的
/// 两轴上。那一发 <c>ColorChanged</c> 抛在<b>旧订阅还挂着</b>的时候，而且我们事先
/// 不知道会被夹成什么——与 <c>DatePicker</c> 的 <c>MinYear</c> / <c>MaxYear</c>
/// 同一个形状，同一套解法（静默窗）。
/// </para>
/// <para>
/// <c>PreviousColor</c> 没有暴露：它是"上一次确认过的颜色"，官方只在展开/收起
/// 那几个时刻自己用，声明式这边给了也没有落点。
/// </para>
/// </remarks>
public sealed record ColorPickerElement : Element
{
    /// <summary>
    /// 当前颜色。<b>受控</b>：给了值才受控，<c>null</c> 就是"不管它"。
    /// </summary>
    public Color? Color { get; init; }

    /// <summary>颜色变了（官方 <c>ColorChanged</c>）。参数是新颜色。</summary>
    public Action<Color>? OnColorChanged { get; init; }

    /// <summary>透明度那一档在不在（官方 <c>IsAlphaEnabled</c>）。关掉会把 A 拉到 255。</summary>
    public bool IsAlphaEnabled { get; init; }

    /// <summary>透明度滑杆在不在（官方 <c>IsAlphaSliderVisible</c>）。</summary>
    public bool IsAlphaSliderVisible { get; init; } = true;

    /// <summary>十六进制输入框在不在（官方 <c>IsHexInputVisible</c>）。</summary>
    public bool IsHexInputVisible { get; init; } = true;

    /// <summary>色相滑杆在不在（官方 <c>IsColorSliderVisible</c>）。</summary>
    public bool IsColorSliderVisible { get; init; } = true;

    /// <summary>那片二维光谱在不在（官方 <c>IsColorSpectrumVisible</c>）。</summary>
    public bool IsColorSpectrumVisible { get; init; } = true;

    /// <summary>光谱的形状（官方 <c>ColorSpectrumShape</c>）：方框还是圆环。</summary>
    public MuxControls.ColorSpectrumShape Shape { get; init; } =
        MuxControls.ColorSpectrumShape.Box;

    /// <summary>
    /// 最下面那个"更多"展开按钮在不在（官方 <c>IsMoreButtonVisible</c>）。
    /// </summary>
    /// <remarks>
    /// 它管的是<b>展开按钮</b>而不是展开内容本身：关掉它，alpha 滑杆与十六进制输入框
    /// 那一片就<b>常驻显示</b>（因为没有按钮可折叠了）——不是"把它们藏起来"。
    /// 想要"永远展开"的效果，关它；想要"永远不展开"，把它连同那几个
    /// <c>...Visible</c> 一起关掉。
    /// </remarks>
    public bool IsMoreButtonVisible { get; init; } = true;

    /// <summary>光谱那两轴是哪两个通道（官方 <c>ColorSpectrumComponents</c>）。</summary>
    public MuxControls.ColorSpectrumComponents Components { get; init; } =
        MuxControls.ColorSpectrumComponents.HueSaturation;
}

/// <summary>
/// 富文本编辑框（对应 UWP 原生的 <c>Windows.UI.Xaml.Controls.RichEditBox</c>）：
/// 带格式的文本编辑。<b>文本只出不进。</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>它官方没有 <c>Text</c> 属性。</b>文本住在 <c>Document</c>
/// （<c>Windows.UI.Text.ITextDocument</c>）里，读写走
/// <c>GetText</c> / <c>SetText</c>——XAML 里也确实写不出
/// <c>&lt;RichEditBox Text="…"/&gt;</c>。也就是说：<b>官方没有给"当前文本"留一个
/// 可写的属性</b>，这与 <c>TextBox</c> 完全不同。
/// </para>
/// <para>
/// <b>为什么不装成受控。</b>受控的前提是"写进去的值"与"回读出来的值"是同一个
/// 东西，否则"值没变"永远判不成立、写回会自激。而 <c>GetText</c> 以 <c>\r</c>
/// 作段落符，末尾那一个是<b>文档结构</b>不是用户输入的文本（写进去 <c>"abc"</c>、
/// 读出来 <c>"abc\r"</c>）。剥掉它只是<b>我们自造的归一化</b>，控件并不认这份约定，
/// 于是"受控"就建立在一条只有本框架知道的规则上——与
/// <see cref="CalendarViewElement"/> 的"选中集合是活集合，没有回执通道能把两份
/// 状态对齐到可判定"是同一条规矩：<b>没有可判定的落点，就不装成受控</b>。
/// </para>
/// <para>
/// 于是 <see cref="InitialText"/> 只在挂载时写一次，之后一律<b>只出不进</b>：
/// 用户在里面打字由 <see cref="OnTextChanged"/> 送出来，反过来<b>不能</b>由 state
/// 驱动控件内容。要整篇换掉就把这块内容换掉重建（或走 <c>Native()</c>）。
/// </para>
/// </remarks>
public sealed record RichEditBoxElement : Element
{
    /// <summary>
    /// <b>初始</b>文本。只在挂载时写一次，之后不再写回（理由见类型注释）。
    /// </summary>
    public string? InitialText { get; init; }

    /// <summary>
    /// 文本变了（官方 <c>TextChanged</c>）。参数是剥掉末尾段落符之后的文本——
    /// 多段文本里<b>中间</b>那些 <c>\r</c> 会保留，它们是真的换行。
    /// </summary>
    public Action<string>? OnTextChanged { get; init; }

    /// <summary>标题（官方 <c>Header</c>）。</summary>
    public string? Header { get; init; }

    /// <summary>占位提示（官方 <c>PlaceholderText</c>）。</summary>
    public string? PlaceholderText { get; init; }

    /// <summary>只读（官方 <c>IsReadOnly</c>）。只读与"不给回调"是两件事：前者仍可选中复制。</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>回车是换行还是"确认"（官方 <c>AcceptsReturn</c>，默认换行）。</summary>
    public bool AcceptsReturn { get; init; } = true;

    /// <summary>要不要拼写检查（官方 <c>IsSpellCheckEnabled</c>，默认开）。</summary>
    public bool IsSpellCheckEnabled { get; init; } = true;
}
