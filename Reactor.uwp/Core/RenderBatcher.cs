// 纯状态机、平台无关，所以放 Core 层：组件节点（ComponentNode）与 UWP 宿主
// （ReactorHost）两条重渲染路径都要用它。
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
/// </para>
/// </remarks>
internal sealed class RenderBatcher
{
    private bool _queued;

    /// <summary>本轮是否已经排过一次渲染。</summary>
    public bool IsQueued => _queued;

    /// <summary>
    /// 请求一次渲染。<returns> true = 这次请求真的排上了队；false = 已合并进既有那次。</returns>
    /// </summary>
    public bool TrySchedule()
    {
        if (_queued)
        {
            return false;
        }

        _queued = true;
        return true;
    }

    /// <summary>渲染跑完后复位，下一轮重新接受请求。</summary>
    public void Reset() => _queued = false;
}
