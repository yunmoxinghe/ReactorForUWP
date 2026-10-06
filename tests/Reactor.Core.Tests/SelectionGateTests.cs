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
        Invariants();
        Fuzz();
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

    // ── 2. 闭环不变量 ──────────────────────────────────────────────
    /// <summary>
    /// INV9：异步回写执行时若目标已经变过，必须收手——否则会把用户<b>此后的</b>选择整个盖回去。
    /// </summary>
    /// <remarks>
    /// 对应的真代码是 <c>SelectionRestore.Schedule</c> 异步体第一行
    /// <c>now.Index != expected</c>。这道复查此前没有任何测试守着：
    /// 仿真当年把回写简化成"和渲染共用同一个 pending 旗标"，执行时机永远紧跟下一次渲染，
    /// 快照不可能陈旧——删掉那行一行代码也不会有一条测试变红。
    /// <para>
    /// 用户能看到的样子：<b>值自己弹回来了</b>——点了第 3 项，界面闪回第 1 项。
    /// 而且只在"上一次刚好点过当前选中项"之后复现，手点很难串起来。
    /// </para>
    /// </remarks>
    private static void StaleRestoreDoesNotClobber()
    {
        ControlledSelectionSim Build(bool dropStale)
        {
            var sim = new ControlledSelectionSim { DropStaleRestore = dropStale };
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

        Program.Check(
            "INV9 反向对照：去掉陈旧检查后新选择确实被盖回去（证这道复查有用）",
            broken.ControlIndex != 2,
            "去掉复查也沒回退 —— 这个最小场景没摸到它，反向对照不成立，trace=" +
            string.Join(" | ", broken.Trace));
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

        // 第三条对照：去掉 SelectionRestore 里那句"目标被改过就别动手"。
        // 这道复查此前<b>没有任何测试守着</b>——仿真把回写简化成"跟渲染共用同一个 pending
        // 旗标"，执行时机永远紧跟下一次渲染，快照不可能陈旧，删掉那行也不会红。
        var stale = RunSequences(
            writeBack: true, leakEcho: false, dropStale: false, restoreWithoutListener: true,
            report: false, silent: false, out _);

        Program.Check(
            "反向对照：去掉异步回写的陈旧检查后同一批序列必须失败（证那道复查有用）",
            stale > 0,
            stale > 0
                ? null
                : "去掉复查也全绿——这批序列区分不出它起没起作用，这道复查等于没有回归网");

        Console.WriteLine($"        去掉陈旧检查时同一批序列失败 {stale} 条");

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

    private static int RunSequences(
        bool writeBack,
        bool leakEcho,
        bool dropStale,
        bool restoreWithoutListener,
        bool report,
        bool silent,
        out int maxRounds)
    {
        var rng = new Random(20261006);   // 固定种子：失败可复现
        var failures = 0;
        var firstFailure = string.Empty;
        maxRounds = 0;

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
                RestoreWithoutListener = restoreWithoutListener,
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

                switch (rng.Next(7))
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

                    default:
                        if (!sim.IsLoaded)
                        {
                            sim.Load();
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

            if (!converged || !explained || !faithful || !noNegative)
            {
                failures++;

                if (report && firstFailure.Length == 0)
                {
                    firstFailure =
                        $"种子序列 #{s}：收敛={converged}（{rounds} 轮）" +
                        $" 有解释={explained} state 到位={faithful}" +
                        $"（state={sim.State} 受控目标={sim.Target} " +
                        $"控件={sim.ControlIndex} 末次点击={sim.LastUserClick}）" +
                        $" 无负值={noNegative}（回调值=[{string.Join(",", sim.CallbackValues)}]）" +
                        Environment.NewLine + "    " + string.Join(Environment.NewLine + "    ", sim.Trace);
                }
            }
        }

        if (report && firstFailure.Length > 0)
        {
            Console.WriteLine("    首个失败时序：");
            Console.WriteLine("    " + firstFailure);
        }

        return failures;
    }
}
