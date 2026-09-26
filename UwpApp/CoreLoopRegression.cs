using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

/// <summary>
/// A4.5 回归页：用真实项目形态（组件嵌套 + 条件渲染 + keyed 列表 + 结构变化）
/// 验证核心 runtime 在控件加入后没有回归。四个场景各一个子组件。
/// </summary>
public sealed class CoreLoopRegression : Component
{
    public override Element Render()
    {
        return ScrollViewer(
            VStack(
                TextBlock("A4.5 回归验证"),
                Component<ConditionalRenderSection>(),
                Component<ParentRerenderSection>(),
                Component<KeyedListSection>(),
                Component<StructureChangeSection>()
            )
        );
    }
}

/// <summary>
/// 场景1：条件渲染。state 存于父组件，TextBox 只是受控显示。
/// 隐藏时 TextBox 卸载，但 state 保留；再次显示按受控值恢复。
/// </summary>
public sealed class ConditionalRenderSection : Component
{
    public override Element Render()
    {
        var (show, setShow) = UseState(true);
        var (text, setText) = UseState("初始文本");

        return VStack(
            TextBlock("[1] 条件渲染：输入文字 → 隐藏 → 显示，内容应恢复为 state 的值"),
            Button(show ? "隐藏 TextBox" : "显示 TextBox", () => setShow(!show)),
            show ? TextBox(text, setText, header: "条件渲染的 TextBox") : TextBlock("(TextBox 已隐藏)")
        );
    }
}

/// <summary>
/// 场景2：父组件 rerender 不影响子组件 state。
/// 点「父按钮」触发父 rerender，子组件（TextBox + Slider）不应重渲染、状态应保留。
/// </summary>
public sealed class ParentRerenderSection : Component
{
    public override Element Render()
    {
        var (parentClicks, setParentClicks) = UseState(0);

        return VStack(
            TextBlock("[2] 父 rerender 不影响子组件：点父按钮，子 TextBox/Slider 状态应保留"),
            Button($"父按钮点击 {parentClicks} 次", () => setParentClicks(parentClicks + 1)),
            TextBlock($"父渲染标记: {parentClicks}"),
            Component<StatefulChild>()
        );
    }
}

/// <summary>场景2 的子组件：自带 TextBox + Slider 双状态。</summary>
public sealed class StatefulChild : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("子组件文本");
        var (value, setValue) = UseState(30.0);

        return VStack(
            TextBox(text, setText, header: "子组件 TextBox"),
            Slider(value, 0, 100, setValue),
            TextBlock($"子组件回显: {text} / {value:0.0}")
        );
    }
}

/// <summary>
/// 场景3：keyed 列表头部插入。先给各 item 的 count 递增，再头部插入 X，
/// A/B/C 各自 count 不应错位（依靠 Key 匹配复用 ComponentNode）。
/// </summary>
public sealed class KeyedListSection : Component
{
    public override Element Render()
    {
        var (ids, setIds) = UseState(new[] { "A", "B", "C" });

        var children = new List<Element?>
        {
            TextBlock("[3] keyed 列表头部插入：先点各 item 的 + 增 count，再点「头部插入新项」看 count 不错位"),
            Button("头部插入新项", () =>
            {
                var next = new string[ids.Length + 1];
                next[0] = $"新{ids.Length}";
                Array.Copy(ids, 0, next, 1, ids.Length);
                setIds(next);
            }),
        };

        foreach (var id in ids)
        {
            children.Add(Component<ListItem, ListItemProps>(new ListItemProps(id)) with { Key = id });
        }

        return VStack(children.ToArray());
    }
}

public sealed record ListItemProps(string Id);

/// <summary>场景3 的列表项：带 props 的同时有独立 count state。</summary>
public sealed class ListItem : Component<ListItemProps>
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return HStack(
            TextBlock($"{Props.Id}"),
            TextBlock($"count={count}"),
            Button("+", () => setCount(count + 1))
        );
    }
}

/// <summary>
/// 场景4：结构变化重排。TextBox + Button 与 TextBox + Slider + Button 之间切换，
/// 确认重排稳定（顺序正确、TextBox 内容保留、不崩溃）。
/// </summary>
public sealed class StructureChangeSection : Component
{
    public override Element Render()
    {
        var (hasSlider, setHasSlider) = UseState(false);
        var (text, setText) = UseState("文本");

        return VStack(
            TextBlock("[4] 结构变化重排：点切换插入/移除 Slider，看 TextBox 内容与顺序稳定、不崩溃"),
            Button(hasSlider ? "移除 Slider" : "插入 Slider", () => setHasSlider(!hasSlider)),
            TextBox(text, setText) with { Key = "txt" },
            hasSlider ? Slider(40, 0, 100) with { Key = "sli" } : null,
            Button("末尾确认按钮") with { Key = "btn" }
        );
    }
}