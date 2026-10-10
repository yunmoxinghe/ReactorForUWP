using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 下拉按钮：<c>DropDownButton</c>。
/// </summary>
/// <remarks>
/// 官方那页的两件事：<b>挂一份菜单</b>、<b>按钮自己带图标</b>。
/// <list type="bullet">
///   <item><b>它没有点击回调。</b>官方 <c>DropDownButton</c> 就不提供
///         <c>Click</c>：按下去的全部意义是展开菜单，动作由菜单里的项承担。
///         本库因此也不给 <c>onClick</c> 参数——给一个"点了什么都不发生"的
///         槽位比不给更糟。</item>
///   <item>要"有一个默认动作 + 几个变体"就换 <c>SplitButton</c>（见下一条目）：
///         那是左半执行、右半展开，与这里不是一个控件。</item>
///   <item>菜单是 <c>MenuFlyout</c>：官方那页的菜单项同样可以带图标、分隔线、
///         可勾选项与子菜单（见「MenuFlyout」条目）。</item>
///   <item><b>按钮上的图标要用"内容"那一档</b>：<c>DropDownButton</c> 是
///         <c>ContentControl</c>，图标与文字一起是它的内容（图标 + 文字横排），
///         不是另有一个 <c>Icon</c> 属性——这一点和 <c>AppBarButton</c> 不一样。</item>
///   <item><b>本库不给它 <c>isEnabled</c></b>：官方那页有"禁用的下拉按钮"，
///         而本库的这两个工厂方法没有这个参数——不是忘了，
///         而是"禁用一个只能展开菜单的按钮"在声明式一侧找不到落点
///         （要禁用通常是把整组命令换掉）。真需要时用 <c>Native()</c>。</item>
/// </list>
/// </remarks>
public sealed class DropDownButtonBasic : Component
{
    public override Element Render()
    {
        var (chosen, setChosen) = UseState("（还没选）");

        return VStack(16,
            HStack(12,
                DropDownButton("颜色…", MenuFlyout(
                    MenuItem("红色", FontIcon("\uE91F"), () => setChosen("红色")),
                    MenuItem("绿色", FontIcon("\uE91F"), () => setChosen("绿色")),
                    MenuItem("蓝色", FontIcon("\uE91F"), () => setChosen("蓝色")))),

                // 按钮自己带一个图标：图标与文字一起是它的内容（ContentControl），
                // 不是另有一个 Icon 属性。
                DropDownButton(
                    HStack(6, FontIcon("\uE8CB"), TextBlock("排序…")),
                    MenuFlyout(
                        MenuItem("按名称", onClick: () => setChosen("按名称")),
                        MenuItem("按日期", onClick: () => setChosen("按日期")),
                        MenuSeparator(),
                        MenuItem("按大小", onClick: () => setChosen("按大小")))),

                // 官方第二例是「只有图标、没有文字」那一档：内容直接给一个 FontIcon，
                // 整个按钮缩成一个方块。没有文字就没有无障碍名字，必须自己补。
                DropDownButton(
                        FontIcon("\uE715"),
                        MenuFlyout(
                            MenuItem("标记为已读", onClick: () => setChosen("标记为已读")),
                            MenuItem("移动到…", onClick: () => setChosen("移动到…"))))
                    .AutomationName("邮件操作")),

            TextBlock($"最后一次选择：{chosen}").Caption().Subtle(),

            TextBlock("DropDownButton 官方连 Click 事件都不提供：按下去就是展开，"
                      + "动作由菜单里的项承担 —— 需要「默认动作 + 变体」时用 SplitButton。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
