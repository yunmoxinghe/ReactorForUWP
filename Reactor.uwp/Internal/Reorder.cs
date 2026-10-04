// IList<T> 而不是 IReadOnlyList<T>：XAML 的 UIElementCollection 只实现了前者。
using System.Collections.Generic;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 面板重排的前置判断（纯逻辑，不依赖 XAML，可脱离 UI 断言）。
/// </summary>
/// <remarks>
/// <para>
/// <b>它解决什么。</b>协调器重排子控件时，最常见的情形是"顺序根本没变"
/// （只有内容变了、或压根没动）。原先的实现对每个下标都做一次
/// <c>IndexOfNative</c> 线性扫描，而它放在 <c>for i</c> 的内层 ——
/// 即使顺序一模一样也要扫 n 次，n 个节点就是 n² 次引用比较。
/// 500 个节点一次重排 = 25 万次比较，纯属白烧。
/// </para>
/// <para>
/// <b>修法是先花 O(n) 判一次"是否已同序"</b>：是则整个重排循环直接跳过
/// （顺带保住焦点——控件一次都没被 Remove/Insert）；
/// 否则从<b>第一个不匹配的下标</b>开始干活，前缀不动。
/// </para>
/// <para>
/// <b>为什么是"从第一个不匹配处开始"仍然正确。</b>
/// 循环的不变量是：处理完下标 i 后，面板的 <c>[0..i]</c> 恒等于目标的
/// <c>[0..i]</c>。既然 <c>[0..firstMismatch)</c> 已经逐位相等，
/// 把它们当作"已固定的前缀"完全成立；
/// 而且目标序列已去重，目标控件不可能藏在这个前缀里，
/// 所以从 <c>firstMismatch</c> 开始找它必然找得到（或确认它不在面板里）。
/// </para>
/// </remarks>
internal static class Reorder
{
    /// <summary>
    /// 返回第一个 <c>current[i] != desired[i]</c>（引用比较）的下标。
    /// </summary>
    /// <returns>
    /// -1 = 已同序（可以整段跳过重排）；
    /// 0..n-1 = 第一个需要动的位置；
    /// 长度不一致时返回 0（无法逐位对齐，只能整体重来）。
    /// </returns>
    public static int FirstMismatch<T>(IList<T> current, IList<T> desired)
        where T : class
    {
        if (current.Count != desired.Count)
        {
            return 0;
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (!ReferenceEquals(current[i], desired[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>已经同序（<see cref="FirstMismatch{T}"/> 返回 -1）。</summary>
    public static bool IsSameOrder<T>(IList<T> current, IList<T> desired)
        where T : class =>
        FirstMismatch(current, desired) < 0;
}
