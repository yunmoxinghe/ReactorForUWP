namespace Reactor.Uwp.Internal;

/// <summary>
/// 受控选中类控件（<c>RadioButtons</c> / <c>ComboBox</c>）的事件闸门判定结果。
/// </summary>
/// <remarks>
/// 之所以要把"判定结果"单独列成一个枚举，而不是让 <see cref="SelectionGate.Decide"/>
/// 直接返回 <c>bool</c>：被吞掉的那一发<b>也得知道是被哪一道吞的</b>。
/// 只回 true/false 的话，"该放的没放"这种事故是无法定位的——
/// 四道闸各有各的作者，修法完全不同。
/// </remarks>
internal enum SelectionVerdict
{
    /// <summary>放行给用户回调。</summary>
    Pass = 0,

    /// <summary>
    /// 判据零：事件参数里没有真东西（<c>AddedItems</c> 全为 null）→ 这是"取消选中"。
    /// 作者是 <c>RadioButtons.cpp:431</c> 的 <c>OnChildUnchecked</c>，不是用户选中了某项。
    /// </summary>
    CancelTransient = 1,

    /// <summary>
    /// 判据一：控件还没进过可视树 → 对面 <c>m_blockSelecting</c> 仍是 true
    /// （<c>RadioButtons.h:91</c>），此刻的事件全是 WinUI 内部中间态。
    /// </summary>
    NotReady = 2,

    /// <summary>
    /// 判据二：items 正在整批替换。这一发的作者是
    /// <c>RadioButtons.cpp:518</c> 的 <c>UpdateItemsSource</c>，不是用户。
    /// </summary>
    Rebuilding = 3,

    /// <summary>判据三：值等于我们刚写进去的那个，作者是本次受控下发。</summary>
    Echo = 4,
}

/// <summary>
/// 事件闸门的前三道判据。<b>纯函数、不依赖 WinRT</b>，因此能以 Link 方式编进
/// <c>net10.0</c> 的测试工程，在没有 UWP 运行时、没有 UI 线程的环境里被穷举断言。
/// </summary>
/// <remarks>
/// <b>为什么要把判据从 handler 里剥出来。</b>这三道判据是"点了没反应"类事故的全部
/// 归因所在，而它们过去只存在于 <c>Handlers.Controls.cs</c> 的方法体里——
/// 那份文件依赖 <c>Microsoft.UI.Xaml.Controls</c>，测试工程（<c>net10.0</c>）
/// 根本编不进去，于是判据只能靠真机点击去验证，一次点击只能覆盖一种到达顺序。
/// 剥出来之后，判据本身可以被穷举 + 被随机序列驱动，bug 由测试自己找出来。
/// <para>
/// <b>顺序即因果链，不能调。</b>判据零必须在最前：它与判据一二三不是并列关系，
/// 而是"这一发到底是不是一次选中"的定性。取消与选中两发的<b>到达顺序没有保证</b>
/// （实测 <c>-1</c> 有时在真值前 3ms、有时在后 4ms），值与就绪状态都区分不开它们，
/// 只有事件参数能——所以参数判据必须先于一切。
/// </para>
/// <para>
/// <b>回声判据（判据三）刻意不在这里。</b><see cref="EchoGuard.Consume"/> 有副作用
/// （消费登记），放进纯函数里会让"短路"语义变得不可见：前三道任意一道命中时
/// <b>都不该调用它</b>，否则会把登记白白吃掉，下一发真实输入就被误判成回声。
/// 调用方按 <c>Decide → 若 Pass 再 Consume</c> 的顺序写，语义一眼可见。
/// </para>
/// </remarks>
internal static class SelectionGate
{
    /// <summary>
    /// 按因果顺序判定一发选中事件该不该放行给用户回调。
    /// </summary>
    /// <param name="hasRealItem"><c>AddedItems</c> 里是否存在非 null 的项。</param>
    /// <param name="isReady">控件是否已经进过可视树（<see cref="ReadyGate"/>）。</param>
    /// <param name="isRebuilding">items 是否正在整批替换。</param>
    public static SelectionVerdict Decide(bool hasRealItem, bool isReady, bool isRebuilding)
    {
        if (!hasRealItem)
        {
            return SelectionVerdict.CancelTransient;
        }

        if (!isReady)
        {
            return SelectionVerdict.NotReady;
        }

        if (isRebuilding)
        {
            return SelectionVerdict.Rebuilding;
        }

        return SelectionVerdict.Pass;
    }

    /// <summary>该判定是否要吞掉这一发事件。</summary>
    public static bool Suppress(SelectionVerdict verdict) => verdict != SelectionVerdict.Pass;

    /// <summary>
    /// 这次受控写入<b>要不要登记回声期望</b>。
    /// </summary>
    /// <param name="writingIndex">即将写入的下标（<c>&lt; 0</c> 表示清空选中）。</param>
    /// <param name="isReady">控件是否已进过可视树。</param>
    /// <param name="isRebuilding">items 是否正在整批替换。</param>
    /// <remarks>
    /// <b>它防的是"回声登记泄漏"。</b>回声那道是四道闸里的<b>最后一道</b>——
    /// <c>Consume</c> 只有在前三道全放行时才被调用。于是：
    /// <list type="bullet">
    ///   <item>未就绪时写入 → 对面 <c>m_blockSelecting</c> 挡住 <c>Select</c>（
    ///         <c>RadioButtons.cpp:358</c>），<b>事件根本不来</b>，登记永远等不到消费；</item>
    ///   <item>重建中写入 → 事件被"重建中"那道吞掉，<c>Consume</c> 不会被调用；</item>
    ///   <item>写入 <c>-1</c>（清空）→ 事件 <c>AddedItems</c> 里没有实项，被判据零拦掉。</item>
    /// </list>
    /// 这三种情况下登记都等于<b>埋一颗永不消费的雷</b>：等到用户真的点了同一个值，
    /// <c>Consume</c> 匹配上这条陈旧登记，把一次真实用户操作判成回声吞掉。
    /// 表现是"点了没反应"，且<b>只在某一个具体值上复现</b>——换个值就正常，
    /// 靠手点几乎不可能定位（真机上它就是"必须先切换一次材质其他设置才生效"）。
    /// <para>
    /// <b>刻意复用 <see cref="Decide"/> 而不是另写一个条件</b>：这样"回声道何时可达"
    /// 只有一个事实来源。将来若调整闸门顺序或增删判据，这里自动跟着变，
    /// 不会出现"闸门改了、登记条件没改"的错位。
    /// </para>
    /// </remarks>
    public static bool ShouldExpectEcho(int writingIndex, bool isReady, bool isRebuilding) =>
        writingIndex >= 0 && Decide(hasRealItem: true, isReady, isRebuilding) == SelectionVerdict.Pass;

    /// <summary>
    /// 被吞掉的这一发，要不要<b>排队纠正回受控值</b>。
    /// </summary>
    /// <remarks>
    /// <b>签名里刻意没有 <c>hasCallback</c>。</b>这是它与本类其余判据最大的区别，
    /// 也是它单独存在的原因：纠正兑现的是<b>"这个属性由 state 说了算"这个承诺</b>，
    /// 而这份承诺是控件自己领下的，跟有没有人挂 <c>OnSelectedIndexChanged</c>
    /// <b>无关</b>。把它写成 <c>(verdict, hasCallback)</c> 就是在诱导后来者把两件事捆起来。
    /// <para>
    /// 曾经的流程是反的：<c>Dispatch</c> 首句 <c>if (callback is null) return;</c>
    /// 一竿子打下去，连同后面的纠正一起跳过。于是<b>受控但没挂回调</b>的控件遇上
    /// "点当前已选中项"：对面 <c>OnChildUnchecked</c>(<c>cpp:431</c>) 把控件拨到 -1，
    /// 事件无人接，state 又没变 ⇒ 不重渲染 ⇒ <b>永久停在"什么都没选中"</b>，
    /// 而且再也点不回来（受控值仍是旧值，界面的 visible state 与之不一致）。
    /// 挂了回调的同一个控件却表现正常——差别只有"有没有人监听"，
    /// 这正是"同一种病在两个页面上表现不同"的来源。
    /// </para>
    /// <para>
    /// <b>只对取消选中纠正，不对其余三道：</b>
    /// 未就绪 / 重建中 —— 控件此刻的值就是它真实的值，纠正等于替用户做了决定；
    /// 回声 —— 值本来就是对的，纠正会自激（写 → 事件 → 再纠正 → 再写）。
    /// </para>
    /// </remarks>
    public static bool ShouldRestoreAfterSuppress(SelectionVerdict verdict) =>
        verdict == SelectionVerdict.CancelTransient;

    /// <summary>
    /// 把判定结果翻成人能读的原因，供日志用。
    /// </summary>
    /// <remarks>
    /// 放在这里而不是各 handler 里各写一份：四个受控控件共用同一套判据，
    /// 原因文本分家写就会各写各的措辞，排查时"同一个病有两个名字"。
    /// </remarks>
    public static string Reason(SelectionVerdict verdict) => verdict switch
    {
        SelectionVerdict.CancelTransient => "取消选中（AddedItems 无实项）",
        SelectionVerdict.NotReady => "未就绪",
        SelectionVerdict.Rebuilding => "items 重建中",
        SelectionVerdict.Echo => "回声",
        _ => "?",
    };
}
