namespace Reactor.Uwp.Internal;

/// <summary>
/// 受控下标的<b>写回策略</b>：这个值该不该真的写进控件。
/// </summary>
/// <remarks>
/// 之所以单独抽出这一份，是因为它<b>不是任何一个控件的性质，而是 <c>Selector</c>
/// 的性质</b>——<c>ComboBox</c> / <c>ListView</c> / <c>GridView</c> 共用的是 XAML
/// 内核里同一个基类。放在各个 handler 里各写一份条件，等于把"什么情况下写会被拒"
/// 抄成 N 份，将来改一处漏一处。
/// <para>
/// 源码（<c>microsoft-ui-xaml</c> <c>main</c> 分支，即 WinUI 2 / 3 共用的 DXAML 内核）：
/// <list type="bullet">
///   <item><c>dxaml/xcp/dxaml/lib/Selector_Partial.cpp:144-148</c>：
///         <c>SelectedIndex</c> 依赖属性变更 → <c>OnSelectedIndexChanged</c>。</item>
///   <item>同文件 <c>Selector::OnSelectedIndexChanged</c>：
///         <c>if (newValue &gt;= -1 &amp;&amp; newValue &lt; (INT) nCount)</c> 才真正选中，
///         否则 <c>undoChange = TRUE; outOfRange = TRUE;</c>，把值改回旧值；
///         <b>且当 <c>nCount != 0</c> 时以 <c>E_INVALIDARG</c> 收尾</b>
///         （注释原文：<c>Only throw if the value was actually out of range and there
///         are already existing items.</c>）。nCount 为 0 时改存
///         <c>m_selectedIndexValueSetBeforeItemsAvailable</c>，等 items 到位再补。</item>
///   <item>同文件 <c>Selector::EndChange</c>：选中真的变了就在
///         <c>didChange</c> 分支里<b>同步</b>调 <c>InvokeSelectionChanged</c>，
///         后者 → <c>OnSelectionChanged(args)</c> → <c>RaiseSelectionChanged</c>
///         → <c>pEventSource-&gt;Raise(...)</c>。即<b>受控写回会同步抛事件</b>。</item>
/// </list>
/// </para>
/// <para>
/// <b>把这两条条件合成一个布尔值，是为了让答案只有一个出处。</b>
/// 于是是否登记回声（<see cref="SelectionGate.ShouldExpectEcho"/>）、是否需要纠正
/// （<see cref="SelectionGate.ShouldRestoreAfterSuppress"/>）都不用再看条目数。
/// </para>
/// <para>
/// <b>取不到源码的人怎么复核：</b>本机 <c>curl</c> 出网被拦（<c>github.com</c> 全部
/// <c>000</c>），本次原文是经 <c>WebFetch</c> 取回的；行号只有 144-148 这一处由
/// <b>两次窗口取回的重叠区互相印证</b>过，其余引用按函数名给出，没有臆造行号。
/// </para>
/// </remarks>
internal static class SelectionPolicy
{
    /// <summary>
    /// 受控目标值要不要真的写进控件。<c>false</c> 的两种情形：值已经是对的（写了也是空转），
    /// 或者控件此刻根本无法兑现这个值（越界 → 见类注释里的 <c>E_INVALIDARG</c> 那条）。
    /// </summary>
    /// <param name="itemCount">控件当前的条目数（必须是<b>本轮 items 已经改完之后</b>的数）。</param>
    /// <param name="currentIndex">控件当前的值。</param>
    /// <param name="targetIndex">受控目标值。</param>
    /// <remarks>
    /// <b>取值范围恰好是 <c>Selector</c> 自己认的那一段</b>（它的判据原文是
    /// <c>newValue &gt;= -1 &amp;&amp; newValue &lt; nCount</c>）：
    /// <list type="bullet">
    ///   <item><c>-1</c> 是<b>合法值</b>，意思是"清空选中"，必须照写——
    ///         否则 state 说"没选中"而控件还亮着某一项，两边对着看就是 bug。</item>
    ///   <item>小于 <c>-1</c> 或大于等于 <c>nCount</c> 的值控件兑现不了：
    ///         变更会被整条 undo，items 已存在时还以 <c>E_INVALIDARG</c> 收尾。</item>
    /// </list>
    /// 清空带来的副作用由另一个地方拦住（<c>SelectionRestore</c> 对
    /// <c>target.Index &lt; 0</c> 直接不纠正），<b>不是</b>靠这里拒绝负值来拦的：
    /// 那一条才是防止"清空 → 事件无实项 → 再纠正 → 再清空"自激的地方。
    /// </remarks>
    public static bool ShouldApply(int itemCount, int currentIndex, int targetIndex) =>
        targetIndex >= -1 && targetIndex < itemCount && targetIndex != currentIndex;

    /// <summary>
    /// 这个值是否已经落在控件能兑现的范围之外。
    /// </summary>
    /// <remarks>
    /// 单独留一个方法是为了<b>日志</b>：写不下是整个家族里唯一会导致写入被拒的情形，
    /// 值得和不写的另一半（"值已经对了"）区分开来讲。
    /// </remarks>
    public static bool IsOutOfRange(int itemCount, int targetIndex) =>
        targetIndex < -1 || targetIndex >= itemCount;
}
