using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

/// <summary>
/// 新元素演示 + 自检页：把这一轮补齐的元素全部摆出来，可交互验证；
/// 顶部是一次性自检结果（纯逻辑断言 + Context 接线验证）。
/// 点顶部按钮可切回原回归页。
/// </summary>
public sealed class ElementGallery : Component
{
    /// <summary>用于验证 Provide / UseContext 是否真的接通。</summary>
    internal static readonly Context<string> Accent = new("(默认值)");

    /// <summary>虚拟化列表的演示数据源：2000 项，验证"只挂载可视区"。</summary>
    private static readonly IReadOnlyList<object?> BigList =
        Enumerable.Range(0, 2000).Select(i => (object?)$"第 {i} 行 · 虚拟化列表项").ToArray();

    public override Element Render()
    {
        var (showRegression, setShowRegression) = UseState(false);
        var (comboIndex, setComboIndex) = UseState(0);
        var (isOn, setIsOn) = UseState(false);
        var (radio, setRadio) = UseState(1);
        var (listIndex, setListIndex) = UseState(-1);
        var (gridIndex, setGridIndex) = UseState(-1);
        var (progress, setProgress) = UseState(30.0);
        var (navIndex, setNavIndex) = UseState(0);
        var (backdrop, setBackdrop) = UseState(BackdropKind.None);

        if (showRegression)
        {
            return VStack(
                Button("← 返回新元素演示", () => setShowRegression(false)),
                Component<CoreLoopRegression>());
        }

        return ScrollViewer(
            VStack(10,
                TextBlock("新元素演示（补齐 Grid / Border / 列表 / 输入 / 进度 / 图片 / 导航）")
                    .FontSize(18),
                Button("切换到原回归页 →", () => setShowRegression(true)),

                // ── 背景材质（云母 / 亚克力）─────────────────────
                TextBlock($"背景材质（当前: {backdrop}）").FontSize(16),
                HStack(6, BackdropButtons(backdrop, setBackdrop)),
                TextBlock($"    自检: {Reactor.Uwp.Hosting.BackdropDiagnostics.Report()}"),
                TextBlock("    亚克力若只显示纯色 = 命中 FallbackColor（系统静默回退）"),

                // ── 自检 ─────────────────────────────────────────
                Border(
                    VStack(4, SelfCheckRows())
                ).Padding(Thick(10)),

                // ── Context（Provide → UseContext）──────────────
                TextBlock("Context：外层 Provide(\"已从祖先注入\")，子组件应读到该值").FontSize(16),
                VStack(6,
                    TextBlock("未提供时的默认分支：" + Accent.DefaultValue),
                    VStack(
                        Component<ContextConsumer>()
                    ).Provide(Accent, "已从祖先注入")
                ),

                // ── Grid ────────────────────────────────────────
                TextBlock("Grid：两列自适应 + 星号，表单布局").FontSize(16),
                Grid(
                    new[] { GridSize.Auto, GridSize.Star() },
                    new[] { GridSize.Auto, GridSize.Auto, GridSize.Auto },
                    TextBlock("姓名").Grid(row: 0, column: 0),
                    TextBox(placeholderText: "请输入姓名").Grid(row: 0, column: 1),
                    TextBlock("邮箱").Grid(row: 1, column: 0),
                    TextBox(placeholderText: "name@example.com").Grid(row: 1, column: 1),
                    TextBlock("备注").Grid(row: 2, column: 0),
                    TextBox(placeholderText: "选填").Grid(row: 2, column: 1)
                ),

                // ── Border ──────────────────────────────────────
                TextBlock("Border：圆角 + 背景 + 内边距").FontSize(16),
                new BorderElement(
                    TextBlock("这是一个带边框的容器"))
                {
                    CornerRadius = 8,
                    Background = new SolidColorBrush(Colors.LightGray),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = Thick(1),
                    Padding = Thick(12),
                },

                // ── 输入与选择 ──────────────────────────────────
                TextBlock("输入与选择：ComboBox / ToggleSwitch / RadioButton(s)").FontSize(16),
                VStack(6,
                    ComboBox(
                        new[] { "红色", "绿色", "蓝色" },
                        Optional<int>.Of(comboIndex),
                        setComboIndex),
                    TextBlock($"ComboBox 选中：{comboIndex}"),
                    ToggleSwitch(Optional<bool>.Of(isOn), setIsOn, "开", "关", "开关"),
                    TextBlock($"ToggleSwitch：{(isOn ? "开" : "关")}"),
                    RadioButton("选项 A", Optional<bool>.Of(radio == 0), v => { if (v) setRadio(0); }, "demo"),
                    RadioButton("选项 B", Optional<bool>.Of(radio == 1), v => { if (v) setRadio(1); }, "demo"),
                    RadioButtons(
                        new[] { "第一", "第二", "第三" },
                        Optional<int>.Of(radio),
                        setRadio),
                    TextBlock($"单选：{radio}")
                ),

                // ── 进度 ────────────────────────────────────────
                TextBlock("进度：ProgressBar / ProgressRing").FontSize(16),
                VStack(6,
                    Progress(progress),
                    ProgressRing(progress),
                    TextBlock("不确定进度（Value = null）："),
                    Progress(null),
                    ProgressRing(null),
                    HStack(6,
                        Button("-10", () => setProgress(Math.Max(0, progress - 10))),
                        Button("+10", () => setProgress(Math.Min(100, progress + 10)))
                    ),
                    TextBlock($"进度值：{progress}")
                ),

                // ── 图片 ────────────────────────────────────────
                TextBlock("图片：Image（Assets/StoreLogo.png）").FontSize(16),
                Image("Assets/StoreLogo.png").Width(96).Height(96),

                // ── 列表 ────────────────────────────────────────
                TextBlock("列表：ListView / GridView").FontSize(16),
                // 列表在垂直 StackPanel 里会按内容撑开高度，这里显式限高，
                // 否则选中/重排时高度抖动会顶动下方内容。
                ListView(
                    Optional<int>.Of(listIndex),
                    setListIndex,
                    TextBlock("列表项 1"),
                    TextBlock("列表项 2"),
                    TextBlock("列表项 3")).Height(150),
                TextBlock($"ListView 选中：{listIndex}"),
                GridView(
                    Optional<int>.Of(gridIndex),
                    setGridIndex,
                    TextBlock("网格 1"),
                    TextBlock("网格 2"),
                    TextBlock("网格 3")).Height(120),
                TextBlock($"GridView 选中：{gridIndex}"),

                // ── 导航 ────────────────────────────────────────
                TextBlock("导航：NavigationView").FontSize(16),
                new NavigationViewElement(
                    VStack(TextBlock($"当前页：{navIndex}")),
                    new[]
                    {
                        new NavigationViewItemData("首页", "\uE10F"),
                        new NavigationViewItemData("设置", "\uE713"),
                    })
                {
                    SelectedIndex = navIndex,
                    OnSelectedIndexChanged = setNavIndex,
                }.Height(240),

                // ── 虚拟化列表 ────────────────────────────────
                TextBlock($"虚拟化列表（{BigList.Count} 项，只挂载可视区）").FontSize(16),
                TextBlock("    滚动时应始终流畅；与下方 ListView 的区别是它不会建 2000 个控件"),
                VirtualizingList(
                    BigList,
                    (item, index) => Border(
                        HStack(8,
                            TextBlock($"#{index}"),
                            TextBlock(item?.ToString() ?? "").Wrap())
                    ).Padding(6, 0, 6, 0),
                    itemHeight: 40,
                    height: 200),

                // ── 样式 / 链接 / 设置卡片 ────────────────────
                TextBlock("样式系统：Caption / BodyStrong / Subtitle / Accent").FontSize(16),
                VStack(4,
                    TextBlock("Caption 12px 次要说明").Caption(),
                    TextBlock("BodyStrong 加粗正文").BodyStrong(),
                    TextBlock("Subtitle 副标题").Subtitle(),
                    HStack(
                        Button("强调按钮").Accent(),
                        HyperlinkButton("超链接", () => { }),
                        FontIcon("\uE706")
                    )),

                TextBlock("设置卡片（CommunityToolkit SettingsCard / SettingsExpander）").FontSize(16),
                VStack(4,
                    SettingsCard("导航位置", ComboBox(new[] { "左侧", "顶部" }, Optional<int>.Of(comboIndex), setComboIndex),
                        headerIcon: FontIcon("\uF594")),
                    SettingsExpander(
                        header: "可展开设置",
                        headerIcon: FontIcon("\uE713"),
                        items: new Element?[]
                        {
                            SettingsCard(content: ToggleSwitch(Optional<bool>.Of(isOn), setIsOn),
                                contentAlignment: SettingsCardContentAlignment.Left),
                        })
            ))).Backdrop(backdrop);
    }

    private static Element?[] BackdropButtons(
        BackdropKind current,
        Action<BackdropKind> setBackdrop)
    {
        var options = new (BackdropKind Kind, string Label)[]
        {
            (BackdropKind.None, "无"),
            (BackdropKind.Mica, "云母"),
            (BackdropKind.MicaAlt, "云母Alt"),
            (BackdropKind.DesktopAcrylic, "亚克力"),
            (BackdropKind.AcrylicThin, "亚克力Thin"),
        };

        var buttons = new Element?[options.Length];
        for (var i = 0; i < options.Length; i++)
        {
            var (kind, label) = options[i];
            buttons[i] = Button(
                kind == current ? $"> {label}" : label,
                () => setBackdrop(kind));
        }

        return buttons;
    }

    private static Element[] SelfCheckRows()
    {
        var rows = new List<Element>
        {
            new BorderElement(TextBlock("自检结果").FontSize(15))
            {
                Background = new SolidColorBrush(Colors.LightBlue),
                Padding = Thick(6),
            },
        };

        foreach (var line in SelfChecks.Run())
        {
            rows.Add(TextBlock(line));
        }

        return rows.ToArray();
    }
}

/// <summary>
/// 读取祖先 Provide 的 Context 值。
/// 如果接线没通，这里会显示 Context 的默认值而不是 "已从祖先注入"。
/// </summary>
public sealed class ContextConsumer : Component
{
    public override Element Render()
    {
        var accent = UseContext(ElementGallery.Accent);
        return TextBlock($"UseContext 读到：{accent}");
    }
}

/// <summary>页面上可即时看到的自检项（纯逻辑，不需要访问原生控件）。</summary>
internal static class SelfChecks
{
    internal static IReadOnlyList<string> Run()
    {
        var results = new List<string>();

        void Check(string name, bool ok, string? detail = null) =>
            results.Add($"{(ok ? "ok  " : "FAIL")} {name}{(detail is null ? string.Empty : " — " + detail)}");

        // GridSize（对齐官方：字符串解析 + 隐式转 GridLength）
        Check("GridSize.Parse(\"*\") 往返", GridSize.Parse("*").ToString() == "*", GridSize.Parse("*").ToString());
        Check("GridSize.Px(120)", GridSize.Px(120).ToString() == "120", GridSize.Px(120).ToString());
        Check("GridSize.Star(1.5)", GridSize.Star(1.5).ToString() == "1.5*", GridSize.Star(1.5).ToString());
        Check("GridSize.Parse(\"Auto\")", GridSize.Parse("Auto") == GridSize.Auto);

        GridLength length = GridSize.Star(2);
        Check(
            "GridSize → GridLength 隐式转换",
            length.GridUnitType == GridUnitType.Star && Math.Abs(length.Value - 2) < 1e-9);

        // 工厂与元素类型
        var grid = Grid(new[] { GridSize.Auto, GridSize.Star() }, new[] { GridSize.Auto }, TextBlock("x"));
        Check("Grid 工厂产出 GridDefinition", grid.Definition.Columns.Length == 2 && grid.Definition.Rows.Length == 1);

        var positioned = TextBlock("x").Grid(row: 1, column: 2, rowSpan: 3, columnSpan: 4);
        var attached = positioned.Modifiers?.Grid;
        Check(
            ".Grid(row/column/span) 写入附加属性",
            attached is { Row: 1, Column: 2, RowSpan: 3, ColumnSpan: 4 });

        Check("Border 工厂", Border(TextBlock("x")) is BorderElement);
        Check("ComboBox 工厂", ComboBox(new[] { "a" }) is ComboBoxElement);
        Check("ToggleSwitch 工厂", ToggleSwitch() is ToggleSwitchElement);
        Check("RadioButton 工厂", RadioButton("a") is RadioButtonElement);
        Check("RadioButtons 工厂", RadioButtons(new[] { "a" }) is RadioButtonsElement);
        Check("Progress 工厂", Progress(50) is ProgressElement { IsIndeterminate: false });
        Check("Progress(null) 为不确定", Progress(null) is ProgressElement { IsIndeterminate: true });
        Check("ProgressRing 工厂", ProgressRing(50) is ProgressRingElement);
        Check("Image 工厂", Image("a.png") is ImageElement);
        Check("ListView 工厂", ListView(TextBlock("a")) is ListViewElement);
        Check("GridView 工厂", GridView(TextBlock("a")) is GridViewElement);
        Check(
            "NavigationView 工厂",
            NavigationView(TextBlock("a"), new NavigationViewItemData("首页")) is NavigationViewElement);

        // Provide 写入 ContextValues（协调器靠它压栈）
        var provided = VStack(TextBlock("x")).Provide(ElementGallery.Accent, "已从祖先注入");
        Check(
            "Provide 写入 ContextValues",
            provided.Modifiers?.ContextValues is { Count: 1 });

        return results;
    }
}
