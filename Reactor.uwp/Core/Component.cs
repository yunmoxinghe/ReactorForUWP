using System;
using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// 组件：状态与渲染逻辑共置。每次状态变化触发一次 Render()，
/// 返回新的不可变 <see cref="Element"/> 树，由宿主 reconcile 到真实控件。
/// Hook 必须在每次渲染中以相同顺序调用（与 React hooks 规则一致）。
/// </summary>
public abstract class Component
{
    /// <summary>本组件的 hook 上下文，由宿主在首次渲染前赋值。</summary>
    internal RenderContext Context { get; set; } = new();

    /// <summary>状态变化时的重渲染回调，由 ReactorHost 在首次渲染前赋值。</summary>
    internal Action? RerenderCallback { get; set; }

    /// <summary>返回本组件的 UI 描述树。</summary>
    public abstract Element Render();

    /// <summary>
    /// 声明一个状态槽。返回当前值与 setter；setter 在值变化时请求重渲染。
    /// 元组元素名与官方 Reactor 一致（<c>Value</c> / <c>Set</c>）。
    /// </summary>
    protected (T Value, Action<T> Set) UseState<T>(T initial = default!, bool threadSafe = false)
    {
        return Context.UseState(initial, threadSafe);
    }

    /// <summary>缓存一次计算结果，依赖不变则复用上次的值。</summary>
    protected T UseMemo<T>(Func<T> factory, params object[] dependencies) =>
        Context.UseMemo(factory, dependencies);

    /// <summary>缓存一个回调委托，依赖不变则引用稳定。</summary>
    protected Action UseCallback(Action callback, params object[] dependencies) =>
        Context.UseCallback(callback, dependencies);

    /// <summary>声明一个跨渲染保持的可变引用。</summary>
    protected Ref<T> UseRef<T>(T initialValue = default!) =>
        Context.UseRef(initialValue);

    /// <summary>读取祖先元素 Provide 的 Context 值。</summary>
    protected T UseContext<T>(Context<T> context) =>
        Context.UseContext(context);

    /// <summary>声明一个状态槽，setter 支持函数式更新（接收旧值返回新值）。</summary>
    protected (T Value, Action<Func<T, T>> Update) UseReducer<T>(
        T initialValue = default!,
        bool threadSafe = false)
    {
        return Context.UseReducer(initialValue, threadSafe);
    }

    /// <summary>声明一个 reducer 状态槽（Redux 风格）。</summary>
    protected (TState Value, Action<TAction> Dispatch) UseReducer<TState, TAction>(
        Func<TState, TAction, TState> reducer,
        TState initialValue,
        bool threadSafe = false)
    {
        return Context.UseReducer(reducer, initialValue, threadSafe);
    }

    /// <summary>注册一个副作用。</summary>
    protected void UseEffect(Action effect, params object[] dependencies)
    {
        Context.UseEffect(effect, dependencies);
    }

    /// <summary>注册一个带 cleanup 的副作用。</summary>
    protected void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies)
    {
        Context.UseEffect(effectWithCleanup, dependencies);
    }

    /// <summary>渲染前由宿主调用：重置 hook 读取游标，并绑定当前 Context 作用域。</summary>
    internal void BeginRender(ContextScope? contextScope = null) => Context.BeginRender(contextScope);

    /// <summary>渲染后由宿主调用：校验 hook 数量、执行 effect。</summary>
    internal void EndRender() => Context.EndRender();
}

/// <summary>内部接口：允许 Reconciler 在不反射的情况下设置并校验 props。</summary>
public interface IPropsReceiver
{
    void SetProps(object props);

    /// <summary>更新 props 并返回是否应重渲染（内部调用 ShouldUpdate）。</summary>
    bool UpdateProps(object? newProps);
}

/// <summary>
/// 带类型化 props 的组件基类。Props 由父组件通过 ComponentElement 传入。
/// </summary>
public abstract class Component<TProps> : Component, IPropsReceiver
{
    /// <summary>父组件传入的 props。</summary>
    public TProps Props { get; internal set; } = default!;

    /// <summary>判断 props 变化时是否需要重渲染。默认：结构相等比较。</summary>
    protected internal virtual bool ShouldUpdate(TProps? oldProps, TProps? newProps) =>
        !Equals(oldProps, newProps);

    void IPropsReceiver.SetProps(object props)
    {
        Props = (TProps)props;
    }

    bool IPropsReceiver.UpdateProps(object? newProps)
    {
        var oldProps = Props;
        Props = (TProps?)newProps ?? default!;
        return ShouldUpdate(oldProps, Props);
    }
}
