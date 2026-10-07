using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// WinUI 2 的 <c>Expander</c>：一个可折叠的分组容器。
/// </summary>
/// <remarks>
/// <b>它是 WinUI 2 那个原生 <c>Expander</c></b>，头部自有"展开/折叠"的语义：
/// 键盘可达（<c>Enter</c> / <c>Space</c> 切换）、自动化对等自带展开状态。
/// 自己拿按钮 + 显隐拼一个，UIA 树上就是"一个按钮"，屏幕阅读器念不出层级。
/// <para>
/// 注意 <see cref="ExpanderElement.IsExpanded"/> 在这一版里是<b>初值语义</b>：
/// 元素上没有展开回调，所以用户手动展开之后的值无从回传——此后由控件自己持有，
/// 框架不会在重渲染时把它拽回来（这是刻意的，否则每轮重渲染就合上一次）。
/// </para>
/// </remarks>
public sealed class ExpanderBasic : Component
{
    public override Element Render() =>
        VStack(8,
            Expander(
                header: "点一下展开",
                isExpanded: true,
                content: TextBlock("展开区放任意内容，不只是设置项。").Wrap()),
            Expander(
                header: "带图标",
                headerIcon: FontIcon("\uE713"),
                content: TextBlock("图标会与标题横排（WinUI 2 的 Expander 没有独立的 HeaderIcon 槽，这里拼进 Header 内容）。")
                    .Wrap()));
}
