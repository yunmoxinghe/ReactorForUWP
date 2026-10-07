using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>HyperlinkButton</c>：看起来是链接、行为是按钮。
/// </summary>
/// <remarks>
/// <b>为什么不是一个 <c>Button</c> 改样式。</b>它是官方的
/// <c>HyperlinkButton</c>（<c>ButtonBase</c> 的子类）：自带下划线与指针手型，
/// 在 UIA 树里报的是 <c>Hyperlink</c> 控件类型而不是 <c>Button</c>——
/// 屏幕阅读器念出来的东西不一样，这是选它的真正理由。
/// <para>
/// <b>内容是 <c>TextBlock</c> 时记得给 <c>AutomationName</c>。</b>
/// UIA 的名字回退只认"内容直接就是字符串"那一种；内容是一棵元素树时它取不到，
/// 于是这个链接对读屏与自动化脚本来说是<b>无名</b>的（本仓库的
/// <c>samples/uia-check.ps1</c> 会当场把这种漏网的抓出来）。
/// </para>
/// <para>
/// 这里点击只改一行文字。真要打开浏览器用的是
/// <c>Windows.System.Launcher.LaunchUriAsync</c>——那属于命令式动作，
/// 放在声明式样例里会把重点带偏。
/// </para>
/// </remarks>
public sealed class HyperlinkBasic : Component
{
    public override Element Render()
    {
        var (visited, setVisited) = UseState(false);

        return VStack(10,
            HyperlinkButton(
                    HStack(6,
                        FontIcon("\uE8A7", fontSize: 14).VAlign(VerticalAlignment.Center),
                        TextBlock("点我（内容是一棵元素树，不是纯字符串）")
                            .VAlign(VerticalAlignment.Center)),
                    () => setVisited(true))
                .AutomationName("示例链接，点一下标记已访问"),

            TextBlock(visited ? "已点过一次。" : "还没点过。").Caption().Subtle(),

            TextBlock("纯文字的形态：").Caption().Subtle(),
            HyperlinkButton("回到顶部看标题", () => setVisited(true))
                .AutomationName("回到顶部看标题"));
    }
}
