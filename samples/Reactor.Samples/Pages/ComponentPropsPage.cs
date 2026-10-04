using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Samples.Pages;

/// <summary>子组件的 props：record 即可，框架按结构相等判断要不要重渲染。</summary>
public sealed record GreetingProps(string Name, int Times);

/// <summary>
/// 带 props 的组件：继承 <c>Component&lt;TProps&gt;</c>，通过 <c>Props</c> 读值。
/// </summary>
public sealed class GreetingCard : Component<GreetingProps>
{
    public override Element Render() =>
        new BorderElement(
            VStack(4,
                TextBlock($"你好，{Props.Name}！").BodyStrong(),
                TextBlock($"这是第 {Props.Times} 次问候").Caption()))
        {
            Padding = Thick(12),
        };
}

/// <summary>
/// 演示父子传值：父组件持有状态，子组件只负责显示。
/// </summary>
/// <remarks>
/// 嵌入方式是 <c>Component&lt;T, TProps&gt;(props)</c>；props 变了才重渲染子组件
/// （默认 <c>Equals</c> 比较，需要别的语义就重写 <c>ShouldUpdate</c>）。
/// </remarks>
public sealed class ComponentPropsPage : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("世界");
        var (times, setTimes) = UseState(1);

        return ScrollViewer(
            VStack(12,
                TextBlock("组件 props").FontSize(20),
                TextBox(Optional<string>.Of(name), setName, header: "问候对象"),
                HStack(8,
                    Button("再问候一次", () => setTimes(times + 1)),
                    Button("重置次数", () => setTimes(1))),

                Component<GreetingCard, GreetingProps>(new GreetingProps(name, times)),
                Component<GreetingCard, GreetingProps>(new GreetingProps(name + "（第二份实例）", times)),

                TextBlock("props 是 record，值没变时子组件不会重渲染——可以在 GreetingCard.Render 里打个断点验证。")
                    .Caption()
                    .Wrap()
            ).Padding(16));
    }
}
