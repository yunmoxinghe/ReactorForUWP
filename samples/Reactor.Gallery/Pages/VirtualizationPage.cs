using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

/// <summary>
/// 长列表虚拟化：5000 项，只为可视区（外加上下缓冲）创建真实控件。
/// </summary>
/// <remarks>
/// 重点是 <c>itemKey</c>：数据源会发生插入 / 删除 / 移动时<b>必须</b>给一个稳定身份，
/// 否则下标位移会让已挂载的项拿错内容。点「头部插入」看效果——
/// 可见项应该原地保持自己的内容，只是整体往下挪一行。
/// <para>
/// 去掉 <c>itemKey</c>（把下面那行改成 <c>itemKey: null</c>）再插一次，就能对比出错位。
/// </para>
/// </remarks>
public sealed class VirtualizationPage : Component
{
    private const int Count = 5000;

    public override Element Render()
    {
        var (items, setItems) = UseState(MakeItems());
        var (log, setLog) = UseState("尚未操作");

        List<object?> boxed = items.Cast<object?>().ToList();

        return VStack(12,
            TextBlock($"虚拟化长列表（{items.Count} 项）").FontSize(20),
            HStack(8,
                Button("头部插入", () =>
                {
                    var next = new List<string>(items);
                    next.Insert(0, "NEW-" + (DateTime.Now.Ticks % 10000).ToString("D4"));
                    setItems(next);
                    setLog($"已插入 {next[0]}，原本第 0 项现在是第 1 项");
                }),
                Button("删除第 3 项", () =>
                {
                    if (items.Count <= 3)
                    {
                        return;
                    }

                    var next = new List<string>(items);
                    var removed = next[2];
                    next.RemoveAt(2);
                    setItems(next);
                    setLog($"已删除 {removed}");
                }),
                Button("重置", () =>
                {
                    setItems(MakeItems());
                    setLog("已重置");
                })),
            TextBlock(log).Caption(),
            VirtualizingList(
                boxed,
                (item, index) => TextBlock($"{index,5} · {item}"),
                itemHeight: 28,
                height: 420,
                buffer: 4,
                itemKey: item => item)
        ).Padding(16);
    }

    private static List<string> MakeItems() =>
        Enumerable.Range(0, Count).Select(i => "Row-" + i.ToString("D5")).ToList();
}
