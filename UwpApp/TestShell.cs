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

        // 慢滚对照：与 M1 / M0 同样的控件路径，只是每 tick 前进 2 项而不是几百项。
        // 用来判定"快速滚动闪烁"的来源——慢滚不闪 = 压测自己的大跨步所致。
        new TestCase("M1 慢滚对照", () => Probe(1, smooth: true)),
        new TestCase("M0 慢滚对照", () => Probe(0, smooth: true)),
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
        var (index, setIndex) = UseState(InitialIndex());

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
