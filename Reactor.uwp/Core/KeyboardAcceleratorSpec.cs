using System;
using Windows.System;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// 一个键盘快捷键的声明式描述，对应 XAML 的
/// <c>&lt;KeyboardAccelerator Key="S" Modifiers="Control"/&gt;</c>。
/// </summary>
/// <param name="Key">主键（XAML 的 <c>Key</c>）。</param>
/// <param name="Modifiers">修饰键组合（XAML 的 <c>Modifiers</c>），可位或。</param>
/// <param name="OnInvoked">
/// 触发回调。声明式里拿不到事件参数，所以只给 <c>Action</c>——需要
/// <c>KeyboardAcceleratorInvokedEventArgs</c>（比如要 <c>e.Handled = false</c>
/// 让事件继续冒泡）时用 <c>Native()</c> 逃生舱。
/// </param>
/// <param name="IsEnabled">是否启用（XAML 的 <c>IsEnabled</c>）。</param>
/// <remarks>
/// <b>为什么要有"结构相等"这回事。</b>组件每次重渲染通常都会 new 一份声明
/// （lambda 也是新的），如果按 record 默认的引用相等去比对，就会每轮把
/// <c>KeyboardAccelerators</c> 集合拆了重建、每轮重新挂事件——快捷键集合很小，
/// 但那是无谓的抖动。所以集合同步只看 <see cref="SameDefinition"/>（Key +
/// Modifiers + IsEnabled），<b>回调单独更新</b>：换委托不重建集合，
/// 这样闭包里捕获的最新 state 也能生效。
/// </remarks>
public sealed record KeyboardAcceleratorSpec(
    VirtualKey Key,
    VirtualKeyModifiers Modifiers = VirtualKeyModifiers.None,
    Action? OnInvoked = null,
    bool IsEnabled = true)
{
    /// <summary>
    /// 定义是否相同（<b>不含</b>回调）。相同就复用已有的
    /// <see cref="Windows.UI.Xaml.Input.KeyboardAccelerator"/> 实例。
    /// </summary>
    public bool SameDefinition(KeyboardAcceleratorSpec other) =>
        Key == other.Key && Modifiers == other.Modifiers && IsEnabled == other.IsEnabled;
}
