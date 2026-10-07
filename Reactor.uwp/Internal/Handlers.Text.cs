using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 富文本容器：<c>RichTextBlock</c> + <c>Paragraph</c> + <c>Run</c>。
/// </summary>
/// <remarks>
/// <b>这三样都不是 <c>UIElement</c></b>——它们是流文档对象（<c>Block</c> /
/// <c>Inline</c>），由 <c>RichTextBlock</c> 自己做排版。所以它们<b>不走
/// <see cref="Reconciler"/></b>（那条路的输入输出约定是 <c>UIElement</c>），
/// 由本 handler 在 RichTextBlock 内部按位 patch。
/// <para>
/// <b>为什么值得专门做这一层。</b>画廊要展示示例代码，一行里多段异色文本是最基本的
/// 形态。用一串 <c>TextBlock</c> 横向拼做不到三件事：文本跨元素连续可选、
/// 复制出来带换行、可被 UIA 当成一段 <c>Text</c> 而不是一堆碎片
/// （后一条直接影响屏幕阅读器的朗读连贯性）。原生 <c>RichTextBlock</c> 天生就有。
/// </para>
/// </remarks>
internal sealed class RichTextBlockHandler : ElementHandler<RichTextBlockElement, RichTextBlock>
{
    /// <summary>代码块字体。<c>Consolas</c> 排在 <c>Cascadia Mono</c> 之后是刻意的：
    /// 老系统上前者一定在，后者未必。</summary>
    private const string MonoFamily = "Cascadia Mono, Consolas, Courier New";

    /// <summary>每个 <c>RichTextBlock</c> 当前所用的元素描述（避免每帧重排全部文档对象）。</summary>
    private static readonly WeakTable<RichTextBlock, RichTextBlockElement> Docs = new();

    private static readonly WeakTable<Paragraph, RichParagraphElement> Paras = new();
    private static readonly WeakTable<Run, RichRunElement> Runs = new();

    protected override RichTextBlock Mount(Reconciler reconciler, RichTextBlockElement element)
    {
        var native = new RichTextBlock();

        Sync(native, element);
        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        RichTextBlockElement oldElement,
        RichTextBlockElement newElement,
        RichTextBlock control)
    {
        Sync(control, newElement);
    }

    protected override void Unmount(Reconciler reconciler, RichTextBlock control)
    {
        Docs.Remove(control);

        // 两张表按控件建（键是 Paragraph / Run），卸载时不摘就一直挂着到 GC 才松手。
        // 摘除在这里展开写而不是转调 Forget：卸载路径要求"哪张表在哪摘"一眼可见。
        foreach (var block in control.Blocks)
        {
            if (block is not Paragraph paragraph)
            {
                continue;
            }

            Paras.Remove(paragraph);

            foreach (var inline in paragraph.Inlines)
            {
                if (inline is Run run)
                {
                    Runs.Remove(run);
                }
            }
        }
    }

    private static void Forget(Paragraph paragraph)
    {
        Paras.Remove(paragraph);

        foreach (var inline in paragraph.Inlines)
        {
            if (inline is Run run)
            {
                Runs.Remove(run);
            }
        }
    }

    /// <summary>
    /// 把元素描述同步到文档对象树：<b>同一个描述实例 = 一个字节都不碰</b>。
    /// </summary>
    /// <remarks>
    /// 这条判据是本 handler 的性能与观感底线。低于它就得整篇重建
    /// <c>RichTextBlock.Blocks</c>，而重建会丢掉用户正在框选的文本范围——
    /// 代码块存在的意义就是让人复制走，选区被重建冲掉等于这个功能废了一半。
    /// </remarks>
    private static void Sync(RichTextBlock native, RichTextBlockElement element)
    {
        ApplyAppearance(native, element);

        if (Equals(Docs[native], element))
        {
            return;
        }

        Docs[native] = element;
        SyncBlocks(native.Blocks, element.Blocks);
    }

    private static void ApplyAppearance(RichTextBlock native, RichTextBlockElement element)
    {
        if (element.IsMonospaced)
        {
            native.FontFamily = new FontFamily(MonoFamily);
        }

        native.TextWrapping = element.NoTextWrap ? TextWrapping.NoWrap : TextWrapping.Wrap;

        // 代码块必须能选：这是"展示源码"这个功能的一半，另一半是复制。
        // 控件默认就是 true，显式写出来是为了让它不随将来某个默认值改动而漂。
        native.IsTextSelectionEnabled = true;
    }

    private static void SyncBlocks(BlockCollection target, IReadOnlyList<Element?>? blocks)
    {
        var list = blocks ?? Array.Empty<Element?>();

        for (var i = 0; i < list.Count; i++)
        {
            // 每个段落一个 <c>Paragraph</c>：RichTextBlock 会在段之间插一个换行，
            // 于是"源码行"与"视觉行"一一对应，复制出去的行尾干净。
            if (i < target.Count && target[i] is Paragraph existing)
            {
                SyncParagraph(existing, list[i]);
                continue;
            }

            if (BuildParagraph(list[i]) is { } fresh)
            {
                target.Insert(i, fresh);
            }
        }

        while (target.Count > list.Count)
        {
            if (target[target.Count - 1] is Paragraph stale)
            {
                Forget(stale);
            }

            target.RemoveAt(target.Count - 1);
        }
    }

    private static Paragraph? BuildParagraph(Element? element)
    {
        if (element is not RichParagraphElement paragraph)
        {
            return null;
        }

        var native = new Paragraph();
        SyncParagraph(native, paragraph);
        return native;
    }

    private static void SyncParagraph(Paragraph native, Element? element)
    {
        if (element is not RichParagraphElement paragraph)
        {
            return;
        }

        if (Equals(Paras[native], paragraph))
        {
            return;
        }

        Paras[native] = paragraph;

        native.Margin = new Thickness(paragraph.Indent ?? 0, 0, 0, 0);
        SyncInlines(native.Inlines, paragraph.Inlines);
    }

    private static void SyncInlines(InlineCollection target, IReadOnlyList<Element?>? inlines)
    {
        var list = inlines ?? Array.Empty<Element?>();

        for (var i = 0; i < list.Count; i++)
        {
            if (i < target.Count && target[i] is Run existing)
            {
                SyncRun(existing, list[i]);
                continue;
            }

            if (BuildRun(list[i]) is { } fresh)
            {
                target.Insert(i, fresh);
            }
        }

        while (target.Count > list.Count)
        {
            if (target[target.Count - 1] is Run stale)
            {
                Runs.Remove(stale);
            }

            target.RemoveAt(target.Count - 1);
        }
    }

    private static Run? BuildRun(Element? element)
    {
        if (element is not RichRunElement run)
        {
            return null;
        }

        var native = new Run();
        SyncRun(native, run);
        return native;
    }

    private static void SyncRun(Run native, Element? element)
    {
        if (element is not RichRunElement run)
        {
            return;
        }

        if (Equals(Runs[native], run))
        {
            return;
        }

        Runs[native] = run;

        var text = run.Text ?? string.Empty;
        if (native.Text != text)
        {
            native.Text = text;
        }

        // 颜色走 Modifiers 上那份 ElementModifiers.Foreground —— 这是全部元素共用的
        // 那个槽位（<c>.Foreground()</c> 扩展方法写的就是它），不为 Run 另立一套。
        // 注意这些 Run / Paragraph 不是 UIElement，协调器不会替它们套用到界面上，
        // 所以这里要自己读出来落到 <c>TextElement.Foreground</c>。
        var brush = run.Modifiers?.Foreground;
        if (brush is not null && !ReferenceEquals(native.Foreground, brush))
        {
            native.Foreground = brush;
        }

        native.FontWeight = run.Bold ? Windows.UI.Text.FontWeights.SemiBold : Windows.UI.Text.FontWeights.Normal;
        native.FontStyle = run.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
    }
}
