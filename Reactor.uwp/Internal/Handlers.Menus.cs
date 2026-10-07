using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using System;
using System.Collections.Generic;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// <c>MenuFlyout</c> / <c>MenuFlyoutItem</c> / <c>MenuFlyoutSubItem</c> /
/// <c>MenuFlyoutSeparator</c> 的物化。
/// </summary>
/// <remarks>
/// <b>为什么它不进协调器。</b><c>MenuFlyout</c> 走 <c>FlyoutBase</c> →
/// <c>DependencyObject</c>，<c>MenuFlyoutItem</c> 走 <c>MenuFlyoutItemBase</c> →
/// <c>DependencyObject</c>——两者都不是 <c>UIElement</c>，而协调器那条路的入参与
/// 返回值都要求 <c>UIElement</c>。所以与 <c>RichTextBlock</c> / <c>Paragraph</c> /
/// <c>Run</c> 同一条规矩：<b>元素是描述，宿主 handler 就地物化</b>。
/// <para>
/// 由此带来一个可以接受的行为：菜单内容是<b>整体重建</b>而不是逐项 patch。
/// 菜单项只有文本 / 图标 / 回调三种数据、没有需要跨帧保留的状态（不像输入框里的
/// 光标），而"逐项 patch + 逐项解绑旧回调"要为每个项维护一张"元素 ↔ 原生"的表——
/// 为了一段转瞬即逝的浮出层做这套记账，代价大于收益。
/// <b>代价</b>：应用重渲染时如果菜单正开着，它会被换掉（极罕见，且重渲染通常由
/// 点菜单项本身触发，那一刻菜单本来就要关）。
/// </para>
/// </remarks>
internal static class MenuFlyouts
{
    /// <summary>
    /// 勾选状态那一发的"作者"是谁（两种可勾选菜单项共用一份：键是各自的控件实例，
    /// 两个类型不会撞车）。
    /// </summary>
    private static readonly EchoGuard CheckEcho = new();

    /// <summary>把菜单元素物化成真的 <c>MenuFlyout</c>。</summary>
    public static MenuFlyout Build(MenuFlyoutElement element)
    {
        var flyout = new MenuFlyout();

        foreach (var item in element.Items)
        {
            if (Materialize(item) is { } native)
            {
                flyout.Items.Add(native);
            }
        }

        return flyout;
    }

    /// <summary>
    /// 把一份<b>旧菜单</b>里的可勾选项从回声登记表里摘掉。
    /// </summary>
    /// <remarks>
    /// 菜单是整体重建的（见类注释），旧项连实例一起被丢掉、弱键表也随之消失，
    /// 所以这里<b>不是</b>为了防泄漏——是为了<b>不留悬空登记</b>：
    /// 一个已经被丢掉的项如果还挂着"我写过 true、在等回执"，
    /// 下一轮新建的项就会去 Consume 一份属于旧实例的登记
    /// （与命令条上 <c>AppBarCommands.Unmount</c> 同一个理由）。
    /// </remarks>
    public static void Unmount(FlyoutBase? flyout)
    {
        if (flyout is not MenuFlyout menu)
        {
            return;
        }

        // 不按类型挑：只有可勾选项会进登记表，而"多摘一个不在表里的"是空操作。
        // 反过来按类型挑就要在这里再抄一遍"哪两种是可勾选项"，多一处会走形的清单。
        foreach (var native in menu.Items)
        {
            // 顶层这一句必须<b>就写在 Unmount 的方法体里</b>：契约第七道按方法体
            // 扫这一行，转一层它就看不见了。所以这里不整个转调 ForgetNested。
            CheckEcho.Forget(native);

            // 子菜单里那一层要往下摘：理由见 ForgetNested。
            if (native is MenuFlyoutSubItem sub)
            {
                ForgetNested(sub.Items);
            }
        }
    }

    /// <summary>
    /// 子菜单里那一层（递归）。可勾选项挂在子菜单里时同样是"写完在等回执"的站点，
    /// 而 <see cref="Unmount"/> 只顺着顶层 <c>Items</c> 走一层——不往下摘，换掉菜单
    /// 之后子菜单里那一发登记就悬着了（与命令条上
    /// <c>AppBarCommands.Unmount</c> 同一个理由）。
    /// </summary>
    /// <remarks>
    /// 参数收的是<b>一组</b>而不是一项、循环变量也叫 <c>native</c>：三件套在源码里
    /// 必须落在同一个名字上（第十六道按名字比"作用在谁身上"），递归里换一个变量名
    /// 会把"三者是否同一个控件"这件事在扫描器眼里弄丢。
    /// </remarks>
    private static void ForgetNested(IList<MenuFlyoutItemBase> items)
    {
        foreach (var native in items)
        {
            CheckEcho.Forget(native);

            if (native is MenuFlyoutSubItem sub)
            {
                ForgetNested(sub.Items);
            }
        }
    }

    /// <summary>把菜单里的一个元素翻成真的 <c>MenuFlyoutItemBase</c>。</summary>
    /// <remarks>
    /// 是 internal 而不是 private：<c>MenuBar</c> 的每一组收的也是
    /// <c>MenuFlyoutItemBase</c>（<c>MenuBarItem.Items</c>），要复用这一份。
    /// </remarks>
    internal static MenuFlyoutItemBase? Materialize(Element? element) => element switch
    {
        MenuFlyoutItemElement item => CreateItem(item),
        ToggleMenuFlyoutItemElement item => CreateToggle(item),
        RadioMenuFlyoutItemElement item => CreateRadio(item),
        MenuFlyoutSubItemElement item => CreateSubMenu(item),
        MenuFlyoutSeparatorElement => new MenuFlyoutSeparator(),
        null => null,

        // 菜单里塞了不认识的东西：这不是"忽略"就完事的——它意味着有人以为
        // 菜单项能装任意子树，而官方 MenuFlyoutItem 只有文本 + 图标两个槽位。
        var other => Report(other),
    };

    /// <summary>
    /// 子菜单。<b>递归</b>调用 <see cref="Materialize"/>：官方
    /// <c>MenuFlyoutSubItem.Items</c> 收的是同一个类型
    /// （<c>MenuFlyoutItemBase</c>），所以"下面一层怎么写"与这一层完全相同——
    /// 不为第二层另写一套翻译。
    /// </summary>
    private static MenuFlyoutSubItem CreateSubMenu(MenuFlyoutSubItemElement element)
    {
        var native = new MenuFlyoutSubItem { Text = element.Text ?? string.Empty };

        // 图标与禁用照 <see cref="ApplyCommon"/> 那一条规矩写（惰性属性，罩进静默窗）。
        // 这里不转调它：官方 MenuFlyoutSubItem 继承的是 MenuFlyoutItemBase 而不是
        // MenuFlyoutItem，两个类型之间没有共同基类可套——而且它<b>没有</b>
        // KeyboardAcceleratorTextOverride（所以元素上也没这个槽位，见元素注释）。
        using (CheckEcho.Silence(native))
        {
            if (element.Icon is { } icon && CreateIcon(icon) is { } nativeIcon)
            {
                native.Icon = nativeIcon;
            }

            if (element.IsEnabled is { } enabled)
            {
                native.IsEnabled = enabled;
            }
        }

        foreach (var item in element.Items ?? Array.Empty<Element?>())
        {
            if (Materialize(item) is { } child)
            {
                native.Items.Add(child);
            }
        }

        return native;
    }

    private static MenuFlyoutItem CreateItem(MenuFlyoutItemElement item)
    {
        var native = new MenuFlyoutItem { Text = item.Text ?? string.Empty };

        ApplyCommon(native, item.Icon, item.AcceleratorText, item.IsEnabled);

        if (item.OnClick is { } onClick)
        {
            // 每次都挂在<b>新建</b>的原生项上，所以不存在"旧回调没解绑"——
            // 旧项连同它的订阅一起被回收。这也是整体重建带来的好处之一。
            native.Click += (_, _) => onClick();
        }

        return native;
    }

    private static MenuFlyoutItemBase CreateToggle(ToggleMenuFlyoutItemElement element)
    {
        var native = new ToggleMenuFlyoutItem { Text = element.Text ?? string.Empty };

        ApplyCommon(native, element.Icon, element.AcceleratorText, element.IsEnabled);

        // 订阅先挂、受控值后写：这一发是我们自己写下去的，也得走同一条
        // "认领"路径才能被认出来（与命令条上的 AppBarToggleButton 同一个理由）。
        if (element.OnIsCheckedChanged is { } callback)
        {
            native.Click += (_, _) => Fire(native, callback);
        }

        ApplyToggleChecked(native, element.IsChecked);
        return native;
    }

    private static MenuFlyoutItemBase CreateRadio(RadioMenuFlyoutItemElement element)
    {
        var native = new MuxControls.RadioMenuFlyoutItem { Text = element.Text ?? string.Empty };

        // 组名决定了"跟谁互斥"，写下去会牵动同组的其他项（它们各自的
        // IsChecked 可能被控件改掉）——所以这一发要罩住。
        using (CheckEcho.Silence(native))
        {
            if (element.GroupName is { } group)
            {
                native.GroupName = group;
            }
        }

        ApplyCommon(native, element.Icon, element.AcceleratorText, element.IsEnabled);

        if (element.OnIsCheckedChanged is { } callback)
        {
            native.Click += (_, _) => Fire(native, callback);
        }

        ApplyRadioChecked(native, element.IsChecked);
        return native;
    }

    /// <summary>
    /// 三种菜单项共用的那半截：图标 / 键盘提示 / 禁用。
    /// </summary>
    /// <remarks>
    /// 这三个属性都是<b>惰性</b>的（用户改不了它们），但既然这个类里有一份
    /// 回声登记表，就按规矩把整段罩进静默窗——写它们不会牵动 <c>IsChecked</c>，
    /// 罩住只是让"这一发不是用户输入"这句话落在明处。
    /// </remarks>
    private static void ApplyCommon(
        MenuFlyoutItem native, Element? icon, string? acceleratorText, bool? isEnabled)
    {
        using (CheckEcho.Silence(native))
        {
            if (icon is { } element && CreateIcon(element) is { } nativeIcon)
            {
                native.Icon = nativeIcon;
            }

            if (acceleratorText is { } accelerator)
            {
                native.KeyboardAcceleratorTextOverride = accelerator;
            }

            if (isEnabled is { } enabled)
            {
                native.IsEnabled = enabled;
            }
        }
    }

    /// <summary>
    /// 受控 <c>IsChecked</c> 的那一发：登记 → 写 → 没人领就撤销。
    /// </summary>
    /// <remarks>
    /// 两种可勾选项的 <c>IsChecked</c> <b>不是一个属性</b>：一个是 UWP 的、一个是
    /// WinUI 2 的，而且两者<b>没有共同的基类</b>（实测：互相赋值编译不过——
    /// <c>RadioMenuFlyoutItem</c> 并不继承 <c>ToggleMenuFlyoutItem</c>）。
    /// 所以这里<b>不合并</b>成一个 <c>switch</c>：合并的代价是"受控下发"变成一次
    /// 间接调用，而契约里那几条（三件套的次序、登记值必须就是下发值）盯的就是
    /// 这一行，隔一层就看不见了。
    /// </remarks>
    private static void ApplyToggleChecked(ToggleMenuFlyoutItem native, bool? target)
    {
        // 官方这个属性不可空，所以元素上那个 bool? 里的 null = "不管它"，
        // 不是三态（与 ToggleSplitButton 同一条规矩）。
        if (target is not { } value || native.IsChecked == value)
        {
            return;
        }

        CheckEcho.Expect(native, value);
        native.IsChecked = value;

        // 回调为空时 Click 根本没订，没人领的登记要撤销（否则它会挂到下一次）。
        CheckEcho.CancelIfUnconsumed(native);
    }

    private static void ApplyRadioChecked(MuxControls.RadioMenuFlyoutItem native, bool? target)
    {
        if (target is not { } value || native.IsChecked == value)
        {
            return;
        }

        CheckEcho.Expect(native, value);
        native.IsChecked = value;
        CheckEcho.CancelIfUnconsumed(native);
    }

    /// <summary>
    /// 两种可勾选项的 <c>IsChecked</c> <b>不是一个属性</b>（分属两个命名空间、两个类，
    /// 且没有共同基类），读回值只能按类型分派；写则各写各的（见上面两个方法）。
    /// </summary>
    private static bool? CheckedOf(MenuFlyoutItem item) => item switch
    {
        ToggleMenuFlyoutItem toggle => toggle.IsChecked,
        MuxControls.RadioMenuFlyoutItem radio => radio.IsChecked,
        _ => null,
    };

    /// <summary>
    /// 回执：从 <c>Click</c> 里<b>回读</b> <c>IsChecked</c>
    /// （官方没给 <c>Checked</c> / <c>Unchecked</c>，详见元素上的注释）。
    /// </summary>
    /// <remarks>
    /// <b>两种可勾选项共用一个 <c>Consume</c>，不各写一份。</b>回声登记对每个
    /// 受控站点只有一份，"认领"这件事也只有一种做法——写成两份的话，
    /// 契约里那条"把 <c>Consume</c> 换名、第四道必须报警"就会被另一份挡住
    /// （它按类看存在性，同类里还有一份就看不见这一份没了）。
    /// 这不是为了迁就检查，而是"一个站点一份判据"本来就是对的。
    /// </remarks>
    private static void Fire(MenuFlyoutItem native, Action<bool> callback)
    {
        if (CheckedOf(native) is not { } value)
        {
            return;
        }

        if (CheckEcho.Consume(native, value))
        {
            return;
        }

        callback(value);
    }

    /// <summary>
    /// 图标走 <c>IconElement</c>（不是 <c>IconSource</c>）：<c>MenuFlyoutItem.Icon</c>
    /// 收的是这一个。与 <c>InfoBadge</c> 那边收 <c>IconSource</c> 是官方两种不同的槽位类型，
    /// 别混。
    /// </summary>
    /// <remarks>实现已提到 <see cref="IconElements"/>：命令条按钮用同一个槽位类型。</remarks>
    private static IconElement? CreateIcon(Element element) => IconElements.From(element);

    private static MenuFlyoutItemBase? Report(Element element)
    {
        ReactorApplication.Trace(
            $"[reactor] 菜单只收 MenuItem / ToggleMenuItem / RadioMenuItem / " +
            $"SubMenuItem / MenuSeparator，收到 {element.GetType().Name}，已忽略");
        return null;
    }
}

/// <summary>
/// <c>DropDownButton</c>：一个按钮 + 挂在它身上的菜单。
/// </summary>
/// <remarks>
/// 与官方同形：它<b>没有 Click</b>——按下去的语义就是展开菜单，动作由菜单里的项承担。
/// 要"左半执行、右半展开"用 <see cref="SplitButtonHandler"/>。
/// </remarks>
internal sealed class DropDownButtonHandler : ElementHandler<DropDownButtonElement, MuxControls.DropDownButton>
{
    protected override MuxControls.DropDownButton Mount(Reconciler reconciler, DropDownButtonElement element)
    {
        var control = new MuxControls.DropDownButton();

        ApplyContent(reconciler, control, null, element);
        ApplyFlyout(reconciler, control, null, element.Flyout);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        DropDownButtonElement oldElement,
        DropDownButtonElement newElement,
        MuxControls.DropDownButton control)
    {
        ApplyContent(reconciler, control, oldElement, newElement);
        ApplyFlyout(reconciler, control, oldElement.Flyout, newElement.Flyout);
    }

    /// <summary>
    /// 摘除就地写在这里（不转调 <c>ApplyFlyout</c>）：浮出层里可能有一棵子树，
    /// 而那棵子树<b>不在</b>可视树里——<c>UnmountTree</c> 顺着
    /// <c>Content</c> / <c>Children</c> 递归，走不到它。见
    /// <see cref="ContentFlyouts"/> 的注释。
    /// </summary>
    protected override void Unmount(Reconciler reconciler, MuxControls.DropDownButton control)
    {
        ContentFlyouts.Retire(reconciler, control.Flyout);
    }

    protected override Element? SingleChildOf(DropDownButtonElement element) => element.Content;

    private static void ApplyContent(
        Reconciler reconciler,
        MuxControls.DropDownButton control,
        DropDownButtonElement? oldElement,
        DropDownButtonElement newElement) =>
        reconciler.PatchSingleChild(control, oldElement?.Content, newElement.Content);

    private static void ApplyFlyout(
        Reconciler reconciler,
        MuxControls.DropDownButton control,
        Element? oldFlyout,
        Element? newFlyout) =>
        control.Flyout = FlyoutSlot.Of(reconciler, oldFlyout, newFlyout, control.Flyout);
}

/// <summary>
/// <c>SplitButton</c>：左半边触发 <c>Click</c>，右半边展开菜单。
/// </summary>
internal sealed class SplitButtonHandler : ElementHandler<SplitButtonElement, MuxControls.SplitButton>
{
    protected override MuxControls.SplitButton Mount(Reconciler reconciler, SplitButtonElement element)
    {
        var control = new MuxControls.SplitButton();

        ApplyContent(reconciler, control, null, element);
        ApplyFlyout(reconciler, control, null, element.Flyout);
        RebindClick(control, element.OnClick);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        SplitButtonElement oldElement,
        SplitButtonElement newElement,
        MuxControls.SplitButton control)
    {
        ApplyContent(reconciler, control, oldElement, newElement);
        ApplyFlyout(reconciler, control, oldElement.Flyout, newElement.Flyout);
        RebindClick(control, newElement.OnClick);
    }

    // 摘除就地写在 Unmount 里（不转调 RebindClick）：卸载路径要能一眼看出
    // "这张表在哪儿摘"，而不是顺着回调再跳一层。
    protected override void Unmount(Reconciler reconciler, MuxControls.SplitButton control)
    {
        control.Click -= OnSplitClick;
        Clicks.Remove(control);

        // 浮出层里的子树不在可视树里，UnmountTree 递归不到（见 ContentFlyouts）。
        ContentFlyouts.Retire(reconciler, control.Flyout);
    }

    protected override Element? SingleChildOf(SplitButtonElement element) => element.Content;

    private static void ApplyContent(
        Reconciler reconciler,
        MuxControls.SplitButton control,
        SplitButtonElement? oldElement,
        SplitButtonElement newElement) =>
        reconciler.PatchSingleChild(control, oldElement?.Content, newElement.Content);

    private static void ApplyFlyout(
        Reconciler reconciler,
        MuxControls.SplitButton control,
        Element? oldFlyout,
        Element? newFlyout) =>
        control.Flyout = FlyoutSlot.Of(reconciler, oldFlyout, newFlyout, control.Flyout);

    /// <summary>
    /// 点击回调：先摘后挂。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="Reconciler.RebindButtonClick"/> 那条路走不通（它按 <c>Button</c>
    /// 收录），而 <c>SplitButton</c> 的 <c>Click</c> 是它自己的事件。这里用的是
    /// 与那条路相同的"先摘旧的、再挂新的"这套记账方式：不摘的话每轮重渲染都会
    /// 多挂一个，点一次触发 N 次。
    /// </remarks>
    private static void RebindClick(MuxControls.SplitButton control, Action? onClick)
    {
        control.Click -= OnSplitClick;

        if (onClick is null)
        {
            Clicks.Remove(control);
            return;
        }

        Clicks.Set(control, onClick);
        control.Click += OnSplitClick;
    }

    private static readonly WeakTable<MuxControls.SplitButton, Action> Clicks = new();

    private static void OnSplitClick(MuxControls.SplitButton sender, object args) =>
        Clicks[sender]?.Invoke();
}

/// <summary>
/// 浮出层槽位的共用解析（按钮的 <c>Flyout</c>、元素的 <c>ContextMenu</c>，规矩相同）。
/// </summary>
/// <remarks>
/// 与 <see cref="MenuFlyouts"/> 那个"每次重渲染都重新物化"的取舍不同，
/// <b>内容型</b>那一种是就地 patch 的，所以它要 <see cref="Reconciler"/>——
/// 这也是这个类唯一一个非静态成员参数的来由。
/// </remarks>
internal static class FlyoutSlot
{
    /// <summary>
    /// 把元素槽位翻成真的 <c>FlyoutBase</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 认四种：<see cref="MenuFlyoutElement"/>（菜单）、
    /// <see cref="CommandBarFlyoutElement"/>（命令条浮层）、
    /// <see cref="TextCommandBarFlyoutElement"/>（文本命令条）与
    /// <see cref="FlyoutElement"/>（内容型，装的是一棵<b>子树</b>）。前三种就地
    /// 物化后整体重建；最后一种<b>不走"整体重建"那条路</b>，交给
    /// <see cref="ContentFlyouts"/> 走协调器的 Build / Patch / 卸载——
    /// 因为它的内容里可以有正在输入的 <c>TextBox</c>，重建会把状态抹掉。
    /// </para>
    /// <para>
    /// 其余类型给了原生 <c>FlyoutBase</c> 实例的话走 <c>ContextFlyout</c> 那个
    /// 修饰器，而不是这里；混进来一律留痕并保持原值——静默改成 null 会让
    /// "浮出层突然消失"变成一个没有痕迹的问题。
    /// </para>
    /// </remarks>
    public static FlyoutBase? Of(
        Reconciler reconciler, Element? oldFlyout, Element? newFlyout, FlyoutBase? current)
    {
        if (newFlyout is MenuFlyoutElement menu)
        {
            // 换掉的那一份如果是内容型浮出层，里面有棵子树要收（见 ContentFlyouts）。
            ContentFlyouts.Retire(reconciler, current);
            MenuFlyouts.Unmount(current);
            return MenuFlyouts.Build(menu);
        }

        if (newFlyout is CommandBarFlyoutElement bar)
        {
            ContentFlyouts.Retire(reconciler, current);
            MenuFlyouts.Unmount(current);
            return CommandBarFlyouts.Build(bar);
        }

        // 文本命令条：与 CommandBarFlyout 同形（它是它的子类），只是多认得文本控件。
        if (newFlyout is TextCommandBarFlyoutElement text)
        {
            ContentFlyouts.Retire(reconciler, current);
            MenuFlyouts.Unmount(current);
            return CommandBarFlyouts.BuildText(text);
        }

        if (newFlyout is FlyoutElement flyout)
        {
            MenuFlyouts.Unmount(current);
            return ContentFlyouts.Build(reconciler, flyout, current);
        }

        if (newFlyout is null)
        {
            if (oldFlyout is null)
            {
                return current;
            }

            ContentFlyouts.Retire(reconciler, current);
            MenuFlyouts.Unmount(current);
            return null;
        }

        ReactorApplication.Trace(
            $"[reactor] Flyout 槽位只收 MenuFlyout / CommandBarFlyout / " +
            $"TextCommandBarFlyout / Flyout，收到 {newFlyout.GetType().Name}，保持原值");
        return current;
    }
}
