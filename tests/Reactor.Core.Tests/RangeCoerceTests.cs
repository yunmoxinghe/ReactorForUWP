using System;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 「改区间 → 受控值被夹 → 那一发被当成用户输入」的回归测试。
/// </summary>
/// <remarks>
/// <para>
/// 每一条都自带反向对照：关掉静默窗，同一段序列必须变红。多出来的那一条
/// <see cref="RangeCoerceSim.CoerceOnRangeWrite"/> 是<b>无关对照</b>——把"控件会夹取"
/// 这个前提也关掉，若假回调照样出现，说明它们压根不是这一发夹取引起的，
/// 那这条修法就修错了地方。
/// </para>
/// </remarks>
internal static class RangeCoerceTests
{
    public static void Run()
    {
        Program.Section("RangeCoerce / 改区间把受控值夹了");


        // 1) 定向：Max 100 → 40，值 80 被夹到 40。
        //    有窗：回调 0 次、state 还是 80（没人动过它）。
        var with = Scenario(maxTo: 40, silence: true);
        Program.Expect("有窗：夹了但一声没吭", 0, with.Spurious);
        Program.Expect("有窗：夹取确实发生了", 1, with.Clamps);
        Program.Expect("有窗：state 没被凭空改写", 80.0, with.State);
        Program.Expect("有窗：控件停在夹取后的值", 40.0, with.Value);

        var without = Scenario(maxTo: 40, silence: false);
        Program.Expect("无窗：夹取冒出去一次", 1, without.Spurious);
        Program.Expect("无窗：state 被夹出来的值改写", 40.0, without.State);

        // 2) 定向：Min 0 → 90，值 80 被夹到 90（另一个方向）。
        var raised = Scenario(minTo: 90, silence: false);
        Program.Expect("无窗：抬高下界同样冒出去一次", 1, raised.Spurious);
        Program.Expect("无窗：控件被夹到下界", 90.0, raised.Value);
        Program.Expect("有窗：抬高下界一声没吭", 0, Scenario(minTo: 90, silence: true).Spurious);

        // 3) 定向：两个边界各夹一次 → 无窗时是<b>两发</b>。
        //    这一条是"为什么不能用 Expect 代替静默窗"的判决：一次登记装不下两发。
        var both = BothBounds(silence: false);
        Program.Expect("无窗：两个边界各夹一次 = 两发", 2, both.Spurious);
        Program.Expect("无窗：夹取次数也是两发", 2, both.Clamps);
        Program.Expect("有窗：两发都不出声", 0, BothBounds(silence: true).Spurious);

        // 4) 定向：区间变了但值仍在区间内 → 本来就不该有回调，窗也不能误伤。
        var inside = Scenario(maxTo: 90, silence: true);
        Program.Expect("值仍在区间内：不夹", 0, inside.Clamps);
        Program.Expect("值仍在区间内：也不回调", 0, inside.Spurious);
        Program.Expect("值仍在区间内：无窗同样没有回调（这一档不该有差别）", 0,
            Scenario(maxTo: 90, silence: false).Spurious);

        // 5) 定向：窗只在渲染里开，用户拖动照旧回调（修法不许顺手把真输入也吞了）。
        var drag = new RangeCoerceSim();
        drag.Mount(0, 100, 50, _ => { });
        drag.Update(0, 40, null, _ => { });          // 这一轮把值夹到 40
        drag.UserDrag(30);
        Program.Expect("用户拖动照旧回调一次", 1, drag.UserCalls);
        Program.Expect("用户拖动不会被算成渲染期冒出去的", 0, drag.Spurious);
        Program.Expect("用户拖动的值真的进了 state", 30.0, drag.State);

        // 6) 无关对照：把"控件会夹取"这个前提也关掉，无窗也该是 0。
        //    红了就说明那些假回调另有出处，这条修法认错了病因。
        var noCoerce = new RangeCoerceSim { SilenceWindow = false, CoerceOnRangeWrite = false };
        noCoerce.Mount(0, 100, 80, _ => { });
        noCoerce.Update(0, 40, 80, _ => { });
        Program.Expect("不夹取时即便无窗也没有假回调", 0, noCoerce.Spurious);

        // 7) 定向：声明值越界（state 说 10，区间已经抬到 20..100）。
        //    控件会把它夹成 20 再回读出来 —— 登记声明值的旧写法对不上这一发。
        var coerced = DeclaredOutOfRange(coercedExpect: true);
        Program.Expect("登记夹取后的值：这一发被认出来是自己写的", 0, coerced.Spurious);
        Program.Expect("登记夹取后的值：控件停在夹取后的值", 20.0, coerced.Value);

        var stale = DeclaredOutOfRange(coercedExpect: false);
        Program.Expect("退回登记声明值：这一发被当成用户输入", 1, stale.Spurious);
        Program.Expect("退回登记声明值：state 被夹出来的值改写", 20.0, stale.State);

        // 8) 随机序列：20000 条 × 8 步，三档对照各自计量。
        RandomFuzz();

        Program.Section("RangePolicy / 夹取判据（纯函数，穷举）");

        Program.Expect("区间内原样返回", 50.0, RangePolicy.Coerce(50, 0, 100));
        Program.Expect("低于下界 → 下界", 20.0, RangePolicy.Coerce(10, 20, 100));
        Program.Expect("高于上界 → 上界", 100.0, RangePolicy.Coerce(120, 20, 100));
        Program.Expect("正好落在边界 → 原样", 20.0, RangePolicy.Coerce(20, 20, 100));
        Program.Check("NaN（NumberBox 的『空』）不参与夹取", double.IsNaN(RangePolicy.Coerce(double.NaN, 20, 100)));

        // 幂等：夹过一次的值再夹一次必须不变——否则"写夹取后的值"会每轮都写成新值。
        var idempotent = true;
        for (var v = -20; v <= 120; v += 7)
        {
            for (var min = 0; min <= 100; min += 25)
            {
                for (var max = min; max <= 100; max += 25)
                {
                    var once = RangePolicy.Coerce(v, min, max);
                    if (Math.Abs(RangePolicy.Coerce(once, min, max) - once) > 1e-9)
                    {
                        idempotent = false;
                    }
                }
            }
        }

        Program.Check("夹取是幂等的（写夹取后的值不会再被夹一次）", idempotent);
    }

    /// <summary>单边界场景：<c>Mount(0,100,80)</c> 之后改一个边界。</summary>
    private static RangeCoerceSim Scenario(double? minTo = null, double? maxTo = null, bool silence = true)
    {
        var sim = new RangeCoerceSim { SilenceWindow = silence };
        sim.Mount(0, 100, 80, _ => { });
        sim.Update(minTo ?? 0, maxTo ?? 100, 80, _ => { });
        return sim;
    }

    /// <summary>两边界场景：值 5，下界抬到 10（夹到 10），上界再压到 8（夹到 8）。</summary>
    private static RangeCoerceSim BothBounds(bool silence)
    {
        var sim = new RangeCoerceSim { SilenceWindow = silence };
        sim.Mount(0, 100, 5, _ => { });
        sim.Update(10, 8, 5, _ => { });
        return sim;
    }

    /// <summary>声明值越界：值 50，下界抬到 20（值仍在区间内、没被夹），随后声明 10。</summary>
    private static RangeCoerceSim DeclaredOutOfRange(bool coercedExpect)
    {
        var sim = new RangeCoerceSim { CoercedExpect = coercedExpect };
        sim.Mount(0, 100, 50, _ => { });
        sim.Update(20, 100, null, _ => { });   // 抬下界：50 仍在区间内，不夹
        sim.Update(20, 100, 10, _ => { });     // 声明 10 → 被夹成 20
        return sim;
    }

    /// <summary>跑一批随机序列，返回"有多少条序列出现了假回调 / 一共多少次 / 夹了几次"。</summary>
    private static (int Bad, int Spurious, int Clamps) Pass(bool silence, bool coerced)
    {
        var rng = new Random(20261006);
        int bad = 0, spurious = 0, clamps = 0;

        for (int seq = 0; seq < 20000; seq++)
        {
            var sim = new RangeCoerceSim { SilenceWindow = silence, CoercedExpect = coerced };
            sim.Mount(0, 100, rng.Next(0, 101), _ => { });

            for (int step = 0; step < 8; step++)
            {
                var min = rng.Next(0, 101);
                var max = rng.Next(0, 101);

                if (max < min)
                {
                    (min, max) = (max, min);
                }

                // 声明值：一半跟着 state（受控写回），一半不写（null = 本轮不改它）
                double? declared = rng.Next(2) == 0 ? sim.State : null;
                sim.Update(min, max, declared, _ => { });

                if (rng.Next(3) == 0)
                {
                    sim.UserDrag(rng.Next(0, 101));
                }
            }

            clamps += sim.Clamps;
            spurious += sim.Spurious;

            if (sim.Spurious > 0)
            {
                bad++;
            }
        }

        return (bad, spurious, clamps);
    }

    private static void RandomFuzz()
    {
        var fixedBoth = Pass(silence: true, coerced: true);
        Program.Expect("随机序列：两处修法都在 → 一条假回调都没有", 0, fixedBoth.Bad);
        Program.Check(
            "随机序列：这批序列真的夹到了值（否则上面那条是空转）",
            fixedBoth.Clamps > 0,
            $"clamped={fixedBoth.Clamps}");

        // 反向对照一：只关掉静默窗（管"改区间把值夹了"那一半）。
        var noWindow = Pass(silence: false, coerced: true);
        Program.Check(
            "反向对照：只关掉静默窗，同一批序列必须出现假回调",
            noWindow.Bad > 0,
            $"受影响序列={noWindow.Bad} 假回调={noWindow.Spurious}");

        // 反向对照二：只退回收敛前的登记写法（管"受控值被夹"那一半）。
        var noCoerced = Pass(silence: true, coerced: false);
        Program.Check(
            "反向对照：只把『登记夹取后的值』退回旧写法，同一批序列必须出现假回调",
            noCoerced.Bad > 0,
            $"受影响序列={noCoerced.Bad} 假回调={noCoerced.Spurious}");

        // 两个开关各管一段：关掉静默窗时，那一半的假回调不该被另一半的修法盖住。
        Program.Check(
            "两个开关各管一段（各自关掉都有失败，不是同一个开关在兜两处）",
            noWindow.Bad > 0 && noCoerced.Bad > 0 && noWindow.Spurious != noCoerced.Spurious,
            $"无窗={noWindow.Spurious} 无收敛={noCoerced.Spurious}");

        Console.WriteLine($"       （随机序列：夹取 {fixedBoth.Clamps} 次；"
            + $"只关静默窗 → {noWindow.Bad} 条序列 / {noWindow.Spurious} 次假回调；"
            + $"只退回收敛前写法 → {noCoerced.Bad} 条序列 / {noCoerced.Spurious} 次假回调）");
    }
}
