using System;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 面包屑 <c>ItemTemplate</c> 那段 XAML 文本的回归测试。
/// </summary>
/// <remarks>
/// 锁的是两个已经踩过的坑：
/// <list type="number">
/// <item><b>喂 UIElement 当数据会崩</b>（0x800F1000 "Element is already the child of
/// another element"）。<c>ItemsCollection.Add(TextBlock)</c> 已经给它置了一个父，
/// 控件内部的 ItemsRepeater 再挂就是第二个父。所以向量里只能是<b>数据（字符串）</b>，
/// 外观一律走 <c>ItemTemplate</c>——这几条断言就是在守"只喂数据"这件事。</item>
/// <item><b>样式键拼进 XAML 前必须转义</b>。没转义时带引号 / <c>&amp;</c> 的键会
/// Load 出畸形 XAML，而且报错不带托管堆栈。</item>
/// </list>
/// 文本构造是纯字符串函数（<see cref="BreadcrumbTemplate"/>），所以不用开 App 就能断言。
/// </remarks>
internal static class BreadcrumbTemplateTests
{
    private const string Ns = "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">";

    public static void Run()
    {
        Program.Section("面包屑 ItemTemplate（XAML 文本）");

        // 1) 最简形态：只给文字与居中，不带样式 / 字号
        var plain = BreadcrumbTemplate.BuildXaml(null, null);
        Program.Check("根元素是带默认命名空间的 DataTemplate", plain.StartsWith(Ns, StringComparison.Ordinal));
        Program.Check(
            "条目 = TextBlock + {Binding} + 居中",
            plain.Contains("<TextBlock Text=\"{Binding}\" VerticalAlignment=\"Center\"", StringComparison.Ordinal));
        Program.Check("闭合正确", plain.EndsWith("</DataTemplate>", StringComparison.Ordinal));
        Program.Check(
            "没给样式/字号时不写这两个属性（用控件自带外观）",
            !plain.Contains("Style=", StringComparison.Ordinal) &&
            !plain.Contains("FontSize=", StringComparison.Ordinal));

        // 2) 样式：模板用的是 TitleTextBlockStyle（28px Semibold）
        var styled = BreadcrumbTemplate.BuildXaml("TitleTextBlockStyle", null);
        Program.Check(
            "样式写成 {StaticResource ...}",
            styled.Contains(" Style=\"{StaticResource TitleTextBlockStyle}\"", StringComparison.Ordinal));

        // 3) 字号：必须按 InvariantCulture（本地区域设置下 28.5 可能被写成 28,5）
        var sized = BreadcrumbTemplate.BuildXaml(null, 28);
        Program.Check(
            "字号按 InvariantCulture 写成 28",
            sized.Contains(" FontSize=\"28\"", StringComparison.Ordinal));

        var fractional = BreadcrumbTemplate.BuildXaml(null, 28.5);
        Program.Check(
            "小数分隔符恒为句点",
            fractional.Contains(" FontSize=\"28.5\"", StringComparison.Ordinal));

        // 4) 样式 + 字号同时给：两个属性都在（本地值压过样式，与依赖属性优先级一致）
        var both = BreadcrumbTemplate.BuildXaml("TitleTextBlockStyle", 28);
        Program.Check(
            "样式与字号同时出现",
            both.Contains("{StaticResource TitleTextBlockStyle}", StringComparison.Ordinal) &&
            both.Contains("FontSize=\"28\"", StringComparison.Ordinal));

        // 5) 转义：样式键里带 XML 特殊字符时不能把 XAML 拼坏
        var escaped = BreadcrumbTemplate.BuildXaml("A\"B&C<D>", null);
        Program.Check(
            "样式键里的 XML 特殊字符被转义",
            escaped.Contains("A&quot;B&amp;C&lt;D&gt;", StringComparison.Ordinal));
        Program.Check(
            "转义后不含裸露的引号（否则属性值提前闭合）",
            !escaped.Contains("A\"B", StringComparison.Ordinal));

        // 6) 空串的键视为"没给"，不写出空的属性
        Program.Check(
            "空样式键等同于不设样式",
            !BreadcrumbTemplate.BuildXaml(string.Empty, null).Contains("Style=", StringComparison.Ordinal));
    }
}
