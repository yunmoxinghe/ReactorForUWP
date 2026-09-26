using System;
using System.Threading;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

/// <summary>
/// 核心闭环验收页：证明 Component / Hook / 独立 rerender / patch 四个闭环。
/// 只用已验证存活的 TextBlock/Button/Stack，不引入新控件。
/// </summary>
public sealed class CoreLoopDemo : Component
{
    private int _parentRenderCount;

    public override Element Render()
    {
        _parentRenderCount++;
        var (showChild, setShowChild) = UseState(true);
        var (parentClicks, setParentClicks) = UseState(0);

        return VStack(
            TextBlock($"Parent renders: {_parentRenderCount}") with { Key = "parent-renders" },
            TextBlock($"Parent clicks: {parentClicks}") with { Key = "parent-clicks" },
            Button("Parent +1", () => setParentClicks(parentClicks + 1)) with { Key = "btn-parent" },
            Button(showChild ? "Hide Child" : "Show Child", () => setShowChild(!showChild))
                with { Key = "btn-toggle" },
            showChild ? Component<ChildCounter>() : TextBlock("(child unmounted)") with { Key = "placeholder" }
        );
    }
}

/// <summary>
/// 子组件：独立 state + UseEffect Timer。
/// 验收点：
/// 1. 点击 Child +1 只更新子组件文本，Parent renders 不变（独立 rerender）；
/// 2. 卸载时 UseEffect cleanup 执行，Timer 停止（通过日志验证）；
/// 3. Timer 跨线程 setter 经 dispatcher marshal 不崩溃（threadSafe）。
/// </summary>
public sealed class ChildCounter : Component
{
    public override Element Render()
    {
        // UseReducer 提供函数式更新 setCount(c => c + 1)，避免 [] 依赖的闭包陷阱
        var (count, setCount) = UseReducer<int>(0, threadSafe: true);

        // 空依赖：mount 一次，卸载时 cleanup（不再每秒 cleanup/restart）
        UseEffect(() =>
        {
            ReactorApplication.Trace("[MOUNT] ChildCounter mounted, starting timer");
            var timer = new Timer(_ =>
            {
                // 跨线程 setter：函数式更新，读最新值
                setCount(c => c + 1);
            }, null, 1000, 1000);

            return () =>
            {
                ReactorApplication.Trace("[UNMOUNT] ChildCounter cleanup, stopping timer");
                timer.Dispose();
            };
        });

        return VStack(
            TextBlock($"Child count: {count}") with { Key = "child-count" },
            Button("Child +1", () => setCount(c => c + 1)) with { Key = "btn-child" }
        );
    }
}
