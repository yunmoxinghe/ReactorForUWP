using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 回声抑制<b>键的生命周期</b>：登记过一次的控件，能不能被垃圾回收。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么 GC 也能算作这一层的回归项。</b><see cref="EchoGuard"/> 的字段是
/// <c>static readonly</c>（每个受控属性一份），键是<b>真实控件</b>，值是它的期望回声。
/// 于是这张表的寿命比控件长：控件的回收与否，取决于有没有人记得在
/// <c>Unmount</c> 里调 <see cref="EchoGuard.Forget"/>。而 <c>Unmount</c> 会不会被
/// 调到，是 <c>Reconciler</c> 的事，<c>EchoGuard</c> 自己保证不了——
/// 换言之，这是把"不泄漏"押在<b>另一个模块的调用纪律</b>上的设计。
/// 项目里同一个坑在 <c>WeakTable</c> 上已经填过一次（见那份文件的注释），
/// 这里是把同样的判据补到回声这一份静态状态上。
/// </para>
/// <para>
/// <b>反向对照的做法。</b>本文件的量具是 GC，而 GC 断言最容易出的假象是
/// "写错了也能绿"（对象被提前/延后回收，测试都通过）。所以第一件事是拿
/// <see cref="StrongEchoGuard"/>——修复前那版 <c>Dictionary</c> 实现的<b>忠实副本</b>——
/// 喂给同一套断言，它必须被判成泄漏。它绿了，说明断言失敏，
/// 后面所有绿灯都不可信。
/// </para>
/// </remarks>
internal static class EchoLifetimeTests
{
    public static void Run()
    {
        Program.Section("EchoGuard / 键的生命周期");

        // ── 0) 量具自检：修复前的实现必须被这套断言抓出来 ────────────────
        var strong = new StrongEchoGuard();
        var heldByDictionary = Plant((c, v) => strong.Expect(c, v));
        Program.Check(
            "量具自检：Dictionary 版确实钉住控件（警报不为假）",
            !Reclaimed(heldByDictionary),
            "这条变绿说明 GC 断言失敏，下面所有通过都不可信");

        // ── 1) 正式断言：失去引用后，登记不再钉住控件 ────────────────────
        EchoStats.Reset();
        var guard = new EchoGuard();
        var released = Plant((c, v) => guard.Expect(c, v));
        Program.Check("弱键：控件不可达 → 登记随之消失", Reclaimed(released));

        var forgotten = Plant((c, v) =>
        {
            guard.Expect(c, v);
            guard.Forget(c);
        });
        Program.Check("弱键：Forget 之后控件可回收", Reclaimed(forgotten));

        // ── 2) 换存储不能把语义换丢：存活期间的抑制行为逐条复验 ──────────
        EchoStats.Reset();
        var live = new EchoGuard();
        var alive = new object();
        live.Expect(alive, "hello");
        Program.Check("存活期间：等值回读判定为回声", live.Consume(alive, "hello"));
        Program.Expect("matched 计数", 1L, EchoStats.Matched);

        EchoStats.Reset();
        live.Expect(alive, "world");
        Program.Check("存活期间：值不等不清登记（中间态）", !live.Consume(alive, string.Empty));
        Program.Check("存活期间：终值仍能识别为回声", live.Consume(alive, "world"));
        Program.Expect("mismatch 计数", 1L, EchoStats.Mismatch);

        EchoStats.Reset();
        live.Expect(alive, "keep");
        Program.Check("写入后无回声 → 撤销登记", live.CancelIfUnconsumed(alive));
        Program.Expect("sealed 计数", 1L, EchoStats.Sealed);
        var afterCancel = Plant((c, v) =>
        {
            live.Expect(c, v);
            live.CancelIfUnconsumed(c);
        });
        Program.Check("撤销之后控件可回收", Reclaimed(afterCancel));

        // null 值也是合法登记（string? 类型的受控属性），等号 ==null 不能翻车
        EchoStats.Reset();
        var nullable = new EchoGuard();
        var nullableBox = new object();
        nullable.Expect(nullableBox, null);
        Program.Check("null 值登记后能匹配（不与\"无登记\"混淆）", nullable.Consume(nullableBox, null));
        Program.Expect("matched 计数", 1L, EchoStats.Matched);

        // ── 3) 落到字节：泄漏的定义是"每轮都涨"，不是"一轮之后有大块" ────
        //    弱键表换来有一个一次性成本：ConditionalWeakTable 的内部容器不会随
        //    条目消失而收缩，第一轮之后就会留一块常驻。所以看单轮留存会把它
        //    误判成泄漏——真正的区分指标是<b>第二轮还涨不涨</b>：
        //    强键每轮线性增长，弱键第二轮几乎不涨。
        const long ToleranceBytes = 512 * 1024;
        var baseline = GrowthPerRound(null);
        var strongGrowth = GrowthPerRound((c, i) => strong.Expect(c, i));
        var weakGrowth = GrowthPerRound((c, i) => guard.Expect(c, i));

        Program.Check(
            "量具自检：Dictionary 版每轮线性增长",
            strongGrowth - baseline > ToleranceBytes,
            $"baseline={baseline} dictionary={strongGrowth}");
        Program.Check(
            "弱键：第二轮不再增长（键随控件回收）",
            weakGrowth - baseline <= ToleranceBytes,
            $"baseline={baseline} weak={weakGrowth} dictionary={strongGrowth}");

        Console.WriteLine(
            $"      参考数：每轮 2 万个一次性登记，Dictionary 留存 {strongGrowth} 字节，" +
            $"弱键留存 {weakGrowth} 字节（基线 {baseline}）");
    }

    /// <summary>
    /// 登记一个"外面没人再引用"的控件，回它的弱引用。
    /// </summary>
    /// <remarks>
    /// <c>NoInlining</c> 是必须的：内联会把 <c>box</c> 拖进调用方的栈帧，
    /// 让"何时不可达"变得不确定，断言会随机通过。
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Plant(Action<object, string?> expect)
    {
        var box = new object();
        expect(box, "hello");
        return new WeakReference(box);
    }

    /// <summary>强推 GC，返回目标是否已被回收。</summary>
    private static bool Reclaimed(WeakReference reference)
    {
        CollectFully();
        return !reference.IsAlive;
    }

    /// <summary>强推一轮 GC。</summary>
    private static void CollectFully()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    /// <summary>
    /// 跑两轮"登记一次就没人再管"的两万个控件，返回<b>第二轮</b>之后的净留存。
    /// </summary>
    /// <remarks>
    /// 先跑一轮不计量，是为了把双方的一次性成本（弱键表的容器、强键表的桶）
    /// 摊到预热里。<b>泄漏的定义是每一轮都在长</b>，一次性开销不是泄漏。
    /// </remarks>
    private static long GrowthPerRound(Action<object, int>? plant)
    {
        Round(plant);
        var before = GC.GetTotalMemory(true);
        Round(plant);
        CollectFully();
        return GC.GetTotalMemory(true) - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Round(Action<object, int>? plant)
    {
        const int Count = 20000;

        for (var i = 0; i < Count; i++)
        {
            var box = new object();
            plant?.Invoke(box, i);
        }
    }

    /// <summary>
    /// <b>修复前的实现，故意留在这里。</b>它不是生产代码的一支开关，而是这条
    /// GC 断言的<b>反向对照</b>：同样的玩法喂给它，必须被判成泄漏。
    /// 一旦它变绿，就说明这套断言已经测不出东西了。
    /// </summary>
    private sealed class StrongEchoGuard
    {
        private readonly Dictionary<object, (object? Value, long Tick)> _pending = new();

        public void Expect(object control, object? value) =>
            _pending[control] = (value, Environment.TickCount64);

        public bool Consume(object control, object? value) =>
            _pending.TryGetValue(control, out var p) && Equals(p.Value, value) &&
            _pending.Remove(control);

        public void Forget(object control) => _pending.Remove(control);
    }
}
