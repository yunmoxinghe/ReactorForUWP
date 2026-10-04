using System;
using System.Threading;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 受控属性「回声抑制」的回归测试。
/// </summary>
/// <remarks>
/// 覆盖的是 <see cref="EchoGuard"/> 里那条<b>非破坏性 Consume</b>语义。为什么必须锁死：
/// TextBox 的粘贴 / IME / selection replacement 会连发多个 <c>TextChanged</c>，
/// 旧实现无条件删除登记，第一发事件吃掉登记后，第二发就被当成真实用户输入
/// → 回调 → setState → 重渲染把用户刚粘进去的文本覆盖掉。
/// <para>
/// 这些用例不需要 UWP 运行时：<see cref="EchoGuard"/> 只依赖 <c>System.*</c>，
/// 所以能直接以 Link 方式编进本工程，在本地跑出结论，不必开 App。
/// </para>
/// </remarks>
internal static class EchoGuardTests
{
    public static void Run()
    {
        Program.Section("EchoGuard / 回声抑制");

        // 1) 最基础的回声：框架写入后控件回读等值 → 吞掉
        EchoStats.Reset();
        var guard = new EchoGuard();
        var box = new object();
        guard.Expect(box, "hello");
        Program.Check("写入后回读等值 → 判定为回声", guard.Consume(box, "hello"));
        Program.Expect("matched 计数", 1L, EchoStats.Matched);

        // 2) 没有登记 → 这就是用户输入，绝不能吞
        EchoStats.Reset();
        var fresh = new EchoGuard();
        Program.Check("无登记 → 不是回声", !fresh.Consume(box, "typed"));
        Program.Expect("notExpected 计数", 1L, EchoStats.NotExpected);

        // 3) 核心回归：mismatch 不清登记。
        //    粘贴会先清空选区（""）再落到终值，或 IME 合成期连发中间态；
        //    中间那一发不能把登记吃掉，否则终值那一发被误判成用户输入。
        EchoStats.Reset();
        var paste = new EchoGuard();
        paste.Expect(box, "hello");
        var midway = paste.Consume(box, string.Empty);
        var settled = paste.Consume(box, "hello");
        Program.Check(
            "中间态事件不消费登记，终值仍能识别为回声",
            !midway && settled,
            $"midway={midway} settled={settled}");
        Program.Expect("mismatch 计数", 1L, EchoStats.Mismatch);
        Program.Expect("matched 计数", 1L, EchoStats.Matched);

        // 4) 完整的粘贴链路：回声 → 真实输入 → 写回 → 回声
        EchoStats.Reset();
        var cycle = new EchoGuard();
        cycle.Expect(box, "hello");
        cycle.Consume(box, "hello");                                   // 上一轮渲染的回声
        Program.Check("粘贴产生的新值不被当成回声", !cycle.Consume(box, "world"));
        cycle.Expect(box, "world");                                    // 框架把新值写回控件
        Program.Check("写回后的事件被吞掉，不回环", cycle.Consume(box, "world"));

        // 5) TTL：陈旧登记宁可多回调一次，也不能吞掉真实用户操作
        EchoStats.Reset();
        var stale = new EchoGuard();
        stale.Expect(box, "hello");
        Thread.Sleep(1100);
        Program.Check("超窗后登记作废", !stale.Consume(box, "hello"));
        Program.Expect("expired 计数", 1L, EchoStats.Expired);

        // 6) 控件卸载后主动丢弃登记
        EchoStats.Reset();
        var forget = new EchoGuard();
        forget.Expect(box, "hello");
        forget.Forget(box);
        Program.Check("Forget 后不再识别为回声", !forget.Consume(box, "hello"));
        Program.Expect("Forget 后 matched 保持 0", 0L, EchoStats.Matched);

        Program.Section("RenderGeneration / 过期 render");

        var gen = new RenderGeneration();
        var first = gen.Next();
        var second = gen.Next();
        Program.Expect("current 推进到 2", 2L, gen.Current);
        Program.Check("旧 token 判定为过期", gen.IsStale(first));
        Program.Check("最新 token 未过期", !gen.IsStale(second));
    }
}
