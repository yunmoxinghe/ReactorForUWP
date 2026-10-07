using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>ToggleSplitButton</c>：会"按下就保持"的拆分按钮（WinUI 2 真控件）。
/// </summary>
/// <remarks>
/// 它把两件事合在一个控件上：左半边是 <c>SplitButton</c>（点一下执行 + 右半边展开
/// 菜单），同时整个按钮带一个 <c>IsChecked</c>——按左半边会把它<b>锁在按下状态</b>。
/// 典型用法是编辑器里的"项目符号"：按下即生效，再按一次取消。
/// <para>
/// <b>两个回调各管一半。</b><c>onIsCheckedChanged</c> 是"按下态变了"
/// （官方 <c>IsCheckedChanged</c>），<c>onClick</c> 是"左半边被点了"
/// （继承自 <c>SplitButton</c> 的 <c>Click</c>）。按一次两个都会来，
/// 别把它们当成同一件事。
/// </para>
/// <para>
/// <b>官方 <c>ToggleSplitButton.IsChecked</c> 不可空</b>（没有三态），
/// 所以元素上那个 <c>bool?</c> 里的 <c>null</c> 落到 <c>false</c>，
/// 而不是"保持原样"。
/// </para>
/// </remarks>
public sealed class ToggleSplitButtonBasic : Component
{
    public override Element Render()
    {
        var (bullets, setBullets) = UseState(false);
        var (style, setStyle) = UseState("圆点");
        var (clicks, setClicks) = UseState(0);

        return VStack(14,
            TextBlock("受控 + 菜单").Body(),
            ToggleSplitButton(
                "项目符号",
                isChecked: Optional<bool?>.Of(bullets),
                onIsCheckedChanged: setBullets,
                onClick: () => setClicks(clicks + 1),
                flyout: MenuFlyout(
                    MenuItem("圆点", onClick: () => setStyle("圆点")),
                    MenuItem("方块", onClick: () => setStyle("方块")),
                    MenuItem("数字", onClick: () => setStyle("数字")),
                    MenuSeparator(),
                    MenuItem("取消符号", onClick: () => setBullets(false)))),
            TextBlock($"按下态：{(bullets ? "开" : "关")}；菜单里选的是：{style}；"
                      + $"左半边被点了 {clicks} 次").Caption().Subtle(),

            TextBlock("菜单里也能改按下态").Body(),
            TextBlock("最后那一项「取消符号」改的是 isChecked，不是 Click——"
                      + "官方把这两个通道留成了两件事，用哪个取决于你想让用户感到什么。")
                .Caption().Subtle());
    }
}
