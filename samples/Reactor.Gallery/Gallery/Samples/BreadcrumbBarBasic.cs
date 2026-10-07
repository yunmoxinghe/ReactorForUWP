using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 面包屑：<c>BreadcrumbBar</c>，显示"我在哪"并允许点任意一级往回走。
/// </summary>
/// <remarks>
/// 分隔符形状、条目的可点 / 悬停 / 按下态、跟随系统主题的外观、键盘 Tab
/// 与方向键导航，全部来自官方模板。
/// <para>
/// 这里只喂字符串路径 + 接点击回调下标。<b>喂进去的必须是数据，不能是 UIElement</b>
/// （塞 UIElement 会在首次布局时撞上"一个元素两个父"，
/// <c>0x800F1000</c>）——这是本仓库踩过一次的坑，注释留在
/// <c>BreadcrumbBarHandler</c> 里。
/// </para>
/// </remarks>
public sealed class BreadcrumbBarBasic : Component
{
    private static readonly string[] Path = { "起点", "输入与选择", "TextBox" };

    public override Element Render()
    {
        var (clicked, setClicked) = UseState("还没点过");

        return VStack(10,
            BreadcrumbBar(Path, onItemClicked: index => setClicked(Path[index])),
            TextBlock($"最后点的是：{clicked}").Caption().Subtle());
    }
}
