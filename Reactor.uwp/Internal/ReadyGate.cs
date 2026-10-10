using System;
using Windows.UI.Xaml;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 「控件就绪」闸 —— 把 WinUI 自己的 <c>m_blockSelecting</c> 语义搬到托管侧。
/// </summary>
/// <remarks>
/// <para>
/// <b>出处（WinUI 2 <c>release/2.8</c>，本地副本见 <c>tools/winui2-ref/</c>）：</b>
/// <list type="bullet">
///   <item><c>dev/RadioButtons/RadioButtons.h:91</c>　<c>bool m_blockSelecting{ true };</c></item>
///   <item><c>dev/RadioButtons/RadioButtons.cpp:358</c>　<c>if(!m_blockSelecting &amp;&amp; !m_currentlySelecting &amp;&amp; m_selectedIndex != index)</c></item>
///   <item><c>dev/RadioButtons/RadioButtons.cpp:118-138</c>　<c>OnRepeaterLoaded()</c> 里才 <c>m_blockSelecting = false;</c>，
///         紧跟一次 <c>UpdateSelectedIndex()</c>/<c>UpdateSelectedItem()</c> 做自愈。</item>
/// </list>
/// </para>
/// <para>
/// <b>这段源码说了什么。</b>RadioButtons 内部用 <c>ItemsRepeater</c> 渲染每一项。在 repeater
/// 触发 <c>Loaded</c> 之前，控件<b>拒不接受任何选中</b>——<c>Select()</c> 第一行就被
/// <c>m_blockSelecting</c> 挡回来。也就是说：<b>写给它的 SelectedIndex 会存进依赖属性，
/// 但内部选中态在这一刻还不会真的生效</b>，要等 repeater 进可视树那一刻才补。
/// </para>
/// <para>
/// <b>托管侧怎么对齐。</b>那个私有 bool 看不见，但有一个等价的可观测量：<b>控件自己的 Loaded</b>。
/// 控件 Loaded 时它的模板子树（含内部 repeater）已经就位——WinUI 自己也正是在这一刻解禁。
/// 所以：<b>Loaded 之前，控件抛出的所有选中类事件一律是内部中间态，不许放行给用户回调。</b>
/// </para>
/// <para>
/// <b>为什么这条比"过滤 -1"强。</b>以前的实现是"看见 -1 就丢"，那是在猜。
/// 源码翻出来才知道，-1 只是中间态的<b>一种</b>：
/// <list type="bullet">
///   <item><c>RadioButtons.cpp:310-317</c>　元素被 repeater 回收，且它是勾选的 → <c>Select(-1)</c></item>
///   <item><c>RadioButtons.cpp:516-518</c>　<c>UpdateItemsSource()</c> 无条件先 <c>Select(-1)</c></item>
///   <item><c>RadioButtons.cpp:421-435</c>　子项 Unchecked → <c>Select(-1)</c></item>
/// </list>
/// 反过来，真实用户操作也可能产生 -1（清空选择）。按值过滤两头都会错，
/// <b>按"控件是否已就绪"过滤才是源码给的那个判据</b>。
/// </para>
/// </remarks>
internal static class ReadyGate
{
    /// <summary>
    /// 就绪标记。<c>WeakTable</c> 而非 <c>bool</c> 字段：控件没了条目自己消失，
    /// 不需要每个调用方都记得清理。
    /// </summary>
    private static readonly WeakTable<FrameworkElement, bool> Ready = new();

    /// <summary>
    /// 手上正在等 <c>Loaded</c> 的那个委托，按控件存。
    /// </summary>
    /// <remarks>
    /// 存它是为了让 <c>Arm</c> 变得<b>幂等</b>：同一个控件被 Arm 两次时，第二次该做的是
    /// 换掉回调（<see cref="ReadyArmAction.Keep"/>），而不是再 <c>+=</c> 一个。
    /// 每次 <c>Arm</c> 生成的委托都是新的实例（闭包捕获了 <c>onReady</c>），
    /// <c>-=</c> 只减得掉其中一个——于是 <c>Loaded</c> 来时回调会<b>跑两遍</b>，
    /// <c>ReadyStats.Ready</c> 也跟着翻倍。
    /// </remarks>
    private static readonly WeakTable<FrameworkElement, RoutedEventHandler> Subs = new();

    /// <summary>最新登记的回调。<c>Arm</c> 换了回调就用<b>最新那一份</b>，不用挂订阅那次的旧份。</summary>
    private static readonly WeakTable<FrameworkElement, Action<FrameworkElement>?> OnReady = new();

    /// <summary>
    /// 登记一个控件待就绪。进过树之后回调<b>一次</b>；此刻已在树上的话，当场就回调。
    /// </summary>
    /// <remarks>
    /// <b>泛型而不是 <c>FrameworkElement</c></b>：调用方在回调里要按具体控件类型继续写
    /// 属性（<c>self.SelectedIndex = ...</c>），用基类签名就得到处强转。
    /// <para>
    /// <b>要不要订阅 <c>Loaded</c> 不再是这里说了算。</b>判据在
    /// <see cref="ReadyPolicy.Decide"/>（连同"为什么不能用自己那份标记来判"的说明），
    /// 这里只照着它给的四种动作执行。
    /// </para>
    /// <para>
    /// <b>多次 <c>Arm</c> 是合法的</b>（<c>Update</c> 路径也可能走到这里），每一次只是
    /// 把回调换成最新那份。回调<b>什么时候跑、跑几次</b>由控件自己的生命周期决定，
    /// 不由调用了几遍 <c>Arm</c> 决定。
    /// </para>
    /// </remarks>
    public static void Arm<T>(T control, Action<T>? onReady = null)
        where T : FrameworkElement
    {
        Action<FrameworkElement>? boxed = onReady is null ? null : fe => onReady((T)fe);
        OnReady.Set(control, boxed);

        var action = ReadyPolicy.Decide(
            control.IsLoaded, IsReady(control), Subs.ContainsKey(control));

        switch (action)
        {
            case ReadyArmAction.Arm:
                Subscribe(control);
                return;

            case ReadyArmAction.Keep:
                // 订阅已经在等 Loaded 了，换完回调就走。
                return;

            default:
                // IsLoaded 说它已经在树上了，而我们这份标记还是 false —— 正是
                // <see cref="ReadyPolicy"/> 里写的那种中间态。记下来是为了让它可证伪：
                // 真机上这个计数非 0，就说明那条路径确实被走到了，而不是纸面上的推断。
                if (action == ReadyArmAction.Already && !IsReady(control))
                {
                    ReadyStats.AlreadyLoaded++;
                }

                Settle(control, action);
                return;
        }
    }

    private static void Subscribe<T>(T control)
        where T : FrameworkElement
    {
        void Handler(object sender, RoutedEventArgs _)
        {
            // 摘表与解绑都用<b>订阅时那个引用</b>（<c>control</c>），不用回调给的
            // <c>sender</c>：WinRT 不保证同一原生对象每次都给同一个托管包装，
            // 用 sender 解绑会解不掉、用 sender 摘表会摘不掉（条目残留）。
            // 依据见 RadioButtonsHandler.Handlers 字段的注释。
            Subs.Remove(control);
            control.Loaded -= Handler;
            Settle(control, ReadyArmAction.Already);
        }

        Subs.Set(control, Handler);
        control.Loaded += Handler;
    }

    /// <summary>
    /// 结算：解掉可能残留的订阅、按需置就绪、跑一次回调。
    /// </summary>
    private static void Settle<T>(T control, ReadyArmAction action)
        where T : FrameworkElement
    {
        if (Subs.TryGetValue(control, out var pending) && pending is not null)
        {
            control.Loaded -= pending;
            Subs.Remove(control);
        }

        if (ReadyPolicy.MarksReady(action) && !IsReady(control))
        {
            Ready.Set(control, true);
            ReadyStats.Ready++;
        }

        OnReady.TryGetValue(control, out var callback);
        callback?.Invoke(control);
    }

    /// <summary>
    /// 开关：判就绪时要不要采信控件<b>此刻</b>的 <c>IsLoaded</c>。<b>默认开。</b>
    /// </summary>
    /// <remarks>
    /// <b>关掉它必须 fail</b>（alpha.6 的 fix discipline）：关掉就回到"只看我们自己
    /// 那份标记"的旧行为，而那份标记<b>唯一的写入者是 <c>Loaded</c> 事件的回调</b>——
    /// 事件不来，标记就永远是 false，于是"未就绪"期间的事件一发都不放行。
    /// 判据与出处见 <see cref="ReadyPolicy.IsReady"/>。
    /// </remarks>
    public static bool TrustLiveIsLoaded { get; set; } = true;

    /// <summary>控件是否已经进过可视树（等价于 WinUI 那边 <c>m_blockSelecting == false</c>）。</summary>
    public static bool IsReady(FrameworkElement control)
    {
        var marked = Ready.TryGetValue(control, out var ready) && ready;
        var live = control.IsLoaded;

        if (!marked && live && TrustLiveIsLoaded)
        {
            // 自我修复：标记没置上，但控件此刻确实在树上。
            // 说明那个 Loaded 事件不会来了（UWP/WinUI 的 Loaded/Unloaded 有乱序与
            // 不配对的已知问题，折叠区展开 / 虚拟化回收 / Frame 切页都会踩），
            // 再干等下去就是把这位控件的所有事件永久吞掉。
            // 补上标记而不是只记一笔：补了之后这一段代码不会再进（Healed 不会虚高），
            // 而计数字段单独留着，好让"真机上到底发生过几次"成为可证伪的读数。
            Ready.Set(control, true);
            ReadyStats.Healed++;
            marked = true;
        }

        return ReadyPolicy.IsReady(marked, live, TrustLiveIsLoaded);
    }

    /// <summary>
    /// 卸载时收走登记。<c>WeakTable</c> 保证不调也不泄漏，调了只是更早释放。
    /// </summary>
    public static void Disarm(FrameworkElement control)
    {
        if (Subs.TryGetValue(control, out var pending) && pending is not null)
        {
            control.Loaded -= pending;
        }

        Ready.Remove(control);
        Subs.Remove(control);
        OnReady.Remove(control);
    }
}
