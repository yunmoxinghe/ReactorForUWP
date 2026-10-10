using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml;
using WuControls = Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// UWP 原生的 <c>SplitView</c>：一个侧边面板 + 一片主内容区。
/// </summary>
/// <remarks>
/// <b>为什么 <c>Content</c> 走协调器那条路、<c>Pane</c> 自己管。</b>
/// 协调器的 <c>PatchSingleChild</c> 默认只认 <c>ContentControl</c> 与 <c>Border</c>，
/// <c>SplitView</c> 两者都不是（它继承 <c>Control</c>）。给它补一个
/// <c>SingleChildAccessor</c>（在 <c>ElementHandlerRegistry</c> 里登记）之后
/// <c>Content</c> 就能走那条通用路径了——两个槽位的读写形状是一样的，
/// 没有理由为 <c>Content</c> 另写一套。
/// <c>Pane</c> 是第二个槽位，通用路径里没有"第二个"这个概念，所以自己管。
/// <para>
/// <b>卸载为什么要自己走一遍。</b>协调器的 <c>UnmountTree</c> 替三种形状递归：
/// 组件包装、<c>Panel</c> + <c>ChildrenOf</c>、<c>SingleChildOf</c>，
/// 而 <c>SingleChildOf</c> 那一支只从 <c>ContentControl</c> / <c>Border</c> 里取内容
/// ——<c>SplitView</c> 两个都不匹配，于是两个槽位里的组件 cleanup 全都跑不到
/// （与 <c>PivotHandler</c> 那段注释记的是同一个坑，只是形状不同）。
/// 所以 <see cref="Slots"/> 记住最近一次下发的两个元素，卸载时逐个递归。
/// </para>
/// </remarks>
internal sealed class SplitViewHandler : ElementHandler<SplitViewElement, WuControls.SplitView>
{
    /// <summary>最近一次下发的两个槽位元素（卸载时逐个递归用）。</summary>
    private static readonly WeakTable<WuControls.SplitView, (Element? Pane, Element? Content)> Slots = new();

    protected override WuControls.SplitView Mount(Reconciler reconciler, SplitViewElement element)
    {
        var control = new WuControls.SplitView
        {
            IsPaneOpen = element.IsPaneOpen,
            DisplayMode = element.DisplayMode,
            OpenPaneLength = element.OpenPaneLength,
            CompactPaneLength = element.CompactPaneLength,
            PanePlacement = element.PanePlacement,
        };

        if (element.PaneBackground is { } background)
        {
            control.PaneBackground = background;
        }

        ApplyPane(reconciler, control, null, element.Pane);
        control.Content = element.Content is null ? null : reconciler.Build(element.Content);
        Slots.Set(control, (element.Pane, element.Content));
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        SplitViewElement oldElement,
        SplitViewElement newElement,
        WuControls.SplitView control)
    {
        // 只写真正变化的属性：SplitView 的面板开合由用户操作（轻扫 / 点汉堡键）
        // 驱动，每帧无条件写回会把用户刚做出的开合拽回来。
        if (oldElement.IsPaneOpen != newElement.IsPaneOpen)
        {
            control.IsPaneOpen = newElement.IsPaneOpen;
        }

        if (oldElement.DisplayMode != newElement.DisplayMode)
        {
            control.DisplayMode = newElement.DisplayMode;
        }

        if (Math.Abs(oldElement.OpenPaneLength - newElement.OpenPaneLength) > 1e-6)
        {
            control.OpenPaneLength = newElement.OpenPaneLength;
        }

        if (Math.Abs(oldElement.CompactPaneLength - newElement.CompactPaneLength) > 1e-6)
        {
            control.CompactPaneLength = newElement.CompactPaneLength;
        }

        if (oldElement.PanePlacement != newElement.PanePlacement)
        {
            control.PanePlacement = newElement.PanePlacement;
        }

        // 刷子按引用比：每轮 new 一个 AcrylicBrush 就每帧重建一次模糊层，
        // 那是真金白银的重绘（见 PropWriter 的类注释）。
        PropWriter.SetRef(oldElement.PaneBackground, newElement.PaneBackground, value =>
        {
            if (value is not null)
            {
                control.PaneBackground = value;
            }
        });

        ApplyPane(reconciler, control, oldElement.Pane, newElement.Pane);
        reconciler.PatchSingleChild(control, oldElement.Content, newElement.Content);
        Slots.Set(control, (newElement.Pane, newElement.Content));
    }

    protected override void Unmount(Reconciler reconciler, WuControls.SplitView control) =>
        // Content 槽由 UnmountTree 的通用路径递归，Pane 槽由下面的 ExtraSlotsOf 报出
        // ——这里不再手写任何一次递归。以前是手写的，因为当时公共路径的卸载侧
        // 认不出这个类（见 SingleChildAccessor 的类注释），现在那个洞已经收了。
        Slots.Remove(control);

    /// <summary>
    /// <c>Pane</c> 是主槽之外的第二个槽：<c>UnmountTree</c> 遍历到这里才能连同
    /// 里面的组件一起回收。
    /// </summary>
    protected override IReadOnlyList<(UIElement Native, Element? Element)> ExtraSlotsOf(
        WuControls.SplitView control)
    {
        if (!Slots.TryGetValue(control, out var slots) || control.Pane is not { } pane)
        {
            return Array.Empty<(UIElement, Element?)>();
        }

        return new[] { (pane, slots.Pane) };
    }

    protected override Element? SingleChildOf(SplitViewElement element) => element.Content;

    /// <summary>面板槽位：能就地 patch 就 patch，类型换了才重建。</summary>
    private static void ApplyPane(
        Reconciler reconciler,
        WuControls.SplitView control,
        Element? oldPane,
        Element? newPane)
    {
        var current = control.Pane;

        if (newPane is null)
        {
            if (current is not null)
            {
                reconciler.UnmountNative(current, oldPane ?? EmptyElement.Instance);
                control.Pane = null;
            }

            return;
        }

        if (current is null)
        {
            control.Pane = reconciler.Build(newPane);
            return;
        }

        if (oldPane is not null && Reconciler.CanPatch(oldPane, newPane))
        {
            reconciler.Patch(current, oldPane, newPane);
            return;
        }

        if (oldPane is not null)
        {
            reconciler.UnmountNative(current, oldPane);
        }

        control.Pane = reconciler.Build(newPane);
    }
}

/// <summary>
/// UWP 原生的 <c>CommandBar</c>：一行命令 + 一片内容区。
/// </summary>
/// <remarks>
/// 它继承 <c>ContentControl</c>，因此 <c>Content</c> 直接走协调器的单子槽位
/// （<c>PatchSingleChild</c> 认 <c>ContentControl</c>），<c>UnmountTree</c> 也认得它。
/// 只有两组命令要自己物化。
/// </remarks>
internal sealed class CommandBarHandler : ElementHandler<CommandBarElement, WuControls.CommandBar>
{
    protected override WuControls.CommandBar Mount(Reconciler reconciler, CommandBarElement element)
    {
        var control = new WuControls.CommandBar
        {
            DefaultLabelPosition = element.DefaultLabelPosition,
            OverflowButtonVisibility = element.OverflowButtonVisibility,
            IsSticky = element.IsSticky,
        };

        ApplyCommands(control.PrimaryCommands, element.Primary);
        ApplyCommands(control.SecondaryCommands, element.Secondary);
        reconciler.PatchSingleChild(control, null, element.Content);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        CommandBarElement oldElement,
        CommandBarElement newElement,
        WuControls.CommandBar control)
    {
        if (oldElement.DefaultLabelPosition != newElement.DefaultLabelPosition)
        {
            control.DefaultLabelPosition = newElement.DefaultLabelPosition;
        }

        if (oldElement.OverflowButtonVisibility != newElement.OverflowButtonVisibility)
        {
            control.OverflowButtonVisibility = newElement.OverflowButtonVisibility;
        }

        if (oldElement.IsSticky != newElement.IsSticky)
        {
            control.IsSticky = newElement.IsSticky;
        }

        ApplyCommands(control.PrimaryCommands, newElement.Primary);
        ApplyCommands(control.SecondaryCommands, newElement.Secondary);
        reconciler.PatchSingleChild(control, oldElement.Content, newElement.Content);
    }

    protected override Element? SingleChildOf(CommandBarElement element) => element.Content;

    /// <summary>
    /// 命令组是<b>整体重建</b>而不是逐项 patch，与 <c>MenuFlyout</c> 同一个取舍
    /// （理由见 <c>MenuFlyouts</c> 那段注释）：命令项只有文字 / 图标 / 回调三种数据，
    /// 没有需要跨帧保留的状态。
    /// </summary>
    /// <remarks>
    /// 重建前要先走一遍 <see cref="AppBarCommands.Unmount"/>：被换掉的旧实例连同
    /// 它身上的回声登记一起丢，登记不摘就会被静态表钉住（见那里的注释）。
    /// </remarks>
    private static void ApplyCommands(
        IList<WuControls.ICommandBarElement> target,
        IReadOnlyList<Element?>? items)
    {
        AppBarCommands.Unmount(target);
        target.Clear();

        foreach (var item in items ?? Array.Empty<Element?>())
        {
            if (AppBarCommands.Materialize(item) is { } native)
            {
                target.Add(native);
            }
        }
    }
}

/// <summary>
/// 命令条上那些项（<c>AppBarButton</c> / <c>AppBarSeparator</c> /
/// <c>AppBarToggleButton</c>）的物化。
/// </summary>
/// <remarks>
/// <para>
/// 它们与菜单项同形：<b>是挂在命令条上的子部件</b>，不是独立站位的可视树节点。
/// <c>AppBarButton</c> 虽然继承 <c>ButtonBase</c>（因此"是" <c>UIElement</c>），
/// 但一个 <c>UIElement</c> 不能同时在命令集合里又在某个 <c>Panel</c> 下，
/// 所以它走不了协调器那条路（那条路的前提是"父子都是可视树节点"）。
/// </para>
/// <para>
/// <b>因此这里没有 <c>Update</c>，只有 <c>Unmount</c>。</b>命令组是整体重建的
/// （见 <c>CommandBarHandler.ApplyCommands</c>），每轮拿到的都是<b>新建</b>的
/// 原生项；于是"写受控值"与"写初始值"在这里是同一件事——都在刚 new 出来的
/// 那个实例上、且发生在订阅之后/之前有讲究（见
/// <see cref="CreateToggle"/>）。被换掉的旧实例要走
/// <see cref="Unmount"/>，否则它身上的回声登记会被静态表钉住。
/// </para>
/// </remarks>
internal static class AppBarCommands
{
    /// <summary>
    /// <c>AppBarToggleButton</c> 的回声闸。
    /// </summary>
    /// <remarks>
    /// 它<b>不是</b> <c>ElementHandler</c>（命令项没有协调器那条生命周期），
    /// 所以释放点不是重写的 <c>Unmount</c> 而是这里的
    /// <see cref="Unmount"/>——命令组重建前的那一次统一摘除。
    /// </remarks>
    private static readonly EchoGuard CheckEcho = new();

    /// <summary>把一个命令元素翻成真的 <c>ICommandBarElement</c>；不认识的返回 <c>null</c> 并留痕。</summary>
    public static WuControls.ICommandBarElement? Materialize(Element? element)
    {
        switch (element)
        {
            case AppBarButtonElement button:
                return CreateButton(button);

            case AppBarToggleButtonElement toggle:
                return CreateToggle(toggle);

            case AppBarSeparatorElement:
                return new WuControls.AppBarSeparator();

            case null:
                return null;

            default:
                ReactorApplication.Trace(
                    $"[reactor] 命令条只收 AppBarButton / AppBarToggleButton / AppBarSeparator，" +
                    $"收到 {element.GetType().Name}，已忽略");
                return null;
        }
    }

    /// <summary>
    /// 整组重建<b>之前</b>：把即将被丢掉的那些实例上的回声登记摘掉。
    /// </summary>
    /// <remarks>
    /// 命令项没有 <c>ElementHandler</c> 那条卸载路径（协调器不认识它们），
    /// 所以"谁负责 Forget"必须有个落点，否则 <c>EchoGuard</c> 的登记表会一直
    /// 攥着已经从命令组里换下来的旧按钮。这里就是那个落点：组是整体重建的，
    /// 那么"旧的一批要走了"这件事只在重建前这一刻成立一次——
    /// 与方法名 <c>Unmount</c> 对应的语义也就只有这一次。
    /// </remarks>
    public static void Unmount(IEnumerable<WuControls.ICommandBarElement> items)
    {
        foreach (var item in items)
        {
            if (item is WuControls.AppBarToggleButton native)
            {
                CheckEcho.Forget(native);
            }
        }
    }

    private static WuControls.AppBarButton CreateButton(AppBarButtonElement element)
    {
        var native = new WuControls.AppBarButton { Label = element.Label ?? string.Empty };

        if (element.Icon is { } icon && IconElements.From(icon) is { } nativeIcon)
        {
            native.Icon = nativeIcon;
        }

        if (element.IsEnabled is { } enabled)
        {
            native.IsEnabled = enabled;
        }

        if (element.OnClick is { } onClick)
        {
            // 与菜单项同一个理由：每次都挂在<b>新建</b>的原生项上，
            // 不存在"旧回调没解绑"。
            native.Click += (_, _) => onClick();
        }

        return native;
    }

    /// <summary>
    /// 开关按钮：受控 <c>IsChecked</c>，回执是 <c>Checked</c> / <c>Unchecked</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 顺序与 <c>ToggleButtonHandler</c> 一致：<b>先挂带闸的订阅，再写受控值</b>。
    /// 命令项虽然每轮都是新实例，"写下去会同步抛事件"这件事并没有变——
    /// 新实例也不认识这一发是自己写的，不挂闸就会把它当成用户点的。
    /// </para>
    /// <para>
    /// 与 <c>ToggleButtonHandler</c> 的差别只在"旧值从哪儿来"：那里是
    /// <c>control.IsChecked</c>（长期挂着的同一个控件），这里是
    /// <c>new</c> 出来的默认值——所以这里<b>没有</b> <c>PropWriter.Set</c>
    /// 那种逐字段 diff，只有"和默认值不同才写"。
    /// </para>
    /// </remarks>
    private static WuControls.AppBarToggleButton CreateToggle(AppBarToggleButtonElement element)
    {
        var native = new WuControls.AppBarToggleButton { Label = element.Label ?? string.Empty };

        if (element.Icon is { } icon && IconElements.From(icon) is { } nativeIcon)
        {
            native.Icon = nativeIcon;
        }

        if (element.IsEnabled is { } enabled)
        {
            native.IsEnabled = enabled;
        }

        if (element.OnIsCheckedChanged is { } callback)
        {
            native.Checked += (_, _) => Fire(native, callback, true);
            native.Unchecked += (_, _) => Fire(native, callback, false);
        }

        if (!element.IsChecked.HasValue || native.IsChecked == element.IsChecked.Value)
        {
            return native;
        }

        // 三态那个 null 只会抛 Indeterminate，而 Indeterminate 我们没有订阅，
        // 没有人来 Consume 它——所以只有"变成 bool"这一发需要登记与下发。
        if (element.IsChecked.Value is not { } solid)
        {
            native.IsChecked = null;
            return native;
        }

        CheckEcho.Expect(native, solid);
        native.IsChecked = solid;

        // 回调为空时 Checked / Unchecked 根本没订，没人领的登记要撤销。
        CheckEcho.CancelIfUnconsumed(native);
        return native;
    }

    /// <summary>
    /// 两个事件共用<b>一个</b> <c>Consume</c>：理由同 <c>ToggleButtonHandler.Fire</c>——
    /// 回声登记对每个受控站点只有一份，"勾上"与"取消"是同一份登记的两种取值。
    /// </summary>
    private static void Fire(
        WuControls.AppBarToggleButton native, Action<bool> callback, bool value)
    {
        if (CheckEcho.Consume(native, value))
        {
            return;
        }

        callback(value);
    }
}

/// <summary>
/// WinUI 2 的 <c>MenuBar</c>：横排的若干组，每组点开一个浮出菜单。
/// </summary>
/// <remarks>
/// 每一组（<c>MenuBarItem</c>）的 <c>Items</c> 收的是 <c>MenuFlyoutItemBase</c>——
/// 与 <c>MenuFlyout.Items</c> <b>同一个类型</b>，所以菜单项直接复用
/// <see cref="MenuItem"/> / <see cref="MenuSeparator"/>，物化也复用
/// <c>MenuFlyouts.Materialize</c>。于是"菜单项怎么写"只有一份写法，
/// 换个容器不用再学一遍。
/// </remarks>
internal sealed class MenuBarHandler : ElementHandler<MenuBarElement, MuxControls.MenuBar>
{
    protected override MuxControls.MenuBar Mount(Reconciler reconciler, MenuBarElement element)
    {
        var bar = new MuxControls.MenuBar();
        ApplyItems(bar, element.Items);
        return bar;
    }

    protected override void Update(
        Reconciler reconciler,
        MenuBarElement oldElement,
        MenuBarElement newElement,
        MuxControls.MenuBar control)
    {
        if (!SameItems(oldElement.Items, newElement.Items))
        {
            ApplyItems(control, newElement.Items);
        }
    }

    private static void ApplyItems(MuxControls.MenuBar bar, IReadOnlyList<MenuBarItemData>? items)
    {
        bar.Items.Clear();

        foreach (var data in items ?? Array.Empty<MenuBarItemData>())
        {
            var item = new MuxControls.MenuBarItem { Title = data.Title };

            foreach (var child in data.Items)
            {
                if (MenuFlyouts.Materialize(child) is { } native)
                {
                    item.Items.Add(native);
                }
            }

            bar.Items.Add(item);
        }
    }

    private static bool SameItems(
        IReadOnlyList<MenuBarItemData>? a,
        IReadOnlyList<MenuBarItemData>? b)
    {
        a ??= Array.Empty<MenuBarItemData>();
        b ??= Array.Empty<MenuBarItemData>();

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// WinUI 2 的 <c>CommandBarFlyout</c> 的物化。
/// </summary>
/// <remarks>
/// <b>它挂在别人身上，不在可视树里。</b><c>CommandBarFlyout</c> 继承
/// <c>FlyoutBase</c> → <c>DependencyObject</c>，与 <c>MenuFlyout</c> 同一条规矩：
/// 元素是描述，由 <see cref="FlyoutSlot"/> 就地物化，走不了协调器。
/// <para>
/// 两组命令用的就是 <see cref="AppBarCommands"/> 那一份——所以命令条怎么写，
/// 命令条浮层里就怎么写，不用再学一遍。重建前同样要先
/// <see cref="AppBarCommands.Unmount"/>，理由完全一致。
/// </para>
/// </remarks>
internal static class CommandBarFlyouts
{
    /// <summary>把浮层元素物化成真的 <c>CommandBarFlyout</c>。</summary>
    public static MuxControls.CommandBarFlyout Build(CommandBarFlyoutElement element)
    {
        var flyout = new MuxControls.CommandBarFlyout
        {
            AlwaysExpanded = element.AlwaysExpanded,
        };

        Apply(flyout.PrimaryCommands, element.Primary);
        Apply(flyout.SecondaryCommands, element.Secondary);
        return flyout;
    }

    /// <summary>
    /// 把浮层元素物化成真的 <c>TextCommandBarFlyout</c>（<see cref="Build"/> 的
    /// 文本版）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Build"/> 只差"造哪个原生类"：两组命令的填法、
    /// 重建前先 <see cref="AppBarCommands.Unmount"/> 的规矩、
    /// <c>AlwaysExpanded</c> 的含义<b>逐条相同</b>（它继承 <c>CommandBarFlyout</c>）。
    /// 所以这里不另写一份 <c>Apply</c>，复用同一个。
    /// <para>
    /// 剪贴板那几条命令<b>不在这里填</b>：官方控件挂在文本控件上之后自己补，
    /// 而且会跟着"有没有选中文本"改可用状态——手填一份就丢了这份联动。
    /// </para>
    /// </remarks>
    public static MuxControls.TextCommandBarFlyout BuildText(TextCommandBarFlyoutElement element)
    {
        var flyout = new MuxControls.TextCommandBarFlyout
        {
            AlwaysExpanded = element.AlwaysExpanded,
        };

        Apply(flyout.PrimaryCommands, element.Primary);
        Apply(flyout.SecondaryCommands, element.Secondary);
        return flyout;
    }

    private static void Apply(
        IList<WuControls.ICommandBarElement> target,
        IReadOnlyList<Element?>? items)
    {
        AppBarCommands.Unmount(target);
        target.Clear();

        foreach (var item in items ?? Array.Empty<Element?>())
        {
            if (AppBarCommands.Materialize(item) is { } native)
            {
                target.Add(native);
            }
        }
    }
}
