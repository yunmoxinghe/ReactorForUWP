using System;
using System.Collections.Generic;
using System.IO;
using Reactor.Gallery.Pages;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 诊断页那块「计数增量显示屏」的<b>仪器自检</b>。
/// </summary>
/// <remarks>
/// <para>
/// 它是仪器，不是业务：屏幕上那句"增量：matched+1"是人判定"这一发被谁吞了"的
/// 唯一依据，而仪器出错的方式很安静——它照常显示一行字，只是那个字是假的。
/// 照着假读数去改代码，比没有读数更糟。所以这一块必须自己带哨兵。
/// </para>
/// <para>
/// 最大的威胁是<b>输入格式变了</b>：<c>ReactorLog.Counters()</c> 的文本来自
/// <c>EchoStats.Snapshot()</c> / <c>ReadyStats.Snapshot()</c>，那两处改了键名，
/// <see cref="DiagnosticsCounters.Parse"/> 会安静地拆出空字典，界面表现为
/// "记了基线、点了半天，永远零增量"——而这恰恰会被读成"事件压根没到框架"。
/// 所以第一条用例钉的是格式本身。
/// </para>
/// </remarks>
internal static class DiagnosticsCountersTests
{
    public static void Run()
    {
        Program.Section("诊断页 / 计数增量（仪器自检）");

        var root = RepoRoot();
        if (root is null)
        {
            Program.Check("定位到仓库根目录", false, "从当前目录往上没找到 Reactor.uwp + tests");
            return;
        }

        // ── 1) 真实读数能被解析：用真的 EchoStats 造一份，不照抄字面量 ────
        EchoStats.Reset();
        var guard = new EchoGuard();
        var box = new object();
        guard.Expect(box, "v");
        guard.Consume(box, "v");           // matched
        guard.Expect(box, "w");
        guard.Consume(box, "other");       // mismatch
        guard.Expect(box, "x");
        guard.CancelIfUnconsumed(box);     // sealed
        guard.Consume(box, "z");           // notExpected

        // ready 那一半也用真的 Snapshot 拼，而不是照抄字面量：
        // 键名或分隔符一改，这里就地红，而不是等到真机上发现"永远零增量"。
        ReadyStats.Reset();
        ReadyStats.Ready = 3;
        ReadyStats.Suppressed = 1;
        ReadyStats.AlreadyLoaded = 2;

        var realEcho = EchoStats.Snapshot();
        var sample = $"echo: {realEcho} | ready: {ReadyStats.Snapshot()}";

        Program.Check(
            "真实读数能被解析出全部九个键（格式哨兵）",
            AllKnownKeysPresent(sample),
            MissingKeys(sample));

        // ── 2) 反向对照：格式一改，哨兵必须报警 ──────────────────────────
        var garbled = sample.Replace("=", "：");
        Program.Check(
            "格式一变就报警（否则会伪装成'事件没到框架'）",
            !AllKnownKeysPresent(garbled),
            "换了分隔符还判格式正常 → 哨兵失明，" +
            "真出问题时界面只会显示'零增量'而没人知道是仪器坏了");

        // 键名来源必须真的写在两份源码里（EchoGuard / ReadyGate）——
        // 只跟测试样本对齐的话，改一次格式测试也会跟着一起改，等于没有哨兵。
        Program.Check(
            "九个键名在两个 Stat 类里各有出处",
            KeyNamesExistInSource(root, out var scan),
            scan.Count == 0 ? null : string.Join("；", scan));

        // ── 3) 增量只列涨的那些项 ────────────────────────────────────────
        var before =
            "echo: matched=1 mismatch=0 expired=0 notExpected=0 sealed=0 silenced=0" +
            " | ready: ready=1 suppressed=0";
        // matched 刻意不动：它是唯一会触发警告的计数，留到下面单独验。
        var after =
            "echo: matched=1 mismatch=1 expired=0 notExpected=4 sealed=1 silenced=2" +
            " | ready: ready=1 suppressed=0";

        Program.Expect(
            "增量：只列涨的项，且带符号（含新增的 silenced）",
            "mismatch+1  notExpected+4  sealed+1  silenced+2",
            DiagnosticsCounters.Contrast(before, after));

        Program.Expect(
            "没基线时不给增量（避免对空字符串做差得出全量）",
            string.Empty,
            DiagnosticsCounters.Contrast(string.Empty, after));

        Program.Expect(
            "同一份读数自比 → 零增量",
            string.Empty,
            DiagnosticsCounters.Contrast(after, after));

        // ── 4) 结论的人话版 ──────────────────────────────────────────────
        Program.Check(
            "没记基线 → 提示先记基线",
            DiagnosticsCounters.Verdict(string.Empty, string.Empty).Contains("记基线"));

        Program.Check(
            "有基线却零增量 → 指向'事件没到框架'",
            DiagnosticsCounters.Verdict(before, string.Empty).Contains("没到框架"));

        var echoDelta = DiagnosticsCounters.Contrast(before, after);
        Program.Check(
            "普通增量不带警告",
            !DiagnosticsCounters.Verdict(before, echoDelta).Contains("⚠"));

        var swallowed = DiagnosticsCounters.Verdict(before, "matched+1");
        Program.Check(
            "matched 涨了 → 明确点出这是被回声吞掉",
            swallowed.Contains("matched") && swallowed.Contains("⚠"));

        var silenced = DiagnosticsCounters.Verdict(before, "silenced+1");
        Program.Check(
            "silenced 涨了 → 说明是静默窗挡下的，不是异常",
            silenced.Contains("静默窗") && !silenced.Contains("⚠"));
    }

    private static bool AllKnownKeysPresent(string text)
    {
        var parsed = DiagnosticsCounters.Parse(text);

        foreach (var key in DiagnosticsCounters.KnownKeys)
        {
            if (!parsed.ContainsKey(key))
            {
                return false;
            }
        }

        return true;
    }

    private static string? MissingKeys(string text)
    {
        var parsed = DiagnosticsCounters.Parse(text);
        var missing = new List<string>();

        foreach (var key in DiagnosticsCounters.KnownKeys)
        {
            if (!parsed.ContainsKey(key))
            {
                missing.Add(key);
            }
        }

        return missing.Count == 0
            ? null
            : $"缺 {string.Join("/", missing)}，解析到的原文：{text}";
    }

    /// <summary>八个键名必须能在两份源码里翻到出处，而不是只跟测试样本对齐。</summary>
    private static bool KeyNamesExistInSource(string root, out List<string> missing)
    {
        missing = new List<string>();
        var sources = new[]
        {
            Path.Combine(root, "Reactor.uwp", "Internal", "EchoGuard.cs"),
            Path.Combine(root, "Reactor.uwp", "Internal", "ReadyGate.cs"),
            Path.Combine(root, "Reactor.uwp", "Internal", "ReadyStats.cs"),
        };

        var texts = new List<string>();
        foreach (var path in sources)
        {
            if (File.Exists(path))
            {
                texts.Add(File.ReadAllText(path));
            }
            else
            {
                missing.Add($"找不到 {Path.GetFileName(path)}");
            }
        }

        foreach (var key in DiagnosticsCounters.KnownKeys)
        {
            var found = false;
            foreach (var text in texts)
            {
                if (text.Contains($"{key}=", StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                missing.Add($"{key}= 在源码里没了");
            }
        }

        return missing.Count == 0;
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
