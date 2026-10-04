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
    }
}
