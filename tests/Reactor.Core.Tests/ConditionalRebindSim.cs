using System;
using System.Collections.Generic;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 「<c>Expect</c> → 写属性 → <c>Rebind</c>」这条我们自己写的接线模型。
/// </summary>
/// <remarks>
/// <b>它复刻的不是 WinUI 控件，是 Reactor 自己的代码。</b>不同于面包屑 / ToggleSwitch 那两个
/// 仿真（它们建模的是 WinUI 源码行），这个仿真建模的是仓库里的接线方式，事实来源因此全在本地：
/// <list type="bullet">
///   <item><c>Reactor.uwp/Internal/Reconciler.cs:1013-1092</c> —— 四个 <c>Rebind*</c>
///         一律先 <c>-= existing</c> 退订，再在回调为 <c>null</c> 时 <c>return</c>（不重订）。
///         <b>这决定了"这一发回声有没有人领"只看这一轮有没有回调。</b></item>
///   <item><c>Handlers.Basic.cs:85-107</c>（TextBox）/ <c>142-160</c>（CheckBox）/
///         <c>203-221</c>（Slider）、<c>Handlers.Input.cs:72-82</c>（PasswordBox）/
///         <c>180-195</c>（AutoSuggestBox）/ <c>328-334</c>（NumberBox） —— 都是
///         <c>Expect → 写属性 → Rebind</c> 这个顺序。</item>
///   <item><c>Handlers.Controls.cs:560-566</c>（RadioButton）形状不同：订阅一旦挂上就不再退
///         （<c>Rebind:588-598</c>），但回调为空时 <c>Invoke</c> 走的是
///         <c>current?.Invoke</c> 空转，<c>Consume</c> 同样一次都不会被调用。</item>
/// </list>
/// 两种形状由 <see cref="KeepSubscription"/> 切换（后者 = RadioButton），
/// 两个分支都要跑：这只是"是否退订"的差别，结论必须都成立。
/// </remarks>
internal sealed class ConditionalRebindSim
{
    private readonly EchoGuard _echo = new();

    /// <summary>控件的身份：EchoGuard 按控件实例分桶，这里用一个专用键代表"这一个控件"。</summary>
    private readonly object _key = new();

    private readonly List<string> _trace = new();
    private readonly List<string> _callbackValues = new();

    private string _state = string.Empty;
    private string _control = string.Empty;
    private bool _hasCallback;
    private bool _subscribed;
    private bool _dirty;
    private bool _inDrain;

    /// <summary>true = 写入后撤销没人领的登记（<c>CancelIfUnconsumed</c>）；false = 旧写法。</summary>
    public bool SealEchoAfterWrite { get; init; } = true;

    /// <summary>true = RadioButton 形状（订阅常驻，回调为空时那里只是空转）；false = TextBox 形状（退订）。</summary>
    public bool KeepSubscription { get; init; }

    public string State => _state;

    public string ControlText => _control;

    public bool HasCallback => _hasCallback;

    /// <summary>
    /// 此刻<b>真的有人接</b>：订阅已挂且回调非空。
    /// </summary>
    /// <remarks>
    /// <c>HasCallback</c> 说的是 element 上声明了回调，而 <c>Rebind</c> 要等下一轮
    /// <c>Update</c> 才跑（<c>Handlers.Basic.cs:95</c> 把它排在写入之后）。
    /// 于是"换了回调但这一帧还没渲染"时，控件仍处在上一次的订阅状态——
    /// 用户操作会不会被转告，取决于<b>已经落到控件上的那份</b>，不是新一轮的声明。
    /// 少了这个区分，批量序列会把"延迟一帧"误报成 bug。
    /// </remarks>
    public bool Observing => _subscribed && _hasCallback;

    /// <summary>回调次数。</summary>
    public int CallbackCount => _callbackValues.Count;

    public IReadOnlyList<string> CallbackValues => _callbackValues;

    /// <summary>回调为空期间发生的用户操作（天经地义没人转告 state）。</summary>
    public int UnreportedUserActions { get; private set; }

    /// <summary>受控下发过程中冒出来的回调次数（正常必须为 0）。</summary>
    public int DrainCallbacks { get; private set; }

    public IReadOnlyList<string> Trace => _trace;

    public void Mount(string initial, bool hasCallback)
    {
        _state = initial;
        _control = initial;
        _hasCallback = hasCallback;
        _trace.Add($"mount '{initial}' 回调={hasCallback}");
        Rebind();
    }

    /// <summary>渲染新一轮：框架按自己写了的那条顺序走一遍 Update。</summary>
    public void Drain()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _inDrain = true;
        Update(_state);
        _inDrain = false;
    }

    public void SetState(string value)
    {
        if (_state == value)
        {
            return;
        }

        _state = value;
        _dirty = true;
        _trace.Add($"setState '{value}'");
    }

    public void SetCallback(bool hasCallback)
    {
        if (_hasCallback == hasCallback)
        {
            return;
        }

        _hasCallback = hasCallback;
        _dirty = true;   // 换回调意味着要重新 Rebind：真实代码里 Rebind 在 Update 里跟着跑
        _trace.Add($"回调={(hasCallback ? "有" : "无")}");
    }

    /// <summary>用户直接改控件（打字 / 拨动），同步抛事件。</summary>
    public void UserEdit(string value)
    {
        if (_control == value)
        {
            return;   // 值没变，XAML 不发 ValueChanged / TextChanged
        }

        if (!Observing)
        {
            UnreportedUserActions++;
        }

        _trace.Add($"用户改 '{_control}' → '{value}'");
        SetControlCore(value);
    }

    private void Update(string value)
    {
        if (_control != value)
        {
            _echo.Expect(_key, value);
            _trace.Add($"  Expect '{value}'");

            SetControlCore(value);

            if (SealEchoAfterWrite)
            {
                _echo.CancelIfUnconsumed(_key);
                _trace.Add("  CancelIfUnconsumed");
            }
        }

        // 真实代码里 Rebind 排在写入之后（Handlers.Basic.cs:95 等），这里照抄。
        Rebind();
    }

    private void Rebind()
    {
        // TextBox 形状：回调为空 → Reconciler 先退订再 return，订阅彻底不存在；
        // RadioButton 形状：订阅常驻，但回调为空时 Invoke 只是 current?.Invoke 空转。
        _subscribed = KeepSubscription || _hasCallback;
    }

    private void SetControlCore(string value)
    {
        _control = value;

        if (!_subscribed)
        {
            _trace.Add("  事件：无订阅 → 连 Consume 都不会跑");
            return;
        }

        if (!_hasCallback)
        {
            _trace.Add("  事件：订阅常驻但回调为空（RadioButton 形状：current?.Invoke 空转）");
            return;
        }

        if (_echo.Consume(_key, _control))
        {
            _trace.Add("  事件：判定为回声 → 吞");
            return;
        }

        _callbackValues.Add(_control);

        // 回调之后 app 侧会 setState(value)（受控闭环的标准写法），state 因此跟着走。
        // 少了这一步，模型里"用户改了 → 回调报到 → state 跟上"这条链断了，
        // 收敛不变量会误报一堆假失败——那是模型在撒谎，不是框架的 bug。
        _state = _control;

        if (_inDrain)
        {
            DrainCallbacks++;
        }

        _trace.Add($"  事件：回调 '{_control}'");
    }
}
