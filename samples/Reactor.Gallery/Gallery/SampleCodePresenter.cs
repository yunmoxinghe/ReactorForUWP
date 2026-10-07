using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery;

/// <summary>
/// 源码展示：一个代码块 + 复制按钮（画廊里每个样例下面那一块）。
/// </summary>
/// <remarks>
/// <b>代码不是另抄一份常量，而是从包里读出来的那个源文件</b>
/// （见 <see cref="SourceLoader"/>）：复制出去能直接用，
/// 改了示例这一块跟着变。WinUI 3 Gallery 的 <c>SampleCodePresenter</c> 也是这个做法。
/// <para>
/// 着色由 <see cref="CSharpScanner"/> 切成一块块 <c>Run</c>，交给
/// <see cref="RichTextBlockElement"/> 排。用富文本而不是一串 <c>TextBlock</c>，
/// 换来的三件事在 <c>Samples/RichTextBasic.cs</c> 里有对照说明：
/// 跨元素连续可选、复制出来带换行、UIA 看到的是完整一段而不是一堆碎片。
/// </para>
/// <para>
/// <b>这里没有"编辑"能力</b>：WinUI 3 Gallery 那个 XAML 编辑器不在本示例的对应范围内。
/// "展示 + 复制"已经覆盖了"抄回去用"这条主路径，而做一个能跑 XAML 的实时编辑器
/// 的成本（以及它在 UWP 上能不能跑）完全是另一件事——不做的决定要说出来，
/// 免得被当成漏实现了。
/// </para>
/// </remarks>
public sealed class SampleCodePresenter : Component<CodePresenterProps>
{
    /// <summary>超过这个行数就不再逐行着色：那样会有上千个 Run，布局要卡住。</summary>
    private const int MaxHighlightLines = 400;

    public override Element Render()
    {
        var (copied, setCopied) = UseState(false);
        var path = Props.SourcePath ?? string.Empty;
        var source = UseMemo(() => SourceLoader.Load(path), path);

        // 代码块是纯数据算出来的，用 UseMemo 钉住：否则每一次重渲染（包括
        // 点一下"复制"按钮）都要把整份源码重新切一遍上百个 Run。
        var blocks = UseMemo(() => BuildBlocks(source), source ?? string.Empty);

        return Border(
                VStack(8,
                    HStack(8,
                        TextBlock(Props.Title).Caption().Subtle(),
                        Button(copied ? "已复制" : "复制源码", () =>
                        {
                            Copy(source ?? string.Empty);
                            setCopied(true);
                        })
                            .Disabled(source is null)
                            .AutomationName("复制源码"),
                        When(copied, () => InfoBar("源码已复制到剪贴板").MaxWidth(220))),

                    source is null
                        ? TextBlock($"源码未随包一起构建：{Props.SourcePath}").Wrap()
                        : ScrollViewer(
                            CodeBlock(blocks),
                            horizontalScrollBar: ScrollBarVisibility.Auto,
                            verticalScrollBar: ScrollBarVisibility.Auto)
                            .MaxHeight(420)
                            .Padding(12)
                            .Background(ThemeResource.Brush("CardBackgroundFillColorDefaultBrush"))))
            .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"), 1);
    }

    /// <summary>
    /// 源码 → 富文本段落。<b>每行一个 <c>Paragraph</c></b>：源码的行与视觉的行
    /// 因而一一对应，复制出来的缩进也是干净的。
    /// </summary>
    private static Element[] BuildBlocks(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return Array.Empty<Element>();
        }

        var lines = source.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<Element>(lines.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (i == MaxHighlightLines)
            {
                blocks.Add(Paragraph(
                    Run($"… 后面还有 {lines.Length - MaxHighlightLines} 行，为显示性能只着色前 {MaxHighlightLines} 行")
                        .Foreground(ThemeResource.Brush("TextFillColorSecondaryBrush"))));
                break;
            }

            blocks.Add(Paragraph(RunsOf(lines[i])));
        }

        return blocks.ToArray();
    }

    private static Element[] RunsOf(string line)
    {
        var tokens = CSharpScanner.Scan(line);
        var runs = new Element[tokens.Count];

        for (var i = 0; i < tokens.Count; i++)
        {
            runs[i] = Run(tokens[i].Text).Foreground(tokens[i].Brush);
        }

        return runs.Length == 0 ? new Element[] { Run(" ") } : runs;
    }

    /// <summary>
    /// 复制到剪贴板（<c>Windows.ApplicationModel.DataTransfer</c> 那一条官方路径）。
    /// </summary>
    /// <remarks>
    /// 剪贴板在某些环境（无包标识进程 / 远程序会话剪贴板被禁用）下会抛，
    /// 这里吞掉：复制失败也不影响继续阅读源码。
    /// </remarks>
    private static void Copy(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception)
        {
            // 吞掉：复制不出来不影响阅读。
        }
    }
}
