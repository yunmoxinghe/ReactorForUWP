// 测试外壳：所有手动测试都挂在这个 NavigationView 下面，点一下就切页。
//
// 为什么要有它：之前每换一个对照都要改 App.cs 的根组件（后来改成改
// LocalState\probe-mode.txt），再重新编译 + 重新部署一轮，六种 MODE 光切模式就要
// 六次部署；实验室页和演示页更是只能靠改代码轮换。现在它们全是菜单里的普通项，
// 进页面即跑，页面上还有「重跑 / 停止」，一次部署就能把所有对照跑完。
//
// 菜单前 6 项与压测 MODE 一一对应，diag-run.ps1 -Mode N 落到的就是第 N 页
// （仍然保留「不改代码切换」的能力，但真人测试时直接在界面上点即可）。
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

public sealed class TestShellApp : Component
{
    /// <summary>一个测试页：菜单名 + 页面构建函数。数组下标即菜单项顺序。</summary>
    private sealed record TestCase(string Name, Func<Element> Build);

    private static readonly TestCase[] Cases = new[]
    {
        new TestCase("M0 XAML 基线", () => Probe(0)),
        new TestCase("M1 池化+折叠", () => Probe(1)),
        new TestCase("M2 不复用", () => Probe(2)),
        new TestCase("M3 裸桥不折叠", () => Probe(3)),
        new TestCase("M4 裸桥折叠", () => Probe(4)),
        new TestCase("M5 最小回调", () => Probe(5)),
        new TestCase("虚拟列表实验室", () => Component<VirtualListLabPage>()),
        new TestCase("Echo 实验室", () => Component<EchoLabPage>()),
        new TestCase("CoreLoop 回归", () => Component<CoreLoopRegression>()),
        new TestCase("元素画廊", () => Component<ElementGallery>()),
        new TestCase("A4 演示", () => Component<A4Demo>()),
        new TestCase("CoreLoop 演示", () => Component<CoreLoopDemo>()),
        new TestCase("Blank 模板页", () => Component<BlankTemplateApp>()),
        new TestCase("XAML/代码 控件对照", () => Component<XamlDiffProbe>()),

        // 慢滚对照：与 M1 / M0 同样的控件路径，只是每 tick 前进 2 项而不是几百项。
        // 用来判定"快速滚动闪烁"的来源——慢滚不闪 = 压测自己的大跨步所致。
        new TestCase("M1 慢滚对照", () => Probe(1, smooth: true)),
        new TestCase("M0 慢滚对照", () => Probe(0, smooth: true)),

        // 进设置页复现：冷启动自动点进 Blank 模板的设置页。用来无人值守验证
        // "Frame.Navigate + Toolkit SettingsCard + 面包屑"整条路径不会崩。
        new TestCase("设置页复现", () => Component<SettingsNavProbe>()),

        // 原生对照：不受控的裸 RadioButtons。与受控页做单变量 A/B，
        // 判定"连点在第 3 次起失效"的病灶在受控逻辑还是 WinUI 内部。
        new TestCase("原生 RadioButtons 对照", () => Component<NativeRadioLabPage>()),
        new TestCase("纯原生 RadioButtons（无 handler）", () => Component<RawRadioLabPage>()),
        new TestCase("RadioButtons DP 探针", () => Component<RadioDpProbePage>()),

        // 重挂探针：TreeView / HyperlinkButton / SettingsCard 三个 handler 的回调
        // 是不是"恰好一次"。盯两件事——委托里不许拿 sender 查表（点了没反应），
        // 以及 Unmount 必须真解绑（重挂后每次点击都是双份回调）。详见页面注释。
        new TestCase("重挂探针", () => Component<RebindProbePage>()),

        // 裸 sender 对照：同一控件类型，A 侧走 Reactor handler（用捕获的 control 查表），
        // B 侧自己挂事件、委托里故意用 sender 查表。用来给"WinRT 会不会交回另一个
        // 托管包装"这件事一个真机上的二值结论 —— 它决定了重挂探针那三处修复
        // 在真机上到底验不验得出来。详见页面注释。
        new TestCase("裸 sender 对照", () => Component<SenderIdentityProbePage>()),

        // 诊断量具自检：故意造两个反常场面（可同时选中的两个 RadioButton、后台空转
        // 6 秒），让 winapp_diag.py 那两条从未被触发过的告警自己响一次 ——
        // 不验过就不能信它。详见页面注释。
        new TestCase("诊断量具自检", () => Component<DiagFixturePage>()),
    };

    /// <summary>默认落在 M1（原生桥池化 + 折叠）：这是要长期守住的那一档。</summary>
    private const int DefaultIndex = 1;

    private static readonly NavigationViewItemData[] MenuItems =
        Array.ConvertAll(Cases, c => new NavigationViewItemData(c.Name));

    private static Element Probe(int mode, bool smooth = false) =>
        Component<FactoryProbePage, FactoryProbeProps>(
            new FactoryProbeProps(Mode: mode, Smooth: smooth));

    public override Element Render()
    {
        // <c>InitialIndex()</c> 要读一次 <c>LocalState\probe-mode.txt</c>。直接写
        // <c>UseState(InitialIndex())</c> 的话实参<b>每帧都会求值</b>，而 use state
        // 只在挂载那一帧认它——每点一次菜单就白读一次文件（还裹着 try/catch）。
        // 用 <c>UseMemo</c> 把种子钉成"进程生命周期一次"，再交给 use state。
        var seed = UseMemo(InitialIndex);
        var (index, setIndex) = UseState(seed);

        return NavigationView(
            content: Frame(Cases[index].Build()),
            menuItems: MenuItems,
            paneDisplayMode: NavPaneDisplayMode.Left,
            selectedIndex: index,
            onSelectedIndexChanged: i => setIndex(i),
            header: "Reactor · 手动测试");
    }

    /// <summary>
    /// 冷启动落在哪一页：读 <c>LocalState\probe-mode.txt</c>（内容就是一个数字）。
    /// 前 6 项与 MODE 同序，所以写 MODE 就等于选中对应页；没有文件或越界就用缺省页。
    /// </summary>
    private static int InitialIndex()
    {
        try
        {
            var path = System.IO.Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path, "probe-mode.txt");

            if (!System.IO.File.Exists(path))
            {
                return DefaultIndex;
            }

            var text = System.IO.File.ReadAllText(path).Trim();
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                && n >= 0 && n < Cases.Length
                    ? n
                    : DefaultIndex;
        }
        catch
        {
            return DefaultIndex;
        }
    }
}
