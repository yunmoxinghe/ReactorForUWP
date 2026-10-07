using System;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls.Primitives;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 命令与菜单：<c>MenuFlyout</c> / <c>DropDownButton</c> / <c>SplitButton</c>。
/// </summary>
/// <remarks>
/// 这一组对应 WinUI 3 Gallery 里「Buttons」与「Menus &amp; toolbars」两页的那几个控件，
/// 全部套 WinUI 2 真控件：<c>Microsoft.UI.Xaml.Controls.DropDownButton</c> /
/// <c>SplitButton</c> 与 <c>Windows.UI.Xaml.Controls.MenuFlyout</c>。
/// </remarks>
public static partial class Factories
{
    /// <summary>浮出菜单。<paramref name="items"/> 里放 <see cref="MenuItem"/> / <see cref="MenuSeparator"/>。</summary>
    public static MenuFlyoutElement MenuFlyout(params Element?[] items) => new(items);

    /// <summary>
    /// 菜单项。
    /// </summary>
    /// <param name="text">显示文本（官方 <c>Text</c>）。</param>
    /// <param name="icon">图标（<c>FontIcon</c> / <c>BitmapIcon</c>）。</param>
    /// <param name="onClick">点击回调（官方 <c>Click</c>）。</param>
    /// <param name="acceleratorText">右侧的键盘提示串，如 <c>"Ctrl+S"</c>。</param>
    /// <param name="isEnabled">禁用态。</param>
    public static MenuFlyoutItemElement MenuItem(
        string? text = null,
        Element? icon = null,
        Action? onClick = null,
        string? acceleratorText = null,
        bool? isEnabled = null) =>
        new(text)
        {
            Icon = icon,
            OnClick = onClick,
            AcceleratorText = acceleratorText,
            IsEnabled = isEnabled,
        };

    /// <summary>菜单分隔线。</summary>
    public static MenuFlyoutSeparatorElement MenuSeparator() => new();

    /// <summary>
    /// 子菜单：一项，展开又是一份菜单（官方 <c>MenuFlyoutSubItem</c>）。
    /// </summary>
    /// <param name="text">子菜单那一项的文本。</param>
    /// <param name="icon">图标（<c>FontIcon</c> / <c>BitmapIcon</c> / <c>ImageIcon</c>）。</param>
    /// <param name="isEnabled">禁用态。</param>
    /// <param name="items">
    /// 展开后的菜单项：<see cref="MenuItem"/> / <see cref="ToggleMenuItem"/> /
    /// <see cref="RadioMenuItem"/> / <see cref="MenuSeparator"/>，
    /// 也可以再嵌一层 <see cref="SubMenuItem"/>（官方允许嵌套）。
    /// </param>
    /// <remarks>
    /// <b>没有点击回调</b>：点它是"展开"不是"执行"，官方也没给它单独的 Click 语义
    /// ——详见 <see cref="MenuFlyoutSubItemElement"/>。
    /// <para>
    /// <b>没有 <c>acceleratorText</c> 这个参数</b>：官方
    /// <c>MenuFlyoutSubItem</c> 上就没有 <c>KeyboardAcceleratorTextOverride</c>
    /// （它不继承 <c>MenuFlyoutItem</c>），给了也是白给。
    /// </para>
    /// </remarks>
    public static MenuFlyoutSubItemElement SubMenuItem(
        string? text = null,
        Element? icon = null,
        bool? isEnabled = null,
        params Element?[] items) =>
        new(text, FilterChildren(items))
        {
            Icon = icon,
            IsEnabled = isEnabled,
        };

    /// <summary>
    /// <see cref="SubMenuItem(string?, Element?, bool?, Element?[])"/> 的省事重载：
    /// 只要文本 + 菜单项（不给图标 / 不改禁用态）。
    /// </summary>
    /// <remarks>
    /// 多这一条不是"重载糖"：上面那条把 <c>params</c> 放在最后，前面还夹着两个
    /// 可选参数，于是"给文本 + 一串菜单项"这个最常见的写法会被第二参数
    /// <b>当图标吃进去</b>（类型倒也对得上，错在沉默里）。
    /// 与其让人靠报错去学，不如给一条没有图标位置的重载。
    /// </remarks>
    public static MenuFlyoutSubItemElement SubMenuItem(string? text, params Element?[] items) =>
        new(text, FilterChildren(items));

    /// <summary>
    /// 可勾选的菜单项（官方 <c>ToggleMenuFlyoutItem</c>）。
    /// </summary>
    /// <remarks>
    /// 它只有 <c>Click</c> 一个回执通道（没有 <c>Checked</c> / <c>Unchecked</c>），
    /// 所以 <paramref name="onIsCheckedChanged"/> 的参数是<b>回读</b>出来的值——
    /// 详见 <see cref="ToggleMenuFlyoutItemElement"/> 的说明。
    /// </remarks>
    /// <param name="text">显示文本。</param>
    /// <param name="isChecked">勾选状态（<b>受控</b>：给了值才受控，null = 不管它）。</param>
    /// <param name="onIsCheckedChanged">勾选状态变了。</param>
    /// <param name="icon">图标（<c>FontIcon</c> / <c>BitmapIcon</c>）。</param>
    /// <param name="acceleratorText">右侧的键盘提示串。</param>
    /// <param name="isEnabled">禁用态。</param>
    public static ToggleMenuFlyoutItemElement ToggleMenuItem(
        string? text = null,
        bool? isChecked = null,
        Action<bool>? onIsCheckedChanged = null,
        Element? icon = null,
        string? acceleratorText = null,
        bool? isEnabled = null) =>
        new(text)
        {
            IsChecked = isChecked,
            OnIsCheckedChanged = onIsCheckedChanged,
            Icon = icon,
            AcceleratorText = acceleratorText,
            IsEnabled = isEnabled,
        };

    /// <summary>
    /// 单选菜单项（WinUI 2 的 <c>RadioMenuFlyoutItem</c>）。
    /// </summary>
    /// <remarks>
    /// 多选一靠 <paramref name="groupName"/>：同名的项互斥，而官方<b>只管互斥、
    /// 不管选中</b>——它会逐项回调过来，别假设只有一发。
    /// </remarks>
    /// <param name="text">显示文本。</param>
    /// <param name="isChecked">勾选状态（<b>受控</b>：给了值才受控，null = 不管它）。</param>
    /// <param name="onIsCheckedChanged">勾选状态变了。</param>
    /// <param name="groupName">组名（同名互斥；不给就是"单飞"）。</param>
    /// <param name="icon">图标（<c>FontIcon</c> / <c>BitmapIcon</c>）。</param>
    /// <param name="acceleratorText">右侧的键盘提示串。</param>
    /// <param name="isEnabled">禁用态。</param>
    public static RadioMenuFlyoutItemElement RadioMenuItem(
        string? text = null,
        bool? isChecked = null,
        Action<bool>? onIsCheckedChanged = null,
        string? groupName = null,
        Element? icon = null,
        string? acceleratorText = null,
        bool? isEnabled = null) =>
        new(text)
        {
            IsChecked = isChecked,
            OnIsCheckedChanged = onIsCheckedChanged,
            GroupName = groupName,
            Icon = icon,
            AcceleratorText = acceleratorText,
            IsEnabled = isEnabled,
        };

    /// <summary>
    /// 内容型浮出层：<b>里面可以装任意一棵子树</b>（与 <see cref="MenuFlyout"/> 只装"项"相对）。
    /// </summary>
    /// <remarks>
    /// 挂在按钮的 <c>Flyout</c> 槽位上，或作为 <c>ContextMenu</c>（右键 / 长按）。
    /// <b>没有受控的 <c>IsOpen</c></b>——打开动作是 <c>ShowAt(目标)</c>，
    /// 而目标正是挂它的那个按钮，框架替你做了；元素上没有这个槽位可填，
    /// 详见 <see cref="FlyoutElement"/> 的说明。
    /// </remarks>
    /// <param name="content">浮出层里的内容（一棵子树，不是"项"）。</param>
    /// <param name="placement">弹出方位。</param>
    /// <param name="showMode">弹出方式（<c>Transient</c> = 点别处就收）。</param>
    /// <param name="areOpenCloseAnimationsEnabled">要不要开合动画。</param>
    /// <param name="onOpened">打开了。</param>
    /// <param name="onClosed">收起了。</param>
    public static FlyoutElement Flyout(
        Element? content = null,
        FlyoutPlacementMode placement = FlyoutPlacementMode.Top,
        FlyoutShowMode showMode = FlyoutShowMode.Auto,
        bool areOpenCloseAnimationsEnabled = true,
        Action? onOpened = null,
        Action? onClosed = null) =>
        new(content)
        {
            Placement = placement,
            ShowMode = showMode,
            AreOpenCloseAnimationsEnabled = areOpenCloseAnimationsEnabled,
            OnOpened = onOpened,
            OnClosed = onClosed,
        };

    /// <summary>
    /// 下拉按钮：<b>没有点击回调</b>，按下去就是展开菜单（与官方一致）。
    /// </summary>
    /// <param name="content">按钮内容（可以是元素树，也可以走 <c>DropDownButton(string)</c>）。</param>
    /// <param name="flyout">挂在它身上的浮出层，一般给 <see cref="MenuFlyout"/>。</param>
    public static DropDownButtonElement DropDownButton(Element? content = null, Element? flyout = null) =>
        new(content) { Flyout = flyout };

    /// <summary>
    /// 下拉按钮的便捷重载（选中与控制在后、内容在最前）。
    /// </summary>
    public static DropDownButtonElement DropDownButton(string label, Element? flyout = null) =>
        DropDownButton(TextBlock(label), flyout);

    /// <summary>拆分按钮：左半边 <paramref name="onClick"/> 执行，右半边展开 <paramref name="flyout"/>。</summary>
    public static SplitButtonElement SplitButton(
        Element? content = null,
        Action? onClick = null,
        Element? flyout = null) =>
        new(content) { OnClick = onClick, Flyout = flyout };

    /// <summary>拆分按钮的便捷重载。</summary>
    public static SplitButtonElement SplitButton(
        string label,
        Action? onClick = null,
        Element? flyout = null) =>
        SplitButton(TextBlock(label), onClick, flyout);
}
