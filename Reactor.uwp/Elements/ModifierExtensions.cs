using System;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 链式修饰符：把值记录到 <see cref="ElementModifiers"/>，
/// 由 Reconciler 在 mount/update 时统一应用。
/// 链式调用后返回 <see cref="Element"/>（record with 拷贝保留真实运行时类型）。
/// </summary>
public static class ModifierExtensions
{
    public static Element Margin(this Element element, double uniform) =>
        element with
        {
            Modifiers = Merge(element.Modifiers, m => m with { Margin = new Thickness(uniform) })
        };

    public static Element Margin(
        this Element element, double left, double top, double right, double bottom) =>
        element with
        {
            Modifiers = Merge(element.Modifiers,
                m => m with { Margin = new Thickness(left, top, right, bottom) })
        };

    public static Element Padding(this Element element, double uniform) =>
        element with
        {
            Modifiers = Merge(element.Modifiers, m => m with { Padding = new Thickness(uniform) })
        };

    public static Element Width(this Element element, double width) =>
        element with
        {
            Modifiers = Merge(element.Modifiers, m => m with { Width = width })
        };

    public static Element Height(this Element element, double height) =>
        element with
        {
            Modifiers = Merge(element.Modifiers, m => m with { Height = height })
        };

    public static Element Align(
        this Element element,
        HorizontalAlignment horizontal = HorizontalAlignment.Stretch,
        VerticalAlignment vertical = VerticalAlignment.Stretch) =>
        element with
        {
            Modifiers = Merge(element.Modifiers,
                m => m with { HorizontalAlignment = horizontal, VerticalAlignment = vertical })
        };

    public static Element FontSize(this Element element, double fontSize) =>
        element with
        {
            Modifiers = Merge(element.Modifiers, m => m with { FontSize = fontSize })
        };

    public static Element Foreground(this Element element, Brush brush) =>
        element with
        {
            Modifiers = Merge(element.Modifiers, m => m with { Foreground = brush })
        };

    public static Element Foreground(this Element element, Color color) =>
        element with
        {
            Modifiers = Merge(element.Modifiers, m => m with { ForegroundColor = color })
        };

    public static Element IsEnabled(this Element element, bool enabled) =>
        element with
        {
            Modifiers = Merge(element.Modifiers, m => m with { IsEnabled = enabled })
        };

    public static Element AutomationName(this Element element, string name) =>
        element with
        {
            Modifiers = Merge(element.Modifiers, m => m with { AutomationName = name })
        };

    private static ElementModifiers Merge(
        ElementModifiers? current, Func<ElementModifiers, ElementModifiers> change) =>
        change(current ?? new ElementModifiers());
}
