using System;
using System.Collections.Generic;
using System.Linq;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 虚拟化列表的<b>身份解析</b>：把视窗内的下标映射成稳定身份（key）。
/// </summary>
/// <remarks>
/// 单独抽成不依赖 XAML 的纯逻辑，是为了能在 <c>tests/Reactor.Core.Tests</c> 里
/// 以 Link 方式直接断言——插入/删除导致的下标位移，是这个列表最容易错、又最难
/// 靠肉眼看出来的一类 bug，必须有回归兜住，而不是等手工滚一遍才发现。
/// <para>
/// 规则：
/// <list type="bullet">
/// <item>给了选择器 → 用它的结果当身份，与下标无关。</item>
/// <item>没给选择器，或选择器返回 null → 退化为下标身份（旧行为，下标会整体位移）。</item>
/// <item>同一批里出现重复 key → 这几项退化为下标身份，避免互相抢占同一个挂载槽。</item>
/// </list>
/// </para>
/// </remarks>
internal static class VirtualKeys
{
    /// <summary>
    /// 解析 <paramref name="first"/>..<paramref name="last"/> 这段视窗内每个下标的身份。
    /// </summary>
    internal static Dictionary<int, object> Resolve(
        IReadOnlyList<object?> items,
        int first,
        int last,
        Func<object?, object?>? selector)
    {
        var keyOfIndex = new Dictionary<int, object>();
        if (items is null)
        {
            return keyOfIndex;
        }

        if (first < 0)
        {
            first = 0;
        }

        if (last >= items.Count)
        {
            last = items.Count - 1;
        }

        var firstSeenAt = new Dictionary<object, int>();
        var duplicated = new HashSet<object>();

        for (var i = first; i <= last; i++)
        {
            var key = KeyFor(items[i], i, selector);
            keyOfIndex[i] = key;

            if (!firstSeenAt.TryAdd(key, i))
            {
                duplicated.Add(key);
            }
        }

        if (duplicated.Count > 0)
        {
            foreach (var i in keyOfIndex.Keys.ToList())
            {
                if (duplicated.Contains(keyOfIndex[i]))
                {
                    keyOfIndex[i] = i;
                }
            }
        }

        return keyOfIndex;
    }

    /// <summary>单项的身份：优先取选择器结果，取不到就用下标。</summary>
    internal static object KeyFor(object? item, int index, Func<object?, object?>? selector)
    {
        var key = selector?.Invoke(item);
        return key ?? index;
    }
}
