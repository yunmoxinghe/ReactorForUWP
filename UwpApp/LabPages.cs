// 实验室页：需要人手点两下的那几个验证项。
//
// 与压测页的区别：压测是无人值守跑完出归档，这里是「点一下看一眼」——
// 身份模型对不对、Echo 抑制有没有把输入吃掉，都要靠手动操作才能确认。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Internal;
using static Microsoft.UI.Reactor.Factories;
using WuControls = Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;
using ToolkitControls = CommunityToolkit.WinUI.Controls;
using Reactor.Uwp.Hosting;

namespace UwpApp;

/// <summary>
/// 虚拟列表身份实验室：验证「item key 是身份、下标只是位置」。
/// </summary>
/// <remarks>
/// 关键动作是<b>头部插入</b>：下标整体 +1 但身份没变。走 key 身份时已挂载的
/// 项只挪位置、内容不变；退化成下标身份时它们会集体拿错内容（这正是修之前的现象）。
/// </remarks>
public sealed class VirtualListLabPage : Component
{
    private const int Count = 5000;

    public override Element Render()
    {
        // <c>MakeItems()</c> 要建 5000 个字符串。写 <c>UseState(MakeItems())</c> 的话，
        // <b>每帧都会白建一份</b>——实参每次进 Render 都求值，而 use state 只在挂载
        // 那一帧认它：按一次按钮就是 5000 次字符串插值 + 一次 List 扩容，产物当场丢给 GC。
        // 在一个专门验证"身份 vs 下标"的页面上，把这份开销混进每一帧，
        // 观察到的现象里就分不清哪些是身份模型造成的、哪些是分配抖动造成的。
        // 用 <c>UseMemo</c> 把种子钉成"进程生命周期一次"，再交给 use state。
        // （这一手与 samples/Reactor.Gallery 的 VirtualizationPage 同源，别只在一份里保留。）
        var seed = UseMemo(MakeItems);
        var (items, setItems) = UseState(seed);
        var (useKey, setUseKey) = UseState(true);
        var (log, setLog) = UseState("尚未操作");

        // 同理，装箱这一份也是每帧重建；它只跟着 items 变，那就声明成"跟着 items"。
        List<object?> boxed = UseMemo(() => items.Cast<object?>().ToList(), items);

        return VStack(
            TextBlock($"虚拟列表实验室：{items.Count} 项，身份 = " +
                      (useKey ? "item key（稳定）" : "下标（退化，插入会错位）")),
            HStack(
                Button("头部插入", () =>
                {
                    var next = new List<string>(items);
                    next.Insert(0, "NEW-" + (DateTime.Now.Ticks % 10000).ToString("D4"));
                    setItems(next);
                    setLog($"已在头部插入 {next[0]}，原本第 0 项现在是第 1 项");
                }),
                Button("删除第 3 项", () =>
                {
                    if (items.Count <= 3)
                    {
                        return;
                    }

                    var next = new List<string>(items);
                    var removed = next[2];
                    next.RemoveAt(2);
                    setItems(next);
                    setLog($"已删除 {removed}");
                }),
                Button("末项移到第 2", () =>
                {
                    if (items.Count <= 3)
                    {
                        return;
                    }

                    var next = new List<string>(items);
                    var moved = next[^1];
                    next.RemoveAt(next.Count - 1);
                    next.Insert(1, moved);
                    setItems(next);
                    setLog($"已把 {moved} 移到第 1 位");
                }),
                Button(useKey ? "切到下标身份" : "切到 key 身份", () =>
                {
                    setUseKey(!useKey);
                    setLog("身份模型已切换：再插一次对比两种表现");
                }),
                Button("重置", () =>
                {
                    setItems(MakeItems());
                    setLog("已重置");
                })
            ),
            TextBlock(log),
            TextBlock("滚动到任意位置后点「头部插入」：可见项应原地保持自己的内容，只是整体下移一行。"),
            VirtualizingList(
                boxed,
                (item, i) => TextBlock($"{i,5} · {item}"),
                itemHeight: 28,
                height: 420,
                buffer: 4,
                itemKey: useKey ? (o => o) : null)
        );
    }

    private static List<string> MakeItems() =>
        Enumerable.Range(0, Count).Select(i => "Row-" + i.ToString("D5")).ToList();
}

/// <summary>
/// Echo 实验室：观察 EchoGuard 的匹配/失配计数，回归「粘贴后下一次编辑被覆盖」。
/// </summary>
/// <remarks>
/// 复现手法：粘贴一段文字进下面的输入框，紧接着再敲一个字符。
/// 如果回声抑制是「无脑消费」，第二次编辑会被上一次的回显顶掉；
/// 改成非破坏 Consume 之后，mismatch 应该留在登记里直到 TTL 过期。
/// </remarks>
/// <summary>
/// 原生对照：<b>不受控</b>的裸 RadioButtons。
/// </summary>
/// <remarks>
/// 存在的理由是一个单变量 A/B：同一个控件，去掉"受控下发 + 闸门 + 纠正"这三层之后，
/// 连点还会不会在第 3 次起失效。
/// <list type="bullet">
///   <item>受控页（元素画廊）实测：前 2 次点击正常，<b>第 3 次起永久没有事件</b>
///         （连 Trace 级的"吞掉"都没有，即事件根本没到 handler）。</item>
///   <item>本页不传 <c>SelectedIndex</c>，Reactor 不写控件，也就没有回声与纠正这一层。</item>
/// </list>
/// 若本页点到第 10 次仍然每次有回调 ⇒ 病灶在受控逻辑那一侧；
/// 若本页也在第 3 次起哑掉 ⇒ 病灶在 WinUI 的 RadioButtons 内部（与 Reactor 无关）。
/// </remarks>
public sealed class NativeRadioLabPage : Component
{
    public override Element Render()
    {
        var (picked, setPicked) = UseState(-1);
        var (calls, setCalls) = UseState(0);

        return VStack(
            TextBlock("原生对照：不受控的裸 RadioButtons（不传 SelectedIndex）"),
            TextBlock($"回调次数：{calls}　最近值：{picked}"),
            RadioButtons(
                new[] { "第一", "第二", "第三" },
                onSelectedIndexChanged: v =>
                {
                    setPicked(v);
                    setCalls(calls + 1);
                })
        );
    }
}

/// <summary>
/// <b>纯原生</b>对照：控件自己 <c>new</c>、事件自己挂，<b>完全不经过 Reactor 的 handler</b>。
/// </summary>
/// <remarks>
/// 与 <see cref="NativeRadioLabPage"/> 的区别：那一页只是"不受控"，控件仍走
/// <c>RadioButtonsHandler.Mount/Rebind</c>（事件是 Reactor 挂的）；本页连 handler
/// 都不参与，事件处理里除了打一行日志什么也不做。
/// <para>
/// 用途是把"连点第 3 次起不再抛事件"这条现象的病灶一次钉死：
/// <list type="bullet">
///   <item>本页也哑 ⇒ 是 WinUI <c>RadioButtons</c> 自己的内部状态（<c>m_selectedIndex</c>
///         与依赖属性脱钩，<c>RadioButtons.cpp:358</c> 的入口判据把它挡在门外），
///         与 Reactor 无关，该走 workaround 而不是改受控逻辑。</item>
///   <item>本页不哑 ⇒ 病灶在 Reactor 这一侧（受控下发 / 闸门 / 纠正），继续往里查。</item>
/// </list>
/// </para>
/// </remarks>
public sealed class RawRadioLabPage : Component
{
    public override Element Render() => Native(() =>
    {
        var panel = new WuControls.StackPanel();
        panel.Spacing = 8;

        var status = new WuControls.TextBlock { Text = "事件：0 次" };
        var control = new MuxControls.RadioButtons();

        var calls = 0;
        control.SelectionChanged += (_, args) =>
        {
            calls++;
            var line = $"[raw] 第 {calls} 次 SelectedIndex={control.SelectedIndex} "
                       + $"Added={args.AddedItems.Count} Removed={args.RemovedItems.Count}";
            ReactorLog.Info(ReactorLogChannel.Input, line);
            status.Text = $"事件：{calls} 次　当前 {control.SelectedIndex}";
        };

        foreach (var label in new[] { "第一", "第二", "第三" })
        {
            control.Items.Add(label);
        }

        // 初始选中"第二"，与受控页的初始受控值一致（单变量：只去掉 Reactor 那一层）。
        control.SelectedIndex = 1;

        panel.Children.Add(new WuControls.TextBlock
        {
            Text = "纯原生：RadioButtons 自己 new、事件自己挂（不经过 Reactor handler）",
            TextWrapping = Windows.UI.Xaml.TextWrapping.Wrap,
        });
        panel.Children.Add(status);
        panel.Children.Add(control);
        return panel;
    });
}

/// <summary>
/// DP 探针：每 300ms 采一次页面上所有 <c>RadioButtons</c> 的 <c>SelectedIndex</c>（依赖属性）。
/// </summary>
/// <remarks>
/// 为什么要它：<c>RadioButtons::Select</c> 的内部缓存 <c>m_selectedIndex</c> 从外面读不到，
/// 而"点了没反应"的两种可能全靠它与依赖属性的关系区分：
/// <list type="bullet">
///   <item>DP 跟着 UI 变、却不抛事件 ⇒ <c>Select</c> 被入口判据挡住
///         （<c>cpp:358</c> 的 <c>m_blockSelecting / m_currentlySelecting / m_selectedIndex != index</c>）。</item>
///   <item>DP 根本不动 ⇒ <c>OnChildChecked/Unchecked</c> 压根没触发，是子元素那一层断了。</item>
/// </list>
/// 它同时回答"UI 上选中项与 DP 是否一致"——两者脱钩就是内部缓存与依赖属性对不上。
/// </remarks>
public sealed class RadioDpProbePage : Component
{
    public override Element Render()
    {
        var (calls, setCalls) = UseState(0);
        var (picked, setPicked) = UseState(-1);

        return VStack(
            TextBlock("DP 探针：下面的采样行每 300ms 刷新一次（不受控 + 有回调）"),
            Native(() =>
            {
                var text = new WuControls.TextBlock { Text = "采样：—" };
                var timer = new Windows.UI.Xaml.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300),
                };

                timer.Tick += (_, _) =>
                {
                    var sb = new System.Text.StringBuilder("采样：");
                    Walk(Windows.UI.Xaml.Window.Current?.Content, sb);
                    text.Text = sb.ToString();
                };

                timer.Start();
                return text;
            }),
            TextBlock($"回调次数：{calls}　最近值：{picked}"),
            RadioButtons(
                new[] { "第一", "第二", "第三" },
                onSelectedIndexChanged: v =>
                {
                    setPicked(v);
                    setCalls(calls + 1);
                })
        );
    }

    private static void Walk(Windows.UI.Xaml.DependencyObject? node, System.Text.StringBuilder sb)
    {
        if (node is null)
        {
            return;
        }

        if (node is MuxControls.RadioButtons rb)
        {
            sb.Append($"RB(sel={rb.SelectedIndex},items={rb.Items.Count}) ");
        }

        var count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            Walk(Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i), sb);
        }
    }
}

public sealed class EchoLabPage : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState(string.Empty);
        var (tick, setTick) = UseState(0);

        return VStack(
            TextBlock("Echo 实验室：在下面输入 / 粘贴 / 用输入法组合，观察 EchoGuard 计数。"),
            TextBlock("复现步骤：粘贴一段文字 → 紧接着再输入一个字符 → 看文本有没有被覆盖。"),
            TextBox(
                text,
                v =>
                {
                    setText(v);
                    setTick(tick + 1);
                },
                placeholderText: "在这里粘贴 + 继续编辑",
                header: "输入框"),
            TextBlock($"当前值：{text}"),
            TextBlock($"EchoStats：{EchoStats.Snapshot()}"),
            HStack(
                Button("刷新统计", () => setTick(tick + 1)),
                Button("清零统计", () =>
                {
                    EchoStats.Reset();
                    setTick(tick + 1);
                }),
                Button("清空文本", () => setText(string.Empty))
            )
        );
    }
}

/// <summary>
/// 重挂探针页：<c>TreeView</c> / <c>HyperlinkButton</c> / <c>SettingsCard</c>
/// 三个 handler 的回调是不是<b>恰好一次</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>它盯的头一件事。</b>这三个 handler 原本都在事件委托里拿回调给的
/// <c>sender</c> 去查按控件建的表（<c>ConditionalWeakTable</c>，<b>引用相等</b>），
/// 而 WinRT 不保证同一原生对象每次都交出同一个托管包装——查不到就静默返回，
/// 表现为<b>"点了没反应"</b>。这条跟控件会不会被复用无关，只要 WinRT 换了包装就犯，
/// 所以本页一定能验。
/// </para>
/// <para>
/// <b>第二件事。</b>它们的 <c>Unmount</c> 原本只摘表键、<b>不解绑事件</b>，而
/// <c>Rebind</c> 拿"表键在不在"充当"订阅过没有"的守卫。同一个原生控件被
/// <b>重新挂载</b>时会被判成"没挂过"，委托再挂一份——此后每一次点击都是双份回调。
/// 这条依赖协调器真会复用那个控件实例；不确定会不会发生时，就把下面那个
/// "卸载 / 重挂"按钮点下去，自己看计数。
/// </para>
/// <para>
/// <b>计数为什么不能用 <c>setTree(tree + 1)</c>。</b>闭包捕获的是<b>那一帧</b>的值：
/// 同一帧里到达两次回调（正是要抓的现象）会让两次都基于同一个旧值算出同一个数，
/// 后者把前者覆盖掉——<b>翻倍被自己抹平了</b>，页面看上去一切正常。
/// 这里用实例字段配 <c>Interlocked.Increment</c>，它返回自增<b>之后</b>的值，
/// 同帧来几次就是几。
/// </para>
/// </remarks>
public sealed class RebindProbePage : Component
{
    private int _treeHits;
    private int _linkHits;
    private int _cardHits;

    public override Element Render()
    {
        var (shown, setShown) = UseState(true);
        var (reversed, setReversed) = UseState(false);
        var (tree, setTree) = UseState(0);
        var (link, setLink) = UseState(0);
        var (card, setCard) = UseState(0);
        var (cycles, setCycles) = UseState(0);

        return ScrollViewer(VStack(12.0,
            TextBlock($"重挂探针：Tree 回调 {tree} | 链接 {link} | 卡片 {card} | 卸载/重挂 {cycles} 次"),
            TextBlock("判据：点 N 下，计数就该是 N；卸载再重挂之后，下一发仍然只加 1。"),

            HStack(8.0,
                Button(shown ? "卸载子树" : "重挂子树", () =>
                {
                    setCycles(cycles + 1);
                    setShown(!shown);
                }),
                Button("反转顺序（诱导重排）", () => setReversed(!reversed)),
                Button("计数清零", () =>
                {
                    Interlocked.Exchange(ref _treeHits, 0);
                    Interlocked.Exchange(ref _linkHits, 0);
                    Interlocked.Exchange(ref _cardHits, 0);
                    setTree(0);
                    setLink(0);
                    setCard(0);
                })),

            shown
                ? BuildSubtree(reversed, setTree, setLink, setCard)
                : VStack(TextBlock("（子树已卸载：三个控件都不在树上）"))));
    }

    private Element BuildSubtree(
        bool reversed,
        Action<int> setTree,
        Action<int> setLink,
        Action<int> setCard)
    {
        var treeView = TreeView(
            _ => setTree(Interlocked.Increment(ref _treeHits)),
            TreeNode("甲", expanded: true, TreeNode("甲-1"), TreeNode("甲-2")),
            TreeNode("乙"));

        var link = HyperlinkButton(
            "超链接：点一下算一次",
            () => setLink(Interlocked.Increment(ref _linkHits)));

        var card = SettingsCard(
            header: "设置卡片：点一下算一次",
            description: "本页三份计数应当各自等于你点的次数",
            onClick: () => setCard(Interlocked.Increment(ref _cardHits)));

        return reversed
            ? VStack(12.0, card, link, treeView)
            : VStack(12.0, treeView, link, card);
    }
}

/// <summary>
/// <b>裸 sender 对照</b>：同一控件类型，一侧走 Reactor handler（委托里用捕获的
/// <c>control</c> 查表），另一侧自己 <c>new</c>、自己挂事件，委托里<b>故意用
/// <c>sender</c> 查表</b>（就是修复前的写法）。
/// </summary>
/// <remarks>
/// <para>
/// <b>它要回答的那个问题。</b>三处修复（<c>TreeView</c> / <c>HyperlinkButton</c> /
/// <c>SettingsCard</c>）的理由是"WinRT 不保证同一原生对象每次都交出同一个托管包装，
/// 拿 <c>sender</c> 查按引用建的表会查不到"。可是把修复还原成旧写法重新编译跑真机，
/// 计数<b>照旧</b>——也就是说在这条路径上 <c>sender</c> 恰好就是同一个 RCW，
/// 真机验证因此<b>没有判别力</b>（详见 <c>winapp_rebind.py</c> 的注释）。
/// 本页把这个"恰好"变成可观测的量：两侧点同样的次数，裸侧记<b>收到</b>与
/// <b>命中</b>两个不同的数，外加 <c>ReferenceEquals(sender, control)</c>。
/// </para>
/// <para>
/// <b>怎么读结果。</b>
/// <list type="bullet">
///   <item>裸侧「命中 &lt; 收到」（或同引用 = False）⇒ 身份漂移在这条路径上<b>真的发生</b>，
///         三处修复是真机可复现的修复，回归脚本也就此恢复判别力。</item>
///   <item>裸侧「命中 = 收到」⇒ 该机型该路径上 WinRT 交回同一 RCW，修复是<b>按构造正确</b>
///         的防御性改动，真机仍验不出来；判别力继续由源码级契约承担。</item>
/// </list>
/// 两种结论都算把这条线闭合了：前者把它变成真机回归，后者把它明确定性为"不可达"。
/// </para>
/// <para>
/// <b>为什么裸侧计数用字段而不是 <c>setState</c>。</b>理由与 <see cref="RebindProbePage"/>
/// 那三个字段一致：同一帧到达两次时，捕获旧值的闭包会把翻倍自我抹平。裸侧更是直接
/// 写在 <c>Native</c> 闭包里，压根没有 state 可用。
/// </para>
/// </remarks>
public sealed class SenderIdentityProbePage : Component
{
    private int _aTree;
    private int _aLink;
    private int _aCard;

    /// <summary>
    /// B 侧把摘要往页面上送的通道。每帧刷成<b>当下</b>那个 setter：
    /// <c>Native</c> 里的控件只在挂载时造一次（token 不变就不重建，见
    /// <c>NativeHandler.Update</c>），它闭包里抓到的是<b>第一帧</b>的引用，
    /// 不每帧刷新就会一直用着最早的 setter。
    /// </summary>
    private Action<string>? _sink;

    public override Element Render()
    {
        var (tree, setTree) = UseState(0);
        var (link, setLink) = UseState(0);
        var (card, setCard) = UseState(0);
        // 起手就写满三段格式（而不是"树 — | 链接 — | 卡片 —"）：回归脚本靠
        // "命中N/收到N 同引用=X" 这三段解析这一行，占位符写成破折号会让它
        // 一行都解析不出来，被误报成"读不到状态行"。
        var (raw, setRaw) = UseState(RawCounter.Blank);

        _sink = setRaw;

        // 两列并排而不上下叠：屏外元素（IsOffscreen=True）在 UIA 的 search 里
        // 根本搜不到，也就无从点击 —— 竖着排会让 B 侧整列落在滚动视口之外，
        // 对照直接不成立。
        return ScrollViewer(VStack(12.0,
            TextBlock("裸 sender 对照：A 侧走 Reactor handler（委托里用捕获的 control 查表），"
                    + "B 侧自己 new 控件、自己挂事件，委托里故意用 sender 查表（修复前的写法）。"),
            TextBlock("判据：两侧各点 N 下。B 侧「命中 < 收到」= WinRT 交回的不是同一个托管包装，"
                    + "查表落空、回调静默丢掉——这正是那三处修复要挡的东西。"),
            TextBlock($"A 侧（有修复）：树 {tree} | 链接 {link} | 卡片 {card}"),
            TextBlock($"B 侧（裸 sender）：{raw}"),

            HStack(16.0,
                // 与 B 列同序（卡片在上）：两列顺序不一致时，两边卡片的 y 就不同，
                // 一边在视口内一边在视口外 —— 那是量具的差异，不是对照的差异。
                VStack(8.0,
                    TextBlock("A 侧"),
                    SettingsCard(
                        header: "A-卡片：点一下算一次",
                        description: "A 侧：走 Reactor handler",
                        onClick: () => setCard(Interlocked.Increment(ref _aCard))),
                    TreeView(
                        _ => setTree(Interlocked.Increment(ref _aTree)),
                        TreeNode("A-甲", expanded: true, TreeNode("A-甲-1"), TreeNode("A-甲-2"))),
                    HyperlinkButton(
                        "A-链接：点一下算一次",
                        () => setLink(Interlocked.Increment(ref _aLink)))),
                VStack(8.0,
                    TextBlock("B 侧"),
                    Native(BuildRawPanel)))));
    }

    /// <summary>B 侧一行摘要要用到的三个计数：收到 / 命中 / sender 是否与订阅时同引用。</summary>
    private sealed class RawCounter
    {
        /// <summary>还没收到任何事件时那三段的写法（0 发，同引用尚无反例）。</summary>
        public static string Blank =>
            "树 命中0/收到0 同引用=True | 链接 命中0/收到0 同引用=True | 卡片 命中0/收到0 同引用=True";

        public string Label = string.Empty;
        public int Fired;
        public int Hit;

        /// <summary>取"一直是同一个引用"：任一发不是同引用就永久变 False。</summary>
        public bool SameRef = true;

        public Action? Changed;

        public void OnEvent(object? sender, object control, bool found)
        {
            Fired++;
            Hit += found ? 1 : 0;
            SameRef = SameRef && ReferenceEquals(sender, control);
            Changed?.Invoke();
        }

        public override string ToString() =>
            $"{Label} 命中{Hit}/收到{Fired} 同引用={(SameRef ? "True" : "False")}";
    }

    private WuControls.StackPanel BuildRawPanel()
    {
        // 每个子项都要显式 Stretch：StackPanel 不替子项撑宽度，而
        // SettingsCard 在"宽度为 0"时连点击都点不了（UIA 报 zero size），
        // 这一条不设，B 侧第三个控件就永远拿不到对照数据。
        var panel = new WuControls.StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Stretch,
        };

        var counters = new[] { new RawCounter { Label = "树" }, new RawCounter { Label = "链接" },
                               new RawCounter { Label = "卡片" } };

        // 摘要不落在这一列里，而是送回页面顶部由 Reactor 渲染的那一行：
        // UIA 的 search 搜不到屏外元素，行在自己这一列就意味着脚本读不到它。
        Action refresh = () =>
            _sink?.Invoke(string.Join(" | ", Array.ConvertAll(counters, c => c.ToString())));

        foreach (var c in counters)
        {
            c.Changed = refresh;
        }

        // <b>卡片放第一个</b>，且这一列一行说明都不加。
        // 理由：SettingsCard 不支持 invoke pattern，只能用鼠标 click，而 click 要元素
        // 的屏幕矩形 —— 屏外元素（IsOffscreen=True）的矩形是 0，点就打不中。
        // 放在列尾时它正好被挤出滚动视口（取证：get-property IsOffscreen=True）。
        // 树与链接走 invoke，不受矩形约束，放下面无所谓。
        // 这一列的高度因此也必须压住：多一行说明就会把下面那两个挤出去。
        panel.Children.Add(RawSettingsCard(counters[2]));
        panel.Children.Add(RawTreeView(counters[0]));
        panel.Children.Add(RawHyperlink(counters[1]));
        return panel;
    }

    private static Windows.UI.Xaml.UIElement RawTreeView(RawCounter counter)
    {
        var control = new MuxControls.TreeView();

        var root = new MuxControls.TreeViewNode { Content = "B-甲", IsExpanded = true };
        root.Children.Add(new MuxControls.TreeViewNode { Content = "B-甲-1" });
        root.Children.Add(new MuxControls.TreeViewNode { Content = "B-甲-2" });
        control.RootNodes.Add(root);

        var table = new ConditionalWeakTable<MuxControls.TreeView, object>();
        table.Add(control, new object());

        control.ItemInvoked += (s, _e) =>
            counter.OnEvent(s, control, table.TryGetValue(s, out _));

        return control;
    }

    private static Windows.UI.Xaml.UIElement RawHyperlink(RawCounter counter)
    {
        var control = new WuControls.HyperlinkButton { Content = "B-链接：点一下算一次" };
        var table = new ConditionalWeakTable<WuControls.HyperlinkButton, object>();
        table.Add(control, new object());

        control.Click += (s, _e) =>
            counter.OnEvent(s, control, s is WuControls.HyperlinkButton b && table.TryGetValue(b, out _));

        return control;
    }

    private static Windows.UI.Xaml.UIElement RawSettingsCard(RawCounter counter)
    {
        var control = new ToolkitControls.SettingsCard
        {
            Header = "B-卡片：点一下算一次",
            Description = "B 侧：裸 sender 查表",
            // SettingsCard 默认 <c>IsClickEnabled=false</c>：那样它在 UIA 里根本不是
            // Button（也不抛 Click），裸控件必须自己打开。Reactor 的 handler 会在
            // 有 onClick 时替调用方设上，所以 A 侧不用管——这正是"裸侧"要自己
            // 把每一处补齐的理由：少一处，对照就在那一处悄悄不成立。
            IsClickEnabled = true,
            HorizontalAlignment = Windows.UI.Xaml.HorizontalAlignment.Stretch,
            // 高度也给死：这一列的可用高度是"另一列撑出来的"，而裸侧这一份拿不到
            // 那一份高度（Reactor 侧的卡片有 handler 兜着），落到 0 高度时
            // UIA 报 zero size，鼠标 click 就打不中它。
            Height = 96,
            MinHeight = 96,
            // 给死宽度而不是指着 Stretch：这一列的宽度是"子项固有宽度"撑出来的，
            // 而 SettingsCard 自己的固有宽度是 0 —— Stretch 与列宽互相等着对方，
            // 结果就是 0 宽，UIA 报 zero size、点都点不了。
            Width = 280,
        };

        var table = new ConditionalWeakTable<ToolkitControls.SettingsCard, object>();
        table.Add(control, new object());

        control.Click += (s, _e) =>
            counter.OnEvent(s, control, s is ToolkitControls.SettingsCard b && table.TryGetValue(b, out _));

        return control;
    }
}

/// <summary>
/// 诊断量具自检页：给 <c>tools/uia/winapp_diag.py</c> 那两条告警造能触发的场面。
/// </summary>
/// <remarks>
/// 「多项同时选中」「CPU 持续高」这两条告警<b>从来没被真场景触发过</b>——bug 修完
/// 就没有触发场景了，等于写在那儿没人验过。不验的代价是：等真出事时你不知道它
/// 到底会不会响、响得对不对，误以为它给的是可信结论，比没有更危险。
/// 所以这里<b>故意</b>造两个反常场面，让量具自己响一次，把它从"未验证"变成"验过"：
///  · 甲、乙 两个 RadioButton 的 groupName 不同 → 允许同时选中 → 该报「2 项同时选中」
///  · 「忙 6 秒」起一条后台线程空转 → 该报「CPU 占 N% 单核」
/// 这两个场面是<b>假的</b>（是构造出来的，不是 bug），只用来证明告警分支会响。
/// </remarks>
public sealed class DiagFixturePage : Component
{
    public override Element Render()
    {
        var (a, setA) = UseState(true);
        var (b, setB) = UseState(true);
        var (spins, setSpins) = UseState(0);

        return ScrollViewer(VStack(12.0,
            TextBlock("这一页是给诊断脚本做自检用的：场面是故意造出来的反常，不是 bug。"),
            TextBlock($"甲={a}  乙={b}  已空转 {spins} 次"),
            TextBlock("甲 / 乙 的 groupName 不同 → 允许同时选中（正常 UI 里不该出现）"),
            RadioButton("甲", Optional<bool>.Of(a), v => setA(v), "diag-a"),
            RadioButton("乙", Optional<bool>.Of(b), v => setB(v), "diag-b"),
            Button("忙 6 秒", () =>
            {
                setSpins(spins + 1);
                Spin(6.0);
            })));
    }

    /// <summary>
    /// 在<b>后台</b>线程空转 <paramref name="seconds"/> 秒。
    /// </summary>
    /// <remarks>
    /// 必须走后台线程：在 UI 线程上空转的话 <c>invoke</c> 会一直不返回，而脚本的采样窗
    /// 开在 invoke <b>之后</b>，等它返回时空转早就结束了 —— 反而量不到 CPU，只会看到 0%。
    /// </remarks>
    private static void Spin(double seconds)
    {
        Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                Thread.SpinWait(2000);
            }
        });
    }
}
