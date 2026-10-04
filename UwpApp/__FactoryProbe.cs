// 临时探针：ItemsRepeater 虚拟化 A/B 压测 + 不变量校验（用完即删）
//
// 这一版的目标不是「打印 minted/realized」，而是**证明不变量成立**。
// 每一轮都会落一份可追溯的归档：
//
//   ReactorRuns/<runId>/
//       manifest.json    跑的什么（MODE / 项数 / 布局 / 折叠开关 / 机器信息）
//       config.json      同上，供脚本 diff
//       events.ndjson    每个检查点一行
//       summary.json     最终计数 + PASS/FAIL
//
// MODE 语义：
//   0 = 纯内置：ItemsRepeater + XAML DataTemplate，完全不碰原生桥（基线）
//   1 = RecyclingElementFactory 池化（maxPool=512，回收时折叠）
//   2 = RecyclingElementFactory 不复用（maxPool=0，验证无界增长）
//   3 = 裸桥自池 + **不改 Visibility**（幽灵行对照：预期出现）
//   4 = 裸桥自池 + **折叠**（幽灵行对照：预期消失）
//   5 = 极限最小回调：永远返回同一个元素，只保留反向 P/Invoke + QI + ThisPtr 归还
//
// 工作负载（golden test）：
//   A 单向滚动 → B 往返 20 次 → C 随机跳跃 100 次 → D 重复数据 →
//   E 数据源结构变化（插入/删除/移动）→ F 再完整滚一遍
//
// 关键 invariants（summary 里必须全为 0 才算收口）：
//   doubleAcquire / doubleRecycle / invalidTransition / unknownElement
//   nullData / indexMismatch / ghostVisible
//   conservation（minted == active + pooled + discarded）每个检查点都要 OK
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Internal;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace UwpApp
{
    public sealed class __FactoryRuntimeProbeApp : Component
    {
        // ================== 切模式后重新编译 ==================
        internal const int MODE = 3;
        // ====================================================

        internal const int ItemCount = 5000;
        internal const double ItemHeight = 32;
        internal const int TickMs = 150;
        internal const int MaxPool = 512;
        internal const int RoundTrips = 20;
        internal const int JumpCount = 100;

        // ── 计数器 ────────────────────────────────────────────────────────
        private static int s_prepared, s_clearing, s_indexChanged;
        private static int s_nullData, s_duplicateKey, s_indexMismatch, s_ghostRows, s_ghostVisible;
        private static int s_conservationBroken, s_poolOverflow;
        private static int s_mintedBare, s_reusedBare;
        private static readonly Stack<UIElement> s_barePool = new();
        private static readonly Dictionary<UIElement, (int Index, string Key)> s_live =
            new(ReferenceEqualityComparer.Instance);
        private static readonly List<(UIElement El, string Text)> s_cleared = new();

        private static ObservableCollection<string> s_items = new();
        private static ScrollViewer s_scroller = null!;
        private static RecyclingElementFactory? s_factory;
        private static RunLog s_log = null!;

        private static readonly List<long> s_changeViewMs = new();
        private static readonly List<long> s_tickGapMs = new();
        private static readonly Stopwatch s_watch = new();
        private static long s_lastTickMs;
        private static readonly List<(string Name, int Index, Action? Mutate)> s_steps = new();
        private static int s_stepIndex = -1;
        private static string s_stepName = "init";

        public override Element Render()
        {
            UseEffect(() =>
            {
                try
                {
                    Say("[probe] effect enter");
                    StartRun();
                    Say("[probe] StartRun returned");
                }
                catch (Exception ex)
                {
                    // 注意：这里不能用 s_log?.Event —— 一旦 RunLog 还没建出来就抛异常，
                    // 那条 ?. 会让整段故障变成完全静默（本次就是这么丢的第一手证据）。
                    Say("[probe] EX " + ex);
                }
            }, "once");

            return VStack(TextBlock($"factory probe MODE={MODE}"));
        }

        // ── 编排 ──────────────────────────────────────────────────────────

        // 同时进 VS 输出窗口和 LocalState\reactor-startup.log，便于首帧就能看到进展。
        private static void Say(string msg)
        {
            Debug.WriteLine(msg);
            Reactor.Uwp.Hosting.ReactorApplication.Trace(msg);
        }

        private static void StartRun()
        {
            Say("[probe] creating RunLog");
            s_log = new RunLog(MODE, ItemCount);
            Say("[probe] RunLog dir=" + s_log.Directory);
            EchoStats.Reset();
            s_watch.Restart();

            s_items = new ObservableCollection<string>();
            for (int i = 0; i < ItemCount; i++)
            {
                s_items.Add(Key(i));
            }

            var rc = new MuxControls.ItemsRepeater
            {
                Layout = new MuxControls.StackLayout(),
                ItemsSource = s_items,
            };
            Say("[probe] repeater created items=" + s_items.Count);
            WireRepeaterEvents(rc);
            Say("[probe] building ItemTemplate MODE=" + MODE);
            rc.ItemTemplate = BuildTemplate();
            Say("[probe] ItemTemplate ok");

            s_scroller = new ScrollViewer
            {
                Width = 260,
                Height = 500,
                Content = rc,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollMode = ScrollMode.Enabled,
            };

            var popup = new Windows.UI.Xaml.Controls.Primitives.Popup
            {
                Child = new Border { Width = 280, Height = 520, Child = s_scroller },
                IsOpen = true,
            };

            InstallCrashHooks();

            BuildSteps();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
            timer.Tick += (_, _) =>
            {
                // Timer drift：tick 本该 150ms 一次，实测 500~900ms —— gap 就是 UI 线程
                // 被 realize/layout/同步 IO 占住的直接度量（比 ChangeView 同步耗时有意义）。
                var now = s_watch.ElapsedMilliseconds;
                var gap = s_lastTickMs == 0 ? 0 : now - s_lastTickMs;
                s_lastTickMs = now;

                try
                {
                    // 上一个 step 已经 settle 完，先做检查点，再走下一步。
                    if (s_stepIndex >= 0)
                    {
                        Checkpoint(gap);
                    }

                    // 每步一行轻量 trace：进程一旦被杀，最后一行就是死亡位置。
                    Say($"[probe] tick#{s_stepIndex} {s_stepName} gap={gap}ms live={s_live.Count} ghost={s_ghostVisible}");

                    if (!Advance())
                    {
                        Say("[probe] all steps done, writing summary");
                        timer.Stop();
                        popup.IsOpen = false;
                        Finish();
                        Say("[probe] summary written");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Say("[probe] TICK EX " + ex);
                }
            };
            Say("[probe] steps=" + s_steps.Count + " starting timer");
            timer.Start();

            Say("[probe] begin mode=" + MODE + " items=" + ItemCount);
            s_log.Event("begin", ("mode", MODE), ("items", ItemCount), ("steps", s_steps.Count));
        }

        /// <summary>进程退出/未处理异常的最后一道证据：任何路径都要留下而不是静默。</summary>
        private static void InstallCrashHooks()
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    Say("[probe] UNHANDLED " + e.ExceptionObject);
                    s_log?.Event("crash",
                        ("kind", "unhandled"),
                        ("fatal", e.IsTerminating),
                        ("step", s_stepName),
                        ("detail", Convert.ToString(e.ExceptionObject) ?? "?"));
                    s_log?.Flush();
                };

                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    Say("[probe] ProcessExit step=" + s_stepName + " stepIndex=" + s_stepIndex);
                    s_log?.Event("exit",
                        ("kind", "process-exit"),
                        ("exitCode", Environment.ExitCode),
                        ("stepIndex", s_stepIndex),
                        ("step", s_stepName),
                        ("prepared", s_prepared),
                        ("clearing", s_clearing),
                        ("ghost", s_ghostVisible),
                        ("factory", s_factory?.StatsSnapshot() ?? "n/a"));
                    s_log?.Flush();
                };

                Windows.UI.Xaml.Application.Current.UnhandledException += (_, e) =>
                {
                    Say("[probe] APP UNHANDLED " + e.Exception);
                    s_log?.Event("crash",
                        ("kind", "app-unhandled"),
                        ("step", s_stepName),
                        ("detail", e.Exception?.ToString() ?? "?"));
                    s_log?.Flush();

                    // 标记已处理后 XAML 不会立刻拉闸，有机会看到下一步能不能继续跑。
                    e.Handled = true;
                };

                Say("[probe] crash hooks installed");
            }
            catch (Exception ex)
            {
                Say("[probe] HOOK EX " + ex);
            }
        }

        private static void BuildSteps()
        {
            s_steps.Clear();

            // A 单向滚动
            for (int i = 0; i <= 10; i++)
            {
                s_steps.Add(($"A{i}", Math.Min(ItemCount - 1, i * 500), null));
            }

            // B 往返
            for (int i = 0; i < RoundTrips; i++)
            {
                s_steps.Add((i % 2 == 0 ? "B-end" : "B-home", i % 2 == 0 ? ItemCount - 1 : 0, null));
            }

            // C 随机跳跃（固定种子，可复现）
            var rng = new Random(20261004);
            for (int i = 0; i < JumpCount; i++)
            {
                s_steps.Add(($"C{i}", rng.Next(0, ItemCount), null));
            }

            // D 重复数据：同一字符串出现在多个 index，验证不能用 Data 反推 Index
            for (int i = 0; i < 5; i++)
            {
                s_steps.Add(($"D{i}", Math.Min(ItemCount - 1, i * 900),
                    i == 0 ? (Action)SwitchToDuplicateData : null));
            }

            // E 数据源结构变化：应看到 ElementIndexChanged，而不是全部重建
            s_steps.Add(("E-insert", 0, () => s_items.Insert(0, "INSERTED")));
            s_steps.Add(("E-remove", 100, () => s_items.RemoveAt(5)));
            s_steps.Add(("E-move", 200, () => s_items.Move(s_items.Count - 1, 1)));

            // F 再完整滚一遍
            for (int i = 0; i <= 10; i++)
            {
                s_steps.Add(($"F{i}", Math.Min(s_items.Count - 1, i * 500), null));
            }
        }

        private static bool Advance()
        {
            s_stepIndex++;
            if (s_stepIndex >= s_steps.Count)
            {
                return false;
            }

            var step = s_steps[s_stepIndex];
            s_stepName = step.Name;
            step.Mutate?.Invoke();

            var sw = Stopwatch.StartNew();
            s_scroller.ChangeView(null, step.Index * ItemHeight, null);
            sw.Stop();
            s_changeViewMs.Add(sw.ElapsedMilliseconds);
            return true;
        }

        // ── 检查点 ────────────────────────────────────────────────────────

        private static void Checkpoint(long gap = 0)
        {
            // 1) 内容错位：live 元素的文本必须等于当前数据源在该下标上的值
            foreach (var kv in s_live.ToList())
            {
                var index = kv.Value.Index;
                if (index < 0 || index >= s_items.Count)
                {
                    continue;
                }

                var text = (kv.Key as TextBlock)?.Text ?? string.Empty;
                var expected = s_items[index];
                if (!string.Equals(text, expected, StringComparison.Ordinal))
                {
                    s_indexMismatch++;
                    s_ghostRows++;
                    s_log.Event("mismatch",
                        ("index", index),
                        ("expected", expected),
                        ("actual", text));
                }
            }

            // 2) 幽灵行：已 clearing 且<b>没有回到 realized 集合</b>的元素若仍 Visible，
            //    就会以旧矩形继续绘制。
            //
            //    上一版漏了「已复用」这条过滤：150ms 内被重新 GetElement 取用的元素
            //    会被 Acquire 重新置为 Visible，于是 clearing 全成了 ghost（28536 条假阳性）。
            //    真正的幽灵行 = 不在 s_live 里 + Visible。
            foreach (var c in s_cleared)
            {
                if (s_live.ContainsKey(c.El))
                {
                    continue; // 已复用：此刻 Visible 是合法的
                }

                if (c.El.Visibility == Visibility.Visible)
                {
                    // 限流：单次 galleries 可能上千条，全写会把 UI 线程钉在同步文件 IO 上。
                    if (s_ghostVisible < 20)
                    {
                        s_log.Event("ghost", ("text", c.Text));
                    }

                    s_ghostVisible++;
                    s_ghostRows++;
                }
            }
            s_cleared.Clear();

            // 3) 库存守恒 + 池上限
            if (s_factory is not null)
            {
                if (!s_factory.ConservationOk)
                {
                    s_conservationBroken++;
                }

                if (s_factory.Pooled > MaxPool)
                {
                    s_poolOverflow++;
                }
            }

            if (gap > 0)
            {
                s_tickGapMs.Add(gap);
            }

            s_log.Event("checkpoint",
                ("step", s_stepName),
                ("gapMs", gap),
                ("changeViewMs", s_changeViewMs.Count == 0 ? 0 : s_changeViewMs[^1]),
                ("prepared", s_prepared),
                ("clearing", s_clearing),
                ("indexChanged", s_indexChanged),
                ("live", s_live.Count),
                ("nullData", s_nullData),
                ("indexMismatch", s_indexMismatch),
                ("ghostVisible", s_ghostVisible),
                ("factory", s_factory?.StatsSnapshot() ?? "n/a(bare or MODE0)"),
                ("echo", EchoStats.Snapshot()));

            s_log.Flush();
        }

        private static void Finish()
        {
            var bare = $"minted={s_mintedBare} reused={s_reusedBare} pool={s_barePool.Count}";
            var violations = (s_factory?.Violations ?? 0)
                + s_nullData + s_indexMismatch + s_ghostVisible
                + s_conservationBroken + s_poolOverflow;

            s_log.Event("finish",
                ("mode", MODE),
                ("steps", s_steps.Count),
                ("tickGapMs", Percentiles(s_tickGapMs)),
                ("changeViewMs", Percentiles()),
                ("bare", bare),
                ("factory", s_factory?.StatsSnapshot() ?? "n/a(bare or MODE0)"),
                ("echo", EchoStats.Snapshot()));

            s_log.Summary(
                ("mode", MODE),
                ("steps", s_steps.Count),
                ("prepared", s_prepared),
                ("clearing", s_clearing),
                ("indexChanged", s_indexChanged),
                ("nullData", s_nullData),
                ("duplicateKey", s_duplicateKey),
                ("indexMismatch", s_indexMismatch),
                ("ghostRows", s_ghostRows),
                ("ghostVisible", s_ghostVisible),
                ("conservationBroken", s_conservationBroken),
                ("poolOverflow", s_poolOverflow),
                ("doubleAcquire", s_factory?.DoubleAcquire ?? 0),
                ("doubleRecycle", s_factory?.DoubleRecycle ?? 0),
                ("invalidTransition", s_factory?.InvalidTransition ?? 0),
                ("unknownElement", s_factory?.UnknownElement ?? 0),
                ("minted", s_factory?.Minted ?? s_mintedBare),
                ("reuseRate", Math.Round(s_factory?.ReuseRate ?? 0, 3)),
                ("tickGapMs", Percentiles(s_tickGapMs)),
                ("echoMatched", EchoStats.Matched),
                ("echoMismatch", EchoStats.Mismatch),
                ("echoExpired", EchoStats.Expired),
                ("echoNotExpected", EchoStats.NotExpected),
                ("changeViewMs", Percentiles()),
                ("violations", violations),
                ("PASS", violations == 0 ? "true" : "false"));
        }

        // ── ItemsRepeater 生命周期事件（下标来源，别再用 Data 反查） ──────────

        private static void WireRepeaterEvents(MuxControls.ItemsRepeater rc)
        {
            rc.ElementPrepared += (_, args) =>
            {
                s_prepared++;
                var el = args.Element;
                s_live[el] = (args.Index, (el as TextBlock)?.Text ?? string.Empty);
            };

            rc.ElementClearing += (_, args) =>
            {
                s_clearing++;
                var el = args.Element;
                s_cleared.Add((el, (el as TextBlock)?.Text ?? string.Empty));
                s_live.Remove(el);
            };

            rc.ElementIndexChanged += (_, args) =>
            {
                s_indexChanged++;
                var el = args.Element;
                s_live[el] = s_live.TryGetValue(el, out var cur)
                    ? (args.NewIndex, cur.Key)
                    : (args.NewIndex, (el as TextBlock)?.Text ?? string.Empty);
            };
        }

        // ── 模板 / 工厂 ────────────────────────────────────────────────────

        private static object BuildTemplate()
        {
            if (MODE == 0)
            {
                return BuildXamlDataTemplate();
            }

            if (MODE == 5)
            {
                var single = new TextBlock { Text = "SINGLE" };
                return new NativeElementFactory(
                    (data, parent) => single,
                    (element, parent) => { },
                    null).ItemTemplate;
            }

            if (MODE is 3 or 4)
            {
                // 裸桥自池：MODE 3 不改 Visibility，MODE 4 折叠 —— 幽灵行 A/B
                return new NativeElementFactory(
                    (data, parent) =>
                    {
                        if (data is null)
                        {
                            s_nullData++;
                        }

                        TextBlock tb;
                        if (s_barePool.Count > 0)
                        {
                            tb = (TextBlock)s_barePool.Pop();
                            s_reusedBare++;
                        }
                        else
                        {
                            tb = new TextBlock();
                            s_mintedBare++;
                        }

                        tb.Text = data?.ToString() ?? "null";
                        if (MODE == 4)
                        {
                            tb.Visibility = Visibility.Visible;
                        }

                        return tb;
                    },
                    (element, parent) =>
                    {
                        if (element is null)
                        {
                            return;
                        }

                        s_cleared.Add((element, (element as TextBlock)?.Text ?? string.Empty));
                        if (MODE == 4)
                        {
                            element.Visibility = Visibility.Collapsed;
                        }

                        if (s_barePool.Count < MaxPool)
                        {
                            s_barePool.Push(element);
                        }
                    },
                    null).ItemTemplate;
            }

            s_factory = new RecyclingElementFactory(
                create: () => new TextBlock(),
                bind: (container, data, parent) =>
                {
                    if (data is null)
                    {
                        s_nullData++;
                    }

                    if (container is TextBlock tb)
                    {
                        tb.Text = data?.ToString() ?? "null";
                    }
                },
                maxPool: MODE == 2 ? 0 : MaxPool,
                logPath: null)
            {
                CollapseOnRecycle = true,
            };
            return s_factory.ItemTemplate;
        }

        private static void SwitchToDuplicateData()
        {
            var dup = new List<string>();
            for (int i = 0; i < ItemCount; i++)
            {
                dup.Add(Key(i % 7));
            }

            s_duplicateKey = ItemCount;
            s_items.Clear();
            foreach (var d in dup)
            {
                s_items.Add(d);
            }
        }

        private static string Key(int i) => "Row-" + i.ToString("D4", CultureInfo.InvariantCulture);

        private static string Percentiles(List<long> values)
        {
            if (values.Count == 0)
            {
                return "n/a";
            }

            var sorted = values.OrderBy(x => x).ToList();
            long P(double q) => sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * sorted.Count))];
            return $"p50={P(0.5)} p95={P(0.95)} p99={P(0.99)} max={sorted[^1]} n={sorted.Count}";
        }

        private static string Percentiles() => Percentiles(s_changeViewMs);

        private static Windows.UI.Xaml.DataTemplate BuildXamlDataTemplate()
        {
            const string xaml =
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "  <TextBlock Text='{Binding}' />" +
                "</DataTemplate>";
            return (Windows.UI.Xaml.DataTemplate)Windows.UI.Xaml.Markup.XamlReader.Load(xaml);
        }

        // ── 归档日志 ──────────────────────────────────────────────────────
        //
        // 之前的 reactor-startup.log 放在 LocalState 根目录，包 GUID 一轮换就丢，
        // 这次直接按 run 归档，每次跑完能拿到 manifest/config/events/summary 四件套。

        private sealed class RunLog
        {
            private readonly string _dir;
            private readonly string _runId;
            private System.IO.StreamWriter? _writer;
            private int _pending;

            /// <summary>本轮归档目录。</summary>
            public string Directory => _dir;

            public RunLog(int mode, int itemCount)
            {
                _runId = $"{DateTime.Now:yyyyMMdd-HHmmss}_m{mode}";
                var root = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                _dir = System.IO.Path.Combine(root, "ReactorRuns", _runId);
                System.IO.Directory.CreateDirectory(_dir);

                var meta = Json(
                    ("runId", _runId),
                    ("mode", mode),
                    ("itemCount", itemCount),
                    ("itemHeight", ItemHeight),
                    ("tickMs", TickMs),
                    ("maxPool", MaxPool),
                    ("layout", "StackLayout"),
                    ("collapseOnRecycle", MODE is 1 or 2 or 4),
                    ("roundTrips", RoundTrips),
                    ("jumpCount", JumpCount),
                    ("osVersion", Environment.OSVersion.VersionString),
                    ("startedAt", DateTime.Now.ToString("o", CultureInfo.InvariantCulture)));

                Write("manifest.json", meta);
                Write("config.json", meta);
            }

            public void Event(string kind, params (string Key, object Value)[] fields)
            {
                var all = new List<(string, object)>
                {
                    ("t", DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                    ("evt", kind),
                };
                all.AddRange(fields);
                Append("events.ndjson", Json(all.ToArray()));
            }

            public void Summary(params (string Key, object Value)[] fields) =>
                Write("summary.json", Json(fields));

            /// <summary>把 events 缓冲刷盘。每个检查点后调一次，保证进程被杀也不丢证据。</summary>
            public void Flush()
            {
                try
                {
                    _writer?.Flush();
                    _pending = 0;
                }
                catch
                {
                    // 刷盘失败不要带崩压测
                }
            }

            private void Write(string name, string content) =>
                System.IO.File.WriteAllText(System.IO.Path.Combine(_dir, name), content);

            // 注意：这里必须复用同一个 StreamWriter。
            // 上一版每行都 new FileStream + dispose，一轮压测要开关两万多次 —— 在 UI 线程上
            // 做同步 IO 会把 tick 间隔从 150ms 拖到 900ms，本身就是抖动源和退出诱因之一。
            private void Append(string name, string line)
            {
                try
                {
                    if (System.Threading.Interlocked.Increment(ref _pending) > 0 && _writer is null)
                    {
                        var fs = new System.IO.FileStream(
                            System.IO.Path.Combine(_dir, name),
                            System.IO.FileMode.Append,
                            System.IO.FileAccess.Write,
                            System.IO.FileShare.ReadWrite);
                        _writer = new System.IO.StreamWriter(fs) { AutoFlush = false };
                    }

                    _writer!.Write(line + Environment.NewLine);

                    if (_pending >= 64)
                    {
                        Flush();
                    }
                }
                catch
                {
                    // 日志写失败不能影响压测本身
                }
            }

            private static string Json(params (string Key, object Value)[] fields) =>
                "{" + string.Join(",", fields.Select(f =>
                    "\"" + f.Key + "\":" + (f.Value is string s
                        ? "\"" + s.Replace("\\", "\\\\").Replace("\"", "'") + "\""
                        : Convert.ToString(f.Value, CultureInfo.InvariantCulture) ?? "null"))) + "}";
        }
    }
}
