using System.Runtime.CompilerServices;
using System.Threading;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 给控件发一个<b>稳定且可读</b>的编号，专供日志用。
/// </summary>
/// <remarks>
/// <b>为什么不用 <c>RuntimeHelpers.GetHashCode</c>。</b>它在 AOT / 压缩 GC 下
/// 会随堆移动变化：同一轮日志里同一个控件可能前后显示两个号，
/// 于是"控件被重建了"这种结论纯属量具造的假象（这个跟头栽过一次）。
/// 这里用 <see cref="ConditionalWeakTable{TKey,TValue}"/>：
/// <list type="bullet">
///   <item><b>弱键</b>：控件被回收后条目自动消失，不会把整棵可视树吊在静态表里；</item>
///   <item><b>引用相等</b>：同一个实例永远拿到同一个号，不依赖 <c>GetHashCode</c>；</item>
///   <item><b>号只增不减</b>：日志里"#3 → #7"就是真的换了实例。</item>
/// </list>
/// <para>
/// <b>为什么日志必须有它。</b>页面上往往同时存在多个同类控件
/// （诊断页里就摆了两组 <c>RadioButtons</c>），没有编号时
/// <c>RadioButtons → 用户回调 SelectedIndex=2</c> 根本分不清是哪一组发的，
/// 只能靠猜——而猜正是这一路反复返工的根源。
/// </para>
/// </remarks>
internal static class CtlId
{
    private static readonly ConditionalWeakTable<object, Box> Ids = new();

    private static int _next;

    private sealed class Box
    {
        public int No;
    }

    /// <summary>该控件的日志编号（首次见到时分配）。</summary>
    public static int Of(object control) =>
        Ids.GetValue(control, static _ => new Box { No = Interlocked.Increment(ref _next) }).No;

    /// <summary>带井号的写法，直接拼进日志消息。</summary>
    public static string Tag(object control) => $"#{Of(control)}";
}
