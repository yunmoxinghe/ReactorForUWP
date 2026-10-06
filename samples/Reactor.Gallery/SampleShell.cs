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
        //
        // 初值走 <c>UseMemo → UseState(seed)</c>，而不是直接
        // <c>UseState(SettingsStore.LoadTheme())</c>：后者的实参<b>每帧都会求值</b>，
        // 而 use state 只在挂载那一帧认它——于是每帧白读三次
        // <c>ApplicationData.Current.LocalSettings</c>（跨 COM 调用，还裹着 try/catch），
        // 产物当场丢弃。在诊断页读帧计数时尤其碍事：这笔开销算在这一帧的账单上，
        // 却没有任何可见产出。跟虚拟化页同一个办法：先把种子钉成"进程生命周期一次"。
        var themeSeed = UseMemo(SettingsStore.LoadTheme);
        var autoUpdateSeed = UseMemo(SettingsStore.LoadAutoUpdate);
        var soundSeed = UseMemo(SettingsStore.LoadSound);

        var (theme, setTheme) = UseState(themeSeed);
        var (autoUpdate, setAutoUpdate) = UseState(autoUpdateSeed);
        var (sound, setSound) = UseState(soundSeed);

        // 落到真实后端（ElementSoundPlayer）是副作用，写在 Render 里不行——
        // Render 必须允许重跑而不产生外部影响。UseEffect 跑在 patch 之后，
        // 且只在 sound 变化时才重跑。
        // <b>这一趟不等于启动恢复</b>：它是"用户拨动开关之后"的那一次，此时树已经建好，
        // 只对之后套用的模板起作用。抢在树建立之前的那一次在 <see cref="App"/> 的
        // OnLaunched 里——少了那趟，刚进界面时显示"开"却没有声音，
        // 拨一下开关才恢复，是个很容易被误判成"抛出没生效"的现象。
        UseEffect(() => SoundService.Apply(sound), sound);

        // 往下发的那个对象也要跟着三个值走，不能每帧新建：它是 <c>class</c>，
        // 引用每次都不同，外壳每重渲染一帧（包括在左边清单里换个页面）
        // 就会让所有 <c>UseContext</c> 的页面白白重渲染一遍。
        // 委托虽然在三个 lambda 里，但它们只调 setState 和静态方法，不捕获上面的值，
        // 所以真正的依赖就是这三个读数。
        var settings = UseMemo(
            () => new AppSettings
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
            },
            theme, autoUpdate, sound);

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
