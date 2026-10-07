using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls.Primitives;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 浮出菜单，对应 <c>MenuFlyout</c>。
/// </summary>
/// <remarks>
/// <b>它不是 <c>UIElement</c>。</b><c>MenuFlyout</c> 继承 <c>FlyoutBase</c> →
/// <c>DependencyObject</c>，不在可视树里占位，所以走不了协调器那条路
/// （<c>Build</c> 的入参与返回值都是 <c>UIElement</c>）。因此它与
/// <c>RichTextBlock</c> / <c>Paragraph</c> / <c>Run</c> 同形：<b>元素是数据的描述</b>，
/// 由宿主控件的 handler 就地物化，按位 patch 而不进协调器。
/// <para>
/// 也正因为它不在可视树里，它是<b>挂在别人身上的子部件</b>而不是一个可独立站位的控件：
/// 给 <c>Button.Flyout</c>、<c>DropDownButton.Flyout</c>、<c>SplitButton.Flyout</c>，
/// 或作为 <c>ContextFlyout</c>。
/// </para>
/// </remarks>
public sealed record MenuFlyoutElement(IReadOnlyList<Element?> Items) : Element;

/// <summary>
/// 菜单项，对应 <c>MenuFlyoutItem</c>。
/// </summary>
/// <remarks>
/// 同样<b>不是 <c>UIElement</c></b>（<c>MenuFlyoutItem</c> → <c>MenuFlyoutItemBase</c> →
/// <c>DependencyObject</c>）。它的内容只有文本与图标两种形态，官方控件本身就规定了，
/// 因此这里不设"内容槽"——想塞任意子树请用
/// <see cref="MenuFlyoutElement"/> 之外的承载方式（比如直接给宿主元素树）。
/// </remarks>
public sealed record MenuFlyoutItemElement(string? Text = null) : Element
{
    /// <summary>菜单项图标（<c>FontIcon</c> / <c>BitmapIcon</c>），对应官方 <c>Icon</c>。</summary>
    public Element? Icon { get; init; }

    /// <summary>点击回调，对应官方 <c>Click</c> 事件。</summary>
    public Action? OnClick { get; init; }

    /// <summary>键盘提示串（XAML 的 <c>KeyboardAcceleratorTextOverride</c>），如 "Ctrl+S"。</summary>
    public string? AcceleratorText { get; init; }

    /// <summary>禁用（XAML 的 <c>IsEnabled</c>）。</summary>
    public bool? IsEnabled { get; init; }
}

/// <summary>菜单分隔线，对应 <c>MenuFlyoutSeparator</c>。</summary>
public sealed record MenuFlyoutSeparatorElement() : Element;

/// <summary>
/// 子菜单，对应 UWP 原生的 <c>MenuFlyoutSubItem</c>：一项，展开又是一份菜单。
/// </summary>
/// <remarks>
/// <para>
/// <b>官方 <c>MenuFlyoutSubItem</c> 继承的是 <c>MenuFlyoutItemBase</c>，
/// 不是 <c>MenuFlyoutItem</c></b>（实测：两者互相赋值编译不过）。所以它有
/// <c>Text</c> / <c>Icon</c> / <c>IsEnabled</c>，却<b>没有</b>
/// <c>KeyboardAcceleratorTextOverride</c>——于是这里也就没有那个槽位，
/// 而不是给了之后偷偷不生效。
/// </para>
/// <para>
/// <b><see cref="Items"/> 里能再放一个 <see cref="MenuFlyoutSubItemElement"/></b>——
/// 官方的 <c>MenuFlyoutSubItem.Items</c> 收的同样是
/// <c>MenuFlyoutItemBase</c>，所以嵌套是官方本来就允许的（<c>文件 → 最近使用 → …</c>
/// 那种两层菜单）。这里不设深度上限，也不"为了防止写错而拒绝第二层"：
/// 那道限制得靠人看着，机器看不出来。
/// </para>
/// <para>
/// <b>它没有 <c>Click</c> 的语义</b>：点它是"展开"，不是"执行"，
/// 所以这里不提供 <c>OnClick</c>——与 <see cref="DropDownButtonElement"/>
/// 那个"没有点击回调"是同一个理由。
/// </para>
/// </remarks>
public sealed record MenuFlyoutSubItemElement(
    string? Text = null,
    IReadOnlyList<Element?>? Items = null) : Element
{
    /// <summary>子菜单项图标（<c>FontIcon</c> / <c>BitmapIcon</c> / <c>ImageIcon</c>）。</summary>
    public Element? Icon { get; init; }

    /// <summary>禁用（XAML 的 <c>IsEnabled</c>）。</summary>
    public bool? IsEnabled { get; init; }
}

/// <summary>
/// 可勾选的菜单项，对应 UWP 原生的 <c>ToggleMenuFlyoutItem</c>。
/// </summary>
/// <remarks>
/// <b>它只有一个回执通道：<c>Click</c>。</b>官方 <c>ToggleMenuFlyoutItem</c>
/// 上没有 <c>Checked</c> / <c>Unchecked</c>，也没有 <c>IsCheckedChanged</c>——
/// 点一下就把 <c>IsChecked</c> 翻过来、然后抛 <c>Click</c>（它继承
/// <c>MenuFlyoutItem</c>）。所以回执只能从 <c>Click</c> 里<b>回读</b>
/// <c>IsChecked</c>，而不是从事件参数里拿。
/// <para>
/// 也正因为在 <c>Click</c> 里回读，"用户点了"与"我们写的"两种来源在事件里长得一模一样，
/// 必须靠 <c>EchoGuard</c> 区分——与命令条上那个
/// <c>AppBarToggleButton</c> 是同一个问题的两种形态（那边有
/// <c>Checked</c> / <c>Unchecked</c> 两个事件、共一份登记；这里只有一个事件）。
/// </para>
/// </remarks>
public sealed record ToggleMenuFlyoutItemElement(string? Text = null) : Element
{
    /// <summary>菜单项图标（<c>FontIcon</c> / <c>BitmapIcon</c>），对应官方 <c>Icon</c>。</summary>
    public Element? Icon { get; init; }

    /// <summary>
    /// 勾选状态。<b>受控</b>：给了值才受控，<c>null</c> 就是"不管它"。
    /// 官方这个属性<b>可空</b>（三态），所以这里也是 <c>bool?</c>。
    /// </summary>
    public bool? IsChecked { get; init; }

    /// <summary>勾选状态变了。参数是回读出来的 <c>IsChecked</c>（非 null 的那一半）。</summary>
    public Action<bool>? OnIsCheckedChanged { get; init; }

    /// <summary>键盘提示串（XAML 的 <c>KeyboardAcceleratorTextOverride</c>），如 "Ctrl+B"。</summary>
    public string? AcceleratorText { get; init; }

    /// <summary>禁用（XAML 的 <c>IsEnabled</c>）。</summary>
    public bool? IsEnabled { get; init; }
}

/// <summary>
/// 单选菜单项，对应 WinUI 2 的 <c>RadioMenuFlyoutItem</c>。
/// </summary>
/// <remarks>
/// <b>它与 <see cref="ToggleMenuFlyoutItemElement"/> 不是一个命名空间里的东西</b>
/// （这个是 <c>Microsoft.UI.Xaml.Controls</c>、那个是
/// <c>Windows.UI.Xaml.Controls</c>）——官方把它们分在两处，这里照抄这个划分，
/// 不"顺手统一"。
/// <para>
/// 多选一靠 <see cref="GroupName"/>：<b>同名的项互斥</b>，且官方<b>只管互斥、
/// 不管选中</b>——它不会替你把同组的其他项取消掉之后再来回调，
/// 所以"选中了第 2 个"这件事会<b>逐项</b>回调过来（第 2 个 true、
/// 其余被控件自己改成 false 也会各来一发）。用 state 收的时候要认这个次序，
/// 别假设只有一发。
/// </para>
/// </remarks>
public sealed record RadioMenuFlyoutItemElement(string? Text = null) : Element
{
    /// <summary>菜单项图标（<c>FontIcon</c> / <c>BitmapIcon</c>）。</summary>
    public Element? Icon { get; init; }

    /// <summary>勾选状态。<b>受控</b>：给了值才受控，<c>null</c> 就是"不管它"。</summary>
    public bool? IsChecked { get; init; }

    /// <summary>勾选状态变了。参数是回读出来的 <c>IsChecked</c>。</summary>
    public Action<bool>? OnIsCheckedChanged { get; init; }

    /// <summary>组名（官方 <c>GroupName</c>）。同名互斥；不给就是"单飞"。</summary>
    public string? GroupName { get; init; }

    /// <summary>键盘提示串（XAML 的 <c>KeyboardAcceleratorTextOverride</c>）。</summary>
    public string? AcceleratorText { get; init; }

    /// <summary>禁用（XAML 的 <c>IsEnabled</c>）。</summary>
    public bool? IsEnabled { get; init; }
}

/// <summary>
/// 内容型浮出层，对应 UWP 原生的 <c>Windows.UI.Xaml.Controls.Flyout</c>
/// （XAML 里 <c>&lt;Button.Flyout&gt;&lt;Flyout&gt;…&lt;/Flyout&gt;</c> 那一种）。
/// </summary>
/// <remarks>
/// <para>
/// <b>与 <see cref="MenuFlyoutElement"/> 的分工。</b>菜单装的是"项"
/// （文本 + 图标 + 回调，没有跨帧状态）；这里装的是<b>任意一棵子树</b>——
/// 里面可以有一个正在输入、带着光标的 <c>TextBox</c>。就这一点区别，
/// 决定了它<b>必须走协调器</b>：子树要挂载、要就地 patch、要在宿主卸载时
/// 递归卸载，三件事都只有协调器做得到（详见
/// <see cref="Reactor.Uwp.Internal.ContentFlyouts"/> 的注释）。
/// </para>
/// <para>
/// <b>没有受控的 <c>IsOpen</c>。</b>官方 <c>Flyout</c> 的打开动作是
/// <c>ShowAt(目标)</c>——目标就是<b>挂它的那个按钮</b>，而这个"挂在谁身上"
/// 正是框架替你做的（走按钮的 <see cref="DropDownButtonElement.Flyout"/> 槽位
/// 或 <c>ContextMenu</c> 修饰器）。所以"打开"这件事在元素上没有可填写的槽位，
/// 也就不存在可判定的受控落点：按一贯规矩，<b>没有回执通道的属性不装成受控</b>。
/// 想知道它开了 / 关了，用 <see cref="OnOpened"/> / <see cref="OnClosed"/>。
/// </para>
/// </remarks>
public sealed record FlyoutElement(Element? Content = null) : Element
{
    /// <summary>弹出方位（官方 <c>Placement</c>，默认 <c>Top</c>）。</summary>
    public FlyoutPlacementMode Placement { get; init; } = FlyoutPlacementMode.Top;

    /// <summary>
    /// 弹出方式（官方 <c>ShowMode</c>，默认 <c>Auto</c>）。
    /// <c>Transient</c> = 点别处就收，<c>Standard</c> = 要显式关。
    /// </summary>
    public FlyoutShowMode ShowMode { get; init; } = FlyoutShowMode.Auto;

    /// <summary>要不要开合动画（官方 <c>AreOpenCloseAnimationsEnabled</c>，默认开）。</summary>
    public bool AreOpenCloseAnimationsEnabled { get; init; } = true;

    /// <summary>打开了（官方 <c>Opened</c>）。</summary>
    public Action? OnOpened { get; init; }

    /// <summary>收起了（官方 <c>Closed</c>）。</summary>
    public Action? OnClosed { get; init; }
}

/// <summary>
/// 下拉按钮，对应 WinUI 2 的 <c>DropDownButton</c>（一个按钮 + 一个挂在它身上的菜单）。
/// </summary>
/// <remarks>
/// 与官方同形：它<b>没有 Click 事件</b>（语义上"按下去是为了展开菜单"，
/// 点击动作属于菜单里的项）。要"既能直接执行也能展开菜单"用
/// <see cref="SplitButtonElement"/>。
/// </remarks>
public sealed record DropDownButtonElement(Element? Content = null) : Element
{
    /// <summary>挂在按钮上的浮出层，对应官方 <c>Flyout</c>，一般给 <see cref="MenuFlyoutElement"/>。</summary>
    public Element? Flyout { get; init; }
}

/// <summary>
/// 拆分按钮，对应 WinUI 2 的 <c>SplitButton</c>（左半执行、右半展开菜单）。
/// </summary>
public sealed record SplitButtonElement(Element? Content = null) : Element
{
    /// <summary>左半边的点击回调，对应官方 <c>Click</c> 事件。</summary>
    public Action? OnClick { get; init; }

    /// <summary>右半边展开的浮出层，对应官方 <c>Flyout</c>，一般给 <see cref="MenuFlyoutElement"/>。</summary>
    public Element? Flyout { get; init; }
}
