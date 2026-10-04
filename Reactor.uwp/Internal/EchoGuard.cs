using System;
using System.Collections.Generic;
using System.Threading;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 受控属性的「回声抑制」：区分<b>用户操作</b>与<b>框架自己写入所触发的事件</b>。
/// </summary>
/// <remarks>
/// 官方对应物：<c>ControlDescriptor.Controlled&lt;TValue, TArgs&gt;</c> +
/// <c>ChangeEchoSuppressor</c> / <c>ExpectedEcho</c>。官方 <c>ExpanderElement</c> 的注释
/// 直接写着 <c>IsExpanded</c> 是 "counter-echo HandCodedControlled over Expanding +
/// Collapsed event"——即：框架写入前先登记"我期望回读到的值"，控件随后发出的事件
/// 若回读出该值，就判定为<b>自己写的回声</b>：既不回调用户，也不回写控件。
/// <para>
/// 没有它会出现这样的回环：用户展开卡片 → 事件 → setState → 重渲染 → 框架写
/// IsExpanded → 控件又发事件 → 回调 → setState … 表现为状态抖动 / 闪烁；
/// 或者用户手动改了但 state 没变，下一轮重渲染把用户的值拽回去。
/// </para>
/// <para>
/// 用法（每个受控属性一个实例，handler 里静态持有）：
/// <code>
/// // 写入前：
/// if (oldValue != newValue) { Echo.Expect(control, newValue); control.Prop = newValue; }
/// // 事件里：
/// if (Echo.Consume(control, control.Prop)) return;   // 是回声，不回调
/// callback(control.Prop);
/// </code>
/// </para>
/// </remarks>
internal sealed class EchoGuard
{
    /// <summary>
    /// 期望回声的有效期。
    /// </summary>
    /// <remarks>
    /// 正常情况下"写值 → 控件发事件"是同步的，毫秒级完成。留一秒窗口是为了兜住
    /// 少数异步派发的事件；超过窗口还没等到的登记值直接作废，避免一条陈旧的
    /// 期望把之后一次<b>真实的用户操作</b>误判成回声（宁可多回调一次，也不能丢事件）。
    /// </remarks>
    private const long WindowMs = 1000;

    private readonly Dictionary<object, (object? Value, long Tick)> _pending = new();

    /// <summary>框架即将写入 <paramref name="value"/> 时登记期望值。</summary>
    public void Expect(object control, object? value) =>
        _pending[control] = (value, Environment.TickCount64);

    /// <summary>
    /// 控件事件中调用：回读值等于登记值 → 判定为回声，返回 true。
    /// </summary>
    /// <remarks>
    /// <b>非破坏性语义（2026-10 修正，勿改回去）</b>：只有<b>匹配成功</b>才消费登记。
    /// 旧实现无条件删除登记，而 TextBox 的粘贴 / IME / selection replacement 会连发
    /// 多个 TextChanged：第一个把登记吃掉，第二个就被当成用户输入 → 回调 → setState
    /// → 重渲染把用户刚粘进去的文本覆盖掉（即「粘贴覆盖」bug 的头号嫌疑）。
    /// <para>
    /// 状态机：<c>不存在 → 不是回声</c>；<c>相等 → 消费并判定回声</c>；
    /// <c>不等 → 保留登记，等下一次事件或 TTL</c>；<c>超窗 → 丢弃</c>。
    /// </para>
    /// </remarks>
    public bool Consume(object control, object? value)
    {
        if (!_pending.TryGetValue(control, out var pending))
        {
            EchoStats.NotExpected++;
            return false;
        }

        if (Environment.TickCount64 - pending.Tick > WindowMs)
        {
            // 超窗：期望已经陈旧，作废。宁可多回调一次也不能吞掉真实用户操作。
            _pending.Remove(control);
            EchoStats.Expired++;
            return false;
        }

        if (Equals(pending.Value, value))
        {
            _pending.Remove(control);
            EchoStats.Matched++;
            return true;
        }

        // 不匹配：保留登记。这可能是"写值 → 中间态事件 → 最终态事件"的中间那一发。
        EchoStats.Mismatch++;
        return false;
    }

    /// <summary>丢弃某个控件上未消费的登记（例如控件被卸载）。</summary>
    public void Forget(object control) => _pending.Remove(control);
}

/// <summary>
/// 回声抑制的全局计数（诊断用，压测探针会读它）。
/// </summary>
internal static class EchoStats
{
    /// <summary>匹配成功、判定为回声并吞掉的次数。</summary>
    public static long Matched;

    /// <summary>有登记但值不等的次数（正常：用户真的改了）。</summary>
    public static long Mismatch;

    /// <summary>登记超窗作废的次数。</summary>
    public static long Expired;

    /// <summary>没有登记、直接按用户输入处理的次数。</summary>
    public static long NotExpected;

    public static void Reset()
    {
        Matched = 0;
        Mismatch = 0;
        Expired = 0;
        NotExpected = 0;
    }

    public static string Snapshot() =>
        $"matched={Matched} mismatch={Mismatch} expired={Expired} notExpected={NotExpected}";
}

/// <summary>
/// 渲染代际：只用于丢弃<b>过期 render / 异步回调</b>，<b>不参与回声抑制</b>。
/// </summary>
/// <remarks>
/// 职责必须和 <see cref="EchoGuard"/> 分开：回声抑制回答"这个事件是不是我自己刚写进去的"，
/// 代际回答"这个异步结果是不是已经属于上一轮 render"。混成一个 token 会变成难以维护的大杂烩。
/// <para>
/// 用法：<c>var token = gen.Next(); StartAsync(...);</c> 回来时 <c>if (gen.IsStale(token)) return;</c>
/// </para>
/// </remarks>
internal sealed class RenderGeneration
{
    private long _current;

    /// <summary>当前代际。</summary>
    public long Current => _current;

    /// <summary>开启新一轮（重渲染时调用），返回本轮 token。</summary>
    public long Next() => Interlocked.Increment(ref _current);

    /// <summary>该 token 是否属于过期的一轮。</summary>
    public bool IsStale(long token) => token != Interlocked.Read(ref _current);
}
