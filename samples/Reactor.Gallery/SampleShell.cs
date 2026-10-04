using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Gallery.Pages;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery;

/// <summary>
/// 示例外壳：左边是示例清单，右边是 <c>Frame</c>，点一下就切页。
/// </summary>
/// <remarks>
/// 用 <c>Frame</c> 而不是直接替换内容，是为了顺带演示页面切换过渡
/// （<see cref="Microsoft.UI.Reactor.PageTransition.Entrance"/>，默认淡入上移）。
/// </remarks>
public sealed class SampleShell : Component
{
    private sealed record Sample(string Name, Func<Element> Build);

    private static readonly Sample[] Samples = new[]
    {
        new Sample("快速开始", () => Component<GettingStartedPage>()),
        new Sample("输入与选择", () => Component<InputsPage>()),
        new Sample("布局", () => Component<LayoutPage>()),
        new Sample("列表", () => Component<ListsPage>()),
        new Sample("虚拟化长列表", () => Component<VirtualizationPage>()),
        new Sample("设置页", () => Component<SettingsPage>()),
        new Sample("组件 props", () => Component<ComponentPropsPage>()),
        new Sample("原生控件逃生舱", () => Component<NativeInteropPage>()),
    };

    private static readonly NavigationViewItemData[] MenuItems =
        Array.ConvertAll(Samples, s => new NavigationViewItemData(s.Name));

    public override Element Render()
    {
        var (index, setIndex) = UseState(0);

        return NavigationView(
            content: Frame(Samples[index].Build()),
            menuItems: MenuItems,
            paneDisplayMode: NavPaneDisplayMode.Left,
            selectedIndex: index,
            onSelectedIndexChanged: setIndex,
            header: "Reactor.Uwp 示例");
    }
}
