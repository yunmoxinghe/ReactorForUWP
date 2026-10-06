using System;
using System.Collections.Generic;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 「写区间把受控值夹了」这个形状的模型：<c>Slider</c> / <c>NumberBox</c> 的
/// <c>Minimum</c> / <c>Maximum</c> 一变，控件会把 <c>Value</c> 夹进新区间，
/// 那一发 <c>ValueChanged</c> 抛在<b>旧订阅还挂着</b>的时候。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么会有这一发。</b>源码依据（WinUI 2 分支 <c>dev/NumberBox/NumberBox.cpp</c>）：
/// <c>OnMinimumPropertyChanged</c> 与 <c>OnMaximumPropertyChanged</c>
/// <b>各自</b>调一次 <c>CoerceValue</c>；后者在 <c>Value</c> 越界且
/// <c>ValidationMode == InvalidInputOverwritten</c>（idl 里该枚举的第一项，即默认值，
/// handler 没有改过它）时调 <c>Value(Minimum())</c> 或 <c>Value(max)</c>，
/// 而 <c>OnValuePropertyChanged</c> 在 <c>newValue != oldValue</c> 时
/// <c>m_valueChangedEventSource(...)</c> 抛出 <c>ValueChanged</c>。
/// 也就是说：<b>改两个边界 = 两次夹取机会</b>。
/// </para>
/// <para>
/// <b>为什么它会被当成用户输入。</b>handler 的 <c>Update</c> 里，退订发生在<b>最后</b>
/// 那句 <c>Rebind</c>（<c>Handlers.Basic.cs</c> 的 <c>Slider</c>、
/// <c>Handlers.Input.cs</c> 的 <c>NumberBox</c> 都一样），所以写 Min / Max 的那一刻
/// <b>上一轮的订阅还在</b>：夹取抛出的事件会被旧回调接住，而这一发没有任何
/// <c>Expect</c> 登记（我们事先不知道会被夹成什么值，而且两边界各夹一次时
/// 一次登记装不下两发）→ <c>Consume</c> 判"不是回声" → 回调出去 →
/// <c>setState</c> 被一个用户从未选过的值改写。
/// </para>
/// <para>
/// <b>模型里什么是真的。</b>回声抑制用的是 Link 进来的<b>真</b> <see cref="EchoGuard"/>
/// （含本版新增的 <see cref="EchoGuard.Silence"/>），不是照抄的副本；
/// 夹取那一步照上面那份源码写：<c>Value</c> 只有真变了才抛事件
/// （<c>OnValuePropertyChanged</c> 的 <c>newValue != oldValue</c>）。
/// </para>
/// </remarks>
internal sealed class RangeCoerceSim
{
    /// <summary>修法开关：写 Min / Max 时开静默窗。关掉它，同一批序列必须出现假回调。</summary>
    public bool SilenceWindow = true;

    /// <summary>
    /// 控件行为开关：改区间会夹 <c>Value</c>。关掉它是<b>无关对照</b>——
    /// 若关掉窗之后出现的假回调其实来自别处，这一档会跟着一起红。
    /// </summary>
    public bool CoerceOnRangeWrite = true;

    /// <summary>
    /// 修法开关之二：受控值写入时，登记/下发都用<b>夹取后</b>的值
    /// （<see cref="RangePolicy.Coerce"/>）。关掉它 = 退回"登记声明值"的旧写法，
    /// 那时声明值越界的一批序列必须变红。
    /// </summary>
    public bool CoercedExpect = true;

    /// <summary>渲染期冒出去、被当成用户输入的回调次数。</summary>
    public int Spurious;

    /// <summary>真实用户拖动引起的回调次数。</summary>
    public int UserCalls;

    /// <summary>控件夹了几次（窗要罩的就是这些）。</summary>
    public int Clamps;

    /// <summary>应用 state：回调会写它，所以"没人动过它却变了"能在这一位上读出来。</summary>
    public double State;

    /// <summary>控件当前的区间与值。</summary>
    public double Min = 0, Max = 100, Value = 0;

    // 真 EchoGuard（Link 进来的那一份）：修法与模型共用同一个实现，
    // 所以"关掉窗就红"测的是产品代码里的那段逻辑，不是模型的替身。
    private readonly EchoGuard _echo = new();
    private readonly object _control = new();

    private Action<double>? _callback;
    private bool _updating;

    public RangeCoerceSim()
    {
    }

    /// <summary>挂载：初值 + 首个回调（此刻订阅还没挂，写了也没人听见）。</summary>
    public void Mount(double min, double max, double value, Action<double>? onChanged)
    {
        Min = min;
        Max = max;
        State = value;
        SetValueCore(value, silent: true);
        Bind(onChanged);
    }

    /// <summary>
    /// 一轮渲染（照 handler <c>Update</c> 的次序写：区间 → 受控值 → 最后才 <c>Rebind</c>）。
    /// </summary>
    public void Update(double min, double max, double? value, Action<double>? onChanged)
    {
        _updating = true;

        if (SilenceWindow)
        {
            using (_echo.Silence(_control))
            {
                WriteRange(min, max);
            }
        }
        else
        {
            WriteRange(min, max);
        }

        // 受控值写入：既有机制（Expect → 写 → 撤销没人领的登记），
        // 但登记的是<b>夹取后</b>的值（修法之二）——写声明值会被控件夹成别的值，
        // 回读出来的对不上登记 → mismatch → 那一发照样被当成用户输入。
        if (value.HasValue)
        {
            var declared = value.Value;
            var target = CoercedExpect ? RangePolicy.Coerce(declared, Min, Max) : declared;

            if (!Nearly(Value, target))
            {
                _echo.Expect(_control, target);
                SetValueCore(target, silent: false);
                _echo.CancelIfUnconsumed(_control);
            }
        }

        Bind(onChanged);
        _updating = false;
    }

    /// <summary>用户真的拖了一下。</summary>
    public void UserDrag(double value)
    {
        SetValueCore(value, silent: false);
    }

    private void Bind(Action<double>? onChanged)
    {
        // Rebind 的替身：退订旧的 → 挂新的。要点是它发生在<b>写之后</b>。
        _callback = onChanged is null
            ? null
            : (Action<double>)(v =>
            {
                if (_echo.Consume(_control, v))
                {
                    return;
                }

                Deliver(v);
            });
    }

    private void Deliver(double value)
    {
        // 回调出去 = 应用把它当成用户输入（state 被改写）。
        State = value;

        if (_updating)
        {
            Spurious++;
        }
        else
        {
            UserCalls++;
        }
    }

    private void WriteRange(double min, double max)
    {
        // 两个边界会互相夹（NumberBox 的 CoerceMaximum / CoerceMinimum），
        // 模型照抄：写了一个之后另一个越界就把它拉回来。
        if (!Nearly(Min, min))
        {
            Min = min;

            if (Min > Max)
            {
                Max = Min;
            }

            Coerce();
        }

        if (!Nearly(Max, max))
        {
            Max = max;

            if (Max < Min)
            {
                Min = Max;
            }

            Coerce();
        }
    }

    /// <summary>
    /// 夹取：照 <c>CoerceValue</c> 写——越界就拉到最近的边界，
    /// 且<b>只有值真变了才抛事件</b>（<c>OnValuePropertyChanged</c> 的 <c>newValue != oldValue</c>）。
    /// </summary>
    private void Coerce()
    {
        if (!CoerceOnRangeWrite)
        {
            return;
        }

        var target = Value;

        if (target < Min)
        {
            target = Min;
        }
        else if (target > Max)
        {
            target = Max;
        }

        if (Nearly(target, Value))
        {
            return;
        }

        Clamps++;
        SetValueCore(target, silent: false);
    }

    private void SetValueCore(double value, bool silent)
    {
        var target = CoerceOnRangeWrite ? Clamp(value) : value;

        if (Nearly(target, Value))
        {
            return;
        }

        Value = target;

        if (silent)
        {
            return;
        }

        // 抛事件：此刻在场的是<b>上一轮</b>的回调（Bind 还没跑）。
        _callback?.Invoke(Value);
    }

    private double Clamp(double v) => v < Min ? Min : v > Max ? Max : v;

    private static bool Nearly(double a, double b) => Math.Abs(a - b) < 1e-9;
}
