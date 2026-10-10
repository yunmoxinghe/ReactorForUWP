using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Reactor.Core.Tests;

/// <summary>
/// 属性漂移：<b>element 上声明的旋钮</b>，如果只在 <c>Mount</c> 那一刻被读、
/// <c>Update</c> 侧却不认，必须有<b>登记过的理由</b>。
/// </summary>
/// <remarks>
/// <para>
/// 每个 handler 有两张嘴：<c>Mount</c> 造控件并读一遍元素上的所有属性，
/// <c>Update</c> 在新旧两个元素之间把差异落到同一个控件上。这两张嘴描述的是
/// 同一件事的两个时刻，于是天然容易漂移：<c>Mount</c> 里多写一笔、
/// <c>Update</c> 里忘了加，表现不是报错，而是"改了状态但界面不动"。
/// </para>
/// <para>
/// 这类 bug 的形状和 <see cref="SingleSlotTests"/> 守的那个同源：<b>同一件事写了
/// 两份</b>。区别在于那一处在协调器里（两条路径），这一处在每个 handler 内部
/// （两个时刻）。共通的解法也一样：不靠人记，靠登记的意图。
/// </para>
/// <para>
/// <b>判据为什么限定在"element 声明的属性"</b>：早先是按"Mount 里给控件赋了哪些
/// 值"来算的，结果混进来一堆噪声——<c>Content</c> / <c>Child</c> 那种子槽
/// （它们 <c>Update</c> 里有 <c>PatchSingleChild</c> 管着，不是漏）、对象初始化器里
/// 的硬编码值（<c>HorizontalAlignment = HorizontalAlignment.Stretch</c>）、
/// 甚至正则把 <c>reconciler</c> 误认成属性名。真正能承诺给用户的东西只有
/// <b>element record 上那些可读的属性</b>：声明了就是承诺一个旋钮，
/// 只生效一次就等于旋钮是假的。
/// </para>
/// <para>
/// 规则因此很简单：
/// <list type="bullet">
///   <item><description>
///     某个 element 属性只出现在 <c>Mount</c> 方法体里、而类体其余部分
///     （<c>Update</c> 与 <c>ApplyProps</c> 一类 helper）再没读过 →
///     必须在该 handler 的类注释里写一行 <c>// MOUNT-ONLY: 属性名 …</c>，
///     并把理由写进注释正文；
///   </description></item>
///   <item><description>
///     没有理由的登记只是一个被关掉的报警，所以登记必须连着论证一起写。
///   </description></item>
/// </list>
/// </para>
/// </remarks>
internal static class PropertyDriftTests
{
    private static readonly Regex ClassHead = new(@"^\s*(?:internal|public|private) sealed class (\w+Handler)", RegexOptions.Compiled);

    private static readonly Regex MountOnly = new(@"^\s*//\s*MOUNT-ONLY:\s*(.+)$", RegexOptions.Compiled);

    private static readonly Regex RecordHead = new(@"public sealed record (\w+Element)\b", RegexOptions.Compiled);

    private static readonly Regex PublicProp = new(@"public [\w<>?,\. \[\]]+ (\w+) \{ get;", RegexOptions.Compiled);

    /// <summary>元素属性被读（<c>element.X</c> 一类）。</summary>
    private static readonly Regex Read = new(@"\b(?:[\w]*[Ee]lement|\btyped\w*)\.(\w+)\b", RegexOptions.Compiled);

    public static void Run()
    {
        Program.Section("属性漂移 / 只在 Mount 里被读的旋钮必须登记");

        var root = RepoRoot();
        if (root is null)
        {
            Program.Check("找到仓库根目录", false, "没找到同时含 Reactor.uwp 与 tests 的目录");
            return;
        }

        var elements = ElementProps(Path.Combine(root, "Reactor.uwp", "Elements"));
        Program.Check(
            $"读到 element 定义（{elements.Count} 个）",
            elements.Count > 0);

        var dir = Path.Combine(root, "Reactor.uwp", "Internal");
        var files = Directory.GetFiles(dir, "Handlers.*.cs").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Program.Check($"扫到 handler 文件（{files.Length} 个）", files.Length > 0);

        var unregistered = new List<string>();
        var scanned = 0;

        foreach (var path in files)
        {
            foreach (var handler in SplitHandlers(File.ReadAllText(path)))
            {
                var elementName = handler.Name.Substring(0, handler.Name.Length - "Handler".Length) + "Element";
                if (!elements.TryGetValue(elementName, out var props))
                {
                    continue; // 没有对应 element（就地物化的那批）
                }

                var mount = MethodBody(handler.Body, " Mount(");
                if (mount.Length == 0)
                {
                    continue;
                }

                scanned++;

                var readInMount = Read.Matches(mount).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
                var readElsewhere = Read.Matches(handler.Body.Replace(mount, string.Empty))
                    .Select(m => m.Groups[1].Value)
                    .ToHashSet(StringComparer.Ordinal);

                var registered = Register(handler.Header);

                foreach (var prop in props
                             .Where(p => readInMount.Contains(p))
                             .Where(p => !readElsewhere.Contains(p))
                             .Where(p => !registered.Contains(p))
                             .OrderBy(p => p, StringComparer.Ordinal))
                {
                    unregistered.Add($"{elementName}.{prop}");
                }
            }
        }

        Program.Check(
            $"只在 Mount 里被读的旋钮都登记过（扫了 {scanned} 个 handler）",
            unregistered.Count == 0,
            unregistered.Count == 0 ? null : string.Join(", ", unregistered));

        Mutations(Path.Combine(root, "Reactor.uwp", "Internal"));
    }

    /// <summary>扫 element 定义：<c>元素名 → 它声明的那些属性</c>。</summary>
    private static Dictionary<string, HashSet<string>> ElementProps(string dir)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(dir, "*.cs"))
        {
            var text = File.ReadAllText(path);
            var heads = RecordHead.Matches(text);

            foreach (Match head in heads)
            {
                // record 正文：到下一个 record / class 为止
                var next = new Regex(@"\n(?:public sealed record|public sealed class|internal sealed class|public static partial class)")
                    .Match(text, head.Index + head.Length);
                var end = next.Success ? next.Index : text.Length;
                var body = text.Substring(head.Index, end - head.Index);

                var name = head.Groups[1].Value;
                if (!result.TryGetValue(name, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    result[name] = set;
                }

                foreach (Match p in PublicProp.Matches(body))
                {
                    set.Add(p.Groups[1].Value);
                }
            }
        }

        return result;
    }

    /// <summary>把一个 handler 文件按类切成若干块。</summary>
    private static (string Name, string Header, string Body)[] SplitHandlers(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var heads = Enumerable.Range(0, lines.Length)
            .Where(i => ClassHead.IsMatch(lines[i]))
            .ToArray();

        var result = new List<(string, string, string)>();
        for (var k = 0; k < heads.Length; k++)
        {
            var start = heads[k];
            var stop = k + 1 < heads.Length ? heads[k + 1] : lines.Length;

            // 头部含类签名之前那一段 XML 注释与 MOUNT-ONLY 标记。
            var headStart = start;
            while (headStart > 0 &&
                   (lines[headStart - 1].TrimStart().StartsWith("///") || MountOnly.IsMatch(lines[headStart - 1])))
            {
                headStart--;
            }

            var name = ClassHead.Match(lines[start]).Groups[1].Value;
            result.Add((
                name,
                string.Join('\n', lines.Skip(headStart).Take(start - headStart + 1)),
                string.Join('\n', lines.Skip(start).Take(stop - start))));
        }

        return result.ToArray();
    }

    /// <summary>
    /// 抽出 <c>Mount</c> 的正文：按大括号配平，对象初始化器里的收尾括号不会提前截断它。
    /// </summary>
    private static string MethodBody(string body, string signature)
    {
        var lines = body.Split('\n');
        var start = Array.FindIndex(lines, line => line.Contains("protected override") && line.Contains(signature));
        if (start < 0)
        {
            return string.Empty;
        }

        var flat = string.Join('\n', lines.Skip(start));
        var depth = 0;
        for (var i = 0; i < flat.Length; i++)
        {
            if (flat[i] == '{')
            {
                depth++;
            }
            else if (flat[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return flat.Substring(0, i + 1);
                }
            }
        }

        return flat;
    }

    private static HashSet<string> Register(string header) =>
        header.Split('\n')
            .Where(line => MountOnly.IsMatch(line))
            .SelectMany(line => MountOnly.Match(line).Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// line-level 变异反向对照：让登记消失、或造一个新的漂移，这条 invariant 必须报警。
    /// </summary>
    private static void Mutations(string dir)
    {
        var dateTime = Path.Combine(dir, "Handlers.DateTime.cs");
        var editing = Path.Combine(dir, "Handlers.Editing.cs");

        if (!File.Exists(dateTime) || !File.Exists(editing))
        {
            Program.Check("找到用于变异的两个 handler 文件", false);
            return;
        }

        var dtOriginal = File.ReadAllText(dateTime);
        var dtLines = Lines(dtOriginal, out var dtEol);

        // 变异 1：把 CalendarView 那行登记注释掉——那几个旋钮重新变成孤儿，
        // 这条 invariant 必须认出来。
        var marked = Array.FindIndex(dtLines, line => MountOnly.IsMatch(line));
        if (marked < 0)
        {
            Program.Check("找到那行 MOUNT-ONLY 登记", false);
            return;
        }

        RunOne(dateTime, dtLines, dtEol, marked, "// mutated-by-test", "注释掉那行登记");

        // 变异 2：把 RichEditBox 在 Update 里对 Header 的那一笔拿掉。Mount 里照样
        // 写着 element.Header，于是它变成"Mount 有、Update 没有"——最典型的事故现场。
        // （不用 CalendarView 那句 Rebind 做靶子：回调是 record 的位置参数，
        //  不在"声明的旋钮"这个判据的视野里，打它等于打空。）
        var edOriginal = File.ReadAllText(editing);
        var edLines = Lines(edOriginal, out var edEol);
        var classLine = Array.FindIndex(edLines, line => line.Contains("class RichEditBoxHandler"));
        var elsewhere = classLine < 0
            ? -1
            : Array.FindIndex(
                edLines,
                classLine + 1,
                line => line.Contains(
                    "PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);"));

        if (elsewhere >= 0)
        {
            RunOne(editing, edLines, edEol, elsewhere, "        // mutated-by-test", "把某个旋钮从 Update 侧拿掉");
        }
        else
        {
            Program.Check("找得到 RichEditBox.Update 里那次读取（用于变异）", false);
        }

        Program.Check(
            "变异结束后源码已还原",
            File.ReadAllText(dateTime) == dtOriginal && File.ReadAllText(editing) == edOriginal);
    }

    private static string[] Lines(string text, out string eol)
    {
        eol = text.Contains("\r\n") ? "\r\n" : "\n";
        return text.Replace("\r\n", "\n").Split('\n');
    }

    private static void RunOne(
        string path,
        string[] lines,
        string eol,
        int index,
        string replacement,
        string what)
    {
        var backup = lines[index];
        try
        {
            lines[index] = replacement;
            File.WriteAllText(path, string.Join(eol, lines));

            var orphan = CountOrphans(File.ReadAllText(path)) > 0;
            Program.Check($"{what} 之后这条 invariant 必须报警", orphan);
        }
        finally
        {
            lines[index] = backup;
            File.WriteAllText(path, string.Join(eol, lines));
        }
    }

    /// <summary>整份文件里"只在 Mount 被读且未登记"的旋钮数。</summary>
    private static int CountOrphans(string text)
    {
        var count = 0;
        var elements = ElementProps(Path.Combine(RepoRoot() ?? string.Empty, "Reactor.uwp", "Elements"));

        foreach (var handler in SplitHandlers(text))
        {
            var elementName = handler.Name.Substring(0, handler.Name.Length - "Handler".Length) + "Element";
            if (!elements.TryGetValue(elementName, out var props))
            {
                continue;
            }

            var mount = MethodBody(handler.Body, " Mount(");
            if (mount.Length == 0)
            {
                continue;
            }

            var readInMount = Read.Matches(mount).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var readElsewhere = Read.Matches(handler.Body.Replace(mount, string.Empty))
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
            var registered = Register(handler.Header);

            count += props.Count(p =>
                readInMount.Contains(p) && !readElsewhere.Contains(p) && !registered.Contains(p));
        }

        return count;
    }

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Reactor.uwp")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
