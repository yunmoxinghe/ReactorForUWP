using Reactor.Template.Services;
using Reactor.Uwp.Hosting;
using Windows.ApplicationModel.Activation;

namespace Reactor.Template;

// 入口：没有 App.xaml，手写 Main；manifest 的 EntryPoint 指向这个类。
// WinUI 2 资源加载、窗口挂载、重渲染调度都由 ReactorApplication<TRoot> 完成。
// 标题栏延伸（ExtendViewIntoTitleBar）与按钮配色也是宿主做的，不用自己写。
//
// 换自己的根组件：把 MainPage 改成你的组件类型即可。
// 主题 / 背景材质在 MainPage（.Backdrop）和 Services/AppSettings.cs 里，
// 都是"改一处就生效"的写法，不用在这里初始化。
public sealed partial class App : ReactorApplication<MainPage>
{
    public static void Main(string[] args) =>
        Windows.UI.Xaml.Application.Start(_ => new App());

    /// <summary>
    /// 唯一必须抢在 <c>base.OnLaunched</c> 前面做的事：把持久化的"控件声音"
    /// 落到 XAML 的元素音效开关上。
    /// </summary>
    /// <remarks>
    /// <c>base.OnLaunched</c> 里会 <c>new ReactorHost(...)</c> 把整棵界面树造出来，
    /// 那之后再设 <c>ElementSoundPlayer.State</c> 就不响了——控件模板已经按旧值
    /// 把 <c>ElementSoundMode</c> 定死。原因和类型位置都写在
    /// <see cref="ElementSound"/> 的注释里。
    /// <para>
    /// 参考模板的对应位置是 <c>App.OnLaunched</c> 里的
    /// <c>AppThemeManager.LoadSettings()</c>——在 <c>rootFrame.Navigate</c> 之前。
    /// </para>
    /// </remarks>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ElementSound.Apply(AppSettings.Current.Sound);

        // ── 原子探针在 MainPage 挂载后才挂（见 MainPage 里的那处 UseEffect）──
        // 这里不挂是因为 OnLaunched 阶段拿不到派发器，挂了等于没挂。
        // 它认 LocalState\atom-off.txt，与下面 log-off 那条 A/B 各走各的开关。

        // ── A/B 开关：LocalState\log-off.txt 存在就全关日志 ──────────────
        //
        // 用来验证一个具体假设：日志的落盘本身在改变时序，于是"有日志就正常、
        // 没日志就点不动"——两个现象同源，不是玄学。
        //
        // 机制是确凿的，不是推测：ReactorLog.Persist 每次记一条 Info 都要
        // new FileStream(Append) + StreamWriter 同步写一次 reactor-startup.log，
        // 而且是**在 UI 线程上**。外加探针每 400ms 一次心跳转储。这些都是
        // 毫秒级的同步等待，正好落在"控件已可见但还没 Loaded"那个窗口上：
        // 窗口靠布局与展开动画的进度来关，被日志拖慢之后，用户点下去时
        // 窗口多半已经关了 —— 于是有日志时怎么点都成。
        //
        // 判据：关掉日志后如果重现"点了没反应"，假设成立；
        // 如果照样能点，就得回到"未就绪窗口"之外去找原因。
        // ── 闸门留痕：只给 Input 通道开 Trace ──────────────────────────
        //
        // 通道级门槛<b>优先于</b>全局（<c>Effective</c> 先看 ChannelLevels 再看
        // 全局值），所以下面那条 log-off 把全局设成 Off 也挡不住它 —— 那条 A/B
        // 要验的东西因此保持单变量，不会因为"开了日志"而变成双变量。
        //
        // 它也不引入新的时序干扰：Write 只在 Info 及以上才 Persist，而 Gate 是
        // Trace 级，<b>只进内存 Ring</b>，不会新增"每条日志同步 Append 一次文件"
        // 那个已知干扰源。留痕由 Heartbeat 周期读 Ring 转出，同样只在变化时写盘。
        ReactorLog.SetChannel(ReactorLogChannel.Input, ReactorLogLevel.Trace);

        var quiet = System.IO.File.Exists(System.IO.Path.Combine(
            Windows.Storage.ApplicationData.Current.LocalFolder.Path, "log-off.txt"));

        if (quiet)
        {
            // Off 让 IsEnabled 对一切级别都返回 false：不进 Ring、不落盘。
            ReactorLog.Level = ReactorLogLevel.Off;
        }

        base.OnLaunched(args);
    }
}
