namespace Reactor.Uwp.Internal;

/// <summary>
/// 就绪闸的计数。与 <see cref="EchoStats"/> 同列：一个答"控件准备好了吗"，
/// 一个答"这是不是我自己写的回声"，两条判据互相独立，别合并。
/// </summary>
/// <remarks>
/// <b>为什么单独一个文件。</b>它不碰 XAML，而 <see cref="ReadyGate"/> 依赖
/// <c>Windows.UI.Xaml</c>（<c>FrameworkElement</c> / <c>RoutedEventArgs</c>）。
/// 分开之后这份计数能被 <c>net10.0</c> 的测试工程 Link 进去：诊断页那块
/// 「计数增量」显示屏的格式哨兵要用<b>真的 <c>Snapshot()</c></b> 造样本，
/// 而不是照抄字面量——合在一起就办不到（"还得利这种 Link 能不能编"的地雷也顺便埋下来了）。
/// </remarks>
internal static class ReadyStats
{
    /// <summary>进入就绪状态的控件数。</summary>
    public static long Ready;

    /// <summary>未就绪期间被吞掉的事件数（这些全是 WinUI 内部中间态）。</summary>
    public static long Suppressed;

    /// <summary>
    /// <c>Arm</c> 时发现"控件<b>其实已经在树上</b>"的次数。
    /// </summary>
    /// <remarks>
    /// 它是一条<b>可证伪</b>的读数：非 0 就说明 <see cref="ReadyPolicy"/> 描述的那种
    /// 中间态在真机上真的发生了（若沿用旧写法，这些控件会永久停在未就绪，
    /// 其间的事件一发都不放行）。单独计数而不是并入 <see cref="Ready"/>，是为了让
    /// "有多少控件正常走完 Loaded" 和 "有多少是被探针救回来的" 两件事<b>分开看</b>——
    /// 合起来就再也发现不了这条路径。
    /// </remarks>
    public static long AlreadyLoaded;

    /// <summary>
    /// 我们自己那份标记还是 <c>false</c>、但控件此刻 <c>IsLoaded</c> 已经是
    /// <c>true</c> 的次数（即"错过 <c>Loaded</c> 事件、被即时查问救回来"的控件数）。
    /// </summary>
    /// <remarks>
    /// 它和 <see cref="AlreadyLoaded"/> 是<b>两回事</b>，别合并：
    /// <c>AlreadyLoaded</c> 记的是 <c>Arm</c> 那一刻撞见的中间态，
    /// 这一条记的是 <c>Arm</c> <b>之后</b> 才被发现的——也就是"订阅已经挂上去、
    /// 而 <c>Loaded</c> 永远不会来"的那一种（UWP/WinUI 的 <c>Loaded</c>/
    /// <c>Unloaded</c> 有乱序与不配对的已知问题，见
    /// <see cref="ReadyPolicy.IsReady"/> 的注释）。
    /// <para>
    /// 非 0 就说明这条失效模式在真机上真的发生过；沿用旧写法的话，这些控件的
    /// 事件会<b>一发都不放行</b>，且不报任何错。
    /// </para>
    /// </remarks>
    public static long Healed;

    public static void Reset()
    {
        Ready = 0;
        Suppressed = 0;
        AlreadyLoaded = 0;
        Healed = 0;
    }

    public static string Snapshot() =>
        $"ready={Ready} suppressed={Suppressed} alreadyLoaded={AlreadyLoaded} healed={Healed}";
}
