using System;
using System.Collections.Generic;
using Windows.UI;
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
/// <para>
/// <b><see cref="Brush"/> 给的是活引用，不是快照。</b>这是"实现套 WinUI 2"的
/// 一条硬要求：XAML 的 <c>{ThemeResource}</c> 会跟着主题走，代码里如果只在
/// 渲染那一刻把颜色抄出来，切主题就永远停在旧值上——和"云母不跟随应用主题"
/// 是同一类隐患，只是表现更隐蔽（画的是旧主题的前景色）。做法见
/// <see cref="Brush"/> 的说明：同一个 key 复用同一个 <c>SolidColorBrush</c>，
/// 主题变化时由 <see cref="RefreshLive"/> 统一改它的 <c>Color</c>，
/// 所有拿着这个画笔的控件一起变。
/// </para>
/// </remarks>
public static class ThemeResource
{
    private static readonly object Gate = new();

    /// <summary>
    /// 已发放的活引用画笔：资源键 → 共享实例。
    /// 只有 <c>SolidColorBrush</c> 能登记（它有唯一的 <c>Color</c> 可原地改）。
    /// </summary>
    private static readonly Dictionary<string, SolidColorBrush> Live = new();

    /// <summary>
    /// 取一个<b>随主题更新</b>的画笔——语义对齐 XAML 的
    /// <c>{ThemeResource key}</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么不能直接把资源字典里那个对象返回去：主题切换时 XAML 侧会<b>重新求值</b>
    /// 并换一个实例（或换字典条目），而代码里持有的引用不会跟着换——那还是快照。
    /// 这里改为自己持有一个实例、按 key 共享，主题变化时只改它的 <c>Color</c>：
    /// <c>SolidColorBrush</c> 是依赖对象，改颜色会通知所有引用者重绘，
    /// 于是"一个 key 一个实例、改一次全体生效"。
    /// </para>
    /// <para>
    /// 资源不是纯色（<c>AcrylicBrush</c> / <c>RevealBrush</c> / 渐变）时没有可原地改的
    /// 单一颜色，只能把资源本体返回去（XAML 里多个控件拿到的也是同一个实例，行为一致）。
    /// 这类画笔的着色由系统自己按主题算，不需要我们刷。
    /// </para>
    /// <para>
    /// 拿到的画笔<b>别自己改 Color</b>：下一次主题刷新会把它覆盖回去。
    /// 要画别的颜色就自己 <c>new SolidColorBrush(...)</c>。
    /// </para>
    /// </remarks>
    public static Brush Brush(string key)
    {
        if (ResolveColor(key) is not { } color)
        {
            // 资源不存在，或不是纯色：给资源本体（共享实例，与 XAML 同行为）；
            // 都没有就退回透明。
            return Get<Brush>(key) ?? new SolidColorBrush(Colors.Transparent);
        }

        lock (Gate)
        {
            if (Live.TryGetValue(key, out var live))
            {
                return live;
            }

            live = new SolidColorBrush(color);
            Live[key] = live;
            return live;
        }
    }

    /// <summary>
    /// 主题变化后刷新所有活引用画笔的颜色。
    /// 由宿主在根元素 <c>ActualThemeChanged</c> 里调用（见 <c>ReactorHost</c>）。
    /// </summary>
    /// <remarks>
    /// 只刷新<b>已经发出去</b>的那些 key：没被取过的资源不存在"旧引用要更新"的问题。
    /// 期间 <see cref="Brush"/> 可能从别的线程并发取，所以整段在锁里跑。
    /// </remarks>
    public static void RefreshLive()
    {
        lock (Gate)
        {
            foreach (var pair in Live)
            {
                if (ResolveColor(pair.Key) is { } color && pair.Value.Color != color)
                {
                    pair.Value.Color = color;
                }
            }
        }
    }

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

    /// <summary>
    /// 把一个资源键解析成颜色：先按画笔（资源里通常是 <c>SolidColorBrush</c>），
    /// 再按裸 <c>Color</c>。解析不到返回 null。
    /// </summary>
    private static Color? ResolveColor(string key)
    {
        if (Get<Brush>(key) is SolidColorBrush brush)
        {
            return brush.Color;
        }

        if (Application.Current?.Resources is { } resources &&
            resources.TryGetValue(key, out var value) &&
            value is Color color)
        {
            return color;
        }

        return null;
    }
}
