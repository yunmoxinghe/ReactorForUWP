using System;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 把一棵<b>真实</b>的原生控件树挂进 Reactor 元素树（逃生舱）。
/// </summary>
/// <remarks>
/// 框架本身是"映射"路线：每个元素都对应一个真实 WinUI 2 / UWP 控件。
/// 但 Reactor 的元素库不可能包住所有控件（比如 <c>ItemsRepeater</c> 就还没包），
/// 压测 / 实验室这类场景又要直接操作原生控件。这个元素就是那道门：
/// 由调用方自己 new 控件，Reactor 只负责把它放进布局、并在卸载时收走。
/// <para>
/// <b>谁来更新它</b>：Reactor 不参与这棵子树的内部更新——控件由
/// <see cref="Factory"/> 造出来之后就归调用方管（事件、数据、滚动都自己来）。
/// Reactor 只认 <see cref="Token"/>：它变了才丢弃旧控件重新造一个。
/// 所以<b>不要</b>在 Render 里每次 new 一个 lambda 当 Factory（那样每轮重渲染
/// 都会重建整棵子树）；把委托缓存起来，需要重建时改 Token。
/// </para>
/// </remarks>
public sealed record NativeElement(Func<UIElement> Factory) : Element
{
    /// <summary>
    /// 重建令牌：与上一次不等就丢弃旧控件、重新调 <see cref="Factory"/>。
    /// null 表示"永不重建"（默认）。想强制重建时给个自增的 int / Guid 即可。
    /// </summary>
    public object? Token { get; init; }

    /// <summary>控件被替换或卸载时的清理回调（解事件、停定时器等）。</summary>
    public Action<UIElement>? OnDispose { get; init; }
}

/// <summary><see cref="NativeElement"/> 的工厂方法（<see cref="Factories"/> 的 partial 延续）。</summary>
public static partial class Factories
{
    /// <summary>
    /// 宿主一个原生控件：把 <paramref name="factory"/> 造出来的控件放进 Reactor 布局。
    /// </summary>
    /// <param name="factory">创建控件的委托；<b>引用必须稳定</b>（缓存起来，别每次 new）。</param>
    /// <param name="token">重建令牌，变化时丢弃旧控件重新创建。</param>
    /// <param name="onDispose">被替换 / 卸载时的清理回调。</param>
    public static NativeElement Native(
        Func<UIElement> factory,
        object? token = null,
        Action<UIElement>? onDispose = null) =>
        new(factory) { Token = token, OnDispose = onDispose };
}
