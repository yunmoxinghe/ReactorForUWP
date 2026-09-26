using System;
using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// 组件渲染上下文：按调用顺序管理 hook 槽位，
/// 并在状态变化时请求宿主重渲染。
/// </summary>
public sealed class RenderContext
{
    private readonly List<HookState> _hooks = new(8);
    private int _hookIndex;
    private bool _isRendering;
    private Action? _requestRerender;
    private Func<bool>? _isMountedCheck;
    private Func<ContextBase, object?>? _contextLookup;

    /// <summary>由宿主在首次渲染前注入：状态变化时请求重渲染。</summary>
    internal Action? RequestRerender
    {
        get => _requestRerender;
        set => _requestRerender = value;
    }

    /// <summary>由宿主注入：检查组件是否仍挂载（卸载后忽略 setter）。</summary>
    internal Func<bool>? IsMountedCheck
    {
        get => _isMountedCheck;
        set => _isMountedCheck = value;
    }

    /// <summary>开始一次渲染：重置 hook 下标并标记渲染中。</summary>
    internal void BeginRender()
    {
        _hookIndex = 0;
        _isRendering = true;
    }

    /// <summary>结束一次渲染：校验 hook 数量一致，执行待处理的 effect。</summary>
    internal void EndRender()
    {
        _isRendering = false;

        if (_hookIndex != _hooks.Count)
        {
            throw new HookOrderException(
                $"Hook 数量不一致：本次渲染调用了 {_hookIndex} 个 hook，" +
                $"但之前渲染了 {_hooks.Count} 个。Hook 必须在每次渲染中以相同顺序调用。");
        }

        FlushEffects();
    }

    /// <summary>声明一个状态槽。</summary>
    public (T Value, Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false)
    {
        if (!_isRendering)
        {
            throw new InvalidOperationException("UseState 只能在 Render() 中调用。");
        }

        ValueHookState<T> hook;
        if (_hookIndex >= _hooks.Count)
        {
            hook = new ValueHookState<T>(initialValue, threadSafe);
            _hooks.Add(hook);
        }
        else
        {
            if (_hooks[_hookIndex] is not ValueHookState<T> existing)
            {
                throw new HookOrderException(
                    $"Hook 类型不匹配：第 {_hookIndex} 个 hook 期望 ValueHookState<{typeof(T).Name}>，" +
                    $"但实际是 {_hooks[_hookIndex].GetType().Name}。Hook 必须在每次渲染中以相同顺序调用。");
            }
            hook = existing;
        }

        _hookIndex++;

        T currentValue;
        if (hook.ThreadSafe && hook.Lock is not null)
        {
            lock (hook.Lock)
            {
                currentValue = hook.Value;
            }
        }
        else
        {
            currentValue = hook.Value;
        }

        return (currentValue, SetValue);

        void SetValue(T newValue)
        {
            if (!IsMounted())
            {
                return;
            }

            T oldValue;
            if (hook.ThreadSafe && hook.Lock is not null)
            {
                lock (hook.Lock)
                {
                    oldValue = hook.Value;
                    hook.Value = newValue;
                }
            }
            else
            {
                oldValue = hook.Value;
                hook.Value = newValue;
            }

            if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
            {
                _requestRerender?.Invoke();
            }
        }
    }

    /// <summary>声明一个状态槽，setter 支持函数式更新（接收旧值返回新值）。</summary>
    public (T Value, Action<Func<T, T>> Update) UseReducer<T>(T initialValue, bool threadSafe = false)
    {
        if (!_isRendering)
        {
            throw new InvalidOperationException("UseReducer 只能在 Render() 中调用。");
        }

        ValueHookState<T> hook;
        if (_hookIndex >= _hooks.Count)
        {
            hook = new ValueHookState<T>(initialValue, threadSafe);
            _hooks.Add(hook);
        }
        else
        {
            if (_hooks[_hookIndex] is not ValueHookState<T> existing)
            {
                throw new HookOrderException(
                    $"Hook 类型不匹配：第 {_hookIndex} 个 hook 期望 ValueHookState<{typeof(T).Name}>，" +
                    $"但实际是 {_hooks[_hookIndex].GetType().Name}。");
            }
            hook = existing;
        }

        _hookIndex++;

        T current;
        if (hook.ThreadSafe && hook.Lock is not null)
        {
            lock (hook.Lock)
            {
                current = hook.Value;
            }
        }
        else
        {
            current = hook.Value;
        }

        return (current, Update);

        void Update(Func<T, T> reducer)
        {
            if (!IsMounted())
            {
                return;
            }

            T oldValue, newValue;
            if (hook.ThreadSafe && hook.Lock is not null)
            {
                lock (hook.Lock)
                {
                    oldValue = hook.Value;
                    newValue = reducer(oldValue);
                    hook.Value = newValue;
                }
            }
            else
            {
                oldValue = hook.Value;
                newValue = reducer(oldValue);
                hook.Value = newValue;
            }

            if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
            {
                _requestRerender?.Invoke();
            }
        }
    }

    /// <summary>声明一个 reducer 状态槽。</summary>
    public (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(
        Func<TState, TAction, TState> reducer,
        TState initialValue,
        bool threadSafe = false)
    {
        if (!_isRendering)
        {
            throw new InvalidOperationException("UseReducer 只能在 Render() 中调用。");
        }

        ReducerHookState<TState, TAction> hook;
        if (_hookIndex >= _hooks.Count)
        {
            hook = new ReducerHookState<TState, TAction>(reducer, initialValue, threadSafe);
            _hooks.Add(hook);
        }
        else
        {
            if (_hooks[_hookIndex] is not ReducerHookState<TState, TAction> existing)
            {
                throw new HookOrderException(
                    $"Hook 类型不匹配：第 {_hookIndex} 个 hook 期望 ReducerHookState，" +
                    $"但实际是 {_hooks[_hookIndex].GetType().Name}。");
            }
            hook = existing;
        }

        _hookIndex++;

        if (hook.Dispatch is null)
        {
            hook.Dispatch = action =>
            {
                if (!IsMounted())
                {
                    return;
                }

                TState oldState, newState;
                if (hook.ThreadSafe && hook.Lock is not null)
                {
                    lock (hook.Lock)
                    {
                        oldState = hook.State;
                        newState = hook.Reducer(oldState, action);
                        hook.State = newState;
                    }
                }
                else
                {
                    oldState = hook.State;
                    newState = hook.Reducer(oldState, action);
                    hook.State = newState;
                }

                if (!EqualityComparer<TState>.Default.Equals(oldState, newState))
                {
                    _requestRerender?.Invoke();
                }
            };
        }

        return (hook.State, hook.Dispatch);
    }

    /// <summary>注册一个副作用（deps 为空数组=mount 一次，null=每次渲染，非空=变化才执行）。</summary>
    public void UseEffect(Action effect, params object[] dependencies)
    {
        UseEffectInternal(effect, null, dependencies);
    }

    /// <summary>注册一个带 cleanup 的副作用。</summary>
    public void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies)
    {
        UseEffectInternal(null, effectWithCleanup, dependencies);
    }

    private void UseEffectInternal(
        Action? effect,
        Func<Action>? effectWithCleanup,
        object?[]? dependencies)
    {
        if (!_isRendering)
        {
            throw new InvalidOperationException("UseEffect 只能在 Render() 中调用。");
        }

        EffectHookState hook;
        if (_hookIndex >= _hooks.Count)
        {
            hook = new EffectHookState();
            _hooks.Add(hook);
        }
        else
        {
            if (_hooks[_hookIndex] is not EffectHookState existing)
            {
                throw new HookOrderException(
                    $"Hook 类型不匹配：第 {_hookIndex} 个 hook 期望 EffectHookState，" +
                    $"但实际是 {_hooks[_hookIndex].GetType().Name}。");
            }
            hook = existing;
        }

        _hookIndex++;

        hook.Effect = effect;
        hook.EffectWithCleanup = effectWithCleanup;
        hook.Dependencies = dependencies;
    }

    /// <summary>缓存一次计算结果，依赖不变则直接返回上次的值。</summary>
    public T UseMemo<T>(Func<T> factory, params object[] dependencies)
    {
        if (!_isRendering)
        {
            throw new InvalidOperationException("UseMemo 只能在 Render() 中调用。");
        }

        MemoHookState<T> hook;
        if (_hookIndex >= _hooks.Count)
        {
            hook = new MemoHookState<T>();
            _hooks.Add(hook);
        }
        else
        {
            if (_hooks[_hookIndex] is not MemoHookState<T> existing)
            {
                throw new HookOrderException(
                    $"Hook 类型不匹配：第 {_hookIndex} 个 hook 期望 MemoHookState<{typeof(T).Name}>，" +
                    $"但实际是 {_hooks[_hookIndex].GetType().Name}。");
            }
            hook = existing;
        }

        _hookIndex++;

        if (!hook.HasValue || hook.Dependencies is null ||
            !SameDependencies(hook.Dependencies, dependencies))
        {
            hook.Value = factory();
            hook.Dependencies = Snapshot(dependencies);
            hook.HasValue = true;
        }

        return hook.Value;
    }

    /// <summary>缓存一个回调委托，依赖不变则保持引用稳定（便于事件订阅对比）。</summary>
    public Action UseCallback(Action callback, params object[] dependencies)
    {
        if (!_isRendering)
        {
            throw new InvalidOperationException("UseCallback 只能在 Render() 中调用。");
        }

        CallbackHookState hook;
        if (_hookIndex >= _hooks.Count)
        {
            hook = new CallbackHookState();
            _hooks.Add(hook);
        }
        else
        {
            if (_hooks[_hookIndex] is not CallbackHookState existing)
            {
                throw new HookOrderException(
                    $"Hook 类型不匹配：第 {_hookIndex} 个 hook 期望 CallbackHookState，" +
                    $"但实际是 {_hooks[_hookIndex].GetType().Name}。");
            }
            hook = existing;
        }

        _hookIndex++;

        if (hook.Callback is null || hook.Dependencies is null ||
            !SameDependencies(hook.Dependencies, dependencies))
        {
            hook.Callback = callback;
            hook.Dependencies = Snapshot(dependencies);
        }

        return hook.Callback;
    }

    /// <summary>声明一个跨渲染保持的可变引用；改 Current 不触发重渲染。</summary>
    public Ref<T> UseRef<T>(T initialValue = default!)
    {
        if (!_isRendering)
        {
            throw new InvalidOperationException("UseRef 只能在 Render() 中调用。");
        }

        RefHookState<T> hook;
        if (_hookIndex >= _hooks.Count)
        {
            hook = new RefHookState<T>(initialValue);
            _hooks.Add(hook);
        }
        else
        {
            if (_hooks[_hookIndex] is not RefHookState<T> existing)
            {
                throw new HookOrderException(
                    $"Hook 类型不匹配：第 {_hookIndex} 个 hook 期望 RefHookState<{typeof(T).Name}>，" +
                    $"但实际是 {_hooks[_hookIndex].GetType().Name}。");
            }
            hook = existing;
        }

        _hookIndex++;
        return hook.Ref;
    }

    /// <summary>读取最近的祖先元素提供（<c>Provide</c>）的 Context 值，没有则返回默认值。</summary>
    public T UseContext<T>(Context<T> context)
    {
        if (!_isRendering)
        {
            throw new InvalidOperationException("UseContext 只能在 Render() 中调用。");
        }

        if (context is null) throw new ArgumentNullException(nameof(context));

        var boxed = _contextLookup?.Invoke(context);
        return boxed is T typed ? typed : context.DefaultValue;
    }

    /// <summary>由宿主注入：沿组件链向上查找 Context 值。</summary>
    internal Func<ContextBase, object?>? ContextLookup
    {
        get => _contextLookup;
        set => _contextLookup = value;
    }

    /// <summary>执行所有待处理的 effect（在渲染完成后调用）。</summary>
    internal void FlushEffects()
    {
        foreach (var hook in _hooks)
        {
            if (hook is not EffectHookState effectHook)
            {
                continue;
            }

            var deps = effectHook.Dependencies;

            // 依赖语义：null=每次渲染执行；空/非空数组=与上次对比，变化才执行。
            var shouldRun = deps is null ||
                            !effectHook.HasRun ||
                            !SameDependencies(effectHook.PreviousDependencies, deps);

            if (!shouldRun)
            {
                continue;
            }

            // 先执行上一轮的 cleanup，再运行新 effect
            effectHook.Cleanup?.Invoke();
            effectHook.Cleanup = null;

            if (effectHook.Effect is not null)
            {
                effectHook.Effect();
            }
            else if (effectHook.EffectWithCleanup is not null)
            {
                effectHook.Cleanup = effectHook.EffectWithCleanup();
            }

            effectHook.HasRun = true;
            effectHook.PreviousDependencies = deps;
        }
    }

    /// <summary>卸载时执行所有 cleanup。</summary>
    internal void RunCleanups()
    {
        foreach (var hook in _hooks)
        {
            if (hook is EffectHookState effectHook)
            {
                effectHook.Cleanup?.Invoke();
                effectHook.Cleanup = null;
            }
        }
    }

    private bool IsMounted()
    {
        return _isMountedCheck?.Invoke() ?? true;
    }

    private static object?[] Snapshot(object?[]? deps) => deps is null ? Array.Empty<object?>() : (object?[])deps.Clone();

    private static bool SameDependencies(object?[]? a, object?[]? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }
}
