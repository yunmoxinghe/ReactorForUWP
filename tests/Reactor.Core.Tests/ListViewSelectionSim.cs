using System;
using System.Collections.Generic;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// ListView / GridView 的受控 <c>SelectedIndex</c> 模型。
/// </summary>
/// <remarks>
/// <b>它建模的是两件事的合体：Reactor 的接线 + XAML 内核 <c>Selector</c> 的行为。</b>
/// 事实来源逐条列在下头，方便逐句复核：
/// <list type="bullet">
///   <item><b>受控写回会同步抛事件：</b><c>Selector_Partial.cpp:144-148</c>，
///         <c>SelectedIndex</c> 的依赖属性变更 → <c>OnSelectedIndexChanged</c> → …
///         → <c>EndChange</c> 里<b>同步</b>调 <c>InvokeSelectionChanged</c>。</item>
///   <item><b>越界写入会被拒：</b>同一个函数里 <c>newValue &gt;= -1 &amp;&amp;
///         newValue &lt; nCount</c> 才真的选中，否则整条 undo；
///         且 <c>nCount != 0</c> 时让属性变更回调以 <c>E_INVALIDARG</c> 收尾。</item>
///   <item><b>改 items 会牵动选中：</b><c>NotifyOfSourceChanged</c> 的 Reset 分支先把
///         不再存在的选中 <c>Unselect</c> 掉，再调 <c>SelectAllSelectedSelectorItems</c>
///         把仍然 <c>IsSelected</c> 的项加回来，最后 <c>EndChange</c>——
///         于是这一窗口里<b>既可能抛"没有实项"的事件，也可能抛"有实项"的事件</b>。
///         两种形状本仿真都跑（<see cref="ReaddsSelectionOnReset"/>），
///         因为结论不该依赖控件挑哪一种。</item>
///   <item><b>整批替换是真实路径：</b><c>Reconciler.cs:294-340</c> 的
///         <c>PatchItems</c> 在无法就地修的时候会 <c>Items.Clear()</c> 再逐个
///         <c>Add</c>，走的正是上面那条 Reset 分支。</item>
/// </list>
/// <para>
/// <b>与 <c>ControlledSelectionSim</c> 的分工：</b>那一份建模的是 <c>RadioButtons</c>
/// （有 <c>m_blockSelecting</c>、取消选中会把选中态带成 -1），
/// 这一份建模的是 <c>Selector</c>（没有模板闸门，但会对越界写入明确拒绝）。
/// 两者共用 <see cref="SelectionGate"/> 与 <see cref="EchoGuard"/>——
/// 这两份是 Link 进来的真代码，不是抄过来的副本。
/// </para>
/// </remarks>
internal sealed class ListViewSelectionSim
{
    private readonly EchoGuard _echo = new();

    /// <summary>控件身份：<see cref="EchoGuard"/> 按控件实例分桶，这里用一个专用键代表"这一个控件"。</summary>
    private readonly object _key = new();

    private bool _writing;

    /// <summary>
    /// 此刻处在 items 变动的窗口里。<b>与是否遮蔽无关</b>：关掉遮蔽时它仍旧为 true，
    /// 否则"这些回调其实是控件自己发的"这件事就无从统计了。
    /// </summary>
    private bool _inPatch;

    /// <summary>排队中的纠正（<c>SelectionRestore</c> 的模型：独立队列 + 快照）。</summary>
    private readonly Queue<int> _restore = new();

    public ListViewSelectionSim(int itemCount)
    {
        ItemCount = itemCount;
        Selected = -1;
        State = -1;
    }

    /// <summary>true = 受控写回登记 / 消费回声。关掉 = 本轮修复之前的写法。</summary>
    public bool SuppressEcho { get; init; } = true;

    /// <summary>true = 越界不许下发（<see cref="SelectionPolicy.ShouldApply"/>）。</summary>
    public bool GuardRange { get; init; } = true;

    /// <summary>true = items 变动期间的选中事件按"重建中"处理。</summary>
    public bool ShieldRebuild { get; init; } = true;

    /// <summary>Reset 之后 <c>Selector</c> 是否把仍然选中的项重新加回来（见类注释第 3 条）。</summary>
    public bool ReaddsSelectionOnReset { get; init; }

    /// <summary>Items.Count。</summary>
    public int ItemCount { get; private set; }

    /// <summary>控件当前的 <c>SelectedIndex</c>（<c>-1</c> = 无选中）。</summary>
    public int Selected { get; private set; }

    /// <summary>应用 state 里的受控目标（<c>-1</c> = 无选中）。</summary>
    public int State { get; private set; }

    /// <summary>用户最后一次点到、并且<b>被当成用户输入</b>的下标（没有则 <c>-1</c>）。</summary>
    public int LastUserClick { get; private set; } = -1;

    /// <summary>受控写回的事件被当成用户输入回调出去的次数（期望为 0）。</summary>
    public int SpuriousCallbacks { get; private set; }

    /// <summary>items 变动期间控件自发的事件回调出去的次数（期望为 0）。</summary>
    public int PhantomCallbacks { get; private set; }

    /// <summary>向控件写下越界下标的次数（期望为 0：<c>Selector</c> 会拒）。</summary>
    public int OutOfRangeWrites { get; private set; }

    /// <summary>用户输入被正常回调的次数。</summary>
    public int UserCallbacks { get; private set; }

    /// <summary>受控目标此刻<b>能不能被控件兑现</b>：目标越界时控件没有一个"正确的值"可停。</summary>
    public bool TargetReachable => !SelectionPolicy.IsOutOfRange(ItemCount, State);

    /// <summary>
    /// 静止之后控件的值<b>有没有主人</b>：要么等于受控目标，要么等于用户最后一次点击，
    /// 要么停在"无选中"（<c>-1</c>）；目标越界时无从谈起，一律算解释得通。
    /// </summary>
    /// <remarks>
    /// 与 <c>ControlledSelectionSim</c> 里的同名判据同工：拦的是"控件停在第三种值上"——
    /// 既不来自 state、也不来自用户的那个值。它在界面上的样子就是"点了没反应"。
    /// </remarks>
    public bool Explained
    {
        get
        {
            if (!TargetReachable)
            {
                // 目标越界：控件没有一个"正确的值"可停，停在什么都谈不上偏离。
                return true;
            }

            return Selected == State || Selected == -1 ||
                   (LastUserClick >= 0 && Selected == LastUserClick);
        }
    }

    /// <summary>
    /// 目标可兑现时，控件<b>必须</b>停在受控目标上（或停在用户最后一次点击、还没回写）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Explained"/> 的差别就在这里：那一条放行"停在 -1"，
    /// 这一条不放行——<b>目标还在范围内却停在"无选中"，正是"点了没反应"的样子</b>。
    /// 两条缺一不可（上一轮有过只留一条、反向对照从 4384 掉到 0 的教训）。
    /// </remarks>
    public bool Faithful =>
        !TargetReachable || Selected == State ||
        (LastUserClick >= 0 && Selected == LastUserClick);

    /// <summary>把排队的纠正兑现（对应派发器上的下一轮）。</summary>
    public void Drain()
    {
        while (_restore.Count > 0)
        {
            var expected = _restore.Dequeue();

            // 快照语义：这中间受控值被改过就作废（与 SelectionRestore 里那句
            // `now.Index != expected` 同构）。
            if (State != expected)
            {
                continue;
            }

            Write(expected);
        }
    }

    /// <summary>用户点了第 <paramref name="index"/> 项。</summary>
    public void UserClick(int index)
    {
        if (SelectionPolicy.IsOutOfRange(ItemCount, index) || Selected == index)
        {
            // 下标不存在，或者点的就是当前选中项（值没变 → Selector 不抛事件）。
            return;
        }

        Selected = index;
        Raise(realItem: true);
    }

    /// <summary>应用的 state 变成 <paramref name="index"/>（下一轮重渲染的下发）。</summary>
    public void SetState(int index)
    {
        State = index;
        Write(index);
    }

    /// <summary>整批替换 items：新条目数为 <paramref name="newCount"/>。</summary>
    public void ReplaceItems(int newCount)
    {
        var previous = Selected;
        _inPatch = true;

        try
        {
            ItemCount = newCount;

            if (previous >= 0 && SelectionPolicy.IsOutOfRange(ItemCount, previous))
            {
                // 原来选中的下标已经不存在：Selector 会 Unselect，事件里没有实项。
                Selected = -1;
                Raise(realItem: false);
            }
            else if (ReaddsSelectionOnReset && previous >= 0)
            {
                // Reset 之后 Selector 会把仍然 IsSelected 的容器重新加回选中——
                // 这一发的 AddedItems 里有真东西，靠"取消选中"那道闸拦不住。
                Raise(realItem: true);
            }
        }
        finally
        {
            _inPatch = false;
        }

        // 安静之后照常下发受控值（与 handler 的 Update 顺序一致：
        // 先 PatchItems、再 ApplySelectedIndex）。
        Write(State);
    }

    /// <summary>受控下发。这也是越界守卫与回声登记站着的那一个入口。</summary>
    private void Write(int index)
    {
        if (!GuardRange)
        {
            // 没有守卫：照写不误。越界的那一次 Selector 会把变更 undo，
            // 并让属性变更回调以 E_INVALIDARG 收尾。
            if (SelectionPolicy.IsOutOfRange(ItemCount, index))
            {
                OutOfRangeWrites++;
                return;
            }
        }
        else if (!SelectionPolicy.ShouldApply(ItemCount, Selected, index))
        {
            return;
        }

        if (Selected == index)
        {
            return;
        }

        _writing = true;
        try
        {
            if (SuppressEcho && index >= 0)
            {
                _echo.Expect(_key, index);
            }

            Selected = index;
            Raise(realItem: index >= 0);

            // 写完了回头看一眼：这一发回声没人领就撤销登记
            // （对应真实框架里的 EchoGuard.CancelIfUnconsumed）。
            if (SuppressEcho)
            {
                _echo.CancelIfUnconsumed(_key);
            }
        }
        finally
        {
            _writing = false;
        }
    }

    /// <summary>抛一发 <c>SelectionChanged</c> 并走四道闸门。</summary>
    private void Raise(bool realItem)
    {
        var value = Selected;

        // 遮蔽与否影响的是<b>这一发走哪道闸</b>；处在窗口里这件事本身，
        // 与开关无关（用来给"凭空出现的回调"计数）。
        var verdict = SelectionGate.Decide(realItem, isReady: true, _inPatch && ShieldRebuild);

        if (verdict == SelectionVerdict.Pass && SuppressEcho && _echo.Consume(_key, value))
        {
            verdict = SelectionVerdict.Echo;
        }

        if (SelectionGate.Suppress(verdict))
        {
            if (SelectionGate.ShouldRestoreAfterSuppress(verdict))
            {
                _restore.Enqueue(State);
            }

            return;
        }

        if (_writing)
        {
            // 框架自己写进去的值，被当成用户输入回调出去了。
            SpuriousCallbacks++;
            return;
        }

        if (_inPatch && !_writing)
        {
            // 换数据源期间控件自己发出的那一发，也冒到了用户回调里。
            PhantomCallbacks++;
            UserCallbacks++;
            LastUserClick = value;
            State = value;
            return;
        }

        UserCallbacks++;
        LastUserClick = value;
        State = value;
    }
}
