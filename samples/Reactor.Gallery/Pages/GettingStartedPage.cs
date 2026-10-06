// Microsoft.UI.Reactor：元素类型与修饰符扩展（.FontSize / .Caption 等）都在这里；
// Microsoft.UI.Reactor.Factories：VStack / TextBlock / Button 等工厂方法。
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

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
                // 别把引用方式说反：Gallery 走的是 ProjectReference（改完框架立刻能看效果），
                // 只有 Reactor.Template 才按 NuGet 包引用。写成"只装 NuGet 包"的话，
                // 后来那个人会以为这份示例跑的就是发布出去的位——一旦两者行为不一致
                // （正是"同样代码在 Gallery 里对、在 Template 里坏"那种阴阳脸），
                // 排查方向会直接被引到框架以外去。
                TextBlock("Gallery 直接 ProjectReference 框架源码；想看只装 NuGet 包的写法，看 Reactor.Template。")
                    .Caption(),

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
