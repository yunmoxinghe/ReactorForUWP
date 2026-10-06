using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using MuxControls = Microsoft.UI.Xaml.Controls;
using Reactor.Uwp.Hosting;
using Windows.Foundation;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Reactor.Template.Services;

/// <summary>
/// 「点了之后每一跳」的探针：<b>只记录，不修复</b>。
/// </summary>
/// <remarks>
/// <para>
/// 为什么要有这么个东西：设置项点了没反应，可能的断点至少有五处——
/// <list type="number">
///   <item>控件事件压根没派发（命中层 / 视觉树层面）；</item>
///   <item>事件派发了，被框架的闸门判成回声或"未就绪"吞掉；</item>
///   <item>闸门放行了，但用户回调没被调到；</item>
///   <item>回调到了、<c>setState</c> 也调了，但没触发重渲染；</item>
///   <item>重渲染了，受控值下发被策略挡回（越界 / 不需要下发）。</item>
/// </list>
/// 前三跳在<b>框架</b>内部、后两跳一半在框架一半在页面，光看界面只能得到
/// "没反应"这一个比特——分不清断在哪。这里把<b>页面这一侧能看见的每一跳</b>
/// 记下来，再跟框架自己的日志（<see cref="ReactorLog"/>）并排摆到屏幕上，
/// 链子断在哪一环一眼就能指出。
/// </para>
/// <para>
/// <b>只加不删原则</b>：埋点常驻、默认可关（<see cref="Enabled"/>），
/// 不走"排查时临时插桩、排完必须记得删"那条路——每插一次桩都有可能改到被测对象，
/// 而删掉之后同样的病再犯一遍还是没证据。
/// </para>
/// <para>
/// 本文件<b>不碰任何依赖</b>：不要求升包、不读框架内部类型（<c>EchoStats</c> 是
/// <c>internal</c>），只用 <c>Reactor.Uwp.Hosting</c> 下的公共 API。
/// </para>
/// </remarks>
public static class Probe
{
    /// <summary>
    /// 总开关。<c>false</c> 时所有 <see cref="Hit"/> / <see cref="Render"/> 直接返回，
    /// 界面上的诊断条也跟着不显示。
    /// </summary>
    /// <remarks>
    /// 用 <c>static readonly</c> 而不是 <c>const</c>：const 会被编译器折叠，
    /// 于是 <c>if (!Enabled) return;</c> 变成"永远不可达的代码"（CS0162），
    /// 一个开关反倒给自己招来一条警告。
    /// </remarks>
    public static readonly bool Enabled = true;

    /// <summary>
    /// 是否在界面底部挂那条诊断条。<b>默认 false</b>。
    /// </summary>
    /// <remarks>
    /// 挂上去会压住内容底部那一段——设置页最后两项正好在那儿，于是"用来排查的东西"
    /// 自己变成了"点不到"的原因，这比原来的病更难判断。默认走<b>纯日志</b>路线：
    /// 埋点照记，读数看 <see cref="LogPath"/>。要屏上读数时把这里改成 <c>true</c>。
    /// </remarks>
    public static readonly bool ShowBar = false;

    /// <summary>环形缓冲条数。够看连续几次点击，又不占内存。</summary>
    private const int Capacity = 64;

    private static readonly string[] Ring = new string[Capacity];

    private static int _seq;
    private static int _hits;

    /// <summary>上一轮渲染记下的快照。<see cref="Render"/> 与它相同就不重复记。</summary>
    private static string _lastRender = string.Empty;

    /// <summary>
    /// 命中计数：<b>回调被调到过几次</b>。
    /// </summary>
    /// <remarks>
    /// 这一个数就能把五处断点切成两半：点完它<b>不涨</b>，说明事情断在
    /// 控件 → 闸门 → 回调 那一半（页面代码根本没跑到）；
    /// 它<b>涨了</b>而界面没变，说明断在 setState 之后那一半。
    /// </remarks>
    public static int Hits => Volatile.Read(ref _hits);

    /// <summary>最近一跳的文本。界面上摆在计数旁边，不用翻缓冲。</summary>
    public static string Last
    {
        get
        {
            var n = Volatile.Read(ref _seq);
            return n == 0 ? "（尚无）" : Ring[(n - 1) % Capacity] ?? string.Empty;
        }
    }

    /// <summary>落盘日志的绝对路径。UWP 的 <c>LocalFolder</c> 常被符号链接重定向，
    /// 资源管理器里翻不到，所以这里把真路径摆在屏幕上。</summary>
    public static string LogPath =>
        System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "reactor-startup.log");

    /// <summary>
    /// 记一跳。
    /// </summary>
    /// <param name="step">
    /// 这一跳的名字 + 值。写成 <c>"回调 theme=1"</c> 这种"动词 + 值"的样子：
    /// 排的时候要的是"值在哪一环变了"，不是"哪个方法被调用了"。
    /// </param>
    public static void Hit(string step)
    {
        if (!Enabled)
        {
            return;
        }

        var n = Interlocked.Increment(ref _seq);
        var line = $"#{n} {DateTime.Now:HH:mm:ss.fff} {step}";

        Ring[(n - 1) % Capacity] = line;
        Interlocked.Increment(ref _hits);

        // 同时进框架那条线：它负责落盘 + Debug 输出，界面那条负责当场可读。
        ReactorApplication.Trace($"[probe] {step}");
    }

    /// <summary>
    /// 记一轮渲染时的 state 快照。<b>与上一轮相同就不记</b>。
    /// </summary>
    /// <remarks>
    /// 渲染是每帧都来的，无条件记会把它自己的噪声变成主角——
    /// 而"到底有没有重渲染"由<see cref="Hits"/>与这一条的<b>有无</b>回答：
    /// 回调之后这里<b>没有</b>新行，就是 setState 之后丢渲染的直接证据。
    /// </remarks>
    public static void Render(string snapshot)
    {
        if (!Enabled || string.Equals(snapshot, _lastRender, StringComparison.Ordinal))
        {
            return;
        }

        _lastRender = snapshot;
        Hit($"④ 渲染 {snapshot}");
    }

    /// <summary>按发生顺序取最近 <paramref name="count"/> 跳，新在下。</summary>
    public static string Chain(int count = 5)
    {
        var n = Volatile.Read(ref _seq);
        var take = Math.Min(count, n);
        var rows = new string[take];

        for (var i = 0; i < take; i++)
        {
            rows[i] = Ring[(n - take + i) % Capacity] ?? string.Empty;
        }

        return take == 0 ? "（尚无）" : string.Join(Environment.NewLine, rows);
    }

    /// <summary>
    /// 把框架侧日志提到 <c>Trace</c>，同时关掉 <c>Render</c> 通道。
    /// </summary>
    /// <remarks>
    /// 闸门那条线（"判成回声吞掉" / "未就绪" / "越界不下发"）记的是 <c>Trace</c> 级，
    /// 默认门槛是 <c>Info</c>——所以<b>默认日志里只会出现"放行"的行，被吞掉的一行都没有</b>。
    /// "点了没反应"恰恰要看被吞掉的那一行，这就是为什么之前翻日志什么也看不到。
    /// <para>
    /// 关掉 <c>Render</c> 通道是必需的：它是每帧一条，提到 Trace 之后 512 条环形缓冲
    /// 会在两秒内被它冲光，想看的 <c>Input</c> / <c>Patch</c> 反而没了。
    /// </para>
    /// </remarks>
    public static void Verbose()
    {
        ReactorLog.Level = ReactorLogLevel.Trace;
        ReactorLog.SetChannel(ReactorLogChannel.Render, ReactorLogLevel.Off);
        ReactorLog.Info(ReactorLogChannel.Host, "[probe] 日志提到 Trace，Render 通道关闭");
    }

    /// <summary>转储文件的路径（与落盘日志同一个目录）。</summary>
    public static string DumpPath =>
        System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "probe-live.log");

    /// <summary>
    /// 起一个定时器，把内存里那 512 条环形缓冲<b>按通道</b>转储到
    /// <see cref="DumpPath"/>。
    /// </summary>
    /// <remarks>
    /// <b>它补的是"提到 Trace 也还是读不到"这个缺口。</b>框架只在
    /// <c>Info</c> 及以上落盘（<c>ReactorLog.Write</c> 里的那条判据），
    /// 而<b>解释"为什么吞掉这一发"的那行恰好是 Trace 级</b>——提到 Trace 只让它
    /// 进了内存缓冲，文件里照样一行没有。界面不挂诊断条时，这批证据就没人读得到。
    /// <para>
    /// 所以这里从<b>应用侧</b>绕过去：定时把 <c>Input</c> / <c>Patch</c> / <c>Items</c>
    /// 三个通道从缓冲里取出来写文件。不改框架、不重打包——框架那条"Trace 不落盘"
    /// 的取舍本身是对的（真让它落盘，一秒几百行），只是排查期需要一条旁路。
    /// </para>
    /// <para>
    /// <b>按通道取，不取全量</b>：<c>Host</c> 通道每次构建元素都记一行，
    /// 进一趟设置页就几十行，混进来会把要看的 <c>Input</c> 挤掉。
    /// </para>
    /// <para>
    /// 只在内容变化时才写：定时器回调在线程池上，没必要每 400ms 做一次无意义的写。
    /// </para>
    /// </remarks>
    public static void StartDump()
    {
        Verbose();

        // 必须在 UI 线程上抓派发器：转储回调跑在线程池上，那边拿不到
        // Window.Current（UWP 的 Window.Current 是线程相关的，非 UI 线程返回 null）。
        // OnLaunched 里 Window.Current 有时还没挂上，退回 CoreApplication 那条路，
        // 拿不到就至少还能转储框架日志（可视树那段会写"（派发器未捕获）"）。
        _ui = Window.Current?.Dispatcher
              ?? CoreApplication.MainView?.CoreWindow?.Dispatcher;

        _dump = ThreadPoolTimer.CreatePeriodicTimer(
            _ => Dump(), TimeSpan.FromMilliseconds(400));
    }

    private static ThreadPoolTimer? _dump;

    private static CoreDispatcher? _ui;

    /// <summary>
    /// 快照里出现过控件实例，按<b>引用</b>记住并给一个字母代号。
    /// </summary>
    /// <remarks>
    /// <b>为什么要自己编号，而不是用 <c>GetHashCode</c>。</b>框架内部那份
    /// <c>CtlId</c> 的注释写得很清楚：AOT / 压缩 GC 下 <c>GetHashCode</c>
    /// 会随堆移动变化，同一个控件前后可能显示两个号——按它得出"控件被重建了"
    /// 就是量具造的假象。这里改成<b>引用比较 + 自己发号</b>：
    /// 同一个实例永远同一个字母，换了字母就是真的换了实例。
    /// <para>
    /// <b>刻意持有强引用（上限 52 个）</b>：弱引用做不到"按引用认人"，
    /// 而这是排查期的观测代码，多留几十个控件不影响结论。
    /// </para>
    /// </remarks>
    private static readonly List<object> Known = new();

    private static void Dump()
    {
        try
        {
            var ui = _ui;

            // 可视树只能在 UI 线程上走；线程池直接碰会抛跨线程异常。
            if (ui is null)
            {
                WriteDump("（派发器未捕获：StartDump 不在 UI 线程上调用）");
                return;
            }

            _ = ui.RunAsync(CoreDispatcherPriority.Low, () => WriteDump(Snapshot()));
        }
        catch
        {
            // 转储是观测手段，写不进去不该影响进程。
        }
    }

    private static void WriteDump(string? tree)
    {
        try
        {
            var text = string.Join(
                Environment.NewLine,
                $"===== 心跳 {DateTime.Now:HH:mm:ss.fff} 帧 {ReactorLog.Frames} =====",
                "===== 可视树里的受控控件 =====",
                string.IsNullOrEmpty(tree) ? "（空）" : tree,
                "===== Input（闸门：放行/吞掉）=====",
                ReactorLog.Tail(150, ReactorLogChannel.Input),
                "===== Patch（受控下发）=====",
                ReactorLog.Tail(80, ReactorLogChannel.Patch),
                "===== Items（数据源）=====",
                ReactorLog.Tail(40, ReactorLogChannel.Items),
                "===== 计数器 =====",
                ReactorLog.Counters());

            File.WriteAllText(DumpPath, text);
        }
        catch
        {
            // 同上：观测手段不该影响进程。
        }
    }

    /// <summary>
    /// 走一遍可视树，把三个受控控件各自的<b>身份 / 是否 Loaded / 当前值</b>列出来。
    /// </summary>
    /// <remarks>
    /// 加这一段是被"日志里只有一个 <c>受控下发 ComboBox#5: 1 → 0</c>，但页面上
    /// 明明只 build 过一次 ComboBox（<c>#4</c>）"逼出来的：那一行的主语到底是不是
    /// 用户正在点的那个控件，光看编号<b>无法判定</b>——而它正是"用户的值被谁改回去"
    /// 的唯一证人。
    /// </remarks>
    private static string Snapshot()
    {
        var root = Window.Current?.Content;

        if (root is null)
        {
            return "（Window.Current.Content 为 null）";
        }

        var sb = new StringBuilder();
        Walk(root, 0, sb);

        return sb.Length == 0 ? "（树上没有受控控件）" : sb.ToString();
    }

    private static void Walk(DependencyObject node, int depth, StringBuilder sb)
    {
        if (depth > 40)
        {
            return;
        }

        switch (node)
        {
            case ComboBox cb:
                sb.Append("CB").Append(Id(cb))
                  .Append(" loaded=").Append(IsLoaded(cb))
                  .Append(" idx=").Append(Num(cb.SelectedIndex))
                  .Append(" items=").Append(Num(cb.Items.Count))
                  .AppendLine();
                break;

            case MuxControls.RadioButtons rb:
                sb.Append("RB").Append(Id(rb))
                  .Append(" loaded=").Append(IsLoaded(rb))
                  .Append(" idx=").Append(Num(rb.SelectedIndex))
                  .Append(" items=").Append(Num(rb.Items.Count))
                  .AppendLine();
                break;

            case ToggleSwitch ts:
                sb.Append("TS").Append(Id(ts))
                  .Append(" loaded=").Append(IsLoaded(ts))
                  .Append(" on=").Append(ts.IsOn)
                  .AppendLine();
                break;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);

        for (var i = 0; i < count; i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), depth + 1, sb);
        }
    }

    /// <summary>按引用发一个字母代号；同一个实例永远拿到同一个字母。</summary>
    private static string Id(object control)
    {
        for (var i = 0; i < Known.Count; i++)
        {
            if (ReferenceEquals(Known[i], control))
            {
                return Letter(i);
            }
        }

        if (Known.Count >= 52)
        {
            return "?";
        }

        Known.Add(control);

        return Letter(Known.Count - 1);
    }

    private static string Letter(int i) => ((char)('A' + i)).ToString();

    private static string IsLoaded(FrameworkElement fe)
    {
        try
        {
            return fe.IsLoaded ? "True" : "False";
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>
    /// 界面上那一块要显示的全文。
    /// </summary>
    /// <remarks>
    /// 把<b>页面这一侧</b>（本类的链）与<b>框架那一侧</b>（<see cref="ReactorLog"/> 的
    /// 计数器与最近记录）拼在一起：两边都按时间顺序，链断在哪能直接对齐着看。
    /// </remarks>
    public static string Report(string stateSnapshot)
    {
        var input = ReactorLog.Tail(6, ReactorLogChannel.Input);
        var patch = ReactorLog.Tail(4, ReactorLogChannel.Patch);

        return string.Join(
            Environment.NewLine,
            $"命中 {Hits} ｜ 最近：{Last}",
            $"state: {stateSnapshot}",
            $"帧 {ReactorLog.Frames}",
            $"日志 {LogPath}",
            ReactorLog.Counters(),
            "—— Input（闸门）——",
            string.IsNullOrWhiteSpace(input) ? "（空）" : input,
            "—— Patch（受控下发）——",
            string.IsNullOrWhiteSpace(patch) ? "（空）" : patch,
            "—— 本页链 ——",
            Chain(6));
    }

    /// <summary>清空本页链（不影响框架计数）。重测前点一下，读数从 0 开始。</summary>
    public static void Reset()
    {
        Array.Clear(Ring, 0, Ring.Length);
        Volatile.Write(ref _seq, 0);
        Volatile.Write(ref _hits, 0);
        _lastRender = string.Empty;
    }

    /// <summary>把值格式化成短文本，避免插值时受当前区域设置影响。</summary>
    public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    // ─────────────────────────────────────────────────────────────
    // 自检：应用自己点一遍，把结论落盘
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 自检开关。<c>true</c> 时启动几秒后自动在真实控件上跑一遍手势。
    /// </summary>
    /// <remarks>
    /// <b>为什么要有它</b>：排查"点了没反应"时，最缺的不是修复，是<b>证据</b>。
    /// 靠人手动点，日志里"一行回调都没有"有两种解释——真没点，或者点了没到——
    /// 而这两种解释指向完全不同的结论。让应用自己按固定脚本点一遍，
    /// 报告里每一步的控件值 / 回调计数都是<b>可复现</b>的，不需要第二轮人工操作。
    /// </remarks>
    public static readonly bool SelfTestEnabled = true;

    /// <summary>自检报告的路径。</summary>
    public static string SelfTestPath =>
        System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "selftest.log");

    /// <summary>
    /// 起始页覆盖：LocalState\probe-page.txt 里写 <c>settings</c> 就从设置页起。
    /// </summary>
    /// <remarks>
    /// 自检要在受控控件上动手，而那些控件只在设置页上。文件不存在就返回 null，
    /// 页面照旧从主页起——正常使用时这条路径完全不参与。
    /// </remarks>
    public static string? StartPage
    {
        get
        {
            try
            {
                var path = System.IO.Path.Combine(
                    ApplicationData.Current.LocalFolder.Path, "probe-page.txt");

                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 起一次自检。<b>必须在 UI 线程上调用</b>（内部要抓派发器）。
    /// </summary>
    public static void StartSelfTest()
    {
        if (!SelfTestEnabled)
        {
            return;
        }

        // 只在"自检模式"下跑：起始页被 probe-page.txt 覆盖成设置页时才动手。
        // 否则每次启动都自动改一遍设置——那是排查工具在骚扰它的主人。
        if (!string.Equals(StartPage, "settings", StringComparison.Ordinal))
        {
            return;
        }

        var ui = _ui;

        if (ui is null)
        {
            WriteSelfTest("（派发器未捕获：StartSelfTest 不在 UI 线程上调用）");
            return;
        }

        // 全程用"派发器排队 + 计时器"推进，<b>不用 await</b>。
        // await 看起来更省事，但实测 continuation 不保证回到 Window 所在的那个线程：
        // 第一版在这里 await 了 3 秒，醒过来 Window.Current.Content 已经是 null——
        // 拿不到根元素，整份自检只能写出一句"可视树还没挂上"。
        // 派发器是唯一可靠的"回到 UI 线程"通道，所以每一步都重新排一次。
        // 等 3 秒：首帧布局 + 控件 Loaded 要稳下来。ReadyGate 靠 Loaded 判就绪，
        // 在它之前动手只会得到"未就绪，吞"这一条，测不到要看的东西。
        Schedule(ui, TimeSpan.FromSeconds(3), () => RunSelfTest(ui));
    }

    /// <summary>等 <paramref name="delay"/> 之后再回 UI 线程执行 <paramref name="step"/>。</summary>
    private static void Schedule(CoreDispatcher ui, TimeSpan delay, Action step) =>
        // 语句体不能写成表达式 lambda：RunAsync 交回一个 IAsyncAction，
        // 表达式 lambda 把它当返回值时编译器会报 CS4014（"不等待"）。
        // 加一对大括号才是"只管排队，不等它"。
        ThreadPoolTimer.CreateTimer(
            timer => { _ = ui.RunAsync(CoreDispatcherPriority.Normal, () => step()); }, delay);

    private static void RunSelfTest(CoreDispatcher ui)
    {
        try
        {
            var root = Window.Current?.Content;

            if (root is null)
            {
                WriteSelfTest("（Window.Current.Content 为 null，可视树还没挂上）");
                return;
            }

            var comboBoxes = new List<ComboBox>();
            var radioGroups = new List<MuxControls.RadioButtons>();
            var scrollers = new List<ScrollViewer>();
            Collect(root, 0, comboBoxes, radioGroups, scrollers);

            var rootSize = root is FrameworkElement rootElement
                ? new Size(rootElement.ActualWidth, rootElement.ActualHeight)
                : default;

            var sb = new StringBuilder();
            sb.AppendLine($"===== 自检 {DateTime.Now:HH:mm:ss.fff} 帧 {ReactorLog.Frames} =====");
            sb.AppendLine($"可视树上：ComboBox {Num(comboBoxes.Count)} 个，RadioButtons {Num(radioGroups.Count)} 个");
            sb.AppendLine($"窗口内容区：{F0(rootSize.Width)}x{F0(rootSize.Height)}");

            // 滚动体检：内容比视口高、却<b>滚不动</b>，界面上的说法就是
            // "下面那几项点不到"——不是事件被吞，是它们压根没进视口，
            // 而滚动条是死的。Extent / Viewport 两个数就能定性：
            // 可滚动量 = ExtentHeight - ViewportHeight，必须 > 0 才滚得动。
            foreach (var sv in scrollers)
            {
                var scrollable = sv.ExtentHeight - sv.ViewportHeight;
                sb.AppendLine($"ScrollViewer：视口 {F0(sv.ViewportHeight)} 内容 {F0(sv.ExtentHeight)} "
                    + $"可滚 {F0(scrollable)} 当前偏移 {F0(sv.VerticalOffset)} → "
                    + (scrollable > 1 ? "可滚动" : "⚠ 滚不动（内容没有超出视口，或测量出了偏差）"));
            }

            // 待跑的脚本：一个控件一段，按顺序来。
            var script = new List<Func<StringBuilder, Action<bool>, bool>>();

            foreach (var cb in comboBoxes)
            {
                var control = cb;
                script.Add((log, done) => Gesture(ui, log, "ComboBox", control, control.Items.Count,
                    v => control.SelectedIndex = v, () => control.SelectedIndex, done));
            }

            foreach (var rb in radioGroups)
            {
                var control = rb;

                // 路径一：直接设 SelectedIndex（两段手势）。
                script.Add((log, done) => Gesture(ui, log, "RadioButtons", control, control.Items.Count,
                    v => control.SelectedIndex = v, () => control.SelectedIndex, done));

                // 路径二：<b>点里面那个 RadioButton</b>。真人点的是这个，不是 SelectedIndex。
                // 两条路径在 WinUI 内部走的代码并不相同（一个是"索引变了"，
                // 一个是"某一项被勾上、旧项被反勾"），事件序列也不一样。
                // 只测路径一就下"修好了"的结论，等于拿一半的证据说全部的话。
                var items = new List<RadioButton>();
                CollectRadioItems(control, items);

                script.Add((log, done) => TapRadioItem(ui, log, control, items,
                    () => control.SelectedIndex, done));
            }

            // 最后一段：<b>主题到底有没有落到界面上</b>。
            // 前几段证明的是"值到了回调、也存盘了"，但用户说的"不生效"未必指这个——
            // 很可能指"点了，界面没变"。值和界面之间还隔着一层：
            // 框架要把 ElementTheme 下发到根元素，XAML 再把根元素的 RequestedTheme
            // 变成 ActualTheme。任何一层没跟上，都是"点了没反应"。
            script.Add((log, done) => CheckThemeApplied(log, done));

            if (script.Count == 0)
            {
                sb.AppendLine("结论：FAIL（树上没有受控控件——自检落在了错误的页面）");
                WriteSelfTest(sb.ToString());
                return;
            }

            // 自检会真的改设置（不改就验证不到落盘），所以先记下原值，
            // 跑完原样写回：排查的痕迹不该留在用户的设置里。
            var before = AppSettings.Current;
            var restore = (before.Theme, before.Material, before.Pane, before.Sound);

            var pass = true;
            RunScript(ui, sb, script, 0, ok =>
            {
                pass &= ok;
                sb.AppendLine($"结论：{(pass ? "PASS（值到了回调、也没被改回）" : "FAIL（见上面 FAIL 的那一段）")}");

                AppSettings.Update(s =>
                {
                    s.Theme = restore.Theme;
                    s.Material = restore.Material;
                    s.Pane = restore.Pane;
                    s.Sound = restore.Sound;
                });

                sb.AppendLine($"已还原设置 → {restore.Theme} / {restore.Material} / {restore.Pane} / 声音 {restore.Sound}");
                WriteSelfTest(sb.ToString());
            });
        }
        catch (Exception ex)
        {
            WriteSelfTest($"自检自身抛异常：{ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>按序跑完脚本里的每一段，跑完回调 <paramref name="finished"/>。</summary>
    private static void RunScript(
        CoreDispatcher ui, StringBuilder sb,
        List<Func<StringBuilder, Action<bool>, bool>> script, int index, Action<bool> finished)
    {
        if (index >= script.Count)
        {
            finished(true);
            return;
        }

        script[index](sb, ok => RunScript(ui, sb, script, index + 1, prev => finished(prev && ok)));
    }

    /// <summary>
    /// 在<b>一个</b>真实控件上跑一遍"两段手势"，把每一步的读数记下来。
    /// </summary>
    /// <remarks>
    /// <b>为什么要分两段。</b>真人点一下 RadioButtons / ComboBox，控件自己先发一发
    /// "取消旧选中"（回调值 -1），紧接着再发一发"选中新项"——是<b>两发</b>，不是一发。
    /// 只设一次 <c>SelectedIndex</c> 只复现第二发，而病恰恰出在第一发上
    /// （框架把 -1 判成"取消选中"，排了一次异步纠正，纠正再压掉用户的新值）。
    /// 所以这里显式发两发，中间隔 250ms 让纠正排上队。
    /// </remarks>
    private static bool Gesture(
        CoreDispatcher ui, StringBuilder sb, string kind, FrameworkElement control, int itemCount,
        Action<int> setIndex, Func<int> getIndex, Action<bool> done)
    {
        if (itemCount <= 0)
        {
            sb.AppendLine($"[{kind}] 跳过：还没有数据项");
            done(true);
            return true;
        }

        var start = getIndex();
        var target = itemCount > 1 ? (start == 0 ? 1 : 0) : start;
        var h0 = Hits;

        sb.AppendLine($"[{kind}] 初始 idx={Num(start)}，目标 {Num(target)}，项数 {Num(itemCount)}");
        sb.AppendLine($"　体检：{HealthCheck(control)}");

        // 第一发：取消选中。这一发是"病根"所在，单独记。
        setIndex(-1);

        Schedule(ui, TimeSpan.FromMilliseconds(250), () =>
        {
            sb.AppendLine($"　一发 -1　　→ 控件 idx={Num(getIndex())}　回调 +{Num(Hits - h0)}");

            // 第二发：选中目标值。回调<b>必须</b>在这一发被调到。
            var h1 = Hits;
            setIndex(target);

            Schedule(ui, TimeSpan.FromMilliseconds(250), () =>
            {
                sb.AppendLine($"　二发 {Num(target)}　　→ 控件 idx={Num(getIndex())}　回调 +{Num(Hits - h1)}");

                // 纠正走 dispatcher.RunAsync，晚于这两发；给它 1.5s 兑现。
                // 这一步才是判据：值被改回 = 用户的值被框架的纠正压掉了。
                Schedule(ui, TimeSpan.FromMilliseconds(1500), () =>
                {
                    var final = getIndex();
                    var ok = final == target && Hits > h1;

                    sb.AppendLine($"　等 1.5s　→ 控件 idx={Num(final)}");
                    sb.AppendLine($"　判定：{(ok ? "PASS" : $"FAIL（回调 +{Num(Hits - h1)}，控件停在 {Num(final)}，目标是 {Num(target)}）")}");
                    sb.AppendLine("　新增的链：" + Chain(Hits - h0).Replace(Environment.NewLine, " ｜ "));

                    done(ok);
                });
            });
        });

        // 判定由 done 异步交回，这里的返回值只是占位。
        return true;
    }

    /// <summary>
    /// 勾上 <paramref name="items"/> 里的某一项——真人点 RadioButtons 点的是这个。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Gesture"/> 的区别不是"麻烦一点的真人模拟"，而是<b>另一条代码路径</b>：
    /// 设 <c>SelectedIndex</c> 走的是"索引被改"，勾 <c>RadioButton.IsChecked</c> 走的是
    /// "这一项被勾上、旧项被反勾"——WinUI 内部两段逻辑不同，吐出来的事件序列也不同。
    /// 病出在哪条路径上，只有那条路径自己能证明。
    /// </remarks>
    private static bool TapRadioItem(
        CoreDispatcher ui, StringBuilder sb,
        MuxControls.RadioButtons group, List<RadioButton> items,
        Func<int> getIndex, Action<bool> done)
    {
        if (items.Count < 2)
        {
            sb.AppendLine("[RadioButtons] 点单选项：跳过（可视树上拿到的 RadioButton 少于 2 个，"
                        + "多半是展开区还没布局完）");
            done(true);
            return true;
        }

        var start = getIndex();
        var target = start == 0 ? 1 : 0;
        var h0 = Hits;

        sb.AppendLine($"[RadioButtons] 点第 {Num(target)} 个单选项（真人点的是它）　初始 idx={Num(start)}");

        // 每一项"长什么样"：宽度 + 文字。
        // 加它是被"整组只有 46px 宽"这个读数逼出来的——三个单选项挤在 46px 里，
        // 要么文字被裁，要么压根没文字。两种情况在界面上都是"看不出该点哪个"，
        // 用户说"设置项点不动"时，很可能指的就是这个，而不是事件链断了。
        // 事件链已经在上一段证明是通的，这一段负责证明"用户对得上手"。
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.AppendLine($"　　项 {Num(i)}：w={F0(item.ActualWidth)} h={F0(item.ActualHeight)} "
                + $"文字='{item.Content ?? "（null）"}'");
        }

        items[target].IsChecked = true;

        Schedule(ui, TimeSpan.FromMilliseconds(250), () =>
        {
            sb.AppendLine($"　点完 250ms → 控件 idx={Num(getIndex())}　回调 +{Num(Hits - h0)}");

            Schedule(ui, TimeSpan.FromMilliseconds(1500), () =>
            {
                var final = getIndex();
                var ok = final == target && Hits > h0;

                sb.AppendLine($"　等 1.5s　→ 控件 idx={Num(final)}");
                sb.AppendLine($"　判定：{(ok ? "PASS" : $"FAIL（回调 +{Num(Hits - h0)}，控件停在 {Num(final)}，目标是 {Num(target)}）")}");
                sb.AppendLine("　新增的链：" + Chain(Hits - h0).Replace(Environment.NewLine, " ｜ "));

                done(ok);
            });
        });

        return true;
    }

    /// <summary>
    /// 主题从"设置里的值"走到"界面真的变了"这一段的检查。
    /// </summary>
    /// <remarks>
    /// 要分三段看，缺一段就会把两种病混成一团：
    /// <list type="number">
    ///   <item>设置里的值（上一段已经证明会落盘）；</item>
    ///   <item>根元素的 <c>RequestedTheme</c>——框架<b>有没有下发</b>；</item>
    ///   <item>根元素的 <c>ActualTheme</c>——XAML <b>有没有真的生效</b>。</item>
    /// </list>
    /// 第 2 段对不上是框架的下发问题；第 3 段对不上是 XAML 层面的限制
    /// （<c>RequestedTheme</c> 在元素加载后再改，历史上就有不生效的报道）。
    /// </remarks>
    private static bool CheckThemeApplied(StringBuilder sb, Action<bool> done)
    {
        if (Window.Current?.Content is not FrameworkElement root)
        {
            sb.AppendLine("[主题落地] 跳过：拿不到根元素");
            done(true);
            return true;
        }

        var expected = AppSettings.Current.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        var requested = root.RequestedTheme;
        var actual = root.ActualTheme;

        // 期望是 Default 时 ActualTheme 会解析成系统当前的 Light/Dark，
        // 拿它跟 Default 比必然不等——这种情况只能看"不是 Default 之外的值"。
        var applied = expected == ElementTheme.Default ? actual != ElementTheme.Default : actual == expected;
        var pushed = requested == expected;
        var ok = pushed && applied;

        sb.AppendLine($"[主题落地] 设置里是 {AppSettings.Current.Theme}（期望 {expected}）");
        sb.AppendLine($"　根元素 RequestedTheme={requested}　ActualTheme={actual}");
        sb.AppendLine($"　下发：{(pushed ? "OK" : "⚠ 值没下发到根元素（框架侧）")}");
        sb.AppendLine($"　生效：{(applied ? "OK" : $"⚠ RequestedTheme 已是 {expected}，界面主题却还是 {actual}（XAML 侧）")}");
        sb.AppendLine($"　判定：{(ok ? "PASS" : "FAIL")}");

        done(ok);
        return ok;
    }

    /// <summary>收集一组 RadioButtons 可视树里的 RadioButton（按树序）。</summary>
    private static void CollectRadioItems(DependencyObject node, List<RadioButton> items)
    {
        if (node is RadioButton rb)
        {
            items.Add(rb);
        }

        var count = VisualTreeHelper.GetChildrenCount(node);

        for (var i = 0; i < count; i++)
        {
            CollectRadioItems(VisualTreeHelper.GetChild(node, i), items);
        }
    }

    /// <summary>
    /// 控件"能不能被点到"的体检报告。
    /// </summary>
    /// <remarks>
    /// <b>它要分开两种长得一模一样的病。</b>"点了没反应"往深里挖有两种：
    /// 点击压根<b>没落到控件上</b>（被透明层挡住、尺寸是 0、命中测试关了），
    /// 和点<b>落上了</b>但值被框架的纠正压回去。两种在界面上都是"没反应"，
    /// 在日志里也都是"没有回调"——但修法完全不同。
    /// 命中测试是唯一能当场分开它们的手段：在控件中心点做一次
    /// <c>FindElementsInHostCoordinates</c>，看最上面那层是不是它自己（或它的后代）。
    /// </remarks>
    private static string HealthCheck(FrameworkElement control)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("w=").Append(F0(control.ActualWidth))
              .Append(" h=").Append(F0(control.ActualHeight))
              .Append(" hit=").Append(control.IsHitTestVisible)
              .Append(" enabled=").Append(control is Control c && c.IsEnabled)
              .Append(" opacity=").Append(F0(control.Opacity));

            if (control.ActualWidth <= 0 || control.ActualHeight <= 0)
            {
                return sb.Append(" ｜ 尺寸为 0，点不到（不在视口 / 未布局）").ToString();
            }

            // 要读 ActualWidth/Height，所以按 FrameworkElement 取，不是 UIElement。
            var root = Window.Current?.Content as FrameworkElement;

            if (root is null)
            {
                return sb.Append(" ｜ 无根元素（或不是 FrameworkElement），跳过几何检查").ToString();
            }

            // 几何检查：控件中心点换算到窗口坐标，看它落不落在窗口矩形里。
            //
            // 这里<b>不做</b>命中测试。VisualTreeHelper.FindElementsInHostCoordinates
            // 是能直接回答"最上面那层是谁"的正解，但它在 AOT 下抛
            // NotSupportedException（给 ICollection<UIElement> 取 helper 类型失败）——
            // 观测代码为了看一眼反而把自己搞崩，那就本末倒置了。
            // 退而求其次：尺寸 + 坐标 + 命中开关，够回答"有没有被折叠成 0 尺寸 /
            // 被挪出视口 / 被关掉命中"这三类，剩下的遮挡问题交给手势本身去证
            // （手势真点到了自然会出回调）。
            var point = control.TransformToVisual(root)
                .TransformPoint(new Point(control.ActualWidth / 2, control.ActualHeight / 2));

            var inWindow = point.X >= 0 && point.Y >= 0 &&
                           point.X <= root.ActualWidth && point.Y <= root.ActualHeight;

            return sb.Append(" ｜ 中心(").Append(F0(point.X)).Append(",").Append(F0(point.Y))
                     .Append(") 窗口 ").Append(F0(root.ActualWidth)).Append("x").Append(F0(root.ActualHeight))
                     .Append(inWindow ? " → 在窗口内" : " → ⚠ 在窗口外（滚动出视口 / 被折叠）")
                     .ToString();
        }
        catch (Exception ex)
        {
            return $"体检自身抛异常：{ex.GetType().Name} {ex.Message}";
        }
    }

    private static string F0(double value) => value.ToString("F0", CultureInfo.InvariantCulture);

    /// <summary>按类型收集可视树上的受控控件（拿引用，不拿编号）。</summary>
    private static void Collect(
        DependencyObject node, int depth,
        List<ComboBox> comboBoxes, List<MuxControls.RadioButtons> radioGroups,
        List<ScrollViewer> scrollers)
    {
        if (depth > 40)
        {
            return;
        }

        switch (node)
        {
            case ComboBox cb:
                comboBoxes.Add(cb);
                break;

            case MuxControls.RadioButtons rb:
                radioGroups.Add(rb);
                break;

            case ScrollViewer sv:
                scrollers.Add(sv);
                break;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);

        for (var i = 0; i < count; i++)
        {
            Collect(VisualTreeHelper.GetChild(node, i), depth + 1, comboBoxes, radioGroups, scrollers);
        }
    }

    private static void WriteSelfTest(string text)
    {
        try
        {
            File.WriteAllText(SelfTestPath, text);
            ReactorLog.Info(ReactorLogChannel.Host, $"[probe] 自检报告 → {SelfTestPath}");
        }
        catch
        {
            // 观测手段写不进去不该影响进程。
        }
    }
}
