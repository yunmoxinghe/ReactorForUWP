using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>TextCommandBarFlyout</c>：挂在文本控件上的那条命令条（WinUI 2 真控件）。
/// </summary>
/// <remarks>
/// <para>
/// <b>它是 <c>CommandBarFlyout</c> 的子类，不是另一种东西。</b>两组命令、
/// <c>alwaysExpanded</c> 与它完全同形；多出来的只有一件事——<b>它认得文本控件</b>：
/// 挂上去之后，剪切 / 复制 / 粘贴 / 全选 / 撤销那几条命令由控件自己填，
/// 而且会跟着"当前有没有选中文字"改可用状态。手填一份就丢了这份联动，
/// 所以下面的第一个例子<b>一条自定义命令都不给</b>。
/// </para>
/// <para>
/// <b>槽位叫 <c>SelectionFlyout</c>，不是 <c>ContextFlyout</c>。</b>官方给能选文本的
/// 控件单独留了这个属性（"选中文字之后弹出"），与"右键弹出"是两个槽位、
/// 两个时机。本库因此多了一个 <c>.SelectionFlyout(...)</c> 修饰器，与
/// <c>.ContextMenu(...)</c> 并列。给没有这个属性的控件写会留一条痕并被忽略——
/// 静默变成"怎么点都不弹"比留痕难查得多。
/// </para>
/// <para>
/// 第二个例子往里加自定义命令（加粗 / 斜体）：官方那几条剪贴板命令
/// <b>不会被挤掉</b>，两组并存。
/// </para>
/// </remarks>
public sealed class TextCommandBarFlyoutBasic : Component
{
    public override Element Render()
    {
        var (last, setLast) = UseState("（还没点过）");
        var (bold, setBold) = UseState(false);
        var (italic, setItalic) = UseState(false);
        var (text, setText) = UseState("选中这几个字试试");

        void Report(string what) => setLast(what);

        return VStack(12,
            TextBlock("全靠官方补命令（不填自定义命令）").Body(),

            // 一条自定义命令都不给：剪贴板那几条由控件自己按选区状态填。
            TextBox(text, onChanged: setText, header: "TextBox：选中文字后弹出")
                .SelectionFlyout(TextCommandBarFlyout()),

            TextBlock("自定义命令 + 官方命令并存").Body(),

            RichEditBox(
                    initialText: "在这段文字里选一段，命令条上会同时出现「加粗 / 斜体」"
                                 + "和官方那几条剪贴板命令。",
                    header: "RichEditBox：自定义命令在前",
                    onTextChanged: _ => Report("编辑了文本"))
                .SelectionFlyout(TextCommandBarFlyout(
                    primary: new Element?[]
                    {
                        AppBarToggleButton("加粗", "\uE8DD", bold, value =>
                        {
                            setBold(value);
                            Report($"加粗 → {value}");
                        }),
                        AppBarToggleButton("斜体", "\uE8DB", italic, value =>
                        {
                            setItalic(value);
                            Report($"斜体 → {value}");
                        }),
                    },
                    secondary: new Element?[]
                    {
                        AppBarButton("统计字数", "\uE8A5", () => Report("统计字数")),
                    })),

            TextBlock($"最后一次动作：{last}").Caption().Subtle(),
            TextBlock($"加粗 {bold} / 斜体 {italic}（这两个是本例自己的 state，"
                      + "官方命令条只负责把点击送回来）")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("两个槽位的区别：SelectionFlyout 是「选中文字之后」，"
                      + "ContextMenu（ContextFlyout）是「右键 / 长按」。"
                      + "同一个 TextCommandBarFlyout 元素挂到哪个上都行，"
                      + "只是只有文本控件认前者——别的控件上没有这个属性。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("顺带一提：命令条上那个「加粗」按钮用的是 AppBarToggleButton，"
                      + "它是真的 ToggleButton 子类，受控 IsChecked 走的是与命令条上同一套回声抑制。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
