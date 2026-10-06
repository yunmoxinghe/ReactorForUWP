using System;
using System.Collections.Generic;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// WinUI 受控选中控件的行为模型（测试替身）。
/// </summary>
/// <remarks>
/// <b>为什么需要一个模型。</b>框架里真正的 handler 依赖
/// <c>Microsoft.UI.Xaml.Controls.RadioButtons</c>，那是 UWP 类型，
/// 而本测试工程是 <c>net10.0</c>——编不进来，也就没法在没有窗口、
/// 没有 UI 线程、没有人点击的环境里跑。于是：handler 的<b>判据</b>剥成纯逻辑
/// （<see cref="SelectionGate"/>，可 Link 进来），控件的<b>行为</b>在这里用模型复刻。
/// 两者接起来就是一条可以在 CI 里跑的闭环。
/// <para>
/// <b>模型的每一条规则都有源码出处</b>，不是"看着像"：
/// <list type="bullet">
///   <item><c>m_blockSelecting{ true }</c>（<c>RadioButtons.h:91</c>）：
///         <see cref="IsLoaded"/> 为 false 时 <c>Select</c> 直接返回；但<b>依赖属性照旧被写</b>
///         ——源码原话是"会存进依赖属性，但内部选中态此刻还没生效"。</item>
///   <item><c>Select</c>（<c>cpp:356-379</c>）：三道守卫
///         <c>!m_blockSelecting &amp;&amp; !m_currentlySelecting &amp;&amp; m_selectedIndex != index</c>；
///         函数体用 <c>gsl::finally</c> 把 <c>m_currentlySelecting</c> 撑到返回为止，
///         所以<b>事件处理器执行期间重入的 Select 一律被挡</b>。</item>
///   <item><c>OnChildUnchecked</c>（<c>cpp:421-431</c>）：除了
///         <c>!m_currentlySelecting</c>，还有第二道守卫
///         <c>m_selectedIndex == 旧项索引</c>——<b>这一条决定了 <c>-1</c> 只在
///         "取消先到"时才出现</b>：若选中先发生，<c>m_selectedIndex</c> 已经变成新项，
///         与旧项索引不等，<c>Select(-1)</c> 根本不执行。</item>
///   <item><c>UpdateItemsSource</c>（<c>cpp:516-518</c>）：整批换 items 时
///         <b>无条件先 <c>Select(-1)</c></b>。</item>
/// </list>
/// 事件用 <c>(value, hasRealItem)</c> 二元组代替 <c>SelectionChangedEventArgs</c>：
/// 后者是 WinRT 类型，编不进 <c>net10.0</c>；而判据只用到
/// "<c>AddedItems</c> 里有没有非 null"这一条信息，二元组足够且等价。
/// </para>
/// </remarks>
internal sealed class FakeSelectionControl
{
    /// <summary>依赖属性 <c>SelectedIndex</c>——外界可读，也是受控下发的写入口。</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>内部 repeater 是否已 Loaded（对应 <c>m_blockSelecting == false</c>）。</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>items 是否正在整批替换（框架侧用来拦因果明确的那一发）。</summary>
    public bool Rebuilding { get; private set; }

    public int ItemCount { get; private set; } = 3;

    /// <summary>选中变更事件：<c>(此刻依赖属性的值, AddedItems 里有没有非 null 项)</c>。</summary>
    public event Action<int, bool>? Changed;

    /// <summary><c>m_selectedIndex</c>（<c>RadioButtons.h:85</c>）。</summary>
    private int _internal = -1;

    /// <summary><c>m_currentlySelecting</c>（<c>RadioButtons.h:88</c>）。</summary>
    private bool _selecting;

    /// <summary>
    /// repeater 进可视树。<c>cpp:127</c> 把 <c>m_blockSelecting</c> 置 false 后紧跟一次
    /// <c>UpdateSelectedIndex()</c> 自愈——按<b>依赖属性</b>把内部选中态补回来。
    /// </summary>
    public void Load()
    {
        IsLoaded = true;
        _internal = SelectedIndex;
    }

    /// <summary>框架的受控下发：写依赖属性。</summary>
    public void WriteControlled(int index)
    {
        // 依赖属性无条件被写（这也是"折叠区吞值"不至于永久丢失的原因）。
        SelectedIndex = index;

        // 未 Loaded：m_blockSelecting 仍为 true，内部选中态不动，也不抛事件。
        if (!IsLoaded)
        {
            return;
        }

        Select(index);
    }

    /// <summary>
    /// 用户点了第 <paramref name="index"/> 项。
    /// </summary>
    /// <param name="cancelFirst">
    /// 两条路径谁先到——<b>源码没有保证顺序，所以两种都要跑</b>。
    /// 实测日志里 <c>-1</c> 与真值的先后关系确实不稳定。
    /// </param>
    public void UserClick(int index, bool cancelFirst)
    {
        if (!IsLoaded || index < 0 || index >= ItemCount)
        {
            return;
        }

        var old = _internal;

        if (index == old)
        {
            // 点<b>已勾选</b>的那一项：IsChecked 只是 true→false，<b>只发 Unchecked</b>，
            // 没有紧随的 Checked（那要点在别处才会发生）。
            // 少了这个分支，模型会自己把选项又选回去并回调一次——
            // 那是模型编的行为，会把"点当前项不产生回调"（INV6）冲掉，
            // 也会给随机序列掺进假信号。
            OnChildUnchecked(old);
            return;
        }

        if (cancelFirst)
        {
            OnChildUnchecked(old);
            OnChildChecked(index);
        }
        else
        {
            OnChildChecked(index);
            OnChildUnchecked(old);
        }
    }

    /// <summary>整批替换 items（<c>cpp:516-518</c>：无条件先 <c>Select(-1)</c>）。</summary>
    public void RebuildItems(int count)
    {
        Rebuilding = true;
        Select(-1);
        ItemCount = count;
        Rebuilding = false;
    }

    /// <summary>
    /// 内部 repeater 回收第 <paramref name="index"/> 项对应的元素。
    /// </summary>
    /// <remarks>
    /// 出处 <c>RadioButtons.cpp:304-315</c>：<c>OnRepeaterElementClearing</c> 里，
    /// 被回收的元素如果正是勾选中的那一个，会<b>顺手 <c>Select(-1)</c></b>。
    /// 也就是"滚动/虚拟化把当前选中项划出可视区"会凭空清掉选中——
    /// 折叠区、长列表、切页都走这条路。它和点当前选中项抛的那发一模一样
    /// （<c>AddedItems = { null }</c>），所以修法必须是同一套，不能只对点击生效。
    /// </remarks>
    public void RecycleElement(int index)
    {
        if (_internal == index)
        {
            Select(-1);
        }
    }

    private void OnChildChecked(int index)
    {
        if (_selecting)
        {
            return;
        }

        Select(index);
    }

    private void OnChildUnchecked(int oldIndex)
    {
        if (_selecting)
        {
            return;
        }

        // 第二道守卫：只有"被取消的正是当前选中项"才会 Select(-1)。
        // 少了这一条，-1 会无条件出现，模型就比真实控件更凶，
        // 测出来的"失败"会是量具造的假。
        if (_internal == oldIndex)
        {
            Select(-1);
        }
    }

    private void Select(int index)
    {
        if (!IsLoaded || _selecting || _internal == index)
        {
            return;
        }

        _selecting = true;
        _internal = index;

        // cpp:376-377：SelectedIndex / SelectedItem 两个依赖属性连着写。
        SelectedIndex = index;

        // cpp:401：GetDataAtIndex(-1) 显式 return nullptr → AddedItems 是 {null}。
        // 越界同理：取不到数据，AddedItems 里同样没有非 null 项。
        // 这一条必须建模——受控值越界（items 变少而 state 还指着旧下标）时，
        // 事件会伪装成"取消选中"命中判据零，而新加的纠正逻辑会去写回同一个越界值。
        // 不建模就永远看不出那里会不会自激成死循环。
        Changed?.Invoke(index, index >= 0 && index < ItemCount);

        _selecting = false;
    }
}

/// <summary>
/// 受控闭环仿真：<c>state → 受控下发 → 控件 → 事件 → 闸门 → 回调 → setState → …</c>
/// </summary>
/// <remarks>
/// 存在的理由：<b>"点了没反应"从来不是一个函数错了，而是这条环上某一环断了。</b>
/// 单点单测（<c>EchoGuardTests</c>、<c>SelectionGate</c> 穷举）只能证明零件合格，
/// 证明不了接起来还成立。这里把整条环搭起来，然后断言<b>不变量</b>——
/// 不变量比"点一下看看"强得多：它对所有可能的到达顺序都成立，
/// 而人工点击一次只能覆盖一种顺序（这次的病恰恰是顺序不定造成的）。
/// <para>
/// 渲染刻意做成<b>排队</b>（<see cref="Drain"/>）而不是同步递归：真实框架里
/// <c>setState</c> 走 <c>RenderBatcher</c> 排到下一帧。同步递归的话，
/// 振荡会直接爆栈而不是被观测到——那就少了一类能被发现的 bug。
/// </para>
/// </remarks>
internal sealed class ControlledSelectionSim
{
    private readonly FakeSelectionControl _ctl = new();
    private readonly EchoGuard _echo = new();
    private readonly List<string> _trace = new();

    private int _state = -1;
    private int _target = -1;
    private bool _hasTarget;
    private bool _ready;
    private bool _pending;

    // 异步回写的独立队列。真实实现是 selectionRestore 走 Dispatcher.RunAsync，
    // 与"下一帧的受控下发"是两条不同的队列，执行顺序并不固定。
    private bool _restorePending;
    private int _restoreExpected;

    /// <summary>进过用户回调的次数。</summary>
    public int CallbackCount { get; private set; }

    /// <summary>每次进用户回调的值，按到达顺序。</summary>
    public List<int> CallbackValues { get; } = new();

    /// <summary>被闸门吞掉的次数。</summary>
    public int Suppressed { get; private set; }

    /// <summary>
    /// 抛出的事件里"下标非负但没有实项"的次数——即<b>受控值越界</b>。
    /// </summary>
    /// <remarks>
    /// 用来证明"越界"这个场景<b>真的被测到了</b>。不计数的话，一条指望覆盖越界
    /// 的用例可能一次都没跑到那条分支，绿得毫无意义（量具失真的老毛病）。
    /// </remarks>
    public int OutOfRangeEvents { get; private set; }

    /// <summary>当前 state。</summary>
    public int State => _state;

    /// <summary>控件依赖属性的当前值。</summary>
    public int ControlIndex => _ctl.SelectedIndex;

    /// <summary>完整时序，失败时用来定位断点。</summary>
    public IReadOnlyList<string> Trace => _trace;

    /// <summary>控件是否已经 Loaded（随机序列里避免重复 Load）。</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// 被吞掉的"取消选中"要不要把受控值纠正回来。<b>默认开</b>——它就是这次的修法。
    /// </summary>
    /// <remarks>
    /// 做成开关而不是写死，是为了让测试能<b>反向对照</b>：
    /// 把它关掉后同一步操作必须失败。一条无论修不修都通过的用例等于没写——
    /// 这个项目已经在量具失真上栽过五次（<c>GetHashCode</c> 指纹、非泛型
    /// <c>IEnumerable</c>、NuGet 与 ProjectReference 混用、符号链接日志、
    /// 帧日志刷屏），每一次都是"看着在测，其实没测到"。
    /// 关掉开关还能绿的用例，一律视为没测到东西。
    /// </remarks>
    public bool WriteBackOnSuppress { get; init; } = true;

    /// <summary>
    /// 受控纠正要不要管"有没有人监听"。<b>默认开</b>——它就是这次的修法。
    /// </summary>
    /// <remarks>
    /// 旧的写法把 <c>if (callback is null) return;</c> 放在整道闸的门口，
    /// 于是纠正被连带跳过：<b>受控但没挂回调</b>的控件遇上"点当前已选中项"，
    /// 会被 WinUI 内部的 <c>Select(-1)</c> 拨到无选中，而 state 没变 ⇒ 不重渲染
    /// ⇒ <b>永久停在 -1</b>。挂了回调的同一个控件却正常。
    /// <para>
    /// 关掉它必须红 —— 这条纪律不变：开关的存在就是为了证明用例不是在给一个
    /// 已经正确的实现点赞。
    /// </para>
    /// </remarks>
    public bool RestoreWithoutListener { get; init; } = true;

    /// <summary>
    /// 复现<b>旧</b>行为：受控写入<b>无条件</b>登记回声期望（默认 false，即已修）。
    /// </summary>
    /// <remarks>
    /// 保留这个开关是为了让"泄漏回声登记"这个 bug 有反向对照——
    /// 它和 <see cref="WriteBackOnSuppress"/> 是两个独立的 bug，
    /// 各自都得证明"不修就坏"，否则无法确认测试真的摸到了它。
    /// </remarks>
    public bool LeakEchoRegistration { get; init; }

    /// <summary>
    /// 写入后要不要撤销"没被同步消费掉"的回声登记。默认开（修法）。
    /// </summary>
    /// <remarks>
    /// 与 <c>ControlledToggleSim.SealEchoAfterWrite</c> 同出一辙：
    /// handler 的 <c>Dispatch</c> 首句是 <c>if (callback is null) return;</c>——
    /// 回调为空时<b>连 <c>Consume</c> 都不会被调用</b>，登记下来的期望永远没人领。
    /// 等到某次真实用户操作的值恰好等于它，就会被判成回声吞掉。
    /// </remarks>
    public bool SealEchoAfterWrite { get; init; } = true;

    /// <summary>
    /// 异步回写执行时，要不要复查目标值有没有被改过。默认开——对应
    /// <c>SelectionRestore.Schedule</c> 异步体第一行的 <c>now.Index != expected</c>。
    /// </summary>
    /// <remarks>
    /// 这道复查此前<b>没有任何测试守着</b>：仿真把回写简化成"跟渲染共用同一个 pending
    /// 旗标"，于是它的执行时机永远紧跟下一次渲染，快照不可能陈旧——删掉那行也不会红。
    /// 这里把它建模成<b>独立队列 + 快照</b>，才谈得上给这道复查做反向对照。
    /// </remarks>
    public bool DropStaleRestore { get; init; } = true;

    /// <summary>
    /// 此刻控件上挂着的回调是否非空（对应 handler 里 <c>Callbacks[control]</c>）。
    /// </summary>
    public bool HasCallback => _guardActive;

    private bool _guardActive = true;

    /// <summary>本次 render 给不给 <c>OnSelectedIndexChanged</c>。</summary>
    public void SetCallback(bool has)
    {
        _guardActive = has;
        _trace.Add($"rebind callback={has}");
    }

    public ControlledSelectionSim()
    {
        _ctl.Changed += OnChanged;
    }

    /// <summary>挂载：受控值立刻下发，但此刻控件还没 Loaded（折叠区场景）。</summary>
    public void Mount(int initial)
    {
        _state = initial;
        _target = initial;
        _hasTarget = true;
        ApplyControlled();
    }

    /// <summary>控件进可视树。</summary>
    public void Load()
    {
        if (IsLoaded)
        {
            return;
        }

        _ctl.Load();
        _ready = true;
        IsLoaded = true;
        _trace.Add("Loaded");

        // 框架的 Loaded 兜底：把受控目标值再补一次（幂等）。
        ApplyControlled();
    }

    /// <summary>用户点击第 <paramref name="index"/> 项。</summary>
    public void Click(int index, bool cancelFirst = false)
    {
        _trace.Add($"click {index} (cancelFirst={cancelFirst})");
        LastUserClick = index;
        _ctl.UserClick(index, cancelFirst);
    }

    /// <summary>程序化改 state（模拟页面上别的按钮改了同一个 state）。</summary>
    public void SetState(int value)
    {
        _trace.Add($"setState {_state} → {value}");
        SetStateCore(value);
    }

    /// <summary>整批替换 items。</summary>
    /// <remarks>
    /// 替换之后<b>紧跟一次受控下发</b>：真实框架里这两件事发生在同一次
    /// <c>Update</c> 内——先 <c>ReplaceItems</c>（WinUI 随之抛 <c>Select(-1)</c>），
    /// 再 <c>ApplySelectedIndex</c> 把受控值补回去。
    /// 少了这一步控件就停在重建抛出的 <c>-1</c> 上，那是<b>建模漏了一环</b>，
    /// 不是框架的病——照着漏掉的建模去"修"框架，只会改出一个假修复。
    /// </remarks>
    public void RebuildItems(int count)
    {
        _trace.Add($"rebuildItems {count}");
        _ctl.RebuildItems(count);
        ApplyControlled();
    }

    /// <summary>虚拟化回收第 <paramref name="index"/> 项的元素（选中项被划出可视区）。</summary>
    public void Recycle(int index)
    {
        _trace.Add($"recycle {index}");
        _ctl.RecycleElement(index);
    }

    /// <summary>
    /// 强制跑一轮受控下发（模拟"下一次渲染一定会发生"）。
    /// </summary>
    /// <remarks>
    /// 用于确定性用例：回调为空时用户点控件，变化没人转告 state，两者的分歧是
    /// <b>正确的</b>——受控控件的承诺是<b>下一次渲染</b>把它拽回来，不是立刻同步。
    /// 随机序列里不用它判收敛：强制渲染会把"点了没反应"也一起抹平，
    /// 让反向对照失去牙齿（这次真踩到了）。那里改看
    /// <see cref="UnreportedUserActions"/>。
    /// </remarks>
    public void RenderNow()
    {
        _pending = true;
        Drain();
    }

    /// <summary>
    /// 回调为空期间发生过的用户操作数。
    /// </summary>
    /// <remarks>
    /// 这些操作<b>按定义</b>不会转告 state（元素压根没给回调），
    /// 所以此后 state 与控件不一致是<b>正确行为</b>，不是 bug。
    /// 用它把"该断言一致性"和"不该断言"分开，比事后强制渲染保真得多。
    /// </remarks>
    public int UnreportedUserActions { get; private set; }

    /// <summary>
    /// 用户<b>最后一次真正点了哪一项</b>（不论那一发是被吞还是进了回调）。
    /// </summary>
    /// <remarks>
    /// 用于把"控件偏离受控值"这件事判成有解释的：<b>受控控件的偏离只可能是两种样子</b>——
    /// 停在受控值上，或者停在用户最后一次点击的值上（后者只在没人监听时短暂成立，
    /// 下一次渲染会把它拽回来）。<b>除这两种以外的任何值都是病。</b>
    /// <para>
    /// 它取代的是此前那种"整个序列只看 <see cref="UnreportedUserActions"/> 是否非零"
    /// 的一刀切豁免：那条判据一旦触发，后续<b>所有</b>偏差都被赦免，包括真 bug。
    /// 用它检验"纯无回调"序列时暴露得很清楚 —— 放弃纠错的旧写法，
    /// 20000 条里只红 21 条，而现实却是每条序列都有几十次"点当前选中项"的机会。
    /// </para>
    /// </remarks>
    public int LastUserClick { get; private set; } = -1;
    public int? Target => _hasTarget ? _target : null;

    /// <summary>受控目标是否落在条目数之内（越界的 target 控件兑现不了，不参与判 deviate）。</summary>
    public bool TargetInRange => _hasTarget && _target >= 0 && _target < _ctl.ItemCount;

    /// <summary>
    /// Drain 之后控件此刻的值有没有<b>解释</b>：必须是受控目标或用户最后一次点击的值。
    /// </summary>
    /// <remarks>
    /// 比"state 与控件严格相等"更弱，也比"序列里出过未上报操作就一律豁免"强得多：
    /// 前者会把正确的分歧报成 bug，后者会把真的 bug 赦免掉。这里只放行两种<b>有作者</b>
    /// 的值——控件的承诺（受控目标）与用户的手（最后一次点击），第三种值没有主人。
    /// <para>
    /// <b>它和 <see cref="Faithful"/> 互补，缺一不可。</b>前者管"控件值有没有主人"，
    /// 后者管"回写有没有违背用户此后的选择"。单留这一条时，"陈旧回写把用户的新选择
    /// 盖回去"恰好落成 <c>ControlIndex == Target</c>，看起来<b>完全有解释</b>——
    /// 实测那个反向对照直接从 4384 条掉到 0 条。
    /// </para>
    /// </remarks>
    public bool Explained =>
        !TargetInRange
        || ControlIndex == (Target ?? ControlIndex)
        || ControlIndex == LastUserClick;

    /// <summary>
    /// Drain 之后 state 与控件是否一致，<b>只豁免"用户最后一次点击还没人转告"</b>这一种。
    /// </summary>
    /// <remarks>
    /// 它取代的是 <c>UnreportedUserActions &gt; 0 就整条序列免检</c>那条一刀切
    /// 判据——那条一旦触发，<b>后续所有</b>偏差都被赦免，包括真 bug。
    /// 换成按"最后一次点击"精确豁免之后，同一批序列上"放弃纠错"的反向对照
    /// 从 21 条涨到近四千条，说明原先那条判据一直在替 bug 打掩护。
    /// <para>
    /// 为什么用 <see cref="LastUserClick"/> 而不是"有没有回调"：真正决定
    /// "分歧是否被允许"的是<b>控件此刻停在哪个值上</b>——停在用户刚点的那一项，
    /// 那是他亲手拨的，下一次渲染会收敛；停在别处（典型是 -1），谁都没让它去那儿。
    /// </para>
    /// </remarks>
    public bool Faithful =>
        !TargetInRange
        || State == ControlIndex
        || ControlIndex == LastUserClick;

    /// <summary>
    /// 跑完所有排队的渲染。
    /// </summary>
    /// <returns>实际执行的渲染轮数。</returns>
    public int Drain(int maxRounds = 64)
    {
        var rounds = 0;

        while ((_pending || _restorePending) && rounds < maxRounds)
        {
            rounds++;

            if (_pending)
            {
                _pending = false;
                ApplyControlled();
            }

            if (_restorePending)
            {
                RunRestore();
            }
        }

        return rounds;
    }

    private void SetStateCore(int value)
    {
        // 同值 setState 不排队重渲染（React 语义，也是"点当前项没反应"的成因）。
        if (_state == value)
        {
            return;
        }

        _state = value;
        _pending = true;
    }

    /// <summary>受控下发：把 state 落到控件上。</summary>
    private void ApplyControlled()
    {
        if (!_hasTarget)
        {
            return;
        }

        _target = _state;

        if (_ctl.SelectedIndex == _target)
        {
            return;
        }

        _trace.Add($"下发 {_ctl.SelectedIndex} → {_target}");

        // 只在"这一发回声有可能走到回声判据"时才登记。
        // 回声那道是四道闸里的<b>最后一道</b>：只有前三道全放行才会调用 Consume。
        // 于是——
        //   未就绪时写入 → 事件会被"未就绪"那道吞掉，Consume 不会被调用；
        //   重建中写入   → 事件会被"重建中"那道吞掉，Consume 也不会被调用。
        // 这两种情况下登记等于埋一颗<b>永不消费的雷</b>：等到用户真的点了同一个值，
        // Consume 匹配上这条陈旧登记，把一次真实用户操作判成回声吞掉。
        // 表现就是"点了没反应"，而且只在该值上复现，极难靠手点定位。
        // 复用真实判据而不是在这里重写一遍条件：模型与线上代码共用同一个函数，
        // 才谈得上"测的是线上那份逻辑"（否则测的是一份抄走的副本，抄错了也测不出来）。
        if (LeakEchoRegistration || SelectionGate.ShouldExpectEcho(_target, _ready, _ctl.Rebuilding))
        {
            _echo.Expect(_ctl, _target);
        }

        _ctl.WriteControlled(_target);

        if (SealEchoAfterWrite && _echo.CancelIfUnconsumed(_ctl))
        {
            _trace.Add("  本次写入无回声 → 撤销登记");
        }
    }

    /// <summary>
    /// 跑一次异步回写（对应 <c>SelectionRestore.Schedule</c> 里那个
    /// <c>RunAsync</c> 回调）。
    /// </summary>
    private void RunRestore()
    {
        var expected = _restoreExpected;
        _restorePending = false;

        // handler 的异步体第一行：
        //   if (!targets.TryGetValue(control, out var now) || now.Index != expected) return;
        // 也就是"这中间受控值被改过就别动手"。少了它，一次排队的回写会把用户此后的
        // 选择整个盖回去——而且盖的是<b>上一轮</b>的值，用户看到的是"值自己弹回来了"。
        if (DropStaleRestore && (!_hasTarget || _target != expected))
        {
            _trace.Add($"  restore#{expected} 已陈旧（现在目标是 {_target}）→ 丢弃");
            return;
        }

        if (_ctl.SelectedIndex == expected)
        {
            return;
        }

        _trace.Add($"  restore → {expected}");

        // 真实 ApplySelectedIndex(control, index) 会同时把目标记录写成 index。
        _target = expected;

        if (SelectionGate.ShouldExpectEcho(expected, _ready, _ctl.Rebuilding))
        {
            _echo.Expect(_ctl, expected);
        }

        _ctl.WriteControlled(expected);

        if (SealEchoAfterWrite && _echo.CancelIfUnconsumed(_ctl))
        {
            _trace.Add("  本次回写无回声 → 撤销登记");
        }
    }

    private void OnChanged(int value, bool hasRealItem)
    {
        if (!_guardActive)
        {
            // 对应 handler 的旧写法：callback is null 时整道闸跳过。
            // 纠正<b>不属于"有人监听才做的事"</b>——受控控件的承诺是"值由 state 说了算"，
            // 与有没有接过通知无关（见 SelectionGate.ShouldRestoreAfterSuppress）。
            // 这里照真 handler 修好之后的形状走：判定照做，只有回调那一步省掉。
            var silentVerdict = SelectionGate.Decide(hasRealItem, _ready, _ctl.Rebuilding);

            if (RestoreWithoutListener
                && WriteBackOnSuppress
                && SelectionGate.ShouldRestoreAfterSuppress(silentVerdict)
                && _hasTarget
                && _ctl.SelectedIndex != _target)
            {
                _restoreExpected = _target;
                _restorePending = true;
                _trace.Add($"  无人监听也纠正：排队回写 → {_restoreExpected}");
            }

            if (SelectionGate.Suppress(silentVerdict))
            {
                return;
            }

            // 走到这里的是"真用户的选中"：没回调 ⇒ 转告不了 state，
            // 此后的不一致是<b>下一次渲染会收敛</b>的正常语义，不是 bug。
            UnreportedUserActions++;
            _trace.Add($"  event {value} → 无人接（回调为空）");
            return;
        }

        if (!hasRealItem && value >= 0)
        {
            OutOfRangeEvents++;
        }

        var verdict = SelectionGate.Decide(hasRealItem, _ready, _ctl.Rebuilding);

        // 回声那道单独判：Consume 有副作用，必须在前三道都放行之后才调用。
        if (verdict == SelectionVerdict.Pass && _echo.Consume(_ctl, value))
        {
            verdict = SelectionVerdict.Echo;
        }

        if (SelectionGate.Suppress(verdict))
        {
            Suppressed++;
            _trace.Add($"  event {value} real={hasRealItem} → 吞（{SelectionGate.Reason(verdict)}）");

            // 受控语义：这个值由 state 说了算。事件照旧吞掉（绝不能把 state 打成 -1），
            // 但"取消选中"这一发会把控件拨到 -1，而它<b>不是用户意图</b>——
            // 单选控件不允许点掉选中。于是不纠正的话：界面上没有任何一项被选中，
            // state 却还是旧值，表现就是"点了没反应"。
            //
            // 只对 CancelTransient 纠正，不对其余三道：
            //   未就绪 / 重建中 → 控件此刻的值就是真实值，纠正等于把用户点掉了；
            //   回声 → 值本来就对的，纠正会自激。
            //
            // 排队而不是同步写：事件是在 WinUI 的 m_currentlySelecting 期间派发的，
            // 此刻写 SelectedIndex 会被 Select 的守卫挡掉（gsl::finally 还没复位），
            // 写了也白写——真实 handler 里对应的是 Dispatcher.RunAsync。
             if (WriteBackOnSuppress
                 && verdict == SelectionVerdict.CancelTransient
                 && _hasTarget
                 && _ctl.SelectedIndex != _target)
             {
                 // 快照此刻的目标值：真实 SelectionRestore 里是
                 // <c>var expected = target.Index;</c>，异步体再拿它跟当时的目标比。
                 _restoreExpected = _target;
                 _restorePending = true;
                 _trace.Add($"  排队的异步回写 → {_restoreExpected}");
             }

            return;
        }

        CallbackCount++;
        CallbackValues.Add(value);
        _trace.Add($"  event {value} real={hasRealItem} → 回调");

        SetStateCore(value);
    }
}
