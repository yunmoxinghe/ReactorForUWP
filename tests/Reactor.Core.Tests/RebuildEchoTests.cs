using System;

namespace Reactor.Core.Tests;

/// <summary>
/// <see cref="RebuildEchoSim"/> 的行为断言：定向、反向对照、随机序列。
/// </summary>
/// <remarks>
/// 两个开关<b>分别关</b>、分别计数。最关键的一条是 <c>ReapplyConverges</c>：
/// 它把「补发之后控件停在写入值」与「补发之后控件自己收敛成别的值」分成两档，
/// 只有后一档能把两种修法区分开 —— 修法承重的就是这一档。
/// </remarks>
internal static class RebuildEchoTests
{
    public static void Run()
    {
        Program.Section("重建 items 之后补发受控值的那一发");

        // 1) 值猜得准（补发之后控件就停在写入值）：
        //    两种形状都不漏 —— 此时 Expect / Consume 那一对已经兜住了。
        //    这一档是「修法不是靠多罩一段蒙对的」的证据。
        Program.Expect("值猜得准 + 罩到补发完：一声没吭", 0, Rebuild(suppressReapply: true).Spurious);
        Program.Expect("值猜得准 + 只罩重建：也不漏（Expect 兜住了）", 0, Rebuild(suppressReapply: false).Spurious);

        // 2) 值猜不准（补发之后 repeater 重算，选中收敛成别的值）：
        //    Expect 登记的值匹配不上，只有「罩到补发完」这一档接得住。
        var converged = Rebuild(suppressReapply: true, converges: false);
        Program.Expect("值猜不准 + 罩到补发完：一声没吭", 0, converged.Spurious);
        Program.Expect("值猜不准 + 罩到补发完：两发都落在区间里", 3, converged.Silenced);

        var leaked = Rebuild(suppressReapply: false, converges: false);
        Program.Expect("值猜不准 + 只罩重建：漏出去一次（修法就在这里承重）", 1, leaked.Spurious);

        // 3) 另一半：不补发 → 选中丢了，但那一类不该混进假回调。
        var noReapply = Rebuild(suppressReapply: true, reapply: false);
        Program.Expect("不补发：控件选中停在 -1（丢了）", -1, noReapply.Selected);
        Program.Expect("不补发：丢选中那一类冒出来了", 1, noReapply.Lost);
        Program.Expect("不补发：假回调那一类不受影响", 0, noReapply.Spurious);

        // 4) 无关对照：Clear 压根不会带走选中 → 一个开关都不用开也不会漏。
        var inert = Rebuild(suppressReapply: true, clearDrops: false);
        Program.Expect(
            "无关对照：控件不动选中时，重建全程一发都没多抛（只剩挂载那一发）",
            1,
            inert.Raised);
        Program.Expect("无关对照：自然也不漏", 0, inert.Spurious);

        // 5) 反向对照的另一头：真实用户操作一个都不能少。
        var clicked = Rebuild(suppressReapply: true);
        clicked.UserClick(3);
        Program.Expect("用户点了一下：回调照样出去", 1, clicked.UserCallbacks);
        Program.Expect("用户点了一下：没被算成假回调", 0, clicked.Spurious);
        Program.Expect("用户点了一下：state 跟着走", 3, clicked.State);

        // 6) 延后到下一帧的那一发：它是「取消选中」，判据零本来就接得住，
        //    与区间无关 —— 这一档证明第 19 节的理由不是"异步"。
        var deferred = Rebuild(suppressReapply: true, defer: true);
        Program.Expect("延后形状：那一发真的排到了下一帧", 1, deferred.Deferred);
        Program.Expect("延后形状：判据零接住了它，不用靠区间", 0, deferred.Spurious);

        // 7) 挂载期写的受控值，那一发延后到 repeater 加载完才回来 ——
        //    它回来时选中的是<b>实项</b>，判据零拦不住；
        //    而它早于控件自己的 Loaded，所以「就绪」这一问问得到它。
        var lateMount = MountedRestore(gateOnReady: false);
        Program.Expect("挂载期延后那一发：真的排到了后面", 1, lateMount.Deferred);
        Program.Expect("挂载期延后那一发 + 不问就绪：漏出去一次", 1, lateMount.Spurious);
        Program.Expect("挂载期延后那一发 + 问就绪：一声没吭", 0, MountedRestore(gateOnReady: true).Spurious);

        RandomFuzz();
    }

    /// <summary>一条「重建 items」的序列：挂载（5 项、选中第 2 项）→ 重建成 4 项。</summary>
    private static RebuildEchoSim Rebuild(
        bool suppressReapply,
        bool converges = true,
        bool reapply = true,
        bool defer = false,
        bool clearDrops = true,
        bool closeOnReturn = true)
    {
        var sim = new RebuildEchoSim
        {
            SuppressDuringRebuild = true,
            SuppressDuringReapply = suppressReapply,
            ReapplyAfterRebuild = reapply,
            ReapplyConverges = converges,
            DefersToNextFrame = defer,
            ClearDropsSelection = clearDrops,
            CloseMarkerOnReturn = closeOnReturn,
        };

        sim.Mount(count: 5, index: 2, callback: null);
        sim.Rebuild(count: 4, index: 2, callback: _ => { });

        if (defer)
        {
            sim.Tick();
        }

        return sim;
    }

    /// <summary>挂载（5 项、选中第 2 项），那一发延后到 repeater 加载完才回来。</summary>
    private static RebuildEchoSim MountedRestore(bool gateOnReady)
    {
        var sim = new RebuildEchoSim
        {
            MountDefersRestore = true,
            GateOnReady = gateOnReady,
        };

        // 回调必须挂着：真代码里"有没有人听"不该影响拦不拦（第五道契约），
        // 但没人听的时候自然也数不出"漏出去"。
        sim.Mount(count: 5, index: 2, callback: _ => { });
        sim.Tick();
        return sim;
    }

    /// <summary>
    /// 随机序列：修法全程开着的那一档，假回调必须<b>一次都没有</b>，
    /// 而真实用户操作必须<b>一次都不少</b>。
    /// </summary>
    private static void RandomFuzz()
    {
        var spurious = 0;
        var expectedUser = 0;
        var gotUser = 0;
        var lost = 0;
        var raised = 0;
        var random = new Random(20261006);

        for (var round = 0; round < 200; round++)
        {
            var sim = new RebuildEchoSim
            {
                SuppressDuringRebuild = true,
                SuppressDuringReapply = true,
                ReapplyAfterRebuild = true,
                ReapplyConverges = random.Next(2) == 0,
                DefersToNextFrame = random.Next(4) == 0,
            };

            var count = 3 + random.Next(6);
            var index = random.Next(count);
            sim.Mount(count, index, callback: null);

            for (var step = 0; step < 4; step++)
            {
                var nextCount = 2 + random.Next(8);

                if (nextCount == count)
                {
                    // 数量没变 → 不算重建，改点别的（声明值动了也不该有假回调）。
                    if (sim.UserClick(random.Next(nextCount)))
                    {
                        expectedUser++;
                    }

                    continue;
                }

                sim.Rebuild(nextCount, index, callback: _ => { });
                count = nextCount;

                if (sim.DefersToNextFrame)
                {
                    sim.Tick();
                }

                if (random.Next(3) == 0 && sim.UserClick(random.Next(count)))
                {
                    expectedUser++;
                }
            }

            spurious += sim.Spurious;
            lost += sim.Lost;
            raised += sim.Raised;
            gotUser += sim.UserCallbacks;
        }

        Program.Check($"随机序列（200 轮）：假回调一次都没有（实际 {spurious}）", spurious == 0);
        Program.Check($"随机序列：真实用户操作一次都不少（期望 {expectedUser}，实际 {gotUser}）", gotUser == expectedUser);
        Program.Check($"随机序列：选中一次都没丢（实际 {lost}）", lost == 0);
        Program.Check($"随机序列：模型真的在动（共抛 {raised} 发，不是空转）", raised > 200);
    }
}
