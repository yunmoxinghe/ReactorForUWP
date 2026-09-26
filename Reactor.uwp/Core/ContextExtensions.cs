using System;
using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// 在元素树上提供 Context 值（对齐官方 <c>ContextExtensions.Provide</c>，单独拆文件）。
/// 后代组件通过 <c>UseContext(context)</c> 读取，最近的提供者生效。
/// </summary>
public static class ContextExtensions
{
    public static TElement Provide<TElement, TValue>(
        this TElement element,
        Context<TValue> context,
        TValue value)
        where TElement : Element
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        if (context is null) throw new ArgumentNullException(nameof(context));

        return (TElement)(element with
        {
            Modifiers = (element.Modifiers ?? new ElementModifiers()) with
            {
                ContextValues = Merge(element.Modifiers?.ContextValues, context, value),
            },
        });
    }

    private static IReadOnlyDictionary<ContextBase, object?> Merge(
        IReadOnlyDictionary<ContextBase, object?>? existing,
        ContextBase context,
        object? value)
    {
        var map = new Dictionary<ContextBase, object?>();
        if (existing is not null)
        {
            foreach (var pair in existing)
            {
                map[pair.Key] = pair.Value;
            }
        }

        map[context] = value;
        return map;
    }
}
