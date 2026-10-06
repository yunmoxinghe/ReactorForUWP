using System;
using System.Collections.Generic;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// <c>ToggleSwitch</c> 的行为模型（测试替身）。
/// </summary>
/// <remarks>
/// <b>每一条规则都指到源码</b>（副本见
/// <c>tools/winui2-ref/dxaml/ToggleSwitch/</c>，取自
/// <c>microsoft/microsoft-ui-xaml@main</c> 的 DXAML 分支——
/// <c>ToggleSwitch</c> 是 <c>Windows.UI.Xaml</c> 的 OS 控件，不在 WinUI 2
/// 的 <c>dev/</c> 树里，但 WinUI 2 / WinUI 3 共用这份 <c>dxaml/xcp</c> 内核实现）：
/// <list type="bullet">
///   <item><c>ToggleSwitch_Partial.cpp:347-350</c>：<c>OnPropertyChanged2</c> 里
///         <c>KnownPropertyIndex::ToggleSwitch_IsOn</c> 分支<b>直接</b>调
///         <c>OnToggledProtected()</c>，中间<b>没有任何条件</b>。</item>
///   <item><c>ToggleSwitch.g.cpp:335-351</c> → <c>OnToggled()</c>
///         （<c>g.cpp:316-331</c>）→ <c>OnToggledImpl()</c>
///         （<c>Partial.cpp:623-641</c>）→ <c>pToggledEventSource-&gt;Raise(...)</c>。
///         <b>raise 不检查模板是否已应用</b>，也不检查控件是否在可视树里。</item>
///   <item><c>ToggleSwitch_Partial.h:145-197</c>：全部成员字段只有
///         <c>m_isDragging</c> / <c>m_wasDragged</c> / <c>m_isPointerOver</c>
///         和若干位移量。<b>没有 <c>RadioButtons</c> 那种
///         <c>m_blockSelecting</c> / <c>m_currentlySelecting</c></b>——
///         所以既不会吞事件，也不防重入。</item>
/// </list>
/// 结论（这就是当初不敢下笔、必须取源码的那一条）：
/// <b><c>Toggled</c> 对每一次 <c>IsOn</c> 变更都抛，不分古今、不分是否在树里。</b>
/// 因此本模型的默认分支是 <see cref="ToggledFiresWhenUnloaded"/> = true。
/// 开关保留是为了把"如果哪天它变成有条件的，我们会暴露在哪里"也写进测试，
/// 而不是为了掩盖当初的猜测。
/// </remarks>
internal sealed class FakeToggleControl
{
    /// <summary>依赖属性 <c>IsOn</c>。</summary>
    public bool IsOn { get; private set; }

    /// <summary>模板是否已应用（控件是否进过可视树）。</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// 未套模板时写 <c>IsOn</c> 是否抛 <c>Toggled</c>。
    /// </summary>
    /// <remarks>
    /// <b>源码已核实为 true</b>（<c>Partial.cpp:623-641</c> 的 raise 没有任何前置条件）。
    /// 留成开关只为了让测试把反面也跑一遍：如果它变成 false，我们会在哪里破。
    /// </remarks>
    public bool ToggledFiresWhenUnloaded { get; init; } = true;

    /// <summary><c>Toggled</c>：参数为<b>此刻</b>的 <c>IsOn</c>。</summary>
    public event Action<bool>? Toggled;

    /// <summary>模板应用完成。</summary>
    public void Load() => IsLoaded = true;

    /// <summary>框架的受控写入。</summary>
    public void WriteControlled(bool value)
    {
        if (IsOn == value)
        {
            return;
        }

        IsOn = value;

        if (IsLoaded || ToggledFiresWhenUnloaded)
        {
            Toggled?.Invoke(IsOn);
        }
    }

    /// <summary>用户拨动到 <paramref name="value"/>。</summary>
    /// <remarks>
    /// 用户必然是在模板已应用之后才碰得到滑块，所以这里无条件抛事件。
    /// </remarks>
    public void UserToggle(bool value)
    {
        if (IsOn == value)
        {
            return;
        }

        IsOn = value;
        Toggled?.Invoke(IsOn);
    }
}

/// <summary>
/// 受控开关的闭环仿真：<c>state → 受控下发 → 控件 → Toggled → 回声判据 → 回调 → setState → …</c>
/// </summary>
/// <remarks>
/// 刻意照抄 <c>ToggleSwitchHandler</c> 的<b>语句顺序</b>，因为顺序本身就是这个
/// bug 面的一部分：
/// <list type="number">
///   <item><c>Mount</c>：先写 <c>IsOn</c>，<b>再</b> <c>Rebind</c>。所以挂载那一次
///         写入抛的事件没人接（连 <c>EchoGuard</c> 都不会被调用），handler 也
///         确实没在 <c>Mount</c> 里登记回声。</item>
///   <item><c>Update</c>：先写 <c>IsOn</c>，<b>再</b> <c>Rebind</c>。于是写入抛出的
///         那一发，是<b>上一次</b>挂着的回调在处理。</item>
///   <item>回调为 <c>null</c> 时 <c>Rebind</c> 存进去的就是 <c>null</c>，
///         事件处理器里 <c>current?.Invoke(...)</c> 直接空转——
///         <b><c>EchoGuard.Consume</c> 根本不会被调用</b>。
///         登记下来的回声期望就<b>没人消费</b>，一直留到 TTL 或下一次写入覆盖。</item>
/// </list>
/// 第 3 条看起来就是 <c>RadioButtons</c> 上抓到过的"泄漏回声登记"的同类入口，
/// 只是触发条件不同。仿真把这些原样搬进来，让随机序列去判它究竟咬不咬人——
/// <b>判据交给测试，不交给直觉</b>。
/// <para>
/// <b>渲染是排队的</b>（对应 <c>RenderBatcher</c>：仅首帧同步，后续排队到下一帧）。
/// 这意味着<b>两次点击可以落在同一帧里</b>——第二次点击时，第一次的 <c>setState</c>
/// 还没变成受控下发。同步递归的仿真造不出这条时序。
/// </para>
/// </remarks>
internal sealed class ControlledToggleSim
{
    private readonly FakeToggleControl _ctl;
    private readonly EchoGuard _echo = new();
    private readonly List<string> _trace = new();

    private bool _state;
    private bool _controlled = true;
    private bool _pending;

    /// <summary>
    /// 此刻控件上是否挂着回调。断言"一次拨动该回调几次"必须看它——
    /// 没挂回调时 0 次是<b>正确</b>的，不是 bug。
    /// </summary>
    public bool HasCallback => _guardActive;

    /// <summary>
    /// 此刻控件上挂着的回调是否非空，对应 <c>Callbacks[control]</c>。
    /// </summary>
    /// <remarks>
    /// 为 <c>false</c> 时事件照抛，但 handler 里 <c>current?.Invoke</c> 空转，
    /// <c>EchoGuard.Consume</c> <b>不会被调用</b>——这是"登记了却没人消费"的入口。
    /// </remarks>
    private bool _guardActive;

    /// <summary>进过用户回调的次数。</summary>
    public int CallbackCount { get; private set; }

    /// <summary>每次进用户回调的值，按到达顺序。</summary>
    public List<bool> CallbackValues { get; } = new();

    /// <summary>被判成回声吞掉的次数。</summary>
    public int SwallowedAsEcho { get; private set; }

    /// <summary>当前 state。</summary>
    public bool State => _state;

    /// <summary>控件依赖属性的当前值。</summary>
    public bool ControlIsOn => _ctl.IsOn;

    /// <summary>完整时序，失败时用来定位断点。</summary>
    public IReadOnlyList<string> Trace => _trace;

    /// <summary>
    /// 受控写入之后，要不要把<b>没被同步消费掉</b>的回声登记撤销。默认开（修法）。
    /// </summary>
    /// <remarks>
    /// 做成开关是为了<b>反向对照</b>：关掉后同一批序列必须失败。
    /// 一条修不修都绿的用例等于没写——这个项目已经在量具失真上栽过五次。
    /// </remarks>
    public bool SealEchoAfterWrite { get; init; } = true;

    public ControlledToggleSim(bool toggledFiresWhenUnloaded = true)
    {
        _ctl = new FakeToggleControl { ToggledFiresWhenUnloaded = toggledFiresWhenUnloaded };
        _ctl.Toggled += OnToggled;
    }

    /// <summary>
    /// 挂载（对应 handler 的 <c>Mount</c>）。
    /// </summary>
    /// <param name="controlled">是否受控（<c>element.IsOn</c> 有没有值）。</param>
    /// <param name="initial">受控时的初始值。</param>
    /// <param name="hasCallback">有没有 <c>OnIsOnChanged</c>。</param>
    public void Mount(bool controlled, bool initial = false, bool hasCallback = true)
    {
        _controlled = controlled;
        _state = initial;
        _trace.Add($"mount {(controlled ? $"受控={initial}" : "非受控")} callback={hasCallback}");

        // 顺序照抄：先写值（此刻回调还没挂，handler 也不登记回声），再 Rebind。
        if (controlled)
        {
            _ctl.WriteControlled(initial);
        }

        _guardActive = hasCallback;
    }

    /// <summary>控件进可视树。</summary>
    public void Load()
    {
        if (_ctl.IsLoaded)
        {
            return;
        }

        _ctl.Load();
        _trace.Add("Loaded");
        ApplyControlled();
    }

    /// <summary>
    /// 本次 render 给不给 <c>OnIsOnChanged</c>（对应 <c>Rebind</c> 存进去的值）。
    /// </summary>
    public void SetCallback(bool has)
    {
        _guardActive = has;
        _trace.Add($"rebind callback={has}");
    }

    /// <summary>用户拨动到 <paramref name="value"/>。</summary>
    public void UserToggle(bool value)
    {
        _trace.Add($"toggle → {value}");
        _ctl.UserToggle(value);
    }

    /// <summary>
    /// 回调为空期间发生过的用户拨动数。
    /// </summary>
    /// <remarks>
    /// 这些操作<b>按定义</b>不会转告 state（元素压根没给 <c>OnIsOnChanged</c>），
    /// 此后 state 与控件不一致是<b>正确行为</b>，不是 bug。
    /// 这条比起"事后强制渲染再判收敛"保真得多——那样会把"点了没反应"
    /// 一并抹平，让反向对照失去牙齿。
    /// </remarks>
    public int UnreportedUserActions { get; private set; }

    /// <summary>程序化改 state（模拟页面上别的入口改同一个 state）。</summary>
    public void SetState(bool value)
    {
        _trace.Add($"setState {_state} → {value}");
        SetStateCore(value);
    }

    /// <summary>跑完所有排队的渲染（真实框架里 = 下一帧）。</summary>
    public int Drain(int maxRounds = 64)
    {
        var rounds = 0;

        while (_pending && rounds < maxRounds)
        {
            _pending = false;
            rounds++;
            ApplyControlled();
        }

        return rounds;
    }

    /// <summary>
    /// 强制跑一轮受控下发（模拟"下一次渲染一定会发生"）。
    /// </summary>
    /// <remarks>
    /// 收敛不变量必须在它之后判。原因：回调为空时用户拨动开关，
    /// 没人把这个变化告诉 state——两者的分歧是<b>正确的</b>，
    /// 受控控件的承诺是"<b>下一次渲染</b>把它拽回来"，不是"立刻同步"。
    /// 直接断言 <c>state == IsOn</c> 会把这种正常分歧报成 bug，
    /// 是又一次量具失真（血泪教训：假失败比漏检更耗人）。
    /// </remarks>
    public void RenderNow()
    {
        _pending = true;
        Drain();
    }

    private void SetStateCore(bool value)
    {
        // 同值 setState 不排队重渲染（React 语义）。
        if (_state == value)
        {
            return;
        }

        _state = value;
        _pending = true;
    }

    /// <summary>
    /// 受控下发（对应 handler 的 <c>Update</c> 里那一段）。
    /// </summary>
    /// <remarks>
    /// 顺序照抄：先写 <c>IsOn</c>（用<b>旧</b>回调处理回声），再换回调。
    /// </remarks>
    private void ApplyControlled()
    {
        if (_controlled && _ctl.IsOn != _state)
        {
            _trace.Add($"下发 {_ctl.IsOn} → {_state}");
            _echo.Expect(_ctl, _state);
            _ctl.WriteControlled(_state);

            // 没有同步回声 = 这一发永远不会有人来 Consume（回调为空就是这种情形）。
            // 留着它等于埋一颗雷：等用户之后把回调装回来、再拨到同一个值，
            // Consume 匹配上这条陈旧登记，把一次真实用户操作判成回声吞掉。
            if (SealEchoAfterWrite && _echo.CancelIfUnconsumed(_ctl))
            {
                _trace.Add("  本次写入无回声 → 撤销登记");
            }
        }
    }

    private void OnToggled(bool value)
    {
        if (!_guardActive)
        {
            // 对应 handler 里 current?.Invoke(...)：回调为空，连 Consume 都不会被调用。
            // 没人监听 ⇒ state 无从得知控件漂了，此后的不一致是"受控但没给回调"
            // 的正常语义（下一次渲染才会拽回来），不是 bug——记一笔以便豁免断言。
            UnreportedUserActions++;
            _trace.Add($"  Toggled {value} → 无人接（回调为空）");
            return;
        }

        if (_echo.Consume(_ctl, value))
        {
            SwallowedAsEcho++;
            _trace.Add($"  Toggled {value} → 吞（回声）");
            return;
        }

        CallbackCount++;
        CallbackValues.Add(value);
        _trace.Add($"  Toggled {value} → 回调");
        SetStateCore(value);
    }
}
