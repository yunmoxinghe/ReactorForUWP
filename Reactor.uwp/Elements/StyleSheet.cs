using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 纯代码构造 <see cref="Style"/> 的构建器（对齐 XAML 的
/// <c>&lt;Style BasedOn="…" TargetType="…"&gt;&lt;Setter …/&gt;&lt;/Style&gt;</c>）。
/// </summary>
/// <remarks>
/// 官方 Microsoft.UI.Reactor 只提供 <c>ApplyStyle(name)</c>（从应用资源字典取现成样式），
/// 没有纯代码定义新样式的能力；而 WinUI 设置页模板大量依赖
/// <c>BasedOn</c> + <c>Setter</c> 的派生样式（例如节标题 = BodyStrong + 上边距）。
/// 这里补上这一层，让 <c>StyleSheet.Define</c> 的产物与内置样式走同一条解析路径。
/// </remarks>
public sealed class StyleBuilder
{
    private readonly List<Setter> _setters = new();
    private Type? _targetType;
    private string? _basedOnKey;
    private Style? _basedOnStyle;

    /// <summary>指定目标类型（XAML 的 <c>TargetType</c>）。</summary>
    public StyleBuilder Target(Type targetType)
    {
        _targetType = targetType;
        return this;
    }

    /// <summary><see cref="Target(Type)"/> 的泛型便捷重载。</summary>
    public StyleBuilder Target<T>() where T : FrameworkElement => Target(typeof(T));

    /// <summary>继承另一个已定义的样式（按键名，解析时机推迟到构建时）。</summary>
    public StyleBuilder BasedOn(string parentKey)
    {
        _basedOnKey = parentKey;
        return this;
    }

    /// <summary>继承一个现成 <see cref="Style"/> 实例。</summary>
    public StyleBuilder BasedOn(Style style)
    {
        _basedOnStyle = style;
        return this;
    }

    /// <summary>添加一个 setter（XAML 的 <c>&lt;Setter Property="…" Value="…"/&gt;</c>）。</summary>
    public StyleBuilder Set(DependencyProperty property, object value)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        _setters.Add(new Setter(property, value));
        return this;
    }

    /// <summary>便捷：设置 <see cref="FrameworkElement.MarginProperty"/>。</summary>
    public StyleBuilder Margin(Thickness thickness) =>
        Set(FrameworkElement.MarginProperty, thickness);

    /// <summary>便捷：设置四边统一外边距。</summary>
    public StyleBuilder Margin(double uniform) =>
        Set(FrameworkElement.MarginProperty, new Thickness(uniform));

    /// <summary>便捷：设置 <see cref="FrameworkElement.MarginProperty"/>（左,上,右,下）。</summary>
    public StyleBuilder Margin(double left, double top, double right, double bottom) =>
        Set(FrameworkElement.MarginProperty, new Thickness(left, top, right, bottom));

    /// <summary>便捷：设置 <see cref="Control.PaddingProperty"/> / Panel 的内边距。</summary>
    public StyleBuilder Padding(Thickness thickness) =>
        Set(Control.PaddingProperty, thickness);

    /// <summary>便捷：设置字号（TextBlock 与 Control 共用依赖属性）。</summary>
    public StyleBuilder FontSize(double size) =>
        Set(Control.FontSizeProperty, size);

    /// <summary>便捷：设置前景色。</summary>
    public StyleBuilder Foreground(Windows.UI.Xaml.Media.Brush brush) =>
        Set(Control.ForegroundProperty, brush);

    /// <summary>构建最终 <see cref="Style"/>。重复调用返回同一实例（Style 一旦被使用即密封）。</summary>
    public Style Build()
    {
        var style = new Style(_targetType ?? typeof(FrameworkElement));

        if (_basedOnStyle is not null)
        {
            style.BasedOn = _basedOnStyle;
        }
        else if (_basedOnKey is { } key && StyleSheet.Resolve(key) is { } parent)
        {
            style.BasedOn = parent;
        }

        foreach (var setter in _setters)
        {
            style.Setters.Add(setter);
        }

        return style;
    }
}

/// <summary>
/// 命名样式表：自定义样式 + 应用资源字典的统一解析入口。
/// </summary>
/// <remarks>
/// 解析顺序：先查本表（<see cref="Define"/> 注册过的），再查
/// <c>Application.Current.Resources</c>（含 WinUI 的 <c>XamlControlsResources</c>
/// 与系统内置样式：<c>CaptionTextBlockStyle</c> / <c>BodyStrongTextBlockStyle</c> 等）。
/// <para>
/// <b>找不到键不抛异常</b>：官方 Reactor 早期版本用索引器取值，拼错样式名会让整个渲染
/// 崩在 mount 动作里。这里退化为"保持默认外观"，只打一条日志。
/// </para>
/// </remarks>
public static class StyleSheet
{
    private static readonly Dictionary<string, Lazy<Style>> Definitions = new(StringComparer.Ordinal);

    /// <summary>定义一个自定义命名样式。重复定义同一键以先定义者为准（对齐官方 first-wins）。</summary>
    /// <remarks>
    /// <b>必须是 first-wins，不能覆盖。</b>页面组件通常在 <c>Render()</c> 里调
    /// <c>DefineStyles()</c>，也就是<b>每次重渲染都会跑一遍</b>；若允许覆盖，
    /// 每轮都会产出一个新的 <see cref="Style"/> 实例，而协调器是用
    /// <c>ReferenceEquals</c> 判断样式有没有变的——于是每轮都判定"变了"并重新赋值，
    /// 后果是<b>控件模板被反复重建</b>：
    /// <list type="bullet">
    /// <item>切主题时模板重建与 XAML 的主题资源刷新叠在一起，直接卡死；</item>
    /// <item>模板里的 <c>ElementSoundMode</c> 绑定每次都从头绑一遍，
    ///       控件声音的表现与 XAML 版（样式只定义一次、模板稳定）对不上。</item>
    /// </list>
    /// 对应到 XAML：样式写在 <c>App.xaml</c> 里，整个进程就解析一次。
    /// </remarks>
    public static void Define(string key, Action<StyleBuilder> build)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("样式键不能为空", nameof(key));
        if (build is null) throw new ArgumentNullException(nameof(build));

        if (Definitions.ContainsKey(key))
        {
            return;
        }

        // Lazy：真正的构建推迟到首次使用，此时应用资源字典（BasedOn 目标）一定已就绪。
        Definitions[key] = new Lazy<Style>(() =>
        {
            var builder = new StyleBuilder();
            build(builder);
            return builder.Build();
        });
    }

    /// <summary>是否已定义过该键（含应用资源字典中的同名样式）。</summary>
    public static bool Contains(string key) => Definitions.ContainsKey(key);

    /// <summary>解析样式；找不到返回 null（不抛异常）。</summary>
    public static Style? Resolve(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (Definitions.TryGetValue(key, out var lazy))
        {
            // 构建失败（例如 BasedOn 目标缺失导致的异常）不应拖垮整次渲染。
            try
            {
                return lazy.Value;
            }
            catch (Exception ex)
            {
                global::Reactor.Uwp.Hosting.ReactorLog.Error(global::Reactor.Uwp.Hosting.ReactorLogChannel.Resource, $"自定义样式构建失败: {key} - {ex.Message}");
                return null;
            }
        }

        // 应用资源字典：ResourceDictionary 自己会遍历 MergedDictionaries，
        // 所以 XamlControlsResources 里的 WinUI 样式同样能命中。
        var resources = Application.Current?.Resources;
        if (resources is not null &&
            resources.TryGetValue(key, out var value) &&
            value is Style style)
        {
            return style;
        }

        global::Reactor.Uwp.Hosting.ReactorLog.Warn(global::Reactor.Uwp.Hosting.ReactorLogChannel.Resource, $"未找到命名样式: {key}");
        return null;
    }
}
