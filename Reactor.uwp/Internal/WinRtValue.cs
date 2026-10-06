using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 托管值 → WinRT 属性的投影层。
/// </summary>
/// <remarks>
/// XAML 控件上有大量 <c>object</c> 类型的属性（<c>ItemsSource</c> / <c>Content</c> /
/// <c>Header</c> / <c>HeaderIcon</c> …）。C# 侧写什么都能编译，但 CsWinRT 在 ABI
/// 边界上只接受有限的几种形状，写错就是运行时崩溃，而且位置深、堆栈长。
/// <para>
/// 最容易踩的是集合：WinRT 侧要的是 <c>IVector&lt;IInspectable&gt;</c>
/// （或 <c>IObservableVector&lt;IInspectable&gt;</c>），而 <c>List&lt;string&gt;</c>
/// 会被投影成 <c>IVector&lt;HSTRING&gt;</c>——WinUI 2 的
/// BreadcrumbBar / ItemsRepeater 直接拒绝：
/// “Argument 'source' is not a supported vector.”
/// 所以这里统一先把元素装箱成 <c>object</c>，再挑控件真能接受的容器。
/// </para>
/// <para>
/// <b>实测结论（OS 26200 / WinUI 2.8.7）：光"赋值不报错"不代表能用。</b>
/// <c>ObservableCollection&lt;object&gt;</c> 之类的托管集合赋值能过，但 CsWinRT 建的
/// CCW 过不了布局期的 <c>ItemsSourceView</c>：控件第一次真正参与 Measure 时才抛
/// 不带托管堆栈的 COMException“未指定的错误”，并让进程 fast-fail（0xC000027B），
/// 连 <c>Application.UnhandledException</c> 都拦不住。
/// 因此凡是给 WinUI 2 的 ItemsRepeater 系控件喂集合，一律改用控件自身的
/// <c>Items</c>（真正的 WinRT 向量，<c>Items.Add(...)</c>），不要走 ItemsSource + 托管集合。
/// </para>
/// </remarks>
internal static class WinRtValue
{
    /// <summary>单一值的投影：字符串 / UIElement / 值类型原样；集合走向量投影。</summary>
    public static object? Project(object? value) =>
        value is null or string or UIElement ? value : ToVectorIfCollection(value) ?? value;

    /// <summary>
    /// 给 WinRT 的集合型属性（典型是 <c>ItemsSource</c>）赋值。
    /// 逐个尝试候选容器，全失败时退回 null 并记日志——宁可少渲染，也不要让整个 App 崩掉。
    /// </summary>
    /// <param name="assign">真正的赋值动作（<c>control.ItemsSource = value</c>）。</param>
    /// <param name="source">托管集合；null 表示清空。</param>
    /// <param name="what">用于日志定位，例如控件名 + 属性名。</param>
    /// <returns>是否赋值成功。</returns>
    public static bool TryAssignItemsSource(
        Action<object?> assign,
        IEnumerable? source,
        string what,
        bool trace = true)
    {
        if (source is null)
        {
            return TryAssign(assign, null, what, trace);
        }

        var items = Materialize(source);
        if (items.Count == 0)
        {
            return TryAssign(assign, null, what, trace);
        }

        foreach (var candidate in Candidates(source, items))
        {
            if (TryAssign(assign, candidate, what, trace))
            {
                return true;
            }
        }

        // 全军覆没：清空并继续，别把异常抛到宿主导致进程终止。
        if (trace)
        {
            Reactor.Uwp.Hosting.ReactorLog.Warn(Reactor.Uwp.Hosting.ReactorLogChannel.Interop, $"{what}: 所有集合投影均被拒绝，已置空");
        }

        TryAssign(assign, null, what, trace);
        return false;
    }

    /// <summary>把任意托管集合物化成 <c>List&lt;object&gt;</c>（元素逐个装箱）。</summary>
    private static List<object?> Materialize(IEnumerable source)
    {
        var items = new List<object?>();
        foreach (var item in source)
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// 候选容器顺序：
    /// <list type="number">
    /// <item>原对象（若它本身就是 <c>object</c> 向量，直接复用）</item>
    /// <item><c>ObservableCollection&lt;string&gt;</c>——XAML <c>{x:Bind}</c> 到
    /// <c>ItemsSource</c> 的实际形态（模板仓库就是这么用的），最贴近原生路径</item>
    /// <item><c>ObservableCollection&lt;object&gt;</c>——<c>IObservableVector&lt;IInspectable&gt;</c></item>
    /// <item><c>List&lt;object&gt;</c>——<c>IVector&lt;IInspectable&gt;</c></item>
    /// <item><c>object[]</c></item>
    /// </list>
    /// </summary>
    private static IEnumerable<object> Candidates(IEnumerable source, List<object?> items)
    {
        if (source is IList<object?> or IList<object>)
        {
            yield return source;
        }

        if (items.TrueForAll(item => item is string))
        {
            var strings = new ObservableCollection<string>();
            foreach (var item in items)
            {
                strings.Add((string)item!);
            }

            yield return strings;
        }

        var observable = new ObservableCollection<object?>();
        foreach (var item in items)
        {
            observable.Add(item);
        }

        yield return observable;
        yield return items;
        yield return items.ToArray();
    }

    private static bool TryAssign(Action<object?> assign, object? value, string what, bool trace)
    {
        try
        {
            assign(value);
            return true;
        }
        catch (Exception ex)
        {
            if (trace)
            {
                Reactor.Uwp.Hosting.ReactorLog.Warn(Reactor.Uwp.Hosting.ReactorLogChannel.Interop, $"{what}: 赋值被拒绝（{value?.GetType().Name ?? "null"}）- {ex.Message}");
            }

            return false;
        }
    }

    private static object? ToVectorIfCollection(object value) =>
        value is IEnumerable enumerable && value is not string
            ? Boxed(enumerable)
            : null;

    private static ObservableCollection<object?> Boxed(IEnumerable source)
    {
        var observable = new ObservableCollection<object?>();
        foreach (var item in source)
        {
            observable.Add(item);
        }

        return observable;
    }
}
