using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using WuControls = Windows.UI.Xaml.Controls;
using WuXaml = Windows.UI.Xaml;
using SelectionChangedEventArgs = Windows.UI.Xaml.Controls.SelectionChangedEventArgs;

namespace Reactor.Uwp.Internal;

/// <summary>
/// UWP 原生的 <c>Pivot</c>：横向滑动切换的若干页，<b>每页各自持有内容</b>。
/// </summary>
/// <remarks>
/// <b>原生控件，不是自绘。</b>滑动手势、表头随手指滚动、只 realize 当前与相邻页
/// 这套卸载策略、<c>Pivot</c> 角色的自动化对等，全部来自官方模板。
/// <para>
/// <b>为什么内容挂在 <c>PivotItem</c> 上，而不是像 <c>TabViewHandler</c> 那样
/// 只在容器上留一份。</b>Pivot 的卖点就是"内容跟着手一起横着滑"，而这件事的
/// 前提是每一页的内容都在自己的 <c>PivotItem</c> 里、随 <c>PivotItem</c> 一起
/// 被平移。只留一份内容的折中做法（页签条 + 一个内容区）在滑动时会变成
/// "表头动了内容没动"，做出来的就不是 Pivot 了。
/// <c>PivotItem.Content</c> 是<b>内容槽</b>而不是集合，放进去的元素不会拿到
/// 第二个父（<c>TabViewHandler</c> 注释里记着那道坑的形状：<c>0x800F1000</c>）。
/// </para>
/// <para>
/// <b>卸载为什么要自己走一遍。</b>协调器的 <c>UnmountTree</c> 只替三种形状递归：
/// 组件包装、<c>Panel</c> + <c>ChildrenOf</c>、<c>SingleChildOf</c>。
/// 这里是"N 个 <c>PivotItem</c>、每个一份内容"，三种都不是——若不自己来，
/// 每页里的组件 cleanup 就永远不跑。所以 <see cref="Contents"/> 记住最近一次
/// 下发的内容描述，卸载时逐页 <c>UnmountNative</c>。
/// </para>
/// </remarks>
internal sealed class PivotHandler : ElementHandler<PivotElement, WuControls.Pivot>
{
    private static readonly WeakTable<WuControls.Pivot, Action<int>?> Callbacks = new();

    /// <summary><c>SelectedIndex</c> 受控：写回会同步触发 SelectionChanged，需要回声抑制。</summary>
    private static readonly EchoGuard SelectionEcho = new();

    /// <summary>最近一次下发的受控值：控件自己飘了要按它纠正回来。</summary>
    private static readonly WeakTable<WuControls.Pivot, SelectedTarget> Targets = new();

    /// <summary>正在增删页：期间的选中变动作者是控件自己，不是用户。</summary>
    private static readonly WeakTable<WuControls.Pivot, bool> Rebuilding = new();

    private static readonly WeakTable<WuControls.Pivot, bool> RestorePending = new();

    /// <summary>枢轴 → 最近一次下发的各页内容描述（卸载时逐页递归用）。</summary>
    private static readonly WeakTable<WuControls.Pivot, Element?[]> Contents = new();

    /// <summary>挂在控件上的 <c>SelectionChanged</c> 委托（Unmount 要拿它解绑）。语义与 <c>RadioButtonsHandler.Handlers</c> 一致，详见那边的注释。</summary>
    private static readonly WeakTable<WuControls.Pivot, WuControls.SelectionChangedEventHandler?> Handlers = new();

    protected override WuControls.Pivot Mount(Reconciler reconciler, PivotElement element)
    {
        var pivot = new WuControls.Pivot
        {
            Title = element.Title,
        };

        // 顺序不能反：先把带闸的事件处理器挂上，再写受控值——这一次受控写回发出的
        // SelectionChanged 必须被认成回声，而不是"页面一出现就回调了一次换页"。
        Rebind(pivot, element.OnSelectedIndexChanged);
        ApplyItems(reconciler, pivot, null, element.Items);
        ApplySelectedIndex(pivot, element.SelectedIndex);

        // 与 NavigationView / TabView 同一个形状：挂载时写下去的 SelectedIndex
        // 要等内部那一层就位才真正生效，期间补抛的那一发作者是我们。
        ReadyGate.Arm(pivot, ctl =>
        {
            if (Targets.TryGetValue(ctl, out var target))
            {
                ApplySelectedIndex(ctl, target.Index);
            }
        });

        return pivot;
    }

    protected override void Update(
        Reconciler reconciler,
        PivotElement oldElement,
        PivotElement newElement,
        WuControls.Pivot control)
    {
        if (oldElement.Title != newElement.Title)
        {
            control.Title = newElement.Title;
        }

        ApplyItems(reconciler, control, oldElement.Items, newElement.Items);
        ApplySelectedIndex(control, newElement.SelectedIndex);
        Rebind(control, newElement.OnSelectedIndexChanged);
    }

    protected override void Unmount(Reconciler reconciler, WuControls.Pivot pivot)
    {
        SelectionEcho.Forget(pivot);
        ReadyGate.Disarm(pivot);

        if (Handlers.TryGetValue(pivot, out var handler) && handler is { } attached)
        {
            pivot.SelectionChanged -= attached;
            Handlers.Remove(pivot);
        }

        Callbacks.Remove(pivot);
        Targets.Remove(pivot);
        Rebuilding.Remove(pivot);
        RestorePending.Remove(pivot);

        if (Contents.TryGetValue(pivot, out var contents))
        {
            for (var i = 0; i < pivot.Items.Count && i < contents.Length; i++)
            {
                if (pivot.Items[i] is WuControls.PivotItem item &&
                    item.Content is WuXaml.UIElement child)
                {
                    reconciler.UnmountNative(child, contents[i] ?? EmptyElement.Instance);
                }
            }

            Contents.Remove(pivot);
        }
    }

    /// <summary>
    /// 按位对齐各页：<b>能就地 patch 就 patch</b>，只有数量变了才增删。
    /// </summary>
    /// <remarks>
    /// 整体 <c>Clear() + 全量重建</c> 会丢掉 Pivot 自己的滚动位置与手势状态，
    /// 每次重渲染来一遍就是"闪"。与 <c>TabViewHandler.ApplyTabs</c> 同一个形状。
    /// </remarks>
    private static void ApplyItems(
        Reconciler reconciler,
        WuControls.Pivot pivot,
        IReadOnlyList<PivotItemElement>? oldItems,
        IReadOnlyList<PivotItemElement>? newItems)
    {
        var list = newItems ?? Array.Empty<PivotItemElement>();
        var old = oldItems ?? Array.Empty<PivotItemElement>();

        // 第一段：就地 patch 共有的那些页。页数不动 → 控件不会重算选中，
        // 这一段的写入作者是我们，不需要开窗。
        var shared = Math.Min(pivot.Items.Count, list.Count);

        for (var i = 0; i < shared; i++)
        {
            if (pivot.Items[i] is WuControls.PivotItem existing)
            {
                ApplyItem(reconciler, existing, i < old.Count ? old[i] : null, list[i]);
            }
        }

        // 第二段：增删页。页数变了 → 控件会自己重算选中（少了一页，选中就不可能
        // 是原来那个下标），那一发的作者不是用户 —— 与其他受控站点同形，这里开窗。
        //
        // 窗刻意只罩这一段：patch 已有页不会碰选中，罩进去等于把静默窗开得比
        // 需要的大，用户在这期间的真实动作也被一起吞掉。
        if (pivot.Items.Count != list.Count)
        {
            Rebuilding.Set(pivot, true);

            try
            {
                RemoveExtraPages(reconciler, pivot, list.Count, old);
                AppendMissingPages(reconciler, pivot, list, shared);
            }
            finally
            {
                Rebuilding.Set(pivot, false);
            }
        }

        Contents.Set(pivot, list.Select(item => item.Content).ToArray());
    }

    /// <summary>删掉多出来的页。<b>移除之前先把这一页的内容树卸掉</b>：组件 cleanup 不跑，订阅与定时任务就跟着控件一起泄漏。</summary>
    private static void RemoveExtraPages(
        Reconciler reconciler,
        WuControls.Pivot pivot,
        int target,
        IReadOnlyList<PivotItemElement> old)
    {
        while (pivot.Items.Count > target)
        {
            var index = pivot.Items.Count - 1;

            if (index < old.Count &&
                pivot.Items[index] is WuControls.PivotItem doomed &&
                doomed.Content is WuXaml.UIElement child)
            {
                reconciler.UnmountNative(child, old[index].Content ?? EmptyElement.Instance);
            }

            pivot.Items.RemoveAt(index);
        }
    }

    /// <summary>补上新增的页。<paramref name="first"/> 之后才算"新增"——前面的已在第一段 patch 过。</summary>
    private static void AppendMissingPages(
        Reconciler reconciler,
        WuControls.Pivot pivot,
        IReadOnlyList<PivotItemElement> list,
        int first)
    {
        for (var i = first; i < list.Count; i++)
        {
            var fresh = new WuControls.PivotItem();
            ApplyItem(reconciler, fresh, null, list[i]);
            pivot.Items.Add(fresh);
        }
    }

    private static void ApplyItem(
        Reconciler reconciler,
        WuControls.PivotItem item,
        PivotItemElement? old,
        PivotItemElement next)
    {
        if (!Equals(item.Header as string, next.Header))
        {
            item.Header = next.Header;
        }

        // PivotItem 继承 ContentControl，内容槽的 patch 走协调器那条通用路径。
        reconciler.PatchSingleChild(item, old?.Content, next.Content);
    }

    /// <summary>把受控值落到枢轴上。<b>没有值时要清掉登记</b>——否则控件已经不受控了。</summary>
    private static void ApplySelectedIndex(WuControls.Pivot pivot, Optional<int> index)
    {
        if (!index.HasValue)
        {
            Targets.Remove(pivot);
            return;
        }

        ApplySelectedIndex(pivot, index.Value);
    }

    private static void ApplySelectedIndex(WuControls.Pivot pivot, int index)
    {
        var count = pivot.Items.Count;
        Targets.Set(pivot, new SelectedTarget(index));

        if (!SelectionPolicy.ShouldApply(count, pivot.SelectedIndex, index))
        {
            if (SelectionPolicy.IsOutOfRange(count, index))
            {
                ReactorLog.Gate(
                    $"Pivot{CtlId.Tag(pivot)} 受控值 {index} 越界（Items.Count={count}），不下发");
            }

            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 Pivot{CtlId.Tag(pivot)}: {pivot.SelectedIndex} → {index}");

        // 未就绪时不登记：那一发要么根本不来，要么来了也被"未就绪"那道吞掉，
        // Consume 不会被调用——登记等于埋一颗永不消费的雷
        // （详见 SelectionGate.ShouldExpectEcho）。
        if (SelectionGate.ShouldExpectEcho(
                index,
                ReadyGate.IsReady(pivot),
                Rebuilding.TryGetValue(pivot, out var busy) && busy))
        {
            SelectionEcho.Expect(pivot, index);
        }

        pivot.SelectedIndex = index;

        // 写完回头看一眼：这一发的回声没人来领就撤销登记。
        // 留着它，等用户之后点到同一个下标，会被判成框架自己的回声吞掉。
        if (SelectionEcho.CancelIfUnconsumed(pivot))
        {
            ReactorLog.Gate($"Pivot{CtlId.Tag(pivot)} 本次下发无回声 → 撤销登记");
        }
    }

    /// <summary>事件 → 用户回调的闸门，与 <c>TabViewHandler.Dispatch</c> 同构（各道判据见那里的注释）。</summary>
    private static void Dispatch(
        WuControls.Pivot pivot, SelectionChangedEventArgs args, Action<int>? callback)
    {
        var value = pivot.SelectedIndex;
        var tag = $"Pivot{CtlId.Tag(pivot)}";

        var verdict = SelectionGate.Decide(
            SelectionArgs.SelectedSomething(args),
            ReadyGate.IsReady(pivot),
            Rebuilding.TryGetValue(pivot, out var busy) && busy);

        if (verdict == SelectionVerdict.Pass && SelectionEcho.Consume(pivot, value))
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
                RestorePending[pivot] = true;
                SelectionRestore.Schedule(
                    pivot, RestorePending, Targets,
                    c => c.Items.Count, c => c.SelectedIndex, ApplySelectedIndex);
            }

            return;
        }

        SelectionRestore.Cancel(pivot, RestorePending);

        ReactorLog.Pass($"{tag} → 用户回调 SelectedIndex={value}");
        callback?.Invoke(value);
    }

    private static void Rebind(WuControls.Pivot pivot, Action<int>? selection)
    {
        if (!Handlers.ContainsKey(pivot))
        {
            WuControls.SelectionChangedEventHandler handler = (s, args) =>
            {
                // 用订阅时那个引用（<c>pivot</c>）查表，不用 <c>sender</c>。
                // 理由见 RadioButtonsHandler.Handlers 字段的注释。
                if (Callbacks.TryGetValue(pivot, out var current))
                {
                    // 不管这一轮有没有人监听，四道判据与纠正都要跑：
                    // 它们兑现的是"受控"，不是"送达"。
                    Dispatch(pivot, args, current);
                }
            };

            pivot.SelectionChanged += handler;
            Handlers.Set(pivot, handler);
        }

        Callbacks[pivot] = selection;
    }
}
