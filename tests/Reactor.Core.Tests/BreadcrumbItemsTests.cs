using System;
using System.Collections.Generic;
using System.Linq;

namespace Reactor.Core.Tests;

/// <summary>
/// 面包屑 <c>ItemsSource</c> 的回归网：
/// <b>每次下发必须是新引用，否则 <c>ItemsRepeater</c> 认为数据源没换。</b>
/// </summary>
/// <remarks>
/// <para>
/// 这条线此前<b>零覆盖</b>——是三个 bug 里唯一没有回归网守着的，而且它当时的"修复已生效"
/// 证据事后被证明是假的（跨进程 <c>GetHashCode</c>，见 <c>docs/winui2-source-notes.md</c>）。
/// 这里补的就是那份欠账。
/// </para>
/// <para>
/// 模型之外的那一半靠 <b>Link 真代码</b> 兜住：内容 diff 用的是 <c>Seq.SequenceEqual</c>、
/// 引用有没有变用的是 <c>ItemsSourcePolicy.WillTriggerRebuild</c>——两个都是
/// <c>Reactor.uwp</c> 里 handler 正在调用的那一份，不是抄过来的副本。
/// </para>
/// </remarks>
internal static class BreadcrumbItemsTests
{
    public static void Run()
    {
        Program.Section("面包屑 ItemsSource / 引用语义");

        FirstRender();
        ChangeAfterVisible();
        SameContentDoesNotRepublish();
        CollapsedThenVisible();
        ReverseControl();
        Fuzz();
    }

    /// <summary>BC1：挂载 → 进可视树，条目必须出现。</summary>
    private static void FirstRender()
    {
        var sim = new BreadcrumbSim();
        sim.Mount(new[] { "首页", "设置" });
        sim.ApplyTemplate();

        Program.Check(
            "BC1 挂载并 ApplyTemplate 后渲染出全部条目",
            sim.Rendered.SequenceEqual(new[] { "首页", "设置" }),
            $"渲染=[{Join(sim.Rendered)}] trace={Join(sim.Trace)}");
    }

    /// <summary>
    /// BC2：控件<b>已经可见</b>之后改内容 —— 这是原 bug 唯一会咬人的窗口。
    /// </summary>
    private static void ChangeAfterVisible()
    {
        var sim = new BreadcrumbSim();
        sim.Mount(new[] { "首页", "设置" });
        sim.ApplyTemplate();
        sim.Update(new[] { "首页", "设置", "外观" });

        Program.Check(
            "BC2 可见状态下改内容，界面跟着变",
            sim.Rendered.SequenceEqual(new[] { "首页", "设置", "外观" }),
            $"渲染=[{Join(sim.Rendered)}] trace={Join(sim.Trace)}");

        sim.Update(new[] { "首页" });

        Program.Check(
            "BC2b 减少条目同样要跟着变",
            sim.Rendered.SequenceEqual(new[] { "首页" }),
            $"渲染=[{Join(sim.Rendered)}] trace={Join(sim.Trace)}");
    }

    /// <summary>
    /// BC3：内容没变就不许重发 —— 换引用会拆掉整套条目重建，每轮都换 = 永远在重建。
    /// </summary>
    private static void SameContentDoesNotRepublish()
    {
        var sim = new BreadcrumbSim();
        sim.Mount(new[] { "首页", "设置" });
        sim.ApplyTemplate();

        var before = sim.RebuildCount;

        // 内容相同但实例不同 —— 必须被 diff 判成"没变"。
        sim.Update(new[] { "首页", "设置" });
        sim.Update(new List<string> { "首页", "设置" });

        Program.Expect("BC3 内容未变时 0 次重建", 0, sim.RebuildCount - before);
        Program.Expect("BC3b 内容未变时 0 次下发", 0, sim.PublishCount - 1);
    }

    /// <summary>
    /// BC4：<c>Collapsed</c> 容器里的面包屑（<c>repeater</c> 还没建）改内容，
    /// 之后进入可视树必须直接拿到<b>最新</b>内容 —— 不需要任何 Loaded 兜底。
    /// </summary>
    private static void CollapsedThenVisible()
    {
        var sim = new BreadcrumbSim();
        sim.Mount(new[] { "首页" });
        sim.Update(new[] { "首页", "设置" });
        sim.Update(new[] { "首页", "设置", "外观" });
        sim.ApplyTemplate();

        Program.Check(
            "BC4 折叠期间改的内容，ApplyTemplate 后一次补齐",
            sim.Rendered.SequenceEqual(new[] { "首页", "设置", "外观" }),
            $"渲染=[{Join(sim.Rendered)}] trace={Join(sim.Trace)}");
    }

    /// <summary>
    /// 反向对照：换成"复用同一个集合"的写法，同一批用例必须失败。
    /// 这里全绿 = 这些用例没摸到那个 bug，等于没写。
    /// </summary>
    private static void ReverseControl()
    {
        var visible = new BreadcrumbSim { ReuseCarrier = true };
        visible.Mount(new[] { "首页", "设置" });
        visible.ApplyTemplate();
        visible.Update(new[] { "首页", "设置", "外观" });

        Program.Check(
            "BC-R1 反向对照：复用载体时可见态改内容必须失败（页面停旧值）",
            !visible.Rendered.SequenceEqual(new[] { "首页", "设置", "外观" }),
            "复用载体却也更新了 —— 这条用例没摸到 bug");

        // BC1 / BC4 在复用写法下<b>本来就该通过</b>：还没 ApplyTemplate 时，
        // 重建读的是集合的当前内容，清了再加也能读到新的。
        // 这正是"必须先做一次别的操作才看得出来"的来源 —— bug 只在第二次更新之后才显形。
        var first = new BreadcrumbSim { ReuseCarrier = true };
        first.Mount(new[] { "首页", "设置" });
        first.ApplyTemplate();

        Program.Check(
            "BC-R2 反向对照：bug 的窗口确实是\"第二次更新之后\"（首次渲染不受影响）",
            first.Rendered.SequenceEqual(new[] { "首页", "设置" }),
            $"首次渲染就错了，那上面的模型跟源码说的不一致，trace={Join(first.Trace)}");
    }

    private const int Sequences = 20000;
    private const int Steps = 12;

    private static void Fuzz()
    {
        Program.Section($"面包屑 / 随机序列（{Sequences} 条 × {Steps} 步）");

        var fixedFailures = RunSequences(reuse: false, report: true, out var firstFixed);

        Program.Check(
            $"{Sequences} 条序列全部满足不变量（可见时渲染内容 == 最后一次下发）",
            fixedFailures == 0,
            fixedFailures == 0 ? null : $"{fixedFailures} 条失败，首个：{Environment.NewLine}{firstFixed}");

        var brokenFailures = RunSequences(reuse: true, report: false, out _);

        Program.Check(
            "反向对照：换成复用集合的写法后同一批序列必须失败",
            brokenFailures > 0,
            brokenFailures > 0 ? null : "复用写法也全绿 —— 这批序列没覆盖到该场景，不能作为回归防线");

        Console.WriteLine($"        未修复时同一批序列失败 {brokenFailures} 条");
    }

    private static int RunSequences(bool reuse, bool report, out string firstFailure)
    {
        const int MaxDepth = 4;

        var rng = new Random(20261006);
        var failures = 0;
        firstFailure = string.Empty;

        for (var s = 0; s < Sequences; s++)
        {
            // 一半序列从"还没进可视树"开始（折叠容器是面包屑的常态）。
            var sim = new BreadcrumbSim { ReuseCarrier = reuse };
            sim.Mount(Path(rng, rng.Next(1, MaxDepth)));

            if (rng.Next(2) == 0)
            {
                sim.ApplyTemplate();
            }

            var expected = Array.Empty<string>();

            for (var step = 0; step < Steps; step++)
            {
                switch (rng.Next(6))
                {
                    case 0:
                    case 1:
                    case 2:
                        expected = Path(rng, rng.Next(1, MaxDepth));
                        sim.Update(expected);
                        break;

                    case 3:
                        // 重复同一内容：不该触发任何下发。
                        sim.Update(expected);
                        break;

                    default:
                        sim.ApplyTemplate();
                        break;
                }
            }

            sim.ApplyTemplate();

            if (!sim.Rendered.SequenceEqual(expected))
            {
                failures++;

                if (report && firstFailure.Length == 0)
                {
                    firstFailure =
                        $"种子序列 #{s}：期望 [{Join(expected)}]，实际 [{Join(sim.Rendered)}]" +
                        Environment.NewLine + "    " +
                        string.Join(Environment.NewLine + "    ", sim.Trace);
                }
            }
        }

        return failures;
    }

    private static string[] Path(Random rng, int depth)
    {
        // 词汇表刻意做小：同内容重复出现的概率高，才能同时压到"diff 不该多发"那条。
        var vocabulary = new[] { "首页", "设置", "外观", "高级", "关于" };
        var result = new string[depth];

        for (var i = 0; i < depth; i++)
        {
            result[i] = vocabulary[rng.Next(vocabulary.Length)];
        }

        return result;
    }

    private static string Join<T>(IEnumerable<T> items) => string.Join(" | ", items);
}
