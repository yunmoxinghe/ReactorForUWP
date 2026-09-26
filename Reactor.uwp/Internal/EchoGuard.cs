using System;
using System.Collections.Generic;

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
    /// 控件事件中调用：回读值等于登记值 → 判定为回声（并消费掉登记），返回 true。
    /// </summary>
    public bool Consume(object control, object? value)
    {
        if (!_pending.TryGetValue(control, out var pending))
        {
            return false;
        }

        _pending.Remove(control);
        return Environment.TickCount64 - pending.Tick <= WindowMs && Equals(pending.Value, value);
    }

    /// <summary>丢弃某个控件上未消费的登记（例如控件被卸载）。</summary>
    public void Forget(object control) => _pending.Remove(control);
}
