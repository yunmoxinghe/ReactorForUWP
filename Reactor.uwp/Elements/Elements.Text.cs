using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 富文本容器，对应 <c>RichTextBlock</c>：多个段落、段内多个不同样式的 <c>Run</c>。
/// </summary>
/// <remarks>
/// <b>为什么不用一串 <c>TextBlock</c> 拼。</b>语法高亮这种"一行里多段异色文本"的场景，
/// 用 <c>TextBlock</c> 只能靠多个 <c>TextBlock</c> 横向排列 + 手工折行，文本可选、
/// 可复制、可按 TAB 顺序聚焦这些能力一个都拿不到。而 <c>RichTextBlock</c> 是 UWP
/// 原生为此设计的控件：<c>Paragraph.Inlines</c> 里每个 <c>Run</c> 各自带
/// <c>FontFamily</c> / <c>Foreground</c>，文本流出读者读到的就是一段连续可选择文本。
/// <para>
/// 因此这里做的<b>只是把原生对象树描述成 Element</b>，一个字都没有自己绘制。
/// </para>
/// </remarks>
public sealed record RichTextBlockElement(IReadOnlyList<Element?> Blocks) : Element
{
    /// <summary>代码块样式的富文本：等宽字体 + 原文折行。</summary>
    public bool IsMonospaced { get; init; }

    /// <summary>
    /// 是否关闭自动折行（XAML 的 <c>TextWrapping</c>）。
    /// </summary>
    /// <remarks>
    /// 代码块默认要它：<b>高亮着色是按逻辑 Token 切出来的</b>，一旦在窗口里自动折行，
    /// 读者的"行"和源码的"行"就不再对应，看缩进会看错层级。关掉之后长行由外层
    /// <c>ScrollViewer</c> 横向滚动，与任何编辑器的观感一致。
    /// </remarks>
    public bool NoTextWrap { get; init; }
}

/// <summary>
/// 富文本的一个段落，对应 <c>Paragraph</c>。
/// </summary>
/// <remarks>
/// 代码块每行一个段落：连续的 <c>Run</c> 不会被换行符打扰，粘贴出去的行首缩进也干净。
/// </remarks>
public sealed record RichParagraphElement(IReadOnlyList<Element?> Inlines) : Element
{
    /// <summary>段前间距（XAML 的 <c>Margin</c>，这里只给左边距用于缩进分级）。</summary>
    public double? Indent { get; init; }
}

/// <summary>
/// 富文本的一段同样式文本，对应 <c>Run</c>。
/// </summary>
/// <remarks>
/// 语法高亮的最小单位就是它：一个 <c>Run</c> 一种前景色。
/// <paramref name="Foreground"/> 走
/// <see cref="ThemeResource.Brush(string)"/> 那种<b>活引用</b>画笔，切主题时颜色跟着变。
/// </remarks>
public sealed record RichRunElement(string? Text = null) : Element
{
    /// <summary>是否加粗（XAML 的 <c>FontWeight</c>）。</summary>
    public bool Bold { get; init; }

    /// <summary>是否斜体（XAML 的 <c>FontStyle</c>）。</summary>
    public bool Italic { get; init; }
}
