// 面包屑 ItemTemplate 的 XAML 文本构造。
//
// 为什么单独一个文件、且不碰 Windows.UI.Xaml：
//   纯代码建不了 DataTemplate（UWP 没有 FrameworkElementFactory），只能
//   XamlReader.Load 一段文本——那段文本因此成了唯一的"真身"，写错就是运行时崩，
//   而且崩在 XAML 解析里、不带托管堆栈。把它做成纯字符串函数，
//   控制台测试就能直接断言（见 tests/Reactor.Core.Tests/BreadcrumbTemplateTests.cs），
//   不用开 App。所以这个文件里不要引入任何 WinRT 类型。

using System;
using System.Globalization;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 拼 <c>DataTemplate</c> 的 XAML 文本——对应模板 <c>MainPage.xaml</c> 里的
/// <c>&lt;BreadcrumbBar.ItemTemplate&gt;&lt;TextBlock Text="{x:Bind}"
/// VerticalAlignment="Center" Style="{StaticResource TitleTextBlockStyle}"/&gt;</c>。
/// </summary>
internal static class BreadcrumbTemplate
{
    /// <param name="styleKey">条目的命名样式键（写进 <c>Style="{StaticResource ...}"</c>），null = 不写。</param>
    /// <param name="fontSize">条目的字号（写成 XAML 属性 = 本地值，压过样式里的同名字号），null = 不写。</param>
    public static string BuildXaml(string? styleKey, double? fontSize)
    {
        var style = string.IsNullOrEmpty(styleKey)
            ? string.Empty
            : $" Style=\"{{StaticResource {Escape(styleKey!)}}}\"";

        var size = fontSize is { } value
            ? $" FontSize=\"{value.ToString(CultureInfo.InvariantCulture)}\""
            : string.Empty;

        // 根元素必须带默认命名空间，否则 XamlReader.Load 会抛。
        // 注意 Text="{Binding}"：{x:Bind} 要编译期生成代码，运行时 Load 的树用不了；
        // 条目的 DataContext 就是集合里那一项本身（字符串），所以 {Binding} 直接出文字。
        return "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
               $"<TextBlock Text=\"{{Binding}}\" VerticalAlignment=\"Center\"{style}{size}/>" +
               "</DataTemplate>";
    }

    /// <summary>
    /// 属性值的 XML 转义。样式键来自调用方，拼进 XAML 前必须过这一道，
    /// 否则键里带引号 / &amp; / 尖括号时 Load 出来的是畸形 XAML（且报错不带托管堆栈）。
    /// </summary>
    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
