using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Template;

/// <summary>
/// 起始页：一个计数器。
/// </summary>
/// <remarks>
/// <c>Render()</c> 只描述"此刻界面长什么样"。点按钮后 <c>setCount</c> 触发重渲染，
/// 框架 diff 出最小改动打到真实控件上——没有双向绑定，也没有控件更新代码。
/// <para>
/// 从这里往下写就行：状态用 <c>UseState</c>，派生数据直接算，
/// 条件分支用 <c>When</c> / <c>If</c>，列表用 <c>ForEach</c>（上千项换
/// <c>VirtualizingList</c>）。更多写法看同仓库的 <c>Reactor.Gallery</c>。
/// </para>
/// </remarks>
public sealed class MainPage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return ScrollViewer(
            VStack(12,
                TextBlock("Reactor.Uwp 模板").FontSize(20),
                TextBlock("装了 Reactor.Uwp 包，没引用框架源码。").Caption(),
                TextBlock($"Count: {count}"),
                HStack(8,
                    Button("-", () => setCount(count - 1)),
                    Button("+", () => setCount(count + 1)))
            ).Padding(16));
    }
}
