using System;
using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// 协调器遍历元素树时维护的"当前 Context 值"栈（对齐官方
/// <c>Microsoft.UI.Reactor.Core.ContextScope</c>）。
/// 进入带 <c>ContextValues</c> 的元素时压栈，离开时弹栈；
/// 同一个 Context 以**最近一次**提供的值为准（后代遮蔽祖先）。
/// </summary>
internal sealed class ContextScope
{
    private readonly List<(ContextBase Context, object? Value)> _stack = new();
    private long _version;

    /// <summary>压入一组 Context 值，返回本次压入的条数（供调用方对称弹出）。</summary>
    internal int Push(IReadOnlyDictionary<ContextBase, object?> values)
    {
        var count = 0;
        foreach (var (ctx, val) in values)
        {
            _stack.Add((ctx, val));
            count++;
        }

        _version++;
        return count;
    }

    internal void Pop(int count)
    {
        if (count <= 0 || _stack.Count == 0)
        {
            return;
        }

        var remove = Math.Min(count, _stack.Count);
        _stack.RemoveRange(_stack.Count - remove, remove);
        _version++;
    }

    /// <summary>读取最近一次提供的 Context 值；没有提供者则返回默认值。</summary>
    internal T Read<T>(Context<T> context)
    {
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_stack[i].Context, context) && _stack[i].Value is T typed)
            {
                return typed;
            }
        }

        return context.DefaultValue;
    }

    /// <summary>弱类型读取（供诊断/比较使用）。</summary>
    internal object? Read(ContextBase context)
    {
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_stack[i].Context, context))
            {
                return _stack[i].Value;
            }
        }

        return context.DefaultValueBoxed;
    }

    internal bool HasActiveValues => _stack.Count > 0;

    internal long Version => _version;

    /// <summary>当前栈的快照条数（供组件节点在与祖先同步时对齐）。</summary>
    internal int Count => _stack.Count;

    /// <summary>
    /// 把另一个作用域的内容**整体替换**为本作用域的内容。
    /// 用于把遍历期的上下文固化到组件节点上，
    /// 这样组件在之后（可能跨 Dispatcher 异步）重渲染时仍能读到祖先提供的 Context。
    /// </summary>
    internal void ReplaceWith(ContextScope other)
    {
        _stack.Clear();
        _stack.AddRange(other._stack);
        _version++;
    }

    internal void Clear()
    {
        _stack.Clear();
        _version++;
    }
}
