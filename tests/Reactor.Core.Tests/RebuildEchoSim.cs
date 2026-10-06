using System;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 「重建 items 之后<b>补发受控值</b>，那一发漏在抑制区间外面」的行为模型。
/// </summary>
/// <remarks>
/// <para>
/// 真身：<c>NavigationView</c> 的 <c>MenuItems</c> 被整批替换（<c>ApplyMenuItems</c>
/// 是 <c>Clear()</c> 之后重新 <c>Add</c>）。第 19 节把它从「只罩 <c>Clear + Add</c>
/// 的静默窗」改成「罩到补发结束的持续标记」，本模型就是那一笔的<b>两半</b>：
/// <list type="bullet">
///   <item><b>漏假回调</b>——补发那一发的作者是我们，却跑进了用户回调；</item>
///   <item><b>丢选中</b>——重建把选中清空后不再补发，控件的选中值就停在 -1。</item>
/// </list>
/// </para>
/// <para>
/// <b>为什么 <c>Expect</c> 兜不住补发那一发。</b>真代码在
/// <c>ApplySelectedItem</c> 里是照 <c>SelectionGate.ShouldExpectEcho(index, isReady,
/// isRebuilding)</c> 决定要不要登记的，而重建期间第三问为真 → <b>不登记</b>
/// （重建时的值不可信，登记了反而会吞掉之后一次真实用户操作）。于是补发那一发
/// 走到 <c>Consume</c> 时无登记可匹配，只能靠<b>抑制区间</b>拦。
/// 旧代码的区间只罩到 <c>Clear + Add</c> 为止，补发站在区间外 —— 这就是漏的那一半。
/// </para>
/// <para>
/// 用的都是 Link 进来的<b>真家伙</b>：<c>EchoGuard</c>（含 <c>Expect</c> /
/// <c>Consume</c> / <c>CancelIfUnconsumed</c>）、<c>SelectionGate.Decide</c>、
/// <c>SelectionGate.ShouldExpectEcho</c>、<c>SelectionPolicy.ShouldApply</c>、
/// <c>WeakTable</c>（当 handler 里那个 <c>Rebuilding</c> 标记用）。
/// 模型不复制判据，只复制<b>次序</b>。
/// </para>
/// </remarks>
internal sealed class RebuildEchoSim
{
    /// <summary>开关 A：<c>Clear + Add</c> 期间开抑制（旧代码就有这一半）。</summary>
    public bool SuppressDuringRebuild = true;

    /// <summary>
    /// 开关 B：<b>补发受控值</b>期间也开着（第 19 节补的就是这一件）。
    /// 关掉它应当只放出「漏假回调」这一类，不该动「丢选中」那一类。
    /// </summary>
    public bool SuppressDuringReapply = true;

    /// <summary>
    /// 开关 C：重建之后按受控值<b>补发</b>。
    /// 关掉它应当只放出「丢选中」那一类，不该动「漏假回调」那一类。
    /// </summary>
    public bool ReapplyAfterRebuild = true;

    /// <summary>无关对照：<c>Clear</c> 真的会把选中容器带走（选中塌成 -1）。</summary>
    public bool ClearDropsSelection = true;

    /// <summary>
    /// 补发之后控件<b>真的停在写入的那个值</b>。关掉它 = 控件自己又收敛成了别的值。
    /// </summary>
    /// <remarks>
    /// 这正是 <c>Expect</c> 装不下的那一类（与 <c>SiblingWriteSim</c> 的
    /// <c>SiblingSideEffect</c> 同构）：<b>收敛成什么只有控件知道</b>，事先猜不到，
    /// 于是登记的值匹配不上，<c>Consume</c> 拦不住 —— 只能靠区间。
    /// 真身是 <c>ApplyMenuItems</c> 之后 repeater 重算选中。
    /// </remarks>
    public bool ReapplyConverges = true;

    /// <summary>
    /// 形状开关：控件把「清空选中」那一发<b>延后到下一帧</b>才抛。
    /// </summary>
    /// <remarks>
    /// 对应 <c>NavigationView.cpp</c> 里那条早退的注释原文：
    /// <c>Template has not been applied yet. SelectionModel's selectedIndex state will
    /// get properly updated after the repeater finishes loading.</c>
    /// 也就是说：<b>模板没套好时</b>，那一发不是同步的，而是排在 repeater 加载完之后。
    /// </remarks>
    public bool DefersToNextFrame;

    /// <summary>
    /// 持续标记是不是在 <c>Rebuild</c> 返回那一刻就关（真代码 <c>finally</c> 的写法）。
    /// 关掉它 = 标记一直开到下一帧派发完。
    /// </summary>
    public bool CloseMarkerOnReturn = true;

    /// <summary>
    /// 挂载期写的受控值，那一发<b>延后到 repeater 加载完</b>才抛。
    /// </summary>
    /// <remarks>
    /// 源码依据（<c>NavigationView.cpp</c> 的早退注释逐字核对过）：模板没套好时
    /// <c>OnSelectionModelSelectionChanged</c> 直接 return，
    /// <c>SelectionModel's selectedIndex state will get properly updated after the
    /// repeater finishes loading</c>。也就是说那一发不是同步的，
    /// 而且它回来时选中的是<b>实项</b>（不是取消选中）——判据零拦不住它。
    /// </remarks>
    public bool MountDefersRestore;

    /// <summary>
    /// 事件入口问不问「控件就绪了没」（<c>ReadyGate.IsReady</c>）。
    /// </summary>
    /// <remarks>
    /// repeater 是控件模板子树的一部分，<b>子先于父</b>——repeater 的
    /// <c>Loaded</c> 早于控件自己的 <c>Loaded</c>。所以那一发回来时
    /// 控件还没就绪，这一问正好问得到它。
    /// </remarks>
    public bool GateOnReady;

    /// <summary>不是用户动的，却被当成用户输入回调出去的次数。</summary>
    public int Spurious { get; private set; }

    /// <summary>一轮渲染结束后，控件选中值与声明值<b>不一致</b>的帧数（选中丢了）。</summary>
    public int Lost { get; private set; }

    /// <summary>控件自己抛出的事件总数（证明模型真的在动，不是空转）。</summary>
    public int Raised { get; private set; }

    /// <summary>被 <c>Expect</c> / <c>Consume</c> 配对认成回声吞掉的次数。</summary>
    public int Echoed { get; private set; }

    /// <summary>被抑制区间（<c>SelectionGate.Decide</c> 的重建那一问）挡下的次数。</summary>
    public int Silenced { get; private set; }

    /// <summary>延后到下一帧才抛出来的那一发的次数。</summary>
    public int Deferred { get; private set; }

    /// <summary>真实用户操作被回调出去的次数（修法不该动它）。</summary>
    public int UserCallbacks { get; private set; }

    /// <summary>控件当前的选中下标。</summary>
    public int Selected => _selected;

    /// <summary>state（声明值）当前是多少。</summary>
    public int State => _state;

    private readonly EchoGuard _echo = new();
    private readonly WeakTable<object, bool> _rebuilding = new();
    private readonly object _control = new();

    private int _count;
    private int _selected = -1;
    private int _state = -1;
    private bool _mounting;
    private bool _ready;

    /// <summary>排到下一帧的那一发。<c>-2</c> = 没有排队。</summary>
    private int _deferred = -2;

    private Action<int>? _callback;

    /// <summary>挂载：订阅先挂上，再下发受控值（真 handler 就是这个次序）。</summary>
    public void Mount(int count, int index, Action<int>? callback)
    {
        _mounting = true;
        _count = count;
        _selected = -1;
        _state = index;
        _callback = callback;
        _ready = false;
        Apply(index, MountDefersRestore);

        // 挂载期抛的事件不算假回调：那时候用户还没机会动它
        // （真代码 Mount 里也没有抑制，靠的是"此时没人听"）。
        _mounting = false;
    }

    /// <summary>
    /// 一轮重建：<c>items</c> 数量变了（= 整批替换），声明的选中值没变。
    /// </summary>
    public void Rebuild(int count, int index, Action<int>? callback)
    {
        _state = index;
        _callback = callback; // 真代码 Rebind 在 Update 最后，这里同理

        // —— 区间一：Clear + Add（旧代码的静默窗就到这里为止）——
        if (SuppressDuringRebuild)
        {
            _rebuilding.Set(_control, true);
        }

        _count = count;
        MoveSelection(ClearDropsSelection ? -1 : _selected, DefersToNextFrame);

        if (SuppressDuringRebuild && !SuppressDuringReapply)
        {
            // 旧代码的形状：窗在补发之前就关了。
            _rebuilding.Set(_control, false);
        }

        // —— 区间二：补发受控值（第 19 节把它挪进区间里）——
        if (ReapplyAfterRebuild)
        {
            if (SuppressDuringReapply && !SuppressDuringRebuild)
            {
                _rebuilding.Set(_control, true);
            }

            Apply(_state);

            if (!ReapplyConverges && _state > 0)
            {
                // repeater 重算：选中被收敛成别的值，这一发同样不是用户动的。
                MoveSelection(0);
            }
        }

        if (CloseMarkerOnReturn)
        {
            _rebuilding.Set(_control, false);
        }

        Score();
    }

    /// <summary>帧推进：repeater 加载完 → 派发排队那一发 → 控件 Loaded（就绪）。</summary>
    public void Tick()
    {
        if (_deferred != -2)
        {
            var value = _deferred;
            _deferred = -2;
            Deferred++;

            Raised++;
            Dispatch(value);
        }

        // 次序不能反：repeater 是模板子树的一部分，子先于父 ——
        // repeater 的 Loaded 早于控件自己的 Loaded，所以那一发回来时仍未就绪。
        _ready = true;

        // 「一直开到帧末」的那一档在这一刻关门。
        _rebuilding.Set(_control, false);
        Score();
    }

    /// <summary>
    /// 用户点了一下：这一发<b>必须</b>回调出去，不算假回调。
    /// </summary>
    /// <returns>是否真的回调了（点了当前已选中项时控件不发事件，回调自然也不跑）。</returns>
    public bool UserClick(int index)
    {
        if (index < 0 || index >= _count)
        {
            return false;
        }

        var before = UserCallbacks;
        MoveSelection(index, defer: false, fromUser: true);

        // 用户操作 → setState，声明值跟着走（这就是"漏出去的假回调"会破坏的东西）。
        _state = index;
        return UserCallbacks > before;
    }

    private void Apply(int index, bool defer = false)
    {
        if (!SelectionPolicy.ShouldApply(_count, _selected, index))
        {
            return;
        }

        var busy = _rebuilding.TryGetValue(_control, out var rebuilding) && rebuilding;

        // 重建期间第三问为真 → 不登记（真代码就是这个判据）。
        // 于是补发那一发没有 Expect 可匹配，只能靠区间拦。
        if (SelectionGate.ShouldExpectEcho(index, isReady: !GateOnReady || _ready, isRebuilding: busy))
        {
            _echo.Expect(_control, index);
        }

        MoveSelection(index, defer);
        _echo.CancelIfUnconsumed(_control);
    }

    private void MoveSelection(int index, bool defer = false, bool fromUser = false)
    {
        // 源码的早退：selectedItem == SelectedItem() 时一发都不抛。
        if (_selected == index)
        {
            return;
        }

        _selected = index;

        if (defer)
        {
            _deferred = index;
            return;
        }

        Raised++;
        Dispatch(index, fromUser);
    }

    private void Dispatch(int value, bool fromUser = false)
    {
        var busy = _rebuilding.TryGetValue(_control, out var rebuilding) && rebuilding;

        // 判据零在这里翻译成整数：value >= 0 表示"这一发里有个实项"。
        // 真代码传的是 SelectionArgs.SelectedSomething(args)，形状相同。
        var verdict = SelectionGate.Decide(
            hasRealItem: value >= 0,
            isReady: !GateOnReady || _ready,
            isRebuilding: busy);

        if (SelectionGate.Suppress(verdict))
        {
            Silenced++;
            return;
        }

        if (_echo.Consume(_control, value))
        {
            Echoed++;
            return;
        }

        if (_mounting || _callback is null)
        {
            return;
        }

        if (fromUser)
        {
            // 用户自己动的：必须回调出去，这一发是本条线的对照物 ——
            // 修法把假回调挡住了，但真实操作一个都不能少。
            UserCallbacks++;
            _callback(value);
            return;
        }

        Spurious++;

        // 回调跑出去 = setState：state 被这一发凭空改掉。
        _state = value;
        _callback(value);
    }

    private void Score()
    {
        // 只记「选中被清空之后没补回来」这一档。控件收敛成别的值
        // （ReapplyConverges=false）是另一回事，混进来会把两类问题搅成一个数。
        if (_selected < 0 && !SelectionPolicy.IsOutOfRange(_count, _state))
        {
            Lost++;
        }
    }
}
