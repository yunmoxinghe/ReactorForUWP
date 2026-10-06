using System;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 数据源下发策略：<b>内部按引用相等比较数据源的控件</b>（WinUI 2 面包屑就是）该怎么喂。
/// 纯逻辑、不依赖 WinRT → <c>tests/Reactor.Core.Tests</c> 直接 Link 进来断言。
/// </summary>
/// <remarks>
/// <b>依据链（全部在 <c>tools/winui2-ref/</c> 里核过）：</b>
/// <list type="number">
///   <item><c>dev/Breadcrumb/BreadcrumbBar.cpp:80-83</c> —— <c>OnPropertyChanged</c>
///         里只有 <c>property == s_ItemsSourceProperty</c> 时才调
///         <c>UpdateItemsRepeaterItemsSource()</c>；</item>
///   <item><c>cpp:142-155</c> —— 该函数重建 <c>m_breadcrumbItemsSourceView</c>，
///         并在 repeater 已存在时 <c>itemsRepeater.ItemsSource(*m_itemsIterable)</c>；</item>
///   <item><c>cpp:167-170</c> —— 官方注释写着
///         <c>// A new BreadcrumbIterable must be created as ItemsRepeater compares if the
///         previous itemsSource is equals to the new one</c>。</item>
/// </list>
/// 合起来就是：<b>XAML 依赖属性对"设成同一个引用"不抛变更通知</b>（第 1 步不会发生），
/// 于是第 2、3 步整条不打，内部 <c>ItemsRepeater</c> 的条目<b>纹丝不动</b>。
/// 这正是"面包屑不见了"的根：长期持有一个 <c>ItemCollection</c>、之后原地
/// <c>Clear()/Add()</c>——引用从头到尾没变。
/// <para>
/// 唯一的补救口子是集合变更通知，而它在面包屑里是残的：
/// <c>BreadcrumbBar.h</c> 的 <c>m_itemsSourceAsObservableVectorChanged</c> 全仓库只有
/// revoke 没有赋值（死代码）；真正接的那条 <c>ItemsSourceView.CollectionChanged</c>
/// 处理体（<c>cpp:163-175</c>）开头就要求 <c>m_itemsRepeater</c> 已存在。
/// </para>
/// </remarks>
internal static class ItemsSourcePolicy
{
    /// <summary>
    /// 把 <paramref name="candidate"/> 赋给 <c>ItemsSource</c>，内部 repeater 会不会动。
    /// </summary>
    /// <param name="currentSource">控件上当前的数据源引用。</param>
    /// <param name="candidateSource">打算写进去的那个。</param>
    /// <returns>
    /// true = 引用变了，会触发重建；false = <b>引用没变，写了也白写</b>。
    /// </returns>
    public static bool WillTriggerRebuild(object? currentSource, object? candidateSource) =>
        !ReferenceEquals(currentSource, candidateSource);

    /// <summary>中文原因，用于日志。</summary>
    public static string Reason(bool willTrigger) =>
        willTrigger ? "引用变化，会重建" : "引用未变，ItemsRepeater 不会重建（条目不更新）";
}
