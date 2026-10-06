using System;
using System.Collections.Generic;

namespace Reactor.Core.Tests;

/// <summary>
/// 受控 <c>ToggleSwitch</c> 的闭环不变量与随机序列。
/// </summary>
/// <remarks>
/// 存在的理由：<c>ToggleSwitch</c> 是四个受控控件里<b>唯一没有第二道闸门</b>的——
/// <c>RadioButtons</c> / <c>ComboBox</c> 有 <c>SelectionGate</c> 挡着"未就绪/重建中/取消选中"，
/// <c>ToggleSwitch</c> 只有 <c>EchoGuard</c>。
/// 于是"一次用户操作恰好一次回调"这条底线，在它身上完全押在回声判据上——
/// 回声判据一旦吃掉真实操作，这里就是最先破的地方。
/// <para>
/// 它的 WinUI 行为也从源码坐实了（<c>tools/winui2-ref/dxaml/ToggleSwitch/</c>）：
/// <c>IsOn</c> 依赖属性一变就 <b>raise <c>Toggled</c></b>
/// （<c>ToggleSwitch_Partial.cpp:347-350</c> → <c>ToggleSwitch.g.cpp:335-351</c> →
/// <c>Partial.cpp:623-641</c>），且没有 <c>m_blockSelecting</c> 那类闸门。
/// </para>
/// </remarks>
internal static class ToggleEchoTests
{
    public static void Run()
    {
        Program.Section("受控 ToggleSwitch / 闭环不变量");

        SingleToggle();
        ControlledWriteIsNotUserInput();
        Converges();
        UncontrolledIsNotDraggedBack();
        TwoTogglesInOneFrame();
        LeakedRegistration();
        Fuzz();
    }

    /// <summary>INV-T1：一次用户拨动恰好一次回调，且值正确。</summary>
    private static void SingleToggle()
    {
        var sim = new ControlledToggleSim();
        sim.Mount(controlled: true, initial: true);
        sim.Load();
        sim.Drain();

        sim.UserToggle(false);
        var callbacks = sim.CallbackCount;
        sim.Drain();

        Program.Check(
            "INV-T1 一次用户拨动恰好一次回调",
            callbacks == 1 && sim.CallbackValues.Count == 1 && sim.CallbackValues[0] == false,
            $"回调 {callbacks} 次，值=[{string.Join(",", sim.CallbackValues)}] " +
            $"trace={string.Join(" | ", sim.Trace)}");

        Program.Check(
            "INV-T1b 拨动后 state 与控件一致",
            sim.State == false && sim.ControlIsOn == false,
            $"state={sim.State} 控件={sim.ControlIsOn}");
    }

    /// <summary>INV-T2：框架自己的受控下发不能冒成用户回调。</summary>
    private static void ControlledWriteIsNotUserInput()
    {
        var sim = new ControlledToggleSim();
        sim.Mount(controlled: true, initial: false);
        sim.Load();

        sim.SetState(true);
        sim.Drain();

        Program.Check(
            "INV-T2 受控下发不产生用户回调",
            sim.CallbackCount == 0,
            $"回调 {sim.CallbackCount} 次 trace={string.Join(" | ", sim.Trace)}");

        Program.Check(
            "INV-T2b 受控下发确实落到控件上",
            sim.ControlIsOn && sim.State,
            $"state={sim.State} 控件={sim.ControlIsOn}");
    }

    /// <summary>INV-T3：来回拨动都收敛。</summary>
    private static void Converges()
    {
        var sim = new ControlledToggleSim();
        sim.Mount(controlled: true, initial: false);
        sim.Load();
        sim.Drain();

        for (var i = 0; i < 6; i++)
        {
            sim.UserToggle(i % 2 == 0);
            sim.Drain();
        }

        Program.Check(
            "INV-T3 反复拨动后 state 与控件一致",
            sim.State == sim.ControlIsOn,
            $"state={sim.State} 控件={sim.ControlIsOn} " +
            $"trace={string.Join(" | ", sim.Trace)}");
    }

    /// <summary>
    /// INV-T4：非受控模式（元素不给 <c>IsOn</c>）下用户拨动要回调，
    /// 且框架不能把值拽回去。
    /// </summary>
    private static void UncontrolledIsNotDraggedBack()
    {
        var sim = new ControlledToggleSim();
        sim.Mount(controlled: false);
        sim.Load();

        sim.UserToggle(true);
        sim.Drain();
        sim.UserToggle(true);   // 同值再拨一次：用户来得及 Changing 两次也无妨

        Program.Check(
            "INV-T4 非受控：用户拨动被回调，且不被拽回",
            sim.CallbackCount == 1 && sim.ControlIsOn,
            $"回调 {sim.CallbackCount} 次 控件={sim.ControlIsOn} " +
            $"trace={string.Join(" | ", sim.Trace)}");
    }

    /// <summary>
    /// INV-T5：两次拨动落在<b>同一帧</b>里（渲染还在排队）时，两次都得回调。
    /// </summary>
    /// <remarks>
    /// 这条时序靠手点几乎造不出来：<c>RenderBatcher</c> 把渲染排到下一帧，
    /// 两次点击之间通常已经渲过一次。但慢机器、连击、或者触摸滑动的重复 pointer
    /// 事件都可能挤进同一帧——而它一旦发生，第二次<code>setState</code>还没落地，
    /// 正是回声判据最容易误伤的时候。
    /// </remarks>
    private static void TwoTogglesInOneFrame()
    {
        var sim = new ControlledToggleSim();
        sim.Mount(controlled: true, initial: true);
        sim.Load();
        sim.Drain();

        // 刻意不 Drain：两次点击落在同一帧内。
        sim.UserToggle(false);
        sim.UserToggle(true);
        sim.Drain();

        Program.Check(
            "INV-T5 同一帧内连拨两次：两次都回调",
            sim.CallbackCount == 2 &&
            sim.CallbackValues.Count == 2 &&
            sim.CallbackValues[0] == false &&
            sim.CallbackValues[1] == true,
            $"回调 {sim.CallbackCount} 次，值=[{string.Join(",", sim.CallbackValues)}] " +
            $"trace={string.Join(" | ", sim.Trace)}");

        Program.Check(
            "INV-T5b 同一帧内连拨两次后收敛到最后一次的值",
            sim.State && sim.ControlIsOn,
            $"state={sim.State} 控件={sim.ControlIsOn}");
    }

    /// <summary>
    /// INV-T6：回调为空时的受控写入会留下<b>无人消费</b>的回声登记，
    /// 之后把回调装回来，用户拨到同一个值会被误判成回声吞掉。
    /// </summary>
    /// <remarks>
    /// 这条不需要任何假设——<c>Toggled</c> 一定抛（源码已核实），
    /// 漏的不是事件，是<b>消费它的人</b>：handler 的 <c>Rebind</c> 把 <c>null</c>
    /// 存进 <c>Callbacks</c> 后，事件处理器里 <c>current?.Invoke(...)</c> 直接空转，
    /// <c>EchoGuard.Consume</c> 连一次都不会被调用。
    /// </remarks>
    private static void LeakedRegistration()
    {
        var fixedSim = RunLeak(seal: true);
        var broken = RunLeak(seal: false);

        Program.Check(
            "INV-T6 回调为空时写入不会留下陈旧登记",
            fixedSim.Calls == 2 && fixedSim.State == true,
            $"回调 {fixedSim.Calls} 次 state={fixedSim.State} " +
            $"trace={string.Join(" | ", fixedSim.Trace)}");

        Program.Check(
            "INV-T6 反向对照：不撤销登记时第二次拨动被误吞（证用例有效）",
            broken.Calls == 1 && broken.State == false,
            $"回调 {broken.Calls} 次 state={broken.State} " +
            $"—— 反向对照不成立说明这条用例没摸到 bug，" +
            $"trace={string.Join(" | ", broken.Trace)}");
    }

    private static (int Calls, bool State, IReadOnlyList<string> Trace) RunLeak(bool seal)
    {
        var sim = new ControlledToggleSim { SealEchoAfterWrite = seal };

        // 挂上去的时候没有 OnIsOnChanged：handler 里 Callbacks[control] = null。
        sim.Mount(controlled: true, initial: false, hasCallback: false);
        sim.Load();

        // 程序化改 state → 受控写入 → Toggled 抛了，但没人 Consume → 登记泄漏。
        sim.SetState(true);
        sim.Drain();

        // 后来的 render 给上了 OnIsOnChanged。
        sim.SetCallback(true);

        // 用户拨回来：false 会被回调（值不匹配陈旧登记），true 会被误吞。
        sim.UserToggle(false);
        sim.UserToggle(true);
        sim.Drain();

        return (sim.CallbackCount, sim.State, sim.Trace);
    }

    private static void Fuzz()
    {
        Program.Section("受控 ToggleSwitch / 随机序列");

        const int Sequences = 20000;

        // 两个分支都跑：源码核实为真值的那一支算断言，另一支算"如果哪天它变了，
        // 我们会在哪里破"。只跑一支等于把结论押在单一解释上。
        foreach (var firesWhenUnloaded in new[] { true, false })
        {
            var fixedFailures = RunSequences(firesWhenUnloaded, seal: true, out var firstFixed);
            var brokenFailures = RunSequences(firesWhenUnloaded, seal: false, out _);
            Program.Check(
                $"未套模板也会抛 Toggled = {firesWhenUnloaded}：{Sequences} 条序列全部满足不变量",
                fixedFailures == 0,
                fixedFailures == 0 ? null : $"{fixedFailures} 条失败，首个：{Environment.NewLine}{firstFixed}");

            Program.Check(
                $"未套模板也会抛 Toggled = {firesWhenUnloaded}：反向对照（不撤销登记）必须失败",
                brokenFailures > 0,
                brokenFailures > 0
                    ? $"反向对照失败 {brokenFailures} 条"
                    : "关掉修法也全绿——这批序列没覆盖到该 bug，不能作为回归防线");

            Console.WriteLine($"        该分支未修复时失败 {brokenFailures} 条");
        }
    }

    private static int RunSequences(bool firesWhenUnloaded, bool seal, out string firstFailure)
    {
        const int Sequences = 20000;
        const int Steps = 24;

        var rng = new Random(20261006);   // 固定种子：失败可复现
        var failures = 0;
        firstFailure = string.Empty;

        for (var s = 0; s < Sequences; s++)
        {
            // 一律受控挂载：非受控时 handler 压根不写控件，
            // "state 与控件一致"这条收敛不变量在那种模式下不成立（由 INV-T4 单独覆盖）。
            // 混进来只会混进一批假失败，把真信号淹掉。
            var sim = new ControlledToggleSim(firesWhenUnloaded) { SealEchoAfterWrite = seal };
            sim.Mount(controlled: true, initial: rng.Next(2) == 0, hasCallback: rng.Next(4) != 0);

            if (rng.Next(4) != 0)
            {
                sim.Load();
            }

            sim.Drain();

            for (var step = 0; step < Steps; step++)
            {
                switch (rng.Next(5))
                {
                    case 0:
                    case 1:
                    {
                        var value = rng.Next(2) == 0;
                        var effective = sim.ControlIsOn != value;
                        var expected = effective && sim.HasCallback ? 1 : 0;
                        var before = sim.CallbackCount;

                        sim.UserToggle(value);
                        var delta = sim.CallbackCount - before;

                        if (delta != expected)
                        {
                            Record(
                                ref failures,
                                ref firstFailure,
                                $"第 {step} 步的拨动回调了 {delta} 次（换值={effective} " +
                                $"有回调={sim.HasCallback} 期望 {expected} 次）",
                                sim);
                        }

                        break;
                    }

                    case 2:
                        sim.SetState(rng.Next(2) == 0);
                        break;

                    case 3:
                        sim.SetCallback(rng.Next(4) != 0);
                        break;

                    default:
                        sim.Load();
                        break;
                }

                // 随机 Drain：模拟渲染批处理有时在这一步就落到控件上，有时攒着。
                if (rng.Next(3) != 0)
                {
                    if (sim.Drain() >= 64)
                    {
                        Record(ref failures, ref firstFailure, $"第 {step} 步渲染轮数失控", sim);
                    }
                }
            }

            sim.Drain();

            // 回调为空期间的用户操作天经地义不会转告 state，那种序列本就该分开。
            if (sim.UnreportedUserActions == 0 && sim.State != sim.ControlIsOn)
            {
                Record(
                    ref failures,
                    ref firstFailure,
                    $"不收敛 state={sim.State} 控件={sim.ControlIsOn}",
                    sim);
            }
        }

        return failures;
    }

    private static void Record(ref int failures, ref string firstFailure, string why, ControlledToggleSim sim)
    {
        failures++;

        if (firstFailure.Length == 0)
        {
            firstFailure = why + Environment.NewLine + "    " +
                string.Join(Environment.NewLine + "    ", sim.Trace);
        }
    }
}
