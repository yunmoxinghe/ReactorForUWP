using System;
using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Core;

/// <summary>Hook 槽位基类。所有 hook 状态按调用顺序保存在 RenderContext 的 List 中。</summary>
internal abstract class HookState
{
}

/// <summary>UseState 的槽位：保存当前值与可选的同步锁。</summary>
internal sealed class ValueHookState<T> : HookState
{
    public T Value;
    public readonly object? Lock;
    public readonly bool ThreadSafe;

    public ValueHookState(T initialValue, bool threadSafe)
    {
        Value = initialValue;
        ThreadSafe = threadSafe;
        if (threadSafe)
        {
            Lock = new object();
        }
    }
}

/// <summary>UseReducer 的槽位：保存当前状态与 dispatch 委托。</summary>
internal sealed class ReducerHookState<TState, TAction> : HookState
{
    public TState State;
    public Func<TState, TAction, TState> Reducer;
    public Action<TAction>? Dispatch;
    public readonly bool ThreadSafe;
    public readonly object? Lock;

    public ReducerHookState(Func<TState, TAction, TState> reducer, TState initialValue, bool threadSafe)
    {
        Reducer = reducer;
        State = initialValue;
        ThreadSafe = threadSafe;
        if (threadSafe)
        {
            Lock = new object();
        }
    }
}

/// <summary>UseEffect 的槽位：保存依赖、回调与 cleanup。</summary>
internal sealed class EffectHookState : HookState
{
    /// <summary>本次渲染声明的依赖集合：null=每次渲染都执行。 </summary>
    public object?[]? Dependencies;

    /// <summary>上次执行时的依赖集合，用于判断是否需要重新执行。</summary>
    public object?[]? PreviousDependencies;

    public Action? Effect;
    public Func<Action>? EffectWithCleanup;
    public Action? Cleanup;
    public bool HasRun;
}

/// <summary>Hook 调用顺序错误：每次渲染必须以相同顺序调用 hook。</summary>
public sealed class HookOrderException : InvalidOperationException
{
    public HookOrderException(string message) : base(message) { }
}
