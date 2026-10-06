using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Reactor.Core.Tests;

/// <summary>
/// 受控属性的<b>调用契约</b>自检：每一条回声登记都必须有人负责把它领走（或压根不登记）。
/// </summary>
/// <remarks>
/// 为什么是源码扫描而不是仿真：仿真只能守住"已经想到的那几个控件"。
/// 这条 bug 是<b>接线的性质</b>（<c>Expect → 写属性 → Rebind</c>，而 <c>Rebind(null)</c>
/// 会退订 → 没人 <c>Consume</c>），不是某个控件的性质。因此真正的防线是一条<b>契约</b>：
/// <para>
/// 判据（刻意做成一条简单的规则，复杂规则会被绕过）：
/// 出现 <c>&lt;守卫&gt;.Expect(</c> 之后的 <see cref="Window"/> 行内，
/// 必须出现<b>同一个守卫</b>的 <c>CancelIfUnconsumed(</c>，
/// 或者这条 <c>Expect</c> 处在<b>条件登记</b>的 <c>if</c> 里（如 <c>ShouldExpectEcho</c>）。
/// </para>
/// <para>
/// 还有对称的一半：<b>卸载</b>。<see cref="LifetimeScannerCaughtViolation"/> 那一半查的是
/// 每个 handler 用过的守卫在它自己的 <c>Unmount</c> 里有没有被 <c>Forget</c> 掉、
/// 以及有没有人真的去 <c>Consume</c>。前者漏了就是静态表钉住控件（详见
/// <c>EchoLifetimeTests</c>），后者漏了等于这套登记永远匹配不上——
/// 抑制形同虚设，还白白多一次字典写入。
/// </para>
/// </remarks>
internal static class EchoContractTests
{
    /// <summary>登记之后允许出现撤销的最大间隔（行）。够容纳注释，又不至于放行到别的方法里去。</summary>
    private const int Window = 25;

    private static readonly Regex Expect = new(@"(\w+)\.Expect\(", RegexOptions.Compiled);

    /// <summary>
    /// 第十三道用：登记点连<b>接收者</b>一起取——要与下发语句的接收者比对，
    /// 否则区间里一条写给<b>别的</b>控件的赋值也会把次序判成合格。
    /// </summary>
    private static readonly Regex ExpectSite =
        new(@"(\w+)\.Expect\(\s*([A-Za-z_]\w*)", RegexOptions.Compiled);

    private static readonly Regex Guard = new(@"ShouldExpectEcho\(", RegexOptions.Compiled);

    /// <summary>
    /// 第七道用：<c>X.Prop =</c>（排除 <c>==</c>、<c>+=</c> 之类，排除 <c>a.b.Prop</c>）。
    /// 接收者名<b>不限</b>——只认 <c>control.</c> 那几个名字的写法会漏掉
    /// <c>case TextBox box:</c> 里的 <c>box.</c>。
    /// </summary>
    private static readonly Regex WriteProp =
        new(@"(?<![\w.])([A-Za-z_]\w*)\.([A-Z]\w*)\s*(?<![-+*/!=<>])=(?!=)", RegexOptions.Compiled);

    /// <summary>第十二道用：事件订阅 <c>X.Event +=</c>（<c>+=</c> 本身不算）。</summary>
    private static readonly Regex Subscribe = new(@"\.(\w+)\s*\+=(?!=)", RegexOptions.Compiled);

    /// <summary>第十二道用：事件退订 <c>X.Event -=</c>。</summary>
    private static readonly Regex Unsubscribe = new(@"\.(\w+)\s*-=(?!=)", RegexOptions.Compiled);

    /// <summary>
    /// 第十二道用：方法签名行。取<b>最后一个</b>标识符作为参数名前的名字。
    /// </summary>
    private static readonly Regex MethodSignature = new(
        @"^\s+(?:private|internal|public|protected|static|sealed|override|abstract|async|readonly|partial"
        + @"|\s)*[\w.<>\[\],\? ]+\s(\w+)\s*\(", RegexOptions.Compiled);

    public static void Run()
    {
        Program.Section("受控属性 / 调用契约（源码扫描）");

        ScannerCaughtViolation();
        EveryRegistrationIsAnswered();
        LifetimeScannerCaughtViolation();
        EveryGuardIsReleasedOnUnmount();
        RestoreIsNotGatedOnListener();
        GuardedSiteScannerCaughtViolation();
        EveryControlledWriteIsGuarded();
        EchoProneCoversEveryWrite();
        EveryPerControlTableIsReleased();
        OutsideHandlerWritesAreNeutralized();
        RangeWritesAreSilenced();
        UncontrolledWritesAreSilenced();
        EveryInertRegistrationIsLoadBearing();
        CollectionWritesAreGuarded();
        VerdictKitIsAnswered();
        SubscriptionsAreIdempotent();
        EchoTripleIsOrdered();
        EchoVerdictIsLoadBearing();
        SuppressionWindowsAreClosed();
        EchoPartiesShareOneControl();
        ExpectationMatchesTheWrite();
        SuppressionCoversItsOwnControl();
        SilenceWindowsUseTheirOwnTable();
        GateVerdictIsEnforced();
    }

    /// <summary>
    /// 反向对照：<b>扫描器本身</b>必须能抓到违约样本。
    /// </summary>
    /// <remarks>
    /// 一条永远不会红的防线等于没有防线——包括"扫描器自己写错了"这一种寂静失效
    /// （正则漏配、窗口开太大吃进别的方法）。所以这里喂一段明知有病的源码进去。
    /// </remarks>
    private static void ScannerCaughtViolation()
    {
        string[] bad =
        {
            "        ValueEcho.Expect(control, next);",
            "        control.Value = next;",
            "",
            "        Rebind(control, callback);",
        };

        string[] good =
        {
            "        ValueEcho.Expect(control, next);",
            "        control.Value = next;",
            "        ValueEcho.CancelIfUnconsumed(control);",
        };

        Program.Check(
            "违约样本必须被扫描器抓出来",
            !LineIsAnswered(bad, 0),
            "明知有病的样本却被判为合格 → 扫描器没在看");

        Program.Check(
            "合格样本不应被误判",
            LineIsAnswered(good, 0),
            "正常样本被判违约 → 扫描器在误报");
    }

    private static void EveryRegistrationIsAnswered()
    {
        var files = SourceFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到 handler 源码", false, "找不到 Reactor.uwp/Internal/Handlers.*.cs");
            return;
        }

        var violations = new List<string>();

        foreach (var path in files)
        {
            var lines = File.ReadAllLines(path);

            for (var i = 0; i < lines.Length; i++)
            {
                if (!Expect.IsMatch(lines[i]))
                {
                    continue;
                }

                if (LineIsAnswered(lines, i))
                {
                    continue;
                }

                violations.Add($"{Path.GetFileName(path)}:{i + 1}  {lines[i].Trim()}");
            }
        }

        Program.Check(
            $"每一条 Echo 登记都有人负责撤销（扫描 {files.Count} 个文件）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : $"{violations.Count} 处登记没人领，写下去的回声会变成陈旧期望吞掉下一次真实输入：" +
                  Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>第 <paramref name="index"/> 行的登记是否满足契约。</summary>
    private static bool LineIsAnswered(IReadOnlyList<string> lines, int index)
    {
        var match = Expect.Match(lines[index]);
        var guard = match.Groups[1].Value;

        // 条件登记：这一发登记本身就写在"回声到不到得了闸门"的判据里。
        // 只认这一个具体判据（`SelectionGate.ShouldExpectEcho`），不认笼统的 `if (`：
        // 绝大多数登记本来就在 `if (值变了)` 里，放开后者等于什么都没查。
        for (var up = Math.Max(0, index - 8); up < index; up++)
        {
            if (Guard.IsMatch(lines[up]))
            {
                return true;
            }
        }

        for (var i = index + 1; i < Math.Min(lines.Count, index + 1 + Window); i++)
        {
            if (lines[i].Contains($"{guard}.CancelIfUnconsumed("))
            {
                return true;
            }
        }

        return false;
    }

    // ── 生命周期那一半：卸载时要放开、读物要有人领 ──────────────────────

    /// <summary>
    /// 只认顶层类：<c>Handler</c> 里的嵌套类型（如 <c>StrongBox</c>）不算新类。
    /// </summary>
    /// <remarks>
    /// <b><c>abstract</c> / 泛型基类必须认。</b>旧写法是
    /// <c>^(?:\w+\s+)?(?:sealed\s+)?class\s+(\w+)</c>，它认不出
    /// <c>internal abstract class ItemsViewHandler&lt;TElement, TControl&gt;</c>——
    /// 于是 <c>ListView</c> / <c>GridView</c> 那整个类<b>从来没进过任何一条契约的视野</b>
    /// （它的代码行被算进了前一个类的区间里）。这是"扫描器对某个形状是瞎的"的样板：
    /// 合成样本全绿、真源码却漏，只有拿真源码开刀才看得出来。
    /// </remarks>
    private static readonly Regex TopClass =
        new(@"^(?:\w+\s+)*(?:sealed\s+|abstract\s+|static\s+|partial\s+)*class\s+(\w+)",
            RegexOptions.Compiled);

    private static readonly Regex Forget = new(@"(\w+)\.Forget\(", RegexOptions.Compiled);

    private static readonly Regex Consume = new(@"(\w+)\.Consume\(", RegexOptions.Compiled);

    /// <summary>
    /// 反向对照：生命周期这一半的扫描器也必须能抓到违约样本。
    /// </summary>
    private static void LifetimeScannerCaughtViolation()
    {
        // 守两个方向：「Unmount 里漏 Forget」和「压根没写 Unmount」。
        // 后者是这条线最容易犯的错——新人照抄一个受控属性时，常常连 Unmount 一起跳过。
        var missingForget = Demo(DemoFlaw.MissingForget);
        var noUnmount = Demo(DemoFlaw.NoUnmount);
        var healthy = Demo(DemoFlaw.None);

        Program.Check(
            "Unmount 漏 Forget 的类必须被抓出来",
            LifetimeViolations(missingForget).Count > 0,
            "明知有病的样本却被判合格 → 第二条契约没在看");

        Program.Check(
            "没有 Unmount 的类必须被抓出来",
            LifetimeViolations(noUnmount).Count > 0,
            "整个忘了写 Unmount 却判合格 → 这条防线对最常见的漏法失效");

        var healthyViolations = LifetimeViolations(healthy);
        Program.Check(
            "合格的类不应被误判",
            healthyViolations.Count == 0,
            healthyViolations.Count == 0
                ? null
                : string.Join("；", healthyViolations));

        MutationsOfRealSources();
    }

    /// <summary>
    /// <b>拿真实源码开刀。</b>逐个把 handler 里真实的 <c>Forget</c> / <c>Consume</c> 行
    /// 删掉一行，喂回扫描器，它必须<b>指名道姓</b>地报警。
    /// </summary>
    /// <remarks>
    /// 合成样本只能证明"扫描器会看这种形状"，证明不了它对<b>现在这份源码</b>有效——
    /// 万一某段代码因为缩进、嵌套、写法的缘故压根没进它的视野，照样假绿。
    /// 所以这里直接改坏仓库里的真行（只在内存里，不落盘），一处一行，
    /// 并且要求报警里带出守卫名和原因，避免"因为别的什么凑巧变红"。
    /// </remarks>
    private static void MutationsOfRealSources()
    {
        var files = SourceFiles();
        var missed = new List<string>();
        var mutations = 0;

        foreach (var path in files)
        {
            var lines = new List<string>(File.ReadAllLines(path));

            foreach (var victim in IndicesOf(lines, Forget))
            {
                // 只对"卸载时清理"的那一行开刀：别的 Forget（若有）不在本契约管辖内。
                if (!OwnerIsUnmount(lines, victim))
                {
                    continue;
                }

                var guard = Forget.Match(lines[victim]).Groups[1].Value;
                var broken = new List<string>(lines);
                broken.RemoveAt(victim);
                mutations++;

                if (!LifetimeViolations(broken).Exists(
                        v => v.Contains(guard, StringComparison.Ordinal) &&
                             v.Contains("Forget", StringComparison.Ordinal)))
                {
                    missed.Add($"{Path.GetFileName(path)}:{victim + 1} 删掉 {guard}.Forget 没报警");
                }
            }

            foreach (var victim in IndicesOf(lines, Consume))
            {
                var guard = Consume.Match(lines[victim]).Groups[1].Value;
                var broken = new List<string>(lines);
                broken[victim] = Indent(lines[victim]) + "// 变异：这一行的 Consume 被抹掉";
                mutations++;

                if (!LifetimeViolations(broken).Exists(
                        v => v.Contains(guard, StringComparison.Ordinal) &&
                             v.Contains("Consume", StringComparison.Ordinal)))
                {
                    missed.Add($"{Path.GetFileName(path)}:{victim + 1} 抹掉 {guard}.Consume 没报警");
                }
            }
        }

        Program.Check(
            $"真实源码逐行变异，每处都要指名报警（{mutations} 处）",
            mutations >= 10 && missed.Count == 0,
            missed.Count == 0
                ? null
                : $"{missed.Count} 处变异溜过去了（防线对这些形状是瞎的）：" +
                  Environment.NewLine + string.Join(Environment.NewLine, missed));
    }

    /// <summary>所有匹配 <paramref name="pattern"/> 的行号。</summary>
    private static List<int> IndicesOf(IReadOnlyList<string> lines, Regex pattern)
    {
        var result = new List<int>();

        for (var i = 0; i < lines.Count; i++)
        {
            if (pattern.IsMatch(lines[i]))
            {
                result.Add(i);
            }
        }

        return result;
    }

    /// <summary>这一行是否落在某个 <c>Unmount</c> 方法体内。</summary>
    private static bool OwnerIsUnmount(IReadOnlyList<string> lines, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (!TopClass.IsMatch(lines[i]))
            {
                continue;
            }

            var (unmount, end) = UnmountRange(lines, i, lines.Count - 1);
            return unmount >= 0 && index >= unmount && index <= end;
        }

        return false;
    }

    private static string Indent(string line) =>
        new(' ', line.Length - line.TrimStart().Length);

    /// <summary>一个<b>合格</b>的虚构受控 handler（两个受控属性、两个守卫都登记都消费都 Forget），
    /// 按 <paramref name="flaw"/> 弄坏一处。</summary>
    private static List<string> Demo(DemoFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoHandler : ElementHandler<DemoElement, Demo>",
            "{",
            "    private static readonly EchoGuard ValueEcho = new();",
            "    private static readonly EchoGuard TextEcho = new();",
            "",
            "    protected override void Update(Reconciler r, DemoElement o, DemoElement n, Demo control)",
            "    {",
            "        ValueEcho.Expect(control, n.Value);",
            "        control.Value = n.Value;",
            "        ValueEcho.CancelIfUnconsumed(control);",
            "",
            "        TextEcho.Expect(control, n.Text);",
            "        control.Text = n.Text;",
            "        TextEcho.CancelIfUnconsumed(control);",
            "    }",
            "",
            "    private static void Rebind(Demo control, Action<string>? callback)",
            "    {",
            "        control.Changed += (_, _) =>",
            "        {",
            "            if (ValueEcho.Consume(control, control.Value)) return;",
            "            if (TextEcho.Consume(control, control.Text)) return;",
            "            callback?.Invoke(control.Value);",
            "        };",
            "    }",
            "",
            "    protected override void Unmount(Reconciler reconciler, Demo control)",
            "    {",
            "        ValueEcho.Forget(control);",
            "        TextEcho.Forget(control);",
            "    }",
            "}",
        };

        switch (flaw)
        {
            case DemoFlaw.MissingForget:
                lines.RemoveAll(l => l.Contains("TextEcho.Forget(control);"));
                return lines;

            case DemoFlaw.NoUnmount:
                var at = lines.FindIndex(l => l.Contains("protected override void Unmount("));
                var head = lines.GetRange(0, at);
                head.Add("}");
                return head;

            default:
                return lines;
        }
    }

    /// <summary>把 <see cref="Demo"/> 弄坏的方式。</summary>
    private enum DemoFlaw
    {
        None,
        MissingForget,
        NoUnmount,
    }

    private static void EveryGuardIsReleasedOnUnmount()
    {
        var files = SourceFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到 handler 源码", false, "找不到 Reactor.uwp/Internal/Handlers.*.cs");
            return;
        }

        var violations = new List<string>();

        foreach (var path in files)
        {
            foreach (var violation in LifetimeViolations(File.ReadAllLines(path)))
            {
                violations.Add($"{Path.GetFileName(path)}  {violation}");
            }
        }

        Program.Check(
            $"每个用过的守卫都在 Unmount 里被 Forget、且都有人 Consume（扫描 {files.Count} 个文件）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : $"{violations.Count} 处违约：" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>扫一份源码，返回违反"卸载"契约的条目（类 + 原因）。</summary>
    private static List<string> LifetimeViolations(IReadOnlyList<string> lines)
    {
        var violations = new List<string>();

        var starts = new List<(int Index, string Name)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var m = TopClass.Match(lines[i]);
            if (m.Success)
            {
                starts.Add((i, m.Groups[1].Value));
            }
        }

        for (var k = 0; k < starts.Count; k++)
        {
            var (start, name) = starts[k];
            var end = k + 1 < starts.Count ? starts[k + 1].Index - 1 : lines.Count - 1;

            var expected = Names(lines, start, end, Expect);
            if (expected.Count == 0)
            {
                // 这个类不登记回声（多数 handler 只是布局/容器），不归这条契约管。
                continue;
            }

            var consumed = Names(lines, start, end, Consume);
            var unmount = UnmountRange(lines, start, end);
            var forgotten = unmount.Start < 0
                ? new HashSet<string>(StringComparer.Ordinal)
                : Names(lines, unmount.Start, unmount.End, Forget);

            foreach (var guard in expected)
            {
                if (unmount.Start < 0)
                {
                    violations.Add($"{name}：用了 {guard} 登记，却没有 Unmount");
                    continue;
                }

                if (!forgotten.Contains(guard))
                {
                    violations.Add($"{name}：{guard} 在 Unmount 里没有 Forget（控件会被静态表钉住）");
                }

                if (!consumed.Contains(guard))
                {
                    violations.Add($"{name}：{guard} 登记了却没有任何 Consume（抑制形同虚设）");
                }
            }
        }

        return violations;
    }

    /// <summary>区间内某个守卫名.method 调用涉及的守卫名集合。</summary>
    private static HashSet<string> Names(
        IReadOnlyList<string> lines, int start, int end, Regex pattern)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        for (var i = Math.Max(0, start); i <= end && i < lines.Count; i++)
        {
            foreach (Match m in pattern.Matches(lines[i]))
            {
                set.Add(m.Groups[1].Value);
            }
        }

        return set;
    }

    /// <summary>类块内 <c>Unmount</c> 方法体的行范围。</summary>
    private static (int Start, int End) UnmountRange(
        IReadOnlyList<string> lines, int start, int end) =>
        MethodRange(lines, start, end, l => l.Contains("void Unmount("));

    /// <summary>
    /// 类块内第一个满足 <paramref name="pred"/> 的方法的行范围（含签名行）。
    /// </summary>
    /// <remarks>
    /// 三种方法体形状都得认：块体（大括号配平）、单行表达式体（<c>=> ...;</c>）、
    /// 以及<b>跨行</b>表达式体（签名一行、<c>=> ...;</c> 在下一行）。
    /// 最后那种最容易漏：只取签名行会把方法体判成空，
    /// 于是"这里什么都没释放"被误报成"压根没写过这个方法"。
    /// <c>ButtonHandler.Unmount</c> 正是跨行写法，第二道契约此前靠
    /// "Button 没有回声登记"绕开了这个洞，属于潜伏的失聪。
    /// </remarks>
    private static (int Start, int End) MethodRange(
        IReadOnlyList<string> lines, int start, int end, Func<string, bool> pred)
    {
        for (var i = start; i <= end; i++)
        {
            if (!pred(lines[i]))
            {
                continue;
            }

            // 单行表达式体（`=> Callbacks.Remove(control);`）：方法体就这一行。
            if (lines[i].TrimEnd().EndsWith(";"))
            {
                return (i, i);
            }

            // 跨行表达式体：一直吃到分号那一行。
            if (lines[i].Contains("=>"))
            {
                var tail = i;

                while (tail <= end && !lines[tail].TrimEnd().EndsWith(";"))
                {
                    tail++;
                }

                return (i, tail);
            }

            // 块体：按大括号配平算。`    {` 独占一行是这里的写法，
            // 按"缩进 4 就是下一个成员"判会立刻终止，把方法体判成空。
            var depth = 0;
            var stop = end;
            for (var j = i; j <= end; j++)
            {
                depth += Balance(lines[j]);

                if (depth <= 0 && j > i)
                {
                    stop = j;
                    break;
                }
            }

            return (i, stop);
        }

        return (-1, -1);
    }

    /// <summary>一行里花色括号的净值（注释行不参与，免得注释里的括号把配平带偏）。</summary>
    private static int Balance(string line)
    {
        if (line.TrimStart().StartsWith("//"))
        {
            return 0;
        }

        var depth = 0;
        foreach (var ch in line)
        {
            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
            }
        }

        return depth;
    }

    /// <summary>
    /// 每个装着四道闸的 <c>Dispatch</c>，里面的受控纠正必须站在
    /// <c>callback</c> 的早退<b>之前</b>。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须对着源码扫，不能只靠仿真。</b>仿真 Link 进来的是
    /// <c>SelectionGate</c>（纯判据），<b>不是</b> <c>Handlers.Controls.cs</c>——
    /// 后者依赖 UWP 类型，连编译都不可能。于是"handler 里那句
    /// <c>if (callback is null) return;</c> 又挪回门口"这种退化，
    /// 254 项测试<b>一项都不会红</b>——做变异验证时亲眼撞上的：
    /// 前两个变异变红，第三个（就是这一条）只能原地跳过。
    /// 仿真绿着，真代码却在漏，靠再多仿真也补不上，只能看源码。
    /// <para>
    /// 这条锁的是一件很容易被"顺手"弄丢的事：受控纠正兑现的是"这个属性由 state
    /// 说了算"的承诺，<b>与有没有人监听无关</b>。把它写成"有人监听才纠正"，
    /// 同一种吞法就会在两种页面上给出两个不同的结论。
    /// </para>
    /// </remarks>
    private static void RestoreIsNotGatedOnListener()
    {
        var violations = new List<string>();
        var sawSite = false;

        foreach (var path in SourceFiles())
        {
            var lines = File.ReadAllLines(path);

            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("SelectionRestore.Schedule("))
                {
                    continue;
                }

                sawSite = true;

                // 往上找最近的那个方法头，确认这是个 dispatch 而不是别的东西。
                var head = i;
                while (head > 0 && !lines[head].Contains("private static void Dispatch"))
                {
                    head--;
                }

                if (head == 0 && !lines[0].Contains("Dispatch"))
                {
                    violations.Add($"{Path.GetFileName(path)}:{i + 1} 找不到所属的 Dispatch");
                    continue;
                }

                // 纠正必须走纯判据，不能就地写一个 `verdict == CancelTransient`：
                // 就地写一份等于把"该纠正哪一道"这件关系到全部 Selector 的事
                // 复制成了 N 份，改一处漏一处。
                var usedPolicy = false;
                for (var j = head; j <= i; j++)
                {
                    if (lines[j].Contains("ShouldRestoreAfterSuppress("))
                    {
                        usedPolicy = true;
                    }
                }

                if (!usedPolicy)
                {
                    violations.Add(
                        $"{Path.GetFileName(path)}:{i + 1} 纠正没走 SelectionGate 的纯判据");
                }

                // 这一段之间不许再有 callback 早退。
                for (var j = head; j <= i; j++)
                {
                    if (lines[j].Contains("if (callback is null)"))
                    {
                        violations.Add(
                            $"{Path.GetFileName(path)}:{j + 1} 纠正之前有 callback 早退" +
                            "（受控承诺被绑到有没有人监听上了）");
                    }
                }
            }
        }

        Program.Check(
            "纠正站点存在（含句型误配时的报警）",
            sawSite,
            "一处都没扫到 —— 改判刑名或换写法会让这条契约悄悄失聪");

        Program.Check(
            "受控纠正不受「有没有人监听」影响（" +
            string.Join("、", SourceFiles().Select(f => Path.GetFileName(f))) + "）",
            violations.Count == 0,
            violations.Count == 0 ? null : string.Join("；", violations));
    }

    // ── 第四道：写了受控属性的类，不许没有回声登记 ──────────────────────

    /// <summary>
    /// 受控属性写回 ↔ 回执通道。
    /// 左侧是"会被写下去的属性"，右侧是这个类里<b>"写下去会有事件回调出去"</b>的文本证据。
    /// </summary>
    /// <remarks>
    /// <b>为什么两侧都要有才管。</b>只查"写了属性"会误报一大片：
    /// <c>TextBlockHandler</c> 写 <c>Text</c>、<c>ExpanderHandler</c> 写
    /// <c>IsExpanded</c>，但这两个属性<b>没有回执通道</b>（元素上根本没有对应的
    /// 变更回调），写下去不会冒成用户输入，自然不需要回声抑制。
    /// 只查"有事件订阅"也不行：那会把所有只订阅不写回的 handler 都拉进来。
    /// </remarks>
    private static readonly (string Property, string Channel)[] EchoProne =
    {
        // 右侧一律要"订阅的证据"，不是"出现过这个词"。
        // `Visibility.Collapsed` 里也有 Collapsed，事件名里也有 SelectionChanged
        // 出现在别的上下文里——只认 `\s*+=`（真订阅）或 Rebind*（订阅被收进的那个助手）。
        ("SelectedIndex", @"SelectionChanged\s*\+="),
        ("SelectedItem", @"SelectionChanged\s*\+="),
        ("IsChecked", @"\bChecked\s*\+=|\bUnchecked\s*\+=|RebindCheckBox"),
        ("IsOn", @"Toggled\s*\+="),
        ("Text", @"TextChanged\s*\+=|RebindTextChanged"),
        ("Password", @"PasswordChanged\s*\+="),
        ("Value", @"ValueChanged\s*\+=|RebindSlider"),
        ("IsExpanded", @"\bExpanding\s*\+=|\bExpanded\s*\+=|\bCollapsed\s*\+="),
    };

    /// <summary>
    /// 控件<b>自己会改</b>的属性全集：用户一交互它就变了，所以写回有可能冒成用户输入。
    /// </summary>
    /// <remarks>
    /// 这份清单是<b>手写的</b>，因此它自己也需要一道闸——
    /// 否则以后新增一个 `IsPaneOpen` 那样的属性，第四道契约照样一声不吭
    /// （这正是 ListView / GridView 静默两个版本的成因）。
    /// 闸就是 <see cref="EchoProneCoversEveryWrite"/>。
    /// 只列"用户能改"的：<c>IsEnabled</c> 之类用户改不动的进不来，
    /// 进来就会把每个写它的 handler 都拖进契约视野。
    /// </remarks>
    private static readonly string[] UserEditable =
    {
        "SelectedIndex", "SelectedItem", "IsChecked", "IsOn", "IsExpanded",
        "IsPaneOpen", "IsSelected", "Text", "Password", "Value", "Date", "Time",
    };

    /// <summary>
    /// 刻意<b>不受控</b>的属性（<c>defaultValue</c> 语义）：元素上没有对应的变更回调，
    /// 框架无从得知用户动过它，因此不需要回声登记。
    /// </summary>
    /// <remarks>
    /// 与"漏登记"的区别就在这份登记里：写进来的是决定，没写进来的是遗漏。
    /// 想把它变成真受控，得先在 Element / 工厂上加回调参数，那时它就该进
    /// <see cref="EchoProne"/>，并<b>同时</b>从这儿删掉。
    /// </remarks>
    private static readonly (string Property, string Reason)[] UncontrolledByDesign =
    {
        ("IsPaneOpen", "NavigationView 工厂没有开合回调参数 —— 用户点汉堡键改的是控件，state 不知情"),
        // IsExpanded 不订阅是有源码依据的：SettingsExpander 的 Expanded / Collapsed
        // 在参考源码里没有任何一处 .Invoke（tools/ctk-ref/SettingsExpander.Events.cs:12、17
        // 声明了却没人抛），用户点击走的是 xaml:263 的 TwoWay 绑定，只冒到
        // SettingsExpander.cs:69 的 OnIsExpandedChanged，而那里只抛 automation peer 事件。
        ("IsExpanded", "工厂没有展开回调参数；且 SettingsExpander 的 Expanded/Collapsed 是死事件，订阅也收不到回执"),
    };

    /// <summary>
    /// 第七道用：<c>X.Y =</c> 里的 <c>X</c> 只有被声明成这些类型，才算"往控件上写"。
    /// </summary>
    /// <remarks>
    /// 少了这一层，<c>hook.Value = x</c>（<c>Core/RenderContext.cs</c>）和
    /// <c>box.Value = x</c>（<c>Internal/WeakTable.cs</c> 的弱引用盒子）都会被算成
    /// 受控属性写回，真违约淹在一堆误报里。
    /// </remarks>
    private static readonly string[] ControlTypes =
    {
        "UIElement", "FrameworkElement", "Control", "ContentControl", "Panel",
        "TextBlock", "TextBox", "PasswordBox", "RichEditBox", "AutoSuggestBox",
        "ComboBox", "ListBox", "ListView", "GridView", "FlipView", "Pivot",
        "CheckBox", "RadioButton", "RadioButtons", "ToggleSwitch", "Slider",
        "Button", "HyperlinkButton", "RepeatButton", "ToggleButton",
        "BreadcrumbBar", "NavigationView", "Expander", "SettingsExpander",
        "NumberBox", "RatingControl", "ColorPicker", "SplitView", "TabView",
        "ScrollViewer", "DatePicker", "TimePicker", "CalendarDatePicker", "InfoBar",
    };

    /// <summary>
    /// 第七道的登记表：<b>handler 之外</b>写用户可改属性的地方，一处一条。
    /// </summary>
    /// <remarks>
    /// <c>AnchorMethod</c> + <c>AnchorPattern</c> 是"靠什么中和这件事"的落点：
    /// 契约会去那个方法体里找这段正则，找不到就报警。这样"登记了但没修"藏不住，
    /// 而后来人改动那段代码时也会被顶回来。
    /// </remarks>
    private static readonly (string File, string Property, string Receiver,
        string? AnchorMethod, string? AnchorPattern, string Reason)[] OutsideWriteLedger =
    {
        // TextBlock 改 Text 不抛任何事件，没有回执通道，也就不存在"被当成用户输入"
        // 这条路。锚点写的是"唯一的文本订阅入口只认 TextBox"——若哪天给 TextBlock
        // 也接上回执，这条理由就不成立了，登记得跟着变。
        ("Internal/Localization.cs", "Text", "TextBlock", "RebindTextChanged",
            @"RebindTextChanged\(TextBox",
            "TextBlock 没有文本回执通道（改 Text 不抛事件），写它是纯展示，不构成回声问题"),

        // 真洞口：ApplyUid 排在 Build 最后（订阅之后），它写 Text 会抛一次没有回声
        // 登记的 TextChanged。靠 RebindTextChanged 闭包里那道"挂载期不承认"中和。
        ("Internal/Localization.cs", "Text", "TextBox", "RebindTextChanged",
            @"PropWriter\.IsMounting\s*\|\|\s*!textBox\.IsLoaded",
            "挂载期写入发生在订阅之后，由 RebindTextChanged 的「挂载期/未 Loaded 不承认」拦住"),
    };

    /// <summary>
    /// 反向对照：这一半的扫描器必须能抓到"写了受控属性却完全没有回声登记"的类。
    /// </summary>
    /// <remarks>
    /// 前三道契约守的都是"<b>已经登记</b>的回声要有人领"——
    /// <c>LifetimeViolations</c> 开头那句 <c>if (expected.Count == 0) continue;</c>
    /// 就是明证：<b>压根没登记的类直接被跳过</b>。
    /// 于是"照抄了一份受控属性却压根没接回声抑制"这种漏法，前三道<b>一道都看不见</b>——
    /// ListView / GridView 就是这样静静地躺在仓库里两个大版本的。
    /// 这一半补的就是这个洞。
    /// </remarks>
    private static void GuardedSiteScannerCaughtViolation()
    {
        var noGuard = DemoSite(DemoSiteFlaw.NoGuard);
        var noChannel = DemoSite(DemoSiteFlaw.NoChannel);
        var healthy = DemoSite(DemoSiteFlaw.None);

        Program.Check(
            "写了受控属性却没有回声登记的类必须被抓出来",
            ControlledWriteViolations(noGuard).Count > 0,
            "明知有病的样本却被判合格 → 第四道契约没在看");

        Program.Check(
            "写了属性但没有回执通道的类不应被误报（如 TextBlock 写 Text）",
            ControlledWriteViolations(noChannel).Count == 0,
            string.Join("；", ControlledWriteViolations(noChannel)));

        Program.Check(
            "合格的类不应被误判",
            ControlledWriteViolations(healthy).Count == 0,
            string.Join("；", ControlledWriteViolations(healthy)));

        MutationsOfRealGuardedSites();
    }

    /// <summary>
    /// <b>拿真实源码开刀。</b>逐个把受控站点里的 <c>Expect</c> / <c>Consume</c> /
    /// <c>Forget</c> 抹掉一行，要求扫描器<b>指名道姓</b>地报警。
    /// </summary>
    /// <remarks>
    /// 与前一节那 22 处变异的差别：那一节证明"扫描器认得这些形状"，
    /// 这一节证明"<b>现在这份源码里每一个受控站点都在它的视野内</b>"。
    /// 后者尤其要紧——<c>ItemsViewHandler</c> 曾经因为 <c>abstract</c> 不被识别
    /// 而整体失踪（见 <see cref="TopClass"/> 的注释），合成样本对此一声不吭。
    /// </remarks>
    private static void MutationsOfRealGuardedSites()
    {
        var files = SourceFiles();
        var missed = new List<string>();
        var mutations = 0;

        foreach (var path in files)
        {
            var raw = new List<string>(File.ReadAllLines(path));
            var sites = Sites(raw);

            foreach (var site in sites)
            {
                foreach (var token in new[] { "Expect", "Consume", "Forget" })
                {
                    // 抹掉这个类里该 token 的<b>全部</b>出现：只抹一处的话，
                    // 同一属性在 Mount / Update 各登记一次的类（如 NavigationView）
                    // 还剩一处，判据本来就该判它合格——那是规则对，不是扫描器瞎。
                    var victims = new List<int>();
                    for (var i = site.Start; i <= site.End; i++)
                    {
                        if (Regex.IsMatch(raw[i], token + @"\("))
                        {
                            victims.Add(i);
                        }
                    }

                    if (victims.Count == 0)
                    {
                        missed.Add($"{Path.GetFileName(path)} {site.Name}：找不到 {token} 可变异");
                        continue;
                    }

                    var broken = new List<string>(raw);
                    foreach (var victim in victims)
                    {
                        broken[victim] = Indent(raw[victim]) + "// 变异：这一行的 " + token + " 被抹掉";
                    }

                    mutations++;

                    if (!ControlledWriteViolations(broken).Exists(
                            v => v.Contains(site.Name, StringComparison.Ordinal) &&
                                 v.Contains(token, StringComparison.Ordinal)))
                    {
                        missed.Add(
                            $"{Path.GetFileName(path)}:{victims[0] + 1}（共 {victims.Count} 行）" +
                            $" 抹掉 {site.Name} 的 {token} 没报警");
                    }
                }
            }
        }

        Program.Check(
            $"受控站点逐行变异，每处都要指名报警（{mutations} 处）",
            mutations >= 12 && missed.Count == 0,
            missed.Count == 0
                ? null
                : $"{missed.Count} 处变异溜过去了：" + Environment.NewLine + string.Join(Environment.NewLine, missed));
    }

    private static void EveryControlledWriteIsGuarded()
    {
        var files = SourceFiles();
        var violations = new List<string>();
        var sites = 0;

        foreach (var path in files)
        {
            sites += Sites(File.ReadAllLines(path)).Count;

            foreach (var violation in ControlledWriteViolations(File.ReadAllLines(path)))
            {
                violations.Add($"{Path.GetFileName(path)}  {violation}");
            }
        }

        Program.Check(
            $"定位到受控站点（扫描 {files.Count} 个文件，命中 {sites} 个）",
            sites >= 12,
            "命中的站点数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            "每个写了受控属性又有回执通道的类，回声三件套齐全",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : $"{violations.Count} 处：" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        ReadmeSiteCountMatches(sites);
    }

    /// <summary>
    /// README 里那个"受控站点数"必须和源码扫描出来的对上。
    /// </summary>
    /// <remarks>
    /// README 是<b>打进 NuGet 包</b>的（`PackageReadmeFile`），也就是会发到 nuget.org 上。
    /// 而这类"共有几处"的数字，写下来的那一刻就开始变旧 ——
    /// 它曾经写着 7 处，实际已经是 12 处了，中间差了好几个版本的修复。
    /// 所以这里的要求不是"记得顺手改 README"，而是<b>让测试去对</b>：
    /// README 里留一个机器可校验的标记，数字和扫描结果不一致就红。
    /// </remarks>
    private static void ReadmeSiteCountMatches(int sites)
    {
        var root = RepoRoot();

        if (root is null)
        {
            return;
        }

        var path = Path.Combine(root, "Reactor.uwp", "README.md");

        if (!File.Exists(path))
        {
            Program.Check("README 里的受控站点数与扫描结果一致", false, "找不到 Reactor.uwp/README.md");
            return;
        }

        var text = File.ReadAllText(path);
        var claimed = SiteCountIn(text);

        Program.Check(
            "README 里留了机器可校验的受控站点数标记",
            claimed >= 0,
            "README 里没有 <!-- CONTROLLED-SITES: N --> —— 这个数会随包发到 nuget.org，不能靠人记着改");

        if (claimed < 0)
        {
            return;
        }

        Program.Check(
            $"README 里的受控站点数与源码扫描一致（README {claimed} / 扫出 {sites}）",
            claimed == sites,
            $"README 写 {claimed}，源码扫出 {sites} —— 新增或删掉受控站点时同步改 README 里那个标记");

        // 反向对照：把 README 里的数改掉，上面那条必须红。
        // 没有这一半，"标记存在 + 数字恰好对上"有可能只是运气。
        var mutated = Regex.Replace(
            text, @"(CONTROLLED-SITES:\s*)\d+", "${1}" + (sites + 1).ToString("D", null));

        Program.Check(
            "把 README 里的数改掉，这条校验必须红",
            SiteCountIn(mutated) != sites,
            "改了 README 里的数字也照样绿 —— 那这条校验是摆设");
    }

    /// <summary>从 README 文本里读出标记中的站点数；没有标记返回 -1。</summary>
    private static int SiteCountIn(string text)
    {
        var m = Regex.Match(text, @"CONTROLLED-SITES:\s*(\d+)");

        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    /// <summary>
    /// 第五道：<see cref="EchoProne"/> 与 <see cref="UncontrolledByDesign"/> 合起来
    /// 必须<b>盖住源码里每一个"用户可改属性"的写回点</b>。
    /// </summary>
    /// <remarks>
    /// 前四道守的都是"清单里的东西对不对"，这一道守的是<b>清单本身</b>。
    /// 手写清单的失效方式和手写正则一样是寂静的：漏一个属性，
    /// 那个属性上的所有 bug 从此隐身，而且没有任何一条测试会红。
    /// 所以这里把"清单完整性"也做成一条会红的断言，并拿真实源码变异自证。
    /// </remarks>
    private static void EchoProneCoversEveryWrite()
    {
        var files = SourceFiles();
        var unknown = new List<string>();
        var mutations = 0;
        var missed = new List<string>();

        foreach (var path in files)
        {
            unknown.AddRange(UnknownControlledWrites(File.ReadAllLines(path), path));

            // 真实源码变异：往每个文件的第一个类块里塞一个<b>没登记</b>的写回点，
            // 扫描器必须在那一行上报警。塞的是 IsSelected —— 用户可改，
            // 但当前没有任何 handler 写它，正好当探针。
            var raw = new List<string>(File.ReadAllLines(path));
            var blocks = ClassBlocks(StripComments(raw));

            if (blocks.Count == 0)
            {
                continue;
            }

            var open = BodyOpen(raw, blocks[0].Start);

            if (open < 0)
            {
                continue;
            }

            var mutated = new List<string>(raw);
            mutated.Insert(open + 1, "        control.IsSelected = next.IsSelected;");
            mutations++;

            if (UnknownControlledWrites(mutated, path).Count == 0)
            {
                missed.Add($"{Path.GetFileName(path)}:{open + 2} 塞进去的 IsSelected 没人管");
            }
        }

        Program.Check(
            "用户可改的属性要么进了 EchoProne、要么显式登记为不受控",
            unknown.Count == 0,
            unknown.Count == 0
                ? null
                : $"{unknown.Count} 处没人认领：" + Environment.NewLine + string.Join(Environment.NewLine, unknown));

        Program.Check(
            $"真实源码里冒出一个没登记的用户可改属性，必须报警（{mutations} 处）",
            mutations >= 7 && missed.Count == 0,
            missed.Count == 0
                ? null
                : $"{missed.Count} 处溜过去了：" + Environment.NewLine + string.Join(Environment.NewLine, missed));

        // 反向对照的另一半：证明 UncontrolledByDesign 那份登记是<b>承重</b>的，
        // 不是写上去好看的。整份拿掉之后，IsPaneOpen 必须立刻变成"没人认领"。
        var stripped = new List<string>();

        foreach (var path in files)
        {
            stripped.AddRange(UnknownControlledWrites(File.ReadAllLines(path), path, true));
        }

        Program.Check(
            "拿掉不受控登记，IsPaneOpen 必须立刻没人认领（那份登记得是承重的）",
            stripped.Exists(v => v.Contains("IsPaneOpen")),
            "登记条目是死的：删掉也没人报警，等于没登记 —— 而源码里真的在写这个属性");
    }

    /// <summary>扫一份源码，列出"写了用户可改属性，但两处清单都没登记它"的地方。</summary>
    /// <param name="ignoreUncontrolled">
    /// 当作 <see cref="UncontrolledByDesign"/> 不存在——用来证明那份登记是承重的。
    /// </param>
    private static List<string> UnknownControlledWrites(
        IReadOnlyList<string> raw, string path, bool ignoreUncontrolled = false)
    {
        var lines = StripComments(raw);
        var result = new List<string>();

        foreach (var (start, end, name) in ClassBlocks(lines))
        {
            var body = string.Join(Environment.NewLine, Block(lines, start, end));

            foreach (var property in UserEditable)
            {
                if (!Regex.IsMatch(body, @"\.\s*" + property + @"\s*=(?!=)"))
                {
                    continue;
                }

                if (Array.Exists(EchoProne, e => e.Property == property) ||
                    (!ignoreUncontrolled &&
                     Array.Exists(UncontrolledByDesign, u => u.Property == property)))
                {
                    continue;
                }

                result.Add(
                    $"{Path.GetFileName(path)}:{start + 1} {name} 写了 {property}，" +
                    "但 EchoProne 与 UncontrolledByDesign 里都没有它 —— " +
                    "要么它是真受控（该补登记与回声三件套），要么登记它为不受控并写明理由");
            }
        }

        return result;
    }

    /// <summary>类声明之后那个 `{` 的行号；找不着返回 -1。</summary>
    private static int BodyOpen(IReadOnlyList<string> raw, int classLine)
    {
        for (var i = classLine; i < raw.Count && i <= classLine + 3; i++)
        {
            if (raw[i].TrimEnd().EndsWith("{", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>受控站点：类块里既有属性写回、又有回执通道的那些类。</summary>
    private static List<(int Start, int End, string Name)> Sites(IReadOnlyList<string> raw)
    {
        var lines = StripComments(raw);
        var result = new List<(int, int, string)>();

        for (var k = 0; k < ClassBlocks(lines).Count; k++)
        {
            var (start, end, name) = ClassBlocks(lines)[k];
            var body = string.Join(Environment.NewLine, Block(lines, start, end));

            foreach (var (property, channel) in EchoProne)
            {
                if (Regex.IsMatch(body, @"\.\s*" + property + @"\s*=(?!=)") &&
                    Regex.IsMatch(body, channel))
                {
                    result.Add((start, end, name));
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>扫一份源码，返回违反"受控写回必须有回声登记"的条目。</summary>
    private static List<string> ControlledWriteViolations(IReadOnlyList<string> raw)
    {
        var lines = StripComments(raw);
        var violations = new List<string>();

        foreach (var (start, end, name) in ClassBlocks(lines))
        {
            var body = string.Join(Environment.NewLine, Block(lines, start, end));

            foreach (var (property, channel) in EchoProne)
            {
                if (!Regex.IsMatch(body, @"\.\s*" + property + @"\s*=(?!=)") ||
                    !Regex.IsMatch(body, channel))
                {
                    continue;
                }

                foreach (var token in new[] { "Expect", "Consume", "Forget" })
                {
                    if (Regex.IsMatch(body, @"\w+\." + token + @"\("))
                    {
                        continue;
                    }

                    violations.Add(
                        $"{name}：写了 {property} 且有回执通道，却没有 {token} —— " +
                        (token == "Expect"
                            ? "受控写回会冒出去当用户输入"
                            : token == "Consume"
                                ? "登记永远匹配不上，抑制形同虚设"
                                : "控件会被静态表钉住"));
                }
            }
        }

        return violations;
    }

    /// <summary>顶层类的行区间（<c>开始, 结束, 类名</c>）。</summary>
    private static List<(int Start, int End, string Name)> ClassBlocks(IReadOnlyList<string> lines)
    {
        var result = new List<(int, int, string)>();
        var starts = new List<(int Index, string Name)>();

        for (var i = 0; i < lines.Count; i++)
        {
            var m = TopClass.Match(lines[i]);
            if (m.Success)
            {
                starts.Add((i, m.Groups[1].Value));
            }
        }

        for (var k = 0; k < starts.Count; k++)
        {
            var end = k + 1 < starts.Count ? starts[k + 1].Index - 1 : lines.Count - 1;
            result.Add((starts[k].Index, end, starts[k].Name));
        }

        return result;
    }

    private static List<string> Block(IReadOnlyList<string> lines, int start, int end)
    {
        var block = new List<string>();

        for (var i = Math.Max(0, start); i <= end && i < lines.Count; i++)
        {
            block.Add(lines[i]);
        }

        return block;
    }

    /// <summary>
    /// 把注释行挖空（保留行号）。扫描器读的是"代码里有什么"，
    /// 注释里提到的 <c>Expanding/Collapsed</c> 之类不该被当成真的有订阅。
    /// </summary>
    // ── 第八道：写「会把受控值夹走」的相邻属性，必须在静默窗里写 ──────────

    /// <summary>会夹取受控值的相邻属性写入：<c>Minimum</c> / <c>Maximum</c>。</summary>
    private static readonly Regex RangeWrite =
        new(@"\.(Minimum|Maximum)\s*(?<![-+*/!=<>])=(?!=)", RegexOptions.Compiled);

    /// <summary>开窗那一行：<c>using (&lt;守卫&gt;.Silence(...))</c>。</summary>
    private static readonly Regex SilenceOpen = new(@"\.Silence\(", RegexOptions.Compiled);

    /// <summary>方法头（用于"往上回溯到哪儿为止"）。</summary>
    private static readonly Regex MethodHead =
        new(@"^\s{4,}(?:(?:private|internal|public|protected|static|sealed|override|virtual|async)\s+)+" +
            @"[\w<>\[\],\.\?]+\s+(\w+)\s*\(", RegexOptions.Compiled);

    /// <summary>这些"名字 + 括号"不是方法头，别把回溯提前掐断。</summary>
    private static readonly HashSet<string> NotAMethod =
        new(StringComparer.Ordinal) { "new", "if", "using", "return", "switch", "while", "foreach", "catch", "lock", "nameof", "throw" };

    private enum DemoRangeFlaw
    {
        None,
        NoWindow,
        HelperSilenced,
        HelperUnsilenced,
        HelperMountOnly,
        NoEchoGuard,
    }

    /// <summary>
    /// 第八道契约：写了<b>会把受控值夹走</b>的相邻属性（<c>Minimum</c> / <c>Maximum</c>），
    /// 那一笔必须在<b>静默窗</b>里。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 它守的是本版第 14 处：改区间会把受控值夹到新区间，那一发事件抛在
    /// <b>旧订阅还挂着</b>的时候（<c>Rebind</c> 在 <c>Update</c> 最后才退订），
    /// 而夹出来的值事先不知道 —— 只能靠"这段时间的事件都不是用户输入"来挡
    /// （<c>EchoGuard.Silence</c>），没法靠登记期望值。
    /// </para>
    /// <para>
    /// 判据刻意做简单：<b>从写入行往上回溯，先遇到窗就算合规，先遇到方法头就算违约</b>；
    /// 违约的那一方若是个辅助方法（如 <c>NumberBox</c> 的 <c>ApplyRange</c>），
    /// 改为检查它的<b>调用点</b>是否都在窗里。两种免检：
    /// <list type="bullet">
    ///   <item>类里没有 <c>EchoGuard</c>（<c>ProgressBar</c> / <c>ProgressRing</c> 也写
    ///         <c>Minimum</c>，但它们没有受控值回调）—— 没人接的那一发不归这条管；</item>
    ///   <item>调用点在 <c>Mount</c> 里 —— 那一刻订阅还没挂上，写了也没人听见
    ///         （与第 13 处"挂载期回执不承认"同一个理由）。</item>
    /// </list>
    /// </para>
    /// </remarks>
    private static void RangeWritesAreSilenced()
    {
        var files = SourceFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到 handler 源码", false, "找不到 Reactor.uwp/Internal/Handlers.*.cs");
            return;
        }

        var violations = new List<string>();
        var sites = 0;

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path));
            sites += RangeWritesInScope(lines);

            foreach (var violation in RangeWindowViolations(lines))
            {
                violations.Add($"{Path.GetFileName(path)}  {violation}");
            }
        }

        Program.Check(
            $"定位到会夹取受控值的写入点（扫描 {files.Count} 个文件，命中 {sites} 处）",
            sites >= 4,
            "命中的写入数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            "每一处会夹取受控值的写入都在静默窗里（或属于两种免检）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : $"{violations.Count} 处：{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

        Program.Check(
            "合成样本：窗内写 → 不报警",
            RangeWindowViolations(DemoRange(DemoRangeFlaw.None)).Count == 0,
            "健康的样本也被判违约 —— 判据太宽");

        Program.Check(
            "合成样本：不开窗直接写 → 必须报警",
            RangeWindowViolations(DemoRange(DemoRangeFlaw.NoWindow)).Count == 1,
            "不开窗也照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：辅助方法被窗罩住的调用点 → 不报警",
            RangeWindowViolations(DemoRange(DemoRangeFlaw.HelperSilenced)).Count == 0,
            "NumberBox 那种『写在辅助方法里』的形状被误判");

        Program.Check(
            "合成样本：辅助方法的调用点没罩窗 → 必须报警",
            RangeWindowViolations(DemoRange(DemoRangeFlaw.HelperUnsilenced)).Count == 1,
            "只在辅助方法内部找窗 → 会漏掉真正的调用点");

        Program.Check(
            "合成样本：只在 Mount 里调用 → 不报警（那一刻还没有订阅者）",
            RangeWindowViolations(DemoRange(DemoRangeFlaw.HelperMountOnly)).Count == 0,
            "挂载期根本没有监听者在场，不该要求开窗");

        Program.Check(
            "合成样本：没有受控值的类（ProgressBar 形状）不该归这条管",
            RangeWindowViolations(DemoRange(DemoRangeFlaw.NoEchoGuard)).Count == 0,
            "把没有回执通道的控件也算进来 —— 会淹没真正的违约");

        MutationsOfRealSilenceWindows();
    }

    /// <summary>落在契约视野内的写入行数：有受控值（<c>EchoGuard</c>）且写了 Min / Max。</summary>
    private static int RangeWritesInScope(IReadOnlyList<string> lines)
    {
        var count = 0;

        foreach (var span in ClassSpans(lines))
        {
            if (!HasEchoGuard(lines, span))
            {
                continue;
            }

            count += IndicesOf(lines, RangeWrite).Count(i => i >= span.Start && i <= span.End);
        }

        return count;
    }

    private static List<string> RangeWindowViolations(IReadOnlyList<string> lines)
    {
        var violations = new List<string>();

        foreach (var span in ClassSpans(lines))
        {
            var writes = IndicesOf(lines, RangeWrite).Where(i => i >= span.Start && i <= span.End).ToList();

            if (writes.Count == 0 || !HasEchoGuard(lines, span))
            {
                continue;
            }

            // 同一个方法里的多处写入只报一次：要点是"这个方法没罩窗"，
            // 报两遍只是把同一个错误说两遍。
            var reported = new HashSet<string>(StringComparer.Ordinal);

            foreach (var i in writes)
            {
                if (SilencedBefore(lines, span.Start, i))
                {
                    continue;
                }

                var owner = EnclosingMethod(lines, i);

                // 挂载期还没有订阅者在场（与第 13 处同一个理由），写了也没人听见。
                if (owner == "Mount")
                {
                    continue;
                }

                // 辅助方法（如 NumberBox 的 ApplyRange）：改查它的调用点。
                // Update 本身不算辅助方法——它没有"调用点"可查，放行它等于放行一切。
                if (owner is not null && owner != "Update" &&
                    AllCallSitesSilenced(lines, span, owner))
                {
                    continue;
                }

                var key = $"{span.Name}/{owner}";

                if (!reported.Add(key))
                {
                    continue;
                }

                violations.Add(
                    $"{span.Name}  {owner ?? "（认不出所属方法）"}  第 {i + 1} 行：" +
                    $"{lines[i].Trim()} —— 没有静默窗");
            }
        }

        return violations;
    }

    /// <summary>
    /// 从这一行往上回溯：落进某个窗的<b>块里</b> → true；先遇到方法头 → false。
    /// </summary>
    /// <remarks>
    /// <b>"看见窗"不等于"被这个窗罩住"。</b>早先的判据是"往上先遇到窗就算合规"，
    /// 于是一个方法里有两道窗时，前面那道会把后面那道罩的写入<b>一并认领</b>：
    /// <c>NavigationView.Update</c> 里 <c>PaneDisplayMode</c> 的窗在前、
    /// <c>MenuItems</c> 的窗在后，删掉后者，扫描器照样判"罩住了"——
    /// 变异测试因此假绿（第九道把它逼出来了）。
    /// 现在改成算窗的<b>块范围</b>：写入行要真的落在 <c>using { … }</c> 之间。
    /// </remarks>
    private static bool SilencedBefore(IReadOnlyList<string> lines, int top, int index)
    {
        for (var i = Math.Min(index, lines.Count - 1); i >= top; i--)
        {
            int close;

            if (SilenceOpen.IsMatch(lines[i]))
            {
                // WindowClose 给的是块尾 } 的下一行，所以块内是 [i + 1, close - 1]。
                close = WindowClose(lines, i);
            }
            else if (RebuildOpen.IsMatch(lines[i]))
            {
                // 持续标记（Rebuilding.Set(…, true) … Set(…, false)）：与第十道的
                // InGuardBlock 同一个口径。第九道原本只认 Silence 窗，于是把
                // NavigationView 改用持续标记之后，它立刻报了两处假违约——
                // 那是这一道自己失聪，不是源码退步。
                close = lines.Count;

                for (var k = i + 1; k < lines.Count; k++)
                {
                    if (RebuildClose.IsMatch(lines[k]))
                    {
                        close = k;
                        break;
                    }
                }
            }
            else
            {
                if (IsMethodHead(lines[i]))
                {
                    return false;
                }

                continue;
            }

            if (index > i && index < close)
            {
                return true;
            }

            // 没罩住这一行就继续往前找更早的窗，别在这里就下结论。
        }

        return false;
    }

    /// <summary>
    /// 辅助方法（如 <c>ApplyRange</c>）的每个调用点：要么在窗里，要么在 <c>Mount</c> 里。
    /// </summary>
    private static bool AllCallSitesSilenced(
        IReadOnlyList<string> lines,
        (int Start, int End, string Name) span,
        string method,
        bool allowInitialize = false)
    {
        // 只排除"前面是单词字符"（免得 DemoFoo 被 Foo 命中）。
        // <b>不许</b>排除点前缀：<c>reconciler.PatchItems(…)</c> 这种带接收者的调用
        // 前面恰好是个点，排除掉就等于一条调用点都找不到 —— 那"调用点没罩窗"这一档
        // 直接失聪（第十道里真踩到了，参见 <see cref="AllCallSitesGuarded"/>）。
        var call = new Regex(@"(?<![\w])" + Regex.Escape(method) + @"\s*\(", RegexOptions.Compiled);
        var sites = 0;

        for (var i = span.Start; i <= span.End; i++)
        {
            if (!call.IsMatch(lines[i]))
            {
                continue;
            }

            if (IsMethodHead(lines[i]))
            {
                continue;   // 声明行本身
            }

            sites++;

            var owner = EnclosingMethod(lines, i);

            // Initialize 也在挂载路径上：那一刻订阅还没挂上（第九道把这一档也放行）。
            var mounting = owner == "Mount" || (allowInitialize && owner == "Initialize");

            if (!mounting && !SilencedBefore(lines, span.Start, i))
            {
                return false;
            }
        }

        // 一个调用点都没有 = 这段辅助方法不写任何控件，谈不上漏窗。
        return true;
    }

    private static string? EnclosingMethod(IReadOnlyList<string> lines, int index)
    {
        for (var i = Math.Min(index, lines.Count - 1); i >= 0; i--)
        {
            var m = MethodHead.Match(lines[i]);

            if (m.Success && !NotAMethod.Contains(m.Groups[1].Value))
            {
                return m.Groups[1].Value;
            }
        }

        return null;
    }

    private static bool IsMethodHead(string line)
    {
        var m = MethodHead.Match(line);
        return m.Success && !NotAMethod.Contains(m.Groups[1].Value);
    }

    private static bool HasEchoGuard(IReadOnlyList<string> lines, (int Start, int End, string Name) span)
    {
        for (var i = span.Start; i <= span.End && i < lines.Count; i++)
        {
            if (lines[i].Contains("EchoGuard", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 反向对照，<b>两个方向</b>：删掉真源码里的窗要报警；只把窗<b>挪到写入之后</b>
    /// 也要报警——后者证明这条契约盯的是<b>位置</b>，不是"类里有没有 <c>Silence</c>"。
    /// </summary>
    private static void MutationsOfRealSilenceWindows()
    {
        var files = SourceFiles();
        var mutations = 0;
        var missed = new List<string>();

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path));

            for (var i = 0; i < lines.Count; i++)
            {
                if (!SilenceOpen.IsMatch(lines[i]))
                {
                    continue;
                }

                // 两道判据各管一段视野，变异也要分派清楚：
                // 罩的是 Minimum / Maximum → 第八道必须报警；
                // 其余（第九道把视野泛化到"任何非受控属性"）→ 第九道必须报警。
                // 不分派的话，新增的那些窗会让第八道"没报警"—— 但它本来就不该管它们。
                var close = WindowClose(lines, i);
                var isRange = IsRangeWindow(lines, i, close);
                var before9 = UncontrolledWriteViolations(lines).Count;

                var deleted = new List<string>(lines);
                deleted.RemoveAt(i);
                mutations++;

                if (isRange && RangeWindowViolations(deleted).Count == 0)
                {
                    missed.Add($"{Path.GetFileName(path)}  第 {i + 1} 行 删掉窗后第八道没报警：{lines[i].Trim()}");
                }

                if (UncontrolledWriteViolations(deleted).Count <= before9)
                {
                    missed.Add($"{Path.GetFileName(path)}  第 {i + 1} 行 删掉窗后第九道没多报：{lines[i].Trim()}");
                }

                // 挪到窗<b>关掉之后</b>（块尾那个大括号的后面）——必须落在写入之后，
                // 否则"挪了"和"没挪"没区别，这一向的对照就是假的。
                var moved = new List<string>(lines);
                moved.RemoveAt(i);
                moved.Insert(Math.Min(close, moved.Count), lines[i]);
                mutations++;

                if (isRange && RangeWindowViolations(moved).Count == 0)
                {
                    missed.Add($"{Path.GetFileName(path)}  第 {i + 1} 行 窗挪到写入之后第八道没报警：{lines[i].Trim()}");
                }

                if (UncontrolledWriteViolations(moved).Count <= before9)
                {
                    missed.Add($"{Path.GetFileName(path)}  第 {i + 1} 行 窗挪到写入之后第九道没多报：{lines[i].Trim()}");
                }
            }
        }

        Program.Check(
            $"删掉 / 挪动真源码里的静默窗都必须报警（{mutations} 处）",
            mutations >= 4 && missed.Count == 0,
            missed.Count == 0
                ? null
                : $"{missed.Count} 处溜过去了：{Environment.NewLine}{string.Join(Environment.NewLine, missed)}");
    }

    // ── 第九道：有受控值的 handler 里，写「非受控属性」要么在窗里、要么登记为惰性 ──

    /// <summary>
    /// 已知<b>不会牵动受控值</b>的属性写入：换了它们，控件不会因此把受控值改掉，
    /// 也就不会有「不是用户动的」那一发事件冒出来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这份清单是第八道的泛化。</b>第八道只认 <c>Minimum</c> / <c>Maximum</c>
    /// 两个名字，于是 <c>SelectionMode</c>、<c>MaxLength</c>、<c>GroupName</c> 这些
    /// 同样会牵动受控值的写入<b>一条都进不了视野</b>—— 按名字认，漏掉的是名单之外的
    /// 全部。第九道把判据反过来：<b>默认全管，豁免要登记</b>。
    /// 以后新增一处属性写入，这条契约会先红，逼出一句"它会不会牵动受控值"的答复。
    /// </para>
    /// <para>
    /// 登记一条的门槛：<b>说得出它为什么改不动受控值</b>。说不出就不登记，开窗——
    /// 窗在"永远没等到事件"时的代价是零，漏罩则是一发假回调，代价不对称。
    /// </para>
    /// <para>
    /// <b>按（类名, 属性名）记，不按属性名记。</b>第十道一开始就是这么记的，
    /// 第九道却只记属性名——于是 `Header` 一旦登记，<b>十二个</b> handler 里写
    /// `Header` 的地方全被免掉，包括那些"这里的 Header 其实牵动受控值"的。
    /// 同一个属性在 A 控件上是标签、在 B 控件上是整棵子树（`Content` 就是：
    /// CheckBox 上是文字、NavigationView 上是当前页面），按名字记等于把两种语义
    /// 混成一条豁免。现在按对记，并且
    /// <see cref="EveryInertRegistrationIsLoadBearing"/> 逐条验证它<b>真的承重</b>——
    /// 登记了一条没人用的豁免，那条检查会红。
    /// </para>
    /// </remarks>
    private static readonly (string Class, string Property, string Reason)[] InertByDesign =
    {
        ("TextBoxHandler", "Header", "只换标题文本；控件不会因为换了标题而改 Text"),
        ("TextBoxHandler", "PlaceholderText", "只换占位文本；同上"),
        ("CheckBoxHandler", "Content", "这里是标签文字；换标签不动 IsChecked（ToggleButton 的选中态与 Content 无耦合）"),
        ("ComboBoxHandler", "Header", "只换标题文本；不改 SelectedIndex"),
        ("ComboBoxHandler", "PlaceholderText", "只换占位文本；同上"),
        ("ToggleSwitchHandler", "Header", "只换标题文本；不改 IsOn"),
        ("ToggleSwitchHandler", "OnContent", "开态文字；换文字不动 IsOn"),
        ("ToggleSwitchHandler", "OffContent", "关态文字；同上"),
        ("RadioButtonHandler", "Content", "这里是标签文字；换标签不动 IsChecked"),
        ("RadioButtonsHandler", "Header", "只换标题文本；不改 SelectedIndex"),
        ("ItemsViewHandler", "Header", "只换标题文本；不改选中"),
        ("ItemsViewHandler", "IsItemClickEnabled",
            "只决定点击时抛不抛 ItemClick；ItemClick 与 SelectionChanged 是两条独立的事件，它不改选中"),
        ("NavigationViewHandler", "Header", "只换标题文本；不改选中项"),
        ("NavigationViewHandler", "IsPaneOpen", "开合面板不改选中项；且 PaneOpening / PaneClosed 我们没有订阅，那一发没人接"),
        ("NavigationViewHandler", "IsSettingsVisible", "设置项的可见性；不动选中"),
        ("NavigationViewHandler", "IsBackButtonVisible", "返回键的可见性；不动选中"),
        ("NavigationViewHandler", "IsBackEnabled", "返回键能不能点；不动选中"),
        ("NavigationViewHandler", "AlwaysShowHeader", "标题区的显示策略；不动选中"),
        ("PasswordBoxHandler", "Header", "只换标题文本；不改 Password"),
        ("PasswordBoxHandler", "PlaceholderText", "只换占位文本；同上"),
        ("PasswordBoxHandler", "IsPasswordRevealButtonEnabled",
            "只决定「显示密码」那个按钮在不在，不动 Password 的内容"),
        ("AutoSuggestBoxHandler", "Header", "只换标题文本；不改 Text"),
        ("AutoSuggestBoxHandler", "PlaceholderText", "只换占位文本；同上"),
        ("NumberBoxHandler", "Header", "只换标题文本；不改 Value"),
    };

    /// <summary>不受控属性的写入：<c>X.Prop =</c>，排除 <c>==</c> / <c>&gt;=</c> / <c>=&gt;</c> / 插值 <c>={</c>。</summary>
    private static readonly Regex AnyWrite =
        new(@"\.([A-Za-z]\w*)\s*(?<![=!<>+\-*/])=(?![=>{])", RegexOptions.Compiled);

    /// <summary>受控值属性（<see cref="EchoProne"/> 那几个）：归第四道管，这里不重复管。</summary>
    private static readonly HashSet<string> ControlledValues =
        new(EchoProne.Select(e => e.Property), StringComparer.Ordinal);

    /// <summary>第九道的豁免键：<b>（类名, 属性名）</b>，与第十道同一个口径。</summary>
    private static readonly HashSet<(string Class, string Property)> InertKeys =
        new(InertByDesign.Select(e => (e.Class, e.Property)));

    /// <summary>
    /// 第九道契约：有受控值（<c>EchoGuard</c>）的 handler 里，<b>每一处</b>非受控属性的
    /// 写入，要么在静默窗里、要么在 <see cref="InertByDesign"/> 里登记过。
    /// </summary>
    private static void UncontrolledWritesAreSilenced()
    {
        var files = SourceFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到 handler 源码（第九道）", false, "找不到 Reactor.uwp/Internal/Handlers.*.cs");
            return;
        }

        var violations = new List<string>();
        var sites = 0;
        var registered = 0;

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path));
            sites += UncontrolledWritesInScope(lines);
            registered += UncontrolledWritesInScope(lines, inertOnly: true);

            foreach (var violation in UncontrolledWriteViolations(lines))
            {
                violations.Add($"{Path.GetFileName(path)}  {violation}");
            }
        }

        Program.Check(
            // 两个数是不相交的两堆：sites = 没登记、因此<b>必须</b>靠抑制块罩住的；
            // registered = 走了惰性登记的。写成"命中 X 处，其中 Y 处走了登记"是错的
            // ——Y 根本不是 X 的子集，两个数加起来才是写入点总数。
            $"定位到非受控属性的写入点（扫描 {files.Count} 个文件：" +
            $"{sites} 处靠抑制块罩住、{registered} 处走了惰性登记）",
            sites >= 12 && registered >= 5,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            "每一处非受控属性的写入都在静默窗里，或已登记为惰性",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : $"{violations.Count} 处：{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

        Program.Check(
            "合成样本：窗里写未登记的属性 → 不报警",
            UncontrolledWriteViolations(DemoAnyWrite(DemoAnyFlaw.None)).Count == 0,
            "健康的样本也被判违约 —— 判据太宽");

        Program.Check(
            "合成样本：不开窗写未登记的属性 → 必须报警",
            UncontrolledWriteViolations(DemoAnyWrite(DemoAnyFlaw.NoWindow)).Count == 1,
            "不开窗也照样绿 —— 这条契约是摆设");

        // 样本自己带豁免表（"DemoAnyHandler" 是本样本的类名），不给真登记表塞假条目。
        var demoWaiver = new[] { ("DemoAnyHandler", "Header") };

        Program.Check(
            "合成样本：写了已登记的属性（Header）→ 不报警",
            UncontrolledWriteViolations(DemoAnyWrite(DemoAnyFlaw.Registered), demoWaiver).Count == 0,
            "登记那份名单没生效");

        // 这一条是「按（类名, 属性名）记」这把牙本身：旧口径按属性名记，
        // `Header` 一登记就把下面这个没登记的类也免了 —— 那就分不清
        // "这个类的 Header 真的惰性" 和 "蹭了别人的豁免"。
        Program.Check(
            "合成样本：同名属性在没登记的另一个类里写 → 必须报警（豁免按（类名, 属性名）生效）",
            UncontrolledWriteViolations(DemoAnyWrite(DemoAnyFlaw.OtherClass), demoWaiver).Count == 1,
            "换了个类照样不报 —— 那还是按属性名在记，颗粒度没真改");

        Program.Check(
            "合成样本：没有受控值的类 → 不归这条管",
            UncontrolledWriteViolations(DemoAnyWrite(DemoAnyFlaw.NoEchoGuard)).Count == 0,
            "把没有回执通道的控件也算进来 —— 会淹没真正的违约");

        Program.Check(
            "合成样本：挂载期（Mount / Initialize）写 → 不报警",
            UncontrolledWriteViolations(DemoAnyWrite(DemoAnyFlaw.MountOnly)).Count == 0,
            "挂载期根本没有监听者在场，不该要求开窗");

        // 反向对照的另一半：把 InertByDesign 那份登记当作不存在，
        // 真源码里必须至少有一处跟着变红 —— 否则那份登记是摆设。
        Program.Check(
            "反向对照：把惰性登记当不存在，真源码里必须冒出违约（证明登记是承重的）",
            WithoutInertRegistration(files) > 0,
            "登记全撤掉也不红 —— 说明登记的这些属性本来就没被写到");
    }

    /// <summary>把 <see cref="InertKeys"/> 临时清空，数一遍真源码的违约数。</summary>
    private static int WithoutInertRegistration(List<string> files)
    {
        var saved = new HashSet<(string Class, string Property)>(InertKeys);
        InertKeys.Clear();

        try
        {
            var count = 0;

            foreach (var path in files)
            {
                count += UncontrolledWriteViolations(StripComments(File.ReadAllLines(path))).Count;
            }

            return count;
        }
        finally
        {
            foreach (var name in saved)
            {
                InertKeys.Add(name);
            }
        }
    }

    /// <summary>
    /// <b>逐条</b>撤登记：每一条（类名, 属性名）豁免都必须是<b>承重</b>的——
    /// 单独撤掉它，真源码里必须立刻冒出至少一处违约。
    /// </summary>
    /// <remarks>
    /// <see cref="WithoutInertRegistration"/> 只证明了"整份登记有东西在托着"：
    /// 二十几条里只要有<em>一条</em>真被写到，它就绿。这挡不住反向的腐烂——
    /// 往表里塞没人用的条目（比如把某个其实落在窗里、根本不需要豁免的写入也登记上），
    /// 整份对照照样绿，而那条假豁免会在将来真有人写它的时候免掉一次该报的警。
    /// 按名字记的旧口径恰好是这种腐烂的温床：一条 `Header` 免掉十二个类，
    /// 其中大半压根没写 `Header`。
    /// </remarks>
    private static void EveryInertRegistrationIsLoadBearing()
    {
        var files = SourceFiles();

        var parsed = files
            .Select(p => StripComments(File.ReadAllLines(p)).ToArray())
            .ToList();

        var idle = new List<string>();

        foreach (var entry in InertByDesign)
        {
            InertKeys.Remove((entry.Class, entry.Property));

            try
            {
                var count = parsed.Sum(l => UncontrolledWriteViolations(l).Count);

                if (count == 0)
                {
                    idle.Add($"{entry.Class}/{entry.Property} —— {entry.Reason}");
                }
            }
            finally
            {
                InertKeys.Add((entry.Class, entry.Property));
            }
        }

        Program.Check(
            $"逐条撤登记：{InertByDesign.Length} 条豁免每条都必须承重",
            idle.Count == 0,
            idle.Count == 0
                ? null
                : $"{idle.Count} 条撤掉之后真源码一声不吭 —— 它们要么没人写，要么本来就落在窗里：" +
                  Environment.NewLine + string.Join(Environment.NewLine, idle));
    }

    /// <summary>
    /// 落在第九道视野内的写入处数：有受控值、且属性既不是受控值、也没被登记为惰性。
    /// <paramref name="inertOnly"/> 为 true 时只数"走了登记"的那些。
    /// </summary>
    private static int UncontrolledWritesInScope(IReadOnlyList<string> lines, bool inertOnly = false)
    {
        var count = 0;

        foreach (var span in ClassSpans(lines))
        {
            if (!HasEchoGuard(lines, span))
            {
                continue;
            }

            for (var i = span.Start; i <= span.End && i < lines.Count; i++)
            {
                foreach (Match m in AnyWrite.Matches(lines[i]))
                {
                    var property = m.Groups[1].Value;
                    var isControlled = ControlledValues.Contains(property);
                    var isInert = InertKeys.Contains((span.Name, property));

                    if (isControlled || isInert != inertOnly)
                    {
                        continue;
                    }

                    count++;
                }
            }
        }

        return count;
    }

    /// <param name="inert">
    /// 豁免表，默认 <see cref="InertKeys"/>。合成样本传自己的那份，
    /// 免得为了让样本通过而往真登记表里塞假条目。
    /// </param>
    private static List<string> UncontrolledWriteViolations(
        IReadOnlyList<string> lines,
        ICollection<(string Class, string Property)>? inert = null)
    {
        var waived = inert ?? InertKeys;
        var violations = new List<string>();

        foreach (var span in ClassSpans(lines))
        {
            if (!HasEchoGuard(lines, span))
            {
                continue;
            }

            // 同一个方法写同一个属性多次只报一次：要点是"这一笔没罩窗"，
            // 报两遍只是把同一个错误说两遍。
            var reported = new HashSet<string>(StringComparer.Ordinal);

            for (var i = span.Start; i <= span.End && i < lines.Count; i++)
            {
                foreach (Match m in AnyWrite.Matches(lines[i]))
                {
                    var property = m.Groups[1].Value;

                    if (ControlledValues.Contains(property) || waived.Contains((span.Name, property)))
                    {
                        continue;
                    }

                    if (SilencedBefore(lines, span.Start, i))
                    {
                        continue;
                    }

                    var owner = EnclosingMethod(lines, i);

                    // 挂载期还没有订阅者在场（与第 13 处同一个理由），写了也没人听见。
                    if (owner is "Mount" or "Initialize")
                    {
                        continue;
                    }

                    // 辅助方法：改查它的调用点。Update 本身不算辅助方法——
                    // 它没有"调用点"可查，放行它等于放行一切。
                    if (owner is not null && owner != "Update" &&
                        AllCallSitesSilenced(lines, span, owner, allowInitialize: true))
                    {
                        continue;
                    }

                    if (!reported.Add($"{span.Name}/{owner}/{property}"))
                    {
                        continue;
                    }

                    violations.Add(
                        $"{span.Name}  {owner ?? "（认不出所属方法）"}  第 {i + 1} 行：" +
                        $"{lines[i].Trim()} —— {property} 没罩窗，也没登记为惰性");
                }
            }
        }

        return violations;
    }

    // ── 第十道契约：集合写入必须在抑制块里 ─────────────────────────────────

    /// <summary>
    /// 第十道：<b>集合写入</b>（<c>Items.Clear()</c> / <c>MenuItems.Add(…)</c> /
    /// <c>ItemsSource =</c>）必须落在抑制块里，或按（类名, 属性名）登记为惰性。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这一道补的是第九道的形状盲区。</b>第八、九道的判据都是 <c>X.Prop = v</c>
    /// 这种<b>赋值</b>形式，而"改集合"是<b>方法调用</b>形式
    /// （<c>Items.Clear()</c>、<c>Children.Add(…)</c>）——它们一条都进不了那两道的视野。
    /// 于是 <c>ComboBox.ReplaceItems</c> 之所以"通过"，不是因为契约看见了它并判它合格，
    /// 而是因为契约<b>根本没看见它</b>。那份绿灯是假的：今天给某个新控件加一句
    /// <c>control.Items.Clear()</c>，两道契约一声都不会吭。
    /// </para>
    /// <para>
    /// <b>换集合恰好是牵动受控选中值最典型的动作。</b>第 17 节那个行为模型
    /// （<c>SiblingWriteSim</c>）建模的就是它：<c>Clear</c> 把选中冲成 -1，
    /// 那一发抛在旧订阅还挂着的时候。所以这一道盯的是真东西，不是形状洁癖。
    /// </para>
    /// <para>
    /// 抑制块两种，都认：<c>EchoGuard.Silence</c> 的窗（同步块）与
    /// <c>Rebuilding</c> 标记位（<c>true</c> → <c>false</c> 之间）。后者是
    /// ComboBox / RadioButtons / ItemsView 一直在用的老机制，与窗等价
    /// （同样是"这段的作者是渲染路径，不是用户"），一并认下。
    /// </para>
    /// </remarks>
    private static void CollectionWritesAreGuarded()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第十道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var sites = parsed.Sum(f => CollectionSites(f.Lines));

        var violations = new List<string>();

        foreach (var f in parsed)
        {
            foreach (var violation in CollectionWriteViolations(f.Lines, parsed))
            {
                violations.Add($"{Path.GetFileName(f.Path)}  {violation}");
            }
        }

        Program.Check(
            $"定位到集合写入点（扫描 {files.Count} 个文件，命中 {sites} 处）",
            sites >= 15,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            "每一处集合写入都在抑制块里（静默窗 / Rebuilding 标记），或已登记为惰性",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : $"{violations.Count} 处：{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

        Program.Check(
            "合成样本：Rebuilding 标记里改集合 → 不报警",
            CollectionWriteViolations(DemoCollection(DemoCollectionFlaw.None), WithSample(DemoCollection(DemoCollectionFlaw.None), parsed)).Count == 0,
            "健康的样本也被判违约 —— 判据太宽");

        Program.Check(
            "合成样本：静默窗里改集合 → 不报警",
            CollectionWriteViolations(DemoCollection(DemoCollectionFlaw.Silenced), WithSample(DemoCollection(DemoCollectionFlaw.Silenced), parsed)).Count == 0,
            "窗和 Rebuilding 标记是等价的两种写法，只认一种会逼人改写法而不是改语义");

        Program.Check(
            "合成样本：裸改集合 → 必须报警",
            CollectionWriteViolations(DemoCollection(DemoCollectionFlaw.NoGuard), WithSample(DemoCollection(DemoCollectionFlaw.NoGuard), parsed)).Count == 1,
            "裸写也照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：挂载期改集合 → 不报警",
            CollectionWriteViolations(DemoCollection(DemoCollectionFlaw.MountOnly), WithSample(DemoCollection(DemoCollectionFlaw.MountOnly), parsed)).Count == 0,
            "挂载期根本没有监听者在场，不该要求罩块");

        Program.Check(
            "合成样本：登记为惰性的（Reconciler.Children）→ 不报警",
            CollectionWriteViolations(DemoCollection(DemoCollectionFlaw.Registered), WithSample(DemoCollection(DemoCollectionFlaw.Registered), parsed)).Count == 0,
            "按（类名, 属性名）登记那份名单没生效");

        Program.Check(
            "合成样本：辅助方法在调用点被罩住 → 不报警",
            CollectionWriteViolations(DemoCollection(DemoCollectionFlaw.HelperGuarded), WithSample(DemoCollection(DemoCollectionFlaw.HelperGuarded), parsed)).Count == 0,
            "认不出调用点那层，真源码里的 ApplyMenuItems 会被误报");

        Program.Check(
            "合成样本：辅助方法的调用点裸着 → 必须报警",
            CollectionWriteViolations(DemoCollection(DemoCollectionFlaw.HelperUnguarded), WithSample(DemoCollection(DemoCollectionFlaw.HelperUnguarded), parsed)).Count == 1,
            "只看辅助方法自己有没有罩块，等于放行一切");

        // 变异：真源码里删掉每一道抑制块的开门行，违约数必须变多。
        //
        // 两处讲究：
        // （a）计数要用<b>全局</b>总数，不能只看这一个文件。删掉 ItemsViewHandler 的
        //     Rebuilding.Set(true) 之后，真正冒出来的是<b>另一个文件</b>里
        //     Reconciler.PatchItems 那两行——只在同文件里数，这一处会假绿。
        // （b）按"这个块罩住了什么"分派：块里若只有 Minimum/Maximum 之类的属性写入，
        //     它是第八、九道的猎物，第十道本来就不该管它，要求它红就是假红。
        var mutations = 0;
        var missed = new List<string>();
        var before = parsed.Sum(f => CollectionWriteViolations(f.Lines, parsed).Count);

        foreach (var f in parsed)
        {
            for (var i = 0; i < f.Lines.Length; i++)
            {
                if (!SilenceOpen.IsMatch(f.Lines[i]) && !RebuildOpen.IsMatch(f.Lines[i]))
                {
                    continue;
                }

                var close = SilenceOpen.IsMatch(f.Lines[i])
                    ? WindowClose(f.Lines, i)
                    : RebuildCloseLine(f.Lines, i);

                if (!BlockCoversCollection(f.Lines, i, close, parsed))
                {
                    continue;
                }

                var deleted = new List<string>(f.Lines);
                deleted.RemoveAt(i);
                mutations++;

                var mutated = parsed
                    .Select(g => ReferenceEquals(g.Lines, f.Lines)
                        ? (g.Path, Lines: deleted.ToArray())
                        : g)
                    .ToList();

                if (mutated.Sum(g => CollectionWriteViolations(g.Lines, mutated).Count) <= before)
                {
                    missed.Add($"{Path.GetFileName(f.Path)}  第 {i + 1} 行 删掉抑制块后没多报：" +
                               $"{f.Lines[i].Trim()}");
                }
            }
        }

        Program.Check(
            $"变异：删掉真源码里罩着集合写入的抑制块（{mutations} 处）都必须多报违约",
            mutations >= 3 && missed.Count == 0,
            missed.Count == 0 ? null : string.Join(Environment.NewLine, missed));

        // 反向对照：把惰性登记当不存在，真源码里必须冒出违约 ——
        // 否则那份登记是摆设（登记一个从没人写过的名字也不会被发现）。
        Program.Check(
            "反向对照：把惰性登记当不存在，真源码里必须冒出违约（证明登记是承重的）",
            WithoutInertCollection(files) > 0,
            "登记全撤掉也不红 —— 说明登记的这些集合本来就没被写到");
    }

    /// <summary>
    /// 第十一道契约：<b>受控选中类 handler 必须逐件答复判据簇六件套</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 前十道守的全是"<b>写入</b>那一侧"：写了受控值得有人领回声、写了别的东西得在
    /// 抑制块里。它们共同的前提是——<b>这个 handler 已经接了判据</b>。
    /// 于是存在这样一种漏法：每一条写入都规规矩矩地开了窗（第八、九、十道全绿），
    /// 但判据本身一件都没接，"该吞的按自己的一套吞、该纠正的没人纠正"。
    /// <c>NavigationViewHandler</c> 就是这样躺在仓库里的。
    /// </para>
    /// <para>
    /// 六件套是四个受控选中控件<b>共用</b>的判据（理由写在 <c>SelectionPolicy</c> 与
    /// <c>SelectionGate</c> 的类注释里：抄成 N 份就会改一处漏一处）。
    /// 这一道把"抽出来了"变成"必须接上"。
    /// </para>
    /// </remarks>
    private static void VerdictKitIsAnswered()
    {
        // 与第十道同一个口径：<b>全树</b>。第十道当初把口径从 Handlers.*.cs 扩到全树
        // 才看见 Reconciler.Children 那一处；判据簇同样是"谁都可能漏接"的东西，
        // 没有理由相信它只可能漏在 handler 里。
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第十一道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var classes = parsed.Sum(f => ControlledSelectionClasses(f.Lines).Count);
        var violations = new List<string>();

        foreach (var f in parsed)
        {
            foreach (var violation in VerdictViolations(f.Lines))
            {
                violations.Add($"{Path.GetFileName(f.Path)}  {violation}");
            }
        }

        Program.Check(
            $"定位到受控选中类（扫描 {files.Count} 个文件，命中 {classes} 个）",
            classes >= 3,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            "每个受控选中类都逐件答复了判据簇六件套（接上，或登记豁免并写明理由）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : $"{violations.Count} 处：{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

        Program.Check(
            "合成样本：六件齐全 → 不报警",
            VerdictViolations(DemoVerdict(DemoVerdictFlaw.None)).Count == 0,
            "健康的样本也被判违约 —— 判据太宽");

        Program.Check(
            "合成样本：缺一件 → 必须报警",
            VerdictViolations(DemoVerdict(DemoVerdictFlaw.MissingOne)).Count == 1,
            "缺一件也照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：不是选中类（没有 SelectionChanged 通道）→ 不归这条管",
            VerdictViolations(DemoVerdict(DemoVerdictFlaw.NotSelection)).Count == 0,
            "把没有选中通道的控件也算进来 —— 会淹没真正的违约");

        // 变异：真源码里删掉每一件判据的落点，违约数必须变多。
        //
        // 只认"该类里唯一一处"的落点：Rebuilding 的开门与关门都匹配同一件，
        // 删掉关门行判据还在，要求它报警就是假红。
        var before = parsed.Sum(f => VerdictViolations(f.Lines).Count);
        var mutations = 0;
        var missed = new List<string>();

        foreach (var f in parsed)
        {
            for (var i = 0; i < f.Lines.Length; i++)
            {
                var kit = Array.FindIndex(VerdictKit, k => Regex.IsMatch(f.Lines[i], k.Pattern));

                if (kit < 0 || !SoleAnchorInClass(f.Lines, i, VerdictKit[kit].Pattern))
                {
                    continue;
                }

                var deleted = new List<string>(f.Lines);
                deleted.RemoveAt(i);
                mutations++;

                var mutated = parsed
                    .Select(g => ReferenceEquals(g.Lines, f.Lines) ? (g.Path, Lines: deleted.ToArray()) : g)
                    .ToList();

                if (mutated.Sum(g => VerdictViolations(g.Lines).Count) <= before)
                {
                    missed.Add($"{Path.GetFileName(f.Path)}  第 {i + 1} 行 删掉 " +
                               $"{VerdictKit[kit].Item} 的落点后没多报：{f.Lines[i].Trim()}");
                }
            }
        }

        Program.Check(
            $"变异：删掉真源码里判据簇的落点（{mutations} 处）都必须多报违约",
            mutations >= 10 && missed.Count == 0,
            missed.Count == 0 ? null : string.Join(Environment.NewLine, missed));

        // 变异（类级）：有些件在一个类里有<b>好几处</b>落点（NavigationView 的
        // ReadyGate.IsReady 在 ApplySelectedItem 与两个事件入口各有一处），逐行删不动它
        // ——删掉其中一处，剩下的还在，要求报警就是假红。整类删掉该件的全部落点才行。
        var classLevel = 0;
        var classMissed = new List<string>();

        foreach (var f in parsed)
        {
            foreach (var span in ClassBlocks(f.Lines))
            {
                var body = string.Join(Environment.NewLine, Block(f.Lines, span.Start, span.End));

                if (!SelectionChannel.IsMatch(body) || !HasEchoGuard(f.Lines, span))
                {
                    continue;
                }

                foreach (var (item, pattern) in VerdictKit)
                {
                    if (Array.Exists(VerdictWaived, w => w.Class == span.Name && w.Item == item))
                    {
                        continue;
                    }

                    var hits = new List<int>();

                    for (var i = span.Start; i <= span.End && i < f.Lines.Length; i++)
                    {
                        if (Regex.IsMatch(f.Lines[i], pattern))
                        {
                            hits.Add(i);
                        }
                    }

                    if (hits.Count == 0)
                    {
                        continue;
                    }

                    var stripped = new List<string>(f.Lines);

                    for (var k = hits.Count - 1; k >= 0; k--)
                    {
                        stripped.RemoveAt(hits[k]);
                    }

                    classLevel++;

                    var mutated = parsed
                        .Select(g => ReferenceEquals(g.Lines, f.Lines)
                            ? (g.Path, Lines: stripped.ToArray())
                            : g)
                        .ToList();

                    if (mutated.Sum(g => VerdictViolations(g.Lines).Count) <= before)
                    {
                        classMissed.Add(
                            $"{Path.GetFileName(f.Path)} {span.Name} 整类删掉 {item} 的 " +
                            $"{hits.Count} 处落点后没多报");
                    }
                }
            }
        }

        Program.Check(
            $"变异（类级）：整类删掉某一件的全部落点（{classLevel} 组）都必须多报违约",
            classLevel >= 5 && classMissed.Count == 0,
            classMissed.Count == 0 ? null : string.Join(Environment.NewLine, classMissed));

        // 反向对照：把豁免登记当作不存在，真源码里必须冒出违约 ——
        // 否则那份登记是摆设（给一个本来就已接上的件登记豁免，撤掉也不会红）。
        Program.Check(
            "反向对照：把豁免登记当不存在，真源码里必须冒出违约（证明豁免是承重的）",
            parsed.Sum(f => VerdictViolations(f.Lines, ignoreWaivers: true).Count) > 0,
            "豁免全撤掉也不红 —— 说明这些件本来就已接上，那份登记是多余的");
    }

    /// <summary>
    /// 第十一道用：判据簇六件套，以及"接上了"的落点长什么样。
    /// </summary>
    /// <remarks>
    /// <b>最后一件刻意只认"持续判据"、不认时间窗。</b><c>Rebuilding.Set(…, true)</c>
    /// 与 <c>ReadyGate.Arm</c> 都是"显式开门、直到有人关门"；而 <c>Silence</c> 是
    /// 靠 <c>using</c> 块的时间窗——第 18 节已经论证过：时间窗罩不住延后到窗关之后的
    /// 异步后果。把 <c>Silence</c> 也算成这一件，等于给"用窗罩异步"发免死金牌。
    /// </remarks>
    private static readonly (string Item, string Pattern)[] VerdictKit =
    {
        ("ShouldApply", @"SelectionPolicy\.ShouldApply"),
        ("ShouldExpectEcho", @"SelectionGate\.ShouldExpectEcho"),
        ("Decide", @"SelectionGate\.Decide"),
        ("Restore", @"SelectionGate\.ShouldRestoreAfterSuppress"),
        ("Rebuild", @"Rebuilding\.Set\([^)]*,\s*true\s*\)|ReadyGate\.Arm"),

        // 判据一（未就绪）单独成件：它最容易缺，而且缺了之后症状最不像 bug ——
        // 那一发事件会在控件 Loaded 之前悄悄跑进用户回调一次（表现为"初始页被改掉"），
        // 而不是"点了没反应"。上一轮 NavigationView 就缺这一件，
        // 靠行为模型 RebuildEchoSim.MountDefersRestore 才逼出来。
        ("NotReady", @"ReadyGate\.IsReady"),
    };

    /// <summary>
    /// 第十一道用：豁免登记（类名, 件名, 理由）。
    /// </summary>
    /// <remarks>
    /// 登记一条的门槛与第九、十道一样：<b>说得出它为什么不需要</b>，而且理由要能
    /// 追溯到控件源码。说不出就接上——接判据的代价是几行代码，
    /// 漏判据的代价是一发假回调。
    /// </remarks>
    private static readonly (string Class, string Item, string Reason)[] VerdictWaived =
    {
        // NavigationView 的事件参数是 NavigationViewSelectionChangedEventArgs，
        // 没有 AddedItems，共用的 SelectionArgs.SelectedSomething 接不上去。
        // 但三道判据各自都有天然兑现，逐条核对过源码（release/2.8
        // dev/NavigationView/NavigationView.cpp）：
        //   判据零（取消选中）—— RaiseSelectionChangedEvent 在 nextItem 为 nullptr 时
        //     **不设** SelectedItemContainer，handler 里那句
        //     `args.SelectedItemContainer is NavigationViewItem` 守卫天然等价于
        //     "AddedItems 里没有实项"；
        //   判据一（未就绪）—— 由 Rebind 事件入口的 `!ReadyGate.IsReady(control)` 接上。
        //     **原先写的是"由 !m_appliedTemplate 早退天然兑现"，那条理由不成立**：
        //     早退只覆盖"模板没套好"那一段，而源码注释接着说 selectedIndex 会在
        //     "repeater finishes loading" 之后被正确更新——即那一发是<b>延后</b>回来的，
        //     且回来时选中的是实项（判据零拦不住）。repeater 是模板子树的一部分，
        //     子先于父，所以它回来时控件尚未 Loaded，`ReadyGate.IsReady` 问得到它。
        //     行为模型见 RebuildEchoSim.MountDefersRestore / GateOnReady。
        //   判据二（重建中）—— 由 Rebuilding 标记承担，Rebind 里显式检查。
        // 三条都不是"不需要"，是"控件自己已经做了"或"由等价的闸门接上"，
        // 所以登记而不是接。
        ("NavigationViewHandler", "Decide",
            "事件参数不是 SelectionChangedEventArgs（没有 AddedItems）；三道判据分别由 SelectedItemContainer 守卫 / !m_appliedTemplate 早退 / Rebuilding 标记天然兑现"),

        // ShouldRestoreAfterSuppress 只对 CancelTransient 生效，而那一条是 RadioButtons
        // 特有的"点当前已选中项 → OnChildUnchecked → Select(-1)"（cpp:431）语义。
        // NavigationView 点已选中项走的是 ItemInvoked，SelectionChanged 根本不抛；
        // 重建导致的清空则由 ApplySelectedItem 在标记关门前同步补发。
        ("NavigationViewHandler", "Restore",
            "没有 CancelTransient 那条路径（点已选中项走 ItemInvoked，不抛 SelectionChanged）；重建清空由 ApplySelectedItem 同步补发"),

        // Selector（ListView / GridView 的基类）没有 RadioButtons 那道 m_blockSelecting
        // 闸门：OnSelectedIndexChanged 的提前返回只有 IsSelectionReentrancyAllowed()
        // 与 IsInit() 两条，<b>都与模板是否套上无关</b>。于是"控件就绪了没"在
        // Selector 上不是一个可问的问题——不是一个可以省略的答案，是<b>没有答案</b>。
        // 源码依据见 ItemsViewHandler 类注释（Selector_Partial.cpp）。
        ("ItemsViewHandler", "NotReady",
            "Selector 没有模板闸门（OnSelectedIndexChanged 早退只问重入锁与 IsInit），「未就绪」在这里没有答案可问，判据一直接传 true"),
    };

    /// <summary>选中类回执通道：<c>SelectionChanged +=</c>，真订阅，不是"出现过这个词"。</summary>
    private static readonly Regex SelectionChannel =
        new(@"SelectionChanged\s*\+=", RegexOptions.Compiled);

    /// <summary>落在第十一道视野内的类：既有选中回执通道、又持有 <c>EchoGuard</c>。</summary>
    private static List<string> ControlledSelectionClasses(IReadOnlyList<string> lines)
    {
        var result = new List<string>();

        foreach (var span in ClassBlocks(lines))
        {
            var body = string.Join(Environment.NewLine, Block(lines, span.Start, span.End));

            if (SelectionChannel.IsMatch(body) && HasEchoGuard(lines, span))
            {
                result.Add(span.Name);
            }
        }

        return result;
    }

    /// <summary>
    /// 第十一道的违约清单：每个受控选中类缺了六件套里的哪几件。
    /// </summary>
    /// <param name="ignoreWaivers">
    /// 当作 <see cref="VerdictWaived"/> 不存在——用来证明那份豁免是承重的。
    /// </param>
    private static List<string> VerdictViolations(IReadOnlyList<string> lines, bool ignoreWaivers = false)
    {
        var result = new List<string>();

        foreach (var span in ClassBlocks(lines))
        {
            var body = string.Join(Environment.NewLine, Block(lines, span.Start, span.End));

            if (!SelectionChannel.IsMatch(body) || !HasEchoGuard(lines, span))
            {
                continue;
            }

            foreach (var (item, pattern) in VerdictKit)
            {
                if (Regex.IsMatch(body, pattern))
                {
                    continue;
                }

                if (!ignoreWaivers &&
                    Array.Exists(VerdictWaived, w => w.Class == span.Name && w.Item == item))
                {
                    continue;
                }

                result.Add(
                    $"{span.Name} 没接 {item} —— 要么接上，要么在 VerdictWaived 里登记并写明理由");
            }
        }

        return result;
    }

    /// <summary>
    /// 这一行是不是它所在类里<b>唯一</b>一处匹配该判据。
    /// </summary>
    /// <remarks>
    /// 只删唯一一处才算真变异：<c>Rebuilding</c> 的开门与关门都命中同一件，
    /// 删掉关门行判据照样在，要求它报警就是假红。
    /// </remarks>
    private static bool SoleAnchorInClass(IReadOnlyList<string> lines, int index, string pattern)
    {
        foreach (var span in ClassBlocks(lines))
        {
            if (index < span.Start || index > span.End)
            {
                continue;
            }

            var count = 0;

            for (var i = span.Start; i <= span.End && i < lines.Count; i++)
            {
                if (Regex.IsMatch(lines[i], pattern))
                {
                    count++;
                }
            }

            return count == 1;
        }

        return false;
    }

    private enum DemoVerdictFlaw
    {
        None,
        MissingOne,
        NotSelection,
    }

    /// <summary>
    /// 第十一道的合成样本。
    /// </summary>
    /// <remarks>
    /// 类名刻意叫 <c>DemoVerdictHandler</c>：判据是按<b>类名</b>登记的，
    /// 撞真源码里的名字会串到那份登记上去。
    /// </remarks>
    private static string[] DemoVerdict(DemoVerdictFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoVerdictHandler : ElementHandler<DemoElement, DemoCombo>",
            "{",
            "    private static readonly EchoGuard SelectionEcho = new();",
            "    private static readonly WeakTable<DemoCombo, bool> Rebuilding = new();",
            string.Empty,
            "    protected override void Update(",
            "        Reconciler reconciler, DemoElement o, DemoElement n, DemoCombo control)",
            "    {",
        };

        if (flaw != DemoVerdictFlaw.NotSelection)
        {
            lines.Add("        control.SelectionChanged += (_, args) => { };");
        }

        if (flaw == DemoVerdictFlaw.MissingOne)
        {
            // 只抽掉 Decide 这一件，其余照留 —— 缺一件就该只报一条，
            // 报两条就分不清"判据太宽"和"确实缺了两件"。
            // 判据一的落点要单独留一行：它原本写在 Decide 的实参里，
            // 跟着 Decide 一起删就会一次缺两件。
            lines.Add("        var verdict = SelectionVerdict.Pass;");
            lines.Add("        if (!ReadyGate.IsReady(control)) { return; }");
        }
        else
        {
            lines.Add("        var verdict = SelectionGate.Decide(");
            lines.Add("            SelectionArgs.SelectedSomething(args),");
            lines.Add("            ReadyGate.IsReady(control),");
            lines.Add("            Rebuilding.TryGetValue(control, out var busy) && busy);");
        }

        lines.Add("        if (SelectionPolicy.ShouldApply(control.Items.Count, control.SelectedIndex, 1))");
        lines.Add("        {");
        lines.Add("            if (SelectionGate.ShouldExpectEcho(1, true, false))");
        lines.Add("            {");
        lines.Add("                SelectionEcho.Expect(control, 1);");
        lines.Add("            }");
        lines.Add("        }");

        lines.Add(string.Empty);
        lines.Add("        if (SelectionGate.ShouldRestoreAfterSuppress(verdict))");
        lines.Add("        {");
        lines.Add("        }");

        lines.Add(string.Empty);
        lines.Add("        Rebuilding.Set(control, true);");
        lines.Add("        Rebuilding.Set(control, false);");
        lines.Add("    }");
        lines.Add("}");
        return lines.ToArray();
    }

    /// <summary>把 <see cref="InertCollectionKeys"/> 临时清空，数一遍真源码的违约数。</summary>
    private static int WithoutInertCollection(List<string> files)
    {
        var saved = new HashSet<string>(InertCollectionKeys, StringComparer.Ordinal);
        InertCollectionKeys.Clear();

        try
        {
            var parsed = files
                .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
                .ToList();

            return parsed.Sum(f => CollectionWriteViolations(f.Lines, parsed).Count);
        }
        finally
        {
            foreach (var key in saved)
            {
                InertCollectionKeys.Add(key);
            }
        }
    }

    /// <summary>
    /// 惰性登记：<b>（类名, 属性名）</b>。按类名分开记是因为同一个属性名
    /// （<c>Items</c>）在 ComboBox 上是要管的，在 SettingsExpander 上不是——
    /// 只按属性名登记会把前者一起豁免掉。
    /// </summary>
    private static readonly (string Class, string Property, string Reason)[] InertCollectionByDesign =
    {
        ("Reconciler", "Children", "布局面板的子元素：Panel 没有选中类回执通道，改 Children 不抛 SelectionChanged"),
        ("ExpanderHandler", "Children", "表头里的图标 + 文本：同上"),
        ("VirtualizingListHandler", "Children", "虚拟化画布：Canvas 没有选中通道"),
        ("SettingsExpanderHandler", "Items", "展开区卡片容器：SettingsExpander 不是 Selector，Items 变动不抛选中类事件"),
        ("BreadcrumbBarHandler", "Items", "临时载体 ItemsControl——它不进可视树、没有订阅；真控件那笔走的是 ItemsSource"),
        ("BreadcrumbBarHandler", "ItemsSource", "BreadcrumbBar 的回执是 ItemClicked（只在点击时抛），换 ItemsSource 不抛"),
        ("AutoSuggestBoxHandler", "ItemsSource", "候选列表：回执是 SuggestionChosen / TextChanged，换候选项不抛这两者"),
    };

    /// <summary>
    /// 集合变更：<c>X.Items.Clear()</c> / <c>X.MenuItems.Add(…)</c> / <c>X.ItemsSource =</c>。
    /// </summary>
    private static readonly Regex CollectionMutate = new(
        @"\.(\w+)\s*(?:\.\s*(?:Clear|Add|Insert|Remove|RemoveAt|Move|ReplaceAll)\s*\(|=\s)",
        RegexOptions.Compiled);

    /// <summary>会被这一道认作"集合"的属性名。窄一点，别把 <c>Text =</c> 这类扫进来。</summary>
    private static readonly HashSet<string> CollectionNames =
        new(new[] { "Items", "MenuItems", "Children", "ItemsSource", "SelectedItems", "Columns", "Rows", "TabItems" },
            StringComparer.Ordinal);

    private static readonly HashSet<string> InertCollectionKeys =
        new(InertCollectionByDesign.Select(e => e.Class + "." + e.Property), StringComparer.Ordinal);

    /// <summary>Rebuilding 标记位的开门 / 关门行。</summary>
    private static readonly Regex RebuildOpen =
        new(@"Rebuilding\.Set\([^)]*,\s*true\s*\)", RegexOptions.Compiled);

    private static readonly Regex RebuildClose =
        new(@"Rebuilding\.Set\([^)]*,\s*false\s*\)", RegexOptions.Compiled);

    /// <summary>落在第十道视野内的集合写入处数（不管合不合规）。</summary>
    private static int CollectionSites(IReadOnlyList<string> lines)
    {
        var count = 0;

        foreach (var span in ClassSpans(lines))
        {
            for (var i = span.Start; i <= span.End && i < lines.Count; i++)
            {
                foreach (Match m in CollectionMutate.Matches(lines[i]))
                {
                    if (CollectionNames.Contains(m.Groups[1].Value))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private static List<string> CollectionWriteViolations(
        string[] lines, List<(string Path, string[] Lines)> all)
    {
        var violations = new List<string>();

        // 同一个方法写同一个集合多次只报一次：要点是"这一笔没罩块"，
        // 报两遍只是把同一个错误说两遍。
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var span in ClassSpans(lines))
        {
            for (var i = span.Start; i <= span.End && i < lines.Length; i++)
            {
                foreach (Match m in CollectionMutate.Matches(lines[i]))
                {
                    var property = m.Groups[1].Value;

                    if (!CollectionNames.Contains(property))
                    {
                        continue;
                    }

                    if (InertCollectionKeys.Contains(span.Name + "." + property))
                    {
                        continue;
                    }

                    if (InGuardBlock(lines, span, i))
                    {
                        continue;
                    }

                    var owner = EnclosingMethod(lines, i);

                    // 挂载期还没有订阅者在场（与第 13 处同一个理由），写了也没人听见。
                    if (owner is "Mount" or "Initialize")
                    {
                        continue;
                    }

                    // 辅助方法：改查它的调用点（可能落在<b>另一个文件</b>里——
                    // Reconciler.PatchItems 就是被 ItemsViewHandler 的 Rebuilding 罩住的）。
                    // Update 本身不算辅助方法：它没有调用点可查，放行它等于放行一切。
                    if (owner is not null && owner != "Update" && AllCallSitesGuarded(all, owner))
                    {
                        continue;
                    }

                    if (!reported.Add($"{span.Name}/{owner}/{property}"))
                    {
                        continue;
                    }

                    violations.Add(
                        $"{span.Name}  {owner ?? "（认不出所属方法）"}  第 {i + 1} 行：" +
                        $"{lines[i].Trim()} —— 改 {property} 没罩抑制块，也没登记为惰性");
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// 这一行是否落在某个抑制块里：静默窗（<c>using { … }</c>）或
    /// <c>Rebuilding</c> 标记的开 / 关之间。
    /// </summary>
    private static bool InGuardBlock(
        IReadOnlyList<string> lines, (int Start, int End, string Name) span, int index)
    {
        for (var g = span.Start; g < index; g++)
        {
            int close;

            if (SilenceOpen.IsMatch(lines[g]))
            {
                close = WindowClose(lines, g);
            }
            else if (RebuildOpen.IsMatch(lines[g]))
            {
                close = lines.Count;

                for (var k = g + 1; k <= span.End && k < lines.Count; k++)
                {
                    if (RebuildClose.IsMatch(lines[k]))
                    {
                        close = k;
                        break;
                    }
                }
            }
            else
            {
                continue;
            }

            // 块内是 (g, close)：开门行自己不算，关门行也不算。
            if (index > g && index < close)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 辅助方法的<b>每一个</b>调用点（跨文件）都要在抑制块里，或落在挂载期。
    /// 一个调用点都没有 = 这段辅助方法没有调用方，谈不上漏。
    /// </summary>
    private static bool AllCallSitesGuarded(
        List<(string Path, string[] Lines)> all, string method)
    {
        // 只排除"前面是单词字符"（DemoReplaceItems 不该被 ReplaceItems 命中），
        // <b>不许</b>排除点前缀：真源码里的调用点长这样 ——
        // <c>reconciler.PatchItems(control, …)</c>，前面恰好是个点。
        // 上一版写成 (?<![\w.]) 于是这一处调用点<b>一条都匹配不到</b>，
        // 变异测试跟着假绿。（合成样本里是 ApplyRange(control) 那种不带点的写法，
        // 所以第九道一直没暴露这个洞。）
        var call = new Regex(@"(?<![\w])" + Regex.Escape(method) + @"\s*\(", RegexOptions.Compiled);

        foreach (var f in all)
        {
            foreach (var span in ClassSpans(f.Lines))
            {
                for (var i = span.Start; i <= span.End && i < f.Lines.Length; i++)
                {
                    if (!call.IsMatch(f.Lines[i]) || IsMethodHead(f.Lines[i]))
                    {
                        continue;
                    }

                    if (InGuardBlock(f.Lines, span, i))
                    {
                        continue;
                    }

                    if (EnclosingMethod(f.Lines, i) is "Mount" or "Initialize")
                    {
                        continue;
                    }

                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 把合成样本自己也塞进"所有文件"：辅助方法的调用点<b>就在样本自己里</b>，
    /// 不塞进去的话一个调用点都找不到，于是"调用点裸着"那一档永远不报警。
    /// </summary>
    private static List<(string Path, string[] Lines)> WithSample(
        string[] sample, List<(string Path, string[] Lines)> parsed) =>
        parsed.Append(("（合成样本）", sample)).ToList();

    /// <summary><c>Rebuilding.Set(x, true)</c> 那一行的配对关门行号。</summary>
    private static int RebuildCloseLine(IReadOnlyList<string> lines, int open)
    {
        for (var k = open + 1; k < lines.Count; k++)
        {
            if (RebuildClose.IsMatch(lines[k]))
            {
                return k;
            }
        }

        return lines.Count;
    }

    /// <summary>被调方法的名字：<c>reconciler.PatchItems(…)</c> → <c>PatchItems</c>。</summary>
    private static readonly Regex Callee = new(@"\.\s*(\w+)\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// 这一道抑制块（<paramref name="open"/> … <paramref name="close"/>）罩住的，
    /// 到底有没有集合写入——直接的，或经由一个辅助方法的。
    /// </summary>
    private static bool BlockCoversCollection(
        IReadOnlyList<string> lines, int open, int close, List<(string Path, string[] Lines)> all)
    {
        for (var i = open + 1; i < close && i < lines.Count; i++)
        {
            foreach (Match m in CollectionMutate.Matches(lines[i]))
            {
                if (CollectionNames.Contains(m.Groups[1].Value))
                {
                    return true;
                }
            }

            foreach (Match c in Callee.Matches(lines[i]))
            {
                if (MethodWritesCollection(all, c.Groups[1].Value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>按名字找这个方法的定义体，看它里面有没有集合写入。</summary>
    private static bool MethodWritesCollection(
        List<(string Path, string[] Lines)> all, string method)
    {
        var head = new Regex(
            @"^\s{4,}(?:(?:private|internal|public|protected|static|sealed|override|virtual|async)\s+)+" +
            @"[\w<>\[\],\.\?]+\s+" + Regex.Escape(method) + @"\s*\(", RegexOptions.Compiled);

        foreach (var f in all)
        {
            foreach (var span in ClassSpans(f.Lines))
            {
                for (var i = span.Start; i <= span.End && i < f.Lines.Length; i++)
                {
                    if (!head.IsMatch(f.Lines[i]))
                    {
                        continue;
                    }

                    for (var k = i; k <= span.End && k < f.Lines.Length; k++)
                    {
                        foreach (Match m in CollectionMutate.Matches(f.Lines[k]))
                        {
                            if (CollectionNames.Contains(m.Groups[1].Value))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    private enum DemoCollectionFlaw
    {
        None,
        Silenced,
        NoGuard,
        MountOnly,
        Registered,
        HelperGuarded,
        HelperUnguarded,
    }

    /// <summary>第十道的合成样本。</summary>
    private static string[] DemoCollection(DemoCollectionFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoCollectionHandler : ElementHandler<DemoElement, DemoCombo>",
            "{",
            "    private static readonly EchoGuard SelectionEcho = new();",
            "    private static readonly WeakTable<DemoCombo, bool> Rebuilding = new();",
            string.Empty,
            "    protected override DemoCombo Mount(Reconciler reconciler, DemoElement element)",
            "    {",
            "        var control = new DemoCombo();",
            "        control.Items.Add(\"a\");",
            "        return control;",
            "    }",
        };

        if (flaw == DemoCollectionFlaw.MountOnly)
        {
            lines.Add("}");
            return lines.ToArray();
        }

        if (flaw == DemoCollectionFlaw.Registered)
        {
            // 类名换成 Reconciler，属性名换成 Children —— 走（类名, 属性名）那条登记。
            lines[0] = "internal sealed class Reconciler";
            lines.Add("    private static void PatchChildren(DemoCombo control)");
            lines.Add("    {");
            lines.Add("        control.Children.Add(1);");
            lines.Add("    }");
            lines.Add("}");
            return lines.ToArray();
        }

        var helper = flaw is DemoCollectionFlaw.HelperGuarded or DemoCollectionFlaw.HelperUnguarded;

        if (helper)
        {
            // 名字刻意不叫 ReplaceItems：调用点那层是<b>按名字</b>查的，
            // 用真源码里已有的名字会串到 ComboBoxHandler 那份上去。
            lines.Add("    private static void DemoReplaceItems(DemoCombo control)");
            lines.Add("    {");
            lines.Add("        control.Items.Clear();");
            lines.Add("    }");
        }

        lines.Add(string.Empty);
        lines.Add("    protected override void Update(");
        lines.Add("        Reconciler reconciler, DemoElement o, DemoElement n, DemoCombo control)");
        lines.Add("    {");

        if (helper)
        {
            lines.Add(flaw == DemoCollectionFlaw.HelperGuarded
                ? "        Rebuilding.Set(control, true);"
                : "        var nothing = 0;");
            lines.Add("        DemoReplaceItems(control);");
            lines.Add(flaw == DemoCollectionFlaw.HelperGuarded
                ? "        Rebuilding.Set(control, false);"
                : string.Empty);
        }
        else
        {
            switch (flaw)
            {
                case DemoCollectionFlaw.None:
                    lines.Add("        Rebuilding.Set(control, true);");
                    lines.Add("        control.Items.Clear();");
                    lines.Add("        Rebuilding.Set(control, false);");
                    break;

                case DemoCollectionFlaw.Silenced:
                    lines.Add("        using (SelectionEcho.Silence(control))");
                    lines.Add("        {");
                    lines.Add("            control.Items.Clear();");
                    lines.Add("        }");
                    break;

                default:
                    lines.Add("        control.Items.Clear();");
                    break;
            }
        }

        lines.Add("    }");
        lines.Add("}");
        return lines.ToArray();
    }

    private enum DemoAnyFlaw
    {
        None,
        NoWindow,
        Registered,
        NoEchoGuard,
        MountOnly,

        /// <summary>写在<b>另一个</b>类里（类没登记），用来验豁免的颗粒度。</summary>
        OtherClass,
    }

    /// <summary>第九道的合成样本。</summary>
    private static List<string> DemoAnyWrite(DemoAnyFlaw flaw)
    {
        var guard = flaw == DemoAnyFlaw.NoEchoGuard
            ? "    private static readonly WeakTable<DemoSlider, int> States = new();"
            : "    private static readonly EchoGuard ValueEcho = new();";

        // OtherClass 那一档换一个类名：属性照旧写 Header，但类名没登记过。
        var owner = flaw == DemoAnyFlaw.OtherClass ? "DemoOtherHandler" : "DemoAnyHandler";

        var lines = new List<string>
        {
            $"internal sealed class {owner} : ElementHandler<DemoElement, DemoSlider>",
            "{",
            guard,
            string.Empty,
            "    protected override void Update(",
            "        Reconciler reconciler, DemoElement o, DemoElement n, DemoSlider control)",
            "    {",
        };

        if (flaw == DemoAnyFlaw.None)
        {
            lines.Add("        using (ValueEcho.Silence(control))");
            lines.Add("        {");
            lines.Add("            control.StepFrequency = n.Step;");
            lines.Add("        }");
        }
        else if (flaw == DemoAnyFlaw.Registered || flaw == DemoAnyFlaw.OtherClass)
        {
            lines.Add("        PropWriter.Set(o.Header, n.Header, value => control.Header = value);");
        }
        else if (flaw == DemoAnyFlaw.NoWindow)
        {
            lines.Add("        control.StepFrequency = n.Step;");
        }

        lines.Add("    }");

        if (flaw == DemoAnyFlaw.MountOnly)
        {
            lines.Add(string.Empty);
            lines.Add("    protected override DemoSlider Mount(Reconciler reconciler, DemoElement element)");
            lines.Add("    {");
            lines.Add("        var native = new DemoSlider();");
            lines.Add("        native.StepFrequency = element.Step;");
            lines.Add("        return native;");
            lines.Add("    }");
        }

        lines.Add("}");
        return lines;
    }

    /// <summary>这个窗罩的是不是 <c>Minimum</c> / <c>Maximum</c>（决定该由第八道还是第九道认领）。</summary>
    private static bool IsRangeWindow(IReadOnlyList<string> lines, int usingLine, int close)
    {
        for (var i = usingLine; i < close && i < lines.Count; i++)
        {
            if (RangeWrite.IsMatch(lines[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>静默窗那块（<c>using { … }</c>）结束之后的行号，供"把窗挪到写入之后"用。</summary>
    private static int WindowClose(IReadOnlyList<string> lines, int usingLine)
    {
        var indent = lines[usingLine].Length - lines[usingLine].TrimStart().Length;

        for (var i = usingLine + 1; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.Trim() == "}" &&
                line.Length - line.TrimStart().Length == indent)
            {
                return i + 1;
            }
        }

        return usingLine + 1;
    }

    /// <summary>第八道的合成样本。</summary>
    private static List<string> DemoRange(DemoRangeFlaw flaw)
    {
        var guard = flaw == DemoRangeFlaw.NoEchoGuard
            ? "    private static readonly WeakTable<DemoSlider, int> States = new();"
            : "    private static readonly EchoGuard ValueEcho = new();";

        var lines = new List<string>
        {
            "internal sealed class DemoRangeHandler : ElementHandler<DemoElement, DemoSlider>",
            "{",
            guard,
            string.Empty,
            "    protected override void Update(",
            "        Reconciler reconciler, DemoElement o, DemoElement n, DemoSlider control)",
            "    {",
        };

        var helper = flaw is DemoRangeFlaw.HelperSilenced or DemoRangeFlaw.HelperUnsilenced
            or DemoRangeFlaw.HelperMountOnly;

        var windowed = flaw is DemoRangeFlaw.None or DemoRangeFlaw.HelperSilenced;

        if (windowed)
        {
            lines.Add("        using (ValueEcho.Silence(control))");
            lines.Add("        {");
        }

        // Mount-only 那一档的 Update 不碰区间（否则它自己就是一个没罩窗的调用点）
        lines.Add(flaw == DemoRangeFlaw.HelperMountOnly
            ? "        PropWriter.Set(o.Header, n.Header, value => control.Header = value);"
            : helper
                ? "        ApplyRange(control, n);"
                : "        control.Minimum = n.Min;");

        if (windowed)
        {
            lines.Add("        }");
        }

        lines.Add("    }");

        if (helper)
        {
            lines.Add(string.Empty);
            lines.Add("    private static void ApplyRange(DemoSlider control, DemoElement e)");
            lines.Add("    {");
            lines.Add("        control.Minimum = e.Min;");
            lines.Add("        control.Maximum = e.Max;");
            lines.Add("    }");
        }

        if (flaw is DemoRangeFlaw.HelperSilenced or DemoRangeFlaw.HelperMountOnly)
        {
            lines.Add(string.Empty);
            lines.Add("    protected override DemoSlider Mount(Reconciler reconciler, DemoElement element)");
            lines.Add("    {");
            lines.Add("        var native = new DemoSlider();");
            lines.Add("        ApplyRange(native, element);");
            lines.Add("        return native;");
            lines.Add("    }");
        }

        lines.Add("}");
        return lines;
    }

    private static List<string> StripComments(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);

        foreach (var line in lines)
        {
            var t = line.TrimStart();
            result.Add(t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*") ? string.Empty : line);
        }

        return result;
    }

    /// <summary>一个虚构的受控 handler，按 <paramref name="flaw"/> 弄坏一处。</summary>
    private static List<string> DemoSite(DemoSiteFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoPickerHandler : ElementHandler<DemoPickerElement, DemoPicker>",
            "{",
            "    protected override void Update(Reconciler r, DemoPickerElement o, DemoPickerElement n, DemoPicker control)",
            "    {",
            "        control.SelectedIndex = n.SelectedIndex;",
            "    }",
            "",
            "    private static void Rebind(DemoPicker control, Action<int>? callback)",
            "    {",
            "        control.SelectionChanged += (s, args) => callback?.Invoke(control.SelectedIndex);",
            "    }",
            "}",
        };

        switch (flaw)
        {
            case DemoSiteFlaw.NoChannel:
                lines.RemoveAll(l => l.Contains("SelectionChanged"));
                return lines;

            case DemoSiteFlaw.None:
                lines.InsertRange(4, new[]
                {
                    "        SelectionEcho.Expect(control, n.SelectedIndex);",
                });
                lines.InsertRange(6, new[]
                {
                    "        if (SelectionEcho.Consume(control, control.SelectedIndex)) return;",
                });
                lines.AddRange(new[]
                {
                    "",
                    "    protected override void Unmount(Reconciler r, DemoPicker control)",
                    "    {",
                    "        SelectionEcho.Forget(control);",
                    "    }",
                });
                return lines;

            default:
                return lines;
        }
    }

    /// <summary>把 <see cref="DemoSite"/> 弄坏的方式。</summary>
    private enum DemoSiteFlaw
    {
        None,
        NoGuard,
        NoChannel,
    }

    /// <summary>
    /// 第七道：<b>handler 之外</b>写用户可改属性的地方，必须逐一登记，并且登记里
    /// 写的"靠什么中和"必须在真实源码里真的还在。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 前六道的扫描口径都是 <c>Handlers.*.cs</c>——从来没人问过"为什么只扫这些文件"。
    /// 把口径扩到 <c>Reactor.uwp</c> 全树量一遍，多出来的就一处真洞口：
    /// <c>Localization.ApplyUid</c> 给 <c>x:Uid</c> 套 resw 里的 <c>Uid/Text</c>
    /// （<c>Localization.cs:83</c>）。它跑在 <c>Build</c> 的<b>最后</b>
    /// （<c>Reconciler.cs:102</c>），也就是 <c>handler.Mount</c> 已经订阅之后——
    /// 于是那一笔写抛出来的 <c>TextChanged</c> 没有任何回声登记（<c>ApplyUid</c>
    /// 拿不到 <c>TextBoxHandler</c> 的 <c>TextEcho</c>），会被当成用户输入回调出去：
    /// 界面上是"输入框的初始值被 resw 里的串顶掉，还多一次 <c>OnChanged</c>"。
    /// </para>
    /// <para>
    /// 这一道与第五道的分工：第五道管"<b>属性名</b>认不认得"（<c>EchoProne</c> 清单），
    /// 这一道管"<b>写回点在谁手里</b>"。同一个属性名在 handler 里写是受三件套保护的，
    /// 在 handler 外写则完全不在其余六道的视野内——<c>Text</c> 早在 <c>EchoProne</c>
    /// 里，第五道照样一声不吭。
    /// </para>
    /// <para>
    /// 判据刻意做成"实测写回点集合 <b>必须恰好等于</b>登记表"：多了报警（新洞口没人认领），
    /// 少了也报警（登记已经失效）。光登记一句话是不够的——所以每条登记还带一个
    /// <c>Anchor</c>（方法名 + 必须在方法体里匹配到的正则），删掉那段中和代码就会红。
    /// 这样"登记了但没修"也藏不住。
    /// </para>
    /// </remarks>
    /// <summary>
    /// 第十二道契约：<b>事件订阅必须幂等</b>，或者登记为"一次性"并写明理由。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 前十道全从"<b>写</b>"那一侧出发（往控件上写什么、写在什么窗里）。
    /// 这一道看的是另一半——<b>接</b>的那一侧：`X.Event += h`。
    /// </para>
    /// <para>
    /// 危险只在一种形状上：订阅点<b>每次 Patch 都会走到</b>，却没有幂等保护。
    /// `Rebind` 正是这种——它每次 Patch 都被调用，用来把回调换成捕获了新 state 的闭包。
    /// 里面一个裸 `+=` 就是每渲染一次叠一层，用户点一下回调跑 N 遍。
    /// 表现为"点了之后状态跳来跳去"，与受控值被写歪是同一类症状、不同入口。
    /// </para>
    /// <para>
    /// 幂等有两种写法，两种都认（只认一种会逼人改写法而不是改语义）：
    /// <b>(a) 表驱动</b>——`+=` 罩在 `if (!Table.ContainsKey(control))` 里，
    /// 只订一次、之后换表里的委托；<b>(b) 配对退订</b>——同一个方法体里有
    /// `<c>.同一个事件 -=</c>`（先摘旧的再挂新的，或挂一个自摘的一次性 handler）。
    /// </para>
    /// <para>
    /// 两种都不是的必须登记。注意 <b>(b) 的口径是"同一个方法内"</b>：
    /// `+=` 在 `Mount` 而 `-=` 在 `Unmount` <b>不算</b>——那条路径安全靠的是
    /// "Mount 每实例只跑一次"，不是靠配对，所以它走登记（<c>BreadcrumbBar</c> 正是这档）。
    /// 口径再放宽到"同一个类里"就废了：`+=` 在 `Update`、`-=` 只在 `Unmount`
    /// 这种真漏也会蒙混过关，而它正是这一道要拦的形状。
    /// </para>
    /// </remarks>
    private static void SubscriptionsAreIdempotent()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第十二道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var sites = parsed.Sum(f => SubscriptionSites(f.Lines).Count);

        var found = new List<(string File, string Event)>();

        foreach (var f in parsed)
        {
            foreach (var site in SubscriptionSites(f.Lines))
            {
                if (IsIdempotentSubscription(f.Lines, site))
                {
                    continue;
                }

                found.Add((Relative(f.Path), site.Event));
            }
        }

        var distinct = found.Distinct().OrderBy(t => t.File + "|" + t.Event, StringComparer.Ordinal).ToList();
        var ledger = SubscribeOnce.Select(e => (e.File, e.Event))
            .OrderBy(t => t.File + "|" + t.Event, StringComparer.Ordinal)
            .ToList();

        var unknown = distinct.Except(ledger).ToList();
        var stale = ledger.Except(distinct).ToList();

        Program.Check(
            $"定位到事件订阅点（扫描 {files.Count} 个文件，命中 {sites} 处）",
            sites >= 30,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            $"没有幂等保护的订阅点全部有人认领（实测 {distinct.Count} 处 / {found.Count} 个订阅点）",
            unknown.Count == 0 && stale.Count == 0 && distinct.Count > 0,
            (unknown.Count == 0 ? null :
                $"{unknown.Count} 处没人认领：" + string.Join("、", unknown.Select(t => $"{t.File} 的 {t.Event}")) + Environment.NewLine) +
            (stale.Count == 0 ? null :
                $"{stale.Count} 条登记已失效（源码里那个订阅点已经有保护了，或没了）：" +
                string.Join("、", stale.Select(t => $"{t.File} 的 {t.Event}"))));

        Program.Check(
            "合成样本：表驱动（ContainsKey 守卫）→ 不报警",
            UnwaivedSubscriptions(DemoSubscription(DemoSubFlaw.None)).Count == 0,
            "守卫块没被认出来 —— 真源码里 12 处订阅会被误报");

        Program.Check(
            "合成样本：先 -= 再 +=（换委托）→ 不报警",
            UnwaivedSubscriptions(DemoSubscription(DemoSubFlaw.Paired)).Count == 0,
            "配对退订没被认出来 —— 真源码里 15 处订阅会被误报");

        Program.Check(
            "合成样本：裸 +=（每次 Patch 叠一层）→ 必须报警",
            UnwaivedSubscriptions(DemoSubscription(DemoSubFlaw.Bare)).Count == 1,
            "裸订阅也照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：`+=` 在 Update、`-=` 只在 Unmount → 必须报警",
            UnwaivedSubscriptions(DemoSubscription(DemoSubFlaw.UnsubscribedElsewhere)).Count == 1,
            "配对退订的口径放到\"同一个类\"就废了：这正是要拦的形状（Patch 每次叠一层）");

        MutationsOfRealSubscriptions();
    }

    /// <summary>一段合成源码里，既没幂等保护、也没登记过的一次性订阅。</summary>
    private static List<string> UnwaivedSubscriptions(IReadOnlyList<string> lines)
    {
        var result = new List<string>();

        foreach (var site in SubscriptionSites(lines))
        {
            if (IsIdempotentSubscription(lines, site))
            {
                continue;
            }

            result.Add($"第 {site.Index + 1} 行  {site.Method}.{site.Event}");
        }

        return result;
    }

    /// <summary>
    /// 承重自查：<b>把真源码里每个订阅点的幂等保护拆掉，违约数必须增加</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 关键在"瞄准谁"。一开始我按"所有含 <c>ContainsKey</c> 的行"和"所有 <c>-=</c> 行"
    /// 两批去拆，立刻踩到两个坑：
    /// 一是<b>已豁免订阅的 <c>-=</c></b>（<c>BreadcrumbBar.Unmount</c> 那条）——
    /// 那个订阅点本来就是靠"Mount 只跑一次"过关的，拆它的 <c>-=</c> 当然不该有事；
    /// 二是<b><c>+=</c> 与 <c>-=</c> 在同一行</b>（<c>InputApplier.ApplyKeyEvents</c>
    /// 把两个委托当参数传进 <c>RebindKeyEvent</c>）——整行注释掉连订阅点一起没了，
    /// 违约数反而<b>下降</b>，照样判"没报"。
    /// </para>
    /// <para>
    /// 所以改成从订阅点反查"是谁在保护它"：守卫就改守卫那一行，
    /// 配对就把方法体内该事件的<b>全部</b> <c>-=</c> 一起改掉（改一处留一处仍然成对，
    /// 会假绿）。拆法是改字符串、不删行——删行会让括号配平塌掉，
    /// 扫描器会把它读成"结构坏了"而不是"保护没了"。
    /// </para>
    /// <para>
    /// 计数跨文件求和：只在被改的那个文件里数，违约冒到别处就看不见了。
    /// </para>
    /// </remarks>
    private static void MutationsOfRealSubscriptions()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            return;
        }

        var waived = SubscribeOnce.Select(e => (e.File, e.Event)).ToList();
        var guardJobs = new List<(string Path, int Line, string Label)>();
        var pairJobs = new List<(string Path, List<int> Lines, string Label)>();
        var guardSeen = new HashSet<string>(StringComparer.Ordinal);
        var pairSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path)).ToArray();
            var depths = Depths(lines);
            var rel = Relative(path);

            foreach (var site in SubscriptionSites(lines))
            {
                if (waived.Contains((rel, site.Event)))
                {
                    continue;
                }

                var guard = GuardLine(lines, depths, site.Index, site.Start);

                if (guard >= 0)
                {
                    if (guardSeen.Add($"{rel}:{guard}"))
                    {
                        guardJobs.Add((path, guard, $"{rel}:{guard + 1} 守卫 {site.Event}"));
                    }

                    continue;
                }

                var pairs = UnsubscribeLines(lines, site);

                if (pairs.Count == 0)
                {
                    continue;
                }

                var key = $"{rel}:{string.Join(",", pairs)}";

                if (pairSeen.Add(key))
                {
                    pairJobs.Add((path, pairs, $"{rel} 的 {site.Event} 配对退订（{pairs.Count} 行）"));
                }
            }
        }

        var before = SubscriptionViolationCount(files);
        var guardMissed = new List<string>();
        var pairMissed = new List<string>();

        foreach (var job in guardJobs)
        {
            var after = Mutate(
                job.Path,
                (i, l) => i == job.Line ? l.Replace("ContainsKey", "AlwaysTrue") : null);

            if (after <= before)
            {
                guardMissed.Add($"{job.Label}：拆掉守卫后违约数 {before} → {after}");
            }
        }

        foreach (var job in pairJobs)
        {
            var after = Mutate(
                job.Path,
                (i, l) => job.Lines.Contains(i) ? l.Replace("-=", "/*drop*/") : null);

            if (after <= before)
            {
                pairMissed.Add($"{job.Label}：拆掉退订后违约数 {before} → {after}");
            }
        }

        Program.Check(
            $"变异：拆掉真源码里的 ContainsKey 守卫（{guardJobs.Count} 处）都必须多报违约",
            guardJobs.Count >= 6 && guardMissed.Count == 0,
            guardMissed.Count == 0 ? null : string.Join(Environment.NewLine, guardMissed));

        Program.Check(
            $"变异：拆掉真源码里的配对退订（{pairJobs.Count} 处）都必须多报违约",
            pairJobs.Count >= 6 && pairMissed.Count == 0,
            pairMissed.Count == 0 ? null : string.Join(Environment.NewLine, pairMissed));
    }

    /// <summary>
    /// 按 <paramref name="rewrite"/> 改写若干行（返回 null 表示原样保留），
    /// 数一遍<b>全树</b>的订阅违约数，然后还原文件。
    /// </summary>
    /// <remarks>
    /// 改写一律用"替换行内字符串"，不整行注释掉：<c>+=</c> 与 <c>-=</c> 同在一行时，
    /// 整行干掉会连订阅点一起抹掉，违约数不升反降。
    /// </remarks>
    private static int Mutate(string path, Func<int, string, string?> rewrite) =>
        MutateCounted(path, rewrite, () => SubscriptionViolationCount(FrameworkFiles()))[0];

    /// <summary>
    /// 按 <paramref name="rewrite"/> 改写若干行（返回 null 表示原样保留），
    /// 跑一遍<b>全部</b>计数器，然后逐字节还原。
    /// </summary>
    /// <remarks>
    /// 计数器<b>可以有多个</b>：一条新契约要证明自己不是重复防线，
    /// 就得拿老契约去量同一批变异（见第十四道）。
    /// </remarks>
    private static int[] MutateCounted(
        string path, Func<int, string, string?> rewrite, params Func<int>[] counters)
    {
        // 还原必须<b>逐字节</b>：`File.WriteAllLines` 一律按 `Environment.NewLine` 重拼，
        // 而这个仓库是混合换行的（`Core/`、`Elements/` 里有一批裸 LF 的文件）。
        // 哪天有人往这种文件里加一个订阅，跑一次测试就会把整个文件悄悄改成 CRLF，
        // 于是 `git status` 把整个文件显示为已改 —— 与"变异没还原"看起来一模一样。
        var original = File.ReadAllBytes(path);
        var eol = original.AsSpan().IndexOf((byte)'\r') >= 0 ? "\r\n" : "\n";
        var lines = File.ReadAllLines(path);

        try
        {
            var mutated = new List<string>(lines.Length);

            for (var i = 0; i < lines.Length; i++)
            {
                mutated.Add(rewrite(i, lines[i]) ?? lines[i]);
            }

            File.WriteAllText(path, string.Join(eol, mutated));
            return counters.Select(c => c()).ToArray();
        }
        finally
        {
            File.WriteAllBytes(path, original);
        }
    }

    /// <summary>全树里"既没幂等保护也没登记"的订阅点数。</summary>
    private static int SubscriptionViolationCount(IReadOnlyList<string> files)
    {
        var waived = SubscribeOnce.Select(e => (e.File, e.Event)).ToList();
        var total = 0;

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path)).ToArray();

            foreach (var site in SubscriptionSites(lines))
            {
                if (IsIdempotentSubscription(lines, site))
                {
                    continue;
                }

                if (waived.Contains((Relative(path), site.Event)))
                {
                    continue;
                }

                total++;
            }
        }

        return total;
    }

    /// <summary>
    /// 第十二道的豁免登记：<b>一次性</b>的订阅点（所在方法每个控件实例至多跑一次）。
    /// </summary>
    /// <remarks>
    /// 登记门槛与第九道一样：<b>说得出"为什么它不会被叠第二次"</b>。
    /// 这套表是"实测集合必须恰好等于登记表"，所以既漏不掉新的、也塞不进没用的。
    /// </remarks>
    private static readonly (string File, string Event, string Reason)[] SubscribeOnce =
    {
        ("Hosting/ReactorApplication.cs", "Closed",
            "应用级：OnLaunched 里订一次，生命周期与进程同长，没有 Patch 路径"),
        ("Hosting/ReactorApplication.cs", "LayoutMetricsChanged",
            "应用级：ExtendIntoTitleBar 里订一次；标题栏只铺一次"),
        ("Hosting/ReactorHost.cs", "ActualThemeChanged",
            "宿主构造里订一次，宿主进程内只有一个"),
        ("Hosting/ReactorHost.cs", "MapChanged",
            "宿主构造里订一次；同上"),
        ("Internal/Handlers.Template.cs", "ItemClicked",
            "BreadcrumbBar：`+=` 在 Mount（每实例一次），`-=` 在 Unmount。"
            + "它安全靠的是 Mount 只跑一次，不是靠配对——所以登记而不是算幂等"),
        ("Internal/Handlers.Virtual.cs", "Loaded",
            "自绘（Canvas）路径：Mount 里订一次，用于首次布局拿到 Viewport"),
        ("Internal/Handlers.Virtual.cs", "SizeChanged",
            "自绘路径：Mount 里订一次；同上"),
        ("Internal/Handlers.Virtual.cs", "ViewChanged",
            "自绘路径：Mount 里订一次；同上"),
    };

    /// <summary>`X.Event +=` 的订阅点（含所在方法的行范围，便于查配对退订）。</summary>
    private static List<(int Index, string Event, string Method, int Start, int End)> SubscriptionSites(
        IReadOnlyList<string> lines)
    {
        var result = new List<(int, string, string, int, int)>();
        var depths = Depths(lines);

        for (var i = 0; i < lines.Count; i++)
        {
            var m = Subscribe.Match(lines[i]);

            if (!m.Success)
            {
                continue;
            }

            var (start, end, name) = EnclosingMethod(lines, depths, i);
            result.Add((i, m.Groups[1].Value, name, start, end));
        }

        return result;
    }

    /// <summary>订阅点是否幂等：表驱动守卫，或同一方法内有配对退订。</summary>
    private static bool IsIdempotentSubscription(
        IReadOnlyList<string> lines, (int Index, string Event, string Method, int Start, int End) site)
    {
        if (GuardLine(lines, Depths(lines), site.Index, site.Start) >= 0)
        {
            return true;
        }

        return UnsubscribeLines(lines, site).Count > 0;
    }

    /// <summary>方法体内给这个事件退订的行。</summary>
    private static List<int> UnsubscribeLines(
        IReadOnlyList<string> lines, (int Index, string Event, string Method, int Start, int End) site)
    {
        var unsub = new Regex(@"\." + Regex.Escape(site.Event) + @"\s*-=", RegexOptions.Compiled);
        var result = new List<int>();

        for (var i = site.Start; i <= site.End && i < lines.Count; i++)
        {
            if (unsub.IsMatch(lines[i]))
            {
                result.Add(i);
            }
        }

        return result;
    }

    /// <summary>
    /// 罩住 <paramref name="index"/> 的那个 <c>if (!某表.ContainsKey(...))</c> 所在行；没有则 -1。
    /// </summary>
    private static int GuardLine(
        IReadOnlyList<string> lines, int[] depths, int index, int floor)
    {
        for (var j = index - 1; j >= Math.Max(0, floor); j--)
        {
            if (!lines[j].Contains("ContainsKey") || !lines[j].Contains("!"))
            {
                continue;
            }

            // 这个 if 的块从哪一行的 `{` 开始。
            var open = -1;

            for (var k = j; k < Math.Min(j + 3, lines.Count); k++)
            {
                if (lines[k].Contains('{'))
                {
                    open = k;
                    break;
                }
            }

            if (open < 0 || depths[index] <= depths[j])
            {
                continue;
            }

            // 块得一路罩到这一行：中途回到 if 所在深度就说明已经出块了。
            var covered = true;

            for (var k = open + 1; k <= index; k++)
            {
                if (depths[k] <= depths[j])
                {
                    covered = false;
                    break;
                }
            }

            if (covered)
            {
                return j;
            }
        }

        return -1;
    }

    /// <summary>每行<b>起始</b>处的花括号深度。</summary>
    private static int[] Depths(IReadOnlyList<string> lines)
    {
        var result = new int[lines.Count];
        var depth = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            result[i] = depth;
            depth += Balance(lines[i]);
        }

        return result;
    }

    /// <summary>
    /// 包住第 <paramref name="index"/> 行的那个方法：签名行、方法体末行、方法名。
    /// </summary>
    /// <remarks>
    /// 认的是"往上第一个处在更外一层深度、且长得像方法签名"的行；
    /// 方法体末行是深度回到签名层、且净值为负的那一行（即方法自己的闭括号）。
    /// 嵌套块的闭括号深度更深，不会被误当成方法结尾。
    /// </remarks>
    private static (int Start, int End, string Name) EnclosingMethod(
        IReadOnlyList<string> lines, int[] depths, int index)
    {
        for (var j = index; j >= 0; j--)
        {
            if (depths[j] > depths[index] || !IsMethodSignature(lines[j]))
            {
                continue;
            }

            var name = MethodSignature.Match(lines[j]).Groups[1].Value;

            for (var k = j + 1; k < lines.Count; k++)
            {
                if (depths[k] == depths[j] + 1 && Balance(lines[k]) < 0)
                {
                    return (j, k, name);
                }
            }

            return (j, lines.Count - 1, name);
        }

        return (0, lines.Count - 1, "?");
    }

    private static bool IsMethodSignature(string line)
    {
        var t = line.TrimStart();

        if (t.Length == 0 || !MethodSignature.IsMatch(line))
        {
            return false;
        }

        // `if (...)` / `for (...)` / `catch (...)` 这些不是方法；`=> ` 是表达式体成员的调用行。
        foreach (var keyword in StatementKeywords)
        {
            if (t.StartsWith(keyword, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return !line.Contains("=>") && !line.Contains("new ") && !line.TrimEnd().EndsWith(";");
    }

    private static readonly string[] StatementKeywords =
    {
        "if ", "if(", "for ", "for(", "foreach ", "while ", "switch ", "catch ", "else", "do ", "try",
        "lock ", "using ", "fixed ", "return", "throw",
    };

    /// <summary>合成样本：一个 handler 的订阅点，按 <paramref name="flaw"/> 弄坏一处。</summary>
    private static List<string> DemoSubscription(DemoSubFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoSubHandler : ElementHandler<DemoSubElement, DemoSub>",
            "{",
            "    private static readonly WeakTable<DemoSub, Action<int>?> Callbacks = new();",
            "",
            "    protected override void Update(Reconciler r, DemoSubElement o, DemoSubElement n, DemoSub control)",
            "    {",
            flaw == DemoSubFlaw.UnsubscribedElsewhere
                ? "        control.Changed += OnChanged;"
                : "        Rebind(control, n.OnChanged);",
            "    }",
            "",
            "    protected override void Unmount(Reconciler r, DemoSub control)",
            "    {",
            "        control.Changed -= OnChanged;",
            "        Callbacks.Remove(control);",
            "    }",
            "",
            "    private static void Rebind(DemoSub control, Action<int>? callback)",
            "    {",
        };

        // 四种形状：守卫 / 换委托 / 裸订阅 / 订阅在 Update 而退订在 Unmount。
        if (flaw == DemoSubFlaw.None)
        {
            lines.Add("        if (!Callbacks.ContainsKey(control))");
            lines.Add("        {");
            lines.Add("            Callbacks[control] = null;");
            lines.Add("            control.Changed += OnChanged;");
            lines.Add("        }");
        }
        else if (flaw == DemoSubFlaw.Paired)
        {
            lines.Add("        control.Changed -= OnChanged;");
            lines.Add("        control.Changed += OnChanged;");
        }
        else if (flaw == DemoSubFlaw.Bare)
        {
            lines.Add("        Callbacks[control] = callback;");
            lines.Add("        control.Changed += OnChanged;");
        }
        else
        {
            // 订阅已经挪进 Update（每次 Patch 都走）了，这里只换回调。
            lines.Add("        Callbacks[control] = callback;");
        }

        lines.Add("    }");
        lines.Add("");
        lines.Add("    private static void OnChanged(object sender, DemoArgs args)");
        lines.Add("    {");
        lines.Add("    }");
        lines.Add("}");

        return lines;
    }

    private enum DemoSubFlaw
    {
        None,
        Paired,
        Bare,

        /// <summary>`+=` 在每次 Patch 都走的 Update 里，`-=` 只在 Unmount（不算配对）。</summary>
        UnsubscribedElsewhere,
    }

    // ── 第十三道：三件套的「相对次序」 ────────────────────────────────

    /// <summary>
    /// 第十三道契约：受控写入三件套必须按「<b>登记 → 下发 → 撤销</b>」排。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>前十二道漏掉的是什么。</b>第一道只问「<c>Expect</c> 之后有没有
    /// <c>CancelIfUnconsumed</c>」，第四道只问「三件套齐不齐」，
    /// 两道都<b>不看它排在下发之前还是之后</b>。而次序错了就是同一个 bug 的两副面孔：
    /// <list type="bullet">
    ///   <item><b>撤销排在下发之前</b>——那一发回声此刻还没到，撤销看见的是「没人领」，
    ///         于是把刚登记的期望<b>立刻抹掉</b>；等控件同步抛事件时表里已经没东西了
    ///         → <c>NotExpected</c> → 框架自己的写入被当成用户输入回调出去。</item>
    ///   <item><b>登记排在下发之后</b>——写入那一刻表里没有期望，同样
    ///         <c>NotExpected</c>，结局一模一样。</item>
    /// </list>
    /// 两副面孔的终点都是「state 被自己写的值顶一次」，表现就是抖动 / 覆盖输入框里
    /// 刚敲进去的字。而第一道在这两种源码下<b>照样全绿</b>——
    /// <c>LineIsAnswered</c> 只做「窗口里有没有这个串」的判断，位置一概不问。
    /// </para>
    /// <para>
    /// <b>判据</b>：对每个登记点，取它之后第一条同一个 guard 的撤销，
    /// 要求两者之间<b>存在一条以同一控件为接收者的下发</b>（<c>control.Prop = …</c>）。
    /// 没有撤销的不算次序违约——那是第一道的职责，两条分工，
    /// 免得同一处漏子被两个扫描器各数一遍、把变异计数搅浑。
    /// </para>
    /// <para>
    /// <b>认不出的边界（不假装能拦）</b>：这条契约认的是<b>相对位置</b>，
    /// 认不出「区间里那条下发到底是不是受控下发」。那一层由第四道
    /// （三件套齐全）与第五道（受控属性必须在 <c>EchoProne</c> 名单里）兜着。
    /// </para>
    /// </remarks>
    private static void EchoTripleIsOrdered()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第十三道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var sites = parsed.Sum(f => f.Lines.Count(l => ExpectSite.IsMatch(l)));

        var violations = new List<string>();

        foreach (var f in parsed)
        {
            foreach (var v in OrderingViolations(f.Lines))
            {
                violations.Add($"{Relative(f.Path)}:{v}");
            }
        }

        Program.Check(
            $"定位到受控登记点（扫描 {files.Count} 个文件，命中 {sites} 处）",
            sites >= 12,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            $"三件套的次序都是「登记 → 下发 → 撤销」（实测 {violations.Count} 处越位）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : "次序错了就是等不到回声 —— 框架自己的写入会被当成用户输入回调出去："
                  + Environment.NewLine + string.Join(Environment.NewLine, violations));

        Program.Check(
            "合成样本：登记 → 下发 → 撤销 → 不报警",
            OrderingViolations(DemoOrder(DemoOrderFlaw.None)).Count == 0,
            "正确次序被判违约 → 真源码里 12 处会被误报");

        Program.Check(
            "合成样本：撤销排在下发之前 → 必须报警",
            OrderingViolations(DemoOrder(DemoOrderFlaw.CancelBeforeWrite)).Count == 1,
            "撤销挪到下发之前却照样绿 —— 这条契约是摆设（第一道对此恰恰是瞎的）");

        Program.Check(
            "合成样本：登记排在下发之后 → 必须报警",
            OrderingViolations(DemoOrder(DemoOrderFlaw.ExpectAfterWrite)).Count == 1,
            "登记挪到下发之后却照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：区间里的下发不是同一个控件 → 必须报警",
            OrderingViolations(DemoOrder(DemoOrderFlaw.ForeignReceiver)).Count == 1,
            "不看接收者的话，写给别的控件的赋值会让越位蒙混过关");

        MutationsOfRealOrdering();
    }

    /// <summary>一段源码里次序越位的受控登记点。</summary>
    private static List<string> OrderingViolations(IReadOnlyList<string> lines)
    {
        var result = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var site = ExpectSite.Match(lines[i]);

            if (!site.Success)
            {
                continue;
            }

            var guard = site.Groups[1].Value;
            var receiver = site.Groups[2].Value;
            var cancel = CancelLine(lines, i, guard);

            // 没有撤销 → 第一道契约的职责（它报得比这里清楚），这里只管次序。
            if (cancel < 0)
            {
                continue;
            }

            if (!WriteBetween(lines, i, cancel, receiver))
            {
                result.Add(
                    $"第 {i + 1} 行 {guard}.Expect 与第 {cancel + 1} 行的撤销之间没有 {receiver} 的下发");
            }
        }

        return result;
    }

    /// <summary>第 <paramref name="index"/> 行那条登记，它的撤销在第几行（-1 = 没有）。</summary>
    private static int CancelLine(IReadOnlyList<string> lines, int index, string guard)
    {
        for (var j = index + 1; j < Math.Min(lines.Count, index + 1 + Window); j++)
        {
            if (lines[j].Contains($"{guard}.CancelIfUnconsumed("))
            {
                return j;
            }
        }

        return -1;
    }

    /// <summary>
    /// <c>(from, to)</c> 之间有没有一条<b>以 <paramref name="receiver"/> 为接收者</b>的下发。
    /// </summary>
    /// <remarks>
    /// 接收者必须比对：区间里一条写给<b>别的</b>控件的赋值（<c>other.Value = …</c>）
    /// 同样长得像下发，不比对就会把「受控下发其实在撤销之后」放过去。
    /// </remarks>
    private static bool WriteBetween(IReadOnlyList<string> lines, int from, int to, string receiver)
    {
        for (var j = from + 1; j < to; j++)
        {
            var write = WriteProp.Match(lines[j]);

            if (write.Success && write.Groups[1].Value == receiver)
            {
                return true;
            }
        }

        return false;
    }

    private static int OrderViolationCount(IReadOnlyList<string> files) =>
        files.Sum(p => OrderingViolations(StripComments(File.ReadAllLines(p)).ToArray()).Count);

    /// <summary>第一道的违约数（登记了没人领）。第十三道拿它做反向对照。</summary>
    private static int RegistrationViolationCount(IReadOnlyList<string> files)
    {
        var count = 0;

        foreach (var path in files)
        {
            var lines = File.ReadAllLines(path);

            for (var i = 0; i < lines.Length; i++)
            {
                if (!Expect.IsMatch(lines[i]) || LineIsAnswered(lines, i))
                {
                    continue;
                }

                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 承重自查：<b>把真源码里的「登记 / 撤销」与「下发」对调，次序违约数必须增加</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 变异手法是<b>对调两行</b>而不是删行：删行会让第一道（登记有没有人领）
    /// 一起红，就分不清「这条变异有没有牙」和「第十三道是不是重复防线」了。
    /// 对调之后三件套<b>一件不少</b>，只是位置变了——正是这条契约唯一盯的形状。
    /// </para>
    /// <para>
    /// 所以这里同时做<b>反向对照</b>：同一批变异，第一道必须<b>一条都看不见</b>。
    /// 它要是也报了，说明第十三道是重复的，不该单列。
    /// </para>
    /// </remarks>
    private static void MutationsOfRealOrdering()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            return;
        }

        var jobs = new List<(string Path, int Line, int Write, string Label)>();

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path)).ToArray();
            var rel = Relative(path);

            for (var i = 0; i < lines.Length; i++)
            {
                var site = ExpectSite.Match(lines[i]);

                if (!site.Success)
                {
                    continue;
                }

                var guard = site.Groups[1].Value;
                var receiver = site.Groups[2].Value;
                var cancel = CancelLine(lines, i, guard);

                if (cancel < 0)
                {
                    continue;
                }

                var write = -1;

                for (var j = i + 1; j < cancel; j++)
                {
                    var w = WriteProp.Match(lines[j]);

                    if (w.Success && w.Groups[1].Value == receiver)
                    {
                        write = j;
                        break;
                    }
                }

                if (write < 0)
                {
                    continue;
                }

                jobs.Add((path, i, write, $"{rel}:{i + 1} 登记↔下发"));
                jobs.Add((path, cancel, write, $"{rel}:{cancel + 1} 撤销↔下发"));
            }
        }

        var before = OrderViolationCount(files);
        var missed = new List<string>();
        var alsoSeenByFirst = new List<string>();

        foreach (var job in jobs)
        {
            var after = MutateSwap(
                job.Path,
                job.Line,
                job.Write,
                () => OrderViolationCount(FrameworkFiles()),
                () => RegistrationViolationCount(SourceFiles()));

            if (after[0] <= before)
            {
                missed.Add($"{job.Label}：对调后次序违约数 {before} → {after[0]}");
            }

            if (after[1] != 0)
            {
                alsoSeenByFirst.Add($"{job.Label}：第一道也报了 {after[1]} 处");
            }
        }

        Program.Check(
            $"变异：把真源码里的登记 / 撤销与下发对调（{jobs.Count} 处）都必须多报次序违约",
            jobs.Count >= 20 && missed.Count == 0,
            missed.Count == 0 ? null : string.Join(Environment.NewLine, missed));

        Program.Check(
            "反向对照：同一批变异，第一道（登记有没有人领）必须一条都看不见",
            jobs.Count > 0 && alsoSeenByFirst.Count == 0,
            alsoSeenByFirst.Count == 0
                ? null
                : "第一道也能抓到 → 第十三道是重复防线，不该单列："
                  + Environment.NewLine + string.Join(Environment.NewLine, alsoSeenByFirst));
    }

    /// <summary>
    /// 对调第 <paramref name="a"/>、<paramref name="b"/> 两行，跑一遍所有计数器，然后还原。
    /// </summary>
    private static int[] MutateSwap(string path, int a, int b, params Func<int>[] counters)
    {
        // 还原必须逐字节：见 Mutate 那段注释（混合换行仓库，WriteAllLines 会改坏整个文件）。
        var original = File.ReadAllBytes(path);
        var eol = original.AsSpan().IndexOf((byte)'\r') >= 0 ? "\r\n" : "\n";
        var lines = File.ReadAllLines(path);

        try
        {
            var mutated = new List<string>(lines);
            (mutated[a], mutated[b]) = (mutated[b], mutated[a]);
            File.WriteAllText(path, string.Join(eol, mutated));

            return counters.Select(c => c()).ToArray();
        }
        finally
        {
            File.WriteAllBytes(path, original);
        }
    }

    /// <summary>合成样本：一个 handler 的三件套，按 <paramref name="flaw"/> 排错一处。</summary>
    private static List<string> DemoOrder(DemoOrderFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoOrderHandler : ElementHandler<DemoOrderElement, DemoOrder>",
            "{",
            "    private static readonly EchoGuard DemoEcho = new();",
            "",
            "    protected override void Update(Reconciler r, DemoOrderElement o, DemoOrderElement n, DemoOrder control)",
            "    {",
        };

        switch (flaw)
        {
            case DemoOrderFlaw.None:
                lines.Add("        DemoEcho.Expect(control, n.Value);");
                lines.Add("        control.Value = n.Value;");
                lines.Add("        DemoEcho.CancelIfUnconsumed(control);");
                break;

            case DemoOrderFlaw.CancelBeforeWrite:
                lines.Add("        DemoEcho.Expect(control, n.Value);");
                lines.Add("        DemoEcho.CancelIfUnconsumed(control);");
                lines.Add("        control.Value = n.Value;");
                break;

            case DemoOrderFlaw.ExpectAfterWrite:
                lines.Add("        control.Value = n.Value;");
                lines.Add("        DemoEcho.Expect(control, n.Value);");
                lines.Add("        DemoEcho.CancelIfUnconsumed(control);");
                break;

            default:
                lines.Add("        DemoEcho.Expect(control, n.Value);");
                lines.Add("        other.Value = n.Value;");
                lines.Add("        DemoEcho.CancelIfUnconsumed(control);");
                break;
        }

        lines.Add("    }");
        lines.Add("");
        lines.Add("    protected override void Unmount(Reconciler r, DemoOrder control)");
        lines.Add("    {");
        lines.Add("        DemoEcho.Forget(control);");
        lines.Add("    }");
        lines.Add("}");

        return lines;
    }

    private enum DemoOrderFlaw
    {
        None,

        /// <summary>撤销排在下发之前：撤销那一刻回声还没到，等于把登记立刻抹掉。</summary>
        CancelBeforeWrite,

        /// <summary>登记排在下发之后：写入那一刻表里没有期望。</summary>
        ExpectAfterWrite,

        /// <summary>区间里的那条下发不是同一个控件。</summary>
        ForeignReceiver,
    }

    // ── 第十四道：读侧（回声判据的结果必须真的用于拦截） ──────────────

    /// <summary>第十四道用：整条 <c>X.Consume(…)</c> 调用，变异时要整段搬走。</summary>
    private static readonly Regex ConsumeCall =
        new(@"\w+\.Consume\([^)]*\)", RegexOptions.Compiled);

    /// <summary>
    /// 第十四道用：一个标识符被赋值（<c>verdict = Echo</c> 这类改判），带捕获组。
    /// </summary>
    private static readonly Regex Assignment = new(@"^\s*(\w+)\s*=(?!=)", RegexOptions.Compiled);

    /// <summary>
    /// 第十四道契约：回声判据的<b>结果</b>必须真的用于拦截——
    /// 既不能当独立语句丢掉，也不能判成回声之后照样往下走。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>第十三道管写侧，这一道管读侧。</b>三件套的「登记 → 下发 → 撤销」排对了，
    /// 事件回来那一侧照样可以全废：<c>Consume</c> 返回 <c>true</c> 表示
    /// 「这是我们自己写的回声，别回调用户」，而这个返回值
    /// <b>没有任何类型或编译器机制强迫你用它</b>。两种写法让它形同虚设：
    /// <list type="bullet">
    ///   <item><b>当独立语句调用</b>——<c>Echo.Consume(control, value);</c>
    ///         返回值直接扔掉，等于没判；</item>
    ///   <item><b>在 <c>if</c> 里判了却不拦</b>——<c>if (Echo.Consume(…)) { Log(); }</c>
    ///         然后照样 <c>callback(value);</c>。</item>
    /// </list>
    /// 两者的结果一模一样：<b>框架自己的写入每次都被当成用户输入回调出去</b>
    /// → <c>setState</c> → 重渲染 → 抖动 / 覆盖。
    /// </para>
    /// <para>
    /// <b>为什么第四道看不见。</b>第四道问的是「三件套齐不齐」，而这两种写法里
    /// <c>Consume</c> <b>就写在源码里</b>——字符串还在，第四道照样判绿。
    /// 它盯的是<b>存在性</b>，这一道盯的是<b>用法</b>。变异里做了两条反向对照把它钉死。
    /// </para>
    /// <para>
    /// <b>判据</b>：① 调用必须出现在 <c>if (</c> 的条件里；
    /// ② 那个 <c>if</c> 的体内必须有<b>停下来</b>的动作——<c>return</c>，
    /// 或改写判据（<c>verdict = SelectionVerdict.Echo</c> 那 4 处）。
    /// </para>
    /// </remarks>
    private static void EchoVerdictIsLoadBearing()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第十四道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var sites = parsed.Sum(f => f.Lines.Count(l => Consume.IsMatch(l)));

        var violations = new List<string>();

        foreach (var f in parsed)
        {
            foreach (var v in ConsumeViolations(f.Lines))
            {
                violations.Add($"{Relative(f.Path)}:{v}");
            }
        }

        Program.Check(
            $"定位到回声判据点（扫描 {files.Count} 个文件，命中 {sites} 处）",
            sites >= 12,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            $"每一处回声判据都真的用于拦截（实测 {violations.Count} 处形同虚设）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : "判了却不拦 = 框架自己的写入被当成用户输入回调出去："
                  + Environment.NewLine + string.Join(Environment.NewLine, violations));

        Program.Check(
            "合成样本：判成回声就 return → 不报警",
            ConsumeViolations(DemoConsume(DemoConsumeFlaw.None)).Count == 0,
            "最常见的写法被判违约 → 真源码里 8 处会被误报");

        Program.Check(
            "合成样本：判成回声后改判 verdict → 不报警",
            ConsumeViolations(DemoConsume(DemoConsumeFlaw.VerdictForm)).Count == 0,
            "改判那 4 处（ComboBox / ListView / RadioButtons 等）会被误报");

        Program.Check(
            "合成样本：当独立语句调用（返回值被丢弃）→ 必须报警",
            ConsumeViolations(DemoConsume(DemoConsumeFlaw.Discarded)).Count == 1,
            "返回值扔掉却照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：判了却不拦（体内只记日志）→ 必须报警",
            ConsumeViolations(DemoConsume(DemoConsumeFlaw.NoEarlyExit)).Count == 1,
            "if 里判了却照样往下走会漏过去 —— 返回值形式上有用、实际上没用");

        MutationsOfRealConsume();
    }

    /// <summary>一段源码里<b>没有真的用于拦截</b>的回声判据。</summary>
    private static List<string> ConsumeViolations(IReadOnlyList<string> lines)
    {
        var result = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            if (!Consume.IsMatch(lines[i]))
            {
                continue;
            }

            // 判据一：结果必须参与控制流，不能当独立语句丢掉。
            if (!lines[i].TrimStart().StartsWith("if (", StringComparison.Ordinal))
            {
                result.Add($"第 {i + 1} 行  {lines[i].Trim()} —— 回声判据的结果被丢弃");
                continue;
            }

            var (start, end) = IfBlock(lines, i);

            if (start < 0)
            {
                continue;
            }

            if (!StopsAfterEcho(lines, i, start, end))
            {
                result.Add($"第 {i + 1} 行  判成回声后没有停下来 —— {lines[i].Trim()}");
            }
        }

        return result;
    }

    /// <summary>
    /// 那个 <c>if</c> 的块体里有没有<b>真的停下来</b>：<c>return</c>，
    /// 或把<b>条件里出现过</b>的标识符改判掉（<c>verdict = Echo</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>只看"体内第一行"会假红。</b><c>ToggleSwitchHandler.Guard</c> 的写法是
    /// 先记一行 <c>ReactorLog.Gate(...)</c> 再 <c>return</c>——第一行不是
    /// <c>return</c>，判"没停下来"就误报了。所以要看<b>整个块体</b>。
    /// </para>
    /// <para>
    /// <b>只认"块内有赋值"又会假绿。</b>块里随便一句 <c>var tag = …</c>
    /// 也是赋值，把它算成"停下来"等于没查。所以改判必须改的是
    /// <b>条件表达式里出现过的那个标识符</b>——<c>verdict</c> 正是这种。
    /// </para>
    /// </remarks>
    private static bool StopsAfterEcho(IReadOnlyList<string> lines, int index, int start, int end)
    {
        var condition = lines[index];

        for (var j = start + 1; j < end; j++)
        {
            if (lines[j].Contains("return"))
            {
                return true;
            }

            var assigned = Assignment.Match(lines[j]);

            if (assigned.Success && condition.Contains(assigned.Groups[1].Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>第 <paramref name="index"/> 行那个 <c>if</c> 的块体范围（<c>{</c> 到配对的 <c>}</c>）。</summary>
    private static (int Start, int End) IfBlock(IReadOnlyList<string> lines, int index)
    {
        var brace = -1;

        for (var j = index; j < Math.Min(lines.Count, index + 4); j++)
        {
            if (lines[j].TrimEnd().EndsWith("{"))
            {
                brace = j;
                break;
            }
        }

        if (brace < 0)
        {
            return (-1, -1);
        }

        var depth = 0;

        for (var j = brace; j < lines.Count; j++)
        {
            depth += Balance(lines[j]);

            if (depth <= 0 && j > brace)
            {
                return (brace, j);
            }
        }

        return (brace, lines.Count - 1);
    }

    private static int ConsumeViolationCount(IReadOnlyList<string> files) =>
        files.Sum(p => ConsumeViolations(StripComments(File.ReadAllLines(p)).ToArray()).Count);

    /// <summary>第四道的违约数（三件套齐不齐）。第十四道拿它做反向对照。</summary>
    private static int FourthViolationCount(IReadOnlyList<string> files) =>
        files.Sum(p => ControlledWriteViolations(File.ReadAllLines(p)).Count);

    /// <summary>
    /// 承重自查：<b>把真源码里的回声判据弄成"判了不算数"，违约数必须增加</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两个方向各 12 处：① 整段搬进 <c>if</c> 体内当独立语句（返回值丢弃）；
    /// ② 把体内的 <c>return</c> / 改判换成一句日志（判了却不拦）。
    /// 两个方向都<b>不删 <c>Consume</c> 这个串</b>——它还在源码里。
    /// </para>
    /// <para>
    /// 于是可以同时做两条反向对照：
    /// <b>(a)</b> 同一批变异，第四道（三件套齐不齐）必须<b>一条都看不见</b>；
    /// <b>(b)</b> 反过来把 <c>Consume</c> <b>换名</b>（站点消失），第四道必须<b>立刻红</b>。
    /// 两条合起来说明：第四道盯存在性、这一道盯用法，互补而不重复。
    /// </para>
    /// </remarks>
    private static void MutationsOfRealConsume()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            return;
        }

        var jobs = new List<(string Path, int Line, int Brace, int End, string Call, string Label)>();

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path)).ToArray();
            var rel = Relative(path);

            for (var i = 0; i < lines.Length; i++)
            {
                // 只挑当前合规的站点：已经在报警的不需要再变异。
                if (!Consume.IsMatch(lines[i]) ||
                    !lines[i].TrimStart().StartsWith("if (", StringComparison.Ordinal))
                {
                    continue;
                }

                var call = ConsumeCall.Match(lines[i]).Value;
                var (brace, end) = IfBlock(lines, i);

                if (call.Length == 0 || brace < 0)
                {
                    continue;
                }

                jobs.Add((path, i, brace, end, call, $"{rel}:{i + 1}"));
            }
        }

        var before = ConsumeViolationCount(files);
        var missed = new List<string>();
        var seenByFourth = new List<string>();

        foreach (var job in jobs)
        {
            // 方向一：整段搬进 if 体内当独立语句（if 的条件换成 true）。
            var a = MutateCounted(
                job.Path,
                (i, l) => i == job.Line
                    ? Indent(l) + "if (true)"
                    : i == job.Brace
                        ? l + " " + job.Call + ";"
                        : null,
                () => ConsumeViolationCount(FrameworkFiles()),
                () => FourthViolationCount(SourceFiles()));

            if (a[0] <= before)
            {
                missed.Add($"{job.Label}：返回值被丢弃后违约数 {before} → {a[0]}");
            }

            if (a[1] != 0)
            {
                seenByFourth.Add($"{job.Label}：第四道也报了 {a[1]} 处");
            }

            // 方向二：把块体**整段**换成一句日志 —— 判了却不拦。
            // 只改第一行没用：ToggleSwitch 那处是「先记日志再 return」，
            // 改掉第一行后面的 return 还在，照样判合规。
            var b = MutateCounted(
                job.Path,
                (i, l) => i > job.Brace && i < job.End && l.Trim().Length > 0
                    ? Indent(l) + "ReactorLog.Gate(\"mutated\");"
                    : null,
                () => ConsumeViolationCount(FrameworkFiles()),
                () => FourthViolationCount(SourceFiles()));

            if (b[0] <= before)
            {
                missed.Add($"{job.Label}：判了却不拦，违约数 {before} → {b[0]}");
            }

            if (b[1] != 0)
            {
                seenByFourth.Add($"{job.Label}：第四道也报了 {b[1]} 处");
            }
        }

        Program.Check(
            $"变异：把真源码里的回声判据弄成「判了不算数」（{jobs.Count * 2} 处）都必须多报违约",
            jobs.Count >= 12 && missed.Count == 0,
            missed.Count == 0 ? null : string.Join(Environment.NewLine, missed));

        Program.Check(
            "反向对照：同一批变异，第四道（三件套齐不齐）必须一条都看不见",
            jobs.Count > 0 && seenByFourth.Count == 0,
            seenByFourth.Count == 0
                ? null
                : "第四道也能抓到 → 第十四道是重复防线，不该单列："
                  + Environment.NewLine + string.Join(Environment.NewLine, seenByFourth));

        // 对照 (b)：把 Consume 换名（站点消失），第四道必须立刻红 ——
        // 证明第四道盯的是"在不在"，不是"有没有被用"。
        var fourthBlind = new List<string>();

        foreach (var job in jobs)
        {
            var c = MutateCounted(
                job.Path,
                (i, l) => i == job.Line ? l.Replace(".Consume(", ".Peek(") : null,
                () => FourthViolationCount(SourceFiles()));

            if (c[0] == 0)
            {
                fourthBlind.Add($"{job.Label}：换名后第四道仍然 0 报警");
            }
        }

        Program.Check(
            $"反向对照：把 Consume 换名（{jobs.Count} 处），第四道必须逐一报警（它盯的是存在性）",
            fourthBlind.Count == 0,
            fourthBlind.Count == 0
                ? null
                : "第四道连「站点没了」都看不见 —— 那它根本没在管三件套："
                  + Environment.NewLine + string.Join(Environment.NewLine, fourthBlind));
    }

    /// <summary>合成样本：一个事件回调，按 <paramref name="flaw"/> 把回声判据写废。</summary>
    private static List<string> DemoConsume(DemoConsumeFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoConsumeHandler : ElementHandler<DemoConsumeElement, DemoConsume>",
            "{",
            "    private static readonly EchoGuard DemoEcho = new();",
            "",
            "    private static void Rebind(DemoConsume control, Action<double>? callback)",
            "    {",
            "        control.Changed += (s, e) =>",
            "        {",
        };

        switch (flaw)
        {
            case DemoConsumeFlaw.None:
                lines.Add("            if (DemoEcho.Consume(control, e.Value))");
                lines.Add("            {");
                lines.Add("                return;");
                lines.Add("            }");
                break;

            case DemoConsumeFlaw.VerdictForm:
                lines.Add("            var verdict = SelectionVerdict.Pass;");
                lines.Add("            if (verdict == SelectionVerdict.Pass && DemoEcho.Consume(control, e.Value))");
                lines.Add("            {");
                lines.Add("                verdict = SelectionVerdict.Echo;");
                lines.Add("            }");
                break;

            case DemoConsumeFlaw.Discarded:
                lines.Add("            DemoEcho.Consume(control, e.Value);");
                break;

            default:
                lines.Add("            if (DemoEcho.Consume(control, e.Value))");
                lines.Add("            {");
                lines.Add("                ReactorLog.Gate(\"echo\");");
                lines.Add("            }");
                break;
        }

        lines.Add("");
        lines.Add("            callback?.Invoke(e.Value);");
        lines.Add("        };");
        lines.Add("    }");
        lines.Add("}");

        return lines;
    }

    private enum DemoConsumeFlaw
    {
        None,

        /// <summary>判成回声后改判 <c>verdict</c>（ComboBox / ListView 那 4 处的写法）。</summary>
        VerdictForm,

        /// <summary>当独立语句调用：返回值直接扔掉。</summary>
        Discarded,

        /// <summary>在 <c>if</c> 里判了，体内却只记一行日志，照样往下走。</summary>
        NoEarlyExit,
    }

    // ── 第十五道：开了的窗必须关，且关闭路径要扛异常 ──────────────────

    /// <summary>第十五道用：一个 bool 持续标记被<b>打开</b>（<c>X.Set(ctl, true)</c>）。</summary>
    private static readonly Regex FlagOpen =
        new(@"(\w+)\.Set\(\s*[A-Za-z_]\w*\s*,\s*true\s*\)", RegexOptions.Compiled);

    /// <summary>第十五道用：同一个标记被<b>关掉</b>（<c>X.Set(ctl, false)</c>）。</summary>
    private static readonly Regex FlagClose =
        new(@"(\w+)\.Set\(\s*[A-Za-z_]\w*\s*,\s*false\s*\)", RegexOptions.Compiled);

    /// <summary>第十五道用：<c>using (X.Silence(ctl))</c> 整行，变异时要拆掉 <c>using</c>。</summary>
    private static readonly Regex UsingWindow = new(@"^(\s*)using \((.+)\)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// 第十五道用：<b>语义上单向</b>的标记——开了本来就不该关，不归这条契约管。
    /// </summary>
    /// <remarks>
    /// 与第十道的惰性登记同构：<b>默认全管，例外要登记并写明理由</b>，
    /// 且实测集合必须<b>恰好等于</b>这张表（同时挡"漏登记"和"塞失效条目"）。
    /// </remarks>
    private static readonly (string File, string Flag, string Reason)[] OneWayFlags =
    {
        ("Internal/InputApplier.cs", "FocusRequested",
            "一次性幂等标记：请求过焦点就永远为真，关掉反而会在下次挂载时重复请求"),
        ("Internal/ReadyGate.cs", "Ready",
            "就绪位：Arm 之后单向置真，取消走的是 Disarm 整条摘除，不是置回 false"),
    };

    /// <summary>
    /// 第十五道契约：抑制用的窗<b>开了必须关</b>，而且关闭要扛得住异常。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这一道盯的是"关没关"，不是"罩没罩住"。</b>第八、九、十道问的都是
    /// 「这次写入有没有落在窗里」，它们<b>默认窗是会关的</b>。
    /// 而窗一旦开了不关，代价是<b>永久</b>的：
    /// <list type="bullet">
    ///   <item><c>Silence</c> 是个深度计数器，开一次不 <c>Dispose</c>，
    ///         该控件<b>之后所有用户输入</b>一律被判成"我们自己写的"→ 点了没反应；</item>
    ///   <item><c>Rebuilding</c> 是持续标记，开了不关，
    ///         该控件<b>之后所有选中事件</b>一律被吞。</item>
    /// </list>
    /// 两者都是"某个控件从此哑掉"，而且只在<b>前面抛过一次异常</b>时才现形——
    /// 所以关闭必须走 <c>finally</c>（或 <c>using</c> 的 <c>Dispose</c>），
    /// 不能走正常路径。
    /// </para>
    /// <para>
    /// <b>判据（两种形状）</b>：
    /// ① <c>X.Silence(ctl)</c> 必须包在 <c>using (</c> 里——<c>Dispose</c> 就是关窗；
    /// ② <c>Rebuilding.Set(ctl, true)</c> 的配对 <c>Set(ctl, false)</c>
    /// 必须落在 <c>finally</c> 块里。
    /// </para>
    /// <para>
    /// <b>单向标记要登记。</b>不是每个 <c>Set(ctl, true)</c> 都是窗：
    /// <c>FocusRequested</c> 是一次性幂等标记、<c>Ready</c> 是就绪位，
    /// 它们开了本来就不该关。所以<b>默认全管，例外登记</b>。
    /// </para>
    /// <para>
    /// <b>为什么第八、九道看不见。</b><c>SilenceOpen</c> 这个正则认的是
    /// <c>.Silence(</c> 这个串，<b>不认 <c>using</c></b>。把 <c>using</c> 拆掉之后，
    /// 写入在它们眼里<b>仍然被罩着</b>——所以"窗没关"这件事它们一条都报不出来
    /// （反向对照实测确认）。
    /// </para>
    /// </remarks>
    private static void SuppressionWindowsAreClosed()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第十五道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var windows = parsed.Sum(f => f.Lines.Count(l => SilenceOpen.IsMatch(l)));
        var flags = parsed.Sum(f => f.Lines.Count(l => FlagOpen.IsMatch(l)));

        var violations = new List<string>();
        var oneWay = new List<(string File, string Flag)>();

        foreach (var f in parsed)
        {
            foreach (var v in WindowViolations(f.Lines))
            {
                violations.Add($"{Relative(f.Path)}:{v}");
            }

            for (var i = 0; i < f.Lines.Length; i++)
            {
                var flag = FlagOpen.Match(f.Lines[i]);

                // Rebuilding 走上面的判据；其余的必须登记为单向。
                if (flag.Success && flag.Groups[1].Value != "Rebuilding")
                {
                    oneWay.Add((Relative(f.Path), flag.Groups[1].Value));
                }
            }
        }

        Program.Check(
            $"定位到抑制窗口（扫描 {files.Count} 个文件：{windows} 处静默窗 + {flags} 处标记开启）",
            windows >= 5 && flags >= 6,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            $"每一处抑制都关得掉，且关在 finally / Dispose 上（实测 {violations.Count} 处会漏）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : "窗开了不关 = 该控件从此永久处于被抑制状态（点了没反应）："
                  + Environment.NewLine + string.Join(Environment.NewLine, violations));

        var distinct = oneWay.Distinct().OrderBy(t => t.File + "|" + t.Flag, StringComparer.Ordinal).ToList();
        var ledger = OneWayFlags.Select(e => (e.File, e.Flag))
            .OrderBy(t => t.File + "|" + t.Flag, StringComparer.Ordinal)
            .ToList();

        var unknown = distinct.Except(ledger).ToList();
        var stale = ledger.Except(distinct).ToList();

        Program.Check(
            $"开了不关的标记全部有人认领（实测 {distinct.Count} 个单向标记）",
            unknown.Count == 0 && stale.Count == 0 && distinct.Count > 0,
            (unknown.Count == 0 ? null :
                $"{unknown.Count} 个标记开了却不关、也没登记：" +
                string.Join("、", unknown.Select(t => $"{t.File} 的 {t.Flag}")) + Environment.NewLine) +
            (stale.Count == 0 ? null :
                $"{stale.Count} 条登记已失效（源码里那个标记已经不是单向的了，或没了）：" +
                string.Join("、", stale.Select(t => $"{t.File} 的 {t.Flag}"))));

        Program.Check(
            "合成样本：using 窗 + finally 关标记 → 不报警",
            WindowViolations(DemoWindow(DemoWindowFlaw.None)).Count == 0,
            "最常见的写法被判违约 → 真源码里 5 处窗、4 处标记会被误报");

        Program.Check(
            "合成样本：静默窗没包 using → 必须报警",
            WindowViolations(DemoWindow(DemoWindowFlaw.SilenceNotDisposed)).Count == 1,
            "窗开着不 Dispose 却照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：标记在正常路径上关（不在 finally）→ 必须报警",
            WindowViolations(DemoWindow(DemoWindowFlaw.FlagClosedOutsideFinally)).Count == 1,
            "写在正常路径上，中间一抛异常就永久留着 —— 正是这条要拦的形状");

        Program.Check(
            "合成样本：标记关在 catch 而不是 finally → 必须报警",
            WindowViolations(DemoWindow(DemoWindowFlaw.FlagClosedInCatch)).Count == 1,
            "catch 只覆盖抛异常那条路，正常返回时窗照样不关");

        // 反向对照：窗没关时，第八/九道的判据<b>仍然认这个窗</b> ——
        // 它们盯的是"写入有没有被罩住"，不是"窗有没有关"。
        var leaked = DemoWindow(DemoWindowFlaw.SilenceNotDisposed);
        var sound = DemoWindow(DemoWindowFlaw.None);
        var leakWrite = leaked.FindIndex(l => l.Contains("control.Minimum ="));
        var soundWrite = sound.FindIndex(l => l.Contains("control.Minimum ="));

        Program.Check(
            "反向对照：窗没关时，第八 / 九道的判据仍然认这个窗（它们盯的不是「关没关」）",
            leakWrite >= 0 && soundWrite >= 0 &&
            SilencedBefore(leaked, 0, leakWrite) && SilencedBefore(sound, 0, soundWrite),
            "第八 / 九道也看得见 → 第十五道是重复防线，不该单列");

        MutationsOfRealWindows();
    }

    /// <summary>一段源码里<b>关不掉</b>（或关得不够稳）的抑制。</summary>
    private static List<string> WindowViolations(IReadOnlyList<string> lines)
    {
        var result = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            if (SilenceOpen.IsMatch(lines[i]) &&
                !lines[i].TrimStart().StartsWith("using (", StringComparison.Ordinal))
            {
                result.Add($"第 {i + 1} 行  {lines[i].Trim()} —— 静默窗没有 using，开了不关");
            }

            var open = FlagOpen.Match(lines[i]);

            if (!open.Success || open.Groups[1].Value != "Rebuilding")
            {
                continue;
            }

            var close = -1;

            for (var k = i + 1; k < lines.Count; k++)
            {
                var shut = FlagClose.Match(lines[k]);

                if (shut.Success && shut.Groups[1].Value == open.Groups[1].Value)
                {
                    close = k;
                    break;
                }
            }

            if (close < 0)
            {
                result.Add($"第 {i + 1} 行  {lines[i].Trim()} —— 标记开了没关");
                continue;
            }

            if (!HasFinallyBetween(lines, i, close))
            {
                result.Add(
                    $"第 {i + 1} 行  标记的关闭在第 {close + 1} 行，但不在 finally 里 —— " +
                    "中间一抛异常就永久留着");
            }
        }

        return result;
    }

    /// <summary><c>(open, close)</c> 之间有没有 <c>finally</c>（关窗要走这条路才扛得住异常）。</summary>
    private static bool HasFinallyBetween(IReadOnlyList<string> lines, int open, int close)
    {
        for (var i = open + 1; i < close; i++)
        {
            if (lines[i].TrimStart().StartsWith("finally", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int WindowViolationCount(IReadOnlyList<string> files) =>
        files.Sum(p => WindowViolations(StripComments(File.ReadAllLines(p)).ToArray()).Count);

    /// <summary>
    /// 承重自查：<b>把真源码里的关窗路径拆掉，违约数必须增加</b>。
    /// </summary>
    /// <remarks>
    /// 两个方向：<c>using</c> 拆掉（5 处窗不再 <c>Dispose</c>）、
    /// <c>finally</c> 换成 <c>catch</c>（4 处标记只在抛异常时关）。
    /// 都不删掉 <c>.Silence(</c> / <c>Rebuilding.Set(</c> 这两个<b>串</b>本身——
    /// 第八、九道正是靠它们认窗的，串还在，它们就照样判绿。
    /// </remarks>
    private static void MutationsOfRealWindows()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            return;
        }

        var silenceJobs = new List<(string Path, int Line, string Label)>();
        var finallyJobs = new List<(string Path, int Line, string Label)>();

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path)).ToArray();
            var rel = Relative(path);

            for (var i = 0; i < lines.Length; i++)
            {
                if (SilenceOpen.IsMatch(lines[i]) &&
                    lines[i].TrimStart().StartsWith("using (", StringComparison.Ordinal))
                {
                    silenceJobs.Add((path, i, $"{rel}:{i + 1} 拆掉 using"));
                }
            }

            for (var i = 0; i < lines.Length; i++)
            {
                if (!RebuildOpen.IsMatch(lines[i]))
                {
                    continue;
                }

                var close = -1;

                for (var k = i + 1; k < lines.Length; k++)
                {
                    if (RebuildClose.IsMatch(lines[k]))
                    {
                        close = k;
                        break;
                    }
                }

                if (close < 0)
                {
                    continue;
                }

                for (var k = i + 1; k < close; k++)
                {
                    if (lines[k].TrimStart().StartsWith("finally", StringComparison.Ordinal))
                    {
                        finallyJobs.Add((path, k, $"{rel}:{k + 1} finally→catch"));
                    }
                }
            }
        }

        var before = WindowViolationCount(files);
        var missed = new List<string>();

        foreach (var job in silenceJobs)
        {
            var after = MutateCounted(
                job.Path,
                (i, l) => i == job.Line ? UsingWindow.Replace(l, "$1$2;") : null,
                () => WindowViolationCount(FrameworkFiles()));

            if (after[0] <= before)
            {
                missed.Add($"{job.Label}：拆掉 using 后违约数 {before} → {after[0]}");
            }
        }

        foreach (var job in finallyJobs)
        {
            var after = MutateCounted(
                job.Path,
                (i, l) => i == job.Line ? l.Replace("finally", "catch") : null,
                () => WindowViolationCount(FrameworkFiles()));

            if (after[0] <= before)
            {
                missed.Add($"{job.Label}：finally 换成 catch 后违约数 {before} → {after[0]}");
            }
        }

        Program.Check(
            $"变异：拆掉真源码里的关窗路径（{silenceJobs.Count} 处 using + " +
            $"{finallyJobs.Count} 处 finally）都必须多报违约",
            silenceJobs.Count >= 5 && finallyJobs.Count >= 4 && missed.Count == 0,
            missed.Count == 0 ? null : string.Join(Environment.NewLine, missed));
    }

    // ── 第十六道：三件套必须作用在<b>同一个控件</b>上 ──────────────────

    /// <summary>
    /// 第十六道用：<c>X.Expect(ctl, …)</c> / <c>X.Consume(ctl, …)</c> / <c>X.Forget(ctl)</c>
    /// 这三处——取表名、角色、以及<b>第一个参数</b>（控件）。
    /// </summary>
    private static readonly Regex EchoParty =
        new(@"(\w+)\.(Expect|Consume|Forget)\(\s*([A-Za-z_]\w*)", RegexOptions.Compiled);

    /// <summary>
    /// 第十六道契约：同一张回声表的 <c>Expect</c> / <c>Consume</c> / <c>Forget</c>
    /// 三处，传进去的必须是<b>同一个控件</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这一道盯的是"三件套作用在谁身上"，不是"三件套齐不齐"。</b>
    /// 第二、四道数的是<b>表名</b>（<c>TextEcho</c> 有没有人 <c>Consume</c>、有没有
    /// 在 <c>Unmount</c> 里 <c>Forget</c>）；第十三道比对的是<b>下发语句的接收者</b>
    /// （<c>control.Value = …</c> 里那个 <c>control</c>）。
    /// <b>没有任何一道看过 <c>Consume</c> 括号里的第一个参数。</b>
    /// </para>
    /// <para>
    /// 而写错这个参数是<b>静默失效</b>：<c>Consume</c> 靠「登记值 == 回读值」判回声，
    /// <b>键是控件</b>。键不同 → 表里查不到 → 一律 <c>NotExpected</c> →
    /// 框架自己写的每一次都被当成用户输入回调出去。症状与"压根没写 <c>Consume</c>"
    /// 一模一样，但代码上三件套一件不少——正是第二、四道全绿却仍在抖动的那一类。
    /// </para>
    /// <para>
    /// <b>判据</b>：按类、按表收集三件套各自的控件参数，要求它们的<b>并集只有一个元素</b>，
    /// 即三者都作用在同一个控件上。典型的错法是嵌套 handler 里
    /// <c>control</c> 与 <c>parent</c> / <c>sender</c> 混用，或复制粘贴改了一半。
    /// </para>
    /// <para>
    /// <b>认不出的边界（不假装能拦）</b>：按<b>类</b>切分，所以"登记在 A 类、
    /// 消费在 B 类"这种跨类的写法看不见（当前源码没有这种写法）。
    /// 另外它只比参数<b>名字</b>，不追别名——<c>var c = control;</c> 之后用
    /// <c>c</c> 登记，这条认不出来。
    /// </para>
    /// </remarks>
    private static void EchoPartiesShareOneControl()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第十六道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var sites = parsed.Sum(f => f.Lines.Count(l => EchoParty.IsMatch(l)));

        var violations = new List<string>();

        foreach (var f in parsed)
        {
            foreach (var v in PartyViolations(f.Lines))
            {
                violations.Add($"{Relative(f.Path)}:{v}");
            }
        }

        Program.Check(
            $"定位到回声三件套（扫描 {files.Count} 个文件：{sites} 处）",
            sites >= 36,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            $"三件套都作用在<b>同一个</b>控件上（实测 {violations.Count} 处不是）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : "键不同 → 表里永远查不到 → 框架自己的写入全被当成用户输入回调出去："
                  + Environment.NewLine + string.Join(Environment.NewLine, violations));

        Program.Check(
            "合成样本：三个角色都用同一个控件 → 不报警",
            PartyViolations(DemoParty(DemoPartyFlaw.None)).Count == 0,
            "最常见的写法被判违约 → 真源码里 12 组三件套会被误报");

        Program.Check(
            "合成样本：Consume 传的是另一个控件 → 必须报警",
            PartyViolations(DemoParty(DemoPartyFlaw.ConsumeOtherControl)).Count == 1,
            "键不同却照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：Expect 登记在另一个控件上 → 必须报警",
            PartyViolations(DemoParty(DemoPartyFlaw.ExpectOtherControl)).Count == 1,
            "登记错了控件，三件套照样齐全，第二、四道看不见");

        Program.Check(
            "合成样本：Forget 清的是另一个控件 → 必须报警",
            PartyViolations(DemoParty(DemoPartyFlaw.ForgetOtherControl)).Count == 1,
            "清的不是登记的那个 → 真正的那条登记永远留着");

        MutationsOfRealParties();
    }

    /// <summary>第十六道用：三件套<b>不是同一个控件</b>的地方（按类、按表）。</summary>
    private static List<string> PartyViolations(IReadOnlyList<string> lines)
    {
        var result = new List<string>();
        var heads = IndicesOf(lines, TopClass);

        for (var h = 0; h < heads.Count; h++)
        {
            var start = heads[h];
            var end = h + 1 < heads.Count ? heads[h + 1] - 1 : lines.Count - 1;

            // 表 → 角色 → 控件参数集合。
            var byTable = new SortedDictionary<string,
                SortedDictionary<string, SortedSet<string>>>(StringComparer.Ordinal);

            for (var i = start; i <= Math.Min(end, lines.Count - 1); i++)
            {
                foreach (Match m in EchoParty.Matches(lines[i]))
                {
                    var table = m.Groups[1].Value;
                    var role = m.Groups[2].Value;

                    if (!byTable.TryGetValue(table, out var roles))
                    {
                        roles = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                        byTable[table] = roles;
                    }

                    if (!roles.TryGetValue(role, out var ctls))
                    {
                        ctls = new SortedSet<string>(StringComparer.Ordinal);
                        roles[role] = ctls;
                    }

                    ctls.Add(m.Groups[3].Value);
                }
            }

            foreach (var (table, roles) in byTable)
            {
                var all = new SortedSet<string>(StringComparer.Ordinal);

                foreach (var ctls in roles.Values)
                {
                    all.UnionWith(ctls);
                }

                if (all.Count <= 1)
                {
                    continue;
                }

                var name = TopClass.Match(lines[start]).Groups[1].Value;
                var detail = string.Join("、",
                    roles.Select(r => $"{r.Key} 作用在 {JoinNames(r.Value)}"));

                result.Add($"第 {start + 1} 行  {name} 的 {table} —— 三件套不是同一个控件（{detail}）");
            }
        }

        return result;
    }

    private static string JoinNames(IEnumerable<string> names) => string.Join("、", names);

    private static int PartyViolationCount(IReadOnlyList<string> files) =>
        files.Sum(p => PartyViolations(StripComments(File.ReadAllLines(p)).ToArray()).Count);

    /// <summary>第二道的违约数（第十六道拿它做反向对照）。</summary>
    private static int LifetimeViolationCount(IReadOnlyList<string> files) =>
        files.Sum(p => LifetimeViolations(File.ReadAllLines(p)).Count);

    private static void MutationsOfRealParties()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            return;
        }

        var jobs = new List<(string Path, int Line, string Table, string Role, string Label)>();

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path)).ToArray();
            var rel = Relative(path);

            for (var i = 0; i < lines.Length; i++)
            {
                var m = EchoParty.Match(lines[i]);

                // 只改当前合规的站点（参数就是 control 的那些）。
                if (m.Success && m.Groups[3].Value == "control")
                {
                    jobs.Add((path, i, m.Groups[1].Value, m.Groups[2].Value,
                        $"{rel}:{i + 1} {m.Groups[1].Value}.{m.Groups[2].Value}(control→sender)"));
                }
            }
        }

        var before = PartyViolationCount(files);
        var lifetimeBefore = LifetimeViolationCount(SourceFiles());
        var fourthBefore = FourthViolationCount(SourceFiles());
        var missed = new List<string>();

        foreach (var job in jobs)
        {
            var head = job.Table + "." + job.Role + "(control";

            // 换掉第一个参数：表名、角色、三件套一件不少，只有控件口径变了。
            var after = MutateCounted(
                job.Path,
                (i, l) => i == job.Line ? l.Replace(head, job.Table + "." + job.Role + "(sender") : null,
                () => PartyViolationCount(FrameworkFiles()),
                () => LifetimeViolationCount(SourceFiles()),
                () => FourthViolationCount(SourceFiles()));

            if (after[0] <= before)
            {
                missed.Add($"{job.Label}：换掉控件后违约数 {before} → {after[0]}");
            }

            // 反向对照：表名与三件套都没动，第二、四道对这次变异不该有任何反应。
            if (after[1] != lifetimeBefore)
            {
                missed.Add($"{job.Label}：第二道对它报了 {lifetimeBefore} → {after[1]}（不该有反应）");
            }

            if (after[2] != fourthBefore)
            {
                missed.Add($"{job.Label}：第四道对它报了 {fourthBefore} → {after[2]}（不该有反应）");
            }
        }

        Program.Check(
            $"变异：把真源码里三件套的控件换掉（{jobs.Count} 处）都必须多报违约，" +
            "且第二、四道始终没反应",
            jobs.Count >= 36 && missed.Count == 0,
            missed.Count == 0 ? null : string.Join(Environment.NewLine, missed));
    }

    /// <summary>合成样本：一组回声三件套，按 <paramref name="flaw"/> 让其中一个作用在别的控件上。</summary>
    private static List<string> DemoParty(DemoPartyFlaw flaw)
    {
        var expect = flaw == DemoPartyFlaw.ExpectOtherControl ? "sender" : "control";
        var consume = flaw == DemoPartyFlaw.ConsumeOtherControl ? "sender" : "control";
        var forget = flaw == DemoPartyFlaw.ForgetOtherControl ? "sender" : "control";

        return new List<string>
        {
            "internal sealed class DemoPartyHandler : ElementHandler<DemoElement, DemoSlider>",
            "{",
            "    private static readonly EchoGuard ValueEcho = new();",
            "",
            "    protected override void Update(DemoElement newElement, DemoElement? oldElement, DemoSlider control)",
            "    {",
            $"        ValueEcho.Expect({expect}, newElement.Value);",
            "        control.Value = newElement.Value;",
            "    }",
            "",
            "    private static void OnValueChanged(DemoSlider sender, DemoSlider control)",
            "    {",
            $"        if (ValueEcho.Consume({consume}, control.Value))",
            "        {",
            "            return;",
            "        }",
            "    }",
            "",
            "    protected override void Unmount(Reconciler reconciler, DemoSlider control)",
            "    {",
            $"        ValueEcho.Forget({forget});",
            "    }",
            "}",
        };
    }

    private enum DemoPartyFlaw
    {
        None,

        /// <summary>消费的控件不是登记的那个。</summary>
        ConsumeOtherControl,

        /// <summary>登记在别的控件上。</summary>
        ExpectOtherControl,

        /// <summary>清理的控件不是登记的那个。</summary>
        ForgetOtherControl,
    }


    // ── 第十七道：登记的值必须就是下发下去的那个值 ────────────────

    /// <summary>第十七道用：<c>X.Expect(ctl, V)</c> —— 取表名、控件、<b>登记值</b>。</summary>
    private static readonly Regex ExpectValue =
        new(@"(\w+)\.Expect\(\s*([A-Za-z_]\w*)\s*,\s*([^,()]+?)\s*\)", RegexOptions.Compiled);

    /// <summary>第十七道用：整行赋值 <c>ctl.Prop = W;</c> —— 取接收者、属性、<b>右值</b>。</summary>
    private static readonly Regex Assign =
        new(@"^\s*([A-Za-z_]\w*)\.([A-Za-z_]\w*)\s*=(?!=)\s*(.+?);\s*$", RegexOptions.Compiled);

    /// <summary>
    /// 第十七道用：<b>间接但同口径</b>的写法——下发的值与登记的值不是同一个表达式，
    /// 两端却是同一个口径，不归这条契约管。
    /// </summary>
    /// <remarks>
    /// 与第十、十二、十五道同构：<b>默认全管，例外登记</b>，且实测集合必须
    /// <b>恰好等于</b>这张表（同时挡"漏登记"和"塞失效条目"）。
    /// </remarks>
    private static readonly (string File, string Property, string Reason)[] ValueBridges =
    {
        ("Internal/Handlers.Controls.cs", "SelectedItem",
            "NavigationView 只有 SelectedItem（对象）这一个写入口，登记的是下标；"
            + "回读侧 Guard 已把对象转回下标，两端仍是同一口径（且这一发本就没有回声）"),
    };

    /// <summary>
    /// 第十七道契约：<c>Expect</c> 登记下去的值，必须就是<b>实际下发</b>的那个值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这一道盯的是"值的口径"，第十三道盯的是"写的次序"。</b>
    /// 第十三道只要求「登记与撤销之间存在一条<b>以同一控件为接收者</b>的下发」，
    /// <b>完全不看下发的是什么值</b>。于是
    /// <c>Expect(control, a); control.Value = b;</c> 在它眼里完全合格。
    /// </para>
    /// <para>
    /// 而这是<b>静默失效</b>：<c>Consume</c> 靠「登记值 == 回读值」判回声，
    /// 回读的是下发之后控件<b>实际变成</b>的值（<c>b</c>），
    /// 登记的却是 <c>a</c> → 永远对不上 → 一律 <c>NotExpected</c> →
    /// 框架自己的每一次写入都被当成用户输入回调出去。三件套一件不少、次序也对的
    /// 情况下照样抖动——前十六道全绿。
    /// </para>
    /// <para>
    /// <b>判据</b>：对每处登记，取它与撤销之间<b>第一条</b>以该控件为接收者的下发，
    /// 要求右值与登记值<b>字符串相等</b>。间接口径（如 NavigationView 的
    /// <c>SelectedItem</c>）必须登记豁免。
    /// </para>
    /// <para>
    /// <b>为什么第十三道看不见。</b>它的 <c>WriteBetween</c> 只比对
    /// <c>WriteProp</c> 的<b>接收者</b>分组，右值根本没进正则。
    /// 把右值换掉之后，那条下发在它眼里<b>仍然在窗口里</b>（反向对照实测确认）。
    /// </para>
    /// </remarks>
    private static void ExpectationMatchesTheWrite()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第十七道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var expects = parsed.Sum(f => f.Lines.Count(l => ExpectValue.IsMatch(l)));

        var violations = new List<string>();
        var bridged = new List<(string File, string Property)>();

        foreach (var f in parsed)
        {
            var rel = Relative(f.Path);

            foreach (var v in ValueViolations(f.Lines, rel, bridged))
            {
                violations.Add($"{rel}:{v}");
            }
        }

        Program.Check(
            $"定位到受控登记（扫描 {files.Count} 个文件：{expects} 处）",
            expects >= 12,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            $"登记的值就是下发下去的那个值（实测 {violations.Count} 处对不上）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : "对不上 → 回声回来永远匹配不上 → 框架自己的写入全被当成用户输入回调出去："
                  + Environment.NewLine + string.Join(Environment.NewLine, violations));

        var distinct = bridged.Distinct()
            .OrderBy(t => t.File + "|" + t.Property, StringComparer.Ordinal).ToList();
        var ledger = ValueBridges.Select(e => (e.File, e.Property))
            .OrderBy(t => t.File + "|" + t.Property, StringComparer.Ordinal).ToList();

        var unknown = distinct.Except(ledger).ToList();
        var stale = ledger.Except(distinct).ToList();

        Program.Check(
            $"间接口径的写法全部有人认领（实测 {distinct.Count} 处）",
            unknown.Count == 0 && stale.Count == 0 && distinct.Count > 0,
            (unknown.Count == 0 ? null :
                $"{unknown.Count} 处登记值与下发值不同、也没登记：" +
                string.Join("、", unknown.Select(t => $"{t.File} 的 {t.Property}")) + Environment.NewLine) +
            (stale.Count == 0 ? null :
                $"{stale.Count} 条登记已失效（源码里那处已经改成同口径写法了）：" +
                string.Join("、", stale.Select(t => $"{t.File} 的 {t.Property}"))));

        Program.Check(
            "合成样本：登记值与下发值一致 → 不报警",
            ValueViolations(DemoValue(DemoValueFlaw.None), null, null).Count == 0,
            "最常见的写法被判违约 → 真源码里 11 处会被误报");

        Program.Check(
            "合成样本：下发的值不是登记的那个 → 必须报警",
            ValueViolations(DemoValue(DemoValueFlaw.DifferentValue), null, null).Count == 1,
            "值对不上却照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：登记值是成员表达式（源码里的真实形状）→ 不报警",
            ValueViolations(DemoValue(DemoValueFlaw.MemberExpression), null, null).Count == 0,
            "真实写法被判违约 → 真源码里 newElement.IsChecked.Value 那两处会被误报");

        MutationsOfRealValues();
    }

    /// <summary>
    /// 第十七道用：登记值与下发值<b>对不上</b>的地方。
    /// </summary>
    /// <param name="file">相对路径（合成样本传 <c>null</c>，此时不查豁免表）。</param>
    /// <param name="bridged">间接口径的实测收集器（可为 <c>null</c>）。</param>
    private static List<string> ValueViolations(
        IReadOnlyList<string> lines, string? file,
        List<(string File, string Property)>? bridged)
    {
        var result = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var m = ExpectValue.Match(lines[i]);

            if (!m.Success)
            {
                continue;
            }

            var guard = m.Groups[1].Value;
            var ctl = m.Groups[2].Value;
            var value = m.Groups[3].Value.Trim();
            var cancel = CancelLine(lines, i, guard);
            var limit = cancel < 0 ? Math.Min(lines.Count, i + 9) : cancel;

            for (var j = i + 1; j < limit; j++)
            {
                var w = Assign.Match(lines[j]);

                if (!w.Success || w.Groups[1].Value != ctl)
                {
                    continue;
                }

                var written = w.Groups[3].Value.Trim();

                if (written != value)
                {
                    if (file is not null &&
                        ValueBridges.Any(b => b.File == file && b.Property == w.Groups[2].Value))
                    {
                        bridged?.Add((file, w.Groups[2].Value));
                    }
                    else
                    {
                        result.Add(
                            $"第 {i + 1} 行  登记的是 {value}，下发到 {ctl}.{w.Groups[2].Value} 的却是 "
                            + $"{written}（回声回来时对不上 → 一律 NotExpected）");
                    }
                }

                break;
            }
        }

        return result;
    }

    private static int ValueViolationCount(IReadOnlyList<string> files) =>
        files.Sum(p => ValueViolations(
            StripComments(File.ReadAllLines(p)).ToArray(), Relative(p), null).Count);

    private static void MutationsOfRealValues()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            return;
        }

        var jobs = new List<(string Path, int Line, string Old, string New, string Label)>();

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path)).ToArray();
            var rel = Relative(path);

            for (var i = 0; i < lines.Length; i++)
            {
                var m = ExpectValue.Match(lines[i]);

                if (!m.Success)
                {
                    continue;
                }

                var guard = m.Groups[1].Value;
                var ctl = m.Groups[2].Value;
                var value = m.Groups[3].Value.Trim();
                var cancel = CancelLine(lines, i, guard);
                var limit = cancel < 0 ? Math.Min(lines.Length, i + 9) : cancel;

                for (var j = i + 1; j < limit; j++)
                {
                    var w = Assign.Match(lines[j]);

                    if (!w.Success || w.Groups[1].Value != ctl)
                    {
                        continue;
                    }

                    var written = w.Groups[3].Value.Trim();

                    // 豁免对象自己的配套代码不拆（第十二道那条教训）：它本来就是
                    // 间接口径，换掉右值<b>仍然是"不同"</b>，改了不承重 —— 假红。
                    var isBridge = written != value &&
                        ValueBridges.Any(b => b.File == rel && b.Property == w.Groups[2].Value);

                    if (!isBridge)
                    {
                        jobs.Add((path, j, written, written + "Mutated",
                            $"{rel}:{j + 1} {w.Groups[1].Value}.{w.Groups[2].Value} = {written}"));
                    }

                    break;
                }
            }
        }

        var before = ValueViolationCount(files);
        var orderBefore = OrderViolationCount(files);
        var missed = new List<string>();

        foreach (var job in jobs)
        {
            // 只换右值：接收者、属性、三件套、次序<b>一件都没动</b>。
            var after = MutateCounted(
                job.Path,
                (i, l) => i == job.Line ? l.Replace(job.Old + ";", job.New + ";") : null,
                () => ValueViolationCount(FrameworkFiles()),
                () => OrderViolationCount(FrameworkFiles()));

            if (after[0] <= before)
            {
                missed.Add($"{job.Label}：换掉下发的值后违约数 {before} → {after[0]}");
            }

            // 反向对照：第十三道只看接收者，右值换没换它看不见。
            if (after[1] != orderBefore)
            {
                missed.Add($"{job.Label}：第十三道对它报了 {orderBefore} → {after[1]}（不该有反应）");
            }
        }

        Program.Check(
            $"变异：把真源码里下发的值换掉（{jobs.Count} 处）都必须多报违约，" +
            "且第十三道始终没反应",
            jobs.Count >= 11 && missed.Count == 0,
            missed.Count == 0 ? null : string.Join(Environment.NewLine, missed));
    }

    /// <summary>合成样本：一处受控登记，按 <paramref name="flaw"/> 让下发的值对不上。</summary>
    private static List<string> DemoValue(DemoValueFlaw flaw)
    {
        var (expect, write) = flaw switch
        {
            DemoValueFlaw.DifferentValue => ("target", "other"),
            DemoValueFlaw.MemberExpression =>
                ("newElement.IsChecked.Value", "newElement.IsChecked.Value"),
            _ => ("target", "target"),
        };

        return new List<string>
        {
            "internal sealed class DemoValueHandler : ElementHandler<DemoElement, DemoSlider>",
            "{",
            "    private static readonly EchoGuard ValueEcho = new();",
            "",
            "    protected override void Update(DemoElement newElement, DemoElement? oldElement, DemoSlider control)",
            "    {",
            "        var target = newElement.Value;",
            "        var other = oldElement?.Value ?? 0;",
            $"        ValueEcho.Expect(control, {expect});",
            $"        control.Value = {write};",
            "        ValueEcho.CancelIfUnconsumed(control);",
            "    }",
            "",
            "    protected override void Unmount(Reconciler reconciler, DemoSlider control)",
            "    {",
            "        ValueEcho.Forget(control);",
            "    }",
            "}",
        };
    }

    private enum DemoValueFlaw
    {
        None,

        /// <summary>下发的值不是登记的那个。</summary>
        DifferentValue,

        /// <summary>登记值是成员表达式——源码里的真实形状，不该被判违约。</summary>
        MemberExpression,
    }

    // ── 第十八道：抑制窗必须罩在<b>它自己那个控件</b>上 ──────────────

    /// <summary>第十八道用：静默窗开在哪个控件上（<c>X.Silence(ctl)</c>）。</summary>
    private static readonly Regex SilenceCtl =
        new(@"\.Silence\(\s*([A-Za-z_]\w*)\s*\)", RegexOptions.Compiled);

    /// <summary>第十八道用：持续标记开在哪个控件上（<c>Rebuilding.Set(ctl, true)</c>）。</summary>
    private static readonly Regex FlagCtl =
        new(@"Rebuilding\.Set\(\s*([A-Za-z_]\w*)\s*,\s*true\s*\)", RegexOptions.Compiled);

    /// <summary>
    /// 第十八道用：集合动作 <c>X.Items.Clear()</c> / <c>X.MenuItems.Add(…)</c>。
    /// </summary>
    /// <remarks>
    /// 第十道的 <c>CollectionMutate</c> 只取了<b>属性名</b>一个分组，没有接收者——
    /// 而这一道要比对的正是接收者，所以另开一个。
    /// 持续标记罩住的多半是集合动作（重建列表）而不是赋值，少了这一半，
    /// 4 处标记里只有 1 处能被认出来。
    /// </remarks>
    private static readonly Regex CollectionAct = new(
        @"(?<![\w.])([A-Za-z_]\w*)\.(\w+)\s*(?:\.\s*(?:Clear|Add|Insert|Remove|RemoveAt|Move|ReplaceAll)\s*\(|=\s)",
        RegexOptions.Compiled);

    /// <summary>
    /// 第十八道用：把控件当<b>实参</b>交给 helper 去改（<c>PatchItems(control, …)</c> /
    /// <c>ApplyMenuItems(control, …)</c>）。
    /// </summary>
    /// <remarks>
    /// 抑制罩住的不一定是一条赋值——<c>Handlers.Controls.cs:1156</c> 那处罩的是
    /// <c>reconciler.PatchItems(control, …)</c>，改控件的活儿在 helper 里面。
    /// 少了这一半，4 处持续标记里有 1 处压根进不了视野。
    /// </remarks>
    private static readonly Regex CallWithArgs =
        new(@"(?<![\w])([A-Za-z_]\w*)\s*\((.*)\)", RegexOptions.Compiled);

    /// <summary>
    /// 第十八道用：<c>Xxx(y)</c> 里的 <c>Xxx</c> 是<b>关键字</b>而不是调用。
    /// </summary>
    /// <remarks>
    /// <c>if (modeChanged)</c> 长得跟调用一模一样，一次就把三处条件判断
    /// 误报成"在改别的控件"。按名字排掉——这些关键字本来就成不了 helper。
    /// </remarks>
    private static readonly HashSet<string> NotACall =
        new(new[] { "if", "while", "switch", "for", "foreach", "return", "catch", "lock", "using", "fixed", "do" },
            StringComparer.Ordinal);

    /// <summary>
    /// 第十八道契约：抑制（静默窗 / 持续标记）<b>罩住的写入，必须就是它自己那个控件</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这一道盯的是"窗罩在谁身上"，第八、九道盯的是"写入有没有落在窗里"。</b>
    /// 第八、九道用的 <c>SilencedBefore</c> 只认 <c>.Silence(</c> 这个<b>串</b>，
    /// <b>不看它开在哪个控件上</b>。于是 <c>using (X.Silence(other)) { control.Minimum = 0; }</c>
    /// 在它们眼里"这一发被罩住了"，而实际上那一发<b>根本没人管</b>——
    /// 控件照常抛事件，回声不被抑制 → 抖动。
    /// </para>
    /// <para>
    /// 这与第十六道（三件套的控件口径）是同一个形状换了个位置：
    /// 抑制是<b>按键查表</b>的，键错了就静默失效，而且失效得很安静——
    /// 代码上窗开着、写入在窗内，第八、九道全绿。
    /// </para>
    /// <para>
    /// <b>判据（两种形状）</b>：
    /// ① <c>using (X.Silence(A))</c> 的块内，所有写入的接收者必须是 <c>A</c>；
    /// ② <c>Rebuilding.Set(A, true)</c> 到配对 <c>Set(A, false)</c> 之间，同上。
    /// </para>
    /// <para>
    /// <b>为什么第八、九道看不见。</b>正因为 <c>SilenceOpen</c> 认的是串，
    /// 把控件换成别的之后，那条写入在它们眼里<b>仍然被罩着</b>——
    /// 拿 <c>SilencedBefore</c> 直接量，实测照旧返回 <c>true</c>（反向对照确认）。
    /// </para>
    /// </remarks>
    private static void SuppressionCoversItsOwnControl()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第十八道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var windows = parsed.Sum(f => f.Lines.Count(l => SilenceCtl.IsMatch(l)));
        var flags = parsed.Sum(f => f.Lines.Count(l => FlagCtl.IsMatch(l)));

        var violations = new List<string>();

        foreach (var f in parsed)
        {
            foreach (var v in WindowScopeViolations(f.Lines))
            {
                violations.Add($"{Relative(f.Path)}:{v}");
            }
        }

        Program.Check(
            $"定位到抑制的作用对象（扫描 {files.Count} 个文件：" +
            $"{windows} 处静默窗 + {flags} 处持续标记）",
            windows >= 5 && flags >= 4,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            $"窗罩住的写入就是它自己那个控件（实测 {violations.Count} 处罩错了人）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : "罩错了控件 = 那一发根本没被抑制，回声照常回调出去："
                  + Environment.NewLine + string.Join(Environment.NewLine, violations));

        Program.Check(
            "合成样本：窗与写入是同一个控件 → 不报警",
            WindowScopeViolations(DemoScope(DemoScopeFlaw.None)).Count == 0,
            "最常见的写法被判违约 → 真源码里 5 处窗、4 处标记会被误报");

        Program.Check(
            "合成样本：窗开在别的控件上 → 必须报警（两条写入各报一次）",
            WindowScopeViolations(DemoScope(DemoScopeFlaw.SilenceOtherControl)).Count == 2,
            "罩错了人却照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：持续标记开在别的控件上 → 必须报警",
            WindowScopeViolations(DemoScope(DemoScopeFlaw.FlagOtherControl)).Count == 1,
            "两种抑制形状都得认，不能只认 using");

        MutationsOfRealScopes();
    }

    /// <summary>第十八道用：抑制罩住的写入<b>不是它自己那个控件</b>的地方。</summary>
    private static List<string> WindowScopeViolations(IReadOnlyList<string> lines)
    {
        var result = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var s = SilenceCtl.Match(lines[i]);

            if (s.Success)
            {
                // 窗没关是第十五道的事，这里不重复计数。
                var close = WindowClose(lines, i);

                if (close > i)
                {
                    ScopeBlock(lines, i + 1, close - 1, s.Groups[1].Value, "静默窗", result);
                }

                continue;
            }

            var f = FlagCtl.Match(lines[i]);

            if (f.Success)
            {
                var close = RebuildCloseLine(lines, i);

                if (close > i)
                {
                    ScopeBlock(lines, i + 1, close - 1, f.Groups[1].Value, "持续标记", result);
                }
            }
        }

        return result;
    }

    /// <summary>第 <paramref name="from"/> ~ <paramref name="to"/> 行里的写入是否都作用在 <paramref name="ctl"/> 上。</summary>
    private static void ScopeBlock(
        IReadOnlyList<string> lines, int from, int to, string ctl, string kind,
        List<string> result)
    {
        for (var j = Math.Max(from, 0); j <= Math.Min(to, lines.Count - 1); j++)
        {
            // 三种"被罩住的动作"形状，按<b>有没有明确接收者</b>排序：
            // 有接收者的（赋值 / 集合动作）判完就走，别再让实参形状去复判同一行——
            // 否则 `control.Items.Add(item)`（实参是 item）会被误报成"在改别的控件"。
            var w = WriteProp.Match(lines[j]);

            if (w.Success)
            {
                if (w.Groups[1].Value != ctl)
                {
                    result.Add($"第 {j + 1} 行  {kind}开在 {ctl} 上，里面却在写 "
                        + $"{w.Groups[1].Value}.{w.Groups[2].Value} —— 这一发不会被罩住");
                }

                continue;
            }

            // 持续标记罩的多半是集合动作（重建列表），不是赋值。
            var c = CollectionAct.Match(lines[j]);

            if (c.Success && CollectionNames.Contains(c.Groups[2].Value))
            {
                if (c.Groups[1].Value != ctl)
                {
                    result.Add($"第 {j + 1} 行  {kind}开在 {ctl} 上，里面却在动 "
                        + $"{c.Groups[1].Value}.{c.Groups[2].Value} —— 这一发不会被罩住");
                }

                continue;
            }

            // 最后才看"把控件交给 helper 去改"（改控件的活儿在 helper 里面）。
            var a = CallWithArgs.Match(lines[j]);

            if (a.Success && !NotACall.Contains(a.Groups[1].Value) &&
                ArgTargets(a.Groups[2].Value, out var passed) &&
                !passed.Contains(ctl) && !Touches(lines[j], ctl))
            {
                result.Add($"第 {j + 1} 行  {kind}开在 {ctl} 上，里面却在改 "
                    + $"{string.Join("、", passed)}（{a.Groups[1].Value}）—— 这一发不会被罩住");
            }
        }
    }

    /// <summary>第十八道用：这一行<b>任何地方</b>提到了 <paramref name="ctl"/>。</summary>
    private static bool Touches(string line, string ctl) =>
        Regex.IsMatch(line, @"\b" + Regex.Escape(ctl) + @"\b");

    /// <summary>
    /// 第十八道用：实参列表里提到了哪些标识符（<c>false</c> = 一个都没有，不算动作）。
    /// </summary>
    /// <remarks>
    /// 字符串字面量要先抠掉：<c>ReactorLog.Gate("hello")</c> 里的 <c>hello</c>
    /// 会被当成"在改别的控件"，一次就能多报一片假违约。
    /// </remarks>
    private static bool ArgTargets(string args, out SortedSet<string> names)
    {
        var stripped = Regex.Replace(args, @"""[^""]*""", string.Empty);
        names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(stripped, @"[A-Za-z_]\w*"))
        {
            names.Add(m.Value);
        }

        return names.Count > 0;
    }

    private static int WindowScopeViolationCount(IReadOnlyList<string> files) =>
        files.Sum(p => WindowScopeViolations(StripComments(File.ReadAllLines(p)).ToArray()).Count);

    private static void MutationsOfRealScopes()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            return;
        }

        var jobs = new List<(string Path, int Line, int Write, string Head, string Replaced, string Label)>();

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path)).ToArray();
            var rel = Relative(path);

            for (var i = 0; i < lines.Length; i++)
            {
                var s = SilenceCtl.Match(lines[i]);
                var head = string.Empty;
                var swapped = string.Empty;
                var close = -1;
                var ctl = string.Empty;
                var kind = string.Empty;

                if (s.Success)
                {
                    head = $".Silence({s.Groups[1].Value})";
                    swapped = ".Silence(other)";
                    close = WindowClose(lines, i);
                    ctl = s.Groups[1].Value;
                    kind = "静默窗";
                }
                else
                {
                    var f = FlagCtl.Match(lines[i]);

                    if (f.Success)
                    {
                        head = $"Rebuilding.Set({f.Groups[1].Value}, true)";

                        // 第二个参数必须留着：写成 Set(other) 的话站点就消失了，
                        // 违约数不升反降 —— 假红。
                        swapped = "Rebuilding.Set(other, true)";
                        close = RebuildCloseLine(lines, i);
                        ctl = f.Groups[1].Value;
                        kind = "持续标记";
                    }
                }

                if (close <= i)
                {
                    continue;
                }

                // 拿块内第一条被罩住的动作当探针：第八、九道要对它做反向对照。
                // 赋值与集合动作都算（持续标记那几处罩的是重建列表）。
                var write = -1;

                for (var j = i + 1; j < close; j++)
                {
                    var w = WriteProp.Match(lines[j]);
                    var c = CollectionAct.Match(lines[j]);
                    var a = CallWithArgs.Match(lines[j]);

                    if ((w.Success && w.Groups[1].Value == ctl) ||
                        (c.Success && c.Groups[1].Value == ctl &&
                         CollectionNames.Contains(c.Groups[2].Value)) ||
                        (a.Success && !NotACall.Contains(a.Groups[1].Value) &&
                         ArgTargets(a.Groups[2].Value, out var passed) && passed.Contains(ctl)))
                    {
                        write = j;
                        break;
                    }
                }

                if (write < 0)
                {
                    continue;
                }

                jobs.Add((path, i, write, head, swapped, $"{rel}:{i + 1} {kind} {head}"));
            }
        }

        var before = WindowScopeViolationCount(files);
        var missed = new List<string>();

        foreach (var job in jobs)
        {
            // 只换窗的控件：写入、三件套、using / finally 一件都没动。
            var after = MutateCounted(
                job.Path,
                (i, l) => i == job.Line ? l.Replace(job.Head, job.Replaced) : null,
                () => WindowScopeViolationCount(FrameworkFiles()),
                () => SilencedBefore(
                    StripComments(File.ReadAllLines(job.Path)).ToArray(), 0, job.Write) ? 0 : 1);

            if (after[0] <= before)
            {
                missed.Add($"{job.Label}：换掉窗的控件后违约数 {before} → {after[0]}");
            }

            // 反向对照：第八、九道只认 .Silence( 这个串，换控件后仍然判"被罩着"。
            if (after[1] != 0)
            {
                missed.Add($"{job.Label}：第八 / 九道这次不认这个窗了（它们盯的不是「罩在谁身上」）");
            }
        }

        Program.Check(
            $"变异：把真源码里抑制的作用对象换掉（{jobs.Count} 处）都必须多报违约，" +
            "且第八 / 九道始终仍然认这个窗",
            jobs.Count >= 9 && missed.Count == 0,
            (missed.Count == 0 ? null : string.Join(Environment.NewLine, missed) + Environment.NewLine) +
            "进视野的：" + string.Join("、", jobs.Select(j => j.Label)));
    }

    // ── 第十九道：静默窗必须开在<b>自家</b>那张表上 ──────────────────

    /// <summary>第十九道用：开窗那一行开在<b>哪张表</b>上。</summary>
    private static readonly Regex SilenceTable =
        new(@"(\w+)\.Silence\(\s*([A-Za-z_]\w*)", RegexOptions.Compiled);

    /// <summary>
    /// 第十九道契约：静默窗必须开在这个类<b>自己消费的那张回声表</b>上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EchoGuard</c> 里 <c>_pending</c> 与 <c>_silenced</c> 都是<b>实例字段</b>——
    /// 静态字段 <c>ValueEcho</c> / <c>TextEcho</c> / <c>SelectionEcho</c> … 各是一张
    /// 独立的表。而 <c>Consume</c> 判回声时第一件事是看
    /// <c>_silenced[control] &gt; 0</c>，也就是<b>只认自己那张表的窗</b>。
    /// </para>
    /// <para>
    /// 于是窗开错表是<b>静默失效</b>：
    /// <c>using (TextEcho.Silence(control)) { control.Maximum = 9; }</c>——
    /// 写 <c>Maximum</c> 会把受控的 <c>Value</c> 夹进新区间（<c>CoerceValue</c>），
    /// 那一发回声走的是 <c>ValueChanged</c> → <c>ValueEcho.Consume</c>；
    /// 而 <c>ValueEcho</c> 的窗<b>没开</b>，<c>TextEcho</c> 的窗开了却永远不会有回声
    /// 落到它身上。结局是 <c>NotExpected</c> → 回调出去 → 抖动。
    /// </para>
    /// <para>
    /// <b>判据</b>：取窗所在的那个类，收集它 <c>Consume</c> 用的表——<c>Consume</c>
    /// 是"接"的那一侧，回声回来走的就是它；类里没有 <c>Consume</c> 时退回
    /// <c>Expect</c> 的集合。窗开的表必须落在这个集合里。
    /// </para>
    /// <para>
    /// <b>与第十八道的分工</b>：第十八道比对窗的<b>控件参数</b>（罩在谁身上），
    /// 这一道比对窗的<b>表名</b>（开在谁家）。两个维度独立——只换表名不影响前者。
    /// </para>
    /// <para>
    /// <b>认不出的边界（不假装能拦）</b>：按<b>类</b>切分，所以一个类里有多张表时，
    /// 只要窗开在其中任何一张被 <c>Consume</c> 的表上就放行（当前源码每个类只有一张）。
    /// 另外它不判断"窗里那一写到底会扰动哪个受控属性"——那一层靠类级归属兜。
    /// </para>
    /// </remarks>
    private static void SilenceWindowsUseTheirOwnTable()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第十九道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var sites = parsed.Sum(f => f.Lines.Count(l => SilenceTable.IsMatch(l)));
        var violations = new List<string>();

        foreach (var f in parsed)
        {
            foreach (var v in SilenceTableViolations(f.Lines))
            {
                violations.Add($"{Relative(f.Path)}:{v}");
            }
        }

        Program.Check(
            $"定位到静默窗（扫描 {files.Count} 个文件：{sites} 处）",
            sites >= 5,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            $"静默窗都开在自家那张表上（实测 {violations.Count} 处不是）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : "_silenced 是每表一份 —— 开错表 = 那一发回声根本没被罩住，照常回调出去："
                  + Environment.NewLine + string.Join(Environment.NewLine, violations));

        Program.Check(
            "合成样本：窗开在本类 Consume 的那张表上 → 不报警",
            SilenceTableViolations(DemoSilence(DemoSilenceFlaw.None)).Count == 0,
            "最常见的写法被判违约 → 真源码里 5 处窗会被误报");

        Program.Check(
            "合成样本：窗开在别家 handler 的表上 → 必须报警",
            SilenceTableViolations(DemoSilence(DemoSilenceFlaw.ForeignTable)).Count == 1,
            "开错表却照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：窗开在本类只 Expect 不 Consume 的表上 → 必须报警",
            SilenceTableViolations(DemoSilence(DemoSilenceFlaw.ExpectOnlyTable)).Count == 1,
            "回声回来走的是 Consume 那张表，光登记不算数");

        Program.Check(
            "合成样本：窗所在的类根本不碰任何回声表 → 必须报警",
            SilenceTableViolations(DemoSilence(DemoSilenceFlaw.NoOwner)).Count == 1,
            "无主的窗永远罩不到任何回声，是死代码");

        MutationsOfRealSilenceTables();
    }

    /// <summary>第十九道用：静默窗开在<b>不是自家</b>的表上的地方。</summary>
    private static List<string> SilenceTableViolations(IReadOnlyList<string> lines)
    {
        var result = new List<string>();

        foreach (var span in ClassSpans(lines))
        {
            var (own, any) = OwnTables(lines, span.Start, span.End);

            for (var i = span.Start; i <= Math.Min(span.End, lines.Count - 1); i++)
            {
                var s = SilenceTable.Match(lines[i]);

                if (!s.Success || own.Contains(s.Groups[1].Value))
                {
                    continue;
                }

                result.Add(any
                    ? $"第 {i + 1} 行  窗开在 {s.Groups[1].Value} 上，而这个类的回声走的是 "
                      + $"{string.Join("、", own)} —— 后者没开窗，罩不住"
                    : $"第 {i + 1} 行  {s.Groups[1].Value}.Silence({s.Groups[2].Value})"
                      + " 所在的类既不登记也不消费任何回声表 —— 这一发永远不会有回声落进来");
            }
        }

        return result;
    }

    /// <summary>
    /// 第十九道用：第 <paramref name="start"/> ~ <paramref name="end"/> 行那个类
    /// 自己消费（没有 <c>Consume</c> 时退回登记）的回声表。
    /// </summary>
    private static (SortedSet<string> Own, bool Any) OwnTables(
        IReadOnlyList<string> lines, int start, int end)
    {
        var consume = new SortedSet<string>(StringComparer.Ordinal);
        var expect = new SortedSet<string>(StringComparer.Ordinal);

        for (var i = start; i <= Math.Min(end, lines.Count - 1); i++)
        {
            foreach (Match m in EchoParty.Matches(lines[i]))
            {
                (string.Equals(m.Groups[2].Value, "Consume", StringComparison.Ordinal)
                    ? consume
                    : expect).Add(m.Groups[1].Value);
            }
        }

        return (consume.Count > 0 ? consume : expect, consume.Count > 0 || expect.Count > 0);
    }

    /// <summary>第 <paramref name="from"/> ~ <paramref name="to"/> 行里第一条作用在 <paramref name="ctl"/> 上的动作（-1 = 没有）。</summary>
    private static int FirstActionOn(IReadOnlyList<string> lines, int from, int to, string ctl)
    {
        for (var j = Math.Max(from, 0); j <= Math.Min(to, lines.Count - 1); j++)
        {
            var w = WriteProp.Match(lines[j]);

            if (w.Success && w.Groups[1].Value == ctl)
            {
                return j;
            }

            var c = CollectionAct.Match(lines[j]);

            if (c.Success && c.Groups[1].Value == ctl &&
                CollectionNames.Contains(c.Groups[2].Value))
            {
                return j;
            }

            var a = CallWithArgs.Match(lines[j]);

            if (a.Success && !NotACall.Contains(a.Groups[1].Value) &&
                ArgTargets(a.Groups[2].Value, out var passed) && passed.Contains(ctl))
            {
                return j;
            }
        }

        return -1;
    }

    private static int SilenceTableViolationCount(IReadOnlyList<string> files) =>
        files.Sum(p => SilenceTableViolations(StripComments(File.ReadAllLines(p)).ToArray()).Count);

    /// <summary>
    /// 变异：把真源码里静默窗的<b>表名</b>换掉，第十九道必须报，
    /// 而第八 / 九道（只认 <c>.Silence(</c> 这个串）与第十八道（只比控件）必须<b>无反应</b>。
    /// </summary>
    private static void MutationsOfRealSilenceTables()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        // 全树出现过的回声表：拿来当"别家的表"。
        var all = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var f in parsed)
        {
            foreach (var line in f.Lines)
            {
                foreach (Match m in EchoParty.Matches(line))
                {
                    all.Add(m.Groups[1].Value);
                }
            }
        }

        var jobs = new List<(string Path, int Line, int Write, string Head, string Swapped,
            string Label)>();

        foreach (var f in parsed)
        {
            var rel = Relative(f.Path);

            foreach (var span in ClassSpans(f.Lines))
            {
                var (own, _) = OwnTables(f.Lines, span.Start, span.End);
                var foreign = string.Empty;

                foreach (var t in all)
                {
                    if (!own.Contains(t))
                    {
                        foreign = t;
                        break;
                    }
                }

                if (foreign.Length == 0)
                {
                    continue;
                }

                for (var i = span.Start; i <= Math.Min(span.End, f.Lines.Length - 1); i++)
                {
                    var s = SilenceTable.Match(f.Lines[i]);

                    if (!s.Success)
                    {
                        continue;
                    }

                    var ctl = s.Groups[2].Value;
                    var close = WindowClose(f.Lines, i);
                    var write = close > i ? FirstActionOn(f.Lines, i + 1, close - 1, ctl) : -1;

                    if (write < 0)
                    {
                        continue;
                    }

                    var head = s.Groups[1].Value + ".Silence(";
                    var label = $"{rel}:{i + 1} {s.Groups[1].Value}.Silence({ctl})";

                    // 方向一：换成一个真实存在、但本类不消费的表（复制粘贴改一半的典型）。
                    jobs.Add((f.Path, i, write, head, foreign + ".Silence(",
                        $"{label} → {foreign}"));

                    // 方向二：换成一个压根不存在的表（改名漏了一处）。
                    jobs.Add((f.Path, i, write, head, "NopeEcho.Silence(",
                        $"{label} → NopeEcho"));
                }
            }
        }

        var before = SilenceTableViolationCount(files);
        var missed = new List<string>();

        foreach (var job in jobs)
        {
            // 只换窗的表名：控件参数、写入、using / finally 一件都没动。
            var after = MutateCounted(
                job.Path,
                (i, l) => i == job.Line ? l.Replace(job.Head, job.Swapped) : null,
                () => SilenceTableViolationCount(FrameworkFiles()),
                () => WindowScopeViolationCount(FrameworkFiles()),
                () => SilencedBefore(
                    StripComments(File.ReadAllLines(job.Path)).ToArray(), 0, job.Write) ? 0 : 1);

            if (after[0] <= before)
            {
                missed.Add($"{job.Label}：换掉窗的表后违约数 {before} → {after[0]}");
            }

            // 反向对照一：第十八道只比控件参数，换表名不影响它。
            if (after[1] != 0)
            {
                missed.Add($"{job.Label}：第十八道这次报了（它盯的不是「开在哪张表上」）");
            }

            // 反向对照二：第八、九道只认 .Silence( 这个串，换表名后仍然判"被罩着"。
            if (after[2] != 0)
            {
                missed.Add($"{job.Label}：第八 / 九道这次不认这个窗了");
            }
        }

        Program.Check(
            $"变异：把真源码里静默窗的表名换掉（{jobs.Count} 处）都必须多报违约，" +
            "且第八 / 九 / 十八道始终无反应",
            jobs.Count >= 10 && missed.Count == 0,
            (missed.Count == 0 ? null : string.Join(Environment.NewLine, missed) + Environment.NewLine) +
            "进视野的：" + string.Join("、", jobs.Select(j => j.Label)));
    }

    /// <summary>第十九道用：合成样本的几种坏法。</summary>
    private enum DemoSilenceFlaw
    {
        /// <summary>窗开在本类 <c>Consume</c> 的那张表上（正确写法）。</summary>
        None,

        /// <summary>窗开在别家 handler 的表上。</summary>
        ForeignTable,

        /// <summary>窗开在本类只 <c>Expect</c> 不 <c>Consume</c> 的那张表上。</summary>
        ExpectOnlyTable,

        /// <summary>窗所在的类既不登记也不消费任何回声表。</summary>
        NoOwner,
    }

    // ── 第二十道：闸门判完之后必须真的<b>拦下来</b> ──────────────────

    /// <summary>第二十道用：判完闸门的那一行。</summary>
    private static readonly Regex SuppressCall =
        new(@"SelectionGate\.Suppress\(", RegexOptions.Compiled);

    /// <summary>
    /// 第二十道契约：<c>SelectionGate.Suppress(verdict)</c> 判成"该吞"之后，
    /// 这个块必须<b>直接退出</b>——不能只记一行日志就往下走。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这一道盯的是闸门那一半，第十四道盯的是回声那一半。</b>
    /// 第十四道的判据要求"以 <c>if (</c> 开头 <b>且</b> 行里有 <c>.Consume(</c>"——
    /// <c>if (SelectionGate.Suppress(verdict))</c> 这一行<b>没有 <c>Consume</c></b>，
    /// 所以它从来没进过第十四道的视野（契约里 <c>Suppress</c> 这个 token 出现 <b>0 次</b>）。
    /// </para>
    /// <para>
    /// 而把这一半做废的代价是<b>中间态泄漏</b>：闸门吞的是
    /// <c>NotReady</c>（模板没就位）、<c>Rebuilding</c>（整批换 items）、
    /// <c>CancelTransient</c>（取消选中那一发）、<c>Echo</c> 四类。
    /// 判了却不拦，这四类就<b>原样回调给用户</b> → setState → 重渲染 →
    /// 把用户刚选中的值拽回去。表现就是抖动、以及"点了没反应"里最难查的那一类。
    /// </para>
    /// <para>
    /// <b>判据</b>：以 <c>if (</c> 开头且含 <c>SelectionGate.Suppress(</c> 的行，
    /// 它的块体内必须有一句<b>直接属于这个块</b>的 <c>return</c>——
    /// 嵌在更里层的 <c>if</c> 里<b>不算</b>：那个 <c>if</c> 不满足时照样往下走。
    /// </para>
    /// </remarks>
    private static void GateVerdictIsEnforced()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码（第二十道）", false, "找不到 Reactor.uwp");
            return;
        }

        var parsed = files
            .Select(p => (Path: p, Lines: StripComments(File.ReadAllLines(p)).ToArray()))
            .ToList();

        var sites = parsed.Sum(f => f.Lines.Count(l => SuppressCall.IsMatch(l)));
        var violations = new List<string>();

        foreach (var f in parsed)
        {
            foreach (var v in VerdictBlockViolations(f.Lines))
            {
                violations.Add($"{Relative(f.Path)}:{v}");
            }
        }

        Program.Check(
            $"定位到闸门判完的那一句（扫描 {files.Count} 个文件：{sites} 处）",
            sites >= 3,
            "命中数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            $"判成“该吞”之后都真的退出了（实测 {violations.Count} 处没有）",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : "中间态（未就绪 / 重建中 / 取消选中 / 回声）会原样回调给用户："
                  + Environment.NewLine + string.Join(Environment.NewLine, violations));

        Program.Check(
            "合成样本：块里直接 return → 不报警",
            VerdictBlockViolations(DemoGate(DemoGateFlaw.None)).Count == 0,
            "最常见的写法被判违约 → 真源码里 3 处闸门会被误报");

        Program.Check(
            "合成样本：只记日志、不退出 → 必须报警",
            VerdictBlockViolations(DemoGate(DemoGateFlaw.NoReturn)).Count == 1,
            "判了却不拦却照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：return 只嵌在里层的 if 里 → 必须报警",
            VerdictBlockViolations(DemoGate(DemoGateFlaw.NestedOnlyReturn)).Count == 1,
            "里层 if 不满足时照样往下走 —— 这是最容易骗过“块里有没有 return”的形状");

        Program.Check(
            "合成样本：两个闸门块、只坏一个 → 只报一次（按站点算，不是按方法算）",
            VerdictBlockViolations(DemoGate(DemoGateFlaw.TwoBlocksOneBroken)).Count == 1,
            "按方法做包含判断会把好的那块也算进去 —— 违约数对不上就说明判据糊了");

        MutationsOfRealVerdictBlocks();
    }

    /// <summary>第二十道用：判成"该吞"却没有<b>直接退出</b>的地方。</summary>
    private static List<string> VerdictBlockViolations(IReadOnlyList<string> lines)
    {
        var result = new List<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            if (!SuppressCall.IsMatch(lines[i]) ||
                !lines[i].TrimStart().StartsWith("if (", StringComparison.Ordinal))
            {
                continue;
            }

            if (BlockExitLine(lines, i) < 0)
            {
                result.Add($"第 {i + 1} 行  判完闸门（{lines[i].Trim()}）之后没有直接退出"
                    + " —— 未就绪 / 重建中 / 取消选中这些中间态会照样回调出去");
            }
        }

        return result;
    }

    /// <summary>
    /// 第 <paramref name="index"/> 行那个 <c>if</c> 的块体内，
    /// <b>直接属于这个块</b>的那句 <c>return</c> 的行号（-1 = 没有）。
    /// </summary>
    private static int BlockExitLine(IReadOnlyList<string> lines, int index)
    {
        var depth = 0;
        var started = false;

        for (var j = index; j < Math.Min(lines.Count, index + 120); j++)
        {
            var delta = lines[j].Count(c => c == '{') - lines[j].Count(c => c == '}');
            var before = depth;
            depth += delta;

            if (!started)
            {
                if (delta > 0)
                {
                    started = true;
                }

                continue;
            }

            if (depth <= 0)
            {
                break;
            }

            // 只认<b>深度 1</b>、且这一行<b>以 return 起头</b>的：
            // 再深一层的那个 if 不满足时照样往下走；`if (x) return;` 也是条件退出，
            // 不算"判了就拦"。收紧到"以 return 起头"之后，`if (false) return;`
            // 这种单行形式才骗不过去（见 MutationsOfRealVerdictBlocks 方向二）。
            if (before == 1 && lines[j].TrimStart().StartsWith("return", StringComparison.Ordinal))
            {
                return j;
            }
        }

        return -1;
    }

    private static int VerdictBlockViolationCount(IReadOnlyList<string> files) =>
        files.Sum(p => VerdictBlockViolations(StripComments(File.ReadAllLines(p)).ToArray()).Count);

    /// <summary>
    /// 变异：把真源码里闸门那一半做废，第二十道必须报，
    /// 而第十四道（只认 <c>.Consume(</c> 那一行）必须<b>无反应</b>。
    /// </summary>
    private static void MutationsOfRealVerdictBlocks()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            return;
        }

        var jobs = new List<(string Path, int Line, int Exit, string Label)>();

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path)).ToArray();
            var rel = Relative(path);

            for (var i = 0; i < lines.Length; i++)
            {
                if (!SuppressCall.IsMatch(lines[i]) ||
                    !lines[i].TrimStart().StartsWith("if (", StringComparison.Ordinal))
                {
                    continue;
                }

                // 已经在报警的不需要再变异。
                var exit = BlockExitLine(lines, i);

                if (exit < 0)
                {
                    continue;
                }

                jobs.Add((path, i, exit, $"{rel}:{i + 1}"));
            }
        }

        var before = VerdictBlockViolationCount(files);
        var missed = new List<string>();

        foreach (var job in jobs)
        {
            // 方向一：把那句 return 换成一行日志 —— 判了却不拦。
            var after = MutateCounted(
                job.Path,
                (i, l) => i == job.Exit ? Indent(l) + "ReactorLog.Gate(\"mutated\");" : null,
                () => VerdictBlockViolationCount(FrameworkFiles()),
                () => ConsumeViolationCount(FrameworkFiles()));

            if (after[0] <= before)
            {
                missed.Add($"{job.Label}：抽掉 return 后违约数 {before} → {after[0]}");
            }

            // 反向对照：第十四道只认 .Consume( 那一行，这块它没有看。
            if (after[1] != 0)
            {
                missed.Add($"{job.Label}：第十四道这次报了（它盯的不是「闸门判完有没有拦」）");
            }

            // 方向二：把那句 return 降级成条件退出 —— `if (false) return;`
            // 看着"块里明明有 return"，其实拦不住。
            // （不能用"把条件换成恒假"当方向二：那样 `SelectionGate.Suppress(`
            //   这个串没了，站点自己消失，违约数不升反平 —— 第十八道记过的同一个陷阱。）
            var swapped = MutateCounted(
                job.Path,
                (i, l) => i == job.Exit ? Indent(l) + "if (false) return;" : null,
                () => VerdictBlockViolationCount(FrameworkFiles()),
                () => ConsumeViolationCount(FrameworkFiles()));

            if (swapped[0] <= before)
            {
                missed.Add($"{job.Label}：return 降级成条件退出后违约数 {before} → {swapped[0]}");
            }

            if (swapped[1] != 0)
            {
                missed.Add($"{job.Label}：第十四道这次报了（它盯的不是「闸门判完有没有拦」）");
            }
        }

        Program.Check(
            $"变异：把真源码里闸门那一半做废（{jobs.Count} 处 × 2 个方向）都必须多报违约，" +
            "且第十四道始终无反应",
            jobs.Count >= 3 && missed.Count == 0,
            (missed.Count == 0 ? null : string.Join(Environment.NewLine, missed) + Environment.NewLine) +
            "进视野的：" + string.Join("、", jobs.Select(j => j.Label)));
    }

    /// <summary>第二十道用：合成样本的几种坏法。</summary>
    private enum DemoGateFlaw
    {
        /// <summary>块里直接 <c>return</c>（正确写法）。</summary>
        None,

        /// <summary>只记日志，不退出。</summary>
        NoReturn,

        /// <summary><c>return</c> 只嵌在里层的 <c>if</c> 里。</summary>
        NestedOnlyReturn,

        /// <summary>两个闸门块，只坏掉一个。</summary>
        TwoBlocksOneBroken,
    }

    /// <summary>合成样本：一段闸门，按 <paramref name="flaw"/> 让它判了不拦。</summary>
    private static List<string> DemoGate(DemoGateFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoGateHandler",
            "{",
            "    private static void Dispatch(DemoCombo control, Action<int>? callback)",
            "    {",
            "        var verdict = SelectionGate.Decide(true, true, false);",
            string.Empty,
        };

        lines.AddRange(GateBlock(
            noReturn: flaw == DemoGateFlaw.NoReturn,
            nestedOnly: flaw == DemoGateFlaw.NestedOnlyReturn));

        if (flaw == DemoGateFlaw.TwoBlocksOneBroken)
        {
            // 好的那块不能因为旁边有个坏的就被算进去。
            lines.AddRange(GateBlock(noReturn: false, nestedOnly: false));
            lines.AddRange(GateBlock(noReturn: true, nestedOnly: false));
        }

        lines.AddRange(new[] { "        callback?.Invoke(control.SelectedIndex);", "    }", "}" });

        return lines;
    }

    /// <summary>第二十道用：合成出一段 <c>if (SelectionGate.Suppress(verdict)) { … }</c>。</summary>
    private static List<string> GateBlock(bool noReturn, bool nestedOnly)
    {
        var block = new List<string>
        {
            "        if (SelectionGate.Suppress(verdict))",
            "        {",
            "            ReadyStats.Suppressed++;",
        };

        if (nestedOnly)
        {
            block.AddRange(new[]
            {
                "            if (SelectionGate.ShouldRestoreAfterSuppress(verdict))",
                "            {",
                "                ReactorLog.Gate(\"纠正\");",
                "                return;",
                "            }",
            });
        }
        else if (!noReturn)
        {
            block.AddRange(new[] { "            ReactorLog.Gate(\"吞\");", "            return;" });
        }
        else
        {
            block.Add("            ReactorLog.Gate(\"吞\");");
        }

        block.AddRange(new[] { "        }", string.Empty });

        return block;
    }

    /// <summary>合成样本：一段静默窗，按 <paramref name="flaw"/> 让它开在别人的表上。</summary>
    private static List<string> DemoSilence(DemoSilenceFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoSilenceHandler",
            "{",
            "    private static readonly EchoGuard ValueEcho = new();",
            "    private static readonly EchoGuard TextEcho = new();",
            string.Empty,
            "    private static void Patch(DemoSlider control)",
            "    {",
        };

        lines.Add(flaw == DemoSilenceFlaw.ForeignTable
            ? "        using (TextEcho.Silence(control))"
            : "        using (ValueEcho.Silence(control))");

        lines.AddRange(new[]
        {
            "        {",
            "            control.Maximum = 9;",
            "        }",
            string.Empty,
            "        ValueEcho.Expect(control, 3);",
            "        control.Value = 3;",
            "        ValueEcho.CancelIfUnconsumed(control);",
            "    }",
            string.Empty,
            "    private static void OnValueChanged(DemoSlider control)",
            "    {",
        });

        // 只 Expect 不 Consume 那一档：把 Consume 挪到另一张表上。
        lines.Add(flaw == DemoSilenceFlaw.ExpectOnlyTable
            ? "        if (TextEcho.Consume(control, control.Value)) return;"
            : "        if (ValueEcho.Consume(control, control.Value)) return;");

        lines.AddRange(new[] { "    }", "}" });

        if (flaw == DemoSilenceFlaw.NoOwner)
        {
            // 无主那一档：把两张表都摘掉，只剩一个孤零零的窗。
            return lines
                .Where(l => !l.Contains("Echo.Expect(", StringComparison.Ordinal) &&
                            !l.Contains("Echo.Consume(", StringComparison.Ordinal) &&
                            !l.Contains("Echo.CancelIfUnconsumed(", StringComparison.Ordinal))
                .ToList();
        }

        return lines;
    }

    /// <summary>合成样本：一段抑制，按 <paramref name="flaw"/> 让它罩在别的控件上。</summary>
    private static List<string> DemoScope(DemoScopeFlaw flaw)
    {
        var silence = flaw == DemoScopeFlaw.SilenceOtherControl ? "other" : "control";
        var flag = flaw == DemoScopeFlaw.FlagOtherControl ? "other" : "control";

        return new List<string>
        {
            "internal sealed class DemoScopeHandler : ElementHandler<DemoElement, DemoSlider>",
            "{",
            "    private static readonly EchoGuard ValueEcho = new();",
            "    private static readonly WeakTable<DemoSlider, bool> Rebuilding = new();",
            "",
            "    private static void ApplyRange(DemoSlider control, DemoSlider other)",
            "    {",
            $"        using (ValueEcho.Silence({silence}))",
            "        {",
            "            control.Minimum = 0;",
            "            control.Maximum = 100;",
            "        }",
            "",
            $"        Rebuilding.Set({flag}, true);",
            "        try",
            "        {",
            "            control.Items.Clear();",
            "        }",
            "        finally",
            "        {",
            "            Rebuilding.Set(control, false);",
            "        }",
            "    }",
            "}",
        };
    }

    private enum DemoScopeFlaw
    {
        None,

        /// <summary>静默窗开在别的控件上。</summary>
        SilenceOtherControl,

        /// <summary>持续标记开在别的控件上。</summary>
        FlagOtherControl,
    }

    /// <summary>合成样本：一段抑制窗口，按 <paramref name="flaw"/> 把"关"弄坏。</summary>
    private static List<string> DemoWindow(DemoWindowFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoWindowHandler : ElementHandler<DemoElement, DemoSlider>",
            "{",
            "    private static readonly EchoGuard ValueEcho = new();",
            "    private static readonly WeakTable<DemoSlider, bool> Rebuilding = new();",
            "",
            "    private static void ApplyRange(DemoSlider control)",
            "    {",
        };

        // 静默窗那一半：写入必须在窗内（第八 / 九道的判据要靠它）。
        if (flaw == DemoWindowFlaw.SilenceNotDisposed)
        {
            lines.Add("        ValueEcho.Silence(control);");
            lines.Add("        {");
        }
        else
        {
            lines.Add("        using (ValueEcho.Silence(control))");
            lines.Add("        {");
        }

        lines.Add("            control.Minimum = 0;");
        lines.Add("            control.Maximum = 100;");
        lines.Add("        }");

        // 持续标记那一半。
        switch (flaw)
        {
            case DemoWindowFlaw.None:
                lines.Add("        Rebuilding.Set(control, true);");
                lines.Add("        try");
                lines.Add("        {");
                lines.Add("            control.Items.Clear();");
                lines.Add("        }");
                lines.Add("        finally");
                lines.Add("        {");
                lines.Add("            Rebuilding.Set(control, false);");
                lines.Add("        }");
                break;

            case DemoWindowFlaw.FlagClosedOutsideFinally:
                lines.Add("        Rebuilding.Set(control, true);");
                lines.Add("        try");
                lines.Add("        {");
                lines.Add("            control.Items.Clear();");
                lines.Add("            Rebuilding.Set(control, false);");
                lines.Add("        }");
                lines.Add("        finally");
                lines.Add("        {");
                lines.Add("        }");
                break;

            case DemoWindowFlaw.FlagClosedInCatch:
                lines.Add("        Rebuilding.Set(control, true);");
                lines.Add("        try");
                lines.Add("        {");
                lines.Add("            control.Items.Clear();");
                lines.Add("        }");
                lines.Add("        catch");
                lines.Add("        {");
                lines.Add("            Rebuilding.Set(control, false);");
                lines.Add("        }");
                break;

            default:
                lines.Add("        Rebuilding.Set(control, true);");
                lines.Add("        try");
                lines.Add("        {");
                lines.Add("            control.Items.Clear();");
                lines.Add("        }");
                lines.Add("        finally");
                lines.Add("        {");
                lines.Add("            Rebuilding.Set(control, false);");
                lines.Add("        }");
                break;
        }

        lines.Add("    }");
        lines.Add("}");

        return lines;
    }

    private enum DemoWindowFlaw
    {
        None,

        /// <summary>静默窗没包 <c>using</c>：<c>Dispose</c> 不会被调用，窗永久开着。</summary>
        SilenceNotDisposed,

        /// <summary>标记在正常路径上关：中间一抛异常就永久留着。</summary>
        FlagClosedOutsideFinally,

        /// <summary>标记关在 <c>catch</c>：正常返回时根本不关。</summary>
        FlagClosedInCatch,
    }

    private static void OutsideHandlerWritesAreNeutralized()
    {
        var files = FrameworkFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到框架源码", false, "找不到 Reactor.uwp/**/*.cs");
            return;
        }

        var found = OutsideHandlerWrites(files)
            .Select(w => (w.File, w.Property, w.Receiver))
            .Distinct()
            .OrderBy(t => t.File + "|" + t.Property + "|" + t.Receiver, StringComparer.Ordinal)
            .ToList();

        var ledger = OutsideWriteLedger
            .Select(e => (e.File, e.Property, e.Receiver))
            .OrderBy(t => t.File + "|" + t.Property + "|" + t.Receiver, StringComparer.Ordinal)
            .ToList();

        var unknown = found.Except(ledger).ToList();
        var stale = ledger.Except(found).ToList();

        Program.Check(
            $"handler 之外写用户可改属性的地方全部有人认领（实测 {found.Count} 处）",
            unknown.Count == 0 && stale.Count == 0 && found.Count > 0,
            (unknown.Count == 0 ? null :
                $"{unknown.Count} 处没人认领：" + string.Join("、", unknown.Select(t => $"{t.File} 的 {t.Receiver}.{t.Property}")) + "\n") +
            (stale.Count == 0 ? null :
                $"{stale.Count} 条登记已失效（源码里找不到了）：" + string.Join("、", stale.Select(t => $"{t.File} 的 {t.Receiver}.{t.Property}"))));

        // 登记里写的"靠什么中和"必须真的还在源码里。
        var orphan = new List<string>();

        foreach (var entry in OutsideWriteLedger)
        {
            if (entry.AnchorMethod is null || entry.AnchorPattern is null)
            {
                continue;
            }

            if (AnchorHolds(files, entry.AnchorMethod, entry.AnchorPattern))
            {
                continue;
            }

            orphan.Add($"{entry.Receiver}.{entry.Property} 登记的中和手段不见了：" +
                $"{entry.AnchorMethod} 里找不到 {entry.AnchorPattern}");
        }

        Program.Check(
            "每条登记指向的中和手段都还在源码里",
            orphan.Count == 0,
            orphan.Count == 0 ? null : string.Join(Environment.NewLine, orphan));

        // 反向对照一：塞一处 handler 之外、没人认领的受控写回进去，必须报警。
        string[] intruder =
        {
            "internal static class Sneaky",
            "{",
            "    public static void Poke(TextBox box, string value)",
            "    {",
            "        box.Text = value;",
            "    }",
            "}",
        };

        var caught = OutsideHandlerWrites(intruder, "Fake/Sneaky.cs")
            .Select(w => (w.File, w.Property, w.Receiver))
            .Except(ledger)
            .Any();

        Program.Check(
            "handler 之外冒出一处没登记的受控写回，必须报警",
            caught,
            "新增的洞口没人认领却判合格 —— 这一道没在看");

        // 反向对照二：`hook.Value = x` 这种内部字段不该被当成控件属性写回。
        string[] internalField =
        {
            "internal sealed class StateHook<T>",
            "{",
            "    public T Value;",
            "",
            "    public void Set(T next)",
            "    {",
            "        var hook = this;",
            "        hook.Value = next;",
            "    }",
            "}",
        };

        var falseAlarm = OutsideHandlerWrites(internalField, "Fake/Hook.cs").Count;

        Program.Check(
            "内部字段的 Value 不该被当成控件属性写回",
            falseAlarm == 0,
            $"把 {falseAlarm} 处内部字段赋值算成了受控写回 —— 会淹没真正的违约");

        MutationOfNeutralization();
    }

    /// <summary>
    /// 承重自查：<b>把中和那段代码删掉，登记必须失去锚点</b>；而删掉旁边那行日志
    /// 不该有事（证明锚点盯的是机制，不是日志）。
    /// </summary>
    private static void MutationOfNeutralization()
    {
        var root = RepoRoot();

        if (root is null)
        {
            Program.Check("定位到 Reconciler 源码", false, "找不到仓库根");
            return;
        }

        var path = Path.Combine(root, "Reactor.uwp", "Internal", "Reconciler.cs");
        var original = File.ReadAllLines(path).ToList();
        var (start, end) = MethodRange(original, 0, original.Count - 1,
            l => l.Contains("void RebindTextChanged("));

        if (start < 0)
        {
            Program.Check("定位到 RebindTextChanged", false, "方法没了，锚点无从验证");
            return;
        }

        var entry = OutsideWriteLedger.First(e => e.Receiver == "TextBox");

        // 机制行：含 `PropWriter.IsMounting || !textBox.IsLoaded` 的那一行。
        var mechanism = Enumerable.Range(start, end - start + 1)
            .FirstOrDefault(i => original[i].Contains("PropWriter.IsMounting") &&
                                 original[i].Contains("IsLoaded"));

        // 日志行：只提到 IsMounting 的那一行（`ReactorLog.Gate`）。
        var logLine = Enumerable.Range(start, end - start + 1)
            .FirstOrDefault(i => i != mechanism &&
                                 original[i].Contains("PropWriter.IsMounting"));

        Program.Check(
            "定位到中和那一行与它旁边那行日志",
            mechanism > 0 && logLine > 0,
            $"机制行={mechanism}，日志行={logLine} —— 行没定位到，下面的对照就是空的");

        var killed = Break(path, original, mechanism, entry);
        var spared = Break(path, original, logLine, entry);

        Program.Check(
            "删掉中和那一行（机制），登记必须失去锚点",
            killed,
            "删掉 `PropWriter.IsMounting || !textBox.IsLoaded` 之后这一道还判合格 —— 登记是写上去好看的");

        Program.Check(
            "只删掉那行日志，不该有事（锚点盯的是机制不是日志）",
            !spared,
            "删日志也报警 —— 锚点配到了无关的行上，真删机制时反而可能漏");
    }

    /// <summary>把 <paramref name="line"/> 那一行从 <paramref name="path"/> 里去掉，
    /// 重新判定 <paramref name="entry"/> 的锚点是否还成立，然后还原文件。</summary>
    private static bool Break(
        string path, IReadOnlyList<string> lines, int line,
        (string File, string Property, string Receiver, string? AnchorMethod,
            string? AnchorPattern, string Reason) entry)
    {
        if (line <= 0)
        {
            return false;
        }

        var mutated = lines.Where((_, i) => i != line).ToList();

        // 与 <see cref="Mutate"/> 同理：还原走字节，不用 `WriteAllLines` 重拼换行。
        var original = File.ReadAllBytes(path);

        try
        {
            File.WriteAllLines(path, mutated);
            return !AnchorHolds(FrameworkFiles(), entry.AnchorMethod!, entry.AnchorPattern!);
        }
        finally
        {
            File.WriteAllBytes(path, original);
        }
    }

    /// <summary>在框架源码里找 <paramref name="method"/> 的方法体，看 <paramref name="pattern"/> 是否还在。</summary>
    private static bool AnchorHolds(
        IReadOnlyList<string> files, string method, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.Compiled);

        foreach (var path in files)
        {
            var lines = File.ReadAllLines(path);
            var (start, end) = MethodRange(lines, 0, lines.Length - 1,
                l => l.Contains("void " + method + "("));

            if (start < 0)
            {
                continue;
            }

            for (var i = start; i <= end; i++)
            {
                if (regex.IsMatch(lines[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>扫一批源码，列出 handler 之外"往控件上写用户可改属性"的地方。</summary>
    private static List<(string File, int Line, string Property, string Receiver)> OutsideHandlerWrites(
        IReadOnlyList<string> files)
    {
        var result = new List<(string, int, string, string)>();

        foreach (var path in files)
        {
            var rel = Relative(path);

            if (rel.Contains("/Handlers."))
            {
                continue;
            }

            foreach (var hit in OutsideHandlerWrites(File.ReadAllLines(path).ToList(), rel))
            {
                result.Add(hit);
            }
        }

        return result;
    }

    private static List<(string File, int Line, string Property, string Receiver)> OutsideHandlerWrites(
        IReadOnlyList<string> raw, string rel)
    {
        var result = new List<(string, int, string, string)>();
        var body = StripComments(raw);
        var joined = string.Join("\n", body);

        for (var i = 0; i < body.Count; i++)
        {
            foreach (Match m in WriteProp.Matches(body[i]))
            {
                var property = m.Groups[2].Value;

                if (!Array.Exists(UserEditable, p => p == property))
                {
                    continue;
                }

                // 接收者必须被声明成控件类型。`hook.Value = x` 这种内部字段不是
                // 控件属性写回，不挡掉它，真正的违约会淹在一堆误报里。
                foreach (var type in ControlTypes)
                {
                    if (!Regex.IsMatch(joined,
                            @"\b" + Regex.Escape(type) + @"\s+" + Regex.Escape(m.Groups[1].Value) + @"\b"))
                    {
                        continue;
                    }

                    result.Add((rel, i + 1, property, type));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 第六道：<b>以控件为键的表</b>，或者借 <c>reconciler.Rebind*</c> 存进去的东西，
    /// 登记了就必须在 <c>Unmount</c> 里摘掉。
    /// </summary>
    /// <remarks>
    /// 第二道只管 <c>EchoGuard</c> 的 <c>Expect → Forget</c>。但 handler 里按控件建的表
    /// 远不止回声登记：<c>Callbacks</c>（用户回调）、<c>Targets</c>（受控目标）、
    /// <c>Rebuilding</c>（正在换 items）、<c>Carriers</c>（承载控件）、
    /// <c>States</c>（虚拟化状态），以及 <c>reconciler.RebindTextChanged</c> 那一族
    /// （表在 <c>Reconciler</c> 里，handler 这边看不见，但登记/摘除是同一对动作）。
    /// <para>
    /// 这些表现在<b>都是弱键的</b>，所以漏摘不再是"永久钉住整棵子树"，
    /// 而是退化成"条目要等控件被回收才消失"（<c>Forget</c> 的注释里记过这个降级）。
    /// 真正会被这条契约挡住的是另一半：<c>Reconciler</c> 那几张表是
    /// <c>Dictionary&lt;控件, 委托&gt;</c>——<b>强键</b>，漏摘就是真泄漏。
    /// </para>
    /// <para>
    /// 判据（和前五道一样刻意做简单）：类里出现 <c>&lt;表&gt;.Set(</c> /
    /// <c>&lt;表&gt;[x] =</c> / <c>reconciler.RebindXxx(x, 非 null)</c>，
    /// 该类的 <c>Unmount</c> 里就必须出现<b>同一张表</b>的 <c>Remove</c>，
    /// 或同一个 <c>RebindXxx(x, null)</c>。
    /// </para>
    /// </remarks>
    private static void EveryPerControlTableIsReleased()
    {
        var files = SourceFiles();

        if (files.Count == 0)
        {
            Program.Check("定位到 handler 源码", false, "找不到 Reactor.uwp/Internal/Handlers.*.cs");
            return;
        }

        var violations = new List<string>();
        var managed = 0;

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path));
            managed += ClassSpans(lines).Count(span => Registrations(lines, span).Count > 0);

            foreach (var violation in TableLifetimeViolations(lines))
            {
                violations.Add($"{Path.GetFileName(path)}  {violation}");
            }
        }

        Program.Check(
            $"定位到按控件建表的类（扫描 {files.Count} 个文件，命中 {managed} 个）",
            managed >= 18,
            "命中的类数骤降 = 改了正则或换了写法，这条契约正在悄悄失聪");

        Program.Check(
            "每个按控件建表（或借 reconciler 重绑）的类，都在 Unmount 里摘掉了",
            violations.Count == 0,
            violations.Count == 0
                ? null
                : $"{violations.Count} 处：{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

        // 合成样本：健康的不能报警、缺摘除的必须报警、字符串为键的缓存不许误报。
        Program.Check(
            "合成样本：登记了也摘了 → 不报警",
            TableLifetimeViolations(DemoTable(DemoTableFlaw.None)).Count == 0,
            "健康的样本也被判违约 —— 判据太宽");

        Program.Check(
            "合成样本：忘了 Remove → 必须报警",
            TableLifetimeViolations(DemoTable(DemoTableFlaw.MissingRemove)).Count == 1,
            "漏摘也照样绿 —— 这条契约是摆设");

        Program.Check(
            "合成样本：整个 Unmount 都不写 → 必须报警",
            TableLifetimeViolations(DemoTable(DemoTableFlaw.NoUnmount)).Count == 1,
            "没有 Unmount 也照样绿 —— 新建 handler 忘了写 Unmount 就漏过去了");

        Program.Check(
            "合成样本：字符串为键的缓存不该归这条管",
            TableLifetimeViolations(DemoTable(DemoTableFlaw.StringKeyed)).Count == 0,
            "把 string→DataTemplate 这种缓存也算成\"按控件建的表\" —— 会淹没真正的违约");

        Program.Check(
            "合成样本：Arm 了却在 Unmount 里没 Disarm → 必须报警",
            TableLifetimeViolations(DemoTable(DemoTableFlaw.MissingDisarm)).Count == 1,
            "就绪闸的订阅留在控件身上没人解 —— 这类\"服务类登记\"此前没有任何契约在守");

        MutationsOfRealTableReleases();
    }

    /// <summary>
    /// 反向对照：逐个删掉真源码里那些"摘除"行，扫描器必须<b>每一次</b>都报警。
    /// </summary>
    /// <remarks>
    /// 合成样本只证明"它认得这种形状"，证明不了"现在这份源码在它视野内"。
    /// 这里对每个类的每一条摘除语句分别开刀——注意是<b>逐条</b>而不是每个类一次，
    /// 否则"删掉一行还剩另一行"会被误判成扫描器瞎。
    /// </remarks>
    private static void MutationsOfRealTableReleases()
    {
        var files = SourceFiles();
        var mutations = 0;
        var missed = new List<string>();

        foreach (var path in files)
        {
            var lines = StripComments(File.ReadAllLines(path));

            foreach (var span in ClassSpans(lines))
            {
                var (unmount, end) = MethodRange(
                    lines, span.Start, span.End, l => l.Contains("override void Unmount("));

                if (unmount < 0)
                {
                    continue;
                }

                for (var i = end; i >= unmount; i--)
                {
                    if (!RemoveCall.IsMatch(lines[i]) &&
                        !RebindRelease.IsMatch(lines[i]) &&
                        !ServiceDisarm.IsMatch(lines[i]))
                    {
                        continue;
                    }

                    mutations++;

                    var mutated = new List<string>(lines);
                    mutated.RemoveAt(i);

                    if (TableLifetimeViolations(mutated).Count == 0)
                    {
                        missed.Add($"{Path.GetFileName(path)}  {span.Name}  第 {i + 1} 行：{lines[i].Trim()}");
                    }
                }
            }
        }

        Program.Check(
            $"删掉真源码里任意一条摘除语句都必须报警（{mutations} 处）",
            mutations >= 20 && missed.Count == 0,
            missed.Count == 0
                ? null
                : $"{missed.Count} 处溜过去了：{Environment.NewLine}{string.Join(Environment.NewLine, missed)}");
    }

    /// <summary>扫一份源码，列出"登记了却没在 Unmount 里摘掉"的类。</summary>
    private static List<string> TableLifetimeViolations(IReadOnlyList<string> lines)
    {
        var violations = new List<string>();

        foreach (var span in ClassSpans(lines))
        {
            var registered = Registrations(lines, span);

            if (registered.Count == 0)
            {
                // 这个类不按控件建表（多数 handler 只是布局/容器），不归这条契约管。
                continue;
            }

            registered.ExceptWith(Releases(lines, span));

            if (registered.Count == 0)
            {
                continue;
            }

            var names = registered
                .Select(DescribeRegistration)
                .OrderBy(n => n, StringComparer.Ordinal);

            violations.Add($"{span.Name}  Unmount 里没摘掉：{string.Join("、", names)}");
        }

        return violations;
    }

    /// <summary>把一条登记说成人话：前缀 <c>T:</c> 是表、<c>R:</c> 是 reconciler 重绑、<c>S:</c> 是服务类。</summary>
    private static string DescribeRegistration(string registered)
    {
        var body = registered[2..];

        return registered[0] switch
        {
            'R' => "reconciler.Rebind" + body,
            'S' => body + ".Arm / .Disarm",
            _ => body,
        };
    }

    /// <summary>顶层类的行范围（<c>abstract</c> / 泛型基类也算，别再让整个类掉出视野）。</summary>
    private static List<(int Start, int End, string Name)> ClassSpans(IReadOnlyList<string> lines)
    {
        var starts = new List<(int Index, string Name)>();

        for (var i = 0; i < lines.Count; i++)
        {
            var m = TopClass.Match(lines[i]);

            if (m.Success)
            {
                starts.Add((i, m.Groups[1].Value));
            }
        }

        var spans = new List<(int, int, string)>(starts.Count);

        for (var k = 0; k < starts.Count; k++)
        {
            var end = k + 1 < starts.Count ? starts[k + 1].Index - 1 : lines.Count - 1;
            spans.Add((starts[k].Index, end, starts[k].Name));
        }

        return spans;
    }

    /// <summary>
    /// 类里<b>按控件建的表</b>的字段名：声明是 <c>WeakTable/Dictionary/ConditionalWeakTable</c>
    /// 且键类型不是 <c>string</c> / <c>Type</c> 这类标量。
    /// </summary>
    /// <remarks>
    /// 排除标量键是必要的：<c>Dictionary&lt;string, DataTemplate&gt;</c> 这种模板缓存
    /// 不是"按控件建的表"，把它算进来只会淹没真正的违约。
    /// </remarks>
    private static HashSet<string> TableFields(IReadOnlyList<string> lines, int start, int end)
    {
        var joined = string.Join('\n', lines.Skip(start).Take(end - start + 1));
        var fields = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in TableDeclaration.Matches(joined))
        {
            var key = m.Groups[1].Value.Split(',')[0].Trim();
            var bare = key.Contains('<') ? key[(key.LastIndexOf('<') + 1)..].Trim() : key;

            if (ScalarKeyTypes.Contains(bare))
            {
                continue;
            }

            fields.Add(m.Groups[2].Value);
        }

        return fields;
    }

    /// <summary>类里的<b>登记</b>动作：以控件为键写表，或借 reconciler 重绑。</summary>
    private static HashSet<string> Registrations(
        IReadOnlyList<string> lines, (int Start, int End, string Name) span)
    {
        var fields = TableFields(lines, span.Start, span.End);
        var joined = string.Join('\n', lines.Skip(span.Start).Take(span.End - span.Start + 1));
        var registered = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in TableSet.Matches(joined))
        {
            if (fields.Contains(m.Groups[1].Value))
            {
                registered.Add("T:" + m.Groups[1].Value);
            }
        }

        foreach (Match m in TableIndexer.Matches(joined))
        {
            if (fields.Contains(m.Groups[1].Value))
            {
                registered.Add("T:" + m.Groups[1].Value);
            }
        }

        foreach (Match m in RebindRegister.Matches(joined))
        {
            registered.Add("R:" + m.Groups[1].Value);
        }

        // 服务类登记：`ReadyGate.Arm(control, …)` 那一路。表在<b>服务类自己那里</b>，
        // handler 这边看不见，看得见的只有"登记了"这个动作——所以按动作配对来判。
        foreach (Match m in ServiceArm.Matches(joined))
        {
            registered.Add("S:" + m.Groups[1].Value);
        }

        return registered;
    }

    /// <summary>类里 <c>Unmount</c> 中的<b>摘除</b>动作。</summary>
    private static HashSet<string> Releases(
        IReadOnlyList<string> lines, (int Start, int End, string Name) span)
    {
        var released = new HashSet<string>(StringComparer.Ordinal);
        var (start, end) = MethodRange(
            lines, span.Start, span.End, l => l.Contains("override void Unmount("));

        if (start < 0)
        {
            return released;
        }

        var unmount = string.Join('\n', lines.Skip(start).Take(end - start + 1));

        foreach (Match m in RemoveCall.Matches(unmount))
        {
            released.Add("T:" + m.Groups[1].Value);
        }

        foreach (Match m in RebindRelease.Matches(unmount))
        {
            released.Add("R:" + m.Groups[1].Value);
        }

        foreach (Match m in ServiceDisarm.Matches(unmount))
        {
            released.Add("S:" + m.Groups[1].Value);
        }

        // 有些 handler 的摘除藏在自己的 `Rebind(control, null)` 助手里
        // （PasswordBox / AutoSuggestBox / NumberBox 都是这个写法），
        // 把它的方法体也算进来，否则它们会被误判成"没摘"。
        if (!LocalRebindRelease.IsMatch(unmount))
        {
            return released;
        }

        var (helper, helperEnd) = MethodRange(lines, span.Start, span.End, l => RebindHelper.IsMatch(l));

        if (helper < 0)
        {
            return released;
        }

        var body = string.Join('\n', lines.Skip(helper).Take(helperEnd - helper + 1));

        foreach (Match m in RemoveCall.Matches(body))
        {
            released.Add("T:" + m.Groups[1].Value);
        }

        return released;
    }

    /// <summary>一个虚构的"按控件建表"的 handler，按 <paramref name="flaw"/> 弄坏一处。</summary>
    private static List<string> DemoTable(DemoTableFlaw flaw)
    {
        var lines = new List<string>
        {
            "internal sealed class DemoTableHandler : ElementHandler<DemoTableElement, DemoTable>",
            "{",
            "    private static readonly WeakTable<DemoTable, int> Slots = new();",
            "",
            "    protected override void Update(Reconciler r, DemoTableElement o, DemoTableElement n, DemoTable control)",
            "    {",
            "        Slots.Set(control, n.Slot);",
            "    }",
            "",
            "    protected override void Unmount(Reconciler reconciler, DemoTable control)",
            "    {",
            "        Slots.Remove(control);",
            "    }",
            "}",
        };

        switch (flaw)
        {
            case DemoTableFlaw.MissingRemove:
                lines.RemoveAll(l => l.Contains("Slots.Remove(control);"));
                return lines;

            case DemoTableFlaw.NoUnmount:
                var at = lines.FindIndex(l => l.Contains("protected override void Unmount("));
                var head = lines.GetRange(0, at);
                head.Add("}");
                return head;

            case DemoTableFlaw.MissingDisarm:
                // 服务类登记：表在 ReadyGate 自己那里，handler 这边只写这一对动作。
                var mount = lines.FindIndex(l => l.Contains("private static readonly WeakTable"));
                lines[mount] = "    private static readonly WeakTable<DemoTable, int> Slots = new();";
                lines.InsertRange(6, new[] { "        ReadyGate.Arm(control, c => Slots.Set(c, 0));" });
                return lines;

            case DemoTableFlaw.StringKeyed:
                // 字符串为键的模板缓存：生命周期跟控件无关，不该归这条契约管。
                lines.RemoveAll(l => l.Contains("WeakTable<DemoTable, int>"));
                lines.RemoveAll(l => l.Contains("Slots."));
                lines.Insert(2, "    private static readonly Dictionary<string, DataTemplate> Templates = new();");
                lines.InsertRange(6, new[]
                {
                    "        Templates[control.Name] = null;",
                });
                return lines;

            default:
                return lines;
        }
    }

    /// <summary>把 <see cref="DemoTable"/> 弄坏的方式。</summary>
    private enum DemoTableFlaw
    {
        None,
        MissingRemove,
        NoUnmount,
        MissingDisarm,
        StringKeyed,
    }

    private static readonly Regex TableDeclaration = new(
        @"(?:static\s+)?(?:readonly\s+)?(?:WeakTable|Dictionary|ConditionalWeakTable)\s*<([^;]*?)>\s*([A-Z]\w*)\s*(?:=|;)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex TableSet =
        new(@"(?<![.\w])([A-Z]\w*)\.Set\(\s*[A-Za-z_]\w*\s*[,)]", RegexOptions.Compiled);

    private static readonly Regex TableIndexer =
        new(@"(?<![.\w])([A-Z]\w*)\[\s*[A-Za-z_]\w*\s*\]\s*=", RegexOptions.Compiled);

    private static readonly Regex RebindRegister =
        new(@"reconciler\.Rebind(\w+)\(\s*[A-Za-z_]\w*\s*,\s*(?!null\b)", RegexOptions.Compiled);

    private static readonly Regex RebindRelease =
        new(@"reconciler\.Rebind(\w+)\(\s*[A-Za-z_]\w*\s*,\s*null\s*\)", RegexOptions.Compiled);

    private static readonly Regex RemoveCall =
        new(@"(?<![.\w])([A-Z]\w*)\.Remove\(", RegexOptions.Compiled);

    private static readonly Regex LocalRebindRelease =
        new(@"(?<!\.)Rebind\(\s*[A-Za-z_]\w*\s*,\s*null\s*\)", RegexOptions.Compiled);

    private static readonly Regex RebindHelper =
        new(@"^\s*(private|protected|internal|public).*\bRebind\s*\(", RegexOptions.Compiled);

    /// <summary>服务类的<b>登记</b>动作：表在服务类自己那里，handler 这边只看得见这个动作。</summary>
    private static readonly Regex ServiceArm =
        new(@"(?<![.\w])([A-Z]\w*)\.Arm\(\s*[A-Za-z_]\w*", RegexOptions.Compiled);

    /// <summary>服务类的<b>摘除</b>动作，与 <see cref="ServiceArm"/> 成对。</summary>
    private static readonly Regex ServiceDisarm =
        new(@"(?<![.\w])([A-Z]\w*)\.Disarm\(", RegexOptions.Compiled);

    /// <summary>这些键类型说明这张表<b>不是</b>按控件建的（模板缓存、样式表之类）。</summary>
    private static readonly HashSet<string> ScalarKeyTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "string", "int", "long", "short", "byte", "uint", "ulong", "sbyte", "bool",
            "char", "double", "float", "decimal", "Type", "Guid", "DateTime", "ResourceKey",
            "object", "Enum",
        };

    private static List<string> SourceFiles()
    {
        var root = RepoRoot();
        var result = new List<string>();

        if (root is null)
        {
            return result;
        }

        var inner = Path.Combine(root, "Reactor.uwp", "Internal");
        result.AddRange(Directory.GetFiles(inner, "Handlers.*.cs"));
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>
    /// 第七道用：<c>Reactor.uwp</c> <b>全树</b>的源码（<c>obj</c> / <c>bin</c> 除外）。
    /// </summary>
    /// <remarks>
    /// 前六道都只扫 <c>Handlers.*.cs</c>。第七道存在的理由就是"那个口径没人验过"，
    /// 所以它不能沿用同一个口径——否则等于没问。
    /// </remarks>
    private static List<string> FrameworkFiles()
    {
        var root = RepoRoot();
        var result = new List<string>();

        if (root is null)
        {
            return result;
        }

        var dir = Path.Combine(root, "Reactor.uwp");

        result.AddRange(Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains("\\obj\\") && !p.Contains("\\bin\\")));
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>路径相对 <c>Reactor.uwp</c> 目录的写法（正斜杠），便于写进登记表。</summary>
    private static string Relative(string path)
    {
        var marker = Path.Combine("Reactor.uwp") + Path.DirectorySeparatorChar;
        var i = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return i < 0 ? Path.GetFileName(path) : path[(i + marker.Length)..].Replace('\\', '/');
    }

    private static string? RepoRoot()
    {
        // 从当前目录往上一级级找，认定的标志是：目录里同时有 Reactor.uwp 和 tests。
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
