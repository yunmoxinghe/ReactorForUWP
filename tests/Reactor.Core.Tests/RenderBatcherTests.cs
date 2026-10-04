using System;
using Microsoft.UI.Reactor.Core;

namespace Reactor.Core.Tests;

/// <summary>
/// 重渲染批处理的回归测试。
/// </summary>
/// <remarks>
/// 锁的性质：<b>一轮消息泵内的 N 次 setState 只该渲染一次</b>。
/// 没有批处理时，UI 线程上连发 N 次 setState 就是 N 次"整棵树 Render + Patch"——
/// 一个事件处理里改三个状态，代价直接翻三倍；列表里逐项 setState 更是 O(n) 棵树的量。
/// <para>
/// 这几条断言守的是"合并"本身：很容易在后来的重构里被改回"收到就渲染"，
/// 而那种写法既不会报错、也不会让任何现有用例失败，只能靠这里挡住。
/// </para>
/// </remarks>
internal static class RenderBatcherTests
{
    public static void Run()
    {
        Program.Section("重渲染批处理");

        var batcher = new RenderBatcher();

        // 1) 第一次请求排上队
        Program.Check("首次请求被调度", batcher.TrySchedule());
        Program.Check("排队中", batcher.IsQueued);

        // 2) 同一轮内的后续请求被合并（这是本轮修复的核心）
        Program.Check("第二次请求被合并（不重复渲染）", !batcher.TrySchedule());
        Program.Check("第三次请求仍被合并", !batcher.TrySchedule());
        Program.Check("合并期间始终只排了一次", batcher.IsQueued);

        // 3) 渲染跑完后复位，下一轮重新接受
        batcher.Reset();
        Program.Check("渲染结束后复位", !batcher.IsQueued);
        Program.Check("下一轮的第一个请求又能排上", batcher.TrySchedule());

        // 4) 连续 N 次：只有第一次返回 true
        batcher.Reset();
        var scheduled = 0;
        for (var i = 0; i < 100; i++)
        {
            if (batcher.TrySchedule())
            {
                scheduled++;
            }
        }

        Program.Check("连发 100 次只渲染 1 次", scheduled == 1);

        // 5) 复位是幂等的（渲染结束后可能被调用多次）
        batcher.Reset();
        batcher.Reset();
        Program.Check("重复复位不改变状态", !batcher.IsQueued);

        // ── 以下守"渲染死循环"：Render() 或 effect 里同步 setState ──
        // 官方 Reactor 靠 [ThreadStatic] 的同步递归深度挡（MaxRerenderReentrancy = 50）；
        // 我们是 dispatcher 排队，永远不会同步递归，照搬那个计数器会一直读到 0、保护失效。
        // 等价物是"连续自触发轮数"：每一轮渲染期间又排下一轮就 +1，断一轮就清零。

        // 6) 每轮渲染都自触发 → 到上限抛
        var looping = new RenderBatcher();
        var threw = false;
        try
        {
            for (var i = 0; i < RenderBatcher.MaxSelfTriggerRenders + 5; i++)
            {
                looping.BeginRender();   // 渲染开始（清掉本轮排队标志）
                looping.TrySchedule();   // 渲染期间又 setState —— 自触发
                looping.EndRender();     // 渲染结束，下一轮已排上，计数留着
            }
        }
        catch (InvalidOperationException ex)
        {
            threw = ex.Message.Contains("Render loop detected", StringComparison.Ordinal);
        }

        Program.Check("渲染期间连续自触发到上限即抛", threw);

        // 7) 只有"连续"才算：中间夹一轮不自触发就要清零，不能误报
        var mixed = new RenderBatcher();
        var threwMixed = false;
        try
        {
            for (var i = 0; i < 200; i++)
            {
                mixed.BeginRender();
                mixed.TrySchedule();     // 自触发 +1
                mixed.EndRender();

                mixed.BeginRender();     // 这一轮渲染期间不 setState
                mixed.EndRender();       // 没有排队 → 计数清零
            }
        }
        catch (InvalidOperationException)
        {
            threwMixed = true;
        }

        Program.Check("自触发不连续就不算死循环", !threwMixed);
        Program.Check("断一轮之后计数清零", mixed.SelfTriggerCount == 0);

        // 8) 正常的事件驱动 setState（不在渲染期间）永不进计数
        var normal = new RenderBatcher();
        for (var i = 0; i < 100; i++)
        {
            normal.TrySchedule();
            normal.BeginRender();
            normal.EndRender();
        }

        Program.Check("渲染外的连发不进自触发计数", normal.SelfTriggerCount == 0);
        Program.Check("渲染结束后退出渲染态", !normal.IsRendering);
    }
}
