// 实验室页：需要人手点两下的那几个验证项。
//
// 与压测页的区别：压测是无人值守跑完出归档，这里是「点一下看一眼」——
// 身份模型对不对、Echo 抑制有没有把输入吃掉，都要靠手动操作才能确认。
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Internal;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

/// <summary>
/// 虚拟列表身份实验室：验证「item key 是身份、下标只是位置」。
/// </summary>
/// <remarks>
/// 关键动作是<b>头部插入</b>：下标整体 +1 但身份没变。走 key 身份时已挂载的
/// 项只挪位置、内容不变；退化成下标身份时它们会集体拿错内容（这正是修之前的现象）。
/// </remarks>
public sealed class VirtualListLabPage : Component
{
    private const int Count = 5000;

    public override Element Render()
    {
        var (items, setItems) = UseState(MakeItems());
        var (useKey, setUseKey) = UseState(true);
        var (log, setLog) = UseState("尚未操作");

        List<object?> boxed = items.Cast<object?>().ToList();

        return VStack(
            TextBlock($"虚拟列表实验室：{items.Count} 项，身份 = " +
                      (useKey ? "item key（稳定）" : "下标（退化，插入会错位）")),
            HStack(
                Button("头部插入", () =>
                {
                    var next = new List<string>(items);
                    next.Insert(0, "NEW-" + (DateTime.Now.Ticks % 10000).ToString("D4"));
                    setItems(next);
                    setLog($"已在头部插入 {next[0]}，原本第 0 项现在是第 1 项");
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
                Button("末项移到第 2", () =>
                {
                    if (items.Count <= 3)
                    {
                        return;
                    }

                    var next = new List<string>(items);
                    var moved = next[^1];
                    next.RemoveAt(next.Count - 1);
                    next.Insert(1, moved);
                    setItems(next);
                    setLog($"已把 {moved} 移到第 1 位");
                }),
                Button(useKey ? "切到下标身份" : "切到 key 身份", () =>
                {
                    setUseKey(!useKey);
                    setLog("身份模型已切换：再插一次对比两种表现");
                }),
                Button("重置", () =>
                {
                    setItems(MakeItems());
                    setLog("已重置");
                })
            ),
            TextBlock(log),
            TextBlock("滚动到任意位置后点「头部插入」：可见项应原地保持自己的内容，只是整体下移一行。"),
            VirtualizingList(
                boxed,
                (item, i) => TextBlock($"{i,5} · {item}"),
                itemHeight: 28,
                height: 420,
                buffer: 4,
                itemKey: useKey ? (o => o) : null)
        );
    }

    private static List<string> MakeItems() =>
        Enumerable.Range(0, Count).Select(i => "Row-" + i.ToString("D5")).ToList();
}

/// <summary>
/// Echo 实验室：观察 EchoGuard 的匹配/失配计数，回归「粘贴后下一次编辑被覆盖」。
/// </summary>
/// <remarks>
/// 复现手法：粘贴一段文字进下面的输入框，紧接着再敲一个字符。
/// 如果回声抑制是「无脑消费」，第二次编辑会被上一次的回显顶掉；
/// 改成非破坏 Consume 之后，mismatch 应该留在登记里直到 TTL 过期。
/// </remarks>
public sealed class EchoLabPage : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState(string.Empty);
        var (tick, setTick) = UseState(0);

        return VStack(
            TextBlock("Echo 实验室：在下面输入 / 粘贴 / 用输入法组合，观察 EchoGuard 计数。"),
            TextBlock("复现步骤：粘贴一段文字 → 紧接着再输入一个字符 → 看文本有没有被覆盖。"),
            TextBox(
                text,
                v =>
                {
                    setText(v);
                    setTick(tick + 1);
                },
                placeholderText: "在这里粘贴 + 继续编辑",
                header: "输入框"),
            TextBlock($"当前值：{text}"),
            TextBlock($"EchoStats：{EchoStats.Snapshot()}"),
            HStack(
                Button("刷新统计", () => setTick(tick + 1)),
                Button("清零统计", () =>
                {
                    EchoStats.Reset();
                    setTick(tick + 1);
                }),
                Button("清空文本", () => setText(string.Empty))
            )
        );
    }
}
