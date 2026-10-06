using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

/// <summary>
/// 无人值守复现"进设置页"：把自动置位（<c>BlankTemplateApp.AutoOpenSettings</c>）
/// 做成一个页面组件，挂进来就等于"启动 1 秒后自动点一次设置项"。
/// </summary>
/// <remarks>
/// 为什么要有它：这条路径跨了导航（<c>Frame.Navigate</c>）、官方 <c>ItemsRepeater</c>
/// 系的面包屑、Toolkit 的设置卡片三拨东西，任一块在首布局出事都是 fast-fail，
/// 手动点一次要重新部署一轮。这里做成菜单项，一次部署反复跑。
/// </remarks>
public sealed class SettingsNavProbe : Component
{
    public override Element Render()
    {
        UseEffect(() =>
        {
            BlankTemplateApp.AutoOpenSettings = true;
            ReactorApplication.Trace("[nav-probe] 已置位 AutoOpenSettings，等它自动进设置页");

            // 进页之后再等一拍，把面包屑条目的真实字号打出来——
            // "大小不对"这件事只有数据（生效值）说了算，看代码是看不出来的。
            var timer = new Windows.UI.Xaml.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2500),
            };

            void Tick(object? sender, object e)
            {
                timer.Stop();
                ReactorApplication.Trace("[nav-probe] " + DumpBreadcrumbItems());
            }

            timer.Tick += Tick;
            timer.Start();
            return () =>
            {
                timer.Stop();
                timer.Tick -= Tick;
            };
        });

        return Component<BlankTemplateApp>();
    }

    /// <summary>
    /// 遍历窗口可视树，把每个 <c>BreadcrumbBarItem</c> 里那个条目的字号、字重、
    /// 有没有样式打出来。<c>Style=有</c> + <c>FontSize=28</c> 才算把 XAML 的
    /// <c>ItemTemplate</c> 映射对了。
    /// </summary>
    private static string DumpBreadcrumbItems()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("设置页面包屑实测:");

        var bars = new List<Microsoft.UI.Xaml.Controls.BreadcrumbBar>();
        Collect(Windows.UI.Xaml.Window.Current?.Content, bars);

        sb.AppendLine($"  BreadcrumbBar 数 = {bars.Count}");

        foreach (var bar in bars)
        {
            var blocks = new List<Windows.UI.Xaml.Controls.TextBlock>();
            Collect(bar, blocks);

            foreach (var block in blocks)
            {
                try
                {
                    if (string.IsNullOrEmpty(block.Text) || IsGlyph(block.Text))
                    {
                        continue;   // chevron / ellipsis 是官方模板自带的，不算条目
                    }

                    var local = block.ReadLocalValue(
                        Windows.UI.Xaml.Controls.TextBlock.FontSizeProperty);

                    var localText = local is Windows.UI.Xaml.DependencyProperty
                        ? "依赖样式"
                        : local?.ToString() ?? "?";

                    sb.AppendLine(
                        $"  条目 \"{block.Text}\" FontSize={block.FontSize} " +
                        $"(本地={localText}) " +
                        $"FontWeight={block.FontWeight} " +
                        $"Style={(block.Style is null ? "无" : "有")}");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  <读取失败 {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}>");
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>官方模板的 chevron / ellipsis 用的是图标字体的字形，跳过它们。</summary>
    private static bool IsGlyph(string text) =>
        text.Length > 0 && char.IsSurrogate(text[0]);

    private static void Collect<T>(Windows.UI.Xaml.DependencyObject? node, List<T> sink)
        where T : Windows.UI.Xaml.DependencyObject
    {
        if (node is null)
        {
            return;
        }

        for (var i = 0; i < Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i);
            if (child is T typed)
            {
                sink.Add(typed);
            }

            Collect(child, sink);
        }
    }
}
