namespace Reactor.Uwp.Internal;

/// <summary>
/// <see cref="ReadyGate.Arm"/> 在这一次调用上该做什么。
/// </summary>
/// <remarks>
/// 列成枚举而不是让 <see cref="ReadyPolicy.Decide"/> 回 <c>bool</c>，理由与
/// <see cref="SelectionVerdict"/> 相同：四种动作各有各的作者，出事时要能一眼看出
/// 走岔在哪一步。"到底订没订阅"这种二值答案定位不了问题。
/// </remarks>
internal enum ReadyArmAction
{
    /// <summary>
    /// 控件<b>此刻</b>就在可视树里 → 立刻判就绪并回调一次，不必也<b>不能</b>去挂
    /// <c>Loaded</c>（已经抛过了，挂上去就再也等不到）。
    /// </summary>
    Already = 0,

    /// <summary>
    /// 曾经进过树、且手上没有订阅：直接回调一次就好。
    /// 注意<b>不重复计入就绪数</b>——它不是"变就绪"，它早就就绪过了。
    /// </summary>
    ReadyOnce = 1,

    /// <summary>已经有订阅在等 <c>Loaded</c>：只把最新回调记下来，绝不重复 <c>+=</c>。</summary>
    Keep = 2,

    /// <summary>首次登记：挂上订阅，等 <c>Loaded</c>。</summary>
    Arm = 3,
}

/// <summary>
/// 「要不要订阅 <c>Loaded</c>」的判据。<b>纯函数，不依赖 WinRT</b>，所以能被
/// <c>net10.0</c> 的测试工程 Link 进去穷举。
/// </summary>
/// <remarks>
/// <para>
/// <b>它取代的是原来的自证循环。</b>旧写法用 <c>IsReady(control)</c> 判断"要不要挂
/// <c>Loaded</c>"，而 <c>IsReady</c> 读的偏偏<b>就是</b> Loaded 回调跑过之后才置上的
/// 那份标记——用"订阅的结果"反过来决定"要不要订阅"。这条链子只有一种断法：
/// 控件<b>已经 Loaded</b>（真实 <c>IsLoaded == true</c>），而我们那份标记还是 false。
/// 此时旧写法判"还没就绪，挂上去等"，于是<b>永远卡在未就绪</b>；而"未就绪"的语义是
/// "这期间的事件一发都不放行"（<see cref="SelectionVerdict.NotReady"/>），
/// 落到用户感知上就是这个控件<b>点了没反应</b>，且不报任何错。
/// </para>
/// <para>
/// <b>权威信号是 XAML 自己的 <c>FrameworkElement.IsLoaded</c></b>，不是我们抄的那份副本。
/// 这不是猜出来的 API：同仓库的 <c>InputApplier.ApplyFocus</c> 已经在用它判断
/// "此刻能不能 <c>Focus</c>"——同一个问题（控件进树了吗）在这里已经有答复了，
/// 两处各答一份就会各答一半。
/// </para>
/// <para>
/// <b>关于 <c>subscribed</c> 与 <c>alreadyReady</c> 的先后：</b>这里先判订阅，但它俩
/// <b>实际上不会同时为真</b>——就绪结算会一并解掉订阅，所以"已就绪"的控件手上
/// 必然没有订阅。拿这一条做过变异测试：把两个分支调换，<b>全部 246 项仍然通过</b>。也就是说这个顺序<b>没有不变式守护</b>，
/// 注释里不要给它编一个"不这样就会出问题"的理由。留着它只因为它更好读
/// （先看"有没有在等"，再看"等到了没有"），不是因为它挡住了某个坏法。
/// </para>
/// <para>
/// <b>可达性（写清楚，别让人高估这个修复）：</b>今天 <c>Arm</c> 只在两个 <c>Mount</c> 里
/// 被调用，而 <c>Reconciler.BuildChildren</c>（<c>Reconciler.cs:203-208</c>）是先
/// <c>Build</c> 后 <c>Children.Add</c>，即 <c>Arm</c> 一律先于入树，那条致命路径当前
/// <b>不可达</b>。但 <c>Arm</c> 自己的注释写着"例如 Update 路径新建的控件恰好已在树里"，
/// 也就是它<b>预期</b>会被从 Update 调用。所以这是加固而非修一个今天正在复现的故障。
/// 加固的价值在于：把"会不会卡死"从<b>调用顺序的巧合</b>换成<b>控件自己的状态</b>，
/// 并补一条人能看见的读数（<c>ReadyStats.AlreadyLoaded</c>），
/// 让"这条路径在真机上到底有没有走到"变成可证伪的事——而不是埋在纸面上的争论。
/// </para>
/// </remarks>
internal static class ReadyPolicy
{
    /// <summary>
    /// 判定这次 <c>Arm</c> 该做哪一个动作。
    /// </summary>
    /// <param name="isLoaded">控件此刻是否已在可视树（<c>FrameworkElement.IsLoaded</c>）。</param>
    /// <param name="alreadyReady">我们自己那份标记是不是已经置上了。</param>
    /// <param name="subscribed">手上是不是已经有订阅在等 <c>Loaded</c>。</param>
    public static ReadyArmAction Decide(bool isLoaded, bool alreadyReady, bool subscribed) =>
        isLoaded ? ReadyArmAction.Already
            : subscribed ? ReadyArmAction.Keep
            : alreadyReady ? ReadyArmAction.ReadyOnce
            : ReadyArmAction.Arm;

    /// <summary>这次调用要不要<b>挂订阅</b>。四种动作里只有一种是。</summary>
    public static bool ShouldSubscribe(ReadyArmAction action) => action == ReadyArmAction.Arm;

    /// <summary>这次调用要不要<b>现在就回调</b>（不必再等 Loaded）。</summary>
    public static bool ShouldInvokeNow(ReadyArmAction action) =>
        action is ReadyArmAction.Already or ReadyArmAction.ReadyOnce;

    /// <summary>
    /// 这次调用会不会让控件<b>进入</b>就绪（此前尚未就绪，此刻起就绪）。
    /// </summary>
    /// <remarks>
    /// <c>ReadyOnce</c> 刻意不算：它<b>早就</b>就绪过了，再计一次会让
    /// <c>ReadyStats.Ready</c> 随 <c>Arm</c> 次数虚高，把"有多少控件真的进过树"
    /// 变成"被 Arm 了多少次"——诊断读数一偏，人就被往错的方向带。
    /// </remarks>
    public static bool MarksReady(ReadyArmAction action) => action == ReadyArmAction.Already;
}
