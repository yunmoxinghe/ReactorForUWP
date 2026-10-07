using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 富文本的工厂方法（<see cref="Factories"/> 的 partial 延续）。
/// </summary>
public static partial class Factories
{
    /// <summary>富文本容器。<paramref name="blocks"/> 中的每一项成为一个段落。</summary>
    public static RichTextBlockElement RichTextBlock(params Element?[] blocks) =>
        new(FilterChildren(blocks))
        {
            IsMonospaced = false,
        };

    /// <summary>代码块样式的富文本：等宽字体 + 保留原文折行。</summary>
    public static RichTextBlockElement CodeBlock(params Element?[] blocks) =>
        new(FilterChildren(blocks))
        {
            IsMonospaced = true,
            NoTextWrap = true,
        };

    /// <summary>富文本段落。<paramref name="inlines"/> 中的每一项成为段内的一个文本片段。</summary>
    public static RichParagraphElement Paragraph(params Element?[] inlines) =>
        new(FilterChildren(inlines));

    /// <summary>带缩进的富文本段落。</summary>
    public static RichParagraphElement Paragraph(double indent, params Element?[] inlines) =>
        new(FilterChildren(inlines)) { Indent = indent };

    /// <summary>
    /// 一段同样式的文本。
    /// </summary>
    /// <remarks>
    /// <paramref name="foreground"/> 传 <see cref="ThemeResource.Brush(string)"/>
    /// 得到的是主题资源的<b>活引用</b>——切深浅色时颜色跟着走；
    /// 传 <c>new SolidColorBrush(color)</c> 则是写死的颜色。
    /// </remarks>
    public static RichRunElement Run(
        string? text = null,
        Brush? foreground = null,
        bool bold = false,
        bool italic = false)
    {
        RichRunElement run = new(text)
        {
            Bold = bold,
            Italic = italic,
        };

        return foreground is null ? run : run.Foreground(foreground);
    }
}
