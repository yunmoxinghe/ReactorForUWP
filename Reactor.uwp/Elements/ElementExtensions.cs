using System;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 链式修饰符：把值记录到 <see cref="ElementModifiers"/>，
/// 由 Reconciler 在 mount/update 时统一应用。
/// </summary>
/// <remarks>
/// 对齐官方 Microsoft.UI.Reactor.ElementExtensions：
/// <list type="bullet">
/// <item>类名 ElementExtensions（原名 ModifierExtensions），且为 partial 便于拆分。</item>
/// <item>返回泛型 <c>T</c> 而不是 <see cref="Element"/>，链式之后仍保留具体元素类型，
/// 可以继续调用该元素类型专属的扩展方法。</item>
/// </list>
/// </remarks>
public static partial class ElementExtensions
{
    private static T Modify<T>(T el, ElementModifiers mods) where T : Element =>
        (T)(el with { Modifiers = el.Modifiers is null ? mods : el.Modifiers.Merge(mods) });

    private static T Set<T>(T el, Func<ElementModifiers, ElementModifiers> change) where T : Element =>
        (T)(el with { Modifiers = change(el.Modifiers ?? new ElementModifiers()) });

    // ── Margin / Padding ──────────────────────────────────────

    public static T Margin<T>(this T el, double uniform) where T : Element =>
        Set(el, m => m with { Margin = new Thickness(uniform) });

    public static T Margin<T>(this T el, double horizontal, double vertical) where T : Element =>
        Set(el, m => m with { Margin = new Thickness(horizontal, vertical, horizontal, vertical) });

    // 四参数重载全部带默认值：单参/双参调用时更精确的重载优先（官方同规则）。
    public static T Margin<T>(
        this T el, double left = 0, double top = 0, double right = 0, double bottom = 0)
        where T : Element =>
        Set(el, m => m with { Margin = new Thickness(left, top, right, bottom) });

    public static T Margin<T>(this T el, Thickness thickness) where T : Element =>
        Set(el, m => m with { Margin = thickness });

    public static T Padding<T>(this T el, double uniform) where T : Element =>
        Set(el, m => m with { Padding = new Thickness(uniform) });

    public static T Padding<T>(this T el, double horizontal, double vertical) where T : Element =>
        Set(el, m => m with { Padding = new Thickness(horizontal, vertical, horizontal, vertical) });

    public static T Padding<T>(
        this T el, double left = 0, double top = 0, double right = 0, double bottom = 0)
        where T : Element =>
        Set(el, m => m with { Padding = new Thickness(left, top, right, bottom) });

    public static T Padding<T>(this T el, Thickness thickness) where T : Element =>
        Set(el, m => m with { Padding = thickness });

    // ── 尺寸 ──────────────────────────────────────────────────

    public static T Width<T>(this T el, double width) where T : Element =>
        Set(el, m => m with { Width = width });

    public static T Height<T>(this T el, double height) where T : Element =>
        Set(el, m => m with { Height = height });

    public static T Size<T>(this T el, double width, double height) where T : Element =>
        Set(el, m => m with { Width = width, Height = height });

    public static T MinWidth<T>(this T el, double width) where T : Element =>
        Set(el, m => m with { MinWidth = width });

    public static T MinHeight<T>(this T el, double height) where T : Element =>
        Set(el, m => m with { MinHeight = height });

    public static T MaxWidth<T>(this T el, double width) where T : Element =>
        Set(el, m => m with { MaxWidth = width });

    public static T MaxHeight<T>(this T el, double height) where T : Element =>
        Set(el, m => m with { MaxHeight = height });

    // ── 对齐 ──────────────────────────────────────────────────

    public static T HAlign<T>(this T el, HorizontalAlignment alignment) where T : Element =>
        Set(el, m => m with { HorizontalAlignment = alignment });

    public static T VAlign<T>(this T el, VerticalAlignment alignment) where T : Element =>
        Set(el, m => m with { VerticalAlignment = alignment });

    public static T Align<T>(
        this T el,
        HorizontalAlignment horizontal = HorizontalAlignment.Stretch,
        VerticalAlignment vertical = VerticalAlignment.Stretch) where T : Element =>
        Set(el, m => m with { HorizontalAlignment = horizontal, VerticalAlignment = vertical });

    public static T Center<T>(this T el) where T : Element =>
        Set(el, m => m with
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

    // ── 外观 ──────────────────────────────────────────────────

    public static T Opacity<T>(this T el, double opacity) where T : Element =>
        Set(el, m => m with { Opacity = opacity });

    public static T IsVisible<T>(this T el, bool isVisible = true) where T : Element =>
        Set(el, m => m with { IsVisible = isVisible });

    public static T Background<T>(this T el, Brush brush) where T : Element =>
        Set(el, m => m with { Background = brush });

    public static T Background<T>(this T el, Color color) where T : Element =>
        Set(el, m => m with { BackgroundColor = color });

    public static T BorderBrush<T>(this T el, Brush brush) where T : Element =>
        Set(el, m => m with { BorderBrush = brush });

    public static T BorderThickness<T>(this T el, double uniform) where T : Element =>
        Set(el, m => m with { BorderThickness = new Thickness(uniform) });

    public static T BorderThickness<T>(this T el, Thickness thickness) where T : Element =>
        Set(el, m => m with { BorderThickness = thickness });

    public static T WithBorder<T>(this T el, Brush brush, double thickness = 1) where T : Element =>
        Set(el, m => m with { BorderBrush = brush, BorderThickness = new Thickness(thickness) });

    // ── 字体 ──────────────────────────────────────────────────

    public static T FontSize<T>(this T el, double size) where T : Element =>
        Set(el, m => m with { FontSize = size });

    public static T Foreground<T>(this T el, Brush brush) where T : Element =>
        Set(el, m => m with { Foreground = brush });

    public static T Foreground<T>(this T el, Color color) where T : Element =>
        Set(el, m => m with { ForegroundColor = color });

    public static T IsEnabled<T>(this T el, bool enabled = true) where T : Element =>
        Set(el, m => m with { IsEnabled = enabled });

    public static T Disabled<T>(this T el, bool disabled = true) where T : Element =>
        Set(el, m => m with { IsEnabled = !disabled });

    // ── Key / 无障碍 / 提示 ───────────────────────────────────

    public static T WithKey<T>(this T el, string key) where T : Element =>
        (T)(el with { Key = key });

    public static T AutomationName<T>(this T el, string name) where T : Element =>
        Set(el, m => m with { AutomationName = name });

    public static T AutomationId<T>(this T el, string id) where T : Element =>
        Set(el, m => m with { AutomationId = id });

    public static T ToolTip<T>(this T el, string tip) where T : Element =>
        Set(el, m => m with { ToolTip = tip });
}
