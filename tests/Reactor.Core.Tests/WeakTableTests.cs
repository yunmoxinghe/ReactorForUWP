using System;
using System.Runtime.CompilerServices;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// <see cref="WeakTable{TKey, TValue}"/> 的回归测试——锁的是"handler 的静态状态
/// 不许强引用控件"这条规则。
/// </summary>
/// <remarks>
/// <para>
/// 背景：框架里 handler 要跟着控件存回调 / 数据载体 / 滚动状态，以前是
/// <c>static Dictionary&lt;控件, 状态&gt;</c>——字典是静态的、键是控件的强引用，
/// 于是 <c>Unmount</c> 一旦没被调用（或将来有人新加 handler 忘了写），
/// 那棵控件子树就被永久钉住。现在统一换成弱键表：
/// <b>控件不可达 → 条目自动消失</b>，<c>Unmount</c> 里的 <c>Remove</c>
/// 从"不写就泄漏"降级为"提前释放"。
/// </para>
/// <para>
/// 这个测试项目不碰 WinRT，而 <c>ConditionalWeakTable</c> 是 BCL 类型，
/// 所以这条能在控制台里真跑起来（包括 GC 之后条目消失）。
/// </para>
/// </remarks>
internal static class WeakTableTests
{
    private sealed class Key
    {
        public int Id;
    }

    public static void Run()
    {
        Program.Section("WeakTable（handler 静态状态的弱键语义）");

        // 1) 基本读写：与 Dictionary 同形（现有 handler 代码才能一行不动地换过来）
        var table = new WeakTable<Key, string>();
        var key = new Key { Id = 1 };

        Program.Check("空表读回 default", table[key] is null);
        Program.Check("空表 ContainsKey = false", !table.ContainsKey(key));

        table[key] = "回调";
        Program.Check("写入后能读回", table[key] == "回调");
        Program.Check("写入后 ContainsKey = true", table.ContainsKey(key));
        Program.Check(
            "TryGetValue 与 Dictionary 同形",
            table.TryGetValue(key, out var value) && value == "回调");

        // 2) 覆盖与移除
        table[key] = "新回调";
        Program.Check("覆盖生效", table[key] == "新回调");
        Program.Check("Remove 返回 true", table.Remove(key));
        Program.Check("Remove 之后读回 default", table[key] is null);
        Program.Check("重复 Remove 返回 false", !table.Remove(key));

        // 3) null 值要能存（ConditionalWeakTable 本身不接受 null 值，靠装箱层支持）
        table[key] = null;
        Program.Check("null 值也算\"有这条\"", table.ContainsKey(key));
        Program.Check("null 值读回是 null", table[key] is null);

        // 4) 值类型 / 元组也要能存（handler 里好几处存的是元组）
        var tuples = new WeakTable<Key, (int? A, string? B)>();
        tuples[key] = (7, "x");
        Program.Check("元组值能读回", tuples[key] is (7, "x"));

        // 5) 核心：控件不可达 → 表不再钉住它（不需要 Unmount 来救）。
        //    换成 Dictionary 这条会失败：字典强持有键，GC 回收不掉。
        var reference = CollectAndDropKey();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Program.Check(
            "键不可达后能被 GC 回收（表没有强持有它）",
            !reference.TryGetTarget(out _));
    }

    /// <summary>
    /// 在子方法里挂一条、什么都不清就返回：方法返回后那个键只剩弱引用。
    /// </summary>
    /// <remarks>
    /// <c>NoInlining</c> 是必须的：被内联的话局部变量可能被 JIT 当作仍在使用，
    /// GC 回收不掉，测试会假失败。
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Key> CollectAndDropKey()
    {
        var table = new WeakTable<Key, string>();
        var key = new Key { Id = 2 };

        // 只在表里挂过一次，且<b>故意不调 Remove</b>——正是"Unmount 漏掉"的场景。
        table[key] = "挂在控件上的回调";

        return new WeakReference<Key>(key);
    }
}
