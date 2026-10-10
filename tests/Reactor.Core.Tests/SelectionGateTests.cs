using System;
using System.Collections.Generic;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 受控选中控件的回归测试：<b>判据穷举 + 闭环不变量 + 随机序列</b>三层。
/// </summary>
/// <remarks>
/// 分三层是因为它们抓的 bug 不同，缺一层就会漏一类事故：
/// <list type="number">
///   <item><b>判据穷举</b>：证明闸门这个零件本身对。抓"改判据时改错了优先级"。</item>
///   <item><b>闭环不变量</b>：证明接起来之后对。抓"零件都对但环断了"
///         ——"点了没反应"从来不是一个函数错了，而是环上某一环断了。</item>
///   <item><b>随机序列</b>：抓<b>还没想到的</b>组合。这次的病就是这么来的：
///         取消与选中两发的到达顺序没有保证，人工点击一次只覆盖一种顺序，
///         另一种顺序下的失败根本不会被看见。</item>
/// </list>
/// <para>
/// 这些用例全部不需要 UWP 运行时、不需要 UI 线程、不需要人点——
/// 判据是纯函数（<see cref="SelectionGate"/>），控件是模型
/// （<see cref="FakeSelectionControl"/>）。<b>所以要回归这类问题，跑测试就够了。</b>
/// </para>
/// </remarks>
internal static class SelectionGateTests
{
    // 量级是刻意往大了给的：这类 bug 的共性就是"只在特定值 / 特定到达顺序上复现"，
    // 序列越长、步数越多，才越可能撞上那个组合。跑一次不到一秒，
    // 拿 20 倍的量换"多覆盖一类组合"是笔划算买卖。
    private const int Sequences = 20000;
    private const int Steps = 24;

    public static void Run()
    {
        GateTruthTable();
        DeferredNotReadyTruthTable();
        Invariants();
        TwoPartGesture();
        ClickBeforeLoaded();
        ClickBeforeLoadedWithCancelLast();
        ClickBeforeLoadedWithPullBack();
        ClickTwiceBeforeLoaded();
        Fuzz();
    }

    // ── 1b. 补发判据穷举 ────────────────────────────────────────────
    /// <summary>
    /// 未就绪补发的两道判据：<b>该不该记</b>与<b>该不该兑现</b>。
    /// </summary>
    private static void DeferredNotReadyTruthTable()
    {
        Program.Section("SelectionGate / 未就绪补发判据穷举");

        // ── 该不该记（ShouldDeferNotReady）──
        Program.Check(
            "未就绪 + 值等于受控目标 → 不记（那是我们自己下发的中间态）",
            !SelectionGate.ShouldDeferNotReady(0, 0));

        Program.Check(
            "未就绪 + 值不等于受控目标 → 记（用户看得见且点到了）",
            SelectionGate.ShouldDeferNotReady(2, 0));

        Program.Check(
            "未就绪 + 取消选中（-1）→ 不记（补发 -1 等于凭空回调）",
            !SelectionGate.ShouldDeferNotReady(-1, 0));

        Program.Check(
            "还没下发过（target=null）+ 非负值 → 记",
            SelectionGate.ShouldDeferNotReady(1, null));

        // ── 该不该兑现（ShouldFlushDeferred）──
        Program.Check(
            "兑现时控件还停在记下的值、且仍不等于受控目标 → 兑现",
            SelectionGate.ShouldFlushDeferred(2, 2, 0));

        Program.Check(
            "兑现时用户已改主意（控件值变了）→ 作废",
            !SelectionGate.ShouldFlushDeferred(2, 3, 0));

        // 这一条是本轮修的那个 bug：控件在"未就绪"期间被点，进树复查时它已经被
        // WinUI 内部的 Select(-1)（RadioButtons.cpp:431 子项 Unchecked / :518
        // UpdateItemsSource）打到了"无选中"。旧判据见 -1 就放弃，于是那一发
        // 永远补不出来 —— state 不更新、界面不动，用户看到的就是"点了没反应"。
        // 该兑现的是 pending（用户那次点击的真实意图），不是控件此刻漂到的 -1。
        Program.Check(
            "兑现时控件漂到 -1、但记下的值仍有效 → 兑现（否则那次点击永远丢）",
            SelectionGate.ShouldFlushDeferred(2, -1, 0));

        // 这一条是 INV14：控件停在<b>受控目标</b>上、而记下的 pending 不是它。
        // 旧判据把这一格读成"用户又点了别的值"→ 作废。但把它放到受控目标上的
        // 是我们自己的异步回写（同一手势的"取消选中"那一发排下的 SelectionRestore），
        // 不是用户的手 —— 用户此刻并没有"改主意"这个动作，pending 仍然有效。
        // 判成作废的直接后果就是那一次点击凭空消失，而控件最后规规矩矩停在
        // 受控值上，界面毫无异常。
        Program.Check(
            "兑现时控件停在被我们自己拉回的受控值上 → 仍兑现（那不是用户改主意）",
            SelectionGate.ShouldFlushDeferred(2, 0, 0));

        // fix discipline：关掉开关必须回到旧行为，且上面那条断言翻成失败。
        // 不然这个"修"就只是把旧行为改了个名字，谁也没法证伪。
        SelectionGate.FlushWhenControlPulledBack = false;

        Program.Check(
            "关掉开关 → 回到旧行为（控件停在受控值上就判成用户改主意）",
            !SelectionGate.ShouldFlushDeferred(2, 0, 0));

        SelectionGate.FlushWhenControlPulledBack = true;

        // fix discipline：关掉开关必须回到旧行为，且上面那条断言翻成失败。
        // 不然这个"修"就只是把旧行为改了个名字，谁也没法证伪。
        SelectionGate.FlushWhenControlCleared = false;

        Program.Check(
            "关掉开关 → 回到旧行为（控件漂到 -1 就作废）",
            !SelectionGate.ShouldFlushDeferred(2, -1, 0));

        SelectionGate.FlushWhenControlCleared = true;

        Program.Check(
            "兑现前受控下发已把它收敛到位（pending == target）→ 作废，别多调一次",
            !SelectionGate.ShouldFlushDeferred(2, 2, 2));
    }

    // ── 1. 判据穷举 ────────────────────────────────────────────────
    private static void GateTruthTable()
    {
        Program.Section("SelectionGate / 判据穷举");

        // 判据零优先于一切：取消那一发必须被拦，且理由是"取消选中"而不是别的。
        Program.Check(
            "无实项 → 取消选中（优先于未就绪）",
            SelectionGate.Decide(hasRealItem: false, isReady: false, isRebuilding: false)
                == SelectionVerdict.CancelTransient);

        Program.Check(
            "无实项 → 取消选中（优先于重建中）",
            SelectionGate.Decide(hasRealItem: false, isReady: true, isRebuilding: true)
                == SelectionVerdict.CancelTransient);

        Program.Check(
            "有实项但未就绪 → 未就绪",
            SelectionGate.Decide(true, false, false) == SelectionVerdict.NotReady);

        Program.Check(
            "有实项、已就绪、重建中 → 重建中",
            SelectionGate.Decide(true, true, true) == SelectionVerdict.Rebuilding);

        Program.Check(
            "三项俱全 → 放行（回声那道由调用方单独判）",
            SelectionGate.Decide(true, true, false) == SelectionVerdict.Pass);

        // 穷举 8 种组合，确认"只有全通才放行"这一条不被某次重构破坏。
        var passCount = 0;
        for (var mask = 0; mask < 8; mask++)
        {
            var hasReal = (mask & 1) != 0;
            var ready = (mask & 2) != 0;
            var rebuilding = (mask & 4) != 0;

            if (SelectionGate.Decide(hasReal, ready, rebuilding) == SelectionVerdict.Pass)
            {
                passCount++;
            }
        }

        Program.Expect("8 种组合里只有 1 种放行", 1, passCount);

        // 回声登记的条件必须与"回声那道闸何时可达"严格一致——
        // 不一致就是泄漏（见 ShouldExpectEcho 的注释与 INV7）。
        Program.Check(
            "写入真实下标 + 就绪 + 非重建 → 登记回声",
            SelectionGate.ShouldExpectEcho(2, isReady: true, isRebuilding: false));

        Program.Check(
            "未就绪时写入 → 不登记（事件会被「未就绪」吞掉，等不到消费）",
            !SelectionGate.ShouldExpectEcho(2, isReady: false, isRebuilding: false));

        Program.Check(
            "重建中写入 → 不登记（事件会被「重建中」吞掉）",
            !SelectionGate.ShouldExpectEcho(2, isReady: true, isRebuilding: true));

        Program.Check(
            "写入 -1（清空）→ 不登记（事件 AddedItems 无实项，被判据零拦掉）",
            !SelectionGate.ShouldExpectEcho(-1, isReady: true, isRebuilding: false));

        // 穷举：登记为真 当且仅当 该写入产生的事件会走到回声那道。
        var mismatches = 0;
        for (var mask = 0; mask < 8; mask++)
        {
            var ready = (mask & 1) != 0;
            var rebuilding = (mask & 2) != 0;
            var negative = (mask & 4) != 0;

            var expect = SelectionGate.ShouldExpectEcho(negative ? -1 : 1, ready, rebuilding);

            // 事件能不能走到回声道：判据零看 AddedItems 有无实项（写 -1 则无），
            // 判据一二看就绪与重建。
            var reachable = SelectionGate.Decide(
                hasRealItem: !negative, isReady: ready, isRebuilding: rebuilding)
                == SelectionVerdict.Pass;

            if (expect != reachable)
            {
                mismatches++;
            }
        }

        Program.Expect("8 种写入情形下「登记」与「回声闸可达」完全一致", 0, mismatches);
    }

    // ── 3. 两发手势 ────────────────────────────────────────────────
    /// <summary>
    /// INV11：一次手势抛两发（先取消、再选中）时，第一发排下的纠正<b>不得</b>
    /// 覆盖用户刚选的值。
    /// </summary>
    /// <remarks>
    /// 真机症状："设置项点了弹回原样、从来不落盘"。日志里的
    /// <c>受控下发 ComboBox#5: 1 → 0</c> 就是那一笔覆盖。
    /// <para>
    /// 这道防线有<b>两条</b>，各自都要有反向对照：
    /// <list type="number">
    ///   <item>第二发放行时作废待兑现的纠正（<c>CancelRestoreOnRealSelect</c>）；</item>
    ///   <item>纠正兑现前发现控件停在"用户刚选的值"上就收手
    ///         （<c>GuardRestoreOnUserValue</c>）——第二发若被别道闸吞掉
    ///         （例如控件还没 <c>Loaded</c>），第一条不会发生，只剩它挡着。</item>
    /// </list>
    /// </para>
    /// </remarks>
    private static void TwoPartGesture()
    {
        Program.Section("INV11 / 两发手势：纠正不得覆盖用户的选择");

        ControlledSelectionSim Build(bool cancelRestore, bool guardUserValue)
        {
            var sim = new ControlledSelectionSim
            {
                CancelRestoreOnRealSelect = cancelRestore,
                GuardRestoreOnUserValue = guardUserValue,
            };

            sim.Mount(0);
            sim.Load();

            // cancelFirst=true：先抛"取消选中"那一发（无实项），再抛真选中。
            // 这正是 ComboBox 一次手势的形状。
            sim.Click(2, cancelFirst: true);
            sim.Drain();
            return sim;
        }

        string Trace(ControlledSelectionSim sim) =>
            string.Join(" | ", sim.Trace);

        var ok = Build(cancelRestore: true, guardUserValue: true);
        Program.Check(
            "用户选的值进了 state",
            ok.State == 2,
            $"state={ok.State} trace={Trace(ok)}");
        Program.Check(
            "控件停在用户选的值上",
            ok.ControlIndex == 2,
            $"控件={ok.ControlIndex} trace={Trace(ok)}");
        Program.Check(
            "恰好一次回调，且回调值不是 -1",
            ok.CallbackCount == 1 && !ok.CallbackValues.Contains(-1),
            $"回调 {ok.CallbackCount} 次，值=[{string.Join(",", ok.CallbackValues)}] trace={Trace(ok)}");

        // 反向对照一：两道都关掉，用户的选择必须被盖掉——否则这条用例没摸到病。
        var broken = Build(cancelRestore: false, guardUserValue: false);
        Program.Check(
            "反向对照（两道全关）：确实发生了一次把旧值写回控件的动作",
            BrokenWroteBack(broken) && !BrokenWroteBack(ok),
            $"ok 回写={BrokenWroteBack(ok)} broken 回写={BrokenWroteBack(broken)} " +
            $"trace={Trace(broken)}");

        // 反向对照二：只留兑现前复查，也必须挡得住。
        var guardOnly = Build(cancelRestore: false, guardUserValue: true);
        Program.Check(
            "反向对照（只留兑现前复查）：仍然挡住覆盖",
            !BrokenWroteBack(guardOnly),
            $"trace={Trace(guardOnly)}");

        // 反向对照三：只留作废，同样要挡得住。两道各自都得证明自己有牙齿，
        // 否则后来者会以为"留一道就够了"，把另一道删掉。
        var cancelOnly = Build(cancelRestore: true, guardUserValue: false);
        Program.Check(
            "反向对照（只留作废）：仍然挡住覆盖",
            !BrokenWroteBack(cancelOnly),
            $"trace={Trace(cancelOnly)}");
    }

    // ── 1c. INV12：进树之前的那一次点击不得丢失 ─────────────────────
    /// <summary>
    /// INV12：<b>控件已可点、但框架还没标记为就绪</b>时，用户的那一发必须被记下，
    /// 并在 <c>Loaded</c> 时补发。
    /// </summary>
    /// <remarks>
    /// 这是 <b>2026-10 Heisenbug</b> 的回归件。真机 A/B 实测（同一份代码，
    /// 唯一变量是日志开关）：开日志时 11 次点击全部落盘，关日志一次都没落盘。
    /// 根因不在判据错，而在"未就绪就静默丢弃"这条策略本身——
    /// 而落盘（<c>ReactorLog.Persist</c> 每记一条都同步开合一次文件，且在 UI 线程上）
    /// 恰好把那个窗口拖过去，于是<b>开着日志一切正常</b>。
    /// 这类 bug 靠真机点击定位不了：仪器一装上，现象就消失。
    /// <para>
    /// 场景取自折叠的 <c>SettingsExpander</c>：受控下发发生在挂载时（控件还没进树），
    /// 用户展开后<b>立刻</b>点——此时 <c>Loaded</c> 可能还没到。
    /// </para>
    /// </remarks>
    private static void ClickBeforeLoaded()
    {
        Program.Section("INV12 / 进树之前的点击：不得被静默丢掉");

        ControlledSelectionSim Build(bool defer)
        {
            var sim = new ControlledSelectionSim { DeferNotReadyClick = defer };

            // 挂载：受控值下发时控件还没进树（折叠区的形状）。
            sim.Mount(0);
            // 内部 repeater 先进树：此刻起 WinUI 能接受选中、也会抛事件，
            // 但框架的就绪标记还没置上（真机上这两件事之间有真实的时间差）。
            sim.BeginRepeaterLoad();
            // 用户在这个窗口里点了第 2 项。
            sim.Click(2);
            // 控件自身的 Loaded 派发到框架。
            sim.CompleteLoad();
            sim.Drain();
            return sim;
        }

        string Trace(ControlledSelectionSim sim) => string.Join(" | ", sim.Trace);

        var ok = Build(defer: true);
        Program.Check(
            "用户选的值进了 state",
            ok.State == 2,
            $"state={ok.State} trace={Trace(ok)}");
        Program.Check(
            "控件停在用户选的值上",
            ok.ControlIndex == 2,
            $"控件={ok.ControlIndex} trace={Trace(ok)}");
        Program.Check(
            "恰好一次回调，且回调值就是用户点的那个",
            ok.CallbackCount == 1 && ok.CallbackValues.Contains(2),
            $"回调 {ok.CallbackCount} 次，值=[{string.Join(",", ok.CallbackValues)}] trace={Trace(ok)}");

        // 反向对照：关掉补发，这一次点击必须彻底消失——否则这条用例没摸到病。
        // 这一条是全部工作的凭据：它红了才说明"未就绪就丢弃"真的会丢点击。
        var broken = Build(defer: false);
        Program.Check(
            "反向对照（关掉补发）：这一次点击彻底丢失（无回调、state 停在旧值）",
            broken.CallbackCount == 0 && broken.State == 0,
            $"回调 {broken.CallbackCount} 次，state={broken.State} trace={Trace(broken)}");
        Program.Check(
            "反向对照：控件被受控重放拉回旧值（正是用户看到的「点了没反应」）",
            broken.ControlIndex == 0,
            $"控件={broken.ControlIndex} trace={Trace(broken)}");
    }

    /// <summary>
    /// INV13：未就绪窗口里的点击，若"取消选中"那一发<b>后到</b>（进树复查时控件停在
    /// <c>-1</c>），这一发同样不得被静默丢掉。
    /// </summary>
    /// <remarks>
    /// INV12 守的是"未就绪就静默丢弃"这条策略，但它用的点击形状是
    /// <c>cancelFirst: true</c>——取消在先、真值在后，控件最终停在用户选的值上。
    /// 而 <c>RadioButtons.cpp:407-431</c> 的两条路径<b>谁先谁后没有保证</b>
    /// （真机实测：那一发 -1 有时在真值前 3ms，有时在真值后 4ms）。一旦取消那一发
    /// <b>后到</b>，控件在进树复查的那一刻就停在 <c>-1</c>。
    /// <para>
    /// 旧判据 <c>ShouldFlushDeferred</c> 见到 <c>currentIndex &lt; 0</c> 就放弃，
    /// 理由是"控件现在什么都没选中，没有可补的发"。这个理由漏了半句：
    /// <b>可补的发在 <c>pending</c> 里，不在控件上</b>——pending 记的是用户那次点击
    /// 的真实意图，控件漂到 -1 只是 WinUI 的中间态（<c>Select(-1)</c> 的作者是
    /// 子项 Unchecked 或 <c>UpdateItemsSource</c>，不是用户）。于是这一次点击
    /// 永远出不来：state 不变、控件被受控重放拉回旧值，用户看到的就是"点了没反应"。
    /// </para>
    /// <para>
    /// 修法：<b>兑现的权威是 <c>pending</c></b>；控件此刻的值只用来判"用户有没有
    /// 改主意"（<c>currentIndex &gt;= 0 &amp;&amp; != pending</c> 才作废）。
    /// 控件漂到 -1 不构成放弃的理由。
    /// </para>
    /// <para>
    /// 与 INV12 的关系：两条守的是同一个窗口，但点击形状不同，漏掉任何一条
    /// 都会让"一半的点击顺序"静默失效。
    /// </para>
    /// </remarks>
    private static void ClickBeforeLoadedWithCancelLast()
    {
        Program.Section("INV13 / 取消那一发后到（控件停在 -1）：不得被静默丢掉");

        ControlledSelectionSim Build(bool flushWhenCleared)
        {
            var saved = SelectionGate.FlushWhenControlCleared;
            SelectionGate.FlushWhenControlCleared = flushWhenCleared;

            try
            {
                var sim = new ControlledSelectionSim();

                sim.Mount(0);
                sim.BeginRepeaterLoad();
                // 用户在这个窗口里点了第 2 项：框架还没就绪，这一发被吞、记进 Deferred。
                sim.Click(2);
                // 然后控件把选中项<b>回收</b>掉（虚拟化 / 折叠 / 切页都走这条路），
                // 真实控件会无条件 Select(-1)（cpp:304-315），于是控件漂到"无选中"。
                sim.Recycle(2);
                // 控件自身的 Loaded 派发到框架：此刻复查，控件停在 -1。
                sim.CompleteLoad();
                sim.Drain();

                return sim;
            }
            finally
            {
                SelectionGate.FlushWhenControlCleared = saved;
            }
        }

        string Trace(ControlledSelectionSim sim) => string.Join(" | ", sim.Trace);

        var ok = Build(flushWhenCleared: true);

        Program.Check(
            "用户选的值进了 state",
            ok.State == 2,
            $"state={ok.State} trace={Trace(ok)}");

        Program.Check(
            "控件最终停在用户选的值上",
            ok.ControlIndex == 2,
            $"控件={ok.ControlIndex} trace={Trace(ok)}");

        Program.Check(
            "恰好一次回调，值就是用户点的那个",
            ok.CallbackCount == 1 && ok.CallbackValues.Contains(2),
            $"回调 {ok.CallbackCount} 次，值=[{string.Join(",", ok.CallbackValues)}] trace={Trace(ok)}");

        // 反向对照：关掉开关必须复现"点了没反应"——它红了才说明这条用例真摸到了病。
        var broken = Build(flushWhenCleared: false);

        Program.Check(
            "反向对照（关掉开关）：这一次点击彻底丢失（无回调、state 停在旧值）",
            broken.CallbackCount == 0 && broken.State == 0,
            $"回调 {broken.CallbackCount} 次，state={broken.State} trace={Trace(broken)}");

        Program.Check(
            "反向对照：控件被受控重放拉回旧值（正是用户看到的「点了没反应」）",
            broken.ControlIndex == 0,
            $"控件={broken.ControlIndex} trace={Trace(broken)}");
    }

    /// <summary>
    /// INV14：未就绪窗口里的点击记下之后，控件被<b>我们自己的异步回写</b>拉回受控值，
    /// 这一发同样不得被判成"用户改主意"而作废。
    /// </summary>
    /// <remarks>
    /// 记下 <c>pending</c> 之后、<c>Loaded</c> 到来之前，中间还夹着一条异步队列：
    /// 同一手势的"取消选中"那一发会排一次 <c>SelectionRestore</c>，它把控件写回
    /// <b>受控旧值</b>。于是复查时控件停在受控目标上，而 <c>pending</c> 是用户刚点
    /// 的那个值——旧判据 <c>currentIndex != pending</c> 把它读成"用户改主意"，
    /// 那一次点击就凭空消失了。
    /// <para>
    /// 用户看到的样子与 INV12 / INV13 完全一致：<b>点了没反应</b>。而且它更隐蔽——
    /// 控件最后规规矩矩停在受控值上，state 与控件一致，界面没有任何异常。
    /// 上面两条不变量（"值有主人"、"state 与控件一致"）对它<b>全盲</b>，
    /// 只有把"用户做过什么"与"state 收到什么"对起来看才抓得到。
    /// </para>
    /// <para>
    /// 修法：<b>"用户改主意"的判据收紧一格</b>——只有控件停在"既不是 pending、
    /// 也不是受控目标"的值上才算用户改了主意。停在受控目标上是我们自己拉回去的，
    /// 用户此刻并没有伸手。
    /// </para>
    /// </remarks>
    private static void ClickBeforeLoadedWithPullBack()
    {
        Program.Section("INV14 / 被我们自己拉回受控值：不得判成「用户改主意」");

        ControlledSelectionSim Build(bool flushWhenPulledBack)
        {
            var saved = SelectionGate.FlushWhenControlPulledBack;
            SelectionGate.FlushWhenControlPulledBack = flushWhenPulledBack;

            try
            {
                var sim = new ControlledSelectionSim();

                sim.Mount(1);
                sim.BeginRepeaterLoad();
                // 用户在窗口里点了第 0 项：被吞、记进 Deferred。
                sim.Click(0);
                // 又点了一下<b>当前</b>项：只发 Unchecked → 控件被拨到 -1
                // （RadioButtons.cpp:431），于是"取消选中"排下一次异步回写。
                sim.Click(0, cancelFirst: true);
                // 回写落地：控件此刻停在 -1，回写的复查挡不住，于是被拉回受控值 1。
                // 把它放到那儿的<b>是我们，不是用户</b>。
                sim.Drain();
                // 控件自身的 Loaded 派发到框架：此刻复查，控件停在 1，pending 是 0。
                sim.CompleteLoad();
                sim.Drain();

                return sim;
            }
            finally
            {
                SelectionGate.FlushWhenControlPulledBack = saved;
            }
        }

        string Trace(ControlledSelectionSim sim) => string.Join(" | ", sim.Trace);

        var ok = Build(flushWhenPulledBack: true);

        Program.Check(
            "用户选的值进了 state",
            ok.State == 0,
            $"state={ok.State} trace={Trace(ok)}");

        Program.Check(
            "控件最终停在用户选的值上",
            ok.ControlIndex == 0,
            $"控件={ok.ControlIndex} trace={Trace(ok)}");

        Program.Check(
            "恰好一次回调，值就是用户点的那个",
            ok.CallbackCount == 1 && ok.CallbackValues.Contains(0),
            $"回调 {ok.CallbackCount} 次，值=[{string.Join(",", ok.CallbackValues)}] trace={Trace(ok)}");

        // 反向对照：关掉开关必须复现"点了没反应"——它红了才说明这条用例真摸到了病。
        var broken = Build(flushWhenPulledBack: false);

        Program.Check(
            "反向对照（关掉开关）：这一次点击彻底丢失（无回调、state 停在旧值）",
            broken.CallbackCount == 0 && broken.State == 1,
            $"回调 {broken.CallbackCount} 次，state={broken.State} trace={Trace(broken)}");

        Program.Check(
            "反向对照：控件此时规规矩矩停在受控值上（界面毫无异常，只有用户知道没生效）",
            broken.ControlIndex == 1,
            $"控件={broken.ControlIndex} trace={Trace(broken)}");
    }

    /// <summary>
    /// INV15：未就绪窗口里用户先点了 A、<b>随后又拨回受控值 B</b>，先前记下的 A
    /// 必须作废——否则进树时按过期的旧意图补发，把用户<b>最后</b>那一下整个盖掉。
    /// </summary>
    /// <remarks>
    /// 记下的那一发之所以会过期：<c>ShouldDeferNotReady</c> 只在"值 != 受控目标"
    /// 时记账，于是"用户把控件拨回受控目标"这一发<b>既不记账、也不作废旧账</b>。
    /// 旧账于是活到进树那一刻，被当成最新意图补发出去。
    /// <para>
    /// 难点在于<b>光看值分不开两种来源</b>：受控目标正好是 B 时，我们把控件写成 B
    /// 抛一发 <c>B</c>，用户把控件拨回 B 也抛一发 <c>B</c>。
    /// 所以要有"此刻是不是我们自己在写"这道旗标（<c>Applying</c>）——
    /// 它和"重建中"那道同构，只是一个替 WinUI 的 <c>UpdateItemsSource</c> 关门，
    /// 一个替我们自己的写回关门。
    /// </para>
    /// <para>
    /// 用户看到的样子很反直觉：<b>他最后点的那一项不生效，先点的那一项反而生效了。</b>
    /// 而且补发出来的值是用户<b>早就放弃</b>的那个，界面与 state 依旧自洽，
    /// 同样躲过了终态类的不变量。
    /// </para>
    /// </remarks>
    private static void ClickTwiceBeforeLoaded()
    {
        Program.Section("INV15 / 用户随后拨回受控值：先前记下的那一发必须作废");

        ControlledSelectionSim Build(bool cancelDeferred)
        {
            var sim = new ControlledSelectionSim { CancelDeferredOnPullBack = cancelDeferred };

            sim.Mount(1);
            sim.BeginRepeaterLoad();
            // 第一次点击：值不是受控目标 → 记下。
            sim.Click(0);
            // 第二次点击：把控件拨回受控目标 1。这一发自己不记账，
            // 但<b>必须</b>把上一次记下的作废——用户改主意了。
            sim.Click(1);
            sim.CompleteLoad();
            sim.Drain();
            return sim;
        }

        string Trace(ControlledSelectionSim sim) => string.Join(" | ", sim.Trace);

        var ok = Build(cancelDeferred: true);

        Program.Check(
            "用户最后停在哪，state 就是哪个（没有凭空的回调）",
            ok.State == 1 && ok.CallbackCount == 0,
            $"state={ok.State} 回调 {ok.CallbackCount} 次 trace={Trace(ok)}");

        Program.Check(
            "控件停在用户最后选的值上",
            ok.ControlIndex == 1,
            $"控件={ok.ControlIndex} trace={Trace(ok)}");

        // 反向对照：不作废就必须观察到"按过期意图补发"。
        var broken = Build(cancelDeferred: false);

        Program.Check(
            "反向对照（不作废）：进树时按过期的旧意图补发（state 被拉回用户早已放弃的值）",
            broken.CallbackCount == 1 && broken.State == 0,
            $"回调 {broken.CallbackCount} 次，值=[{string.Join(",", broken.CallbackValues)}] " +
            $"state={broken.State} trace={Trace(broken)}");
    }

    // ── 2. 闭环不变量 ──────────────────────────────────────────────
    /// <summary>
    /// INV9：异步回写兑现前必须看清"控件此刻停在谁的值上"——否则会把用户<b>此后的</b>
    /// 选择整个盖回去。
    /// </summary>
    /// <remarks>
    /// 对应的真代码是 <c>SelectionRestore.Schedule</c> 异步体里那句
    /// <c>if (current >= 0 && current != expected) return;</c>。
    /// <para>
    /// <b>2026-10：本条用例守的那道复查换人了。</b>原先守的是异步体第一行的
    /// <c>now.Index != expected</c>（"受控目标变没变"），而它在真机这条链路上
    /// <b>恒等于真、等于没检查</b>：受控目标要等下一轮渲染才更新，回写却排在渲染之前，
    /// 于是它拿到的目标永远是排队那一刻的那个。用户"点了却弹回旧值"正是从这条缝
    /// 漏出去的（真机日志 <c>受控下发 ComboBox#5: 1 → 0</c>）。
    /// 现在守的是"控件当前值"这一道，旧的那一道仍在源码里（多一道无害），
    /// 但<b>不能再把它当成这条链路的防线</b>。
    /// </para>
    /// <para>
    /// 用户能看到的样子：<b>值自己弹回来了</b>——点了第 3 项，界面闪回第 1 项。
    /// 而且只在"上一次刚好点过当前选中项"之后复现，手点很难串起来。
    /// </para>
    /// </remarks>
    private static void StaleRestoreDoesNotClobber()
    {
        ControlledSelectionSim Build(bool dropStale)
        {
            // 关键是"兑现前复查"这一道。<b>"目标变没变"那道陈旧检查在这条链路上无效</b>：
            // 受控目标要等下一轮渲染才更新，而回写排在渲染之前，
            // 它拿到的目标<b>永远是排队的那一刻</b>——比较恒等于真，等于没检查。
            // 真机上"用户点了却弹回旧值"正是从这条缝里漏出去的。
            // 作废那一道也一并关掉，否则它会替复查挡下来，反向对照就成了假的绿。
            var sim = new ControlledSelectionSim
            {
                GuardRestoreOnUserValue = dropStale,
                CancelRestoreOnRealSelect = false,
            };
            sim.Mount(0);
            sim.Load();

            // ① 点<b>当前选中项</b>：WinUI 只发 Unchecked → Select(-1) → 依赖属性写成 -1
            //    （RadioButtons.cpp:421）。闸门吞掉它，并排一次异步回写去纠正，
            //    快照的是<b>当时</b>的受控目标 0。
            sim.Click(0);

            // ② 回写还没上桌（Dispatcher.RunAsync 排在后面），用户已经点了别的一项。
            sim.Click(2);

            sim.Drain();
            return sim;
        }

        var ok = Build(dropStale: true);
        var broken = Build(dropStale: false);

        Program.Check(
            "INV9 异步回写排队后目标变了：用户的新选择不会被盖回去",
            ok.State == 2 && ok.ControlIndex == 2,
            $"state={ok.State} 控件={ok.ControlIndex} trace={string.Join(" | ", ok.Trace)}");

        // 反向对照看的是"回写有没有真的发生"，不是最终值：
        // 回写之后紧跟的一轮渲染会把控件拉回新目标，最终值看不出差别，
        // 但界面上多了一帧"值自己弹回去"的抖动——那就是用户在真机上看到的东西。
        Program.Check(
            "INV9 反向对照：去掉陈旧检查后确实多写了一次旧值（证这道复查有用）",
            BrokenWroteBack(broken) && !BrokenWroteBack(ok),
            $"ok 回写={BrokenWroteBack(ok)} broken 回写={BrokenWroteBack(broken)}，" +
            $"trace={string.Join(" | ", broken.Trace)}");
    }

    /// <summary>这次仿真里，"回写真的落地"有没有发生过（看时序里的 <c>restore →</c>）。</summary>
    private static bool BrokenWroteBack(ControlledSelectionSim sim)
    {
        foreach (var line in sim.Trace)
        {
            if (line.Contains("restore →"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// INV10：受控纠正<b>不该看有没有人监听</b>。
    /// </summary>
    /// <remarks>
    /// <c>Dispatch</c> 原来以 <c>if (callback is null) return;</c> 开头，
    /// 于是"没人听"的时候整道闸被跳过，纠正也一并落在被跳过的那一段里。
    /// 受控但没挂 <c>OnSelectedIndexChanged</c> 的控件，遇上"点当前已选中项"
    /// 就被对面的 <c>Select(-1)</c> 拨到无选中，而 state 没变 ⇒ 不重渲染 ⇒ 永久回不来。
    /// <para>
    /// 它和 INV6 是同一个吞法、同一种丢值，区别只有"元素给没给回调"——
    /// 这正是同一种病在两个页面上表现不同的来源，
    /// 也是提 bug 时最难描述的一类：<b>同一个控件，换个写法就好了。</b>
    /// </para>
    /// </remarks>
    private static void RestoreDoesNotDependOnListener()
    {
        ControlledSelectionSim Build(bool restoreWithoutListener)
        {
            var sim = new ControlledSelectionSim { RestoreWithoutListener = restoreWithoutListener };
            sim.Mount(1);
            sim.Load();
            sim.SetCallback(false);   // 受控，但这一轮没给 OnSelectedIndexChanged
            sim.Click(1);             // 点当前选中项 → 对面抛 AddedItems 无实项的那一发
            sim.Drain();
            return sim;
        }

        var ok = Build(restoreWithoutListener: true);
        var broken = Build(restoreWithoutListener: false);

        Program.Check(
            "INV10 没挂回调的受控控件：点当前选中项后仍保持选中",
            ok.ControlIndex == 1 && ok.State == 1,
            $"控件={ok.ControlIndex} state={ok.State} trace={string.Join(" | ", ok.Trace)}");

        Program.Check(
            "INV10 没挂回调的受控控件：照样不产生用户回调",
            ok.CallbackCount == 0,
            $"回调 {ok.CallbackCount} 次 trace={string.Join(" | ", ok.Trace)}");

        Program.Check(
            "INV10 反向对照：纠正若依赖监听，控件确实永久停在 -1（证用例有效）",
            broken.ControlIndex == -1 && broken.State == 1,
            $"控件={broken.ControlIndex} state={broken.State} " +
            $"trace={string.Join(" | ", broken.Trace)}");

        // 补一句不属于 bug 的对照：真用户的选中就算没人接，也不许被纠正回去。
        // 那条路的正确性是"下一次渲染收敛"，靠纠正硬拉回受控值是错的——
        // 那会让"点了有反应"变成"点了弹回来"。
        var real = new ControlledSelectionSim();
        real.Mount(0);
        real.Load();
        real.SetCallback(false);
        real.Click(2);
        real.Drain();

        Program.Check(
            "INV10-续 没人接的真选择不被纠回受控值（许可的分歧，下一轮渲染才收敛）",
            real.ControlIndex == 2 && real.State == 0 && real.UnreportedUserActions == 1,
            $"控件={real.ControlIndex} state={real.State} 未上报={real.UnreportedUserActions} " +
            $"trace={string.Join(" | ", real.Trace)}");

        real.RenderNow();

        Program.Check(
            "INV10-续 下一次渲染把它拽回来",
            real.ControlIndex == 0,
            $"控件={real.ControlIndex} trace={string.Join(" | ", real.Trace)}");
    }

    private static void Invariants()
    {
        Program.Section("受控闭环 / 不变量");

        StaleRestoreDoesNotClobber();
        RestoreDoesNotDependOnListener();


        // INV1：一次点击 → 恰好一次回调，值对，且 state 与控件收敛到同值。
        // 两种到达顺序都必须成立——源码没有保证谁先谁后。
        foreach (var cancelFirst in new[] { false, true })
        {
            var sim = new ControlledSelectionSim();
            sim.Mount(0);
            sim.Load();
            sim.Click(2, cancelFirst);
            var rounds = sim.Drain();

            Program.Check(
                $"INV1 点击第 3 项（cancelFirst={cancelFirst}）：state 到位",
                sim.State == 2,
                $"state={sim.State} trace={string.Join(" | ", sim.Trace)}");

            Program.Check(
                $"INV1 点击第 3 项（cancelFirst={cancelFirst}）：控件值到位",
                sim.ControlIndex == 2,
                $"控件={sim.ControlIndex} trace={string.Join(" | ", sim.Trace)}");

            Program.Check(
                $"INV1 点击第 3 项（cancelFirst={cancelFirst}）：恰好一次回调",
                sim.CallbackCount == 1,
                $"回调 {sim.CallbackCount} 次，值=[{string.Join(",", sim.CallbackValues)}]");

            Program.Check(
                $"INV1 点击第 3 项（cancelFirst={cancelFirst}）：回调值不含 -1",
                !sim.CallbackValues.Contains(-1),
                $"值=[{string.Join(",", sim.CallbackValues)}]");

            Program.Check(
                $"INV1 点击第 3 项（cancelFirst={cancelFirst}）：收敛（渲染轮数有界）",
                rounds < 64,
                $"渲染 {rounds} 轮");
        }

        // INV1-续：连续点同一个值两次。第二次必须照样进回调——
        // 若回声登记没被正确消费，第二次会被当成回声吞掉，
        // 表现正是"点几下之后回调不再更新"。
        var repeat = new ControlledSelectionSim();
        repeat.Mount(0);
        repeat.Load();
        repeat.Click(1);
        repeat.Drain();
        repeat.Click(2);
        repeat.Drain();
        repeat.Click(1);
        repeat.Drain();

        Program.Check(
            "INV1-续 回到先前选中过的那一项，回调照样要来",
            repeat.CallbackCount == 3 && repeat.State == 1,
            $"回调 {repeat.CallbackCount} 次，值=[{string.Join(",", repeat.CallbackValues)}]，" +
            $"state={repeat.State} trace={string.Join(" | ", repeat.Trace)}");

        // INV2：程序化改 state（受控下发）不该产生用户回调。
        var outside = new ControlledSelectionSim();
        outside.Mount(0);
        outside.Load();
        outside.SetState(2);
        outside.Drain();

        Program.Check(
            "INV2 受控下发不产生用户回调（回声全抑制）",
            outside.CallbackCount == 0,
            $"回调 {outside.CallbackCount} 次 trace={string.Join(" | ", outside.Trace)}");

        Program.Check(
            "INV2 受控下发后控件跟上",
            outside.ControlIndex == 2,
            $"控件={outside.ControlIndex} trace={string.Join(" | ", outside.Trace)}");

        // INV3：折叠区场景——挂载时还没 Loaded，期间的事件一律不得进回调，
        // 但 Loaded 之后受控值必须落到控件上。
        var folded = new ControlledSelectionSim();
        folded.Mount(1);
        folded.Click(2);              // 未 Loaded：模型里根本不抛事件
        folded.Drain();

        Program.Check(
            "INV3 未进可视树期间的点击不进回调",
            folded.CallbackCount == 0,
            $"回调 {folded.CallbackCount} 次 trace={string.Join(" | ", folded.Trace)}");

        folded.Load();
        folded.Drain();

        Program.Check(
            "INV3 Loaded 之后受控值落到控件上",
            folded.ControlIndex == 1 && folded.State == 1,
            $"控件={folded.ControlIndex} state={folded.State} " +
            $"trace={string.Join(" | ", folded.Trace)}");

        // INV4：整批换 items（源码里无条件先 Select(-1)）——
        // 中间态不许进回调，且重建结束后选中必须回到 state 上。
        var rebuilt = new ControlledSelectionSim();
        rebuilt.Mount(2);
        rebuilt.Load();
        rebuilt.Drain();
        var before = rebuilt.CallbackCount;
        rebuilt.RebuildItems(4);
        rebuilt.Drain();

        Program.Check(
            "INV4 items 重建的中间态不进回调",
            rebuilt.CallbackCount == before,
            $"重建前后回调 {before} → {rebuilt.CallbackCount} " +
            $"trace={string.Join(" | ", rebuilt.Trace)}");

        Program.Check(
            "INV4 重建结束后选中回到 state",
            rebuilt.ControlIndex == rebuilt.State,
            $"控件={rebuilt.ControlIndex} state={rebuilt.State} " +
            $"trace={string.Join(" | ", rebuilt.Trace)}");

        // INV6：本次抓到的真 bug——**点当前已选中的那一项**。
        //
        // WinUI 侧发生什么：OnChildChecked 被 Select 的 `m_selectedIndex != index`
        // 守卫整条挡掉（等于这一路没发生），紧接着 OnChildUnchecked 通过
        // （它守的是"被取消的正是当前选中项"，此时成立）→ Select(-1)。
        // 于是控件停在"无选中"，state 却还是旧值 → 界面上"点了没反应"。
        //
        // 修法：受控语义下选中由 state 说了算，单选控件不允许点掉选中，
        // 所以这一发吞掉之后要把受控值纠正回来。
        var fixed6 = new ControlledSelectionSim { WriteBackOnSuppress = true };
        fixed6.Mount(1);
        fixed6.Load();
        fixed6.Click(1);            // 点当前选中项
        fixed6.Drain();

        Program.Check(
            "INV6 点当前选中项：控件保持选中、state 不变",
            fixed6.ControlIndex == 1 && fixed6.State == 1,
            $"控件={fixed6.ControlIndex} state={fixed6.State} " +
            $"trace={string.Join(" | ", fixed6.Trace)}");

        Program.Check(
            "INV6 点当前选中项：不产生用户回调（不是一次真实选中）",
            fixed6.CallbackCount == 0,
            $"回调 {fixed6.CallbackCount} 次，值=[{string.Join(",", fixed6.CallbackValues)}] " +
            $"trace={string.Join(" | ", fixed6.Trace)}");

        // 反向对照：**必须**观察到"不修就坏"。
        // 一条修不修都绿的用例等于没写——这个项目已经在量具失真上栽过五次
        // （GetHashCode 指纹、非泛型 IEnumerable、NuGet 与 ProjectReference 混用、
        //   符号链接日志、帧日志刷屏），每次都是"看着在测，其实没测到"。
        var broken6 = new ControlledSelectionSim { WriteBackOnSuppress = false };
        broken6.Mount(1);
        broken6.Load();
        broken6.Click(1);
        broken6.Drain();

        Program.Check(
            "INV6 反向对照：未修复时控件确实停在 -1 而 state 仍有值（证用例有效）",
            broken6.ControlIndex == -1 && broken6.State == 1,
            $"控件={broken6.ControlIndex} state={broken6.State} " +
            $"—— 反向对照不成立说明这条用例没摸到 bug，trace={string.Join(" | ", broken6.Trace)}");

        // INV7：第二个 bug——**泄漏的回声登记**。
        //
        // 折叠区挂载时（还没 Loaded）框架就下发受控值，并顺手登记了回声期望；
        // 但 WinUI 此刻 m_blockSelecting 仍为 true，Select() 直接返回，
        // **事件根本不会来** → 那条期望永远等不到消费，一直挂着。
        // 之后用户点回那一项时，Consume 匹配上这条陈旧登记 → 判成回声 → 吞。
        //
        // 这就是"点了没反应"里最难手点复现的那一半：**只在某一个具体值上复现**，
        // 换个值就正常。用户报的"必须先切换一次材质其他设置才能生效"也是同一枚硬币——
        // 切材质触发的那次重渲染顺带把控件值改了，才让后续操作重新有反应。
        var leakFixed = new ControlledSelectionSim();
        leakFixed.Mount(0);      // 未 Loaded：受控写入，但 WinUI 不会抛事件
        leakFixed.Load();
        leakFixed.Click(2);
        leakFixed.Drain();
        leakFixed.Click(0);      // 点回当初挂载时下发的那个值
        leakFixed.Drain();

        Program.Check(
            "INV7 折叠区挂载后点回原值：仍要进回调、state 跟上",
            leakFixed.CallbackCount == 2 && leakFixed.State == 0 && leakFixed.ControlIndex == 0,
            $"回调 {leakFixed.CallbackCount} 次，值=[{string.Join(",", leakFixed.CallbackValues)}]，" +
            $"state={leakFixed.State} 控件={leakFixed.ControlIndex} " +
            $"trace={string.Join(" | ", leakFixed.Trace)}");

        // 反向对照：无条件登记时必须坏，且坏法必须正是"被判成回声吞掉"。
        var leakBroken = new ControlledSelectionSim
        {
            LeakEchoRegistration = true,
            SealEchoAfterWrite = false,
        };
        leakBroken.Mount(0);
        leakBroken.Load();
        leakBroken.Click(2);
        leakBroken.Drain();
        leakBroken.Click(0);
        leakBroken.Drain();

        Program.Check(
            "INV7 反向对照：无条件登记时该次点击被陈旧回声吞掉（证用例有效）",
            leakBroken.CallbackCount == 1 && leakBroken.State == 2 && leakBroken.ControlIndex == 0,
            $"回调 {leakBroken.CallbackCount} 次，值=[{string.Join(",", leakBroken.CallbackValues)}]，" +
            $"state={leakBroken.State} 控件={leakBroken.ControlIndex} " +
            $"trace={string.Join(" | ", leakBroken.Trace)}");

        // INV8：受控值越界（items 变少，state 还指着旧下标）。
        // 危险点：越界那一发事件的 AddedItems 里同样没有实项（GetDataAtIndex 取不到），
        // 会伪装成"取消选中"命中判据零——而 bug A 的修法正是"命中判据零就纠正"。
        // 若不加边界，就会变成"纠正 → 又抛一发无实项事件 → 再纠正"的自激。
        // 修法上已经加了越界不纠正的守卫，这里断言它真的没有自激。
        var oor = new ControlledSelectionSim();
        oor.Mount(2);            // 3 项里选第 3 项
        oor.Load();
        oor.RebuildItems(2);     // 砍到 2 项 → state=2 越界
        var oorRounds = oor.Drain();

        Program.Check(
            "INV8 受控值越界：确实抛出了越界事件（证该场景被覆盖）",
            oor.OutOfRangeEvents > 0,
            $"越界事件 {oor.OutOfRangeEvents} 次 —— 为 0 说明这条用例根本没跑到该分支，" +
            $"trace={string.Join(" | ", oor.Trace)}");

        Program.Check(
            "INV8 受控值越界：不自激（渲染轮数有界）",
            oorRounds < 64,
            $"渲染 {oorRounds} 轮 trace={string.Join(" | ", oor.Trace)}");

        Program.Check(
            "INV8 受控值越界：越界值不进用户回调",
            oor.CallbackCount == 0,
            $"回调 {oor.CallbackCount} 次，值=[{string.Join(",", oor.CallbackValues)}] " +
            $"trace={string.Join(" | ", oor.Trace)}");
    }

    // ── 3. 随机序列 ────────────────────────────────────────────────
    private static void Fuzz()
    {
        Program.Section($"受控闭环 / 随机序列（{Sequences} 条 × {Steps} 步）");

        var fixedFailures = RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: true,
            report: true, silent: false, out var maxRounds);

        Program.Check(
            $"随机序列 {Sequences} 条全部满足不变量（收敛 / state 与控件一致 / 回调不含 -1）",
            fixedFailures == 0,
            fixedFailures == 0
                ? null
                : $"{fixedFailures} 条失败（详见上方首个失败时序）");

        Console.WriteLine($"        最大渲染轮数：{maxRounds}");

        // 反向对照：同一批种子序列，把两处修法分别关掉，都必须有失败。
        // 关掉也全绿 = 这 2000 条序列根本没摸到那个 bug，它就是一堆空转，
        // 不能拿来当回归防线。这一步是给"测试本身"做的体检。
        var noWriteBack = RunSequences(
            writeBack: false, leakEcho: false, dropStale: true, restoreWithoutListener: true,
            report: false, silent: false, out _);

        Program.Check(
            "反向对照：关掉「吞后回写」后同一批序列必须失败（证模糊测试有牙齿）",
            noWriteBack > 0,
            noWriteBack > 0
                ? null
                : "关掉修法也全绿——这批序列没覆盖到该 bug，不能作为回归防线");

        var withLeak = RunSequences(
            writeBack: true, leakEcho: true, dropStale: true, restoreWithoutListener: true,
            report: false, silent: false, out _);

        Program.Check(
            "反向对照：恢复「无条件登记回声」后同一批序列必须失败（证模糊测试有牙齿）",
            withLeak > 0,
            withLeak > 0
                ? null
                : "关掉修法也全绿——这批序列没覆盖到该 bug，不能作为回归防线");

        Console.WriteLine(
            $"        未修复时同一批序列失败：吞后回写关掉 {noWriteBack} 条，回声泄漏 {withLeak} 条");

        // 第四条对照：INV13 那一发（未就绪期间点击、控件随后漂到 -1）。
        // 同样是给测试自身做体检：若关掉也全绿，说明这 20000 条序列<b>根本没走出
        // 那个形状</b>，那 INV13 就是本次修复的唯一防线，别误把 fuzz 当成它的回归网。
        // 读数<b>不</b>用终态失败条数，用"丢失次数"。
        // 理由见 ControlledSelectionSim.DeferredLostToClearedCount：终态判据只看序列
        // 跑完之后 state 兑没兑现最后一次点击，丢失发生在<b>中段</b>时会被后续点击
        // 掩盖，于是 20000×24 条里只抓得到 2 条——那是<b>判据的视力</b>，
        // 不是覆盖率的真相。这里数的是每一次丢失本身。
        ControlledSelectionSim.DeferredLostToClearedCount = 0;

        var noFlushWhenCleared = RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: true,
            report: false, silent: false, out _, flushWhenCleared: false);

        var lostToCleared = ControlledSelectionSim.DeferredLostToClearedCount;

        // 门槛取 10 而不是 1：1 次就放行的话，将来动作分布一漂移、形状偶然消失一次
        // 也可能蒙混过关。当前实测 60 次，余量 6 倍。
        //
        // 但要<b>如实说明</b>：60 次 / 480000 步，这个形状在随机序列里<b>仍然罕见</b>，
        // INV13 的主力防线是它那条确定性用例（<c>ClickBeforeLoadedWithCancelLast</c>），
        // 这里只保证 fuzz 不至于对它完全失明。
        Program.Check(
            "反向对照：关掉「控件漂到 -1 仍补发」后必须真的丢点击（证模糊测试对 INV13 有牙齿）",
            lostToCleared >= 10,
            lostToCleared >= 10
                ? null
                : $"关掉修法只丢了 {lostToCleared} 次 —— 这批序列基本没走出" +
                  "「未就绪点击 + 漂到 -1」那个形状，INV13 只能靠确定性用例守着");

        Console.WriteLine(
            $"        未修复时：控件漂到 -1 不补发 → 终态失败 {noFlushWhenCleared} 条，" +
            $"实际丢失 {lostToCleared} 次（终态判据只看见后者的一部分：中段丢失会被" +
            $"后续点击掩盖；INV13 的主力防线仍是确定性用例）");

        // 第五条对照：INV14 那一发——控件被<b>我们自己的回写</b>拉回受控值。
        // 这一格是旧判据 `currentIndex != pending` 唯一会误伤的地方：
        // 判成"用户改主意"之后，那一次点击消失，而控件最后规规矩矩停在受控值上。
        var noFlushWhenPulledBack = RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: true,
            report: false, silent: false, out _, flushWhenPulledBack: false);

        Program.Check(
            "反向对照：关掉「被拉回受控值仍补发」后同一批序列必须失败（证模糊测试对 INV14 有牙齿）",
            noFlushWhenPulledBack > 0,
            noFlushWhenPulledBack > 0
                ? null
                : "关掉修法也全绿——这批序列没走出「未就绪点击 + 回写拉回」那个形状，" +
                  "INV14 只能靠确定性用例守着");

        Console.WriteLine(
            $"        未修复时同一批序列失败：被拉回受控值不补发 {noFlushWhenPulledBack} 条");

        // 第六条对照：INV15——用户随后拨回受控值时，先前记下的那一发不作废。
        var noCancelDeferred = RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: true,
            report: false, silent: false, out _, cancelDeferred: false);

        Program.Check(
            "反向对照：不作废过期意图后同一批序列必须失败（证模糊测试对 INV15 有牙齿）",
            noCancelDeferred > 0,
            noCancelDeferred > 0
                ? null
                : "关掉修法也全绿——这批序列没走出「窗口内连点两次」那个形状，" +
                  "INV15 只能靠确定性用例守着");

        Console.WriteLine(
            $"        未修复时同一批序列失败：过期意图不作废 {noCancelDeferred} 条");

        // 第七条对照：认不出"这一发是我们自己写的"（真代码的 Applying 窗失效）。
        // 它是 INV15 的另一半：作废必须先分清作者，否则"我们写成受控值"那一发
        // 会被当成"用户拨回受控值"，凭空作废一次根本没过期的意图——
        // 修法反过来变成新的丢点击来源。
        var noDetect = RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: true,
            report: false, silent: false, out _, detectOwnWrites: false);

        Program.Check(
            "反向对照：认不出自己写的之后同一批序列必须失败（证 Applying 旗标有回归网）",
            noDetect > 0,
            noDetect > 0
                ? null
                : "关掉它也全绿 —— Applying 旗标目前是裸的，" +
                  "没人能证伪'没有它也一样'，将来可能被当冗余删掉");

        Console.WriteLine(
            $"        未修复时同一批序列失败：认不出自己写的 {noDetect} 条");

        // 第三条对照：去掉 SelectionRestore 里那句"目标被改过就别动手"。
        // 这道复查此前<b>没有任何测试守着</b>——仿真把回写简化成"跟渲染共用同一个 pending
        // 旗标"，执行时机永远紧跟下一次渲染，快照不可能陈旧，删掉那行也不会红。
        ControlledSelectionSim.RestoreWriteBackCount = 0;
        var stale = RunSequences(
            writeBack: true, leakEcho: false, dropStale: false, restoreWithoutListener: true,
            report: false, silent: false, out _);
        var staleWrites = ControlledSelectionSim.RestoreWriteBackCount;

        ControlledSelectionSim.RestoreWriteBackCount = 0;
        RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: true,
            report: false, silent: false, out _);
        var guardedWrites = ControlledSelectionSim.RestoreWriteBackCount;

        // 对照的读数从"违约条数"改成"回写次数"：回写之后紧跟的一轮渲染会把控件
        // 拉回最新目标，最终状态看不出差别，但每一次回写都是界面上一帧可见的抖动。
        Program.Check(
            "反向对照：去掉异步回写的陈旧检查后回写次数必须变多（证那道复查有用）",
            staleWrites > guardedWrites,
            staleWrites > guardedWrites
                ? null
                : $"去掉复查后回写 {staleWrites} 次、留着也是 {guardedWrites} 次——" +
                  "这批序列区分不出它起没起作用，这道复查等于没有回归网");

        Console.WriteLine(
            $"        回写次数：留着陈旧检查 {guardedWrites} 次，去掉 {staleWrites} 次");

        // 第四条对照：退回旧写法——纠正被"有没有人监听"牵着走。
        // 这是本轮抓到的那一半：同一个吞法、同一种值丢失，
        // 只是这一次受害者是<b>没挂 OnSelectedIndexChanged 的受控控件</b>。
        var noListenerRestore = RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: false,
            report: false, silent: false, out _);

        Program.Check(
            "反向对照：纠正若被「有没有人监听」卡住，同一批序列必须失败（证这一半有牙齿）",
            noListenerRestore > 0,
            noListenerRestore > 0
                ? null
                : "关掉它也全绿——这批序列没覆盖到该 bug，这条防线不算数");

        Console.WriteLine($"        纠正依赖监听时同一批序列失败 {noListenerRestore} 条");

        // 混合序列里这一步只红了 178 条 —— 说明"长时间无人监听"那条路径
        // 几乎没被随机走到，靠它守 INV10 等于守个空门。这里专跑一批从头到尾
        // 没挂回调的序列，让覆盖率自己说话（失败数应该在同一个量级才行）。
        var silentOk = RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: true,
            report: false, silent: true, out _);
        var silentBroken = RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: false,
            report: false, silent: true, out _);

        Program.Check(
            $"纯无回调场景 {Sequences} 条全部满足不变量（受控纠正与监听无关）",
            silentOk == 0,
            silentOk == 0 ? null : $"{silentOk} 条失败 —— 修复后的形状竟然还有漏");

        Program.Check(
            "反向对照：纯无回调场景下关掉纠正必须大面积失败（证这条线被真正覆盖）",
            silentBroken > Sequences / 10,
            silentBroken > Sequences / 10
                ? null
                : $"只红了 {silentBroken} 条 —— 这条路径基本没被走到，覆盖率不足以当防线");

        Console.WriteLine(
            $"        纯无回调场景：修复后失败 {silentOk} 条，退回旧写法失败 {silentBroken} 条");

        // ── 真机配置那一批 ──────────────────────────────────────────
        //
        // 上面<b>所有</b>批次都把 INV11 的两道防护（作废 / 兑现前复查）关着——
        // 那是给它们做反向对照用的。副作用是：<b>这 20000×24 条随机序列从来没在
        // 真机实际配置下跑过</b>。真机上两道都是开的，而"开着的组合"会不会引出
        // 别的交互，关着的批次一个字都回答不了。
        //
        // 这一批补的正是那个缺口。它要是红了，说明还有一批事故从来没被测到过。
        var liveFailures = RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: true,
            report: true, silent: false, out _,
            cancelRestore: true, guardUserValue: true);

        Program.Check(
            $"真机配置（INV11 两道防护都在）{Sequences} 条随机序列全部满足不变量",
            liveFailures == 0,
            liveFailures == 0
                ? null
                : $"{liveFailures} 条失败 —— 真机配置下还有没修到的事故（详见上方首个失败时序）");

        // 反向对照：真机配置下把 INV14 那道关掉，同样必须红。
        // 否则"真机配置"这一批对本次修复也是空转。
        var liveNoPullBack = RunSequences(
            writeBack: true, leakEcho: false, dropStale: true, restoreWithoutListener: true,
            report: false, silent: false, out _,
            flushWhenPulledBack: false, cancelRestore: true, guardUserValue: true);

        Program.Check(
            "反向对照：真机配置下关掉「被拉回受控值仍补发」也必须失败",
            liveNoPullBack > 0,
            liveNoPullBack > 0
                ? null
                : "真机配置下关掉它也全绿 —— 这批序列对 INV14 没有牙齿");

        Console.WriteLine(
            $"        真机配置：修复后失败 {liveFailures} 条，关掉 INV14 失败 {liveNoPullBack} 条");
    }

    private static void RecordFailure(
        ref int failures,
        ref string firstFailure,
        string why,
        int sequence,
        ControlledSelectionSim sim)
    {
        failures++;

        if (firstFailure.Length == 0)
        {
            firstFailure =
                $"种子序列 #{sequence}：{why}" + Environment.NewLine + "    " +
                string.Join(Environment.NewLine + "    ", sim.Trace);
        }
    }

    /// <summary>把"哪一条不变量在响"记下来——只知道"有 N 条失败"没法定位。</summary>
    private static void Tally(ref int counter, bool hit)
    {
        if (hit)
        {
            counter++;
        }
    }

    private static int RunSequences(
        bool writeBack,
        bool leakEcho,
        bool dropStale,
        bool restoreWithoutListener,
        bool report,
        bool silent,
        out int maxRounds,
        bool flushWhenCleared = true,
        bool flushWhenPulledBack = true,
        bool cancelDeferred = true,

        // 这两道默认<b>关</b>：前面所有批次都要它们关着——那正是 INV11 的反向对照
        // 形状（"取消选中"排下的纠正到底能不能挡住）。副作用是
        // <b>fuzz 长期没跑过真机实际配置</b>（真机两道都是开的），
        // 这个缺口由下面那批 live 序列补上。
        bool cancelRestore = false,
        bool guardUserValue = false,
        bool detectOwnWrites = true)
    {
        var rng = new Random(20261006);   // 固定种子：失败可复现
        var failures = 0;
        var firstFailure = string.Empty;
        maxRounds = 0;

        // 五条不变量各自的失败条数。只报总数等于只知道"生病了"不知道"哪儿疼"。
        var convergedFails = 0;
        var explainedFails = 0;
        var faithfulFails = 0;
        var negativeFails = 0;
        var honoredFails = 0;

        var savedFlush = SelectionGate.FlushWhenControlCleared;
        var savedPullBack = SelectionGate.FlushWhenControlPulledBack;
        SelectionGate.FlushWhenControlCleared = flushWhenCleared;
        SelectionGate.FlushWhenControlPulledBack = flushWhenPulledBack;

        for (var s = 0; s < Sequences; s++)
        {
            // seal 与 leakEcho 是两条独立的机制，但都作用在"登记"这一步上：
            // 复现旧行为（无条件登记）时必须把 seal 也一并关掉，
            // 否则 seal 会顺手把泄漏出来的登记擦掉，反向对照就成了假的绿。
            var sim = new ControlledSelectionSim
            {
                WriteBackOnSuppress = writeBack,
                LeakEchoRegistration = leakEcho,
                SealEchoAfterWrite = !leakEcho,
                DropStaleRestore = dropStale,
                CancelRestoreOnRealSelect = cancelRestore,
                GuardRestoreOnUserValue = guardUserValue,
                RestoreWithoutListener = restoreWithoutListener,
                CancelDeferredOnPullBack = cancelDeferred,
                DetectOwnWrites = detectOwnWrites,
            };
            var itemCount = 3;
            sim.Mount(rng.Next(itemCount));

            if (rng.Next(4) != 0)
            {
                sim.Load();
            }

            for (var step = 0; step < Steps; step++)
            {
                // 纯无回调模式：每一步都把监听摘掉。上面的混合序列里 case 5 只是偶尔
                // 摘一下，20000 条里凑得出 178 条违规——数字太小说明那条线几乎没被走到，
                // 拿它当防线等于守个空门。这里专跑一批"从头到尾没人监听"的序列，
                // 让这条不变量的覆盖率自己说话。
                if (silent)
                {
                    sim.SetCallback(false);
                }

                switch (rng.Next(8))
                {
                    case 0:
                    case 1:
                    {
                        // per-step 断言才是"点了没反应"的直接探针：
                        // 点到<b>另一项</b>且回调在位 ⇒ 必须恰好来一次回调。
                        // 点<b>当前项</b>按设计不产生回调（INV6）——单选控件不允许点掉选中。
                        // 少了这一步，"被陈旧回声吞掉一次点击"只能靠最终一致性间接发现，
                        // 而最终一致性会被"回调为空期间的正确分歧"豁免掉，牙齿就没了
                        // （本次实测：只靠它时反向对照仅 2 条，加了这步才重新咬住）。
                        var target = rng.Next(itemCount);
                        // 未进可视树时 <c>m_blockSelecting</c> 还在，点击连事件都不会来（INV3）。
                        var switches = sim.ControlIndex != target;
                        var expected = switches && sim.HasCallback && sim.IsLoaded ? 1 : 0;
                        var before = sim.CallbackValues.Count;

                        sim.Click(target, rng.Next(2) == 0);

                        var delta = sim.CallbackValues.Count - before;
                        if (delta != expected)
                        {
                            RecordFailure(
                                ref failures,
                                ref firstFailure,
                                $"第 {step} 步点击 {target} 回调了 {delta} 次" +
                                $"（换项={switches} 有回调={sim.HasCallback} 期望 {expected} 次）",
                                s, sim);
                        }

                        break;
                    }

                    case 2:
                        sim.SetState(rng.Next(itemCount));
                        break;

                    case 3:
                        itemCount = 2 + rng.Next(3);
                        sim.RebuildItems(itemCount);
                        break;

                    // 虚拟化回收（RadioButtons.cpp:304-315）：选中项被划出可视区
                    // 也会凭空抛一次 -1，和"点当前选中项"是同一发，修法必须共用。
                    case 4:
                        sim.Recycle(rng.Next(itemCount));
                        break;

                    // 回调这一轮render有没有给：没有的话 handler 的 Dispatch 直接 return，
                    // 连 EchoGuard.Consume 都不被调用——登记下来的期望就没人领了。
                    case 5:
                        sim.SetCallback(rng.Next(4) != 0);
                        break;

                    // 内部 repeater 单独进树：从这一刻起 WinUI 能接受选中、也会抛事件，
                    // 但框架的就绪标记还没置上。中间这段就是"看得见、点得到，
                    // 框架却认为没就绪"的窗口 —— INV12 / INV13 那个形状只能在它里面出现。
                    //
                    // 这一格是<b>后来补的</b>：原先随机序列只有 sim.Load()，它把两段
                    // 一次性走完，窗口从来没被打开过。于是第四条反向对照（关掉
                    // 「控件漂到 -1 仍补发」）全绿 —— 不是修法多余，是这 20000 条序列
                    // 压根走不出那个形状。那次失败是测试自己的体检报告。
                    case 6:
                        if (!sim.RepeaterLoaded)
                        {
                            sim.BeginRepeaterLoad();
                        }

                        break;

                    default:
                        // 两段连着走（不留窗口）：真机上也有这种情形 —— repeater 与
                        // 控件自身在同一帧进树，用户来不及在中间点一下。
                        if (!sim.RepeaterLoaded)
                        {
                            sim.Load();
                        }
                        else if (!sim.IsLoaded)
                        {
                            sim.CompleteLoad();
                        }

                        break;
                }

                // 随机 Drain：渲染是批处理排队的（RenderBatcher），真实时序里"这次操作
                // 有没有赶上这一帧"是随机的——一帧里可能攒着两次操作，也可能一次一帧。
                // 每步都 flush 等于假设每次操作后面必定紧跟一次渲染，那一整片交错序
                // 就永远测不到；异步回写恰好是跨帧的，它的排序问题只在随机里才会露出来。
                if (rng.Next(3) != 0)
                {
                    sim.Drain();
                }
            }

            // 收尾：真实生命周期里 repeater 进树之后，控件自身的 Loaded 终究会到
            // （页面不会永远停在"进了一半"的状态）。补齐再判不变量，
            // 否则"未就绪补发"这条链路会因为序列恰好在窗口里结束而报出假失败。
            if (!sim.RepeaterLoaded)
            {
                sim.BeginRepeaterLoad();
            }

            if (!sim.IsLoaded)
            {
                sim.CompleteLoad();
            }

            var rounds = sim.Drain();
            maxRounds = Math.Max(maxRounds, rounds);

            // 不变量一：收敛——渲染轮数必须有界。不收敛意味着事件与下发互相激发，
            // 也就是真机上看到的"一直在渲染 / 界面自己抖"。
            var converged = rounds < 64;

            // 不变量二：Drain 之后控件的值必须有<b>主人</b>——要么是受控目标，
            // 要么是用户最后一次点击的那一项。出现过第三种值（典型是 -1）说明
            // 控件的偏离没人负责，也就是"点了之后选中态凭空消失"。
            //
            // 它取代的是"序列里出过未上报操作就豁免一致性"那一刀切写法：那条判据
            // 一旦触发就把<b>后续所有</b>偏差一并赦免，包括真 bug。实测后果很直白——
            // 换成"纯无回调"序列时，放弃纠错的旧写法 20000 条里只红 21 条，
            // 防线形同虚设。
            var explained = sim.Explained;

            // 不变量二·续：<b>state 与控件最终一致</b>，只豁免"用户最后一次点击还没人被通知"。
            // 上面那条只保证控件的值有主人，管不了"回写把用户此后的选择盖回去"——
            // 那种情形恰恰落在 Explained 允许的头一种里，必须靠这一条拦。
            var faithful = sim.Faithful;

            // 不变量三：-1 绝不能进用户回调。它是中间态，进回调就把 state 打成非法值。
            var noNegative = !sim.CallbackValues.Contains(-1);

            // 不变量四：用户真的换了项、那一刻回调也在场 ⇒ 那一次点击必须进 state。
            //
            // 这一条是<b>后加的</b>，加它是因为上面三条对"点击凭空消失"这个形状是盲的：
            // 那一发丢了之后，控件会被受控下发拉回旧值 —— 于是"值有主人"（二）、
            // "state 与控件一致"（三）、"回调不含 -1"（三·续）全部成立，
            // 界面看着规规矩矩，只有用户知道他点的那一下没生效。
            // 一次丢失的点击在终态上和"用户根本没点"长得一模一样，
            // 只看最终状态永远抓不到，必须把"用户做过什么"和"state 收到什么"对起来看。
            var honored = sim.LastClickHonored;

            if (!converged || !explained || !faithful || !noNegative || !honored)
            {
                failures++;
                Tally(ref convergedFails, !converged);
                Tally(ref explainedFails, !explained);
                Tally(ref faithfulFails, !faithful);
                Tally(ref negativeFails, !noNegative);
                Tally(ref honoredFails, !honored);

                if (report && firstFailure.Length == 0)
                {
                    firstFailure =
                        $"种子序列 #{s}：收敛={converged}（{rounds} 轮）" +
                        $" 有解释={explained} state 到位={faithful}" +
                        $"（state={sim.State} 受控目标={sim.Target} " +
                        $"控件={sim.ControlIndex} 末次点击={sim.LastUserClick}）" +
                        $" 点击兑现={honored}（该进 state 的是 {sim.ExpectStateFromClick}）" +
                        $" 无负值={noNegative}（回调值=[{string.Join(",", sim.CallbackValues)}]）" +
                        Environment.NewLine + "    " + string.Join(Environment.NewLine + "    ", sim.Trace);
                }
            }
        }

        if (report)
        {
            Console.WriteLine(
                $"        失败分布：收敛 {convergedFails} / 有解释 {explainedFails} / " +
                $"state 到位 {faithfulFails} / 含负值 {negativeFails} / 点击兑现 {honoredFails}");
        }

        if (report && firstFailure.Length > 0)
        {
            Console.WriteLine("    首个失败时序：");
            Console.WriteLine("    " + firstFailure);
        }

        // 开关是全局静态：必须还原，否则会顺着调用顺序污染后面的用例。
        SelectionGate.FlushWhenControlCleared = savedFlush;
        SelectionGate.FlushWhenControlPulledBack = savedPullBack;

        return failures;
    }
}
