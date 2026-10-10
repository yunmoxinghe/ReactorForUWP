using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Reactor.Core.Tests;

/// <summary>
/// 「订阅 ⇔ 解绑」必须成对的源码级哨兵。
/// </summary>
/// <remarks>
/// <b>它守的那个 bug。</b>受控选中类 handler 靠
/// <c>if (!Callbacks.ContainsKey(control))</c> 判断"这个控件的事件挂过没有"。
/// 而 <c>Unmount</c> 会 <c>Callbacks.Remove(control)</c>（按控件建的表必须在
/// Unmount 摘掉，否则泄漏——这条契约由 <c>HandlerTableTests</c> 守着）。
/// <b>于是键没了、订阅还在</b>：同一个原生控件一旦被<b>重新挂载</b>
/// （复用控件、条件分支换 element 都走这条路），守卫判定"没挂过"，就<b>再挂一次</b>。
/// 此后每一发事件都跑两遍 <c>Dispatch</c>：日志成对、纠正成对、<b>用户回调也成对</b>——
/// 真机 <c>probe-realclick-0302.log</c> 里 <c>RadioButtons#5</c> 那两条
/// <c>→ 用户回调 SelectedIndex=1</c> 就是它：用户点了一次，回调响了两次。
/// <para>
/// 修法是让"摘键"与"解绑"成对发生：委托按控件留一份（<c>Handlers</c> 弱表），
/// <c>Unmount</c> 拿它真的 <c>-=</c>，于是重新挂载时挂上去的永远是<b>唯一</b>那一份。
/// </para>
///
/// <b>为什么用源码扫描而不是仿真。</b>这个病的成因不在"判据算错了"，
/// 而在"<b>两件事的记账不一致</b>"：表键的生命周期 与 事件订阅的生命周期 分了家。
/// 仿真（<c>ControlledSelectionSim</c>）里根本没有"订阅"这个概念，
/// 所以它跑一万条序列也是绿的——实测正是如此：加了
/// <c>DoubleCallbackTests</c> 之后 4000 条随机序列<b>一条都没红</b>。
/// 这类"记账成对"的契约只能锚在真源码上。
///
/// <b>判据就是计数。</b>每个 <c>+=</c> 都要有对应的 <c>-=</c>，数量必须相等；
/// 且不得残留上一版修法的痕迹（<c>Detached</c> 那种"留键 + 打标记"的形状，
/// 它违反"按控件建的表必须在 Unmount 摘掉"那条契约，已被这里挡回去过一次）。
/// </remarks>
internal static class DetachedContractTests
{
    private static readonly string[] Files =
    {
        // Elements.cs / Handlers.Basic.cs 之外的每个 handler 都在这一步成型：
        // 名单按"有订阅就有解绑"的实际覆盖走，加文件不用记得登记旁边那张名单
        // ——sender 那道关已经是全目录扫了，这道先跟着补上 Tree / Template：
        // 它们原本被名单漏掉，于是"Unmount 只摘键不解绑"的形状各活了一份。
        Path.Combine("Reactor.uwp", "Internal", "Handlers.Controls.cs"),
        Path.Combine("Reactor.uwp", "Internal", "Handlers.Selectors.cs"),
        Path.Combine("Reactor.uwp", "Internal", "Handlers.Pivot.cs"),
        Path.Combine("Reactor.uwp", "Internal", "Handlers.Tabs.cs"),
        Path.Combine("Reactor.uwp", "Internal", "Handlers.Template.cs"),
        Path.Combine("Reactor.uwp", "Internal", "Handlers.Tree.cs"),
    };

    /// <summary>受控选中类 handler 订阅的事件（漏一个就等于放跑一处重复订阅）。</summary>
    private static readonly string[] Events =
    {
        "SelectionChanged",
        "ItemClick",
        "ItemInvoked",
        "BackRequested",
        "AddTabButtonClick",
        // Tree / Template 那两处复发之后补进来的：它们就是被这名单漏掉的。
        // 每加一个名字，等于把"这个类别会在 Unmount 里被 -= "写进契约——
        // 只管增加，别删已有的（删掉的名字等于重新放行）。
        "ItemClicked",
        "Click",
        "Expanding",
        "Collapsed",
        // ToggleSwitch.Toggled 与 RadioButton 的 Checked / Unchecked 是
        // 第三次复发：同样是"闭包查表、Unmount 只摘键"，只是事件名不在名单上。
        "Toggled",
        "Checked",
        "Unchecked",
    };

    public static void Run()
    {
        Program.Section("受控选中 / 订阅与解绑必须成对（源码级）");

        var root = RepoRoot();
        if (root is null)
        {
            Program.Check("找到仓库根目录", false, "没找到同时含 Reactor.uwp 与 tests 的目录");
            return;
        }

        foreach (var relative in Files)
        {
            var path = Path.Combine(root, relative);
            if (!File.Exists(path))
            {
                Program.Check($"找到 {relative}", false, path);
                continue;
            }

            InvariantsOf(relative, File.ReadAllText(path));
        }

        SenderIdentity(root);
        Mutation(root);
    }

    /// <summary>
    /// 事件委托里<b>不许用回调给的 <c>sender</c></b> 去查表或解绑。
    /// </summary>
    /// <remarks>
    /// <b>它守的那个 bug。</b>WinRT 不保证同一原生对象每次都交出同一个托管包装（RCW）：
    /// 事件回调里的 <c>sender</c> 可能是<b>另一个包装</b>，而所有按控件建的表都是
    /// <c>ConditionalWeakTable</c>（<b>引用相等</b>）。拿 sender 查表就查不到，
    /// 委托静默返回——表现为<b>"点了没反应"，而且从此每次都这样</b>。
    /// <para>
    /// 真机取证（<c>tools/uia/</c> 的 DP 探针页）：每次点击后
    /// <c>SelectedIndex</c> 依赖属性<b>确实跟着变了</b>，而
    /// <c>RadioButtons.cpp:376-378</c> 里"设依赖属性"与"抛事件"是同一段代码——
    /// 事件一定抛了，只是订阅这一侧按 sender 没查到键。同一轮日志里控件编号
    /// 从 <c>#1</c> 跳到 <c>#2</c>（<c>CtlId</c> 按引用发号、只增不减），
    /// 就是那个新包装留下的指纹。
    /// </para>
    /// <para>
    /// 修法：委托一律写成 <c>(s, args) =&gt; … control …</c>，用闭包捕获的
    /// <c>control</c> 查表与派发，<c>sender</c> 一概不碰。
    /// </para>
    /// </remarks>
    private static void SenderIdentity(string root)
    {
        Program.Section("事件委托不得用 sender 查表（RCW 身份，源码级）");

        // 扫整个 Internal 目录，而不是手写文件清单。
        // 手写清单是会漏的——Handlers.Tree.cs 就是这么溜过去的：它不在清单里，
        // 于是同一个 RCW 病在它身上活到今天。这次连同 MyPropertyExplorer、Probe
        // 在内，目录里所有 .cs 都进网；以后新增 handler 自动受管，不用记得登记。
        var dir = Path.Combine(root, "Reactor.uwp", "Internal");
        var files = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.cs").OrderBy(p => p, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

        if (files.Length == 0)
        {
            Program.Check("找得到 Internal 目录", false, dir);
            return;
        }

        // 三种形状都要拦：把 sender 强转回控件、对 sender 做类型模式匹配、
        // 以及直接拿 sender / s 去查表。只认强转那一种是 Tree.cs 溜过来的原因。
        var shapes = new[]
        {
            // var x = (SomeControl)s;     ← 强转
            new System.Text.RegularExpressions.Regex(@"\w+\s*=\s*\([^)]*\)\s*(s|sender)\s*;"),
            // s is SomeControl c          ← 类型模式匹配（Tree.cs 用的就是这一种）
            new System.Text.RegularExpressions.Regex(@"\b(s|sender)\s+is\s+[A-Za-z_][\w.]*\s+[a-z]\w*"),
            // Callbacks.TryGetValue(s) / table[sender]
            new System.Text.RegularExpressions.Regex(@"TryGetValue\(\s*(s|sender)\b|\[\s*(s|sender)\s*\]"),
        };

        var offendersByFile = new List<string>();
        foreach (var path in files)
        {
            var lines = CodeLines(File.ReadAllText(path));
            var offenders = lines.Where(l => shapes.Any(r => r.IsMatch(l))).ToList();
            if (offenders.Count > 0)
            {
                offendersByFile.Add($"{Path.GetFileName(path)}: {string.Join(" | ", offenders)}");
            }
        }

        Program.Check(
            $"Internal 全目录（{files.Length} 个文件）：不得拿 sender 查表或把它转回控件",
            offendersByFile.Count == 0,
            offendersByFile.Count == 0 ? "干净" : string.Join("  ‖  ", offendersByFile));

        // 变异验证：三种形状各注入一处，都必须被抓到——
        // 只验一种会让另外两种继续瞎着，这正是上一版的问题。
        var probe = files[0];
        var original = File.ReadAllText(probe);
        const string variants =
            "\n// 变异：又把 sender 转回控件\nvar probe0 = (MuxControls.RadioButtons)s;\n" +
            "\n// 变异：对 sender 做类型模式匹配\nvar probe1 = s is MuxControls.RadioButtons r ? 1 : 0;\n" +
            "\n// 变异：直接拿 s 查表\nvar probe2 = Callbacks.TryGetValue(s, out _);\n";
        try
        {
            var mutated = CodeLines(original + variants);
            foreach (var (shape, i) in shapes.Select((r, i) => (r, i)))
            {
                Program.Check(
                    $"变异 {i}：注入一处 sender 用法之后 invariant 必须报警（否则这条契约是瞎的）",
                    mutated.Exists(l => shape.IsMatch(l)));
            }
        }
        finally
        {
            File.WriteAllText(probe, original);
        }

        Program.Check(
            "变异结束后源码已还原",
            File.ReadAllText(probe) == original);
    }

    private static void InvariantsOf(string name, string text)
    {
        var lines = CodeLines(text);

        var subscribes = Events.Sum(e => lines.Count(l => l.Contains($"{e} +=")));
        var detaches = Events.Sum(e => lines.Count(l => l.Contains($"{e} -=")));

        Program.Check(
            $"{name}：每个订阅都有对应的解绑（+= {subscribes} == -= {detaches}）",
            subscribes > 0 && detaches >= subscribes,
            $"订阅 {subscribes}，解绑 {detaches}");

        // 上一版修法（"留键 + Detached 标记"）的残留：它违反"按控件建的表必须在
        // Unmount 里摘掉"那条契约，被 HandlerTableTests 挡回来了。留着就是复发。
        Program.Check(
            $"{name}：不得残留 Detached 那种「留键不解绑」的形状",
            !lines.Any(l => l.Contains("Detached")),
            "仍有 Detached 残留");

        // 委托必须真的被 -= 掉，而不是只存着不用。
        Program.Check(
            $"{name}：解绑用的是存下来的委托（Handlers.TryGetValue）",
            lines.Any(l => l.Contains("Handlers.TryGetValue")));
    }

    /// <summary>
    /// 变异验证：把一处解绑抹掉，上面那条 invariant <b>必须</b>报警。
    /// </summary>
    /// <remarks>
    /// 不验这一条，计数判据可能只是"恰好数对了"，而真正的解绑被后人删掉时它照样绿
    /// ——那就是量具失真，这个项目已经栽过五次。
    /// </remarks>
    private static void Mutation(string root)
    {
        var path = Path.Combine(root, Files[0]);
        var original = File.ReadAllText(path);
        const string needle = "control.SelectionChanged -= selection;";

        if (!original.Contains(needle, StringComparison.Ordinal))
        {
            Program.Check("变异：找得到可抹的解绑", false, needle);
            return;
        }

        try
        {
            File.WriteAllText(path, original.Replace(needle, "// 变异：解绑被抹掉", StringComparison.Ordinal));

            var lines = CodeLines(File.ReadAllText(path));
            var subscribes = Events.Sum(e => lines.Count(l => l.Contains($"{e} +=")));
            var detaches = Events.Sum(e => lines.Count(l => l.Contains($"{e} -=")));

            Program.Check(
                "变异：抹掉一处解绑之后 invariant 必须报警（否则这条契约是瞎的）",
                detaches < subscribes,
                $"订阅 {subscribes}，解绑 {detaches}");
        }
        finally
        {
            // 一定要还原：变异发生在真源码上，留下来后面所有测试都在读一份被改过的源码。
            File.WriteAllText(path, original);
        }

        Program.Check(
            "变异结束后源码已还原",
            File.ReadAllText(path) == original);
    }

    /// <summary>去掉整行注释与 XML 文档注释行，只留真代码——注释里提到这些词不算数。</summary>
    private static List<string> CodeLines(string text) =>
        text.Replace("\r\n", "\n")
            .Split('\n')
            .Where(line =>
            {
                var t = line.TrimStart();
                return !t.StartsWith("//") && !t.StartsWith("///") && !t.StartsWith("*");
            })
            .ToList();

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
