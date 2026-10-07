using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>CommandBarFlyout</c>：挂在别人身上的一条命令条（WinUI 2 真控件）。
/// </summary>
/// <remarks>
/// <para>
/// <b>它不在可视树里。</b><c>CommandBarFlyout</c> 继承 <c>FlyoutBase</c> →
/// <c>DependencyObject</c>，与 <c>MenuFlyout</c> 同一条规矩：<b>元素是描述</b>，
/// 由宿主就地物化，走不了协调器。所以它没有"自己站位"这回事——
/// 必须挂在按钮的 <c>Flyout</c> 上（点开）或者元素的 <c>ContextMenu</c> 上
/// （右键 / 长按弹出）。下面两个入口各来一份。
/// </para>
/// <para>
/// <b>两组命令的形状与 <c>CommandBar</c> 一样。</b>主要命令横排可见，
/// 次要命令收进那个「…」溢出菜单——装的也是 <c>AppBarButton</c> /
/// <c>AppBarSeparator</c> / <c>AppBarToggleButton</c>，不用再学一遍写法。
/// </para>
/// <para>
/// <b><c>alwaysExpanded</c> 是官方属性，不是"把溢出菜单摘掉"。</b>打开之后
/// 次要命令平铺在主要命令旁边；要"压根没有次要命令"就把
/// <c>secondary</c> 留空。
/// </para>
/// </remarks>
public sealed class CommandBarFlyoutBasic : Component
{
    public override Element Render()
    {
        var (last, setLast) = UseState("（还没点过）");
        var (wide, setWide) = UseState(false);

        return VStack(12,
            TextBlock("命令条浮层").Body(),

            HStack(8,
                DropDownButton(
                        HStack(6, SymbolIcon(Symbol.Edit), TextBlock("点开命令条").Body()),
                        flyout: CommandBarFlyout(
                            primary: new Element?[]
                            {
                                AppBarButton("加粗", "\uE8DD", () => setLast("加粗")),
                                AppBarButton("斜体", "\uE8DB", () => setLast("斜体")),
                                AppBarButton("下划线", "\uE8DC", () => setLast("下划线")),
                                AppBarSeparator(),
                                AppBarToggleButton("项目符号", "\uE8D3", false,
                                    value => setLast($"项目符号 = {(value ? "开" : "关")}")),
                            },
                            secondary: new Element?[]
                            {
                                AppBarButton("全选", "\uE8B3", () => setLast("全选")),
                                AppBarButton("清除格式", "\uE75C", () => setLast("清除格式")),
                            },
                            alwaysExpanded: wide)),

                ToggleButton("次要命令平铺", isChecked: wide, onIsCheckedChanged: setWide)),

            Border(
                    VStack(6,
                        TextBlock("在这块区域上右键（触屏是长按），弹出的是命令条浮层 —— "
                                  + "与 XAML 里 UIElement.ContextFlyout 挂 CommandBarFlyout 同形。")
                            .Caption()
                            .Wrap(),
                        TextBlock($"最后一次动作：{last}").Body()))
                .Padding(16)
                .Background(ThemeResource.Brush("LayerFillColorDefaultBrush"))
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"))
                .ContextMenu(CommandBarFlyout(
                    primary: new Element?[]
                    {
                        AppBarButton("复制", SymbolIcon(Symbol.Copy), () => setLast("复制")),
                        AppBarButton("剪切", SymbolIcon(Symbol.Cut), () => setLast("剪切")),
                        AppBarButton("粘贴", SymbolIcon(Symbol.Paste), () => setLast("粘贴")),
                    },
                    secondary: new Element?[]
                    {
                        AppBarButton("选择性粘贴", "\uE77F", () => setLast("选择性粘贴")),
                        AppBarSeparator(),
                        AppBarButton("查找", SymbolIcon(Symbol.Find), () => setLast("查找")),
                    })),

            TextBlock("「点开命令条」用的是 DropDownButton 的 Flyout 槽位；"
                      + "上面那块区域用的是 ContextMenu 修饰器（右键）。"
                      + "两者装的是同一种元素，物化都走 FlyoutSlot。")
                .Caption()
                .Wrap());
    }
}
