using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Gallery.Pages;

/// <summary>
/// 只读量具：从<b>真实可视树</b>里把控件当前的取值读出来。
/// </summary>
/// <remarks>
/// <b>为什么要有这么个东西。</b>排查"点了一下没反应"这类问题时，三方说法会打架：
/// state 说自己变了、回调说自己收到了、控件说自己就是这个值。要分清是谁在撒谎，
/// 只能去问<b>控件本身</b>——而声明式写法里我们手里没有控件引用，只剩可视树这一条路。
/// <para>
/// <b>两条纪律：</b>
/// <list type="bullet">
///   <item><b>只读。</b>一个属性都不写、一个事件都不挂。写过一次，被测对象就不再是
///         原来那个被测对象了（这条代价之前付过：自检每秒拨一次控件值，日志里那些
///         "状态在变"其实全是自检自己拨出来的）。</item>
///   <item><b>不拿实例身份当证据。</b><c>RuntimeHelpers.GetHashCode</c> 在 AOT 下
///         会随 GC 变，用它当指纹得出的"控件每帧都在重建"是假的。要看换没换实例，
///         只能自己发号——见 <see cref="Serial"/>。</item>
/// </list>
/// </para>
/// </remarks>
internal static class LiveProbe
{
    /// <summary>
    /// 控件序号表。弱键：<c>FrameworkElement</c> → 序号，控件没了条目自己消失。
    /// 同一行日志里两个 <c>RB#3</c> 一定是同一个实例，<c>RB#3 → RB#7</c> 才是换了控件。
    /// </summary>
    private static readonly ConditionalWeakTable<object, Serial> Serials = new();

    private static int _next;

    private sealed class Serial
    {
        public int No;
    }

    /// <summary>
    /// 快照整棵树：把每种受控控件的"实际值"读成一行文本。
    /// </summary>
    /// <remarks>
    /// <b>一种控件读全部，不只读第一个。</b>诊断页上刻意摆了两组 RadioButtons
    /// （一组在普通容器、一组在 <c>SettingsExpander</c> 折叠区），两者行为不同正是
    /// 要对比的东西——只读第一个就会漏掉折叠区那一组。
    /// <para>
    /// 顺序是<b>可视树的先序</b>，也就是页面上的先后。想精确对位就给控件加
    /// <c>Tag</c> 再按 Tag 找；这里靠顺序，因为诊断页的布局是固定的。
    /// </para>
    /// </remarks>
    public static ProbeResult Snapshot()
    {
        var empty = new ProbeResult();
        var root = Windows.UI.Xaml.Window.Current?.Content as UIElement;
        if (root is null)
        {
            empty.Text = "(拿不到窗口内容，探针不可用)";
            return empty;
        }

        var radios = new List<MuxControls.RadioButtons>();
        var combos = new List<ComboBox>();
        var toggles = new List<ToggleSwitch>();
        var bars = new List<MuxControls.BreadcrumbBar>();

        Walk(root, node =>
        {
            switch (node)
            {
                case MuxControls.RadioButtons rb: radios.Add(rb); break;
                case ComboBox cb: combos.Add(cb); break;
                case ToggleSwitch ts: toggles.Add(ts); break;
                case MuxControls.BreadcrumbBar bb: bars.Add(bb); break;
            }
        });

        var sb = new StringBuilder();

        for (var i = 0; i < radios.Count; i++)
        {
            var rb = radios[i];
            sb.Append($"RB[{i}]#{Id(rb)} idx={rb.SelectedIndex} n={rb.Items.Count} | ");
        }

        for (var i = 0; i < combos.Count; i++)
        {
            var cb = combos[i];
            sb.Append($"CB[{i}]#{Id(cb)} idx={cb.SelectedIndex} n={cb.Items.Count} | ");
        }

        for (var i = 0; i < toggles.Count; i++)
        {
            sb.Append($"TS[{i}]#{Id(toggles[i])} on={toggles[i].IsOn} | ");
        }

        for (var i = 0; i < bars.Count; i++)
        {
            sb.Append($"BC[{i}] {Describe(bars[i])} | ");
        }

        var result = new ProbeResult
        {
            Text = sb.Length == 0 ? "(树上没找到任何受控控件)" : sb.ToString(0, sb.Length - 3),
        };

        foreach (var rb in radios)
        {
            result.RadioIndex.Add(rb.SelectedIndex);
        }

        foreach (var cb in combos)
        {
            result.ComboIndex.Add(cb.SelectedIndex);
        }

        foreach (var ts in toggles)
        {
            result.ToggleOn.Add(ts.IsOn);
        }

        foreach (var bar in bars)
        {
            result.BarItems.Add(Realized(bar).Count);
        }

        return result;
    }

    /// <summary>
    /// 结构化的读数。除了给人看的一行文本，还要保留<b>可比较的数值</b>：
    /// "state 和控件对不对得上"这件事不该让人肉去比两行数字——比错了就会得出
    /// 假结论，而假结论正是这一路反复返工的原因。交给 <c>Verdict</c> 去比。
    /// </summary>
    internal sealed class ProbeResult
    {
        /// <summary>页面上按可视树先序排列的 RadioButtons 的 SelectedIndex。</summary>
        public List<int> RadioIndex { get; } = new();

        public List<int> ComboIndex { get; } = new();

        public List<bool> ToggleOn { get; } = new();

        /// <summary>每个面包屑<b>实际渲染出来</b>的条目数。</summary>
        public List<int> BarItems { get; } = new();

        /// <summary>同一份读数的一行文本形式，直接显示用。</summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// 面包屑单列一段：它要同时看"数据源里几条"和"渲染出来几条"，
    /// 两者不等时才能区分是数据没喂进去还是模板没解析出来。
    /// </summary>
    private static string Describe(MuxControls.BreadcrumbBar bar)
    {
        // 只报"界面上渲染出了什么"——把每个 BreadcrumbBarItem 的文字读出来。
        // 不再报 ItemsSource 的条数：那个读数在 CsWinRT 投影下反复失真
        // （先是非泛型 IEnumerable 判不出、数成 0；再是 IList<object> 判得出但
        // 报 0，而可视树里明明渲染着东西）。一条假读数逼出的结论全是假的，
        // 与其修它不如直接读屏幕上真实存在的东西。
        var texts = Realized(bar);

        return $"BC#{Id(bar)} 渲染{texts.Count}项[{string.Join('/', texts)}] " +
               $"vis={bar.Visibility} w={bar.ActualWidth:F0} h={bar.ActualHeight:F0}";
    }

    /// <summary>面包屑此刻真的渲染出了哪些条目（文字），按可视树顺序。</summary>
    private static List<string> Realized(MuxControls.BreadcrumbBar bar)
    {
        var texts = new List<string>();

        Walk(bar, node =>
        {
            if (node is MuxControls.BreadcrumbBarItem item)
            {
                texts.Add(TextOf(item));
            }
        });

        return texts;
    }

    /// <summary>
    /// 读一个已渲染条目上真正显示的字。取第一个非空 <c>TextBlock</c>——
    /// 不去猜 <c>ContentControl.Content</c>，条目模板里还挂着折叠状态下隐藏的
    /// 省略号展示器，取 Content 可能取到那个。
    /// </summary>
    private static string TextOf(DependencyObject item)
    {
        var found = "(空)";

        Walk(item, node =>
        {
            if (found == "(空)" && node is TextBlock { Text: { Length: > 0 } text })
            {
                found = text;
            }
        });

        return found;
    }

    /// <summary>深度优先遍历可视树。</summary>
    private static void Walk(DependencyObject node, Action<DependencyObject> visit)
    {
        visit(node);

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), visit);
        }
    }

    /// <summary>给控件发一个稳定序号。换了实例，号就会变。</summary>
    private static int Id(object control)
    {
        if (Serials.TryGetValue(control, out var serial))
        {
            return serial.No;
        }

        serial = new Serial { No = ++_next };
        Serials.Add(control, serial);
        return serial.No;
    }
}
