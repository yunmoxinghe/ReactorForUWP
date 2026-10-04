using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 控件 → handler 私有状态的<b>弱键</b>表。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须是弱键。</b>handler 要跟着控件存点东西（事件回调、面包屑的数据载体、
/// 虚拟化的滚动状态），以前是 <c>static Dictionary&lt;控件, ...&gt;</c>：字典是静态的，
/// 键是控件的<b>强引用</b>，于是"控件被移出可视树"和"控件被回收"是两件事——只要
/// <see cref="Unmount"/> 没被调用（任何绕开 <c>Reconciler.UnmountTree</c> 的路径，
/// 或将来新增 handler 忘了写 Unmount），那棵控件子树就<b>永久被字典钉住</b>，
/// 连它托管的 <c>DataContext</c>、命令、页面一起泄漏。这是"泄漏入口依赖调用方记得
/// 清理"的设计，本身就是缺陷。
/// </para>
/// <para>
/// 换成 <see cref="ConditionalWeakTable{TKey, TValue}"/> 之后，生命周期的判据回到
/// 控件自己：<b>控件不可达 → 条目自动消失</b>。<c>Unmount</c> 里的 <c>Remove</c>
/// 从"不写就泄漏"降级成"提前释放的加速手段"，忘写也不泄漏。
/// </para>
/// <para>
/// <b>值的装箱。</b><c>ConditionalWeakTable</c> 要求值是引用类型，且不接受 null 值
/// （<c>Add(null)</c> 直接抛）。这里在内部套一层 <c>StrongBox</c>，于是
/// 值类型（元组）、null 都能存，对外 API 与 <see cref="Dictionary{TKey, TValue}"/>
/// 保持一致——现有 handler 代码可以一行不动地换过来。
/// </para>
/// <para>
/// <b>别拿它当缓存。</b>条目会随键一起消失，所以"按资源键缓存 DataTemplate"这类
/// <b>有意强持有</b>的场景仍然用 <c>Dictionary</c>（见
/// <c>BreadcrumbBarHandler.Templates</c> / <c>StyleSheet</c> / <c>ThemeResource</c>）。
/// </para>
/// </remarks>
/// <typeparam name="TKey">键。必须是引用类型，一般是真实控件。</typeparam>
/// <typeparam name="TValue">值。可以是值类型、元组、甚至 null。</typeparam>
internal sealed class WeakTable<TKey, TValue>
    where TKey : class
{
    private readonly ConditionalWeakTable<TKey, StrongBox> _table = new();

    /// <summary>装箱层：让值类型 / null 也能进 <c>ConditionalWeakTable</c>。</summary>
    private sealed class StrongBox
    {
        public TValue? Value;
    }

    /// <summary>读。没有该键时返回 <c>default</c>。写：已存在就覆盖。</summary>
    public TValue? this[TKey key]
    {
        get => _table.TryGetValue(key, out var box) ? box.Value : default;
        set => Set(key, value);
    }

    /// <summary>写。已存在就覆盖。</summary>
    public void Set(TKey key, TValue? value)
    {
        if (_table.TryGetValue(key, out var box))
        {
            box.Value = value;
            return;
        }

        _table.Add(key, new StrongBox { Value = value });
    }

    /// <summary>与 <see cref="Dictionary{TKey,TValue}.TryGetValue"/> 同形，方便直接换。</summary>
    /// <remarks>
    /// <c>[MaybeNullWhen(false)]</c> 必须留着：调用方清一色是
    /// <c>if (!TryGetValue(...)) return;</c> 然后直接用那个值，少了它每个调用点
    /// 都要吃一条 CS8602。
    /// </remarks>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_table.TryGetValue(key, out var box))
        {
            // box.Value 可能是 null（这个表允许 null 值，区别于
            // ConditionalWeakTable 本身），但对调用方来说"取到了"就是非 null——
            // 与 Dictionary 对 null 值的处理一致。
            value = box.Value!;
            return true;
        }

        value = default;
        return false;
    }

    public bool ContainsKey(TKey key) => _table.TryGetValue(key, out _);

    /// <summary>
    /// 提前释放。控件不可达时条目本来也会自动消失，这里只是<b>不等 GC</b>——
    /// 所以 <c>Unmount</c> 里调不调都正确，调了更及时。
    /// </summary>
    public bool Remove(TKey key) => _table.Remove(key);

    /// <summary>清空。只在测试里用。</summary>
    public void Clear() => _table.Clear();
}
