using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.System;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 命令按钮：<c>AppBarButton</c> 与 <c>AppBarSeparator</c>。
/// </summary>
/// <remarks>
/// 官方那页的形态基本都在：<b>符号图标 + 文字</b>、<b>只有文字</b>、
/// <b>字体图标</b>（连字体一起指定的那一档）、<b>挂快捷键的</b>、<b>禁用</b>，
/// 外加几条分隔线。
/// <list type="bullet">
///   <item><c>AppBarButton</c> <b>不是</b> <c>Button</c> 的子类：它继承自
///         <c>ButtonBase</c>，实现的是 <c>ICommandBarElement</c>——
///         <b>进了 <c>PrimaryCommands</c> 就不能再进 <c>Panel</c></b>。
///         所以它是"挂在命令条上的部件"，不是能独立站位的控件；
///         本库也因此把它就地物化（不进协调器），见 <c>Handlers.Shell.cs</c>。</item>
///   <item><c>AppBarSeparator</c> 同理：它是<b>命令条里的一条分隔线</b>，
///         不是 <c>MenuSeparator</c>（那是菜单里的），两者类型不同、不能互换。</item>
///   <item>图标三种写法本库都收：<c>SymbolIcon</c>（官方 <c>Symbol</c> 枚举，
///         "Segoe MDL2 Assets 里那批常用码位"的具名清单）、<c>FontIcon</c>
///         （任意码位）、<c>BitmapIcon</c>（位图）。落到 <c>IconSource</c> 槽位时
///         会被翻成对应的 <c>*IconSource</c>。</item>
///   <item><b>禁用是 <c>isEnabled</c></b>，不是"不给回调"。不给回调的结果是
///         "按下去没反应"，而禁用会变灰、且不接焦点——两件事。</item>
/// </list>
/// <para>
/// 下面的按钮放在 <c>CommandBar</c> 里，是因为它<b>只有在命令条里才是它自己</b>
/// （溢出、标签显示时机、图标尺寸都由命令条决定）；单独摆出来就只是个
/// 长得奇怪的按钮。同一批按钮也用在 <c>CommandBarFlyout</c> 与
/// <c>TextCommandBarFlyout</c> 上——"命令项怎么写"只有一份写法。
/// </para>
/// </remarks>
public sealed class AppBarButtonBasic : Component
{
    public override Element Render()
    {
        var (last, setLast) = UseState("（还没点过）");

        return VStack(16,
            CommandBar(
                    content: TextBlock($"最后一次动作：{last}").Caption().Subtle(),
                    primary: new Element?[]
                    {
                        // 符号图标（官方 Symbol 枚举）+ 文字标签。
                        AppBarButton("复制", SymbolIcon(Symbol.Copy), () => setLast("复制")),
                        AppBarButton("剪切", SymbolIcon(Symbol.Cut), () => setLast("剪切")),
                        AppBarButton("粘贴", SymbolIcon(Symbol.Paste), () => setLast("粘贴")),

                        AppBarSeparator(),

                        // 字体图标：任意码位（这里是「设置」）。
                        AppBarButton("设置", "\uE713", () => setLast("设置")),

                        // 连字体一起指定：官方那一档是 Candara 的 Σ。
                        // 走 fontFamily 而不是默认图标字体，是这个重载存在的理由。
                        AppBarButton("求和", FontIcon("Σ", fontFamily: "Candara"),
                            () => setLast("求和")),

                        // 快捷键：官方这一档给 Save 挂 Ctrl+S。
                        // 注意命令项是"就地物化"的（见 Handlers.Shell.CreateButton），
                        // 那一段只读 Label / Icon / IsEnabled / OnClick，不读元素上的修饰符，
                        // 所以这一条目前是"把意图写在元素上"，真正的
                        // KeyboardAccelerators 要等物化那头把 modifiers 带上才生效。
                        AppBarButton("保存", SymbolIcon(Symbol.Save), () => setLast("保存"))
                            .KeyboardAccelerator(VirtualKey.S, VirtualKeyModifiers.Control,
                                () => setLast("Ctrl+S 保存")),

                        // 只有文字、没有图标：命令条上也是合法的。
                        AppBarButton("关于", onClick: () => setLast("关于")),

                        AppBarSeparator(),

                        // 禁用：变灰、不接焦点，与"给了回调但什么都不做"是两回事。
                        AppBarButton("禁用项", SymbolIcon(Symbol.Delete),
                            () => setLast("（不会走到这里）"), isEnabled: false),
                    }),

            TextBlock("把窗口拖窄：放不下的项由官方的溢出算法收进「…」，"
                      + "这不是本库做的——命令条的溢出是控件自己的行为。"
                      + "这里 <c>…</c> 按钮设为常驻可见，所以即使没溢出也能点开看结构。")
                .Caption()
                .Subtle()
                .Wrap(),

            TextBlock("AppBarButton 与 Button 不是一回事：前者实现 ICommandBarElement，"
                      + "进了命令组就不能再当普通的 Panel 子项用。"
                      + "它的图标槽位收 IconElement（SymbolIcon / FontIcon / BitmapIcon），"
                      + "落到 IconSource 槽位时会被翻成对应的 *IconSource。")
                .Caption()
                .Subtle()
                .Wrap());
    }
}
