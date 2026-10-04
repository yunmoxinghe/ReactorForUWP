using System;
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

/// <summary>
/// "纯代码 <c>new</c> 出来的控件" 与 "XamlReader 解析出来的控件" 的内部状态对照。
/// </summary>
/// <remarks>
/// 用来回答一个具体问题：<b>纯 C# 建的控件树，和 XAML 解析器建的控件树，
/// 内部行为是不是一回事</b>（控件声音对不上，怀疑就出在这里）。
/// <para>
/// 做法是同一个类型造两份：一份走 <c>XamlReader.Load</c>（XAML 解析路径），
/// 一份走 <c>new</c>（本框架的路径），都塞进同一个真实容器里走完整布局，
/// 然后把<b>默认样式落到控件上的那些值</b>逐个读出来对比——
/// 样式、模板有没有应用、默认边距/内边距、前景背景。
/// 只要这几项一致，"new 出来的控件"就没有丢掉 XAML 的初始化。
/// </para>
/// 结果同时写进 <c>LocalState\reactor-startup.log</c>（<c>[xamldiff]</c> 段）。
/// </remarks>
public sealed class XamlDiffProbe : Component
{
    private const string Ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    public override Element Render()
    {
        var (report, setReport) = UseState("等待布局…");
        var hostRef = UseRef<StackPanel?>(null);
        hostRef.Current ??= new StackPanel();

        UseEffect(() =>
        {
            var host = hostRef.Current!;

            void OnLoaded(object sender, RoutedEventArgs e)
            {
                host.Loaded -= OnLoaded;

                var sb = new StringBuilder();
                Compare<Button>(sb, host, $"<Button xmlns=\"{Ns}\" Content=\"X\"/>",
                    () => new Button { Content = "X" });
                Compare<ToggleSwitch>(sb, host, $"<ToggleSwitch xmlns=\"{Ns}\"/>",
                    () => new ToggleSwitch());
                Compare<ComboBox>(sb, host, $"<ComboBox xmlns=\"{Ns}\"/>",
                    () => new ComboBox());
                Compare<RadioButton>(sb, host, $"<RadioButton xmlns=\"{Ns}\" Content=\"X\"/>",
                    () => new RadioButton { Content = "X" });

                var text = sb.ToString();
                ReactorApplication.Trace("[xamldiff]\n" + text);
                setReport(text);
            }

            host.Loaded += OnLoaded;
            return () => host.Loaded -= OnLoaded;
        });

        return ScrollViewer(
            VStack(8,
                TextBlock("代码 new vs XamlReader.Load（看日志的 [xamldiff] 段）").BodyStrong(),
                Native(() => hostRef.Current!),
                TextBlock(report).Wrap()));
    }

    private static void Compare<T>(
        StringBuilder sb, Panel host, string xaml, Func<T> factory)
        where T : FrameworkElement
    {
        sb.AppendLine($"── {typeof(T).Name} ──");

        T fromXaml;
        try
        {
            fromXaml = (T)XamlReader.Load(xaml);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  XamlReader.Load 失败（AOT 下可能不可用）: {ex.Message}");
            return;
        }

        var fromCode = factory();

        // 都放进真实容器：控件模板要进可视树并走一次布局才会应用。
        host.Children.Add(fromXaml);
        host.Children.Add(fromCode);

        Append(sb, "  Style", Describe(fromXaml.Style), Describe(fromCode.Style));
        Append(sb, "  Margin", fromXaml.Margin, fromCode.Margin);
        Append(sb, "  Padding", ReadPadding(fromXaml), ReadPadding(fromCode));
        Append(sb, "  Foreground", Describe(fromXaml.GetValue(Control.ForegroundProperty)),
            Describe(fromCode.GetValue(Control.ForegroundProperty)));
        Append(sb, "  模板已应用", VisualTreeHelper.GetChildrenCount(fromXaml) > 0,
            VisualTreeHelper.GetChildrenCount(fromCode) > 0);
        Append(sb, "  模板子节点数", VisualTreeHelper.GetChildrenCount(fromXaml),
            VisualTreeHelper.GetChildrenCount(fromCode));
    }

    private static void Append(StringBuilder sb, string label, object? a, object? b)
    {
        var same = Equals(a, b);
        sb.AppendLine($"  {label,-12} xaml={a,-28} code={b,-28} {(same ? "一致" : "★不同")}");
    }

    private static string? ReadPadding(FrameworkElement el) =>
        el.GetValue(Control.PaddingProperty)?.ToString();

    private static string Describe(object? value) =>
        value is null ? "null" : $"{value.GetType().Name}";
}
