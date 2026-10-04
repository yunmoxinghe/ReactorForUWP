// 纯状态机、平台无关，所以放 Core 层：组件节点（ComponentNode）与 UWP 宿主
// （ReactorHost）两条重渲染路径都要用它。
using System;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// 把一轮消息泵内的多次重渲染请求合并成一次（纯状态，不依赖 XAML，可脱离 UI 断言）。
/// </summary>
/// <remarks>
/// <para>
/// <b>它解决什么。</b>没有它的时候，UI 线程上连发 N 次 <c>setState</c> 就是
/// N 次"整棵树 Render + Patch"：一个事件处理里改三个状态，代价就翻三倍；
/// 列表里逐项 setState 更是直接 O(n) 棵树的量。
/// </para>
/// <para>
/// <b>为什么单独抽出来。</b>合并逻辑只有几行，但它是"性能正确"的一部分，
/// 很容易在后来的重构里被改回"收到就渲染"（看起来更直接、也没报错）。
/// 抽成纯类才能在测试里锁住"N 次请求只调度一次"这个性质。
/// </para>
/// <para>
/// <b>语义变化（要写在明面上）。</b>批处理之后，<c>setState</c> <b>不再同步生效</b>：
/// 它排到当前调用栈结束之后。这是 React / 官方 Reactor 的语义
/// （<c>setState</c> 之后立刻读 UI 状态读到的还是旧值）。
/// 依赖"改完马上能读到新 UI"的写法要改成在渲染后的回调里读。
/// 官方 <c>ReactorHost.RequestRender</c> 的注释就是这么写的：只有首帧同步，
/// 之后一律 dispatcher 批处理。
/// </para>
/// <para>
/// <b>顺带堵住渲染死循环。</b>见 <see cref="BeginRender"/> 与
/// <see cref="SelfTriggerCount"/>：在 <c>Render()</c> 或 effect 里同步 setState
/// 会让每一轮渲染都触发下一轮，界面看起来没反应、CPU 却跑满。
/// </para>
/// </remarks>
internal sealed class RenderBatcher
{
    /// <summary>
    /// 连续"渲染期间又请求渲染"的轮数上限，超过就判定为死循环并抛异常。
    /// </summary>
    /// <remarks>
    /// 对齐官方 Reactor 的 <c>MaxRerenderReentrancy = 50</c>。
    /// </remarks>
    public const int MaxSelfTriggerRenders = 50;

    private bool _queued;
    private bool _rendering;
    private int _selfTriggerCount;

    /// <summary>本轮是否已经排过一次渲染。</summary>
    public bool IsQueued => _queued;

    /// <summary>是否处于渲染中（<see cref="BeginRender"/> 与 <see cref="EndRender"/> 之间）。</summary>
    public bool IsRendering => _rendering;

    /// <summary>
    /// 连续自触发的轮数：每一轮渲染期间又排了下一轮就 +1，
    /// 中间只要有一轮没有自触发就清零。
    /// </summary>
    public int SelfTriggerCount => _selfTriggerCount;

    /// <summary>
    /// 请求一次渲染。<returns> true = 这次请求真的排上了队；false = 已合并进既有那次。</returns>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 连续 <see cref="MaxSelfTriggerRenders"/> 轮都在渲染期间自触发（渲染死循环）。
    /// </exception>
    public bool TrySchedule()
    {
        if (_queued)
        {
            return false;
        }

        _queued = true;

        // 只在渲染期间计数：正常的"事件处理里改状态"不在渲染中，永远不会累加；
        // 累加到上限只可能是 Render() 或 effect（含 cleanup）里同步 setState。
        if (_rendering && ++_selfTriggerCount >= MaxSelfTriggerRenders)
        {
            throw new InvalidOperationException(
                "Render loop detected: rerender re-entered more than " +
                $"{MaxSelfTriggerRenders} times. Likely cause: setState called " +
                "synchronously inside Render() or effect cleanup.");
        }

        return true;
    }

    /// <summary>
    /// 渲染开始：清掉本轮的排队标志（渲染期间到来的 setState 必须能排上<b>下</b>一轮，
    /// 而不是被本轮这个已消费的标志位挡掉），并进入渲染态。
    /// </summary>
    public void BeginRender()
    {
        _queued = false;
        _rendering = true;
    }

    /// <summary>
    /// 渲染结束：退出渲染态；本轮没有再自触发就把连续计数清零
    /// （有排队说明下一轮已经排上，计数要留着继续累加）。
    /// </summary>
    public void EndRender()
    {
        _rendering = false;

        if (!_queued)
        {
            _selfTriggerCount = 0;
        }
    }

    /// <summary>
    /// 只清排队标志，不碰渲染态与自触发计数。
    /// </summary>
    /// <remarks>
    /// 渲染路径请走 <see cref="BeginRender"/> / <see cref="EndRender"/>：
    /// 少了渲染态就识别不出"渲染期间触发的"请求，死循环保护会失效。
    /// 这里保留是为了不依赖渲染流程的场合（比如单元测试）。
    /// </remarks>
    public void Reset() => _queued = false;
}
