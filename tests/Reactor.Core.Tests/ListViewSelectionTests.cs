using System;
using System.Collections.Generic;

namespace Reactor.Core.Tests;

/// <summary>
/// ListView / GridView —— 同一份受控接线的第 13、14 个站点。
/// </summary>
/// <remarks>
/// 与前面几组一样，每条修法都做成<b>开关</b>：默认（全部打开）必须全绿，
/// 关掉某一个必须变红，而且<b>只红它该管的那一条</b>——只断言"至少有一条变红"的话，
/// "哪个开关坏、坏在哪儿"这笔糊涂账会一直挂着，下一次改坏就没人知道是哪儿坏的。
/// </remarks>
internal static class ListViewSelectionTests
{
    private const int Sequences = 20000;
    private const int Steps = 24;
    private const int Seed = 20261006;

    public static void Run()
    {
        Program.Section($"ListView / GridView 受控 SelectedIndex（{Sequences} 条序列 × {Steps} 步）");

        ControlledWriteStaysSilent();
        UserInputStillPasses();
        OutOfRangeTargetIsNotWritten();
        ItemReplacementStaysSilent();
        Fuzz();
        ReverseNoEcho();
        ReverseNoRangeGuard();
        ReverseNoRebuildShield();
    }

    /// <summary>受控写回不许冒出去当用户回调。</summary>
    private static void ControlledWriteStaysSilent()
    {
        var sim = new ListViewSelectionSim(4);
        sim.SetState(2);
        sim.Drain();

        Program.Check(
            "受控写回没有回调给用户",
            sim.SpuriousCallbacks == 0 && sim.UserCallbacks == 0,
            $"冒出去 {sim.SpuriousCallbacks} 次 / 用户回调 {sim.UserCallbacks} 次");

        Program.Check(
            "受控写回之后控件跟上 state",
            sim.Selected == 2 && sim.State == 2,
            $"控件={sim.Selected} state={sim.State}");
    }

    /// <summary>
    /// 新增的抑制不能把真实用户输入一起吞掉——这是"修 A 坏 B"最容易发生的方向。
    /// </summary>
    private static void UserInputStillPasses()
    {
        var sim = new ListViewSelectionSim(4);
        sim.SetState(2);
        sim.UserClick(1);
        sim.Drain();

        Program.Check(
            "用户点击仍然回调一次，且 state 跟上",
            sim.UserCallbacks == 1 && sim.State == 1 && sim.Selected == 1,
            $"回调 {sim.UserCallbacks} 次，state={sim.State}，控件={sim.Selected}");

        // 第二次点同一个值：控件已经在那儿了，不抛事件，回调不该再涨。
        var before = sim.UserCallbacks;
        sim.UserClick(1);

        Program.Check(
            "点当前已选中项不产生第二次回调",
            sim.UserCallbacks == before,
            $"回调从 {before} 涨到 {sim.UserCallbacks}");
    }

    /// <summary>items 变少之后，受控目标越界——这时<b>不许写下去</b>。</summary>
    private static void OutOfRangeTargetIsNotWritten()
    {
        var sim = new ListViewSelectionSim(5);
        sim.SetState(4);
        sim.ReplaceItems(2);
        sim.Drain();

        Program.Check(
            "越界目标没有写进控件",
            sim.OutOfRangeWrites == 0 && sim.Selected == -1,
            $"越界写入 {sim.OutOfRangeWrites} 次，控件停在 {sim.Selected}");

        Program.Check(
            "state 没有被控件带偏（受控值仍是调用方写的那个）",
            sim.State == 4,
            $"state={sim.State}");
    }

    /// <summary>换数据源期间控件自己的选中变动不许冒到用户回调里。</summary>
    private static void ItemReplacementStaysSilent()
    {
        var sim = new ListViewSelectionSim(5) { ReaddsSelectionOnReset = true };
        sim.SetState(3);
        sim.ReplaceItems(5);
        sim.Drain();

        Program.Check(
            "整批换 items 期间没有凭空出现用户回调",
            sim.PhantomCallbacks == 0 && sim.UserCallbacks == 0,
            $"凭空回调 {sim.PhantomCallbacks} 次 / 用户回调 {sim.UserCallbacks} 次");
    }

    private static void Fuzz()
    {
        var bad = Tally(new Options());

        Program.Check(
            "随机序列：没有一项违约",
            bad.Total == 0,
            bad.Describe());

        Program.Check(
            "同一批序列真的走到了那几条路径上（避免「哪儿都没去」的假绿）",
            bad.Sequences == Sequences &&
            bad.Clicked > 0 && bad.ControlledWrites > 0 && bad.ItemReplacements > 0,
            $"点击 {bad.Clicked} 次 / 受控写回 {bad.ControlledWrites} 次 / 换 items {bad.ItemReplacements} 次");

    }

    private static void ReverseNoEcho()
    {
        var bad = Tally(new Options { SuppressEcho = false });

        Program.Check(
            $"反向对照：关掉回声抑制 → 受控写回被当成用户输入（{bad.Spurious} 条序列）",
            bad.Spurious > 0,
            "关掉之后仍然全绿 = 这条测试没在看着它");

        Program.Check(
            "反向对照只红它该管的那一条（不应顺带越界 / 重建期回调）",
            bad.OutOfRange == 0 && bad.Phantom == 0,
            bad.Describe());
    }

    private static void ReverseNoRangeGuard()
    {
        var bad = Tally(new Options { GuardRange = false });

        Program.Check(
            $"反向对照：关掉越界守卫 → 会把写不进去的值写下去（{bad.OutOfRange} 条序列）",
            bad.OutOfRange > 0,
            "关掉之后仍然全绿 = 这条测试没在看着它");

        Program.Check(
            "反向对照只红它该管的那一条",
            bad.Spurious == 0 && bad.Phantom == 0,
            bad.Describe());
    }

    private static void ReverseNoRebuildShield()
    {
        var bad = Tally(new Options { ShieldRebuild = false });

        Program.Check(
            $"反向对照：关掉重建期遮蔽 → 换 items 会凭空回调用户（{bad.Phantom} 条序列）",
            bad.Phantom > 0,
            "关掉之后仍然全绿 = 这条测试没在看着它");

        Program.Check(
            "反向对照只红它该管的那一条",
            bad.Spurious == 0 && bad.OutOfRange == 0,
            bad.Describe());
    }

    private static Tallies Tally(Options options)
    {
        var result = new Tallies();
        var rng = new Random(Seed);

        for (var s = 0; s < Sequences; s++)
        {
            var count = 2 + rng.Next(6);
            var sim = new ListViewSelectionSim(count)
            {
                SuppressEcho = options.SuppressEcho,
                GuardRange = options.GuardRange,
                ShieldRebuild = options.ShieldRebuild,
                // 两种容器形状都跑：Reset 之后 Selector 加不加回选中，结论都得成立。
                ReaddsSelectionOnReset = rng.Next(2) == 0,
            };

            for (var step = 0; step < Steps; step++)
            {
                switch (rng.Next(4))
                {
                    case 0:
                        sim.UserClick(rng.Next(-1, count + 1));
                        result.Clicked++;
                        break;

                    case 1:
                        sim.SetState(rng.Next(-1, count + 1));
                        result.ControlledWrites++;
                        break;

                    case 2:
                        count = rng.Next(0, 7);
                        sim.ReplaceItems(count);
                        result.ItemReplacements++;
                        break;

                    default:
                        sim.Drain();
                        break;
                }
            }

            sim.Drain();
            result.Sequences++;

            if (sim.SpuriousCallbacks > 0)
            {
                result.Spurious++;
            }

            if (sim.PhantomCallbacks > 0)
            {
                result.Phantom++;
            }

            if (sim.OutOfRangeWrites > 0)
            {
                result.OutOfRange++;
            }

            if (!sim.Explained || !sim.Faithful)
            {
                result.Unfaithful++;
                if (result.Examples.Count < 5)
                {
                    result.Examples.Add(
                        $"items={sim.ItemCount} state={sim.State} 控件={sim.Selected} " +
                        $"末次点击={sim.LastUserClick} explained={sim.Explained} faithful={sim.Faithful}");
                }
            }
        }

        return result;
    }

    /// <summary>这一批仿真的三个开关。</summary>
    private sealed class Options
    {
        public bool SuppressEcho { get; init; } = true;
        public bool GuardRange { get; init; } = true;
        public bool ShieldRebuild { get; init; } = true;
    }

    private sealed class Tallies
    {
        public int Sequences;
        public int Clicked;
        public int ControlledWrites;
        public int ItemReplacements;
        public int Spurious;
        public int Phantom;
        public int OutOfRange;
        public int Unfaithful;

        public List<string> Examples { get; } = new();

        public int Total => Spurious + Phantom + OutOfRange + Unfaithful;

        public string Describe() =>
            $"受控写回冒出回调 {Spurious} 条 / 换 items 凭空回调 {Phantom} 条 / " +
            $"越界写入 {OutOfRange} 条 / 控件停在没主人的值上 {Unfaithful} 条" +
            (Examples.Count == 0 ? "" : Environment.NewLine + "        " + string.Join(Environment.NewLine + "        ", Examples));
    }
}
