using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 按钮：<c>Button</c> 的最小形态——一个点击回调。
/// </summary>
/// <remarks>
/// 回调直接写在工厂方法里（<c>Button("…", () =&gt; …)</c>），不需要事件绑定那套仪式：
/// 组件重渲染时由框架把最新的回调接上去，闭包里读到的 <c>count</c> 就是本帧的那个值。
/// <para>
/// <b>本框架的 <c>UseState</c> 与官方 Reactor 一致，只给值重载</b>
/// （没有 <c>setCount(c =&gt; c + 1)</c> 那种函数式更新）。因此"同一帧内的第二个回调"
/// 拿到的是同一个旧值——这个示例是一次一次点的，看不出来；
/// 真需要按最新值累计时就把累计动作放到一个方法里再 <c>setCount(Compute(count))</c>。
/// </para>
/// </remarks>
public sealed class ButtonBasic : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return HStack(8,
            Button("点我", () => setCount(count + 1)),
            Button("归零", () => setCount(0)),
            TextBlock($"已点击 {count} 次").Body());
    }
}
