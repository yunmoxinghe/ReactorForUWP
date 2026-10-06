using System;
using System.Collections.Generic;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// WinUI 2 面包屑内部 <c>ItemsRepeater</c> 的行为模型。
/// </summary>
/// <remarks>
/// 模型只编码<b>有源码出处</b>的三条事实（<c>tools/winui2-ref/dev/Breadcrumb/</c>）：
/// <list type="bullet">
///   <item><c>BreadcrumbBar.cpp:80-83</c> —— 只有 <c>s_ItemsSourceProperty</c>
///         变了才走 <c>UpdateItemsRepeaterItemsSource</c>；而 XAML 依赖属性对
///         "设成同一个引用"不抛变更通知，所以<b>引用不变 = 什么都不会发生</b>；</item>
///   <item><c>cpp:142-155</c> —— 重建发生在该函数里，且只有 repeater 已存在
///         （<c>cpp:151</c>）才立刻重建，否则留给 <c>OnApplyTemplate</c>(<c>cpp:73</c>)；</item>
///   <item><c>dxaml/xcp/core/core/elements/ItemsControl.cpp:403-409</c> ——
///         每个 <c>ItemsControl</c> 各自 new 自己的 <c>CItemCollection</c>，
///         即"新建一个载体"在 WinUI 侧<b>确实</b>意味着一个新引用。</item>
/// </list>
/// <para>
/// 可调的那个开关（<see cref="ReuseCarrier"/>）复刻的是两种写法：
/// 现在的写法（每次新载体）、以及"顺手优化"成长期持有一个集合再原地改的那种。
/// 后者正是当年"面包屑不见了"的原 bug 形态，关掉它就是反向对照。
/// </para>
/// </remarks>
internal sealed class BreadcrumbSim
{
    private sealed class Carrier
    {
        private static int _next;

        public int Id = ++_next;

        public List<string> Items { get; } = new();

        public override string ToString() => $"carrier#{Id}";
    }

    private readonly List<string> _trace = new();

    private Carrier? _shared;
    private object? _source;
    private bool _templateApplied;
    private List<string> _rendered = new();
    private IReadOnlyList<string> _items = Array.Empty<string>();

    /// <summary>
    /// true = 复用长期持有的集合（旧写法，引用不变）；false = 每次新建载体（当前写法）。
    /// </summary>
    public bool ReuseCarrier { get; init; }

    /// <summary>真正写进 <c>ItemsSource</c> 依赖属性并引发变更的次数。</summary>
    public int PublishCount { get; private set; }

    /// <summary>内部 repeater 重建条目的次数。</summary>
    public int RebuildCount { get; private set; }

    /// <summary>界面上实际渲染出来的条目。</summary>
    public IReadOnlyList<string> Rendered => _rendered;

    /// <summary>完整时序，失败时用来定位断点。</summary>
    public IReadOnlyList<string> Trace => _trace;

    private static string Show(IEnumerable<string> items) => string.Join("/", items);

    /// <summary>建控件并按当前 items 下发（对应 <c>Mount</c>）。</summary>
    public void Mount(IReadOnlyList<string> items)
    {
        _trace.Add($"mount [{Show(items)}]");
        _items = items;
        ApplyItems(items);
    }

    /// <summary>控件进可视树（<c>OnApplyTemplate</c> → <c>cpp:73</c> 兜底重建）。</summary>
    /// <remarks>
    /// <b>只生效一次</b>：真实 XAML 里模板每个控件只套一次，之后反复折叠/展开走的是
    /// <c>Visibility</c>，不会重跑 <c>OnApplyTemplate</c>。模型要是每次调用都重建，
    /// 就等于给了 bug 一个免费的补救机会——反向对照会直接归零，是模型在撒谎。
    /// </remarks>
    public void ApplyTemplate()
    {
        if (_templateApplied)
        {
            _trace.Add("ApplyTemplate（模板已套过，XAML 不会再跑一次）");
            return;
        }

        _trace.Add("ApplyTemplate");
        _templateApplied = true;

        if (_source is not null)
        {
            Rebuild();
        }
    }

    /// <summary>渲染一轮新 element（内容 diff 走真代码 <c>Seq.SequenceEqual</c>）。</summary>
    public void Update(IReadOnlyList<string> next)
    {
        _trace.Add($"update [{Show(next)}]");

        if (Seq.SequenceEqual(_items, next))
        {
            _trace.Add("  内容未变 → 不下发");
            return;
        }

        _items = next;
        ApplyItems(next);
    }

    private void ApplyItems(IReadOnlyList<string> items)
    {
        object source;

        if (ReuseCarrier)
        {
            _shared ??= new Carrier();
            _shared.Items.Clear();

            foreach (var text in items)
            {
                _shared.Items.Add(text);
            }

            source = _shared;
            _trace.Add($"  下发 {_shared}（复用原位改名）");
        }
        else
        {
            var fresh = new Carrier();

            foreach (var text in items)
            {
                fresh.Items.Add(text);
            }

            source = fresh;
            _trace.Add($"  下发 {fresh}（新载体）");
        }

        Publish(source);
    }

    private void Publish(object candidate)
    {
        // XAML 依赖属性：设成同一个引用不抛变更通知 → cpp:80-83 整条不打。
        if (!ItemsSourcePolicy.WillTriggerRebuild(_source, candidate))
        {
            _trace.Add($"  ItemsSource 引用未变 → ItemsRepeater 不重建（{candidate}）");
            return;
        }

        _source = candidate;
        PublishCount++;

        // cpp:142-155：repeater 已存在才立刻重建；不存在则等 ApplyTemplate(cpp:73)。
        if (_templateApplied)
        {
            Rebuild();
        }
        else
        {
            _trace.Add("  repeater 未创建 → 等 ApplyTemplate");
        }
    }

    private void Rebuild()
    {
        RebuildCount++;
        _rendered = _source is Carrier carrier ? new List<string>(carrier.Items) : new List<string>();
        _trace.Add($"  rebuild → [{Show(_rendered)}]");
    }
}
