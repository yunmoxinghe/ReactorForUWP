using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Reactor.Core.Tests;

/// <summary>
/// 示例画廊的<b>索引契约</b>自检：展示的那份源码必须真的存在，
/// 并且与"跑起来的那个组件"对得上。
/// </summary>
/// <remarks>
/// 为什么用源码扫描而不是"跑一遍看有没有报错"：
/// <list type="bullet">
///   <item>画廊是 UWP 应用，控制台测试集跑不起来它（要 XAML 运行时 + 包身份）；</item>
///   <item>而这里要守的恰恰是<b>一条推导规则</b>——
///         <c>SourcePath</c> 由 <c>Component&lt;T&gt;()</c> 的类型名推出来，
///         文件名必须等于类名。这条错了，界面上不会报错，只会显示
///         "源码未随包一起构建"，而且是<b>静默</b>的：一个示例少一块代码块，
///         谁也不会立刻发现。</item>
/// </list>
/// 所以把它做成一条契约：新增示例忘了改文件名 / 类名 / 索引，这里直接红。
/// <para>
/// 与 <c>EchoContractTests</c> 同一路数：<b>用源码当事实来源</b>，
/// 不另建一份需要手工同步的清单。
/// </para>
/// </remarks>
internal static class GalleryIndexTests
{
    private static readonly Regex ClassName = new(@"public sealed class (?<name>\w+)", RegexOptions.Compiled);

    private static readonly Regex ItemId = new(@"new GalleryItem\(\s*""(?<id>[^""]+)""", RegexOptions.Compiled);

    private static readonly Regex CategoryId = new(@"new GalleryCategory\(\s*""(?<id>[^""]+)""", RegexOptions.Compiled);

    private static readonly Regex ComponentUse = new(@"Component<(?<type>\w+)>", RegexOptions.Compiled);

    private static readonly Regex SourcePath = new(@"""(?<path>[A-Za-z][\w/]*\.cs)""", RegexOptions.Compiled);

    /// <summary>字符串字面量：扫"代码里引用了什么"之前先抹掉，免得把文案当代码读。</summary>
    private static readonly Regex Strings = new(@"""[^""\r\n]*""", RegexOptions.Compiled);

    /// <summary>ApiMap 里的一条登记：<c>["id"] = new(</c>。</summary>
    private static readonly Regex ApiEntry = new(@"\[""(?<id>[^""]+)""\] = new\(", RegexOptions.Compiled);

    /// <summary>官方类型全名（<c>Windows.UI.*</c> / <c>Microsoft.UI.*</c>）。</summary>
    private static readonly Regex ApiType = new(@"""(Windows|Microsoft)\.UI\.[^""]+""", RegexOptions.Compiled);

    /// <summary>文档链接走前缀常量：<c>Uwp + "textblock"</c> / <c>Mux + "infobar"</c>。</summary>
    /// <summary>
    /// 文档链接走前缀常量：<c>Uwp + "textblock"</c> / <c>Mux + "infobar"</c>
    /// / <c>Shapes + "shape"</c>。
    /// </summary>
    /// <remarks>
    /// <c>Shapes</c> 是第三个前缀：形状住在 <c>Windows.UI.Xaml.Shapes</c> 命名空间，
    /// 套 <c>controls.</c> 前缀会拼出一条不存在的 URL——与其为它开一个手写链接的
    /// 口子，不如多认一个前缀常量，这条契约照旧管得住它。
    /// </remarks>
    private static readonly Regex DocRef = new(@"\b(Uwp|Mux|Shapes) \+ """, RegexOptions.Compiled);

    public static void Run()
    {
        Program.Section("示例索引（展示的源码必须真的存在）");

        var root = RepoRoot();
        if (root is null)
        {
            Program.Check("定位仓库根", false, "从测试输出目录往上没找到 ReacrorForUWP.slnx");
            return;
        }

        var gallery = Path.Combine(root, "samples", "Reactor.Gallery");
        var indexPath = Path.Combine(gallery, "Gallery", "SampleIndex.cs");

        if (!File.Exists(indexPath))
        {
            Program.Check("索引文件存在", false, indexPath);
            return;
        }

        var index = File.ReadAllText(indexPath);

        // ── 1. 文件名必须等于类名 ─────────────────────────────────────────
        // SampleIndex 的静态构造按 "Gallery/Samples/<类型名>.cs" 推导 SourcePath，
        // 这条规则的前提就是"文件名 == 类名"。
        var sampleDir = Path.Combine(gallery, "Gallery", "Samples");
        var mismatched = new List<string>();

        foreach (var file in Directory.GetFiles(sampleDir, "*.cs"))
        {
            var expected = Path.GetFileNameWithoutExtension(file);
            var names = ClassName.Matches(File.ReadAllText(file))
                .Select(m => m.Groups["name"].Value)
                .ToArray();

            if (names.Length == 0 || !names.Contains(expected))
            {
                mismatched.Add($"{Path.GetFileName(file)} → 类名为空或不含 {expected}");
            }
        }

        Program.Check(
            "样例文件里都声明了与文件同名的组件类",
            mismatched.Count == 0,
            mismatched.Count == 0 ? null : string.Join("; ", mismatched));

        // ── 2. 索引引用的组件，其文件必须存在 ────────────────────────────
        // 只看代码里的 Component<…>()：描述文本也会写 "Component<TProps> 父子传值"
        // 这种字样，直接扫全文会把类型参数当成组件名报一遍假红。
        var referenced = ComponentUse.Matches(Strings.Replace(index, string.Empty))
            .Select(m => m.Groups["type"].Value)
            .Distinct()
            .ToArray();

        var missing = new List<string>();

        foreach (var type in referenced)
        {
            if (!File.Exists(Path.Combine(sampleDir, type + ".cs")) &&
                !File.Exists(Path.Combine(gallery, "Pages", type + ".cs")))
            {
                missing.Add(type);
            }
        }

        Program.Check(
            "索引引用的每个组件都有对应的源文件",
            missing.Count == 0,
            missing.Count == 0 ? null : string.Join(", ", missing));

        // ── 3. 显式给出的 SourcePath 必须指向真实文件 ────────────────────
        var badPaths = SourcePath.Matches(index)
            .Select(m => m.Groups["path"].Value)
            .Distinct()
            .Where(p => !File.Exists(Path.Combine(gallery, p.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        Program.Check(
            "显式写的源码路径都能在仓库里找到",
            badPaths.Length == 0,
            badPaths.Length == 0 ? null : string.Join(", ", badPaths));

        // ── 4. id 唯一（重复会让详情页永远只打开第一个）───────────────────
        var duplicates = ItemId.Matches(index)
            .Select(m => m.Groups["id"].Value)
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Program.Check(
            "条目 id 没有重复",
            duplicates.Length == 0,
            duplicates.Length == 0 ? null : string.Join(", ", duplicates));

        var dupCategory = CategoryId.Matches(index)
            .Select(m => m.Groups["id"].Value)
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Program.Check(
            "分类 id 没有重复",
            dupCategory.Length == 0,
            dupCategory.Length == 0 ? null : string.Join(", ", dupCategory));

        // ── 5. 没有孤儿样例文件（写了但没进索引 = 界面上永远看不到）───────
        var orphans = Directory.GetFiles(sampleDir, "*.cs")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(name => !referenced.Contains(name))
            .ToArray();

        Program.Check(
            "样例目录下没有没进索引的文件",
            orphans.Length == 0,
            orphans.Length == 0 ? null : string.Join(", ", orphans));

        // ── 6. 官方对应表（ApiMap）不能与索引脱节 ────────────────────────
        //
        // ApiMap 是一份<b>与索引平行</b>的数据（"这个条目背后那个控件在官方叫什么"）。
        // 平行数据最典型的死法就是脱节：索引删了条目、表里的那行还在，
        // 于是界面上永远不可能显示它，也不会有人发现它是死的。
        // 这里把它钉回索引：表里每一个 id 都必须真有那么一个条目。
        //
        // 另外两条守"每行的形状"：至少写一个官方类型名（否则那张卡是空的），
        // 文档链接必须走文件顶部那两个前缀常量（否则会有人手写一条裸 URL，
        // 而"WinUI 2 的文档已并入 WinUI 3 文档站"这个约定就管不住它了）。
        var apiPath = Path.Combine(gallery, "Gallery", "ApiMap.cs");

        if (!File.Exists(apiPath))
        {
            Program.Check("官方对应表存在", false, apiPath);
            return;
        }

        var apiSource = File.ReadAllText(apiPath);
        var heads = ApiEntry.Matches(apiSource);
        var indexIds = ItemId.Matches(index).Select(m => m.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);

        var dead = new List<string>();
        var empty = new List<string>();
        var rawUrl = new List<string>();

        for (var i = 0; i < heads.Count; i++)
        {
            var id = heads[i].Groups["id"].Value;
            var start = heads[i].Index;
            var end = i + 1 < heads.Count ? heads[i + 1].Index : apiSource.Length;
            var body = apiSource[start..end];

            if (!indexIds.Contains(id))
            {
                dead.Add(id);
            }

            if (!ApiType.IsMatch(body))
            {
                empty.Add(id);
            }

            if (!DocRef.IsMatch(body))
            {
                rawUrl.Add(id);
            }
        }

        Program.Check(
            "官方对应表里没有指向不存在条目的死键",
            dead.Count == 0,
            dead.Count == 0 ? null : string.Join(", ", dead));

        Program.Check(
            "官方对应表里每一条都写了至少一个官方类型名",
            empty.Count == 0,
            empty.Count == 0 ? null : string.Join(", ", empty));

        Program.Check(
            "官方对应表的文档链接都走那两个前缀常量",
            rawUrl.Count == 0,
            rawUrl.Count == 0 ? null : string.Join(", ", rawUrl));
    }

    /// <summary>从测试输出目录往上找仓库根（以 slnx 为准）。</summary>
    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ReacrorForUWP.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
