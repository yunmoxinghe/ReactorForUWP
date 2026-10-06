// ItemsRepeater 虚拟化 A/B 压测 + 不变量校验（虚拟化的回归工具，不是临时代码）
//
// 目标不是「打印 minted/realized」，而是**证明不变量成立**。
// 每一轮都会落一份可追溯的归档：
//
//   ReactorRuns/<runId>/
//       manifest.json    跑的什么（Mode / 项数 / 布局 / 折叠开关 / 机器信息）
//       config.json      同上，供脚本 diff
//       events.ndjson    每个检查点一行
//       summary.json     最终计数 + PASS/FAIL
//
// Mode 由测试壳（UwpApp\TestShell.cs）经 FactoryProbeProps 传入：
// 菜单前 6 项就是这 6 档，点一下切一档，不必改代码、改文件或重新部署。
//
// Mode 语义：
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
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Internal;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace UwpApp
{
    /// <summary>
    /// 压测页参数。测试壳用 <c>Component&lt;T, TProps&gt;</c> 传进来：
    /// 六种 Mode 就是六份参数，切对照不必再改代码、改文件或重新部署。
    /// </summary>
    public sealed record FactoryProbeProps(
        int Mode = 1,
        int ItemCount = 5000,
        double ItemHeight = 32,
        int TickMs = 150,
        int MaxPool = 512,
        int RoundTrips = 20,
        int JumpCount = 100,
        /// <summary>
        /// true = 逐项慢滚（每 tick 前进 SmoothStep 项），false = 原压测的大跨步跳跃。
        /// 用来区分"闪烁"的两种来源：大跨步 + 滚动动画会扫过上千个元素必然闪，
        /// 慢滚若仍闪才是 realization 本身或桥的问题。
        /// </summary>
        bool Smooth = false,
        int SmoothStep = 2,
        int SmoothTicks = 120);

    public sealed class FactoryProbePage : Component<FactoryProbeProps>
    {
        private int Mode => Props.Mode;
        private int ItemCount => Props.ItemCount;
        private double ItemHeight => Props.ItemHeight;
        private int TickMs => Props.TickMs;
        private int MaxPool => Props.MaxPool;
        private int RoundTrips => Props.RoundTrips;
        private int JumpCount => Props.JumpCount;
        private bool Smooth => Props.Smooth;
        private int SmoothStep => Props.SmoothStep;
        private int SmoothTicks => Props.SmoothTicks;

        /// <summary>当前在跑的那一轮：进程级 crash hook 拿不到实例，靠这个静态引用转发。</summary>
        private static FactoryProbePage? s_active;
        private static bool s_hooksInstalled;

        // ── 计数器 ────────────────────────────────────────────────────────
        private int _prepared, _clearing, _indexChanged;
        private int _nullData, _duplicateKey, _indexMismatch, _ghostRows, _ghostVisible;
        private int _conservationBroken, _poolOverflow;
        private int _mintedBare, _reusedBare;
        private readonly Stack<UIElement> _barePool = new();
        private readonly Dictionary<UIElement, (int Index, string Key)> _live =
            new(ReferenceEqualityComparer.Instance);
        private readonly List<(UIElement El, string Text)> _cleared = new();

        private ObservableCollection<string> _items = new();
        private ScrollViewer _scroller = null!;
        private RecyclingElementFactory? _factory;
        private RunLog _log = null!;

        private readonly List<long> _changeViewMs = new();
        private readonly List<long> _tickGapMs = new();
        private readonly Stopwatch _watch = new();
        private long _lastTickMs;
        private readonly List<(string Name, int Index, Action? Mutate)> _steps = new();
        private int _stepIndex = -1;
        private string _stepName = "init";

        /// <summary>本轮的 tick 定时器；切页/重跑时由 <see cref="StopRun"/> 停掉。</summary>
        private DispatcherTimer? _timer;
        private Action<string>? _setStatus;

        /// <summary>
        /// 宿主控件的创建委托。此处缓存是因为它在每次 <c>Token</c> 变化时才被调用，
        /// 缓存能让"重建只会发生在 <c>Token</c> 变化时"这件事在代码里一眼可见。
        /// </summary>
        /// <remarks>
        /// <b>不要把这里理解成"引用必须稳定，否则会每帧重建"。</b>框架那边的判据
        /// 只有 <c>Token</c>：<c>Internal/Handlers.Native.cs</c> 的 <c>Update</c>
        /// 全程只比 <c>Equals(oldElement.Token, newElement.Token)</c>，<c>Factory</c>
        /// 的引用<b>根本不参与比较</b>。所以内联 lambda（像 <c>XamlDiffProbe</c> 那样
        /// 直接写 <c>Native(() => host.Current!)</c>）也不会触发重建。
        /// <c>Reactor.uwp/Elements/Native.cs</c> 上那句"引用必须稳定"的说法与实现不符。
        /// </remarks>
        private Func<UIElement>? _hostFactory;
        private Func<UIElement> HostFactory => _hostFactory ??= BuildHost;

        public override Element Render()
        {
            var (runId, setRunId) = UseState(0);
            var (status, setStatus) = UseState("未开始");

            UseEffect(() =>
            {
                _setStatus = setStatus;

                try
                {
                    Say("[probe] effect enter Mode=" + Mode);

                    // 控件已经在可视树里了（宿主是页面的一部分）就可以直接开跑；
                    // 还没挂上就等它 Loaded，那时 StartRun 会被再调一次。
                    StartRun();
                    Say("[probe] StartRun returned");
                }
                catch (Exception ex)
                {
                    // 注意：这里不能用 _log?.Event —— 一旦 RunLog 还没建出来就抛异常，
                    // 那条 ?. 会让整段故障变成完全静默（本次就是这么丢的第一手证据）。
                    Say("[probe] EX " + ex);
                    setStatus("EX " + ex.Message);
                }

                // 切页 / 重跑：先把上一轮的 timer 收干净，否则两个实例会同时滚。
                return () => StopRun();
            },
                // 依赖里带上 Props：从 M1 切到 M2 时元素类型相同、会走就地 patch，
                // 光靠 runId 不会重跑——把参数本身当依赖，换参数就等于换一轮。
                runId, (object)Props);

            return VStack(
                TextBlock($"压测 Mode={Mode} ｜ {Describe(Mode)}"),
                TextBlock($"items={ItemCount} itemH={ItemHeight} tick={TickMs}ms pool={MaxPool}"),
                TextBlock(status),
                HStack(
                    Button("重跑", () => setRunId(runId + 1)),
                    Button("停止", () => StopRun())
                ),
                // 压测控件躺在页面里（以前是弹一个 Popup 浮层：既挡住左侧菜单，
                // 又脱离页面布局）。Native() 是逃生舱：控件自己造，
                // token=runId 一变就换一棵新的——"重跑"就是换一棵。
                Native(HostFactory, token: runId, onDispose: _ => StopRun())
                    .Width(280)
                    .Height(500)
            );
        }

        /// <summary>Mode 的一句话说明，页首显示用。</summary>
        public static string Describe(int mode) => mode switch
        {
            0 => "纯内置 ItemsRepeater + XAML DataTemplate（基线）",
            1 => "原生桥池化，回收时折叠",
            2 => "原生桥不复用（maxPool=0，看无界增长）",
            3 => "裸桥自池，不改 Visibility（预期出现幽灵行）",
            4 => "裸桥自池，回收时折叠（幽灵行应消失）",
            5 => "极限最小回调：永远返回同一个元素",
            _ => "未知模式",
        };

        // ── 编排 ──────────────────────────────────────────────────────────

        // 同时进 VS 输出窗口和 LocalState\reactor-startup.log，便于首帧就能看到进展。
        private void Say(string msg)
        {
            Debug.WriteLine(msg);
            Reactor.Uwp.Hosting.ReactorApplication.Trace(msg);
        }

        /// <summary>
        /// 造压测用的那棵原生控件树（ScrollViewer + ItemsRepeater），交给
        /// <c>Native()</c> 挂在页面里。尺寸交给宿主容器，这里只管结构。
        /// </summary>
        private UIElement BuildHost()
        {
            _items = new ObservableCollection<string>();
            for (int i = 0; i < ItemCount; i++)
            {
                _items.Add(Key(i));
            }

            var rc = new MuxControls.ItemsRepeater
            {
                Layout = new MuxControls.StackLayout(),
                ItemsSource = _items,
            };
            Say("[probe] repeater created items=" + _items.Count);
            WireRepeaterEvents(rc);
            Say("[probe] building ItemTemplate Mode=" + Mode);
            rc.ItemTemplate = BuildTemplate();
            Say("[probe] ItemTemplate ok");

            _scroller = new ScrollViewer
            {
                Content = rc,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollMode = ScrollMode.Enabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };

            // 挂到可视树之后才有 Viewport，ChangeView 才有效；所以用 Loaded 起跑，
            // 而不是赌 effect 执行时控件已经就位。StartRun 自带幂等保护。
            _scroller.Loaded += (_, _) =>
            {
                if (_timer is null)
                {
                    StartRun();
                }
            };

            return _scroller;
        }

        private void StartRun()
        {
            // 幂等：effect 与 Loaded 都可能调到这里，已经在跑就直接返回。
            if (_timer is not null || _scroller is null)
            {
                return;
            }

            s_active = this;
            ResetCounters();
            Say("[probe] creating RunLog");
            _log = new RunLog(Mode, ItemCount, ItemHeight, TickMs, MaxPool, RoundTrips, JumpCount,
                Mode is 1 or 2 or 4, Smooth);
            Say("[probe] RunLog dir=" + _log.Directory);
            EchoStats.Reset();
            _watch.Restart();

            InstallCrashHooks();

            BuildSteps();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
            _timer.Tick += (_, _) =>
            {
                // Timer drift：tick 本该 150ms 一次，实测 500~900ms —— gap 就是 UI 线程
                // 被 realize/layout/同步 IO 占住的直接度量（比 ChangeView 同步耗时有意义）。
                var now = _watch.ElapsedMilliseconds;
                var gap = _lastTickMs == 0 ? 0 : now - _lastTickMs;
                _lastTickMs = now;

                try
                {
                    // 上一个 step 已经 settle 完，先做检查点，再走下一步。
                    if (_stepIndex >= 0)
                    {
                        Checkpoint(gap);
                    }

                    // 每步一行轻量 trace：进程一旦被杀，最后一行就是死亡位置。
                    Say($"[probe] tick#{_stepIndex} {_stepName} gap={gap}ms live={_live.Count} ghost={_ghostVisible}");

                    // 状态行不每步都刷：150 步里刷 8 次左右，既不至于把 UI 线程
                    // 钉在重渲染上污染 tick gap，又能在页面上看到进度。
                    if (_stepIndex % 20 == 0 || _stepIndex == _steps.Count - 1)
                    {
                        _setStatus?.Invoke(
                            $"step {_stepIndex + 1}/{_steps.Count} {_stepName} ｜ live={_live.Count} " +
                            $"ghost={_ghostVisible} mismatch={_indexMismatch}");
                    }

                    if (!Advance())
                    {
                        Say("[probe] all steps done, writing summary");
                        StopRun();
                        Finish();
                        Say("[probe] summary written");
                        _setStatus?.Invoke(FinishStatus());
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Say("[probe] TICK EX " + ex);
                }
            };
            Say("[probe] steps=" + _steps.Count + " starting timer");
            _timer.Start();

            Say("[probe] begin mode=" + Mode + " items=" + ItemCount);
            _log.Event("begin", ("mode", Mode), ("items", ItemCount), ("steps", _steps.Count));
        }

        /// <summary>收摊：停 timer、摘掉活跃引用。切页、重跑、跑完都走这里。</summary>
        /// <remarks>控件本身由 <c>Native()</c> 宿主负责摘出可视树，这里不管。</remarks>
        private void StopRun()
        {
            if (_timer is { } timer)
            {
                timer.Stop();
                _timer = null;
            }

            if (ReferenceEquals(s_active, this))
            {
                s_active = null;
            }
        }

        /// <summary>"重跑"要清零计数：否则第二轮的数字是两轮叠加的。</summary>
        private void ResetCounters()
        {
            _prepared = _clearing = _indexChanged = 0;
            _nullData = _duplicateKey = _indexMismatch = _ghostRows = _ghostVisible = 0;
            _conservationBroken = _poolOverflow = 0;
            _mintedBare = _reusedBare = 0;
            _barePool.Clear();
            _live.Clear();
            _cleared.Clear();
            _changeViewMs.Clear();
            _tickGapMs.Clear();
            _lastTickMs = 0;
            _stepIndex = -1;
            _stepName = "init";
        }

        /// <summary>进程退出/未处理异常的最后一道证据：任何路径都要留下而不是静默。</summary>
        /// <remarks>
        /// 这三个 hook 是<b>进程级</b>的，只能注册一次；lambda 里因此不能捕获 this
        /// （否则会永久抓住第一个实例，切页后仍在往旧日志里写）。统一走
        /// <see cref="s_active"/> 转发到当前正在跑的那一轮。
        /// </remarks>
        private void InstallCrashHooks()
        {
            if (s_hooksInstalled)
            {
                return;
            }

            s_hooksInstalled = true;

            try
            {
                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    Say("[probe] UNHANDLED " + e.ExceptionObject);
                    if (s_active is not { } self)
                    {
                        return;
                    }

                    self._log?.Event("crash",
                        ("kind", "unhandled"),
                        ("fatal", e.IsTerminating),
                        ("step", self._stepName),
                        ("detail", Convert.ToString(e.ExceptionObject) ?? "?"));
                    self._log?.Flush();
                };

                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    Say("[probe] ProcessExit");
                    if (s_active is not { } self)
                    {
                        return;
                    }

                    self._log?.Event("exit",
                        ("kind", "process-exit"),
                        ("exitCode", Environment.ExitCode),
                        ("stepIndex", self._stepIndex),
                        ("step", self._stepName),
                        ("prepared", self._prepared),
                        ("clearing", self._clearing),
                        ("ghost", self._ghostVisible),
                        ("factory", self._factory?.StatsSnapshot() ?? "n/a"));
                    self._log?.Flush();
                };

                Windows.UI.Xaml.Application.Current.UnhandledException += (_, e) =>
                {
                    Say("[probe] APP UNHANDLED " + e.Exception);
                    if (s_active is { } self)
                    {
                        self._log?.Event("crash",
                            ("kind", "app-unhandled"),
                            ("step", self._stepName),
                            ("detail", e.Exception?.ToString() ?? "?"));
                        self._log?.Flush();
                    }

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

        private void BuildSteps()
        {
            _steps.Clear();

            if (Smooth)
            {
                // 慢滚对照：每 tick 只前进 SmoothStep 项，滚动动画覆盖的行数与
                // 人拖滚动条同量级。此时若还闪，才是 realization / 桥的问题；
                // 不闪就说明上面的"闪烁"来自压测自己每 150ms 跳几百项。
                for (int i = 0; i < SmoothTicks; i++)
                {
                    _steps.Add(($"S{i}", Math.Min(ItemCount - 1, i * SmoothStep), null));
                }

                return;
            }

            // A 单向滚动
            for (int i = 0; i <= 10; i++)
            {
                _steps.Add(($"A{i}", Math.Min(ItemCount - 1, i * 500), null));
            }

            // B 往返
            for (int i = 0; i < RoundTrips; i++)
            {
                _steps.Add((i % 2 == 0 ? "B-end" : "B-home", i % 2 == 0 ? ItemCount - 1 : 0, null));
            }

            // C 随机跳跃（固定种子，可复现）
            var rng = new Random(20261004);
            for (int i = 0; i < JumpCount; i++)
            {
                _steps.Add(($"C{i}", rng.Next(0, ItemCount), null));
            }

            // D 重复数据：同一字符串出现在多个 index，验证不能用 Data 反推 Index
            for (int i = 0; i < 5; i++)
            {
                _steps.Add(($"D{i}", Math.Min(ItemCount - 1, i * 900),
                    i == 0 ? (Action)SwitchToDuplicateData : null));
            }

            // E 数据源结构变化：应看到 ElementIndexChanged，而不是全部重建
            _steps.Add(("E-insert", 0, () => _items.Insert(0, "INSERTED")));
            _steps.Add(("E-remove", 100, () => _items.RemoveAt(5)));
            _steps.Add(("E-move", 200, () => _items.Move(_items.Count - 1, 1)));

            // F 再完整滚一遍
            for (int i = 0; i <= 10; i++)
            {
                _steps.Add(($"F{i}", Math.Min(_items.Count - 1, i * 500), null));
            }
        }

        private bool Advance()
        {
            _stepIndex++;
            if (_stepIndex >= _steps.Count)
            {
                return false;
            }

            var step = _steps[_stepIndex];
            _stepName = step.Name;
            step.Mutate?.Invoke();

            var sw = Stopwatch.StartNew();
            _scroller.ChangeView(null, step.Index * ItemHeight, null);
            sw.Stop();
            _changeViewMs.Add(sw.ElapsedMilliseconds);
            return true;
        }

        // ── 检查点 ────────────────────────────────────────────────────────

        private void Checkpoint(long gap = 0)
        {
            // 1) 内容错位：live 元素的文本必须等于当前数据源在该下标上的值
            foreach (var kv in _live.ToList())
            {
                var index = kv.Value.Index;
                if (index < 0 || index >= _items.Count)
                {
                    continue;
                }

                var text = (kv.Key as TextBlock)?.Text ?? string.Empty;
                var expected = _items[index];
                if (!string.Equals(text, expected, StringComparison.Ordinal))
                {
                    _indexMismatch++;
                    _ghostRows++;
                    _log.Event("mismatch",
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
            //    真正的幽灵行 = 不在 _live 里 + Visible。
            foreach (var c in _cleared)
            {
                if (_live.ContainsKey(c.El))
                {
                    continue; // 已复用：此刻 Visible 是合法的
                }

                if (c.El.Visibility == Visibility.Visible)
                {
                    // 限流：单次 galleries 可能上千条，全写会把 UI 线程钉在同步文件 IO 上。
                    if (_ghostVisible < 20)
                    {
                        _log.Event("ghost", ("text", c.Text));
                    }

                    _ghostVisible++;
                    _ghostRows++;
                }
            }
            _cleared.Clear();

            // 3) 库存守恒 + 池上限
            if (_factory is not null)
            {
                if (!_factory.ConservationOk)
                {
                    _conservationBroken++;
                }

                if (_factory.Pooled > MaxPool)
                {
                    _poolOverflow++;
                }
            }

            if (gap > 0)
            {
                _tickGapMs.Add(gap);
            }

            _log.Event("checkpoint",
                ("step", _stepName),
                ("gapMs", gap),
                ("changeViewMs", _changeViewMs.Count == 0 ? 0 : _changeViewMs[^1]),
                ("prepared", _prepared),
                ("clearing", _clearing),
                ("indexChanged", _indexChanged),
                ("live", _live.Count),
                ("nullData", _nullData),
                ("indexMismatch", _indexMismatch),
                ("ghostVisible", _ghostVisible),
                ("factory", _factory?.StatsSnapshot() ?? "n/a(bare or MODE0)"),
                ("echo", EchoStats.Snapshot()));

            _log.Flush();
        }

        /// <summary>
        /// 违规总数：<b>唯一算数的地方</b>。
        /// </summary>
        /// <remarks>
        /// 页面上的结论行与 summary.json 的结论必须来自同一个数——这两处以前是各自抄
        /// 一遍同一串加法，加一项计数器时就可能出现"屏幕上写 PASS、归档里写 FAIL"，
        /// 而那正是这一整套归档唯一不能被怀疑的东西。
        /// </remarks>
        private int Violations =>
            (_factory?.Violations ?? 0)
            + _nullData + _indexMismatch + _ghostVisible
            + _conservationBroken + _poolOverflow;

        private void Finish()
        {
            var bare = $"minted={_mintedBare} reused={_reusedBare} pool={_barePool.Count}";
            var violations = Violations;

            _log.Event("finish",
                ("mode", Mode),
                ("steps", _steps.Count),
                ("tickGapMs", Percentiles(_tickGapMs)),
                ("changeViewMs", Percentiles()),
                ("bare", bare),
                ("factory", _factory?.StatsSnapshot() ?? "n/a(bare or MODE0)"),
                ("echo", EchoStats.Snapshot()));

            _log.Summary(
                ("mode", Mode),
                ("steps", _steps.Count),
                ("prepared", _prepared),
                ("clearing", _clearing),
                ("indexChanged", _indexChanged),
                ("nullData", _nullData),
                ("duplicateKey", _duplicateKey),
                ("indexMismatch", _indexMismatch),
                ("ghostRows", _ghostRows),
                ("ghostVisible", _ghostVisible),
                ("conservationBroken", _conservationBroken),
                ("poolOverflow", _poolOverflow),
                ("doubleAcquire", _factory?.DoubleAcquire ?? 0),
                ("doubleRecycle", _factory?.DoubleRecycle ?? 0),
                ("invalidTransition", _factory?.InvalidTransition ?? 0),
                ("unknownElement", _factory?.UnknownElement ?? 0),
                ("minted", _factory?.Minted ?? _mintedBare),
                ("reuseRate", Math.Round(_factory?.ReuseRate ?? 0, 3)),
                ("tickGapMs", Percentiles(_tickGapMs)),
                ("echoMatched", EchoStats.Matched),
                ("echoMismatch", EchoStats.Mismatch),
                ("echoExpired", EchoStats.Expired),
                ("echoNotExpected", EchoStats.NotExpected),
                ("changeViewMs", Percentiles()),
                ("violations", violations),
                ("PASS", violations == 0 ? "true" : "false"));
        }

        /// <summary>跑完之后页面上的那一行：PASS/FAIL + 关键计数 + 归档目录。</summary>
        private string FinishStatus()
        {
            return $"{(Violations == 0 ? "PASS" : "FAIL")} violations={Violations} ｜ " +
                   $"prepared={_prepared} clearing={_clearing} indexChanged={_indexChanged} ｜ " +
                   $"minted={_factory?.Minted ?? _mintedBare} " +
                   $"reuse={Math.Round(_factory?.ReuseRate ?? 0, 3)} ｜ ghost={_ghostVisible} ｜ " +
                   $"归档 {_log?.Directory}";
        }

        // ── ItemsRepeater 生命周期事件（下标来源，别再用 Data 反查） ──────────

        private void WireRepeaterEvents(MuxControls.ItemsRepeater rc)
        {
            rc.ElementPrepared += (_, args) =>
            {
                _prepared++;
                var el = args.Element;
                _live[el] = (args.Index, (el as TextBlock)?.Text ?? string.Empty);
            };

            rc.ElementClearing += (_, args) =>
            {
                _clearing++;
                var el = args.Element;
                _cleared.Add((el, (el as TextBlock)?.Text ?? string.Empty));
                _live.Remove(el);
            };

            rc.ElementIndexChanged += (_, args) =>
            {
                _indexChanged++;
                var el = args.Element;
                _live[el] = _live.TryGetValue(el, out var cur)
                    ? (args.NewIndex, cur.Key)
                    : (args.NewIndex, (el as TextBlock)?.Text ?? string.Empty);
            };
        }

        // ── 模板 / 工厂 ────────────────────────────────────────────────────

        private object BuildTemplate()
        {
            if (Mode == 0)
            {
                return BuildXamlDataTemplate();
            }

            if (Mode == 5)
            {
                var single = new TextBlock { Text = "SINGLE" };
                return new NativeElementFactory(
                    (data, parent) => single,
                    (element, parent) => { },
                    null).ItemTemplate;
            }

            if (Mode is 3 or 4)
            {
                // 裸桥自池：Mode 3 不改 Visibility，Mode 4 折叠 —— 幽灵行 A/B
                return new NativeElementFactory(
                    (data, parent) =>
                    {
                        if (data is null)
                        {
                            _nullData++;
                        }

                        TextBlock tb;
                        if (_barePool.Count > 0)
                        {
                            tb = (TextBlock)_barePool.Pop();
                            _reusedBare++;
                        }
                        else
                        {
                            tb = new TextBlock();
                            _mintedBare++;
                        }

                        tb.Text = data?.ToString() ?? "null";
                        if (Mode == 4)
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

                        _cleared.Add((element, (element as TextBlock)?.Text ?? string.Empty));
                        if (Mode == 4)
                        {
                            element.Visibility = Visibility.Collapsed;
                        }

                        if (_barePool.Count < MaxPool)
                        {
                            _barePool.Push(element);
                        }
                    },
                    null).ItemTemplate;
            }

            _factory = new RecyclingElementFactory(
                create: () => new TextBlock(),
                bind: (container, data, parent) =>
                {
                    if (data is null)
                    {
                        _nullData++;
                    }

                    if (container is TextBlock tb)
                    {
                        tb.Text = data?.ToString() ?? "null";
                    }
                },
                maxPool: Mode == 2 ? 0 : MaxPool,
                logPath: null)
            {
                CollapseOnRecycle = true,
            };
            return _factory.ItemTemplate;
        }

        private void SwitchToDuplicateData()
        {
            var dup = new List<string>();
            for (int i = 0; i < ItemCount; i++)
            {
                dup.Add(Key(i % 7));
            }

            _duplicateKey = ItemCount;
            _items.Clear();
            foreach (var d in dup)
            {
                _items.Add(d);
            }
        }

        private string Key(int i) => "Row-" + i.ToString("D4", CultureInfo.InvariantCulture);

        private string Percentiles(List<long> values)
        {
            if (values.Count == 0)
            {
                return "n/a";
            }

            var sorted = values.OrderBy(x => x).ToList();
            long P(double q) => sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * sorted.Count))];
            return $"p50={P(0.5)} p95={P(0.95)} p99={P(0.99)} max={sorted[^1]} n={sorted.Count}";
        }

        private string Percentiles() => Percentiles(_changeViewMs);

        private Windows.UI.Xaml.DataTemplate BuildXamlDataTemplate()
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

            /// <summary>
            /// 参数从外层实例显式传进来：嵌套类不持有外层 this，
            /// 而 ItemHeight/TickMs 这些现在是实例属性而不是 const 了。
            /// </summary>
            public RunLog(
                int mode,
                int itemCount,
                double itemHeight,
                int tickMs,
                int maxPool,
                int roundTrips,
                int jumpCount,
                bool collapseOnRecycle,
                bool smooth = false)
            {
                _runId = $"{DateTime.Now:yyyyMMdd-HHmmss}_m{mode}" + (smooth ? "_smooth" : string.Empty);
                var root = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                _dir = System.IO.Path.Combine(root, "ReactorRuns", _runId);
                System.IO.Directory.CreateDirectory(_dir);

                var meta = Json(
                    ("runId", _runId),
                    ("mode", mode),
                    ("itemCount", itemCount),
                    ("itemHeight", itemHeight),
                    ("tickMs", tickMs),
                    ("maxPool", maxPool),
                    ("layout", "StackLayout"),
                    ("collapseOnRecycle", collapseOnRecycle),
                    ("roundTrips", roundTrips),
                    ("jumpCount", jumpCount),
                    ("smooth", smooth),
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

            private string Json(params (string Key, object Value)[] fields) =>
                "{" + string.Join(",", fields.Select(f =>
                    "\"" + f.Key + "\":" + (f.Value is string s
                        ? Quote(s)
                        : Convert.ToString(f.Value, CultureInfo.InvariantCulture) ?? "null"))) + "}";

            /// <summary>
            /// 字符串值进单行 NDJSON 前必须转义。
            /// </summary>
            /// <remarks>
            /// <c>events.ndjson</c> 是<b>一行一条</b>的格式：任何换字符都会把一条记录
            /// 劈成两行，后面每一行都不是合法 JSON。以前只处理了反斜杠和引号，
            /// 而 <c>"detail"</c> 字段装的是异常串——<c>Exception.ToString()</c>
            /// 天生是多行的（含 <c>\r\n</c> 和 <c>\t</c>）。也就是说<b>越是需要取证
            /// 的那一条（崩溃），归档越一定被它自己写坏</b>，事后用 jq 读直接报错，
            /// 而现场已经没了。引号按原样降级成单引号（保持原有可读性约定），
            /// 控制字符一律转义——不挑"\r\n\t 三个"，其余 <c>&lt; 0x20</c> 的字符
            /// 一律走 <c>\u00xx</c>：按名单转义的写法每漏一个就是一种新的坏档方式，
            /// 而漏的那个恰恰是没人见过的那个。
            /// </remarks>
            private static string Quote(string value)
            {
                var sb = new StringBuilder(value.Length + 8);
                sb.Append('"');

                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '"': sb.Append('\''); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ')
                            {
                                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }

                            break;
                    }
                }

                return sb.Append('"').ToString();
            }
        }
    }
}
