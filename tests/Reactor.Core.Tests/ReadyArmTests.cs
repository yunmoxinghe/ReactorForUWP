using System;
using System.Collections.Generic;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 「就绪闸」那条接线（<c>Arm</c> → 订阅 <c>Loaded</c> → 置就绪 → 回调）的模型。
/// </summary>
/// <remarks>
/// <b>建模对象是我们自己的代码，不是 WinUI 控件。</b>事实来源在本地：
/// <c>Reactor.uwp/Internal/ReadyGate.cs</c> 的 <c>Arm</c>（旧写法用
/// <c>IsReady(control)</c> 决定是否订阅），以及同一问题的另一处答复
/// <c>InputApplier.ApplyFocus</c>（它用 <c>FrameworkElement.IsLoaded</c> 判断
/// "此刻能不能 Focus"）。判据本身来自 Link 进来的 <see cref="ReadyPolicy"/>，
/// 这里仿的是<b>调用它的那段接线</b>，以及关掉它之后的旧行为（反向对照）。
/// </remarks>
internal sealed class ReadyArmSim
{
    /// <summary>true = 用 XAML 的 <c>IsLoaded</c> 当权威信号；false = 旧写法（只问自己那份标记）。</summary>
    public bool UseIsLoadedSignal { get; init; } = true;

    /// <summary>true = 记住手上有没有订阅（有就只换回调）；false = 每次 <c>Arm</c> 都再 <c>+=</c> 一个。</summary>
    public bool IdempotentSubscribe { get; init; } = true;

    /// <summary>控件此刻是否真的在可视树里。</summary>
    private bool _isLoaded;

    private bool _subscribed;
    private bool _ready;

    private int _subscriptions;
    private int _invocations;
    private int _readyMarks;
    private bool _everLoaded;
    private bool _everArmed;

    /// <summary>是否被 <c>Arm</c> 过至少一次。一次都没 Arm 过的序列不该纳入"卡死"判定。</summary>
    public bool EverArmed => _everArmed;

    /// <summary>单次 <c>Loaded</c> 最多回调了几次。&gt; 1 即重复订阅。</summary>
    public int MaxFireInOneLoad { get; private set; }

    /// <summary>是否已就绪。</summary>
    public bool Ready => _ready;

    /// <summary>实际 <c>Loaded +=</c> 的次数。</summary>
    public int Subscriptions => _subscriptions;

    /// <summary>回调被触发的次数。</summary>
    public int Invocations => _invocations;

    /// <summary>计入"进入就绪"的次数（对应 <c>ReadyStats.Ready</c>）。</summary>
    public int ReadyMarks => _readyMarks;

    /// <summary>控件是否被加载过至少一次。</summary>
    public bool EverLoaded => _everLoaded;

    /// <summary>一次 <c>Arm</c>（= handler 的 Mount 里那一行）。</summary>
    public void Arm()
    {
        _everArmed = true;
        Apply(Decide());
    }

    /// <summary>控件进可视树：<c>Loaded</c> 抛一次，所有挂着的订阅各跑一遍。</summary>
    public void Load()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        _everLoaded = true;

        // 一个 Loaded 事件只允许回调一次。重复订阅（Arm 调了 N 次）会让这里跑 N 遍：
        // 回调内容是"重放受控值"，幂等，所以后果是读数虚高 + Controls 重复计入，
        // 而不是立刻可见的故障——正因为安静，才要在这里拦。
        if (_subscriptions > MaxFireInOneLoad)
        {
            MaxFireInOneLoad = _subscriptions;
        }

        for (var i = 0; i < _subscriptions; i++)
        {
            FireOnce();
        }

        _subscriptions = 0;
        _subscribed = false;
    }

    /// <summary>控件出树。注意 WinUI 那边不会退回未就绪，这里也不重置 <c>_ready</c>。</summary>
    public void Unload() => _isLoaded = false;

    /// <summary>当前的判据。<see cref="UseIsLoadedSignal"/> 关掉时退回旧写法。</summary>
    private ReadyArmAction Decide()
    {
        if (!UseIsLoadedSignal)
        {
            // 旧写法：只问自己那份标记，不看 IsLoaded，也没有"有没有订阅"的概念。
            return _ready ? ReadyArmAction.ReadyOnce : ReadyArmAction.Arm;
        }

        return ReadyPolicy.Decide(_isLoaded, _ready, IdempotentSubscribe && _subscribed);
    }

    private void Apply(ReadyArmAction action)
    {
        if (ReadyPolicy.ShouldSubscribe(action))
        {
            _subscribed = true;
            _subscriptions++;
            return;
        }

        if (!ReadyPolicy.ShouldInvokeNow(action))
        {
            return;
        }

        if (ReadyPolicy.MarksReady(action) && !_ready)
        {
            _ready = true;
            _readyMarks++;
        }

        _invocations++;
    }

    private void FireOnce()
    {
        if (!_ready)
        {
            _ready = true;
            _readyMarks++;
        }

        _invocations++;
    }
}

/// <summary>
/// 就绪闸：<c>Arm</c> 的四种动作，以及关掉任一半之后该红的必须红。
/// </summary>
internal static class ReadyArmTests
{
    private static readonly string[] StepNames = { "Arm", "Load", "Unload" };

    public static void Run()
    {
        Program.Section("就绪闸 / Arm 的动作");

        Exhaustive();
        Sequences();
        ReadyPredicate();
    }

    /// <summary>
    /// 「算不算就绪」的判据：<b>不能只看我们自己那份标记</b>。
    /// </summary>
    /// <remarks>
    /// 那份标记<b>唯一的写入者是 <c>Loaded</c> 事件的回调</b>。而 UWP/WinUI 的
    /// <c>Loaded</c>/<c>Unloaded</c> 有乱序与不配对的已知问题：同一个 UI pass 内
    /// remove 再 add 回树，XAML <b>只发 <c>Unloaded</c>、不发 <c>Loaded</c></b>
    /// （XamlBehaviors#251；乱序见 Win2D#954）。于是标记永远是 false，
    /// 而"未就绪"的语义是"这期间的事件一发都不放行" —— 控件在树上、用户点得到，
    /// 却一发都不响应，且不报任何错。
    /// <para>
    /// 修法：<b>或上控件此刻的 <c>IsLoaded</c></b>。它答的是同一个问题，且"此刻
    /// 在树上"必然蕴含"WinUI 已解禁 <c>m_blockSelecting</c>"（控件自己的 Loaded
    /// 抛出来时模板子树已经就位，WinUI 正是那一刻解禁的）。
    /// </para>
    /// <para>
    /// 折叠区展开、虚拟化回收、<c>Frame</c> 切页都会踩 remove→add 这种形状。
    /// </para>
    /// </remarks>
    private static void ReadyPredicate()
    {
        Program.Section("就绪闸 / 算不算就绪（不能只看自己那份标记）");

        Program.Check(
            "标记已置 → 就绪（哪怕此刻离树：m_blockSelecting 不会变回去）",
            ReadyPolicy.IsReady(marked: true, isLoaded: false, trustLive: true));

        Program.Check(
            "标记未置、但此刻在树 → 就绪（Loaded 不会来了，不能干等）",
            ReadyPolicy.IsReady(marked: false, isLoaded: true, trustLive: true));

        Program.Check(
            "标记未置、此刻也不在树 → 未就绪（真的还没进过树）",
            !ReadyPolicy.IsReady(marked: false, isLoaded: false, trustLive: true));

        Program.Check(
            "两者都成立 → 就绪",
            ReadyPolicy.IsReady(marked: true, isLoaded: true, trustLive: true));

        // 反向对照：关掉开关必须回到旧行为 —— 它红了才说明这一条摸到了真病。
        Program.Check(
            "反向对照（关掉开关）：标记未置就一律未就绪，哪怕此刻在树",
            !ReadyPolicy.IsReady(marked: false, isLoaded: true, trustLive: false));

        Program.Check(
            "反向对照（关掉开关）：标记已置仍然就绪（旧行为不变）",
            ReadyPolicy.IsReady(marked: true, isLoaded: false, trustLive: false));
    }

    /// <summary>八个二进制输入 → 期望动作。判据是纯函数，直接穷举。</summary>
    private static void Exhaustive()
    {
        Program.Check(
            "已在树（此前未就绪）→ Already：立刻就绪，不挂订阅",
            ReadyPolicy.Decide(isLoaded: true, alreadyReady: false, subscribed: false) == ReadyArmAction.Already);

        Program.Check(
            "已在树（此前已就绪）→ 仍然是 Already（IsLoaded 优先）",
            ReadyPolicy.Decide(isLoaded: true, alreadyReady: true, subscribed: false) == ReadyArmAction.Already);

        Program.Check(
            "已在树且有订阅 → Already（不该让旧订阅再跑一次回调）",
            ReadyPolicy.Decide(isLoaded: true, alreadyReady: false, subscribed: true) == ReadyArmAction.Already);

        Program.Check(
            "未进树、有订阅 → Keep（只换回调，不重复 +=）",
            ReadyPolicy.Decide(isLoaded: false, alreadyReady: false, subscribed: true) == ReadyArmAction.Keep);

        Program.Check(
            "未进树、曾就绪、无订阅 → ReadyOnce（回调但不重复计数）",
            ReadyPolicy.Decide(isLoaded: false, alreadyReady: true, subscribed: false) == ReadyArmAction.ReadyOnce);

        Program.Check(
            "首次 → Arm",
            ReadyPolicy.Decide(isLoaded: false, alreadyReady: false, subscribed: false) == ReadyArmAction.Arm);

        Program.Check(
            "只有 Arm 才该订阅",
            ReadyPolicy.ShouldSubscribe(ReadyArmAction.Arm)
            && !ReadyPolicy.ShouldSubscribe(ReadyArmAction.Already)
            && !ReadyPolicy.ShouldSubscribe(ReadyArmAction.Keep)
            && !ReadyPolicy.ShouldSubscribe(ReadyArmAction.ReadyOnce));

        Program.Check(
            "ReadyOnce 不计入就绪数（早就绪过，不该把读数虚高）",
            ReadyPolicy.MarksReady(ReadyArmAction.Already)
            && !ReadyPolicy.MarksReady(ReadyArmAction.ReadyOnce)
            && !ReadyPolicy.MarksReady(ReadyArmAction.Keep)
            && !ReadyPolicy.MarksReady(ReadyArmAction.Arm));
    }

    /// <summary>
    /// 随机序列：反复 <c>Arm</c> / 进树 / 出树，要求三条不变式恒成立。
    /// </summary>
    private static void Sequences()
    {
        const int Count = 20000;
        const int Steps = 12;

        Directed();

        // 每个反对照只被要求打破<b>它针对性</b>的那几条。反过来要求"每个反对照每条都红"
        // 是假纪律：那会把本来正确的写法也判成有罪，最后要么删断言要么放松判据，防线作废。
        var shapes = new (string Name, bool IsLoaded, bool Idempotent, string[] Breaks)[]
        {
            ("新写法（20000 条随机序列）", true, true, Array.Empty<string>()),
            ("反对照：不看 IsLoaded（20000 条随机序列）", false, true, new[] { "重订阅", "卡死" }),
            ("反对照：不幂等订阅（20000 条随机序列）", true, false, new[] { "重订阅" }),
        };

        foreach (var shape in shapes)
        {
            var repeats = new Dictionary<string, int>
            {
                ["重订阅"] = 0, ["卡死"] = 0, ["重回调"] = 0, ["虚高"] = 0,
            };

            var samples = new Dictionary<string, string>();

            for (var seed = 0; seed < Count; seed++)
            {
                var rng = new Random(seed * 31 + Steps);
                var sim = new ReadyArmSim
                {
                    UseIsLoadedSignal = shape.IsLoaded,
                    IdempotentSubscribe = shape.Idempotent,
                };

                var tape = new List<string>();

                for (var step = 0; step < Steps; step++)
                {
                    var move = StepNames[rng.Next(StepNames.Length)];
                    tape.Add(move);

                    switch (move)
                    {
                        case "Arm":
                            sim.Arm();
                            break;
                        case "Load":
                            sim.Load();
                            break;
                        default:
                            sim.Unload();
                            break;
                    }
                }

                // 收尾：让它至少进过一次树，否则"是否卡死"这个问题本身不成立。
                tape.Add("Load");
                sim.Load();

                if (!sim.EverArmed)
                {
                    continue;
                }

                Note(repeats, samples, "重订阅", sim.MaxFireInOneLoad > 1, seed, tape);
                Note(repeats, samples, "卡死", sim.EverLoaded && !sim.Ready, seed, tape);
            }

            var modern = shape.Breaks.Length == 0;

            foreach (var kind in new[] { "重订阅", "卡死" })
            {
                if (!modern && Array.IndexOf(shape.Breaks, kind) < 0)
                {
                    continue;
                }

                Program.Check(
                    $"{shape.Name}：{kind}（违规 {repeats[kind]} 条）",
                    modern ? repeats[kind] == 0 : repeats[kind] > 0,
                    Detail(modern, repeats[kind], samples.TryGetValue(kind, out var s) ? s : null));
            }
        }
    }

    /// <summary>
    /// 两条定向场景，各自对应 fuzz 不好覆盖的一点。
    /// </summary>
    private static void Directed()
    {
        // ① 控件已经在树上了才 Arm：必须当场就绪。
        //    旧的写法在这里会去挂一个永远不会来的 Loaded —— 控件就此永久停留在
        //    "未就绪"，那期间的事件全被判据一吞掉，界面表现就是"点了没反应"。
        var loadedFirst = new ReadyArmSim();
        loadedFirst.Load();
        loadedFirst.Arm();

        Program.Check(
            "已在树上才 Arm：当场就绪，不必等下一次 Loaded",
            loadedFirst.Ready && loadedFirst.ReadyMarks == 1,
            $"ready={loadedFirst.Ready} marks={loadedFirst.ReadyMarks}");

        // ② 就绪之后再怎么 Arm / 出树，就绪计数都不许涨。
        //    它是"有几个控件真的进过树"，涨上去就变成"被 Arm 了多少次"。
        var repeat = new ReadyArmSim();
        repeat.Load();
        repeat.Arm();

        for (var i = 0; i < 5; i++)
        {
            repeat.Unload();
            repeat.Arm();
        }

        repeat.Load();
        repeat.Arm();

        Program.Check(
            "就绪后反复 Arm：就绪计数始终为 1",
            repeat.ReadyMarks == 1,
            $"marks={repeat.ReadyMarks}");
    }

    /// <summary>
    /// 失败时才给细节。<b>反向对照那一侧的"没违规"也算失败</b>——
    /// 它说明这条不变式根本抓不住那种坏法，所谓修复就没有证据。
    /// </summary>
    private static string? Detail(bool modern, int violations, string? sample)
    {
        if (modern && violations > 0)
        {
            return sample;
        }

        return !modern && violations == 0 ? "旧写法居然没违规 —— 这条不变式抓不住它" : null;
    }

    /// <summary>记一次违规，并把第一条违规的<b>操作序列</b>留下来当现场证据。</summary>
    private static void Note(
        Dictionary<string, int> counts,
        Dictionary<string, string> samples,
        string kind,
        bool happened,
        int seed,
        List<string> tape)
    {
        if (!happened)
        {
            return;
        }

        counts[kind]++;

        if (!samples.ContainsKey(kind))
        {
            samples[kind] = $"seed={seed} 序列=[{string.Join(' ', tape)}]";
        }
    }
}
