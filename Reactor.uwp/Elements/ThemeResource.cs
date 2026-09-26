using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

/// <summary>
/// WinUI 主题资源查找（对齐官方 Microsoft.UI.Reactor.Elements.ThemeResource）。
/// </summary>
/// <remarks>
/// XAML 里的 <c>{ThemeResource TextFillColorSecondaryBrush}</c> 在纯代码里没有等价写法，
/// 官方的做法是从 <c>Application.Current.Resources</c> 里取（该字典已包含
/// <c>XamlControlsResources</c> 等合并字典）。找不到时返回默认值而不是抛异常——
/// 不同 WinUI 版本的资源键集合并不一致。
/// </remarks>
public static class ThemeResource
{
    public static Brush Brush(string key) =>
        Get<Brush>(key) ?? new SolidColorBrush(Windows.UI.Colors.Transparent);

    public static double Double(string key, double fallback = 0) => Get<double>(key, fallback);

    public static CornerRadius CornerRadius(string key) => Get<CornerRadius>(key);

    public static Thickness Thickness(string key) => Get<Thickness>(key);

    public static T? Get<T>(string key, T? defaultValue = default)
    {
        if (Application.Current?.Resources is { } resources &&
            resources.TryGetValue(key, out var value) &&
            value is T typed)
        {
            return typed;
        }

        return defaultValue;
    }
}
