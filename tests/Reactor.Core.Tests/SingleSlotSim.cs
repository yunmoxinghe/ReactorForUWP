using System;
using System.Collections.Generic;
using System.Linq;

namespace Reactor.Core.Tests;

/// <summary>
/// 单子元素容器「patch 路径 vs 卸载路径」的一致性模型。
/// </summary>
/// <remarks>
/// <para>
/// 模型只编码<b>有源码出处</b>的事实（<c>Reactor.uwp/Internal/Reconciler.cs</c>）：
/// <list type="bullet">
///   <item><description>
///     真实 XAML 里"子内容放在哪个属性"没有统一接口：<c>ContentControl</c> 放
///     <c>Content</c>、<c>Border</c> 放 <c>Child</c>、第三方容器各放各的。
///     想替代它们的容器都得登記到 <c>SingleChildAccessor</c> 那张表里，
///     协调器按具体类型查表（无反射、AOT 友好）。
///   </description></item>
///   <item><description>
///     <c>PatchSingleChild</c>（patch 侧）的查找顺序是三级：ContentControl →
///     Border → 登记表；
///   </description></item>
///   <item><description>
///     <c>UnmountTree</c>（卸载侧）的单槽分支曾经只有前两级，<b>漏了登记表</b>。
///   </description></item>
/// </list>
/// </para>
/// <para>
/// 漏掉那一级的后果不是"报错"而是"静默少做一件事"：从登记表接入的容器在被丢弃
/// 整棵子树时，槽里的子树不会被递归卸载，于是内部的 <c>ComponentNode</c> 永远留在
/// 注册表、<c>IsMounted</c> 仍为 true，继续响应状态更新、去 patch 一棵已经离开
/// 可视树的树。外部表现就是"反复切页内存一直涨"。
/// </para>
/// <para>
/// <b>为什么这个模型值得存在</b>：这类 bug 是<b>判据漂移</b>——两处代码各自描述了
/// 同一件事，某一处少了一级，静态看毫无异样，跑起来也不抛异常。它只在"整棵子树
/// 被丢弃 + 槽里嵌了组件"这个交叉条件下才显形，手工回归很难稳定复现。
/// 判据被收汇成单一真源之后，明天另一条路径再分叉出去，模型照样能报警。
/// </para>
/// </remarks>
internal sealed class SingleSlotSim
{
    /// <summary>容器怎么放子内容：对应协调器那张三级查找表的三档。</summary>
    public enum Kind
    {
        /// <summary>官方 <c>ContentControl</c>：内容在 <c>Content</c> 上。</summary>
        ContentControl,

        /// <summary><c>Border</c>：内容在 <c>Child</c> 上。</summary>
        Border,

        /// <summary>
        /// 既不是 ContentControl 也不是 Border，靠 <c>SingleChildAccessor</c>
        /// 登記接入（<c>Viewbox</c> / <c>ParallaxView</c> / <c>SettingsExpander</c>
        /// / <c>Popup</c> / <c>SplitView</c>）。
        /// </summary>
        Registered,

        /// <summary>协调器不认识的容器：两条路径都只能放弃。</summary>
        Unknown,
    }

    /// <summary>
    /// patch 侧要不要走到登记表那一級。
    /// </summary>
    /// <remarks>
    /// 从来都是要的；开关在这里的作用是<b>证明模型有视力</b>——见
    /// <see cref="SingleSlotTests"/> 里那条反向对照。
    /// </remarks>
    public bool PatchConsultsAccessor = true;

    /// <summary>
    /// <b>修法开关</b>：卸载侧要不要走到登记表那一級。
    /// </summary>
    /// <remarks>
    /// 关掉它就复现了原 bug 的形状：patch 进得去的槽，卸载进不去。
    /// </remarks>
    public bool UnmountConsultsAccessor = true;

    /// <summary>
    /// <b>第二个修法开关</b>：卸载侧要不要连多出来的槽一起遍历
    /// （<c>IElementHandler.ExtraSlotsOf</c>，典型是 <c>SplitView.Pane</c>）。
    /// </summary>
    public bool UnmountWalksExtraSlots = true;

    /// <summary>被观察的那些容器（真实类型，取自 <c>ElementHandlerRegistry</c> 的登記表）。</summary>
    public static readonly Kind[] Registry = { Kind.Registered };

    /// <summary>所有档位（含已知的两档原生容器），用来跑"两条路径答案必须一致"。</summary>
    public static readonly Kind[] All =
    {
        Kind.ContentControl,
        Kind.Border,
        Kind.Registered,
        Kind.Unknown,
    };

    /// <summary>patch 路径能不能进到这个容器的槽里。</summary>
    public bool PatchReaches(Kind kind) => kind switch
    {
        Kind.ContentControl or Kind.Border => true,
        Kind.Registered => PatchConsultsAccessor,
        _ => false,
    };

    /// <summary>卸载路径能不能进到这个容器的槽里（<b>修法开关在这里</b>）。</summary>
    public bool UnmountReaches(Kind kind) => kind switch
    {
        Kind.ContentControl or Kind.Border => true,
        Kind.Registered => UnmountConsultsAccessor,
        _ => false,
    };

    /// <summary>
    /// 两条路径对这个容器给出的答案是不是同一个。
    /// </summary>
    /// <remarks>
    /// 这条就是这次要钉死的判据：不一致意味着"进得去的那条路留下了债，
    /// 而收债的那条路看不见它"。
    /// </remarks>
    public bool PathsAgree(Kind kind) => PatchReaches(kind) == UnmountReaches(kind);

    /// <summary>答不上来的容器数量（默认必须为零）。</summary>
    public int Disagreements(IEnumerable<Kind>? kinds = null) =>
        (kinds ?? All).Count(kind => !PathsAgree(kind));

    /// <summary>
    /// 多出来的槽会不会漏掉（默认必须为零）：<c>Pane</c> 不在主槽上，
    /// 只有显式遍历 <c>ExtraSlotsOf</c> 才能跟着一起回收。
    /// </summary>
    public bool ExtraSlotsLeaked => !UnmountWalksExtraSlots;
}
