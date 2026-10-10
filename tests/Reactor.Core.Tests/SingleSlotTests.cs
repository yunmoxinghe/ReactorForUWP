using System;
using System.IO;
using System.Linq;

namespace Reactor.Core.Tests;

/// <summary>
/// 单子元素容器那条「patch 路径 vs 卸载路径」边界的回归。
/// </summary>
/// <remarks>
/// <para>
/// 事情是这样的：真实 XAML 里"子内容放在哪个属性"没有统一接口
/// （<c>ContentControl.Content</c> / <c>Border.Child</c> / 第三方容器各自不同），
/// 协调器因此持有一张三级查找表。曾经<b>patch 侧三级、卸载侧两级</b>——
/// 卸载少了登记表那一級。
/// </para>
/// <para>
/// 这不是"报错"，是"少做一件事"：登记进来的五个容器（<c>Viewbox</c>、
/// <c>ParallaxView</c>、<c>SettingsExpander</c>、<c>Popup</c>、<c>SplitView</c>）在
/// 被丢弃整棵子树时，槽里的子树不回收。里面的 <c>ComponentNode</c> 永远留在注册表、
/// <c>IsMounted</c> 仍为 true，继续响应状态更新、去 patch 一棵已经离开可视树的树。
/// 静态看着毫无异样，跑起来不抛异常，只表现为"反复切页内存一直涨"。
/// </para>
/// <para>
/// 这一组用例不对全部因果负责，它只钉住<b>判据</b>：
/// <list type="number">
///   <item>两条路径对每一档容器必须给出同一个答案（<see cref="SingleSlotSim"/>）；</item>
///   <item>答案由<b>同一个佑贤</b>给出——源码里只允许存在一份三级顺序，
///        第二份写在任何地方都算漂移（比漏一级更难被发现的那种 bug）；</item>
///   <item>上面两条<b>必须真的在盯东西</b>：用 line-level 变异把它弄坏，
///        它得报警。
/// </list>
/// </para>
/// <para>
/// 第 3 条不是形式主义。第一次补这个洞的方式是"在 <c>Popup</c> 的 handler 里
/// 手写一次递归卸载"——那是补丁，不是修法：明天 <c>Viewbox</c> 的用户撞同一堵墙，
/// 而且那时他已经不知道这堵墙存在。所以这里锁的是<b>单一真源本身</b>，
/// 不是某个控件的行为。
/// </para>
/// </remarks>
internal static class SingleSlotTests
{
    public static void Run()
    {
        Program.Section("单槽容器 / patch 与卸载两条路径的一致性");

        var sim = new SingleSlotSim();

        Program.Check(
            "所有档位：两条路径答案一致（含登记表那一級）",
            sim.Disagreements() == 0,
            sim.Disagreements() == 0 ? null : $"不一致 {sim.Disagreements()} 档");

        Program.Check(
            "登记表接入的容器：patch 进得去的槽，卸载同样进得去",
            SingleSlotSim.Registry.All(kind => sim.PatchReaches(kind) && sim.UnmountReaches(kind)));

        Program.Check(
            "多出来的槽（SplitView.Pane）跟着主槽一起回收",
            !sim.ExtraSlotsLeaked);

        // ── 反向对照：修法关掉之后，上面那几条必须真的是在盯东西 ──────────
        var withoutAccessor = new SingleSlotSim { UnmountConsultsAccessor = false };
        Program.Check(
            "关掉「卸载查登记表」必须漏（不漏 = 这条 invariant 是瞎的）",
            withoutAccessor.Disagreements(SingleSlotSim.Registry) > 0);

        var withoutExtra = new SingleSlotSim { UnmountWalksExtraSlots = false };
        Program.Check(
            "关掉「卸载遍历额外槽」必须漏",
            withoutExtra.ExtraSlotsLeaked);

        // 这条记录的是<b>盲区</b>，不是成绩：两条路同时退化时，一致性判据会放过
        // 它们（两边都没到第三級，看着反而是"一致"的）。正因为有这个盲区，
        // 下面那些源码级 invariant 才是必需的——它们直接锚住"第三級必须存在"。
        var bothDegraded = new SingleSlotSim
        {
            PatchConsultsAccessor = false,
            UnmountConsultsAccessor = false,
        };
        Program.Check(
            "两条路同时退化时一致性判据会放过它们（记录盲区；由源码级 invariant 兜底）",
            bothDegraded.Disagreements() == 0);

        SourceInvariants();
    }

    private static void SourceInvariants()
    {
        Program.Section("单槽容器 / 源码级 invariant（唯一真源）");

        var root = RepoRoot();
        if (root is null)
        {
            Program.Check("找到仓库根目录", false, "没找到同时含 Reactor.uwp 与 tests 的目录");
            return;
        }

        var path = Path.Combine(root, "Reactor.uwp", "Internal", "Reconciler.cs");
        if (!File.Exists(path))
        {
            Program.Check($"找到 {Path.GetFileName(path)}", false, path);
            return;
        }

        var original = File.ReadAllText(path);
        var unmount = StripComments(MethodBody(original, "private void UnmountTree("));
        var patch = StripComments(MethodBody(original, "internal void PatchSingleChild("));

        Program.Check(
            "卸载路径问的是那张表（SingleChildAccessor.TryGetSlot）",
            unmount.Contains("SingleChildAccessor.TryGetSlot"));

        Program.Check(
            "卸载路径会遍历 handler 报出的额外槽（ExtraSlotsOf）",
            unmount.Contains("ExtraSlotsOf"));

        Program.Check(
            "patch 路径问的是同一张表",
            patch.Contains("SingleChildAccessor.TryGetSlot"));

        Program.Check(
            "patch 路径不得自己再写一份三级顺序（写成 switch 就是漂移）",
            !patch.Contains("container is ContentControl") &&
            !patch.Contains("container is Border"));

        // 这里只认 <c>ContentControl</c> 这<b>一个词</b>，不认 <c>Border</c>：
        // <c>UnmountTree</c> 开头那个 <c>native is Border</c> 是「组件包装器」
        // 分支（查 ComponentNode 用），跟"子内容放在哪个属性"毫无关系。
        // 拿 Border 做判据会误报——第一版就误报了一次。
        Program.Check(
            "卸载路径不得自己再写一份三级顺序（ContentControl 这个词只归那张表）",
            !unmount.Contains("ContentControl"));

        Mutations(path, original);
    }

    /// <summary>
    /// line-level 变异反向对照：把<i>真的源码</i>改坏，上面那些 invariant 必须报警。
    /// </summary>
    /// <remarks>
    /// 改真源码这件事只在测试的这一小段时间里发生，并且用 <c>finally</c> 还原、
    /// 结束后再验一次文件回到原样。之所以不走"复制一份到临时目录"的做法：
    /// 本库这几组 contract 一致认定"检查必须真的吃到真文件"，否则路径一改，
    /// 这套 invariant 就悄悄变成了摆设。
    /// </remarks>
    private static void Mutations(string path, string original)
    {
        var eol = original.Contains("\r\n") ? "\r\n" : "\n";
        var lines = original.Replace("\r\n", "\n").Split('\n');

        // 变异 1：把卸载侧那句"问表"换成注释——等于退回"只认 ContentControl /
        // Border"两级 switch 的老写法。
        Mutate(
            path,
            eol,
            lines,
            "抹掉卸载侧那句 TryGetSlot",
            line => line.Contains("SingleChildAccessor.TryGetSlot(native"),
            body => body.Contains("SingleChildAccessor.TryGetSlot"));

        // 变异 2：把额外槽的遍历整个拿掉——SplitView.Pane 又会重新开始漏。
        Mutate(
            path,
            eol,
            lines,
            "抹掉额外槽的遍历",
            line => line.Contains("handler?.ExtraSlotsOf(native)"),
            body => body.Contains("ExtraSlotsOf"));

        // 变异 3：把那张三级顺序"顺手"抄回卸载侧——最容易被未来的改动复制出去的
        // 形态。「不得再写一份」这条必须报警，否则它只是句口号。
        Mutate(
            path,
            eol,
            lines,
            "把三级顺序抄回卸载侧",
            line => line.Contains("var singleChild = handler?.SingleChildOf(element);"),
            body => !body.Contains("ContentControl"),
            replacement:
                "        var revived = native is ContentControl revivedControl ? revivedControl.Content as UIElement : null;");

        Program.Check("变异结束后源码已还原", File.ReadAllText(path) == original);
    }

    /// <summary>
    /// 把命中的那一行注释掉，再问一遍对应判据：它必须<b>消失</b>（也就是报警）。
    /// </summary>
    private static void Mutate(
        string path,
        string eol,
        string[] lines,
        string what,
        Func<string, bool> match,
        Func<string, bool> stillHolds,
        string replacement = "// mutated-by-test")
    {
        var index = Array.FindIndex(lines, line => match(line));
        if (index < 0)
        {
            Program.Check($"{what}：找得到要变异的那一行", false);
            return;
        }

        var backup = lines[index];
        try
        {
            lines[index] = replacement;
            File.WriteAllText(path, string.Join(eol, lines));

            var mutated = StripComments(MethodBody(File.ReadAllText(path), "private void UnmountTree("));
            Program.Check($"{what} 之后那条 invariant 必须报警", !stillHolds(mutated));
        }
        finally
        {
            lines[index] = backup;
            File.WriteAllText(path, string.Join(eol, lines));
        }
    }

    /// <summary>抽出某个方法的整段正文（从签名到它的收尾大括号）。</summary>
    private static string MethodBody(string text, string signature)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.Contains(signature));
        if (start < 0)
        {
            return string.Empty;
        }

        for (var i = start; i < lines.Length; i++)
        {
            if (lines[i] == "    }")
            {
                return string.Join('\n', lines.Skip(start).Take(i - start + 1));
            }
        }

        return string.Join('\n', lines.Skip(start));
    }

    /// <summary>
    /// 去掉注释行。
    /// </summary>
    /// <remarks>
    /// 不加这一步，注释里写一句方法名就能让"这行必须存在"的检查永远通过——
    /// 那种绿色是假的，比没有检查更糟。
    /// </remarks>
    private static string StripComments(string body) =>
        string.Join(
            '\n',
            body.Split('\n').Where(line => !line.TrimStart().StartsWith("//")));

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
