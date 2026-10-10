using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 拆分按钮：<c>SplitButton</c>——左半执行、右半展开。
/// </summary>
/// <remarks>
/// 官方那页的两件事：<b>默认动作 + 变体菜单</b>、<b>禁用</b>。
/// <list type="bullet">
///   <item>左半边是 <c>Click</c>（直接执行），右半边是展开菜单：
///         适合"有一个默认动作、另外几个是变体"（保存 / 另存为 / 导出）。</item>
///   <item>与 <c>DropDownButton</c> 的差别就在这半边上：后者<b>没有</b>点击回调，
///         整颗按钮按下去都只是展开。</item>
///   <item>要"按下去就保持"的那一种是 <c>ToggleSplitButton</c>（另有一份示例），
///         它多出来的是 <c>IsChecked</c> 状态而不是这里的一次性动作。</item>
///   <item><b>本库不给 <c>isEnabled</c></b>（与 <c>DropDownButton</c> 同一条取舍）：
///         官方那页有"禁用的拆分按钮"，而这里没有那个参数——
///         不是漏了，而是声明式一侧没有"把这一组命令整体置灰"之外的落点。</item>
/// </list>
/// </remarks>
public sealed class SplitButtonBasic : Component
{
    public override Element Render()
    {
        var (last, setLast) = UseState("（还没点过）");

        return VStack(16,
            HStack(12,
                SplitButton("保存", () => setLast("保存（默认动作）"), MenuFlyout(
                    MenuItem("另存为…", FontIcon("\uE792"), () => setLast("另存为")),
                    MenuItem("导出为 PDF", FontIcon("\uE7B5"), () => setLast("导出为 PDF")),
                    MenuSeparator(),
                    MenuItem("发布到云端", FontIcon("\uE753"), () => setLast("发布到云端"))))),

            TextBlock($"最后一次动作：{last}").Caption().Subtle(),

            TextBlock("左半边 Click、右半边展开菜单：右半边那半个按钮的展开动作"
                      + "由控件自己处理，本库不暴露 IsOpen —— 声明式树上没有"
                      + "「在某一帧把菜单拉开」这个语义的位置。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
