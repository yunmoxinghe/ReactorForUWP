// Microsoft.UI.Reactor：元素类型与修饰符扩展（.FontSize / .Caption 等）都在这里；
// Microsoft.UI.Reactor.Factories：VStack / TextBlock / Button 等工厂方法。
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Samples.Pages;

/// <summary>
/// 最小可运行示例：状态、事件、条件渲染。
/// </summary>
/// <remarks>
/// <c>Render()</c> 只描述"这一刻界面长什么样"；点按钮后 <c>setCount</c> 触发重渲染，
/// 框架 diff 出最小改动打到真实控件上——不需要手写任何控件更新代码。
/// </remarks>
public sealed class GettingStartedPage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (showBar, setShowBar) = UseState(true);

        return ScrollViewer(
            VStack(12,
                TextBlock("快速开始").FontSize(20),
                TextBlock("这个示例项目不引用框架源码，只装 NuGet 包 Reactor.Uwp。").Caption(),

                // When：条件成立才渲染，否则是 Empty（连占位控件都不建）。
                When(showBar, () => InfoBar($"计数：{count}")),

                HStack(8,
                    Button("-", () => setCount(count - 1)),
                    Button("+", () => setCount(count + 1)),
                    Button("归零", () => setCount(0))),

                // If：带 else 分支的条件渲染。
                If(count == 0,
                    () => TextBlock("还没点过按钮").Caption(),
                    () => TextBlock($"已经点了 {count} 次")),

                Button(showBar ? "隐藏提示条" : "显示提示条", () => setShowBar(!showBar))
            ).Padding(16));
    }
}
