using System;
using System.Collections.Generic;

namespace Reactor.Core.Tests;

/// <summary>
/// 「写了没人领的回声登记会吞掉下一次真实输入」这条 bug 的最小复现与随机序列。
/// </summary>
/// <remarks>
/// 存在理由：这个 bug 在三个受控控件上被修掉了（<c>ToggleSwitch</c> / <c>ComboBox</c> /
/// <c>RadioButtons</c>），但它<b>不是那三个控件的性质，是接线的性质</b>——
/// 所有 <c>Expect → 写属性 → Rebind</c> 的站点都带同一个病。
/// 这里是那条通则的回归防线：以后新加任何一个受控属性，只要照抄了同样的接线，<br/>
/// 要么守住不变量，要么让这里变红。
/// </remarks>
internal static class RebindEchoTests
{
    public static void Run()
    {
        Program.Section("受控属性 / 回调为空时的回声登记");

        LeakedRegistrationIsNotSwallowed();
        ControlledWriteIsEcho();
        Fuzz();
    }

    /// <summary>
    /// INV-R1：框架在<b>没有回调</b>时下发过的值，用户之后改回那个值<c>必须</c>还能回调。
    /// </summary>
    /// <remarks>
    /// 反向对照在 <see cref="RunOne"/> 里：关掉 <c>SealEchoAfterWrite</c> 同一段序列必须失败，
    /// 否则说明这段序列根本没碰到这个 bug，守不住任何东西。
    /// </remarks>
    private static void LeakedRegistrationIsNotSwallowed()
    {
        foreach (var keepSubscription in new[] { false, true })
        {
            var shape = keepSubscription ? "RadioButton 形状（订阅常驻）" : "TextBox 形状（已退订）";

            var (ok, detail) = RunOne(keepSubscription, seal: true);
            Program.Check(
                $"INV-R1 {shape}：回调为空时下发的值，用户改回它仍要回调",
                ok,
                ok ? null : detail);

            var (brokenOk, _) = RunOne(keepSubscription, seal: false);
            Program.Check(
                $"INV-R1 {shape}：反向对照（不撤销登记）必须失败",
                !brokenOk,
                brokenOk ? "关掉修法也全绿 → 这段序列没覆盖到该 bug" : null);
        }
    }

    /// <summary>
    /// 复现序列：挂载（无回调）→ 下发 b → 给上回调 → 用户改到 c → 用户改回 b。
    /// </summary>
    /// <returns>(是否满足不变量, 失败详情)。</returns>
    private static (bool Ok, string Detail) RunOne(bool keepSubscription, bool seal)
    {
        var sim = new ConditionalRebindSim
        {
            KeepSubscription = keepSubscription,
            SealEchoAfterWrite = seal,
        };

        sim.Mount("a", hasCallback: false);
        sim.SetState("b");
        sim.Drain();
        sim.SetCallback(true);
        sim.Drain();

        // 第一步：改到 c —— 与陈旧登记（b）不匹配，登记留着，c 正常回调。
        var beforeC = sim.CallbackCount;
        sim.UserEdit("c");
        var sawC = sim.CallbackCount - beforeC;

        // 第二步：改回 b —— 正好撞上那条没人领过的登记（b），不该被当成回声吞掉。
        var beforeB = sim.CallbackCount;
        sim.UserEdit("b");
        var sawB = sim.CallbackCount - beforeB;

        var ok = sawC == 1 && sawB == 1;

        return ok
            ? (true, string.Empty)
            : (false,
                $"改到 c 回调 {sawC} 次、改回 b 回调 {sawB} 次（都应正好 1 次）" +
                $"{Environment.NewLine}trace={string.Join(" | ", sim.Trace)}");
    }

    /// <summary>INV-R2：框架自己的下发一条回调都不能冒出去。</summary>
    private static void ControlledWriteIsEcho()
    {
        var sim = new ConditionalRebindSim { KeepSubscription = false };
        sim.Mount("a", hasCallback: true);

        for (var i = 0; i < 4; i++)
        {
            sim.SetState(i % 2 == 0 ? "b" : "c");
            sim.Drain();
        }

        Program.Check(
            "INV-R2 有回调时的受控下发不产生用户回调",
            sim.DrainCallbacks == 0 && sim.CallbackCount == 0,
            $"回调 {sim.CallbackCount} 次（下发中冒出的 {sim.DrainCallbacks} 次）" +
            $"{Environment.NewLine}trace={string.Join(" | ", sim.Trace)}");
    }

    private static void Fuzz()
    {
        Program.Section("受控属性 / 随机序列");

        const int Sequences = 20000;

        foreach (var keepSubscription in new[] { false, true })
        {
            var shape = keepSubscription ? "RadioButton 形状" : "TextBox 形状";

            var fixedFailures = RunSequences(keepSubscription, seal: true, out var firstFixed);
            var brokenFailures = RunSequences(keepSubscription, seal: false, out _);

            Program.Check(
                $"{shape}：{Sequences} 条序列全部满足不变量",
                fixedFailures == 0,
                fixedFailures == 0 ? null : $"{fixedFailures} 条失败，首个：{Environment.NewLine}{firstFixed}");

            Program.Check(
                $"{shape}：反向对照（不撤销登记）必须失败",
                brokenFailures > 0,
                brokenFailures > 0
                    ? $"反向对照失败 {brokenFailures} 条"
                    : "关掉修法也全绿——这批序列没覆盖到该 bug，不能作为回归防线");

            Console.WriteLine($"        该形状未修复时失败 {brokenFailures} 条 / {Sequences} 条");
        }
    }

    private static int RunSequences(bool keepSubscription, bool seal, out string firstFailure)
    {
        const int Sequences = 20000;
        const int Steps = 24;

        string[] alphabet = { "a", "b", "c", "d" };
        var rng = new Random(20261006);
        var failures = 0;
        firstFailure = string.Empty;

        for (var s = 0; s < Sequences; s++)
        {
            var sim = new ConditionalRebindSim
            {
                KeepSubscription = keepSubscription,
                SealEchoAfterWrite = seal,
            };

            sim.Mount(alphabet[rng.Next(alphabet.Length)], hasCallback: rng.Next(4) != 0);
            sim.Drain();

            for (var step = 0; step < Steps; step++)
            {
                switch (rng.Next(5))
                {
                    case 0:
                    case 1:
                    {
                        var value = alphabet[rng.Next(alphabet.Length)];
                        var effective = sim.ControlText != value;
                        var expected = effective && sim.Observing ? 1 : 0;
                        var before = sim.CallbackCount;

                        sim.UserEdit(value);
                        var delta = sim.CallbackCount - before;

                        if (delta != expected)
                        {
                            Record(
                                ref failures,
                                ref firstFailure,
                                $"第 {step} 步用户改成 '{value}' 回调了 {delta} 次" +
                                $"（换值={effective} 有人接={sim.Observing} 期望 {expected} 次）",
                                sim);
                        }

                        break;
                    }

                    case 2:
                        sim.SetState(alphabet[rng.Next(alphabet.Length)]);
                        break;

                    case 3:
                        sim.SetCallback(rng.Next(4) != 0);
                        break;

                    default:
                        break;
                }

                if (rng.Next(3) != 0)
                {
                    sim.Drain();
                }
            }

            sim.Drain();

            if (sim.DrainCallbacks != 0)
            {
                Record(
                    ref failures,
                    ref firstFailure,
                    $"受控下发过程中冒出 {sim.DrainCallbacks} 次回调（应为 0）",
                    sim);
            }

            // 回调为空期间的用户操作不会转告 state，那种分歧是对的，不计失败。
            if (sim.UnreportedUserActions == 0 && sim.State != sim.ControlText)
            {
                Record(
                    ref failures,
                    ref firstFailure,
                    $"收敛失败：state={sim.State} 控件={sim.ControlText}",
                    sim);
            }
        }

        return failures;
    }

    private static void Record(
        ref int failures,
        ref string firstFailure,
        string message,
        ConditionalRebindSim sim)
    {
        failures++;

        if (firstFailure.Length == 0)
        {
            firstFailure = $"{message}{Environment.NewLine}trace={string.Join(" | ", sim.Trace)}";
        }
    }
}
