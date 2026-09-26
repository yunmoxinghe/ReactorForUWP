using System;
using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// 跨渲染保持的可变引用（对齐官方 <c>Microsoft.UI.Reactor.Core.Ref&lt;T&gt;</c>）。
/// 改 <see cref="Current"/> 不触发重渲染，适合存放计时器、原生控件句柄等。
/// </summary>
public class Ref<T>
{
    public T Current { get; set; }

    public Ref(T initial) => Current = initial;
}

/// <summary>Context 的弱类型基类，供 <see cref="Element"/> 上的 ContextValues 字典使用。</summary>
public abstract class ContextBase
{
    internal abstract object? DefaultValueBoxed { get; }
}

/// <summary>
/// 一个带默认值的作用域上下文（对齐官方 <c>Context&lt;T&gt;</c>）。
/// 祖先元素用 <c>Provide(context, value)</c> 提供，后代用 <c>UseContext(context)</c> 读取。
/// </summary>
public sealed class Context<T> : ContextBase
{
    public T DefaultValue { get; }

    /// <summary>调试用名称，默认取调用方成员名。</summary>
    public string? Name { get; }

    public Context(T defaultValue, [CallerMemberName] string? name = null)
    {
        DefaultValue = defaultValue;
        Name = name;
    }

    internal override object? DefaultValueBoxed => DefaultValue;
}
