namespace Reactor.Core.Tests;

/// <summary>
/// 挂载顺序的行为级取证：<c>x:Uid</c> 那笔写在订阅之后，不该被当成用户输入。
/// </summary>
internal static class MountOrderTests
{
    private const string Initial = "代码里的初始值";
    private const string Resw = "来自 resw 的初始文案";

    public static void Run()
    {
        Program.Section("挂载顺序：ApplyUid 那笔写不该算用户输入");

        MountDoesNotCallback();
        UserTypingStillCallsBack();
        OffSwitchMustBreak();
        OrderIsTheCause();
        BoundaryEventAfterLoaded();
    }

    private static void MountDoesNotCallback()
    {
        var sim = new MountOrderSim();
        sim.Build(Initial, Resw, v => sim.State = v);
        sim.Load();

        Program.Check(
            "挂载期那笔 resw 写入不回调（也不改 state）",
            sim.Spurious == 0 && !sim.StateClobbered(Initial),
            $"Spurious={sim.Spurious}，state={sim.State} —— 初始值被 resw 顶掉了");
    }

    private static void UserTypingStillCallsBack()
    {
        var sim = new MountOrderSim();
        sim.Build(Initial, Resw, v => sim.State = v);
        sim.Load();
        sim.UserTypes("我敲的");

        Program.Check(
            "用户真的敲了字，回调照旧（修法没把正常输入一起吞掉）",
            sim.UserCalls == 1 && sim.State == "我敲的",
            $"UserCalls={sim.UserCalls}，state={sim.State}");
    }

    /// <summary>反向对照：关掉修法，同一条序列必须出现假回调。</summary>
    private static void OffSwitchMustBreak()
    {
        var sim = new MountOrderSim { SuppressMount = false };
        sim.Build(Initial, Resw, v => sim.State = v);
        sim.Load();

        Program.Check(
            "关掉「挂载期不承认」→ 必须出现假回调，且 state 被凭空改写",
            sim.Spurious == 1 && sim.StateClobbered(Initial),
            $"Spurious={sim.Spurious}，state={sim.State} —— 关掉修法却没坏，说明这条用例没在看");
    }

    /// <summary>
    /// 反事实：把那笔写挪到订阅之前，即便不修也不该有假回调。
    /// 这证明<b>病因是顺序</b>，而不是"resw 不该写 Text"。
    /// </summary>
    private static void OrderIsTheCause()
    {
        var sim = new MountOrderSim
        {
            SuppressMount = false,
            ApplyUidBeforeSubscribe = true,
        };

        sim.Build(Initial, Resw, v => sim.State = v);
        sim.Load();

        Program.Check(
            "反事实：把那笔写挪到订阅之前，不修也没假回调（病因是顺序）",
            sim.Spurious == 0 && !sim.StateClobbered(Initial),
            $"Spurious={sim.Spurious} —— 挪了顺序还有假回调，说明病因判断错了");
    }

    /// <summary>
    /// 已知边界：事件若延后到 <c>Loaded</c> 之后才抛，两条判据都假，拦不住。
    /// 把它钉成一条用例而不是藏起来 —— 边界被写成"应该没问题"才是真正的隐患。
    /// </summary>
    private static void BoundaryEventAfterLoaded()
    {
        var sim = new MountOrderSim { EventAfterLoaded = true };
        sim.Build(Initial, Resw, v => sim.State = v);
        sim.Load();

        // 注意它冒出来的<b>形状</b>：不是记成"挂载期冒出去"，而是被当成一次用户输入
        // （UserCalls=1）。两条判据在那一刻都为假，它与真实键入无从区分——
        // 这就是这条边界的全部代价，如实写死，别让后来人以为"反正拦得住"。
        Program.Check(
            "已知边界：事件延后到 Loaded 之后才抛 → 拦不住，那一发被当成用户输入",
            sim.UserCalls == 1 && sim.StateClobbered(Initial),
            $"UserCalls={sim.UserCalls}，state={sim.State} —— 若这里是 0，说明模型没真的延后那一发");
    }
}
