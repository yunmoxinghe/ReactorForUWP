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
/// <para>
/// <b>存登记的表必须是弱键。</b>这张表是 <c>static readonly</c>（每个受控属性一份），
/// 键是<b>真实控件</b>，于是它的寿命天然比控件长：控件能不能回收，取决于有没有人
/// 在 <c>Unmount</c> 里记得调 <see cref="Forget"/>——而 <c>Unmount</c> 会不会被调到
/// 是 <c>Reconciler</c> 的覆盖问题，这个类<b>保证不了</b>。用 <c>Dictionary</c> 时，
/// 任何一条漏掉的路（绕开 <c>Reconciler.UnmountTree</c> 的路径、或将来新增 handler
/// 忘了写 <c>Unmount</c>）都会把整棵控件子树永久钉住，连同它的 <c>DataContext</c>、
/// 命令、宿主页面一起泄漏。换成 <see cref="WeakTable{TKey, TValue}"/> 之后判据回到
/// 控件自己：<b>控件不可达 → 条目自动消失</b>，<see cref="Forget"/> 从"不写就泄漏"
/// 降级成"提前释放的加速手段"。<c>WeakTable</c> 那份注释把这个坑讲过一遍，
/// 这里的治疗方案完全相同。
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

    private readonly WeakTable<object, (object? Value, long Tick)> _pending = new();

    /// <summary>
    /// 「静默窗」的深度：键是控件，值 &gt;= 1 表示窗正开着（可嵌套）。
    /// </summary>
    private readonly WeakTable<object, int> _silenced = new();

    /// <summary>框架即将写入 <paramref name="value"/> 时登记期望值。</summary>
    public void Expect(object control, object? value) =>
        _pending.Set(control, (value, Environment.TickCount64));

    /// <summary>
    /// 开一段<b>静默窗</b>：窗内这个控件的回执一律判成"框架自己写的"，不回调用户。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>它补的是 <see cref="Expect"/> 补不了的那一类。</b><c>Expect</c> 要求"我知道
    /// 写下去会回读出什么值"，才能拿那个值去匹配。但有一类写入<b>事先不知道会变成
    /// 什么值</b>：把 <c>Minimum</c> / <c>Maximum</c> 写成新区间时，控件会把受控值
    /// <b>夹</b>到新区间里（<c>NumberBox</c> 的 <c>CoerceValue</c> 就是干这个的），
    /// 夹出来的值只有控件自己知道，而且写两个边界可能各夹一次——一次
    /// <see cref="Expect"/> 装不下两发。
    /// </para>
    /// <para>
    /// 这类写入的共同特征是：<b>它发生在渲染路径内部</b>。那段时间内不可能夹杂真实
    /// 用户输入（输入要等这一轮返回后才轮到它），所以"窗内的事件都不是用户输入"
    /// 这个判断不靠预测值，靠时间窗——比猜值稳。
    /// </para>
    /// <para>
    /// 用法（一定要用 <c>using</c>，窗必须关）：
    /// <code>
    /// using (ValueEcho.Silence(control))
    /// {
    ///     PropWriter.Set(old.Min, next.Min, v =&gt; control.Minimum = v);
    ///     PropWriter.Set(old.Max, next.Max, v =&gt; control.Maximum = v);
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <b>已知边界</b>：若那一发事件延后到窗关<b>之后</b>才到（异步派发），窗就罩不住它。
    /// 这条边界与 <c>Reconciler.RebindTextChanged</c> 那条"延后到 <c>Loaded</c> 之后"
    /// 是同一种形状——按本类一贯的原则，宁可多回调一次，也不为了追它去吞真实输入。
    /// </para>
    /// </remarks>
    public SilentWindow Silence(object control)
    {
        _silenced.Set(control, _silenced[control] + 1);
        return new SilentWindow(this, control);
    }

    /// <summary><see cref="EchoGuard.Silence"/> 的窗把手：<c>Dispose</c> 即关窗（可嵌套）。</summary>
    public readonly struct SilentWindow : IDisposable
    {
        private readonly EchoGuard _guard;
        private readonly object _control;

        internal SilentWindow(EchoGuard guard, object control)
        {
            _guard = guard;
            _control = control;
        }

        public void Dispose() => _guard.EndSilence(_control);
    }

    private void EndSilence(object control)
    {
        var depth = _silenced[control] - 1;

        if (depth > 0)
        {
            _silenced.Set(control, depth);
        }
        else
        {
            _silenced.Remove(control);
        }
    }

    /// <summary>
    /// 控件事件中调用：回读值等于登记值 → 判定为回声，返回 true。
    /// </summary>
    /// <remarks>
    /// <b>非破坏性语义（2026-10 修正，勿改回去）</b>：只有<b>匹配成功</b>才消费登记。
    /// 旧实现无条件删除登记，而 TextBox 的粘贴 / IME / selection replacement 会连发
    /// 多个 TextChanged：第一个把登记吃掉，第二个就被当成用户输入 → 回调 → setState
    /// → 重渲染把用户刚粘进去的文本覆盖掉（即「粘贴覆盖」bug 的头号嫌疑）。
    /// <para>
    /// 状态机：<c>静默窗内 → 一律回声</c>；<c>不存在 → 不是回声</c>；
    /// <c>相等 → 消费并判定回声</c>；
    /// <c>不等 → 保留登记，等下一次事件或 TTL</c>；<c>超窗 → 丢弃</c>。
    /// </para>
    /// </remarks>
    public bool Consume(object control, object? value)
    {
        if (_silenced[control] > 0)
        {
            // 窗内：这一段是我们自己在写属性，其中的事件不可能来自用户。
            EchoStats.Silenced++;
            return true;
        }

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

    /// <summary>
    /// 受控写入<b>之后</b>调用：如果刚登记的期望到现在还没被 <see cref="Consume"/>
    /// 消费掉，说明<b>这一发写入压根没有产生回声</b>，撤销登记并返回 true。
    /// </summary>
    /// <remarks>
    /// <b>它补的是 <see cref="Expect"/> 看不见的那一半。</b>登记时我们只能假设
    /// "写下去就会有事件回读"，但至少有两种情形事件不会来：
    /// <list type="bullet">
    ///   <item>事件处理器那一刻是空的（<c>handler</c> 里
    ///         <c>current?.Invoke(...)</c>）——连 <c>Consume</c> 都不会被调用；</item>
    ///   <item>控件此刻不会抛事件（例如还没套模板、或正在整批换数据源）。</item>
    /// </list>
    /// 无论哪种，"登记了没人领"都是同一颗雷：等到某次<b>真实用户操作</b>的值恰好
    /// 等于这条陈旧登记，<c>Consume</c> 会匹配成功，把它当成框架自己的回声吞掉——
    /// 于是 state 不更新、界面看着"点了没反应"。
    /// <para>
    /// <b>代价是对的方向。</b>如果事件其实是异步的（晚于这次检查），撤销之后它
    /// 会走到 <c>NotExpected</c> → 多回调一次。而回调的值等于刚写进去的受控值，
    /// <c>setState</c> 同值不重渲染，因此多出来的是一次空转。
    /// 反过来，"漏撤销"吞掉的是真实用户操作。按本类一贯的原则——
    /// <b>宁可多回调一次，也不能吞掉真实用户操作</b>。
    /// </para>
    /// <para>
    /// 放在写入<b>之后</b>而不是之前，是为了不干扰
    /// "同步事件 → 匹配 → 消费"这条主路径：那一路径上来就已经把登记删掉了，
    /// 这里自然返回 false。
    /// </para>
    /// </remarks>
    /// <returns>是否撤销了一条未被消费的登记。</returns>
    public bool CancelIfUnconsumed(object control)
    {
        if (!_pending.TryGetValue(control, out _))
        {
            return false;
        }

        _pending.Remove(control);
        EchoStats.Sealed++;
        return true;
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

    /// <summary>写入后发现没有同步回声、被 <see cref="EchoGuard.CancelIfUnconsumed"/> 撤销的次数。</summary>
    public static long Sealed;

    /// <summary>
    /// 落在<b>静默窗</b>里、被判定为"框架自己写的"的次数（见 <see cref="EchoGuard.Silence"/>）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Matched"/> 的区别要能在诊断屏上看出来：<c>matched</c> 是"猜中了值"，
    /// <c>silenced</c> 是"根本没猜值、只凭时间窗挡下"——后者专门对应"写边界把受控值夹了"
    /// 那一类。涨它<b>不是</b>异常，是这次渲染确实改动了区间。
    /// </remarks>
    public static long Silenced;

    public static void Reset()
    {
        Matched = 0;
        Mismatch = 0;
        Expired = 0;
        NotExpected = 0;
        Sealed = 0;
        Silenced = 0;
    }

    public static string Snapshot() =>
        $"matched={Matched} mismatch={Mismatch} expired={Expired} notExpected={NotExpected} " +
        $"sealed={Sealed} silenced={Silenced}";
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
