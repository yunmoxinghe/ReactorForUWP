using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>CommandBar</c>：一行命令 + 一片内容区（UWP 原生控件）。
/// </summary>
/// <remarks>
/// <b>命令项不是独立控件，是挂在命令条上的子部件。</b>
/// <c>AppBarButton</c> 虽然继承 <c>ButtonBase</c>，但它一旦进了
/// <c>PrimaryCommands</c> 就不能再进任何 <c>Panel</c>（一个 <c>UIElement</c>
/// 只能有一个父）。所以命令项与菜单项同形：<b>就地物化</b>，不进协调器。
/// 由此带来的取舍与菜单一样——命令组是整体重建而不是逐项 patch（命令项只有
/// 文字 / 图标 / 回调三样数据，没有需要跨帧保留的状态）。
/// <para>
/// <b>主命令区与次命令区的分界是语义，不是排版。</b>次命令区（<c>SecondaryCommands</c>）
/// 里的项会被收进 <c>...</c> 那个溢出菜单；窗口变窄时主命令区里放不下的项还会被
/// 官方的溢出算法自动挪过去（<c>IsDynamicOverflowEnabled</c>）——挪哪几项由控件决定，
/// 不由这里决定。这正是自己用 <c>StackPanel</c> + 一排按钮拼不出来的部分。
/// </para>
/// </remarks>
public sealed class CommandBarBasic : Component
{
    public override Element Render()
    {
        var (last, setLast) = UseState("（还没点过）");

        void Report(string what) => setLast(what);

        return VStack(12,
            TextBlock("命令条").Body(),

            CommandBar(
                    content: Border(
                            TextBlock($"最后一次动作：{last}").Body())
                        .Padding(16)
                        .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),
                    primary: new Element?[]
                    {
                        AppBarButton("新建", "\uE710", () => Report("新建")),
                        AppBarButton("编辑", "\uE70F", () => Report("编辑")),
                        AppBarButton("删除", "\uE74D", () => Report("删除")),
                        AppBarSeparator(),
                        AppBarButton("共享", "\uE72D", () => Report("共享")),
                    },
                    secondary: new Element?[]
                    {
                        AppBarButton("设置", "\uE713", () => Report("设置")),
                        AppBarSeparator(),
                        AppBarButton("关于", "\uE946", () => Report("关于")),
                    },
                    overflowButtonVisibility: CommandBarOverflowButtonVisibility.Visible),

            TextBlock("把窗口拖窄，主命令区放不下的项会被官方的溢出算法挪进右边的「…」菜单——" +
                      "挪哪几项由控件算，不由这里写死。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("溢出按钮常驻（OverflowButtonVisibility = Visible）").Body(),
            CommandBar(
                content: TextBlock("这一条只有两个命令，溢出口里多半是空的。").Caption().Subtle(),
                primary: new Element?[]
                {
                    AppBarButton("新建", "\uE710", () => Report("新建（常驻）")),
                    AppBarButton("编辑", "\uE70F", () => Report("编辑（常驻）")),
                },
                overflowButtonVisibility: CommandBarOverflowButtonVisibility.Visible),
            TextBlock("默认是 Auto——没东西可溢出时它自己藏起来。改成 Visible 只是「一直显示」，"
                      + "不会凭空造出溢出项：里面空就是空。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
