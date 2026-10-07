using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 提示气泡的挂载 / patch / 卸载——本库第二处<b>在可视树之外</b>管一棵真子树的地方
/// （第一处是 <see cref="ContentFlyouts"/>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么它要单独一份记账。</b>气泡挂在宿主的附加属性
/// <c>ToolTipService.ToolTip</c> 上，<b>不在</b>宿主的 <c>Children</c> /
/// <c>Content</c> 里——协调器递归卸载时顺着可视树走，走不进去。宿主一卸载，
/// 气泡里那棵子树就会一直留在协调器的事件表里（这正是
/// <c>Reconciler.UnmountTree</c> 注释里写的那个"反复切页内存一直涨"）。
/// 释放点在 <see cref="Retire"/>，由 <c>Reconciler.UnmountNode</c> 统一收。
/// </para>
/// <para>
/// <b>为什么不"每次重渲染整体重建"。</b>与 <see cref="MenuFlyouts"/>（菜单项只有
/// 三种数据、没有跨帧状态）不同，气泡的内容可以是<b>任意子树</b>：里面可以有一个
/// 展开着的 <c>Expander</c>、一个正在输入的 <c>TextBox</c>。整体重建会把这些状态
/// 每轮抹掉一次。所以走协调器那三条路：<c>Build</c> 建、<c>Patch</c> 就地改、
/// <c>UnmountNative</c> 递归卸——与 <see cref="ContentFlyouts"/> 同一把尺子。
/// </para>
/// <para>
/// <b>"我们造的气泡"与"别人塞进来的"怎么区分。</b><see cref="Tips"/> 只认我们自己
/// 造的那一个：<see cref="Retire"/> 只在"当前挂着的正是它"时才把附加属性擦回
/// <c>null</c>。用户自己 <c>new</c> 好塞进 <c>.ToolTip(...)</c> 的原生实例不在表里，
/// 因此不会被我们擦掉。
/// </para>
/// </remarks>
internal static class ToolTips
{
    /// <summary>宿主 → 当前气泡内容的<b>元素</b>（patch 要它当"旧描述"，卸载要它当"递归到哪儿"）。</summary>
    private static readonly WeakTable<UIElement, Element?> Contents = new();

    /// <summary>宿主 → 我们造的那个气泡。</summary>
    private static readonly WeakTable<UIElement, ToolTip> Tips = new();

    /// <summary>气泡 → 挂在它 <c>Opened</c> 上的那个委托（为了能摘）。</summary>
    private static readonly WeakTable<ToolTip, RoutedEventHandler> Openeds = new();

    /// <summary>气泡 → 挂在它 <c>Closed</c> 上的那个委托（为了能摘）。</summary>
    private static readonly WeakTable<ToolTip, RoutedEventHandler> Closeds = new();

    /// <summary>
    /// 把 <paramref name="mods"/> 里的提示气泡落到 <paramref name="native"/> 上：
    /// 能就地 patch 就 patch，不能就重建一份（旧的先 <see cref="Retire"/>）。
    /// </summary>
    public static void Apply(Reconciler reconciler, UIElement native, ElementModifiers mods)
    {
        // ── 纯文本那条路 ────────────────────────────────────────
        //
        // 官方 XAML 的特性语法 ToolTipService.ToolTip="…"：值就是字符串本身，
        // 没有子树可管。所以这里只要"内容型那份先退场"，再把字符串写上去。
        if (mods.ToolTipContent is not { } content)
        {
            Retire(reconciler, native);
            WriteText(native, mods.ToolTip);
            return;
        }

        // ── 内容型那条路 ────────────────────────────────────────
        var spec = content as ToolTipElement;
        var inner = spec is null ? content : spec.Content;
        var existing = Tips[native];
        var oldContent = Contents[native];

        // 判"能不能复用"用 Reconciler.CanPatch —— 与协调器里判定
        // "这个旧控件还能不能改成新描述"用的是同一把尺子，不另立一套。
        var reusable = existing is not null &&
            (inner is null
                ? oldContent is null
                : oldContent is not null && Reconciler.CanPatch(oldContent, inner));

        if (reusable && existing is { } tip)
        {
            if (spec is not null)
            {
                ApplyProps(tip, spec);
            }

            if (inner is not null && tip.Content is UIElement child)
            {
                reconciler.Patch(child, oldContent!, inner);
                Contents.Set(native, inner);
            }
            else if (spec?.Text is { } text && !Equals(tip.Content, text))
            {
                tip.Content = text;
            }

            Rebind(tip, spec?.OnOpened, spec?.OnClosed);
            return;
        }

        Retire(reconciler, native);

        var built = new ToolTip();
        if (spec is not null)
        {
            ApplyProps(built, spec);
        }

        if (inner is { } subtree)
        {
            built.Content = reconciler.Build(subtree);
            Contents.Set(native, subtree);
        }
        else if (spec?.Text is { } onlyText)
        {
            built.Content = onlyText;
        }

        Rebind(built, spec?.OnOpened, spec?.OnClosed);
        Tips.Set(native, built);
        ToolTipService.SetToolTip(native, built);
    }

    /// <summary>
    /// 收掉一个宿主身上的气泡：递归卸载内容子树 + 解绑 + 摘表。
    /// </summary>
    /// <remarks>
    /// 对不是我们造的气泡是<b>空操作</b>：<see cref="Tips"/> 里没有它，
    /// 就没有子树可收、也没有附加属性要擦。
    /// </remarks>
    public static void Retire(Reconciler reconciler, UIElement native)
    {
        if (Tips[native] is not { } tip)
        {
            return;
        }

        if (Contents[native] is { } element && tip.Content is UIElement child)
        {
            reconciler.UnmountNative(child, element);
        }

        // 先摘事件再摘表：Retire 之后这个气泡不再被我们管，
        // 留一个还会回调的委托在上面等于"卸载了却仍然活着"。
        Rebind(tip, null, null);

        if (ReferenceEquals(ToolTipService.GetToolTip(native), tip))
        {
            ToolTipService.SetToolTip(native, null);
        }

        Tips.Remove(native);
        Contents.Remove(native);
    }

    // ── 小工具 ──────────────────────────────────────────────────

    /// <summary>
    /// 纯文本的 diff-and-write。
    /// </summary>
    /// <remarks>
    /// <b>不给值就是不动它</b>（不擦回 <c>null</c>）——这是 <c>ElementModifiers</c>
    /// 那一整族的既有约定（<c>WriteRefIfChanged</c> 对 null 直接返回），
    /// 本轮不单独改它。想让提示消失，给一个空串而不是 <c>null</c>。
    /// </remarks>
    private static void WriteText(UIElement native, string? text)
    {
        if (text is null || Equals(ToolTipService.GetToolTip(native), text))
        {
            return;
        }

        ToolTipService.SetToolTip(native, text);
    }

    /// <summary>
    /// 属性写入走"与控件当前值比"（<c>PropWriter.Set</c> 语义），不记上一轮的元素
    /// 描述：气泡可能被换掉、也可能第一次建，没有可靠的"旧描述"可比对。
    /// </summary>
    private static void ApplyProps(ToolTip tip, ToolTipElement spec)
    {
        PropWriter.Set(tip.Placement, spec.Placement, value => tip.Placement = value);

        if (spec.HorizontalOffset is { } dx)
        {
            PropWriter.Set(tip.HorizontalOffset, dx, value => tip.HorizontalOffset = value);
        }

        if (spec.VerticalOffset is { } dy)
        {
            PropWriter.Set(tip.VerticalOffset, dy, value => tip.VerticalOffset = value);
        }
    }

    /// <summary>
    /// 先摘后挂。
    /// </summary>
    /// <remarks>
    /// 这里<b>不能</b>用"每次挂在新实例上所以不用摘"那条理由：内容型气泡是
    /// <b>就地 patch</b> 的（见类注释），同一个实例会活过很多轮渲染，
    /// 不摘就每轮多挂一个——弹一次气泡回调 N 次。
    /// </remarks>
    private static void Rebind(ToolTip tip, Action? onOpened, Action? onClosed)
    {
        if (Openeds[tip] is { } opened)
        {
            tip.Opened -= opened;
            Openeds.Remove(tip);
        }

        if (Closeds[tip] is { } closed)
        {
            tip.Closed -= closed;
            Closeds.Remove(tip);
        }

        if (onOpened is { } open)
        {
            RoutedEventHandler handler = (_, _) => open();
            tip.Opened += handler;
            Openeds.Set(tip, handler);
        }

        if (onClosed is { } close)
        {
            RoutedEventHandler handler = (_, _) => close();
            tip.Closed += handler;
            Closeds.Set(tip, handler);
        }
    }
}
