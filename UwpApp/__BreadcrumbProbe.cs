using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

/// <summary>
/// 判定：WinUI 2 的原生 <c>BreadcrumbBar</c> 到底能不能喂集合。
/// </summary>
/// <remarks>
/// 框架里 <c>BreadcrumbBarHandler</c> 是自绘的 <c>StackPanel</c> + "›"，理由是
/// <c>WinRtValue.cs</c> 里记的实测：<c>List&lt;string&gt;</c> 会被投影成
/// <c>IVector&lt;HSTRING&gt;</c>（"not a supported vector"），托管
/// <c>ObservableCollection&lt;object&gt;</c> 赋值能过但 CsWinRT 建的 CCW 过不了
/// 布局期的 <c>ItemsSourceView</c>（COMException → fast-fail 0xC000027B）。
/// 那条记录的出路是"改用控件自身的 <c>Items</c>"——但 <c>BreadcrumbBar</c>
/// 是 <c>Control</c> 派生（模板里挂 <c>ItemsRepeater</c>），<b>它没有 Items</b>，
/// 所以当时那句结论对它不成立，于是改成了自绘。
/// <para>
/// 这里试的是当时没试过的一条路：<b>借一个真原生向量当 ItemsSource</b>。
/// <c>ItemsControl.Items</c> / <c>ListView.Items</c> 是 WinRT 自己的
/// <c>IObservableVector&lt;IInspectable&gt;</c>（不是托管 CCW），理论上
/// <c>ItemsSourceView</c> 应该认。认了就能把 handler 换回真控件。
/// </para>
/// <para>
/// <b>结论（已跑，OS 26200 / WinUI 2.8.7）：mode=3 通过</b>——赋值 OK、布局 OK、
/// 生成 4 个 <c>BreadcrumbBarItem</c>（3 条 + 官方模板自带的 ellipsis 项）。
/// <c>BreadcrumbBarHandler</c> 据此换回了真控件。本文件留着当回归依据：
/// 将来 <c>ItemsSourceView</c> 的行为变了，再跑一次就能确认。
/// </para>
/// <para>
/// <b>第二轮：条目字号对不上。</b>原因是 <c>BreadcrumbBarItem</c> 的默认样式硬设了
/// <c>FontSize = {ThemeResource BreadcrumbBarItemThemeFontSize}</c>，继承链到条目就断，
/// 在 <c>BreadcrumbBar</c> 上设多大字号都不生效；而 <c>BreadcrumbBar</c>
/// <b>没有</b> <c>ItemStyle</c> 属性（winmd 里查过，只有 <c>ItemTemplate</c> /
/// <c>ItemTemplateSelector</c>），官方给的改条目外观的口子就是 <c>ItemTemplate</c>。
/// </para>
/// <para>
/// <b>第二轮的第一版实现是错的（实测必崩，留作反面教材）</b>：当时为了让每条都带上
/// <c>TitleTextBlockStyle</c>，把喂进向量的每项换成了 <c>TextBlock</c>（mode=5）。
/// 结果第一次布局就 <c>COMException 0x800F1000 —— "Element is already the child of
/// another element."</c>：把 <c>UIElement</c> 放进 <c>ItemsControl.Items</c> 已经给它
/// 置了父，<c>ItemsRepeater</c> 再往里挂就是第二个父。<b>UWP 里 UIElement 不能当
/// "数据"，它天生要占一个树上的位置</b>——这条路上每一行看起来都合理，实测一行都走不通。
/// 由此得到本轮的硬结论：面包屑条目<b>只能喂数据</b>，外观必须走
/// <c>ItemTemplate</c>。
/// </para>
/// <para>
/// 于是第三轮试的就是唯一剩下的官方口子：<c>ItemTemplate</c>。纯代码没有
/// <c>FrameworkElementFactory</c>，只能 <c>XamlReader.Load</c> 一段 DataTemplate
/// （mode=6/7）——这也是现行 handler 的做法，本轮就是给它取证。
/// </para>
/// <para>
/// 模式写在 <c>LocalState\breadcrumb-mode.txt</c>（一个数字）：
/// 0 不喂（基线）/ 1 List&lt;string&gt; / 2 ObservableCollection&lt;object&gt; /
/// 3 ItemsControl.Items(字符串) / 4 ListView.Items /
/// 5 ItemsControl.Items(每项=带样式的 TextBlock —— <b>已证必崩，勿用</b>) /
/// <b>6 字符串 + XamlReader.Load 的 DataTemplate（字面字号）</b> /
/// <b>7 字符串 + XamlReader.Load 的 DataTemplate（StaticResource TitleTextBlockStyle）</b>。
/// <b>一次只跑一种</b>：失败的那些是 fast-fail，进程直接挂，没机会写日志。
/// </para>
/// </remarks>
public sealed class BreadcrumbProbe : Component
{
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
                Run(host, text =>
                {
                    ReactorApplication.Trace("[breadcrumb]\n" + text);
                    setReport(text);
                });
            }

            host.Loaded += OnLoaded;
            return () => host.Loaded -= OnLoaded;
        });

        return ScrollViewer(
            VStack(8,
                TextBlock("原生 BreadcrumbBar × ItemsSource 喂法探针（看日志 [breadcrumb] 段）")
                    .BodyStrong(),
                Native(() => hostRef.Current!),
                TextBlock(report).Wrap()));
    }

    private static void Run(Panel host, Action<string> done)
    {
        var sb = new StringBuilder();
        var mode = ReadMode();
        sb.AppendLine($"mode = {mode}（改 LocalState\\breadcrumb-mode.txt 切换，一次只跑一种）");

        var bar = new Microsoft.UI.Xaml.Controls.BreadcrumbBar();
        bar.ItemClicked += (_, _) => { };

        // mode 6/7：条目外观走官方的 ItemTemplate。必须在喂 ItemsSource 之前给。
        if (ApplyItemTemplate(mode, bar, sb))
        {
            done(sb.ToString());
            return;
        }

        // 先赋值再进树：和 handler 的顺序一致。
        var source = BuildSource(mode, out var desc);
        sb.AppendLine($"喂法：{desc}");

        try
        {
            bar.ItemsSource = source;
            sb.AppendLine("赋值：OK（注意——赋值不报错不代表能布局）");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"赋值抛异常：{ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
            done(sb.ToString());
            return;
        }

        ReactorApplication.Trace($"[breadcrumb] 进布局前：mode={mode} 喂法={desc}");

        host.Children.Add(bar);

        // 关键：强制同步布局。控件第一次参与 Measure 时才会真正建 ItemsSourceView，
        // 崩也是崩在这里——而且 fast-fail 不带托管堆栈、UnhandledException 拦不住。
        try
        {
            host.UpdateLayout();
            sb.AppendLine("布局：OK");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"布局抛异常：{ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
            done(sb.ToString());
            return;
        }

        try
        {
            sb.AppendLine($"模板子节点数 = {VisualTreeHelper.GetChildrenCount(bar)}");
            var itemCount = CountDescendants<Microsoft.UI.Xaml.Controls.BreadcrumbBarItem>(bar);
            sb.AppendLine($"实际生成的 BreadcrumbBarItem 数 = {itemCount}");
            sb.AppendLine(itemCount > 0
                ? "结论：★这个喂法可用，原生控件能真映射"
                : "结论：没崩但也没生成条目（等于空壳）");

            // 条目文字的真实字号——"面包屑大小不对"就查这几行。
            AppendResolvedStyle(sb, "TitleTextBlockStyle");
            AppendItemTextProperties(sb, bar);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取结果时抛异常：{ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
        }

        done(sb.ToString());
    }

    /// <summary>
    /// <c>ItemTemplate</c> 的两种候选 XAML。UWP 纯代码没有
    /// <c>FrameworkElementFactory</c>，<c>XamlReader.Load</c> 是唯一能拿到
    /// <c>DataTemplate</c> 的路。
    /// </summary>
    /// <remarks>
    /// <c>{x:Bind}</c> 编译期要用，这里用 <c>{Binding}</c>——条目的 DataContext
    /// 就是集合里的那一项本身（字符串），所以 <c>Text="{Binding}"</c> 直接出文字。
    /// mode 7 想验证的是：Load 出来的独立树能不能解析 <c>StaticResource</c>
    /// （它是 <b>没有父链</b> 的，理论上只能查到 Application 级资源）。
    /// </remarks>
    private const string TemplateCommon =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">";

    private const string TemplateWithLiteralFontSize =
        TemplateCommon +
        "<TextBlock Text=\"{Binding}\" VerticalAlignment=\"Center\" " +
        "FontSize=\"28\" FontWeight=\"SemiBold\"/></DataTemplate>";

    private const string TemplateWithStaticResource =
        TemplateCommon +
        "<TextBlock Text=\"{Binding}\" VerticalAlignment=\"Center\" " +
        "Style=\"{StaticResource TitleTextBlockStyle}\"/></DataTemplate>";

    private static object? BuildSource(int mode, out string desc)
    {
        switch (mode)
        {
            case 1:
                desc = "List<string>（预期：not a supported vector）";
                return new List<string> { "A", "B", "C" };

            case 2:
                desc = "ObservableCollection<object>（预期：布局期 fast-fail 0xC000027B）";
                return new ObservableCollection<object> { "A", "B", "C" };

            case 3:
                desc = "ItemsControl.Items —— 真原生 IObservableVector（本次要判定的）";
                var ic = new ItemsControl();
                ic.Items.Add("A");
                ic.Items.Add("B");
                ic.Items.Add("C");
                return ic.Items;

            case 4:
                desc = "ListView.Items —— 真原生向量（对照）";
                var lv = new ListView();
                lv.Items.Add("A");
                lv.Items.Add("B");
                lv.Items.Add("C");
                return lv.Items;

            case 5:
                // handler 现在的做法：ItemTemplate 语义——喂进去的就是带样式的 TextBlock。
                desc = "ItemsControl.Items + 每项=带 TitleTextBlockStyle 的 TextBlock（handler 现行做法）";
                var host = new ItemsControl();
                foreach (var text in new[] { "A", "B", "C" })
                {
                    host.Items.Add(CreateItemText(text));
                }

                return host.Items;

            // mode 6/7：外观由 ItemTemplate 负责，喂的必须是数据（字符串），不是 UIElement。
            case 6:
            case 7:
                desc = mode == 6
                    ? "ItemsControl.Items(字符串) + ItemTemplate(XamlReader.Load，字面字号)"
                    : "ItemsControl.Items(字符串) + ItemTemplate(XamlReader.Load，StaticResource)";
                var textHost = new ItemsControl();
                textHost.Items.Add("A");
                textHost.Items.Add("B");
                textHost.Items.Add("C");
                return textHost.Items;

            default:
                desc = "不喂（基线：只验证空控件能不能布局）";
                return null;
        }
    }

    /// <summary>
    /// mode 6/7：给 <c>ItemTemplate</c> 挂一段用 <c>XamlReader.Load</c> 编出来的
    /// <c>DataTemplate</c>——纯代码里唯一的官方口子。
    /// </summary>
    /// <returns>true 表示这一步就失败了，调用方直接收尾。</returns>
    private static bool ApplyItemTemplate(
        int mode, Microsoft.UI.Xaml.Controls.BreadcrumbBar bar, StringBuilder sb)
    {
        if (mode is not (6 or 7))
        {
            return false;
        }

        var xaml = mode == 6 ? TemplateWithLiteralFontSize : TemplateWithStaticResource;
        sb.AppendLine(
            mode == 6
                ? "喂法：字符串 + ItemTemplate(XamlReader.Load，字面字号 28/SemiBold)"
                : "喂法：字符串 + ItemTemplate(XamlReader.Load，StaticResource TitleTextBlockStyle)");

        try
        {
            bar.ItemTemplate = (Windows.UI.Xaml.DataTemplate)Windows.UI.Xaml.Markup.XamlReader.Load(xaml);
            sb.AppendLine("XamlReader.Load(DataTemplate)：OK");
            return false;
        }
        catch (Exception ex)
        {
            sb.AppendLine(
                $"★XamlReader.Load 失败：{ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// 照 handler 的现行做法（<b>已证必崩</b>）造条目视图：带 <c>TitleTextBlockStyle</c>
    /// 的 TextBlock。留着当反面教材，别再照抄。
    /// </summary>
    /// <remarks>
    /// 这里的类型必须写全限定：<c>using static Factories</c> 把 <c>TextBlock</c>
    /// 这个名字引成了工厂方法。
    /// </remarks>
    private static Windows.UI.Xaml.Controls.TextBlock CreateItemText(string text)
    {
        var block = new Windows.UI.Xaml.Controls.TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (ResolveStyle("TitleTextBlockStyle") is { } style)
        {
            block.Style = style;
        }

        return block;
    }

    private static Style? ResolveStyle(string key) =>
        StyleSheet.Resolve(key) ?? ThemeResource.Get<Style>(key);

    /// <summary>
    /// 打印样式到底有没有解析出来、里面的字号字重是多少——
    /// 解析不到的话条目会用 <c>BreadcrumbBarItemThemeFontSize</c>，看着就是"大小不对"。
    /// </summary>
    private static void AppendResolvedStyle(StringBuilder sb, string key)
    {
        // 硬证据：UWP 里 TextBlock 与 Control 各自注册了字体那几个依赖属性，
        // 拿 <c>Control.FontSizeProperty</c> 去比对 TextBlock 样式的 setter
        // 永远比对不上——这就是先前"取不出 28px"的根因。
        sb.AppendLine(
            "FontSize 依赖属性是否同一个对象 " +
            $"(TextBlock vs Control) = " +
            $"{ReferenceEquals(Windows.UI.Xaml.Controls.TextBlock.FontSizeProperty, Control.FontSizeProperty)}");

        var style = ResolveStyle(key);
        if (style is null)
        {
            sb.AppendLine($"★资源里没有 {key}——条目会退回默认字号");
            return;
        }

        var settings = new List<string>();
        foreach (var setterBase in style.Setters)
        {
            var property = (setterBase as Setter)?.Property;
            if (property is null) continue;

            if (property == Windows.UI.Xaml.Controls.TextBlock.FontSizeProperty ||
                property == Control.FontSizeProperty ||
                property == Windows.UI.Xaml.Controls.TextBlock.FontWeightProperty ||
                property == Control.FontWeightProperty)
            {
                settings.Add($"{(setterBase as Setter)!.Value}");
            }
        }

        sb.AppendLine($"{key} 里的字号/字重 = {(settings.Count > 0 ? string.Join(", ", settings) : "（没取到）")}");
    }

    /// <summary>
    /// 遍历可视树里所有条目的 TextBlock，把实际生效的字号字重打出来。
    /// <c>FontSize</c> 是继承属性，读出来<b>不代表</b>它来自我们的样式——要看
    /// <c>ReadLocalValue</c>：本地值为空说明只有继承/默认在起作用。
    /// </summary>
    private static void AppendItemTextProperties(StringBuilder sb, DependencyObject root)
    {
        var blocks = new List<Windows.UI.Xaml.Controls.TextBlock>();
        Collect(root, blocks);

        sb.AppendLine($"条目内的 TextBlock 数 = {blocks.Count}");
        foreach (var block in blocks)
        {
            // 单个条目读失败不能把整份报告带走：官方模板里还有 chevron / ellipsis
            // 这些 TextBlock，它们的 Parent 之类拿到 nullptr 就会 NRE。
            try
            {
                var localSize = block.ReadLocalValue(Windows.UI.Xaml.Controls.TextBlock.FontSizeProperty);
                var localWeight = block.ReadLocalValue(Windows.UI.Xaml.Controls.TextBlock.FontWeightProperty);

                sb.AppendLine(
                    $"  \"{block.Text ?? "(null)"}\" FontSize={block.FontSize} " +
                    $"(本地={Format(localSize)}) FontWeight={block.FontWeight} " +
                    $"(本地={Format(localWeight)}) Style={(block.Style is null ? "无" : "有")}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  <读取条目失败：{ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}>");
            }
        }
    }

    private static string Format(object value) =>
        value is DependencyProperty ? "无" : value.ToString() ?? "?";

    private static void Collect(DependencyObject node, List<Windows.UI.Xaml.Controls.TextBlock> sink)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is Windows.UI.Xaml.Controls.TextBlock block)
            {
                sink.Add(block);
            }

            Collect(child, sink);
        }
    }

    private static int ReadMode()
    {
        try
        {
            var path = System.IO.Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path, "breadcrumb-mode.txt");
            return int.Parse(System.IO.File.ReadAllText(path).Trim());
        }
        catch
        {
            // 默认直接测 handler 现行做法（每项=带样式的 TextBlock）。
            return 5;
        }
    }

    private static int CountDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        var count = 0;
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T) count++;
            count += CountDescendants<T>(child);
        }

        return count;
    }
}
