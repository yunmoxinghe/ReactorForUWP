using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>MenuBar</c>：横排的若干组，每组点开一个浮出菜单（WinUI 2 真控件）。
/// </summary>
/// <remarks>
/// 与 <c>CommandBar</c> 的区别是<b>形态</b>而不是能力：命令条是"图标 + 溢出菜单"，
/// 菜单栏是"文字分组"（文件 / 编辑 / 视图），常见于窗口顶部。
/// 两者用的菜单项<b>是同一套写法</b>——官方 <c>MenuBarItem.Items</c> 与
/// <c>MenuFlyout.Items</c> 收的都是 <c>MenuFlyoutItemBase</c>，所以这里直接复用
/// <see cref="MenuItem"/> / <see cref="MenuSeparator"/>，不用为了换容器再学一遍。
/// <para>
/// 菜单项的键盘提示串（<c>AcceleratorText</c>）仍然只是一行字：它对应
/// <c>KeyboardAcceleratorTextOverride</c>，不替你把键挂上去。
/// </para>
/// </remarks>
public sealed class MenuBarBasic : Component
{
    public override Element Render()
    {
        var (last, setLast) = UseState("（还没点过）");

        void Report(string what) => setLast(what);

        return VStack(12,
            TextBlock("菜单栏").Body(),

            MenuBar(
                Menu("文件",
                    MenuItem("新建", FontIcon("\uE710"), () => Report("文件 › 新建"), acceleratorText: "Ctrl+N"),
                    MenuItem("打开…", FontIcon("\uE8E5"), () => Report("文件 › 打开"), acceleratorText: "Ctrl+O"),
                    MenuSeparator(),
                    MenuItem("保存", FontIcon("\uE74E"), () => Report("文件 › 保存"), acceleratorText: "Ctrl+S"),
                    MenuItem("另存为…", FontIcon("\uE792"), () => Report("文件 › 另存为")),
                    MenuSeparator(),
                    MenuItem("退出", FontIcon("\uE7E8"), () => Report("文件 › 退出"))),

                Menu("编辑",
                    MenuItem("撤销", FontIcon("\uE7A7"), () => Report("编辑 › 撤销"), acceleratorText: "Ctrl+Z"),
                    MenuItem("重做", FontIcon("\uE7A6"), () => Report("编辑 › 重做"), acceleratorText: "Ctrl+Y"),
                    MenuSeparator(),
                    MenuItem("剪切", FontIcon("\uE8C6"), () => Report("编辑 › 剪切")),
                    MenuItem("复制", FontIcon("\uE8C8"), () => Report("编辑 › 复制")),
                    MenuItem("粘贴", FontIcon("\uE77F"), () => Report("编辑 › 粘贴")),
                    MenuSeparator(),
                    MenuItem("这一项是禁用的", isEnabled: false)),

                Menu("视图",
                    MenuItem("放大", FontIcon("\uE8A3"), () => Report("视图 › 放大")),
                    MenuItem("缩小", FontIcon("\uE8A3"), () => Report("视图 › 缩小")),
                    MenuSeparator(),
                    MenuItem("全屏", FontIcon("\uE740"), () => Report("视图 › 全屏")))),

            TextBlock($"最后一次动作：{last}").Caption().Subtle(),

            TextBlock("三组各自的菜单项都是 MenuItem / MenuSeparator —— 与「命令与菜单」那条里的"
                      + "菜单同一个写法，因为官方 MenuBarItem.Items 与 MenuFlyout.Items 收的是同一个类型。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
