using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

// ════════════════════════════════════════════════════════════════════
//  页签容器（对应 WinUI 2 的 TabView / TabViewItem）
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// 页签项，对应 WinUI 2 的 <c>TabViewItem</c>。
/// </summary>
/// <remarks>
/// <b>它就是原生那个控件，不是自绘替代品</b>：键盘 <c>Ctrl+Tab</c> 顺序切换、
/// 页签拖拽重排、关闭按钮、<c>Tab</c> 角色的自动化对等全部由官方模板提供。
/// 这里只描述"表头 + 图标 + 能否关闭"三件事。
/// </remarks>
public sealed record TabElement(string? Header = null) : Element
{
    /// <summary>页签上的图标（<c>FontIcon</c> / <c>BitmapIcon</c> 之类）。</summary>
    public Element? HeaderIcon { get; init; }

    /// <summary>是否显示关闭按钮（XAML 的 <c>IsClosable</c>，默认 false）。</summary>
    public bool IsClosable { get; init; }
}

/// <summary>
/// 页签容器，对应 WinUI 2 的 <c>TabView</c>：上排页签条 + 一个内容区。
/// </summary>
/// <param name="Tabs">页签项。</param>
/// <param name="SelectedIndex">受控选中下标。<see cref="Optional{T}.Unset"/> = 非受控。</param>
/// <param name="OnSelectedIndexChanged">选中变化回调。</param>
/// <remarks>
/// <b>为什么内容区在容器上、不在页签上。</b>官方 <c>TabView</c> 的用法分两派： WinUI 3
/// 的常见写法是把内容塞进 <c>TabViewItem.Content</c>（页签与内容一一对应）；WinUI 2 的
/// <c>TabViewItem</c> 同样继承 <c>ContentControl</c>，但把 <c>UIElement</c> 当数据塞进
/// <c>TabItems</c> 集合是行不通的 —— 与本仓库 <c>BreadcrumbBar</c> 那条踩过的坑同形：
/// <c>UIElement</c> 进集合时就拿了父，控件内部再挂一次就是第二个父
/// （<c>0x800F1000 "Element is already the child of another element."</c>）。
/// <para>
/// 所以这里采取"页签只描述表头，内容按当前选中下标由容器渲染"的形态：内容树每次
/// 只有一份，切换页签时走一次 patch，而不是"建 N 棵树各挂一处"。样例画廊里每个页签
/// 的内容都不小（预览 / 源码），这个差别是实打实的。
/// </para>
/// </remarks>
public sealed record TabViewElement(
    IReadOnlyList<TabElement> Tabs,
    Optional<int> SelectedIndex = default,
    Action<int>? OnSelectedIndexChanged = null) : Element
{
    /// <summary>当前选中页签的内容。</summary>
    public Element? Content { get; init; }

    /// <summary>是否显示"新建页签"按钮（对应 WinUI 的 <c>IsAddTabButtonVisible</c>）。</summary>
    public bool IsAddTabButtonVisible { get; init; }

    /// <summary>点"新建页签"按钮（对应官方 <c>AddTabButtonClick</c>）。</summary>
    public Action? OnAddTabClick { get; init; }

    /// <summary>
    /// 页签宽度怎么算（XAML 的 <c>TabWidthMode</c>）。
    /// </summary>
    /// <remarks>
    /// 三档：<c>Equal</c>（等宽，官方默认）/ <c>SizeToContent</c>（按文字长短）
    /// / <c>Compact</c>（收窄，只留图标那一档）。
    /// <b>与 <c>TabViewItem.Header</c> 的长短无关的是 <c>Equal</c></b>：
    /// 想让"文字长的页签宽一点"要显式给 <c>SizeToContent</c>，默认不会。
    /// </remarks>
    public MuxControls.TabViewWidthMode TabWidthMode { get; init; } =
        MuxControls.TabViewWidthMode.Equal;

    /// <summary>
    /// 关闭按钮什么时候冒出来（XAML 的 <c>CloseButtonOverlayMode</c>）。
    /// </summary>
    /// <remarks>
    /// 三档：<c>Auto</c>（官方默认，只在选中那个页签上显示）/
    /// <c>OnPointerOver</c>（鼠标移上去才显示）/ <c>Always</c>（一直显示）。
    /// 注意成员名是 <c>OnPointerOver</c> 而不是 <c>OnHover</c>——
    /// WinUI 2.8 里没有 <c>OnHover</c> 这个值（实测编译不过）。
    /// </remarks>
    public MuxControls.TabViewCloseButtonOverlayMode CloseButtonOverlayMode { get; init; } =
        MuxControls.TabViewCloseButtonOverlayMode.Auto;
}
