using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

/// <summary>
/// 受控控件诊断页：把"点一下到底发生了什么"全部摆到屏幕上，一次跑完就能定性。
/// </summary>
/// <remarks>
/// <b>为什么需要这么一页。</b>"点了没反应"的成因分布在四个环节里：
/// <list type="number">
///   <item>事件<b>有没有落到控件</b>（命中/布局）；</item>
///   <item>控件的事件<b>有没有被闸门放行</b>（未就绪 / 重建中 / 回声）；</item>
///   <item>回调<b>有没有改到 state</b>；</item>
///   <item>state 变了之后<b>值有没有回到控件上</b>（受控属性下发）。</item>
/// </list>
/// 它们长得一模一样（界面不动），但修法完全不同。以前只能一个环节一个环节地
/// 插临时日志、跑一次、看一次——而插桩本身又会扰动被测对象。这一页把四个环节
/// 的输出同时摆在屏幕上：<b>state 值 / 回调次数 / 控件实际值 / 框架日志</b>，
/// 四者对不上就一眼看出断在哪一环，不再需要来回改代码。
/// <para>
/// <b>两组 RadioButtons 是刻意的。</b>第二组放在 <c>SettingsExpander</c> 的展开区里——
/// 那是 Reactor.Template 设置页出问题的原始场景：折叠区里的控件挂载时还没进可视树，
/// 按 WinUI 源码（<c>RadioButtons.h:91</c> 的 <c>m_blockSelecting{ true }</c>）
/// 此刻它拒不接受任何选中。两组对照着看，才能确认修的是不是同一个病。
/// </para>
/// </remarks>
public sealed class DiagnosticsPage : Component
{
    private static readonly string[] Themes = { "浅色", "深色", "高对比" };
    private static readonly string[] Crumbs = { "首页", "设置" };
    private static readonly string[] Plans = { "免费", "专业", "企业" };

    public override Element Render()
    {
        var (radio, setRadio) = UseState(1);
        var (folded, setFolded) = UseState(1);
        var (combo, setCombo) = UseState(0);
        var (sound, setSound) = UseState(true);
        var (crumb, setCrumb) = UseState(-1);

        // 面包屑要能改条目数，理由见下面 ⑤ 那段：<b>只有同一会话里下发过两次，
        // 才能证明"每次都换了数据源实例"</b>。固定两层的面包屑一辈子只下发一次，
        // 那条修复到底生效没有，就永远没人知道。
        var (crumbCount, setCrumbCount) = UseState(2);

        var (radioHits, setRadioHits) = UseState(0);
        var (foldedHits, setFoldedHits) = UseState(0);
        var (comboHits, setComboHits) = UseState(0);
        var (soundHits, setSoundHits) = UseState(0);
        var (crumbHits, setCrumbHits) = UseState(0);

        var (snap, setSnap) = UseState(string.Empty);
        var (tick, setTick) = UseState(0);
        var (verbose, setVerbose) = UseState(false);

        // 通道过滤：-1 = 全部。必须能按通道隔离——高频通道（渲染）会把环形缓冲
        // 冲掉，不隔离的话想看的 Input 行早被挤出去了，屏幕上只剩一堆帧号，
        // 看着"有日志"其实一条证据都没有。
        var (chan, setChan) = UseState(-1);

        // 闸门计数要看<b>增量</b>而不是绝对值：这些计数单调只增，一次操作的区别
        // 只体现在"哪几项各涨了多少"。更重要的是——"点了没反应"那一次是
        // state 没变、不重渲染的，若没有下面的轮询，这一屏数字停在<b>上一次</b>，
        // 恰恰在最需要取证的时候失明。
        var (baseline, setBaseline) = UseState(string.Empty);
        var (counters, setCounters) = UseState(string.Empty);
        var (delta, setDelta) = UseState(string.Empty);
        var (pulse, setPulse) = UseState(0L);
        var (polling, setPolling) = UseState(true);
        var pulseRef = UseRef(0L);

        // 快照必须<b>渲染之后</b>再采：Render() 里读可视树读到的是上一帧的控件，
        // 拿它跟本帧的 state 比，会得出"值和 state 不一致"的假结论——这个跟头
        // 之前栽过一次。UseEffect 在 EndRender 里跑，那时 patch 已经落盘。
        UseEffect(
            () =>
            {
                var probe = LiveProbe.Snapshot();
                var verdict = Verdict(radio, folded, combo, sound, crumbCount, probe);

                // 每帧一行"state + 控件实际值"。这是把日志变成完整因果链的最后一段：
                // 事件(Pass) → setState → 帧(Render) → 控件实际值(这里)。
                // 帧号直接带在这一行里：每帧单独打一条会把整个 Info 缓冲占满，
                // 而"回调之后到底有没有重渲染"这条判据又不能丢——合成一个数最省版面。
                ReactorLog.Info(
                    ReactorLogChannel.Render,
                    $"帧{ReactorLog.Frames} state: radio={radio} folded={folded} combo={combo} " +
                    $"sound={sound} crumb={crumb} " +
                    $"| 回调 {radioHits}/{foldedHits}/{comboHits}/{soundHits}/{crumbHits} " +
                    $"| {probe.Text}");

                // 判定只在<b>对不上</b>时进日志：全对时打 Warn 会把真正的异常淹没掉，
                // 而"对不上"本身就是断点，不需要人再去比对两行数字。
                if (verdict.Bad)
                {
                    ReactorLog.Warn(
                        ReactorLogChannel.Render,
                        $"state 与控件不一致：{verdict.Text}（state: " +
                        $"{radio}/{folded}/{combo}/{sound}/{Math.Min(crumbCount, Crumbs.Length)}）");
                }

                var text = probe.Text + Environment.NewLine + verdict.Text;
                if (!string.Equals(text, snap, StringComparison.Ordinal))
                {
                    setSnap(text);
                }
            },
            // 依赖必须是<b>闭包里读到的每一个 state</b>：少一个，那一项的帧就整帧
            // 不进日志、不进判定、快照也不刷新——上面那句"因果链的最后一环"就断了，
            // 而断的方式是<b>安静的</b>（页面照常显示，只是这一项永远停在旧值），
            // 正是最难发现的一类。⑤ 的 crumb / crumbCount 之前就漏在这儿，
            // 于是 ⑤ 这一组控件实际上<b>完全没被诊断覆盖</b>：点了不加日志、不改判定、
            // 换数据源也不复采样——而它恰好是页面上唯一用来验证"每次下发都换数据源
            // 引用"的那组。
            // <para>
            // 回调计数也要进依赖：受控控件重复选中同一项时 value 不变，只有计数涨，
            // 不在依赖里的话那一次点击连一行都没有，"回调到底来了没"就只能靠猜。
            // </para>
            new object[]
            {
                radio, radioHits,
                folded, foldedHits,
                combo, comboHits,
                sound, soundHits,
                crumb, crumbHits, crumbCount,
                tick,
            });

        // 轮询让读数持续更新。<b>闭包陷阱</b>：这里的 pulse 是这一帧的值，写
        // setPulse(pulse + 1) 的话每次算出的都是同一个数，只有第一次会更新；
        // 所以用 Ref 记一个跨渲染存活的计数器。
        UseEffect(
            () =>
            {
                if (!polling)
                {
                    return () => { };
                }

                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (_, _) =>
                {
                    pulseRef.Current += 1;
                    setPulse(pulseRef.Current);
                };
                timer.Start();

                // cleanup 必须停表：页面卸载后计时器若还活着，既把整棵树钉住不回收，
                // 又会往已经下树的 setState 里打。
                return () => timer.Stop();
            },
            new object[] { polling });

        UseEffect(
            () =>
            {
                var text = ReactorLog.Counters();
                setCounters(text);
                setDelta(DiagnosticsCounters.Contrast(baseline, text));
            },
            new object[] { pulse, baseline });

        return ScrollViewer(
            VStack(12,
                TextBlock("受控控件诊断").FontSize(20),
                TextBlock("四行读数对得上就说明链路通：state / 回调次数 / 控件实际值 / 框架日志。" +
                          "对不上时，最后一个对得上的环节之后就是断点。")
                    .Caption().Wrap(),

                // ── 1. 普通容器里的 RadioButtons ────────────────────────────
                Panel(
                    TextBlock("① RadioButtons（普通容器）").BodyStrong(),
                    RadioButtons(Themes, Optional<int>.Of(radio), v =>
                    {
                        setRadio(v);
                        setRadioHits(radioHits + 1);
                    }),
                    TextBlock($"state={radio}　回调 {radioHits} 次").Caption()),

                // ── 2. 折叠区里的 RadioButtons（原始 bug 场景）────────────
                SettingsExpander(
                    header: "② RadioButtons（SettingsExpander 展开区 —— 原始 bug 场景）",
                    headerIcon: FontIcon("\uE713"),
                    description: "展开后才加载：挂载时的受控值会被 WinUI 吞掉，靠就绪闸补回",
                    items: new Element?[]
                    {
                        SettingsCard(
                            "折叠区内的单选",
                            RadioButtons(Themes, Optional<int>.Of(folded), v =>
                            {
                                setFolded(v);
                                setFoldedHits(foldedHits + 1);
                            }),
                            contentAlignment: SettingsCardContentAlignment.Left),
                        TextBlock($"state={folded}　回调 {foldedHits} 次").Caption(),
                    }),

                // ── 3. ComboBox ────────────────────────────────────────────
                Panel(
                    TextBlock("③ ComboBox（Selector 家族，items 重建会冲掉选中）").BodyStrong(),
                    ComboBox(Plans, Optional<int>.Of(combo), v =>
                    {
                        setCombo(v);
                        setComboHits(comboHits + 1);
                    })
                        .AutomationName("ComboBox 示例选择"),
                    TextBlock($"state={combo}　回调 {comboHits} 次").Caption()),

                // ── 4. ToggleSwitch ────────────────────────────────────────
                Panel(
                    TextBlock("④ ToggleSwitch").BodyStrong(),
                    ToggleSwitch(Optional<bool>.Of(sound), v =>
                    {
                        setSound(v);
                        setSoundHits(soundHits + 1);
                    }, "开", "关", "声音"),
                    TextBlock($"state={sound}　回调 {soundHits} 次").Caption()),

                // ── 5. BreadcrumbBar ───────────────────────────────────────
                // 条目数可增减不是为了好看：WinUI 规定每次下发都得是<b>全新的数据源
                // 引用</b>（内部 ItemsRepeater 按引用相等判断换没换），而这条修复此前
                // <b>没有任何运行时证据</b>——之前日志里那几个不同的 #hash 全是
                // 跨进程取值，GetHashCode 跨进程本来就不一样，等于什么都没证。
                // 现在点"加/减一项"，同一会话里会连着下发两次，看 Items 通道：
                // 两个<b>不同</b>的编号（如 #12 → #37）才说明真的换了实例；
                // 两次同号 = 引用没变 = 条数不会更新，也就是"面包屑不见"复发。
                Panel(
                    TextBlock("⑤ BreadcrumbBar（数据源引用每次下发都必须变）").BodyStrong(),
                    BreadcrumbBar(
                        Crumbs[..Math.Min(crumbCount, Crumbs.Length)],
                        i =>
                        {
                            setCrumb(i);
                            setCrumbHits(crumbHits + 1);
                        }),
                    HStack(8,
                        Button("加一项", () =>
                            setCrumbCount(Math.Min(Crumbs.Length, crumbCount + 1))),
                        Button("减一项", () => setCrumbCount(Math.Max(1, crumbCount - 1)))),
                    TextBlock(
                            $"回调 {crumbHits} 次，最后一次 index={crumb}；" +
                            $"当前 {Math.Min(crumbCount, Crumbs.Length)} 层" +
                            $"（{string.Join('/', Crumbs[..Math.Min(crumbCount, Crumbs.Length)])}）")
                        .Caption()),

                // ── 快照 ──────────────────────────────────────────────────
                Panel(
                    TextBlock("控件实际值（只读探针，可视树实时采样）").BodyStrong(),
                    TextBlock(string.IsNullOrEmpty(snap) ? "(还没采到)" : snap).Caption().Wrap(),
                    TextBlock($"已渲染帧数：{ReactorLog.Frames}").Caption(),
                    HStack(8,
                        Button("刷新快照", () => setTick(tick + 1)),
                        Button(verbose ? "日志：Trace（最啰嗦）" : "日志：Info", () =>
                        {
                            ReactorLog.Level = verbose ? ReactorLogLevel.Info : ReactorLogLevel.Trace;
                            setVerbose(!verbose);
                        }),
                        Button("清空日志", () =>
                        {
                            ReactorLog.Clear();
                            setTick(tick + 1);
                        }))),

                // ── 6. 回声 / 闸门计数 ───────────────────────────────────
                // 六种增量的含义就是"这一发事件被谁处理了"，比读日志快一个量级：
                // notExpected=按用户输入放行（正常路径，后面应该跟着回调）；
                // matched=被判成回声吞掉（用户操作却涨它 = 要找的 bug）；
                // suppressed=被"未就绪/重建中"拦下（只该在页面刚出现时涨）；
                // sealed=受控写入没等到回声，登记被撤销（alpha.6 那三处修复在工作）；
                // expired=登记超窗作废；mismatch=有登记但值不等（多为真实输入的中间态）。
                Panel(
                    TextBlock("⑥ 回声 / 闸门计数（看增量）").BodyStrong(),
                    TextBlock("用法：点『记基线』→ 去点上面任一控件 → 回来看这一行。涨在哪一项，"
                              + "就是那一发事件的去向；一项都没涨，说明事件压根没到框架（看 Input 通道）。")
                        .Caption().Wrap(),
                    TextBlock(string.IsNullOrEmpty(counters) ? "(还没采到)" : counters)
                        .Caption().Wrap(),
                    TextBlock(DiagnosticsCounters.Verdict(baseline, delta)).Caption().Wrap(),
                    HStack(8,
                        Button("记基线", () => setBaseline(ReactorLog.Counters())),
                        Button(
                            polling ? "轮询：开（500ms）" : "轮询：关",
                            () => setPolling(!polling)),
                        Button("立刻读一次", () =>
                        {
                            pulseRef.Current += 1;
                            setPulse(pulseRef.Current);
                        }))),

                // ── 日志 ──────────────────────────────────────────────────
                Panel(
                    TextBlock($"框架日志：{ChanName(chan)}　最近 16 条　级别 {ReactorLog.Level}")
                        .BodyStrong(),
                    HStack(6,
                        ChanButton("全部", -1, chan, setChan),
                        ChanButton("Input 事件", (int)ReactorLogChannel.Input, chan, setChan),
                        ChanButton("Patch 下发", (int)ReactorLogChannel.Patch, chan, setChan),
                        ChanButton("Items 数据源", (int)ReactorLogChannel.Items, chan, setChan),
                        ChanButton("Render 渲染", (int)ReactorLogChannel.Render, chan, setChan)),
                    TextBlock(ReactorLog.Tail(16, chan < 0 ? null : (ReactorLogChannel)chan))
                        .Caption().Wrap(),
                    TextBlock("Pass=放行给用户回调；Gate=被闸门拦下（未就绪/重建中/回声）；" +
                              "Items=数据源下发；Patch=属性下发。判\"点了没反应\"先看 Input："
                              + "有 Pass 是链路后半段的问题，没有 Pass 是前半段。")
                        .Caption().Wrap())
            ).Padding(16));
    }

    /// <summary>
    /// <b>state 与控件实际值逐项比对，直接给结论。</b>
    /// </summary>
    /// <remarks>
    /// 这一路返工的根子，是人肉比对两行数字：state 一行、控件一行，看走眼一次
    /// 就得出假结论，然后照着假结论去改。判定交给代码——它不会看错，也不会累。
    /// <para>
    /// 只对<b>受控属性</b>判定。控件不存在（还没挂载 / 在折叠区里没展开）时给
    /// <c>?</c> 而不是 <c>✗</c>：那是"采不到样"，不是"值错了"，两者修法完全不同。
    /// </para>
    /// </remarks>
    private static (bool Bad, string Text) Verdict(
        int radio, int folded, int combo, bool sound, int crumbCount, LiveProbe.ProbeResult p)
    {
        var sb = new StringBuilder("判定 state→控件：");
        var bad = false;

        void Add(string mark)
        {
            if (mark.Contains('✗'))
            {
                bad = true;
            }

            sb.Append(' ').Append(mark);
        }

        Add(Cmp("①", radio, At(p.RadioIndex, 0)));
        Add(Cmp("②", folded, At(p.RadioIndex, 1)));
        Add(Cmp("③", combo, At(p.ComboIndex, 0)));
        Add(Cmp("④", sound, Flag(p.ToggleOn, 0)));

        // ⑤ 判的是<b>条数</b>而不是"选中项"：面包屑没有可持续的选中态，
        // 它的受控属性其实是 ItemsSource —— 而这一项正是 SectionHeader 之外
        // 唯一用引用相等判断换没换的通道（WinUI 内部 ItemsRepeater 的规矩）。
        // 所以"state→控件"在这里的等价命题是：我声明了几层，树上就得渲染出几个条目。
        // 少一个 = 新数据源没被接受 = 上面那句"面包屑不见"复发，且能直接看出来。
        Add(Cmp("⑤", Math.Min(crumbCount, Crumbs.Length), At(p.BarItems, 0)));

        return (bad, sb.ToString());
    }

    private static int? At(List<int> list, int i) => i < list.Count ? list[i] : null;

    private static bool? Flag(List<bool> list, int i) => i < list.Count ? list[i] : null;

    private static string Cmp(string name, int want, int? got) =>
        got is null
            ? $"{name}?(未采到样)"
            : got == want
                ? $"{name}✓"
                : $"{name}✗(state={want} 控件={got})";

    private static string Cmp(string name, bool want, bool? got) =>
        got is null
            ? $"{name}?(未采到样)"
            : got == want
                ? $"{name}✓"
                : $"{name}✗(state={want} 控件={got})";

    private static string ChanName(int chan) =>
        chan < 0 ? "全部通道" : ((ReactorLogChannel)chan).ToString();

    /// <summary>
    /// 通道切换按钮。当前通道用方括号标出来——否则一连串按钮分不清选中了哪个，
    /// 读数就会被人误读成"没生效"。
    /// </summary>
    private static Element ChanButton(string label, int value, int current, Action<int> setChan) =>
        Button(current == value ? $"[{label}]" : label, () => setChan(value));

    /// <summary>带内边距的分组框。</summary>
    private static Element Panel(params Element?[] children) =>
        new BorderElement(VStack(6, children))
        {
            Padding = Thick(12),
        };
}
