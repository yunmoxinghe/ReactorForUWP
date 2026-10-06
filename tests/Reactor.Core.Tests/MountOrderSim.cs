using System;
using System.Collections.Generic;

namespace Reactor.Core.Tests;

/// <summary>
/// 挂载顺序模型：把 <c>Build</c> 里"写属性 → 订阅 → 又写属性"这个先后关系固化成可执行的东西。
/// </summary>
/// <remarks>
/// <para>
/// 此前这条顺序只活在注释里，没有东西能证明它。<c>Reconciler.Build</c> 的次序是
/// ① <c>handler.Mount</c>（里面先 <c>native.Text = …</c> 再订阅）；
/// ② <c>ApplyModifiers</c> → <c>Localization.ApplyUid</c> 排在最后。
/// 于是 <c>ApplyUid</c> 给 <c>x:Uid</c> 套 resw 里的 <c>Uid/Text</c> 时，
/// <b>订阅已经挂上了</b>：它抛出来的 <c>TextChanged</c> 没有任何回声登记
/// （<c>ApplyUid</c> 拿不到 handler 的 <c>TextEcho</c>），会被当成用户输入回调出去。
/// </para>
/// <para>
/// 模型不做随机模糊——这条顺序是确定性的，随机化只会稀释结论。它做的是
/// <b>反事实</b>：<c>ApplyUidBeforeSubscribe</c> 打开后（把那笔写挪到订阅之前），
/// 即便关掉修法也不该有假回调 —— 这证明<b>病因是顺序</b>，而不是别的什么。
/// </para>
/// </remarks>
internal sealed class MountOrderSim
{
    /// <summary>修法开关：挂载期 / 未进树抛的回执不承认。</summary>
    public bool SuppressMount = true;

    /// <summary>resw 里有没有 <c>Uid/Text</c> 这一条。</summary>
    public bool UidWritesText = true;

    /// <summary>
    /// 反事实：把 <c>ApplyUid</c> 那笔写挪到订阅<b>之前</b>。
    /// 打开它且关掉 <see cref="SuppressMount"/> 仍不该有假回调——那是"病因是顺序"的判决。
    /// </summary>
    public bool ApplyUidBeforeSubscribe;

    /// <summary>
    /// 事件延后到 <c>Loaded</c> 之后才抛。这是判据的<b>已知边界</b>：两条判据都假时
    /// 拦不住，模型把它摆出来而不是藏起来。
    /// </summary>
    public bool EventAfterLoaded;

    /// <summary>挂载期凭空冒出去、被当成用户输入的回调次数。</summary>
    public int Spurious;

    /// <summary>真实用户输入的回调次数。</summary>
    public int UserCalls;

    /// <summary>state 当前值。</summary>
    public string State = string.Empty;

    /// <summary>控件当前值。</summary>
    public string Control = string.Empty;

    /// <summary>控件是否已进可视树。</summary>
    public bool Loaded;

    /// <summary>是否还在 <c>Build</c> 里（对应 <c>PropWriter.IsMounting</c>）。</summary>
    public bool Mounting;

    private Action<string>? _onChanged;
    private readonly List<Action> _deferred = new();

    /// <summary>跑一遍 <c>Build</c>。</summary>
    public void Build(string initial, string? reswText, Action<string> onChanged)
    {
        _onChanged = onChanged;
        State = initial;
        Mounting = true;

        var applyUid = UidWritesText && reswText is not null;

        // ① handler.Mount：先写初值（此刻还没订阅，写了也不会有人听见）
        Control = initial;

        if (ApplyUidBeforeSubscribe && applyUid)
        {
            // 反事实分支：在订阅之前把 resw 的值写进去
            Control = reswText!;
        }

        // ② 订阅
        // ③ ApplyModifiers → ApplyUid（真实顺序是在订阅之后）
        if (!ApplyUidBeforeSubscribe && applyUid)
        {
            Control = reswText!;
            Raise();
        }

        Mounting = false;
    }

    /// <summary>控件挂上可视树。</summary>
    public void Load()
    {
        Loaded = true;

        foreach (var pending in _deferred)
        {
            pending();
        }

        _deferred.Clear();
    }

    /// <summary>用户敲了一个字符。</summary>
    public void UserTypes(string value)
    {
        if (!Loaded)
        {
            // 没进树的控件用户根本敲不到，这属于模型误用。
            throw new InvalidOperationException("未进树的控件不可能有用户输入");
        }

        Control = value;
        Raise();
    }

    private void Raise()
    {
        if (_onChanged is null)
        {
            return;
        }

        if (EventAfterLoaded && !Loaded)
        {
            _deferred.Add(Fire);
            return;
        }

        Fire();
    }

    private void Fire()
    {
        var callback = _onChanged;

        if (callback is null)
        {
            return;
        }

        if (Mounting || !Loaded)
        {
            // 挂载期 / 未进树：这一发没有用户语义。
            if (SuppressMount)
            {
                return;
            }

            Spurious++;
        }
        else
        {
            UserCalls++;
        }

        callback(Control);
    }

    /// <summary>state 改了没有 —— 假回调的特征就是"没人动过它，它却变了"。</summary>
    public bool StateClobbered(string initial) => !string.Equals(State, initial, StringComparison.Ordinal);
}
