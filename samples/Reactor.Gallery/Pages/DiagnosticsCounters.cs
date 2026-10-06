using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Reactor.Gallery.Pages;

/// <summary>
/// 诊断页里那套<b>计数增量</b>的算法。单独放一个文件、不碰 XAML，是为了能像框架的
/// 纯逻辑那样被 <c>net10.0</c> 测试工程 Link 进去跑断言。
/// </summary>
/// <remarks>
/// <b>为什么这也得有测试。</b>它是<b>仪器</b>：屏幕上那句"增量：matched+1"是人判定
/// "这一发被谁吞了"的唯一依据。仪器出错不像业务 bug 那样看得见——它安静地给一句
/// 错的读数，人就照着错的读数去改代码。取证这块以前栽过一次（拿
/// <c>GetHashCode</c> 当指纹），所以这类"显示屏"必须自带反向对照。
/// <para>
/// 输入文本来自 <c>ReactorLog.Counters()</c>（<c>"echo: matched=… | ready: ready=…"</c>）。
/// 那个格式一旦改，"永远零增量"就会伪装成"事件没到框架"——测试里那句
/// "必须解析出全部九个键"就是为这个变化准备的哨兵。
/// </para>
/// </remarks>
internal static class DiagnosticsCounters
{
    /// <summary>九个已知计数：六个来自 <c>EchoStats</c>，三个来自 <c>ReadyStats</c>。</summary>
    /// <remarks>
    /// 新增键必须同步改这里——否则解析出来的字典里没有它，
    /// 界面上表现为"这一档永远不涨"，而那会被读成"没发生"。
    /// </remarks>
    public static readonly string[] KnownKeys =
    {
        "matched", "mismatch", "expired", "notExpected", "sealed", "silenced",
        "ready", "suppressed", "alreadyLoaded",
    };

    /// <summary>
    /// 计数增量的人话版：把"涨了哪几项"翻成"这一发事件走到哪去了"。
    /// </summary>
    /// <remarks>
    /// 一排数字要人对着记九个名字的含义，等于把工作量又推回给肉眼——所以这里直接给结论。
    /// 最难当场取证的那一档（用户操作被当成回声吞掉）落在 <c>matched</c> 上，单独点出来，
    /// 别让它混在一串数字里。
    /// </remarks>
    public static string Verdict(string? baseline, string delta)
    {
        if (string.IsNullOrEmpty(baseline))
        {
            return "增量：还没记基线（先点『记基线』，再去点控件）";
        }

        if (string.IsNullOrEmpty(delta))
        {
            return "增量：一项都没涨 —— 事件压根没到框架，去 Input 通道看有没有 Input 行";
        }

        return delta.Contains("matched+", StringComparison.Ordinal)
            ? $"增量：{delta}　⚠ matched 涨了：这一发被判成回声吞掉。" +
              "如果刚才点的是用户操作、而回调没发生，这就是「点了没反应」的当场证据"
            : delta.Contains("silenced+", StringComparison.Ordinal)
            ? $"增量：{delta}　ℹ silenced 涨了：这一发落在" +
              "「框架正在写属性」的静默窗里（典型是改了 Minimum/Maximum 把受控值夹了）。" +
              "它不是用户输入，本来就该被挡下"
            : delta.Contains("alreadyLoaded+", StringComparison.Ordinal)
            ? $"增量：{delta}　ℹ alreadyLoaded 涨了：这些控件在被登记时就已经在树上了。" +
              "旧写法到此会挂一个永远不会来的 Loaded、把控件永久留在未就绪；现在被探针救了回来"
            : $"增量：{delta}";
    }

    /// <summary>
    /// 两份计数摘要的差值，<b>只列涨了的项</b>——没涨的项打出来只是噪音。
    /// </summary>
    public static string Contrast(string? baseline, string current)
    {
        if (string.IsNullOrEmpty(baseline))
        {
            return string.Empty;
        }

        var before = Parse(baseline);
        var sb = new StringBuilder();

        foreach (var pair in Parse(current))
        {
            before.TryGetValue(pair.Key, out var was);
            var diff = pair.Value - was;

            if (diff == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append("  ");
            }

            sb.Append(pair.Key).Append(diff > 0 ? "+" : string.Empty).Append(diff);
        }

        return sb.ToString();
    }

    /// <summary>把 <c>matched=1 expired=0</c> 这种文本拆成字典。</summary>
    public static Dictionary<string, long> Parse(string text)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(text, @"(\w+)=(-?\d+)"))
        {
            map[m.Groups[1].Value] = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        return map;
    }
}
