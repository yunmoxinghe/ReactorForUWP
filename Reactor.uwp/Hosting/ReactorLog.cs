using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Reactor.Uwp.Hosting;

/// <summary>日志级别。数值越大越啰嗦，与 <see cref="ReactorLog.Level"/> 比较决定是否记录。</summary>
public enum ReactorLogLevel
{
    /// <summary>全关。</summary>
    Off = 0,

    /// <summary>非预期但已降级处理，功能受影响。</summary>
    Error = 1,

    /// <summary>非预期但已自动兜底，功能未受影响。</summary>
    Warn = 2,

    /// <summary>关键节点：首帧、主题切换、重建。</summary>
    Info = 3,

    /// <summary>每一轮渲染 / 每一条消息。量大，仅排查时开。</summary>
    Trace = 4,
}

/// <summary>
/// 日志通道。<b>取代以前那种拿字符串前缀当分类的写法</b>（<c>"[reactor] xxx"</c>、
/// <c>"[bc] xxx"</c>、<c>"[probe] xxx"</c>）。
/// </summary>
/// <remarks>
/// 分区的依据是"这条记录的<b>因果属于哪个环节</b>"：据此既能整段开关，也能在排查时
/// 按单通道隔离。跨环节的消息记在它<b>发生</b>的那一段，不要按"谁触发的"记。
/// </remarks>
public enum ReactorLogChannel
{
    /// <summary>宿主与窗口：主题、backdrop、包激活。</summary>
    Host = 0,

    /// <summary>渲染批次：入队 / 开始 / 结束 / 重入保护。</summary>
    Render = 1,

    /// <summary>Reconciler：挂载、diff、属性下发、卸载。</summary>
    Patch = 2,

    /// <summary>控件事件 → 用户回调的闸门：放行、按回声抑制、按未就绪抑制。</summary>
    Input = 3,

    /// <summary>集合与数据源：items 下发、ItemsSource 引用更换。</summary>
    Items = 4,

    /// <summary>本地化：资源限定符变化触发的整树重建。</summary>
    Localize = 5,

    /// <summary>资源解析：命名样式、DataTemplate、XamlReader.Load。</summary>
    Resource = 6,

    /// <summary>原生互操作：类型投影、集合封送、AOT 相关降级。</summary>
    Interop = 7,
}

/// <summary>
/// Reactor 的统一日志出口。
/// </summary>
/// <remarks>
/// <b>为什么要有这么个东西。</b>以前的日志是"哪儿觉得可疑就在哪儿插一句
/// <c>ReactorApplication.Trace($"[bc] ...")</c>"，于是：分类靠字符串前缀约定、
/// 没有级别、排查时只能全量翻文件、<b>而文件还在被 UWP 符号链接重定向的目录里</b>
/// （<c>C:\Users\…\AppData\Local\Packages\…\LocalState</c> 实际指向另一个盘，
/// <c>find</c> 默认不跟随符号链接 → 看起来"没日志"）。排完还得记得把插的桩删掉。
/// <para>
/// 这里把三件事一次性定下来：
/// <list type="number">
///   <item><b>结构化</b>：通道 + 级别，取代字符串前缀。</item>
///   <item><b>内存环形缓冲</b>：不需要翻文件，<c>Tail()</c> 直接取最后若干条。
///          Gallery 的诊断页就靠它，屏幕上当场可读。</item>
///   <item><b>常驻且可控</b>：埋点本身不用再删，默认级别关掉即可。
///          "排查时临时插桩、排完必须记得删"这个模式本身就是缺陷——每次插桩都有可能
///          改到被测对象，<b>而删掉之后同样的病再犯一遍还是没证据</b>。</item>
/// </list>
/// </para>
/// <para>
/// <b>线程安全</b>：环形槽是引用赋值（原子），槽位序号用 <see cref="Interlocked"/>，
/// 全程无锁。多 Lodge 日志的最坏情况是相邻两行顺序不保证完全严格，可接受。
/// </para>
/// </remarks>
public static class ReactorLog
{
    /// <summary>缓冲条数。够看几次交互，又不至于堆内存。</summary>
    private const int Capacity = 512;

    private static readonly string?[] Ring = new string?[Capacity];

    private static long _seq;

    /// <summary>全局级别门槛。低于它的记录直接丢弃。</summary>
    private static int _level = (int)ReactorLogLevel.Info;

    /// <summary>每个通道各自的门槛。<c>null</c> 表示沿用 <see cref="Level"/>。</summary>
    private static readonly int?[] ChannelLevels = new int?[8];

    /// <summary>
    /// 全局级别。设为 <see cref="ReactorLogLevel.Trace"/> 会非常啰嗦（每帧都打），
    /// 仅用于现场定位。
    /// </summary>
    public static ReactorLogLevel Level
    {
        get => (ReactorLogLevel)Volatile.Read(ref _level);
        set => Volatile.Write(ref _level, (int)value);
    }

    /// <summary>给单个通道单独设门槛，覆盖全局值。传 <c>null</c> 恢复跟随全局。</summary>
    public static void SetChannel(ReactorLogChannel channel, ReactorLogLevel? level) =>
        ChannelLevels[(int)channel] = level is null ? null : (int)level.Value;

    /// <summary>当前某通道的生效门槛。</summary>
    public static ReactorLogLevel Effective(ReactorLogChannel channel) =>
        (ReactorLogLevel)(ChannelLevels[(int)channel] ?? Volatile.Read(ref _level));

    /// <summary>该级别在该通道此刻是否会记。热路径上先问一句避免拼字符串。</summary>
    public static bool IsEnabled(ReactorLogChannel channel, ReactorLogLevel level) =>
        (int)level <= (int)Effective(channel);

    /// <summary>
    /// 记一条。<see cref="ReactorLogLevel.Info"/> 及以上会同时落盘。
    /// </summary>
    public static void Write(ReactorLogChannel channel, ReactorLogLevel level, string message)
    {
        if (!IsEnabled(channel, level))
        {
            return;
        }

        var line = $"{DateTime.Now:HH:mm:ss.fff} [{channel}/{Short(level)}] {message}";

        // 引用赋值是原子的：读者要么看到整行要么看到 null，不会读到写了一半。
        Ring[Interlocked.Increment(ref _seq) % Capacity] = line;

        System.Diagnostics.Debug.WriteLine(line);

        if ((int)level <= (int)ReactorLogLevel.Info)
        {
            Persist(line);
        }
    }

    /// <summary>便捷写法：<c>Info</c> 级。</summary>
    public static void Info(ReactorLogChannel channel, string message) =>
        Write(channel, ReactorLogLevel.Info, message);

    /// <summary>便捷写法：<c>Trace</c> 级（最啰嗦，热路径用）。</summary>
    public static void Trace(ReactorLogChannel channel, string message) =>
        Write(channel, ReactorLogLevel.Trace, message);

    /// <summary>便捷写法：<c>Warn</c> 级：非预期但已自动兜底。</summary>
    public static void Warn(ReactorLogChannel channel, string message) =>
        Write(channel, ReactorLogLevel.Warn, message);

    /// <summary>便捷写法：<c>Error</c> 级：非预期且功能受影响。</summary>
    public static void Error(ReactorLogChannel channel, string message) =>
        Write(channel, ReactorLogLevel.Error, message);

    /// <summary>已执行的渲染轮次（宿主整树 + 各组件子树都算）。</summary>
    /// <remarks>
    /// 存在的理由就一个：<b>让"回调之后到底有没有重渲染"这件事在日志里可判</b>。
    /// 受控控件的完整因果链是
    /// <c>事件 → 闸门 → 回调 → setState → 重渲染 → 受控下发 → 控件</c>，
    /// 以前只有第一环有日志，链子断了根本看不出断在哪——
    /// 日志里出现"回调但没有帧"就是 setState 之后丢渲染的直接证据。
    /// </remarks>
    public static int Frames => Volatile.Read(ref _frame);

    private static int _frame;

    /// <summary>开一轮渲染，返回帧号（从 1 开始）。</summary>
    public static int NextFrame() => Interlocked.Increment(ref _frame);

    /// <summary>
    /// 记一轮渲染。<b><c>Trace</c> 级</b>：一次用户操作会连带出 2 帧（state 一帧、
    /// 效果里回写快照再一帧），按 <c>Info</c> 记会整屏都是它，把真正要看的
    /// <c>Input</c>/<c>Patch</c> 行挤出缓冲区——这个代价付过一次。
    /// "有没有重渲染"改由 <see cref="Frames"/> 计数回答，它一条就够，且不占版面。
    /// </summary>
    public static void Frame(string what) =>
        Write(ReactorLogChannel.Render, ReactorLogLevel.Trace, $"帧{NextFrame()} {what}");

    /// <summary>
    /// 渲染调度的小动作（入队 / 合并 / 丢弃）。<c>Trace</c> 级。
    /// </summary>
    public static void Schedule(string what) =>
        Write(ReactorLogChannel.Render, ReactorLogLevel.Trace, what);

    /// <summary>
    /// 闸门<b>吞掉</b>了一发控件事件。<c>Trace</c> 级：正常运行时大量出现，
    /// 只有"该放行的没放行"时才需要看。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Pass"/> 成对：这两个计数之差就是"被我们压下去的事件总量"，
    /// 诊断页直接摆出来，不用去翻文件。
    /// </remarks>
    public static void Gate(string message) => Write(ReactorLogChannel.Input, ReactorLogLevel.Trace, message);

    /// <summary>闸门<b>放行</b>了一发事件给用户回调。<c>Info</c> 级：量小，是用户操作的客观记录。</summary>
    public static void Pass(string message) => Write(ReactorLogChannel.Input, ReactorLogLevel.Info, message);

    /// <summary>
    /// 按发生顺序取最后 <paramref name="count"/> 条，用于诊断页显示。
    /// </summary>
    /// <param name="count">最多取几条。</param>
    /// <param name="channel">
    /// 只取某个通道。排查"点了没反应"时该看 <see cref="ReactorLogChannel.Input"/>，
    /// 但高频通道（<c>Render</c>）会把环形缓冲冲掉——不按通道隔离，
    /// 想看的证据就被挤没了。传 <c>null</c> 取全部。
    /// </param>
    /// <remarks>
    /// 按 <b>写入序号</b>推算下标，而不是遍历数组：缓冲区没写满时也得到正确顺序，
    /// 且不需要额外的"已写满"标记。
    /// </remarks>
    public static string[] Recent(int count, ReactorLogChannel? channel = null)
    {
        var seen = Volatile.Read(ref _seq);
        var total = (int)Math.Min(seen, Capacity);

        if (channel is null)
        {
            var take = Math.Min(count, total);

            var all = new string[take];
            for (var i = 0; i < take; i++)
            {
                // 从最老的一条开始往前扫。
                all[i] = Ring[(seen - take + i + 1 + Capacity * 2) % Capacity] ?? string.Empty;
            }

            return all;
        }

        // 通道标记在行首固定格式 "[通道/级别]"，直接子串匹配，不必把结构存两份。
        var tag = $"[{channel}/";
        var picked = new List<string>(count);

        for (var i = 0; i < total && picked.Count < count; i++)
        {
            var line = Ring[(seen - total + i + 1 + Capacity * 2) % Capacity];
            if (line is not null && line.Contains(tag, StringComparison.Ordinal))
            {
                picked.Add(line);
            }
        }

        return picked.ToArray();
    }

    /// <summary>
    /// 拼成一段文本。诊断页 / 崩溃转储里直接看的场合用。
    /// </summary>
    public static string Tail(int count, ReactorLogChannel? channel = null) =>
        string.Join(Environment.NewLine, Recent(count, channel));

    /// <summary>清空缓冲。</summary>
    public static void Clear()
    {
        Array.Clear(Ring, 0, Ring.Length);
        Volatile.Write(ref _seq, 0);
    }

    /// <summary>
    /// 闸门计数摘要：<c>EchoStats</c> + <c>ReadyStats</c> 的现值。
    /// </summary>
    /// <remarks>
    /// 这两个计数回答的问题不同（见各自类注释），但排查时总是一起看，就放一处。
    /// </remarks>
    public static string Counters() =>
        $"echo: {Reactor.Uwp.Internal.EchoStats.Snapshot()} | ready: {Reactor.Uwp.Internal.ReadyStats.Snapshot()}";

    private static string Short(ReactorLogLevel level) => level switch
    {
        ReactorLogLevel.Error => "ERR",
        ReactorLogLevel.Warn => "WRN",
        ReactorLogLevel.Info => "INF",
        _ => "TRC",
    };

    /// <summary>
    /// 落盘。<b>失败必须静默</b>：日志是观测手段，不能因为它把进程带走。
    /// </summary>
    private static void Persist(string line)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path, "reactor-startup.log");

            using var fs = new System.IO.FileStream(
                path, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
            using var sw = new System.IO.StreamWriter(fs);
            sw.Write(line);
            sw.Write(Environment.NewLine);
        }
        catch
        {
            // 任意线程都可能调用（含非 UI 线程），访问 ApplicationData 可能失败。
            // Debug.WriteLine 已经输出过了，文件写不进去也不该影响进程存活。
        }
    }
}
