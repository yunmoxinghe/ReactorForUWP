using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 声明式背景修饰符。应用到根元素后，指示宿主在承载根内容时
/// 设置对应背景（UWP 下映射到 <c>BackdropMaterial</c> 附加属性）。
/// </summary>
public static class BackdropExtensions
{
    /// <summary>设置宿主窗口的背景材质。</summary>
    public static T Backdrop<T>(this T el, BackdropKind kind) where T : Element
    {
        if (el is null) throw new global::System.ArgumentNullException(nameof(el));
        var existing = el.Modifiers ?? new ElementModifiers();
        var merged = existing with { Backdrop = kind };
        return (T)(el with { Modifiers = merged });
    }
}