using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
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
        var (backdrop, setBackdrop) = UseState(BackdropKind.None);

        var options = new (BackdropKind Kind, string Label)[]
        {
            (BackdropKind.None, "无"),
            (BackdropKind.Mica, "云母"),
            (BackdropKind.MicaAlt, "云母Alt"),
            (BackdropKind.DesktopAcrylic, "亚克力"),
            (BackdropKind.AcrylicThin, "亚克力Thin"),
        };

        var buttons = new List<Element?>();
        foreach (var (kind, label) in options)
        {
            buttons.Add(Button(
                kind == backdrop ? $"> {label}" : label,
                () => setBackdrop(kind)));
        }

        return ScrollViewer(
            VStack(
                TextBlock("A4.5 回归验证"),
                TextBlock($"[5] 背景材质（当前: {backdrop}）"),
                HStack(buttons.ToArray()),
                // 材质不生效时的自检：NO 即为该条件命中静默回退。
                TextBlock($"    自检: {Reactor.Uwp.Hosting.BackdropDiagnostics.Report()}"),
                TextBlock("    亚克力若只显示橙红色 = 命中 FallbackColor（回退）"),
                Component<ConditionalRenderSection>(),
                Component<ParentRerenderSection>(),
                Component<KeyedListSection>(),
                Component<StructureChangeSection>(),
                Component<ReactorSyntaxSection>(),
                Component<ReentrantUpdateSection>()
            )
        ).Backdrop(backdrop);
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
/// 场景7：patch 途中的同步回调（重入）。
/// TextBox 受控且 onChange 把输入规范化（转大写）→ 渲染时给 TextBox.Text 赋值 →
/// XAML 同步触发 TextChanged → setState → 请求重渲染。
/// 若这第二次渲染在上一轮 patch 还没跑完时同步执行，内外两层会交错改同一棵
/// 原生树，Panel 里多出一个子控件，下一次 patch 按 element 数量索引即
/// ArgumentOutOfRangeException。修复后应被推迟、稳定收敛。
/// </summary>
public sealed class ReentrantUpdateSection : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("abc");

        return VStack(
            TextBlock("[7] 重入：改动下面 TextBox，输入会被规范化为大写，不应崩溃"),
            TextBox(text, v => setText(v.ToUpperInvariant()), header: "自动转大写"),
            TextBlock($"当前值: {text}")
        );
    }
}

/// <summary>
/// 场景6：官方 Reactor 语法兼容。这一节刻意只用与 WinUI3 版 Microsoft.UI.Reactor
/// 相同的写法，用来验证两侧 DSL 是否真的能对上。
/// </summary>
public sealed class ReactorSyntaxSection : Component
{
    public override Element Render()
    {
        var (n, setN) = UseState(2);
        var (show, setShow) = UseState(true);

        // UseMemo / UseCallback / UseRef：与官方同签名
        var doubled = UseMemo(() => n * 2, n);
        var bump = UseCallback(() => setN(n + 1), n);
        var renders = UseRef(0);
        renders.Current++;

        return VStack(8,
            "[6] Reactor 语法兼容（字符串隐式转 TextBlock、spacing、组合子）",
            $"    n={n} doubled={doubled} renders={renders.Current}",
            HStack(8,
                Button("+1", bump),
                Button(show ? "隐藏" : "显示", () => setShow(!show))),
            When(show, () => $"    When 分支：n={n}"),
            If(n > 3, () => "    If 分支：n 已大于 3", () => "    If 分支：n 还不够大"),
            ForEach(new[] { "A", "B", "C" }, (item, i) => $"      {i}: {item}")
        ).Margin(0, 8, 0, 0);
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