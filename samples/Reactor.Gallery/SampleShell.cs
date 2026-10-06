using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Gallery.Pages;
using Windows.UI.Xaml;
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
        new Sample("受控控件诊断", () => Component<DiagnosticsPage>()),
        new Sample("组件 props", () => Component<ComponentPropsPage>()),
        new Sample("原生控件逃生舱", () => Component<NativeInteropPage>()),
    };

    private static readonly NavigationViewItemData[] MenuItems =
        Array.ConvertAll(Samples, s => new NavigationViewItemData(s.Name));

    public override Element Render()
    {
        var (index, setIndex) = UseState(0);

        // 设置状态刻意放在外壳：主题要挂在<b>根元素</b>上才对整个窗口生效，
        // 放在设置页里就只有那一页会变。页面通过 Context 读写这一份。
        var (theme, setTheme) = UseState(SettingsStore.LoadTheme());
        var (autoUpdate, setAutoUpdate) = UseState(SettingsStore.LoadAutoUpdate());
        var (sound, setSound) = UseState(SettingsStore.LoadSound());

        // 落到真实后端（ElementSoundPlayer）是副作用，写在 Render 里不行——
        // Render 必须允许重跑而不产生外部影响。UseEffect 跑在 patch 之后，
        // 且只在 sound 变化时才重跑；首帧跑的那一趟正好充当"启动时恢复上次设置"。
        UseEffect(() => SoundService.Apply(sound), sound);

        var settings = new AppSettings
        {
            Theme = theme,
            AutoUpdate = autoUpdate,
            Sound = sound,

            // 先落盘再 setState：顺序反了的话，万一 setState 触发的重渲染里出异常，
            // 盘上还是旧值，界面却是新的——重启后两边对不上，且没有任何痕迹。
            SetTheme = v =>
            {
                SettingsStore.SaveTheme(v);
                setTheme(v);
            },
            SetAutoUpdate = v =>
            {
                SettingsStore.SaveAutoUpdate(v);
                setAutoUpdate(v);
            },
            SetSound = v =>
            {
                SettingsStore.SaveSound(v);
                setSound(v);
            },
        };

        return NavigationView(
                content: Frame(Samples[index].Build()),
                menuItems: MenuItems,
                paneDisplayMode: NavPaneDisplayMode.Left,
                selectedIndex: index,
                onSelectedIndexChanged: setIndex,
                header: "Reactor.Uwp 示例")
            .Provide(SettingsStore.Settings, settings)
            .Theme(ToElementTheme(theme));
    }

    /// <summary>0 跟随系统 / 1 浅色 / 2 深色。</summary>
    private static ElementTheme ToElementTheme(int theme) => theme switch
    {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
