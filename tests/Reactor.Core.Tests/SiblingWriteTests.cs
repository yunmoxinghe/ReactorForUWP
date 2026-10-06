using System;

namespace Reactor.Core.Tests;

/// <summary>
/// <see cref="SiblingWriteSim"/> 的行为断言：定向、反向对照、随机序列。
/// </summary>
/// <remarks>
/// 两条判据<b>各管一段</b>，两个开关<b>分别关</b>、分别计数：
/// 只断言"关掉修法必须红"的话，分不清哪个开关坏了。
/// </remarks>
internal static class SiblingWriteTests
{
    public static void Run()
    {
        Program.Section("改兄弟属性 / 重建 items 把受控选中值牵走了");

        // 1) 定向：改兄弟属性到 Single（只留第一个，选中从 2 收敛到 0）→ 那一发不该冒出去。
        var single = Scenario(siblingTo: 2, silence: true, reapply: true);
        Program.Expect("有窗：收敛那一档一声没吭", 0, single.Spurious);
        Program.Expect("有窗：受控值没被污染", 2, single.State);
        Program.Expect("有窗：控件选中值被补回声明值", 2, single.Selected);

        Program.Expect(
            "无窗：同一笔冒出去一次",
            1,
            Scenario(siblingTo: 2, silence: false, reapply: true).Spurious);

        // 2) 定向：改到 None 那一档（清空，选中变 -1）。
        Program.Expect("有窗：清空那一档也不冒", 0, Scenario(siblingTo: 1, silence: true, reapply: true).Spurious);
        Program.Expect("无窗：清空那一档照样冒", 1, Scenario(siblingTo: 1, silence: false, reapply: true).Spurious);

        // 3) 定向：重建 items —— 那一发不该冒，且<b>选中不该丢</b>。
        var rebuild = Rebuild(reapply: true, silence: true);
        Program.Expect("重建：那一发不冒出去", 0, rebuild.Spurious);
        Program.Expect("重建：控件选中值回到声明值（没丢）", 2, rebuild.Selected);
        Program.Expect("重建：丢选中的帧数是 0", 0, rebuild.Lost);

        // 4) 反向对照 B：不补发 → 选中丢了（但假回调不会因此变多）。
        var noReapply = Rebuild(reapply: false, silence: true);
        Program.Expect("关掉补发：选中停在 -1（丢了）", -1, noReapply.Selected);
        Program.Check("关掉补发：丢选中那一类冒出来了", noReapply.Lost > 0, $"Lost={noReapply.Lost}");
        Program.Expect("关掉补发：假回调那一类不受影响", 0, noReapply.Spurious);

        // 5) 反向对照 A：不开窗 → 假回调冒出来（但那一次重建本身仍然不丢选中）。
        var noSilence = Rebuild(reapply: true, silence: false);
        Program.Check("关掉窗：假回调那一类冒出来了", noSilence.Spurious > 0, $"Spurious={noSilence.Spurious}");

        // 6) 无关对照：控件压根不会因为兄弟属性被写而改选中。
        var inert = Scenario(siblingTo: 0, silence: false, reapply: true, controlMoves: false);
        Program.Expect("无关对照：控件不动选中时，不开窗也没有假回调", 0, inert.Spurious);

        RandomFuzz();
    }

    /// <summary>
    /// 一条"改兄弟属性"的序列：挂载（3 项、选中第 2 项）→ 改一次兄弟属性。
    /// </summary>
    private static SiblingWriteSim Scenario(int siblingTo, bool silence, bool reapply, bool controlMoves = true)
    {
        var sim = new SiblingWriteSim
        {
            SilenceOnSiblingWrite = silence,
            ReapplyAfterRebuild = reapply,
            ControlMovesSelectionOnSiblingWrite = controlMoves,
        };

        sim.Mount(count: 3, index: 2, callback: null);
        sim.Update(count: 3, sibling: siblingTo, index: 2, callback: null);
        return sim;
    }

    /// <summary>一条"重建 items"的序列：挂载（5 项、选中第 2 项）→ 重建成 4 项。</summary>
    private static SiblingWriteSim Rebuild(bool reapply, bool silence)
    {
        var sim = new SiblingWriteSim
        {
            SilenceOnSiblingWrite = silence,
            ReapplyAfterRebuild = reapply,
        };

        sim.Mount(count: 5, index: 2, callback: null);
        sim.Update(count: 4, sibling: 0, index: 2, callback: null);
        return sim;
    }

    private static void RandomFuzz()
    {
        const int sequences = 20000;
        const int steps = 8;

        // 三个方向各跑一遍，用同一个种子 —— 序列完全一样，只有开关不同。
        var on = Run(sequences, steps, silence: true, reapply: true, controlMoves: true);
        var offSilence = Run(sequences, steps, silence: false, reapply: true, controlMoves: true);
        var offReapply = Run(sequences, steps, silence: true, reapply: false, controlMoves: true);
        // 无关对照只关「控件会自己动选中」这一个前提，其余两个开关照旧打开——
        // 否则它测的是"关掉补发"那一档，丢选中照样会红，对照就串了。
        var inertControl = Run(sequences, steps, silence: false, reapply: true, controlMoves: false);

        Program.Expect("随机序列：两个开关都开着时，一条假回调都没有", 0, on.Spurious);
        Program.Expect("随机序列：两个开关都开着时，选中一次都没丢", 0, on.Lost);

        Program.Check(
            "随机序列：这批序列真的动过选中（否则上面那两条是空转）",
            on.Moved > 0,
            $"Moved={on.Moved}");

        Program.Check(
            "反向对照 A：只关掉静默窗 → 假回调冒出来",
            offSilence.Spurious > 0,
            $"受影响序列={offSilence.Bad} 假回调={offSilence.Spurious}");

        Program.Check(
            "反向对照 B：只关掉补发 → 选中丢了",
            offReapply.Lost > 0,
            $"受影响序列={offReapply.Bad} 丢选中帧={offReapply.Lost}");

        // 两个开关各管一段的<b>硬证据</b>：两段数字不该相等，
        // 相等往往意味着同一个开关在兜两处，另一个是摆设。
        Program.Check(
            "两个开关各管一段：两段数字不相等",
            offSilence.Spurious != offReapply.Lost,
            $"关窗={offSilence.Spurious} 关补发={offReapply.Lost}");

        Program.Check(
            "分段正确：只关窗不影响丢选中那一类",
            offSilence.Lost == 0,
            $"Lost={offSilence.Lost}");

        Program.Check(
            "分段正确：只关补发不影响假回调那一类",
            offReapply.Spurious == 0,
            $"Spurious={offReapply.Spurious}");

        Program.Check(
            "无关对照：把「控件会自己动选中」这个前提也关掉 → 两类问题都消失",
            inertControl.Spurious == 0 && inertControl.Lost == 0,
            $"假回调={inertControl.Spurious} 丢选中={inertControl.Lost}");

        Console.WriteLine(
            $"       （随机序列：控件自己动了选中 {on.Moved} 次；" +
            $"只关窗 → {offSilence.Bad} 条序列 / {offSilence.Spurious} 次假回调；" +
            $"只关补发 → {offReapply.Bad} 条序列 / {offReapply.Lost} 帧丢选中）");
    }

    private static (int Spurious, int Lost, int Moved, int Bad) Run(
        int sequences, int steps, bool silence, bool reapply, bool controlMoves)
    {
        var rng = new Random(20261007);
        var spurious = 0;
        var lost = 0;
        var moved = 0;
        var bad = 0;

        for (var s = 0; s < sequences; s++)
        {
            var sim = new SiblingWriteSim
            {
                SilenceOnSiblingWrite = silence,
                ReapplyAfterRebuild = reapply,
                ControlMovesSelectionOnSiblingWrite = controlMoves,
            };

            var count = rng.Next(1, 7);
            var index = rng.Next(0, count);
            sim.Mount(count, index, callback: null);

            for (var step = 0; step < steps; step++)
            {
                var nextCount = rng.Next(3) == 0 ? rng.Next(1, 7) : count;
                var sibling = rng.Next(2);
                        // 声明值始终夹在区间里：越界兑现不了是另一条线（SelectionPolicy 单测管），
                // 这里混进来会淹没"选中被清空后没补回来"这一类。
                var declared = rng.Next(2) == 0
                    ? Math.Clamp(sim.State, -1, nextCount - 1)
                    : rng.Next(0, nextCount);

                sim.Update(nextCount, sibling, declared, callback: null);
                count = nextCount;

                if (rng.Next(4) == 0)
                {
                    sim.UserClick(rng.Next(0, count));
                }
            }

            spurious += sim.Spurious;
            lost += sim.Lost;
            moved += sim.Moved;

            if (sim.Spurious > 0 || sim.Lost > 0)
            {
                bad++;
            }
        }

        return (spurious, lost, moved, bad);
    }
}
