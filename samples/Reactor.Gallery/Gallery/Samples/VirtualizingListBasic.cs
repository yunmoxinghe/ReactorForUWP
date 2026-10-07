using System;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 长列表的正确打开方式：<c>VirtualizingList</c>——只为看得见的那些项建控件。
/// </summary>
/// <remarks>
/// 这里的数据源是 5000 项，屏幕上同时存在的容器通常只有几十个；
/// 滚出视野的项会被回收再利用于即将进入的项。
/// <para>
/// <b>走的是官方 <c>ItemsRepeater</c></b>（配原生元素工厂），不是自绘独轮车——
/// 所以 <c>UIA</c> 看到的仍是标准 item 结构、<c>UIAutomation</c> 的虚拟列表
/// 协议也能用。原生桥没加载起来时会回退到自绘，回退时 <c>itemKey</c> 才起作用。
/// </para>
/// <para>
/// <b>项等高是这个实现的前提</b>：滚动位置 → 可见下标 → 容器位置，全靠
/// <c>itemHeight</c> 换算。不等高的列表请用 <c>ListView</c>。
/// </para>
/// </remarks>
public sealed class VirtualizingListBasic : Component
{
    private readonly string[] _rows = Enumerable
        .Range(0, 5000)
        .Select(i => string.Format(CultureInfo.InvariantCulture, "第 {0} 行 · 数据源下标越大越靠后", i))
        .ToArray();

    public override Element Render() =>
        VStack(8,
            TextBlock($"共 {_rows.Length} 项，拖动滚动条看回收情况").Caption().Subtle(),
            VirtualizingList(
                _rows,
                (item, _) => TextBlock((string)item!)
                    .AutomationName((string)item!),
                itemHeight: 32,
                height: 260)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"), 1));
}
