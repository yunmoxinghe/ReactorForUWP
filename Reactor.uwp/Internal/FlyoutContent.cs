using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 内容型 <c>Flyout</c> 的挂载 / patch / 卸载——本库第一处<b>在可视树之外</b>
/// 管一棵真子树的地方。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么它不能像 <c>MenuFlyout</c> 那样"每次重渲染整体重建"。</b>菜单项只有
/// 文本 / 图标 / 回调三种数据、没有跨帧状态，重建一遍不损失任何东西（见
/// <see cref="MenuFlyouts"/> 那段注释）。内容型浮出层<b>不成立</b>：它里面可以有
/// 一个正在输入、带着光标与选区的 <c>TextBox</c>，也可以有一个展开着的
/// <c>Expander</c>。整体重建会把这些状态每轮抹掉一次——那不是"记账不划算"，
/// 那是功能不对。所以它走协调器那三条路：<c>Build</c> 建、<c>Patch</c> 就地改、
/// <c>UnmountNative</c> 递归卸。
/// </para>
/// <para>
/// <b>"不在可视树里"是什么意思。</b><c>Flyout</c> 继承
/// <c>FlyoutBase</c> → <c>DependencyObject</c>，它的 <c>Content</c> 虽然是
/// <c>UIElement</c>，但<b>不在宿主控件的 <c>Children</c> / <c>Content</c> 里</b>——
/// 协调器递归卸载时顺着 <c>Panel.Children</c> 与 <c>ContentControl.Content</c> 走，
/// 走不到浮出层里去。所以"宿主卸载时把浮出层内容也卸掉"这件事必须有人显式做，
/// 否则子树里的控件会一直留在协调器的事件表里（这正是
/// <c>Reconciler.UnmountTree</c> 注释里写的那个"反复切页内存一直涨"）。
/// 三个入口都在 <see cref="Retire"/> 上：<c>ContextFlyout</c> 由
/// <c>Reconciler.UnmountNode</c> 统一收，按钮的 <c>Flyout</c> 槽位由各自的
/// handler 在 <c>Unmount</c> 里收。
/// </para>
/// <para>
/// <b>放在这个文件而不是 <c>Handlers.*.cs</c>，不是躲扫描。</b>它是"物化器"
/// （与 <see cref="MenuFlyouts"/> / <c>CommandBarFlyouts</c> 同类，只是那两个
/// 恰好写在 handler 文件里），而按控件建表的物化器本来就不都在
/// <c>Handlers.*.cs</c>——<c>InputApplier</c> 就是现成的先例。契约里那条
/// "每个按控件建表的类都在 Unmount 里摘掉"守的是 <b>handler 类</b>
/// （扫描口径就是 <c>Handlers.*.cs</c>）；这里的释放点不在
/// <c>override void Unmount</c> 上，而在 <see cref="Retire"/>，
/// 由上面那三个入口按各自的时机调用。
/// </para>
/// </remarks>
internal static class ContentFlyouts
{
    /// <summary>
    /// 浮出层 → 它当前内容的<b>元素</b>。patch 要它当"旧描述"，
    /// 卸载要它当"该递归到哪儿"的依据。
    /// </summary>
    /// <remarks>
    /// 弱键：浮出层一被换掉、不再被宿主的 <c>Flyout</c> / <c>ContextFlyout</c>
    /// 引用，这条记录就该跟着消失，不能反过来把它钉住。
    /// </remarks>
    private static readonly WeakTable<FlyoutBase, Element?> Contents = new();

    /// <summary>浮出层 → 挂在它 <c>Opened</c> 上的那个委托（为了能摘）。</summary>
    private static readonly WeakTable<FlyoutBase, EventHandler<object>> Openeds = new();

    /// <summary>浮出层 → 挂在它 <c>Closed</c> 上的那个委托（为了能摘）。</summary>
    private static readonly WeakTable<FlyoutBase, EventHandler<object>> Closeds = new();

    /// <summary>
    /// 把 <paramref name="element"/> 落到 <paramref name="current"/> 上：能就地
    /// patch 就 patch，不能就重建一份（旧的先 <see cref="Retire"/>）。
    /// </summary>
    /// <remarks>
    /// 判"能不能复用"用 <see cref="Reconciler.CanPatch"/>——与协调器里判定
    /// "这个旧控件还能不能改成新描述"用的是同一把尺子，不另立一套，
    /// 否则会出现"协调器说不能复用、这里却硬 patch"的分裂。
    /// </remarks>
    public static FlyoutBase Build(Reconciler reconciler, FlyoutElement element, FlyoutBase? current)
    {
        if (current is Flyout existing
            && existing.Content is UIElement native
            && Contents[existing] is { } oldContent
            && Reconciler.CanPatch(oldContent, element.Content))
        {
            ApplyProps(existing, element);
            reconciler.Patch(native, oldContent, element.Content!);
            Contents.Set(existing, element.Content);
            Rebind(existing, element.OnOpened, element.OnClosed);
            return existing;
        }

        Retire(reconciler, current);

        var flyout = new Flyout();
        ApplyProps(flyout, element);

        if (element.Content is { } content)
        {
            flyout.Content = reconciler.Build(content);
            Contents.Set(flyout, content);
        }

        Rebind(flyout, element.OnOpened, element.OnClosed);
        return flyout;
    }

    /// <summary>
    /// 收掉一个浮出层里的内容子树：递归卸载 + 解绑 + 摘表。
    /// </summary>
    /// <remarks>
    /// 对不是我们造的浮出层（比如 <c>MenuFlyout</c>，或者用户自己 <c>new</c> 好
    /// 塞进 <c>ContextFlyout</c> 修饰器的原生实例）是<b>空操作</b>：
    /// <see cref="Contents"/> 里没有它，就没有子树可收。
    /// </remarks>
    public static void Retire(Reconciler reconciler, FlyoutBase? flyout)
    {
        if (flyout is null)
        {
            return;
        }

        if (flyout is Flyout content && content.Content is UIElement native && Contents[content] is { } element)
        {
            reconciler.UnmountNative(native, element);
        }

        // 先摘事件再摘表：Retire 之后这个浮出层不再被我们管，
        // 留一个还会回调的委托在上面等于"卸载了却仍然活着"。
        Rebind(flyout, null, null);
        Contents.Remove(flyout);
    }

    /// <summary>
    /// 属性写入走"与控件当前值比"（<c>WriteIfChanged</c> 语义），不记上一轮的
    /// 元素描述：浮出层可能被换掉、也可能第一次建，没有可靠的"旧描述"可比对。
    /// 效果与拿旧元素 diff 相同——同值不写，也就不产生变化通知。
    /// </summary>
    private static void ApplyProps(Flyout flyout, FlyoutElement element)
    {
        PropWriter.Set(flyout.Placement, element.Placement, value => flyout.Placement = value);
        PropWriter.Set(flyout.ShowMode, element.ShowMode, value => flyout.ShowMode = value);
        PropWriter.Set(
            flyout.AreOpenCloseAnimationsEnabled,
            element.AreOpenCloseAnimationsEnabled,
            value => flyout.AreOpenCloseAnimationsEnabled = value);
    }

    /// <summary>
    /// 先摘后挂。
    /// </summary>
    /// <remarks>
    /// 这里<b>不能</b>用"每次挂在新实例上所以不用摘"那条理由：内容型浮出层是
    /// <b>就地 patch</b> 的（见类注释），同一个实例会活过很多轮渲染，
    /// 不摘就每轮多挂一个——开一次浮出层回调 N 次。
    /// </remarks>
    private static void Rebind(FlyoutBase flyout, Action? onOpened, Action? onClosed)
    {
        if (Openeds[flyout] is { } opened)
        {
            flyout.Opened -= opened;
            Openeds.Remove(flyout);
        }

        if (Closeds[flyout] is { } closed)
        {
            flyout.Closed -= closed;
            Closeds.Remove(flyout);
        }

        if (onOpened is { } open)
        {
            EventHandler<object> handler = (_, _) => open();
            flyout.Opened += handler;
            Openeds.Set(flyout, handler);
        }

        if (onClosed is { } close)
        {
            EventHandler<object> handler = (_, _) => close();
            flyout.Closed += handler;
            Closeds.Set(flyout, handler);
        }
    }
}
