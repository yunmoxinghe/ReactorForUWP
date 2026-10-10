using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace Reactor.Uwp.Internal;

/// <summary>
/// <c>Selector</c> 一族（<see cref="ListBox"/> / <see cref="FlipView"/>）的共用 handler 骨架。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么另起一个基类，而不是复用 <c>ItemsViewHandler</c>。</b>
/// 那一条的约束是 <c>where TControl : ListViewBase</c>，而 <c>ListBox</c> 与
/// <c>FlipView</c> 只是 <c>Selector</c>：没有 <c>Header</c>、没有
/// <c>IsItemClickEnabled</c>（<c>ItemClick</c> 事件本身就长在 <c>ListViewBase</c> 上）、
/// 选中模式用的是 <see cref="SelectionMode"/> 而不是 <c>ListViewSelectionMode</c>。
/// 把那条约束放宽成 <c>Selector</c> 就要把这三样抽成抽象成员——那等于**改一条
/// 已经验过、且被第十一道契约按行变异盯着的实现**。这里选择不动它：
/// 代价是选中那套判据多一份落点，收益是 ListView / GridView 那条路一行没动。
/// </para>
/// <para>
/// 除了上面那三样，剩下的部分与 <c>ItemsViewHandler</c> <b>同形</b>：受控
/// <c>SelectedIndex</c>、写回的回声抑制、改 items 期间的 <c>Rebuilding</c> 窗、
/// 以及那四道判据 + 纠正，逐项都从那里抄过来（判据本身是共用的静态类，
/// 抄的只是"接上"的那几行）。
/// </para>
/// <para>
/// <c>Selector</c> 没有 <c>RadioButtons</c> 那种模板闸门，所以判据一（未就绪）
/// 直接传 <c>true</c>——理由与 <c>ItemsViewHandler</c> 那份豁免登记完全相同，
/// 本类同样登记在 <c>VerdictWaived</c> 里。
/// </para>
/// </remarks>
internal abstract class SelectorHandler<TElement, TControl> : ElementHandler<TElement, TControl>
    where TElement : Element
    where TControl : Selector
{
    private static readonly WeakTable<TControl, CallbackBox> Callbacks = new();

    /// <summary>最近一次下发的受控值：万一控件自己飘了，纠正时要用。</summary>
    private static readonly WeakTable<TControl, SelectedTarget> Targets = new();

    /// <summary><c>SelectedIndex</c> 受控：写回会同步触发 SelectionChanged，需要回声抑制。</summary>
    private static readonly EchoGuard SelectionEcho = new();

    /// <summary>
    /// 正在改动 items（增删替）：期间的选中变动作者是 <c>Selector</c> 自己，不是用户。
    /// </summary>
    /// <remarks>
    /// 依据同 <c>ItemsViewHandler</c>：<c>Selector</c> 在集合增删时会
    /// <c>BeginChange → Unselect/Select → EndChange</c>，走的就是会抛
    /// <c>SelectionChanged</c> 的那条路。移除当前选中项时控件会自己把选中变成 -1
    /// 并抛事件——这一发不关用户的事。
    /// </remarks>
    private static readonly WeakTable<TControl, bool> Rebuilding = new();
    private static readonly WeakTable<TControl, bool> RestorePending = new();

    /// <summary>挂在控件上的 <c>SelectionChanged</c> 委托（Unmount 要拿它解绑）。语义与 <c>RadioButtonsHandler.Handlers</c> 一致，详见那边的注释。</summary>
    private static readonly WeakTable<TControl, SelectionChangedEventHandler?> Handlers = new();

    protected abstract IReadOnlyList<Element?> ItemsOf(TElement element);
    protected abstract Optional<int> SelectedIndexOf(TElement element);
    protected abstract Action<int>? SelectionCallbackOf(TElement element);

    /// <summary>
    /// 写选中模式（挂载期与更新期各调一次）。<c>FlipView</c> 没有这个属性，
    /// 默认什么都不做。
    /// </summary>
    /// <remarks>
    /// 更新期的调用点已经开好 <c>Rebuilding</c> 标记（见 <see cref="Update"/>），
    /// 子类只管把值写下去，不用自己开门——模式属性不在 <c>Selector</c> 基类上，
    /// 基类没法内联写它。
    /// </remarks>
    protected virtual void ApplySelectionMode(TControl control, TElement? oldElement, TElement newElement)
    {
    }

    protected void Initialize(Reconciler reconciler, TControl control, TElement element)
    {
        ApplySelectionMode(control, null, element);

        foreach (var item in ItemsOf(element))
        {
            if (item is null)
            {
                continue;
            }

            control.Items.Add(reconciler.Build(item));
        }

        // 顺序不能反：先把带闸的事件处理器挂上，再写 SelectedIndex——
        // 这一次受控写回发出的 SelectionChanged 必须被认成回声。
        Rebind(control, SelectionCallbackOf(element));
        ApplySelectedIndex(control, SelectedIndexOf(element));
    }

    protected override void Update(
        Reconciler reconciler,
        TElement oldElement,
        TElement newElement,
        TControl control)
    {
        // 改选中模式与改 items 是<b>同一类动作</b>：两者都会让控件自己重算选中并抛
        // SelectionChanged，那一发的作者是我们，不是用户。所以共用一段 Rebuilding
        // 标记——它本来就是"作者是我们这一段"的开门 / 关门。
        //
        // 这里<b>不用静默窗</b>（<c>SelectionEcho.Silence</c>），两个理由：一是窗在
        // 本次调用返回时就关了，而那一发事件未必已经抛完；二是这一段的写入落在子类
        // 的 <c>ApplySelectionMode</c> 里，窗罩不到那儿。持续标记没有这两个问题。
        Rebuilding.Set(control, true);
        try
        {
            ApplySelectionMode(control, oldElement, newElement);
            reconciler.PatchItems(control, ItemsOf(oldElement), ItemsOf(newElement));
        }
        finally
        {
            Rebuilding.Set(control, false);
        }

        ApplySelectedIndex(control, SelectedIndexOf(newElement));

        Rebind(control, SelectionCallbackOf(newElement));
    }

    protected override void Unmount(Reconciler reconciler, TControl control)
    {
        SelectionEcho.Forget(control);

        if (Handlers.TryGetValue(control, out var handler) && handler is { } attached)
        {
            control.SelectionChanged -= attached;
            Handlers.Remove(control);
        }

        Callbacks.Remove(control);
        Targets.Remove(control);
        Rebuilding.Remove(control);
        RestorePending.Remove(control);
    }

    /// <summary>把受控值落到控件上。<b>没有值时要清掉登记</b>——否则控件已经不受控了。</summary>
    private static void ApplySelectedIndex(TControl control, Optional<int> index)
    {
        if (!index.HasValue)
        {
            Targets.Remove(control);
            return;
        }

        ApplySelectedIndex(control, index.Value);
    }

    private static void ApplySelectedIndex(TControl control, int index)
    {
        Targets.Set(control, new SelectedTarget(index));

        if (!SelectionPolicy.ShouldApply(control.Items.Count, control.SelectedIndex, index))
        {
            if (SelectionPolicy.IsOutOfRange(control.Items.Count, index))
            {
                ReactorLog.Gate(
                    $"{Name(control)} 受控值 {index} 越界（Items.Count={control.Items.Count}），不下发");
            }

            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 {Name(control)}: {control.SelectedIndex} → {index}");

        if (SelectionGate.ShouldExpectEcho(
                index,
                isReady: true,
                Rebuilding.TryGetValue(control, out var busy) && busy))
        {
            SelectionEcho.Expect(control, index);
        }

        control.SelectedIndex = index;

        if (SelectionEcho.CancelIfUnconsumed(control))
        {
            ReactorLog.Gate($"{Name(control)} 本次下发无回声 → 撤销登记");
        }
    }

    /// <summary>
    /// 事件 → 用户回调的四道闸门，与 <c>ItemsViewHandler.Dispatch</c> 同构。
    /// </summary>
    /// <remarks>
    /// <b><c>callback is null</c> 不许在这一层短路。</b>纠正兑现的是"这个属性由 state
    /// 说了算"的承诺，与有没有人监听无关——<c>SelectionRestore.Schedule</c> 那一行
    /// 因此站在 <see cref="SelectionGate.ShouldRestoreAfterSuppress"/> 之后，
    /// 而不是在门口。
    /// </remarks>
    private static void Dispatch(
        TControl control, SelectionChangedEventArgs args, Action<int>? callback)
    {
        var value = control.SelectedIndex;
        var tag = Name(control);

        // 就绪那一道直接给 true：<c>Selector</c> 没有 <c>RadioButtons</c> 那种模板闸门，
        // "未就绪"在这里不是一个可问的问题（豁免登记见本类注释）。
        var verdict = SelectionGate.Decide(
            SelectionArgs.SelectedSomething(args),
            isReady: true,
            Rebuilding.TryGetValue(control, out var busy) && busy);

        if (verdict == SelectionVerdict.Pass && SelectionEcho.Consume(control, value))
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
                // 登记在 handler 自己这里：这张表按控件建，摘除也要落在本类的
                // Unmount 里（契约要求"登记—摘除"成对出现在同一类）。
                RestorePending[control] = true;

                SelectionRestore.Schedule(
                    control, RestorePending, Targets,
                    c => c.Items.Count, c => c.SelectedIndex, ApplySelectedIndex);
            }

            return;
        }

        SelectionRestore.Cancel(control, RestorePending);

        ReactorLog.Pass($"{tag} → 用户回调 SelectedIndex={value}");
        callback?.Invoke(value);
    }

    /// <summary>日志里的控件身份。<c>typeof</c> 的名字是稳定的，实例编号走 <see cref="CtlId"/>。</summary>
    private static string Name(TControl control) => $"{typeof(TControl).Name}{CtlId.Tag(control)}";

    private static void Rebind(TControl control, Action<int>? selection)
    {
        if (!Handlers.ContainsKey(control))
        {
            SelectionChangedEventHandler handler = (s, args) =>
            {
                // 用订阅时那个引用（<c>control</c>）查表，不用 <c>sender</c>。
                // 理由见 RadioButtonsHandler.Handlers 字段的注释。
                if (Callbacks.TryGetValue(control, out var box))
                {
                    // 不管这一轮有没有人监听，四道判据与纠正都要跑：
                    // 它们兑现的是"受控"，不是"送达"。
                    Dispatch(control, args, box.Selection);
                }
            };

            control.SelectionChanged += handler;
            Handlers.Set(control, handler);
        }

        // 整只盒子换掉，而不是"取出来改字段"：后者在源码扫描里会长成一处
        // <c>.Selection =</c> 的属性写入，而它根本不是控件属性。
        Callbacks.Set(control, new CallbackBox { Selection = selection });
    }

    /// <summary>
    /// 事件回调的存放盒。用引用类型而不是 <c>Action&lt;int&gt;?</c> 直接进弱表：
    /// 值是 <see langword="null"/> 时 <c>TryGetValue</c> 的语义会跟着含糊
    /// （"没有条目" 与 "条目是空回调" 分不开），判据那一层就少跑一次。
    /// </summary>
    private sealed class CallbackBox
    {
        public Action<int>? Selection { get; init; }
    }
}

/// <summary>列表框（对应 <see cref="ListBox"/>：只是 <c>Selector</c>，没有 Header 与 ItemClick）。</summary>
internal sealed class ListBoxHandler : SelectorHandler<ListBoxElement, ListBox>
{
    protected override ListBox Mount(Reconciler reconciler, ListBoxElement element)
    {
        var control = new ListBox();
        Initialize(reconciler, control, element);
        return control;
    }

    protected override IReadOnlyList<Element?> ItemsOf(ListBoxElement element) => element.Items;
    protected override Optional<int> SelectedIndexOf(ListBoxElement element) => element.SelectedIndex;
    protected override Action<int>? SelectionCallbackOf(ListBoxElement element) => element.OnSelectedIndexChanged;

    protected override void ApplySelectionMode(
        ListBox control, ListBoxElement? oldElement, ListBoxElement newElement)
    {
        // <paramref name="oldElement"/> 为 null = 挂载期，无条件写；
        // 否则只写"真的变了"的那一笔：改它会把选中态一起牵动，那一发
        // SelectionChanged 的作者是我们（标记已由基类开好）。
        if (oldElement is null || oldElement.SelectionMode != newElement.SelectionMode)
        {
            control.SelectionMode = newElement.SelectionMode;
        }
    }
}

/// <summary>
/// 翻页视图（对应 <see cref="FlipView"/>：一次一项，左右翻）。
/// </summary>
/// <remarks>
/// 它没有 <c>SelectionMode</c>（一次只能选一项是它自己定的），所以
/// <c>SelectionModeChanged</c> / <c>ApplySelectionMode</c> 两个钩子里什么都不做。
/// </remarks>
internal sealed class FlipViewHandler : SelectorHandler<FlipViewElement, FlipView>
{
    protected override FlipView Mount(Reconciler reconciler, FlipViewElement element)
    {
        var control = new FlipView();
        Initialize(reconciler, control, element);
        return control;
    }

    protected override IReadOnlyList<Element?> ItemsOf(FlipViewElement element) => element.Items;
    protected override Optional<int> SelectedIndexOf(FlipViewElement element) => element.SelectedIndex;
    protected override Action<int>? SelectionCallbackOf(FlipViewElement element) => element.OnSelectedIndexChanged;
}
