using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using MuxControls = Microsoft.UI.Xaml.Controls;
using WuControls = Windows.UI.Xaml.Controls;
using SelectionChangedEventArgs = Windows.UI.Xaml.Controls.SelectionChangedEventArgs;
using WuXaml = Windows.UI.Xaml;

namespace Reactor.Uwp.Internal;

/// <summary>
/// WinUI 2 的 TabView：页签条 + 一个内容区。
/// </summary>
/// <remarks>
/// <b>原生控件，不是自绘。</b><c>TabView</c> 自带的事一件都没替它做：键盘
/// <c>Ctrl+Tab</c> 顺序切换、键盘左右箭头切页签、页签拖拽重排、
/// <c>Tab</c> / <c>TabItem</c> 角色的自动化对等、深浅色资源，全部来自官方模板。
/// <para>
/// <b>为什么宿主是 <c>Grid</c> 而不是 <c>TabView</c> 本身。</b>WinUI 的 <c>TabView</c>
/// 继承 <c>Control</c>，<b>没有 <c>Content</c> 属性</b>——"选中哪个页签就显示哪份内容"
/// 这件差事官方交给 <c>TabViewItem.Content</c>。而把元素树塞进 <c>TabItems</c> 集合是
/// 走不通的，坑的形状与 <c>BreadcrumbBarHandler</c> 记下的那条一模一样：
/// <c>UIElement</c> 进集合时就已经拿了父，控件内部把它挂进自己的承载 panel 就是第二个父
/// （<c>0x800F1000 "Element is already the child of another element."</c>）。
/// 所以这里由 Reactor 侧把"页签条（TabView）+ 内容区（ContentControl）"纵向拼起来：
/// 每个页签的内容不再常驻一棵隐藏的子树，切换时同一份内容就地 patch。
/// 画廊里每份内容都不小（实时预览 / 整段源码），这个差别是实打实的。
/// </para>
/// </remarks>
internal sealed class TabViewHandler : ElementHandler<TabViewElement, WuControls.Grid>
{
    private static readonly WeakTable<MuxControls.TabView, (Action<int>? Selection, Action? Add)> Callbacks = new();

    /// <summary>宿主 → 页签条。</summary>
    private static readonly WeakTable<WuControls.Grid, MuxControls.TabView> Strips = new();

    /// <summary>宿主 → 内容区。键用宿主是为了让本地 announced 的清理点与挂载点成对。</summary>
    private static readonly WeakTable<WuControls.Grid, WuControls.ContentControl> ContentHosts = new();

    /// <summary><c>SelectedIndex</c> 受控：写回会同步触发 SelectionChanged，需要回声抑制。</summary>
    private static readonly EchoGuard SelectionEcho = new();

    /// <summary>最近一次下发的受控值：控件自己飘了要按它纠正回来。</summary>
    private static readonly WeakTable<MuxControls.TabView, SelectedTarget> Targets = new();

    /// <summary>正在整批替换页签：期间的选中变动作者是控件自己，不是用户。</summary>
    private static readonly WeakTable<MuxControls.TabView, bool> Rebuilding = new();

    private static readonly WeakTable<MuxControls.TabView, bool> RestorePending = new();

    /// <summary>挂上去的两个委托（SelectionChanged / AddTabButtonClick），Unmount 要拿它们解绑。语义与 <c>RadioButtonsHandler.Handlers</c> 一致，详见那边的注释。</summary>
    private static readonly WeakTable<
        MuxControls.TabView,
        (WuControls.SelectionChangedEventHandler? Selection,
         Windows.Foundation.TypedEventHandler<MuxControls.TabView, object>? Add)> Handlers = new();

    protected override WuControls.Grid Mount(Reconciler reconciler, TabViewElement element)
    {
        var strip = new MuxControls.TabView
        {
            IsAddTabButtonVisible = element.IsAddTabButtonVisible,
            TabWidthMode = element.TabWidthMode,
            CloseButtonOverlayMode = element.CloseButtonOverlayMode,
        };

        var contentHost = new WuControls.ContentControl
        {
            HorizontalAlignment = WuXaml.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = WuXaml.HorizontalAlignment.Stretch,
            VerticalAlignment = WuXaml.VerticalAlignment.Stretch,
            VerticalContentAlignment = WuXaml.VerticalAlignment.Stretch,
        };

        var host = new WuControls.Grid();
        host.RowDefinitions.Add(new WuControls.RowDefinition { Height = WuXaml.GridLength.Auto });
        host.RowDefinitions.Add(new WuControls.RowDefinition { Height = new WuXaml.GridLength(1, WuXaml.GridUnitType.Star) });

        WuControls.Grid.SetRow(strip, 0);
        WuControls.Grid.SetRow(contentHost, 1);
        host.Children.Add(strip);
        host.Children.Add(contentHost);

        Strips[host] = strip;
        ContentHosts[host] = contentHost;

        // 顺序不能反：先把带闸的事件处理器挂上，再写受控值——这一次受控写回发出的
        // SelectionChanged 必须被认成回声，而不是"页面一出现就回调了一次换页签"。
        Rebind(strip, element.OnSelectedIndexChanged, element.OnAddTabClick);
        ApplyTabs(reconciler, strip, element.Tabs);
        ApplySelectedIndex(strip, element.SelectedIndex);

        if (element.Content is { } content)
        {
            contentHost.Content = reconciler.Build(content);
        }

        // 与 NavigationView 同一个形状：TabView 的页签条由内部 repeater
        // （TabViewListView）渲染，挂载时写下去的 SelectedIndex 要等那一层就位才真正
        // 生效，期间补抛的那一发作者是我们。Loaded 之后按目标值再补发一次，
        // 否则"进页面就没有选中页签"。
        ReadyGate.Arm(strip, ctl =>
        {
            if (Targets.TryGetValue(ctl, out var target))
            {
                ApplySelectedIndex(ctl, target.Index);
            }
        });

        return host;
    }

    protected override void Update(
        Reconciler reconciler,
        TabViewElement oldElement,
        TabViewElement newElement,
        WuControls.Grid control)
    {
        if (Strips[control] is not { } strip)
        {
            return;
        }

        if (oldElement.IsAddTabButtonVisible != newElement.IsAddTabButtonVisible)
        {
            strip.IsAddTabButtonVisible = newElement.IsAddTabButtonVisible;
        }

        // 页签宽度 / 关闭按钮的冒出时机都是<b>外观策略</b>，不动选中：
        // 换宽度模式不会让当前下标变（页签还是那几个），换覆盖模式更是只影响
        // 那个按钮什么时候可见。与 IsAddTabButtonVisible 同族，登记为惰性。
        if (oldElement.TabWidthMode != newElement.TabWidthMode)
        {
            strip.TabWidthMode = newElement.TabWidthMode;
        }

        if (oldElement.CloseButtonOverlayMode != newElement.CloseButtonOverlayMode)
        {
            strip.CloseButtonOverlayMode = newElement.CloseButtonOverlayMode;
        }

        // 页签整批替换期间控件会自己重算选中（松散一点说：少了一个 tab，选中就不可能是
        // 原来那个下标），那一发事件的作者不是用户。与其他受控站点同形，这里开窗。
        if (!PropWriter.SequenceEqual(oldElement.Tabs, newElement.Tabs))
        {
            Rebuilding.Set(strip, true);
            try
            {
                ApplyTabs(reconciler, strip, newElement.Tabs);
            }
            finally
            {
                Rebuilding.Set(strip, false);
            }
        }

        ApplySelectedIndex(strip, newElement.SelectedIndex);
        Rebind(strip, newElement.OnSelectedIndexChanged, newElement.OnAddTabClick);

        if (ContentHosts[control] is { } contentHost)
        {
            reconciler.PatchSingleChild(contentHost, oldElement.Content, newElement.Content);
        }
    }

    protected override Element? SingleChildOf(TabViewElement element) => element.Content;

    protected override void Unmount(Reconciler reconciler, WuControls.Grid control)
    {
        if (Strips[control] is { } strip)
        {
            SelectionEcho.Forget(strip);
            ReadyGate.Disarm(strip);

            if (Handlers.TryGetValue(strip, out var attached))
            {
                if (attached.Selection is { } selection)
                {
                    strip.SelectionChanged -= selection;
                }

                if (attached.Add is { } add)
                {
                    strip.AddTabButtonClick -= add;
                }

                Handlers.Remove(strip);
            }

            Callbacks.Remove(strip);
            Targets.Remove(strip);
            Rebuilding.Remove(strip);
            RestorePending.Remove(strip);
            Strips.Remove(control);
        }

        ContentHosts.Remove(control);
    }

    /// <summary>把受控值落到页签条上。<b>没有值时要清掉登记</b>——否则控件已经不受控了。</summary>
    private static void ApplySelectedIndex(MuxControls.TabView strip, Optional<int> index)
    {
        if (!index.HasValue)
        {
            Targets.Remove(strip);
            return;
        }

        ApplySelectedIndex(strip, index.Value);
    }

    private static void ApplySelectedIndex(MuxControls.TabView strip, int index)
    {
        var count = strip.TabItems.Count;
        Targets.Set(strip, new SelectedTarget(index));

        if (!SelectionPolicy.ShouldApply(count, strip.SelectedIndex, index))
        {
            if (SelectionPolicy.IsOutOfRange(count, index))
            {
                ReactorLog.Gate(
                    $"TabView{CtlId.Tag(strip)} 受控值 {index} 越界（TabItems.Count={count}），不下发");
            }

            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 TabView{CtlId.Tag(strip)}: {strip.SelectedIndex} → {index}");

        // 未就绪时不登记：那一发要么根本不来（页签条的 repeater 还没加载），
        // 要么来了也被"未就绪"那道吞掉，Consume 不会被调用——登记等于埋一颗
        // 永不消费的雷（详见 SelectionGate.ShouldExpectEcho）。
        if (SelectionGate.ShouldExpectEcho(
                index,
                ReadyGate.IsReady(strip),
                Rebuilding.TryGetValue(strip, out var busy) && busy))
        {
            SelectionEcho.Expect(strip, index);
        }

        strip.SelectedIndex = index;

        // 写完回头看一眼：这一发的回声没人来领就撤销登记。
        // 留着它，等用户之后点到同一个下标，会被判成框架自己的回声吞掉。
        if (SelectionEcho.CancelIfUnconsumed(strip))
        {
            ReactorLog.Gate($"TabView{CtlId.Tag(strip)} 本次下发无回声 → 撤销登记");
        }
    }

    /// <summary>事件 → 用户回调的闸门，与本文件其它受控站点同构（详见各道判据的注释）。</summary>
    private static void Dispatch(
        MuxControls.TabView strip, SelectionChangedEventArgs args, Action<int>? callback)
    {
        var value = strip.SelectedIndex;
        var tag = $"TabView{CtlId.Tag(strip)}";

        var verdict = SelectionGate.Decide(
            SelectionArgs.SelectedSomething(args),
            ReadyGate.IsReady(strip),
            Rebuilding.TryGetValue(strip, out var busy) && busy);

        if (verdict == SelectionVerdict.Pass && SelectionEcho.Consume(strip, value))
        {
            verdict = SelectionVerdict.Echo;
        }

        if (SelectionGate.Suppress(verdict))
        {
            ReadyStats.Suppressed++;
            ReactorLog.Gate($"{tag} {SelectionGate.Reason(verdict)}，吞 {value}");

            if (SelectionGate.ShouldRestoreAfterSuppress(verdict))
            {
                ReactorLog.Gate($"{tag} 纠正回受控值（控件停在 {value}）");
                RestorePending[strip] = true;
                SelectionRestore.Schedule(
                    strip, RestorePending, Targets,
                    c => c.TabItems.Count, c => c.SelectedIndex, ApplySelectedIndex);
            }

            return;
        }

        SelectionRestore.Cancel(strip, RestorePending);

        ReactorLog.Pass($"{tag} → 用户回调 SelectedIndex={value}");
        callback?.Invoke(value);
    }

    private static void Rebind(
        MuxControls.TabView strip, Action<int>? selection, Action? add)
    {
        if (!Handlers.ContainsKey(strip))
        {
            Callbacks[strip] = (null, null);

            WuControls.SelectionChangedEventHandler selectionHandler = (s, args) =>
            {
                // 用订阅时那个引用（<c>strip</c>）查表，不用 <c>sender</c>。
                // 理由见 RadioButtonsHandler.Handlers 字段的注释。
                if (Callbacks.TryGetValue(strip, out var current))
                {
                    // 不管这一轮有没有人监听，四道判据与纠正都要跑：
                    // 它们兑现的是"受控"，不是"送达"。
                    Dispatch(strip, args, current.Selection);
                }
            };

            var addHandler =
                new Windows.Foundation.TypedEventHandler<
                    MuxControls.TabView,
                    object>((s, _) =>
            {
                if (Callbacks.TryGetValue(strip, out var current))
                {
                    current.Add?.Invoke();
                }
            });

            strip.SelectionChanged += selectionHandler;
            strip.AddTabButtonClick += addHandler;
            Handlers.Set(strip, (selectionHandler, addHandler));
        }

        Callbacks[strip] = (selection, add);
    }

    /// <summary>按位对齐页签：<b>能就地 patch 就 patch</b>，只有数量变了才增删。</summary>
    /// <remarks>
    /// 整体 <c>Clear() + 全量重建</c> 会让页签条丢焦点、丢焦点视觉、丢自动化对等的身份，
    /// 每次重渲染都来一遍就是"闪"。与 <c>SettingsExpanderHandler.ApplyItems</c>
    /// 同一个形状。
    /// </remarks>
    private static void ApplyTabs(
        Reconciler reconciler, MuxControls.TabView strip, IReadOnlyList<TabElement>? tabs)
    {
        var list = tabs ?? Array.Empty<TabElement>();

        for (var i = 0; i < list.Count; i++)
        {
            if (i < strip.TabItems.Count && strip.TabItems[i] is MuxControls.TabViewItem existing)
            {
                ApplyTab(reconciler, existing, list[i]);
                continue;
            }

            var fresh = new MuxControls.TabViewItem();
            ApplyTab(reconciler, fresh, list[i]);
            strip.TabItems.Add(fresh);
        }

        while (strip.TabItems.Count > list.Count)
        {
            strip.TabItems.RemoveAt(strip.TabItems.Count - 1);
        }
    }

    private static void ApplyTab(
        Reconciler reconciler, MuxControls.TabViewItem item, TabElement tab)
    {
        if (!Equals(item.Header as string, tab.Header))
        {
            item.Header = tab.Header;
        }

        if (item.IsClosable != tab.IsClosable)
        {
            item.IsClosable = tab.IsClosable;
        }

        ApplyIconSource(item, tab.HeaderIcon);
    }

    /// <summary>
    /// 页签图标。<c>TabViewItem.IconSource</c> 收的是 <c>IconSource</c>（不是
    /// <c>IconElement</c>），所以这里是"数据 → 数据"的翻译，不进可视树。
    /// </summary>
    /// <remarks>
    /// 图标照 <c>SettingsCardHandlerBase</c> 的做法当<b>内容槽</b>处理（能复用实例就
    /// 复用）：每轮渲染都换一个新的 <c>IconSource</c> 会让页签条的视觉状态重算 → 闪。
    /// </remarks>
    private static void ApplyIconSource(MuxControls.TabViewItem item, Element? icon)
    {
        switch (icon)
        {
            case FontIconElement font:
                if (item.IconSource is MuxControls.FontIconSource existingFont)
                {
                    if (existingFont.Glyph != font.Glyph)
                    {
                        existingFont.Glyph = font.Glyph;
                    }

                    return;
                }

                item.IconSource = new MuxControls.FontIconSource { Glyph = font.Glyph };
                return;

            case BitmapIconElement bitmap:
                if (PackUri.TryCreate(bitmap.UriSource) is not { } uri)
                {
                    item.IconSource = null;
                    return;
                }

                if (item.IconSource is MuxControls.BitmapIconSource existingBitmap)
                {
                    existingBitmap.UriSource = uri;
                    existingBitmap.ShowAsMonochrome = bitmap.ShowAsMonochrome;
                    return;
                }

                item.IconSource = new MuxControls.BitmapIconSource
                {
                    UriSource = uri,
                    ShowAsMonochrome = bitmap.ShowAsMonochrome,
                };
                return;

            default:
                if (item.IconSource is not null)
                {
                    item.IconSource = null;
                }

                return;
        }
    }
}
