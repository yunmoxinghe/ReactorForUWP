using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Template.Services;

/// <summary>
/// 最小心跳 + 受控控件状态快照。
/// </summary>
/// <remarks>
/// <b>一次性排查工具，验完即删。</b>
/// <para>
/// 它要回答的问题只有一个：<b>用户点下去的那一刻，控件到底"就绪"了没有。</b>
/// 框架侧 <c>ReadyGate</c> 用 <c>IsLoaded</c> 对齐 WinUI 2 的 <c>m_blockSelecting</c>
/// （内部 repeater Loaded 之前 RadioButtons 拒不接受选中），未就绪期间抛出的
/// 选中事件<b>一律被闸吞掉</b>。所以"点了没反应"完全可能只是控件还没进树，
/// 跟心跳、跟探针都无关——必须先能看见 <c>IsLoaded</c>，否则全靠猜。
/// </para>
/// <para>
/// 三档，靠 <c>LocalState</c> 里的文件选（改档不用重编译）：
/// <list type="bullet">
/// <item><c>heartbeat-off.txt</c> 存在 → 整个不启动（对照组）</item>
/// <item><c>heartbeat-scan.txt</c> 存在 → 走可视树并采快照</item>
/// <item><c>两个都不存在</c> → 空心跳，只丢一个什么也不做的工作项</item>
/// </list>
/// </para>
/// <para>
/// <b>只在快照变化时才写文件</b>：稳态下不落盘，避免"每 500ms 一次 I/O"
/// 自己变成时序干扰源（这个坑上一轮已经踩过一次）。
/// </para>
/// </remarks>
public static class Heartbeat
{
    private const int PeriodMs = 500;

    private static ThreadPoolTimer? _timer;
    private static CoreDispatcher? _ui;
    private static string _last = string.Empty;
    private static string _lastGate = string.Empty;
    private static bool _gateWritten;
    private static int _beats;

    /// <summary>在 UI 线程调用（<c>UseEffect</c> 里即可）。</summary>
    public static void Start()
    {
        var folder = ApplicationData.Current.LocalFolder.Path;

        if (File.Exists(Path.Combine(folder, "heartbeat-off.txt")))
        {
            Note("off（heartbeat-off.txt 存在，未启动）");
            return;
        }

        var scan = File.Exists(Path.Combine(folder, "heartbeat-scan.txt"));

        _ui = Window.Current?.Dispatcher
              ?? CoreApplication.MainView?.CoreWindow?.Dispatcher;

        if (_ui is null)
        {
            Note("没起来：派发器为 null");
            return;
        }

        _timer = ThreadPoolTimer.CreatePeriodicTimer(
            // 参数名刻意不叫 _：丢弃符与 lambda 参数同名时，下面的 `_ = ...` 会
            // 变成给参数赋值（探针里踩过一次，报错还指不到真凶）。
            tick =>
            {
                try
                {
                    _ = _ui.RunAsync(CoreDispatcherPriority.Low, () =>
                    {
                        CollectGate();

                        if (scan)
                        {
                            Snapshot();
                        }
                    });
                }
                catch
                {
                    // 量具不该影响进程。
                }
            },
            TimeSpan.FromMilliseconds(PeriodMs));

        Note(scan
            ? $"on {PeriodMs}ms + 采快照（写 state.txt）"
            : $"on {PeriodMs}ms 空心跳");
    }

    /// <summary>
    /// 把闸门（<c>Input</c> 通道）的留痕从内存 Ring 里转出来。
    /// </summary>
    /// <remarks>
    /// <b>它是这次排查里唯一能直接回答"为什么吞"的东西。</b>框架的每一道闸都有
    /// <c>ReactorLog.Gate</c> 留痕（被吞的那一发会写清作者：取消选中 / 未就绪 /
    /// 重建中 / 回声），但它走的是 Trace 级，<b>不落盘</b>——只有把 Ring 读出来
    /// 才看得见。而它之所以敢开：Trace 不 Persist，所以不会引入"每条日志同步
    /// Append 一次文件"那个已知时序干扰源。
    /// <para>
    /// 只在内容变化时写：去掉行首时间戳再比对，稳态下不落盘。
    /// </para>
    /// </remarks>
    private static void CollectGate()
    {
        _beats++;

        var rows = Reactor.Uwp.Hosting.ReactorLog.Recent(40, Reactor.Uwp.Hosting.ReactorLogChannel.Input);

        var sb = new StringBuilder();

        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row))
            {
                continue;
            }

            // 行首是 "HH:mm:ss.fff "（13 字符）：去掉它才能比较"内容有没有变"。
            sb.Append(row.Length > 13 ? row.Substring(13) : row).Append('\n');
        }

        var text = sb.ToString();

        // <b>首次无条件写一次。</b>只按"内容有没有变"来控制的话，
        // Ring 为空时 text 与初值都是空串、永远相等，于是<b>文件根本不会被创建</b>——
        // 那"量具没在跑"和"量具在跑但还没有留痕"在磁盘上长得一模一样。
        // 这次排查就栽在这上面：一口气查错了包目录，又看到没有 gate.log，
        // 差点把"没人操作"读成"量具失效"。
        // 首次写一份空的出来，就把这两件事分开了。
        if (_gateWritten && string.Equals(text, _lastGate, StringComparison.Ordinal))
        {
            return;
        }

        _lastGate = text;
        _gateWritten = true;

        // 计数一起带出来：它回答的是留痕回答不了的那半边——
        // 留痕给的是"这一发被哪道闸吞了"，计数给的是"这类事一共发生过几次"。
        // 尤其 healed：非 0 就证明"Loaded 不会来、靠即时查 IsLoaded 救回来"
        // 这条路径在真机上真的可达（源码注释里写的是"当前不可达"）。
        //
        // 心跳序号同理：它证明<b>量具还活着</b>，且能看出"留痕停在几分钟前"
        // 到底是因为没人操作，还是量具已经死了。
        Write("gate.log",
            $"{DateTime.Now:HH:mm:ss} 心跳#{_beats}\n{text}\n{Reactor.Uwp.Hosting.ReactorLog.Counters()}\n");
    }

    private static void Nop()
    {
    }

    /// <summary>走一遍可视树，把受控控件的就绪状态采出来。</summary>
    private static void Snapshot()
    {
        var root = Window.Current?.Content;

        if (root is null)
        {
            return;
        }

        var rows = new List<string>();
        Walk(root, rows);

        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("HH:mm:ss")).Append('\n');

        foreach (var row in rows)
        {
            sb.Append("  ").Append(row).Append('\n');
        }

        var text = sb.ToString();

        // 只在变化时写：稳态下不落盘。
        if (string.Equals(text, _last, StringComparison.Ordinal))
        {
            return;
        }

        _last = text;
        Write("state.txt", text);
    }

    private static void Walk(DependencyObject node, List<string> rows)
    {
        switch (node)
        {
            case MuxControls.RadioButtons rb:
                rows.Add($"RB IsLoaded={rb.IsLoaded} SelectedIndex={rb.SelectedIndex} Items={rb.Items.Count}");
                break;

            case Windows.UI.Xaml.Controls.ComboBox cb:
                rows.Add($"CB IsLoaded={cb.IsLoaded} SelectedIndex={cb.SelectedIndex} Items={cb.Items.Count}");
                break;

            case MuxControls.BreadcrumbBar bar:
                // 只报"界面上真实渲染出来几条"，<b>不报 ItemsSource 的条数</b>。
                // 后者的读数在 CsWinRT 投影下反复失真（Gallery 的 LiveProbe 踩过两轮：
                // 先是非泛型 IEnumerable 判不出、数成 0；再是 IList<object> 判得出
                // 但照样报 0，而可视树里明明渲染着东西）。一条假读数逼出的结论
                // 全是假的，所以直接数屏幕上的 BreadcrumbBarItem。
                //
                // 尺寸一并带上："面包屑整条不见"有两种完全不同的成因——
                // 一种是渲染出 0 项（数据源没喂进去），一种是渲染了但 w/h 为 0
                // （容器 Collapsed / 没布局）。只数条数会把这两件事混成一个。
                rows.Add($"BC IsLoaded={bar.IsLoaded} vis={bar.Visibility} " +
                         $"w={bar.ActualWidth:F0} h={bar.ActualHeight:F0} " +
                         $"渲染={CountRendered(bar)}项");
                break;

            case Windows.UI.Xaml.Controls.ToggleSwitch ts:
                rows.Add($"TS IsLoaded={ts.IsLoaded} IsOn={ts.IsOn}");
                break;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);

        for (var i = 0; i < count; i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), rows);
        }
    }

    /// <summary>
    /// 数面包屑<b>实际渲染出来</b>几个条目（走可视树，不去问数据源）。
    /// </summary>
    private static int CountRendered(DependencyObject node)
    {
        var n = node is MuxControls.BreadcrumbBarItem ? 1 : 0;

        var count = VisualTreeHelper.GetChildrenCount(node);

        for (var i = 0; i < count; i++)
        {
            n += CountRendered(VisualTreeHelper.GetChild(node, i));
        }

        return n;
    }

    private static void Note(string what)
    {
        Write("heartbeat.txt", $"{DateTime.Now:HH:mm:ss} {what}\n");
    }

    private static void Write(string name, string text)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(ApplicationData.Current.LocalFolder.Path, name),
                text);
        }
        catch
        {
            // 留痕失败不影响被测对象。
        }
    }
}
