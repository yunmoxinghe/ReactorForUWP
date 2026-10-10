using System;
using System.Collections.Generic;

namespace Reactor.Core.Tests;

/// <summary>
/// 「一次手势 → 恰好一次用户回调」。
/// </summary>
/// <remarks>
/// <b>真机症状（<c>probe-realclick-0302.log</c>）。</b>同一个 <c>RadioButtons#5</c>，
/// 一次真人点击之后落了<b>两条</b>一模一样的回调：
/// <code>
/// RadioButtons#5 取消选中（AddedItems 无实项），吞 -1
/// RadioButtons#5 纠正回受控值（控件停在 -1）
/// RadioButtons#5 取消选中（AddedItems 无实项），吞 -1   ← 又一组
/// RadioButtons#5 纠正回受控值（控件停在 -1）
/// RadioButtons#5 → 用户回调 SelectedIndex=1
/// RadioButtons#5 → 用户回调 SelectedIndex=1            ← 10ms 后又一条，同值
/// </code>
/// 用户点了一次，<c>OnSelectedIndexChanged</c> 被调了两次、值还相同。
/// 后果是 <c>setState</c> 走两遍、落盘两遍、渲染两遍——界面上表现为"跳一下又跳回去"，
/// 而终态不变量完全看不出来（第二次是同值，state 与控件最后都规规矩矩）。
///
/// <b>为什么现有不变量抓不到它。</b>
/// <see cref="ControlledSelectionSim.Faithful"/> 看的是终态一致、
/// <see cref="ControlledSelectionSim.LastClickHonored"/> 看的是"那次点击有没有进 state"、
/// <see cref="ControlledSelectionSim.Explained"/> 看的是控件值有没有主人——
/// 三条都是<b>终态</b>判据。重复回调不改变终态，只改变过程，
/// 所以这三条一条都不会红。<b>重复是"过程"上的病，必须用过程判据抓。</b>
///
/// <b>判据的写法。</b>不看"回调总数等于点击数"（点当前项按设计不回调，INV6，
/// 两者本就不等），只看<b>过程</b>：回调序列里<b>相邻两项不得同值</b>。
/// 用户点 A → 回调 A，点 B → 回调 B，序列是 A,B,A,B…… 相邻永远不同；
/// 出现 A,A 相邻只可能是同一手势产出了两发。<c>CallbackValues</c> 按到达顺序记，
/// 所以这一条能直接读到。
/// </remarks>
internal static class DoubleCallbackTests
{
    /// <summary>回调序列里相邻两项同值（= 同一手势回调了两次）的情形计数，跨序列累计。</summary>
    private static int _duplicates;

    public static void Run()
    {
        Program.Section("受控选中 / 一次手势不得回调两次");

        Deterministic();
        RandomSweep();

        Program.Check(
            "随机序列：回调序列里没有相邻同值（扫到的重复数以 0 为准）",
            _duplicates == 0,
            $"重复 {_duplicates} 次");
    }

    // ── 确定性用例 ───────────────────────────────────────────────
    //
    // 每条都覆盖真机日志里出现过的一段形状。它们单独看都很普通，
    // 凑在一起是为了让"双回调"这个病没法藏在某一条缝隙里。

    private static void Deterministic()
    {
        // 已就绪、换项点击、两种到达顺序：一次点击 = 一次回调。
        foreach (var cancelFirst in new[] { true, false })
        {
            var sim = new ControlledSelectionSim();
            sim.Mount(0);
            sim.Load();
            sim.Click(1, cancelFirst);
            sim.Drain();

            Program.Check(
                $"已就绪 · 点第 1 项（cancelFirst={cancelFirst}）→ 恰好一次回调",
                sim.CallbackCount == 1,
                $"回调 {sim.CallbackCount} 次，值 [{string.Join(",", sim.CallbackValues)}]");
        }

        // 未就绪窗口里点击（折叠区刚展开那一瞬）：那一发会被"未就绪"吞掉、
        // 记下来、等进树补发。补发与随后可能的正常放行<b>必须只兑现一个</b>。
        // 真机日志里 RadioButtons#5 的两条回调正好是这个形状。
        {
            var sim = new ControlledSelectionSim();
            sim.Mount(0);
            sim.BeginRepeaterLoad();     // repeater 进树，但框架认为还没就绪
            sim.Click(1);                // 这一发被吞 + 记下
            sim.CompleteLoad();          // 进树：补发
            sim.Drain();

            Program.Check(
                "未就绪窗口点击 → 进树补发后只回调一次（不得补发 + 正常放行各一次）",
                sim.CallbackCount == 1,
                $"回调 {sim.CallbackCount} 次，值 [{string.Join(",", sim.CallbackValues)}]\n      trace: {string.Join("\n      ", sim.Trace)}");
        }

        // 点当前已选中项：按设计<b>不</b>产生回调（INV6）。
        // 这一条是反向锚点：它证明"回调数 ≠ 点击数"，所以上面的判据
        // 不能写成"回调数等于点击数"。
        {
            var sim = new ControlledSelectionSim();
            sim.Mount(1);
            sim.Load();
            sim.Click(1);
            sim.Drain();

            Program.Check(
                "点当前已选中项 → 不回调（INV6 锚点）",
                sim.CallbackCount == 0,
                $"回调 {sim.CallbackCount} 次，值 [{string.Join(",", sim.CallbackValues)}]");
        }

        // 连续换项点击：回调序列必须跟着走，且不得出现相邻同值。
        {
            var sim = new ControlledSelectionSim();
            sim.Mount(0);
            sim.Load();

            foreach (var i in new[] { 1, 2, 0, 2, 1 })
            {
                sim.Click(i);
                sim.Drain();
            }

            Program.Check(
                $"连续换项点击 → 回调序列无相邻同值（实际 [{string.Join(",", sim.CallbackValues)}]）",
                !HasAdjacentDuplicate(sim.CallbackValues),
                $"值 [{string.Join(",", sim.CallbackValues)}]");
        }

        // 无人监听期间点击：不回调，但也不能因为"没人接"而在进树时补发两次。
        {
            var sim = new ControlledSelectionSim();
            sim.Mount(0);
            sim.SetCallback(false);
            sim.BeginRepeaterLoad();
            sim.Click(1);
            sim.CompleteLoad();
            sim.SetCallback(true);
            sim.Drain();

            Program.Check(
                "无人监听期间点击 → 恢复监听后不得凭空补出两次",
                !HasAdjacentDuplicate(sim.CallbackValues),
                $"回调 {sim.CallbackCount} 次，值 [{string.Join(",", sim.CallbackValues)}]\n      trace: {string.Join("\n      ", sim.Trace)}");
        }
    }

    // ── 随机穷举 ─────────────────────────────────────────────────

    /// <summary>
    /// 随机序列：让到达顺序、就绪窗口、回调有无、items 重建互相交叉，
    /// 冲出确定性用例想不到的形状。
    /// </summary>
    /// <remarks>
    /// <b>为什么非要随机。</b>双回调的成因是"两条路径都兑现了同一次意图"
    /// （补发 + 正常放行 / 两道队列各跑一遍），它只在特定到达顺序下发生，
    /// 靠人想场景穷举不出来——这正是这套仿真存在的理由。
    /// </remarks>
    private static void RandomSweep()
    {
        var rng = new Random(20261010);
        var worst = new List<string>();

        for (var iteration = 0; iteration < 4000; iteration++)
        {
            var sim = new ControlledSelectionSim();
            var items = 3;

            sim.Mount(rng.Next(items));

            // 就绪窗口随机开合：有的序列先只让 repeater 进树（留窗口），
            // 有的直接完整 Load，有的更晚才 Load。
            var loadKind = rng.Next(3);
            if (loadKind == 1)
            {
                sim.BeginRepeaterLoad();
            }
            else if (loadKind == 2)
            {
                sim.Load();
            }

            if (rng.Next(4) == 0)
            {
                sim.SetCallback(rng.Next(2) == 0);
            }

            var steps = 1 + rng.Next(6);
            for (var s = 0; s < steps; s++)
            {
                var roll = rng.Next(10);

                if (roll < 6)
                {
                    sim.Click(rng.Next(items), rng.Next(2) == 0);
                }
                else if (roll < 8)
                {
                    if (loadKind == 1 || loadKind == 0)
                    {
                        sim.CompleteLoad();
                    }

                    sim.Load();
                }
                else if (roll == 8)
                {
                    items = 2 + rng.Next(3);
                    sim.RebuildItems(items);
                }
                else
                {
                    sim.SetCallback(rng.Next(2) == 0);
                }

                sim.Drain();
            }

            sim.Load();
            sim.Drain();

            if (HasAdjacentDuplicate(sim.CallbackValues))
            {
                _duplicates++;

                if (worst.Count < 3)
                {
                    worst.Add(
                        $"#{iteration} items={items} 回调 [{string.Join(",", sim.CallbackValues)}]\n" +
                        $"      trace:\n        {string.Join("\n        ", sim.Trace)}");
                }
            }
        }

        if (worst.Count > 0)
        {
            Console.WriteLine("      重复回调样本（前 3 条）：");
            foreach (var w in worst)
            {
                Console.WriteLine($"      {w}");
            }
        }
    }

    /// <summary>回调序列里有没有相邻同值——同一手势回调两次的直接指纹。</summary>
    private static bool HasAdjacentDuplicate(IReadOnlyList<int> values)
    {
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] == values[i - 1])
            {
                return true;
            }
        }

        return false;
    }
}
