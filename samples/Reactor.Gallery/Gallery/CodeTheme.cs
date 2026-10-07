using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Reactor.Gallery;

/// <summary>
/// 代码块的调色板：<b>每一行颜色都必须随主题变化</b>。
/// </summary>
/// <remarks>
/// 代码块背景是随主题的卡片色，前景若用写死的颜色，切到深色就很容易得到
/// "注释比正文还显眼"或者"浅色字压在浅底上"这类组合，读者看的是代码而不是配色，
/// 任何一边 Fail 都会让这个功能失去意义。
/// <para>
/// 首选 WinUI 2 的主题资源（<see cref="ThemeResource.Brush(string)"/> 给出的是活引用，
/// 切主题由 <c>ThemeResource.RefreshLive</c> 统一刷新）；<b>资源不存在时退回</b>
/// 按当前 <c>RequestedTheme</c> 现算的纯色画笔——宁可是"这一条不跟随主题"，
/// 也不能是"看不见"。资源键在不同 WinUI 版本上并不一致，这层兜底不是多余的谨慎。
/// </para>
/// </remarks>
internal static class CodeTheme
{
    /// <summary>关键字。</summary>
    public static Brush Keyword => Resolve(
        "AccentFillColorDefaultBrush",
        Color.FromArgb(255, 0, 90, 158),
        Color.FromArgb(255, 122, 195, 255));

    /// <summary>字符串字面量。</summary>
    public static Brush String => Resolve(
        "SystemFillColorCriticalBrush",
        Color.FromArgb(255, 196, 43, 28),
        Color.FromArgb(255, 255, 153, 141));

    /// <summary>注释。</summary>
    public static Brush Comment => Resolve(
        "TextFillColorSecondaryBrush",
        Color.FromArgb(255, 96, 96, 96),
        Color.FromArgb(255, 170, 170, 170));

    /// <summary>数字。</summary>
    public static Brush Number => Resolve(
        "TextFillColorSecondaryBrush",
        Color.FromArgb(255, 96, 96, 96),
        Color.FromArgb(255, 200, 200, 200));

    /// <summary>默认前景（正文与标点）。</summary>
    public static Brush Text => Resolve(
        "TextFillColorPrimaryBrush",
        Color.FromArgb(255, 32, 32, 32),
        Color.FromArgb(255, 242, 242, 242));

    private static Brush Resolve(string key, Color light, Color dark)
    {
        // 资源存在 → 活引用，切主题自动跟随。
        if (ThemeResource.Get<Brush>(key) is not null)
        {
            return ThemeResource.Brush(key);
        }

        return IsDark() ? new SolidColorBrush(dark) : new SolidColorBrush(light);
    }

    /// <summary>
    /// 当前 requested theme。<b>读 XAML 那一份，不从设置里另算</b>：
    /// 设置的缺省是"跟随系统"，那时真正决定颜色的是 Application 侧的值。
    /// </summary>
    private static bool IsDark()
    {
        try
        {
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// C# 的极简扫描器：把一行源码切成一块块同类片段。
/// </summary>
/// <remarks>
/// <b>这里做的是"够用的词法"，不是编译器。</b>目标只有一个：让人在代码块里一眼分清
/// 关键字、字符串、注释、数字。为了它去引 Roslyn（或者写一套完整的词法/文法）
/// 会把示例工程的体积与启动代价假设一起带崩，且 AOT 上多一堆不确定性。
/// <para>
/// 只认四类形态：<c>//</c> 行注释、双引号字符串（含转义）、标识符/关键字/数字、
/// 其余标点。不处理逐字字符串里的换行、<c>#region</c>、XML 文档里的尖括号——
/// 这些判错了也只是颜色偏一点，不会让人读错代码。
/// </para>
/// </remarks>
internal static class CSharpScanner
{
    /// <summary>扫描出来的一块。</summary>
    internal readonly record struct Token(string Text, Brush Brush);

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "get", "goto", "if", "implicit", "in", "init", "int", "interface", "internal",
        "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "record", "ref", "return", "sbyte",
        "sealed", "set", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "where", "while",
    };

    /// <summary>把一行切成带画笔的片段（空行返回空表）。</summary>
    public static List<Token> Scan(string line)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < line.Length)
        {
            var ch = line[i];

            // 行注释：整行剩下的都是它。
            if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                tokens.Add(new Token(line.Substring(i), CodeTheme.Comment));
                break;
            }

            // 字符串（含转义）。
            if (ch == '"')
            {
                var start = i;
                i++;

                while (i < line.Length)
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        i += 2;
                        continue;
                    }

                    if (line[i] == '"')
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                tokens.Add(new Token(line.Substring(start, i - start), CodeTheme.String));
                continue;
            }

            // 数字（含小数，够用即可）。
            if (char.IsDigit(ch) || (ch == '.' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
            {
                var start = i;
                i++;

                while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.'))
                {
                    i++;
                }

                tokens.Add(new Token(line.Substring(start, i - start), CodeTheme.Number));
                continue;
            }

            // 标识符 / 关键字。
            if (char.IsLetter(ch) || ch == '_' || ch == '@')
            {
                var start = i;
                i++;

                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                {
                    i++;
                }

                var word = line.Substring(start, i - start);
                tokens.Add(new Token(word, Keywords.Contains(word) ? CodeTheme.Keyword : CodeTheme.Text));
                continue;
            }

            // 其余：空格与标点，默认前景。
            tokens.Add(new Token(line.Substring(i, 1), CodeTheme.Text));
            i++;
        }

        return tokens;
    }
}
