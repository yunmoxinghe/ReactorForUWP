using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.ApplicationModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Core;
using Windows.UI.Xaml.Media;
using Reactor.Template.Services;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Template;

/// <summary>
/// 应用外壳 + 主页 + 设置页，一比一复刻 <c>UWP-Blank-Template</c> 的
/// <c>MainPage.xaml</c> + <c>Pages/HomePage.xaml</c> + <c>Pages/SettingsPage.xaml</c>。
/// </summary>
/// <remarks>
/// 与 XAML 版本的对应关系：
/// <list type="bullet">
/// <item>页面切换：XAML 用 <c>Frame.Navigate(typeof(Page))</c> + 返回栈，
///       这里用 <see cref="UseState{T}"/> 保存页面栈，等价且可回溯。</item>
/// <item>标题栏：<c>CoreApplicationView.TitleBar.ExtendViewIntoTitleBar</c> +
///       <c>Window.Current.SetTitleBar</c> → 元素上的 <c>.TitleBar()</c>，
///       扩展开关在 <see cref="App"/> 里打开。</item>
/// <item>节标题样式：<c>BasedOn BodyStrongTextBlockStyle + Margin</c> →
///       <see cref="StyleSheet.Define"/>（纯代码构造 Style）。</item>
/// </list>
/// </remarks>
public sealed class MainPage : Component
{
    private const string PageHome = "home";
    private const string PageSettings = "settings";

    /// <summary>
    /// 内容块最大宽度（对齐模板 <c>SettingsPage.xaml</c> 的 <c>MaxWidth="1000"</c>）。
    /// </summary>
    /// <remarks>
    /// 模板的限宽就是 <c>StackPanel MaxWidth="1000" HorizontalAlignment="Stretch"
    /// Margin="20,0,20,36"</c>——<b>没有</b>任何宽度绑定：Stretch 撑满可用宽、
    /// MaxWidth 再把它夹到 1000，是布局系统自己的事。
    /// 主页则<b>完全不限宽</b>（<c>HomePage.xaml</c> 只有 Center/Center 的 StackPanel）。
    /// </remarks>
    private const double ContentMaxWidth = 1000;

    // 大小写统一写成 <c>ReactorForUWP</c>（仓库真名，见 <c>.git/config</c> 的 remote 与
    // csproj 的 RepositoryUrl）。GitHub 的 URL 大小写不敏感，写错也点得开，
    // 但这份 Template 是给人照抄的——抄走三个不一致的地址最省事的解释永远是
    // "仓库搬家了"，而不是"有人手抖"。
    private const string RepoUrl = "https://github.com/yunmoxinghe/ReactorForUWP";
    private const string NugetUrl = "https://www.nuget.org/packages/Reactor.Uwp";
    private const string FeedbackUrl = "https://github.com/yunmoxinghe/ReactorForUWP/issues";

    /// <summary>
    /// 应用名：<b>一律自己按名字查资源表</b>（resw 里的 <c>AppDisplayName</c>），
    /// 不用 <c>Package.Current.DisplayName</c> 当来源。
    /// </summary>
    /// <remarks>
    /// 清单的 <c>uap:VisualElements/@DisplayName</c> 是 <c>ms-resource:AppDisplayName</c>
    /// （外壳/开始菜单走这条，实测解析正确），但 <c>Package.Current.DisplayName</c>
    /// 读的是 <c>&lt;Properties&gt;/&lt;DisplayName&gt;</c> 那条链 —— 它走 AppModel 的
    /// 实时 ms-resource 解析，实测在本包上会<b>解析到错误的资源</b>（拿到的是
    /// <c>SoundGroup/Text</c> 的值「声音」）。按名字查资源表则始终正确。
    /// 所以这里资源表优先，取不到才退回清单属性（清单里该项已写字面量）。
    /// </remarks>
    private static readonly string AppName = ResolveAppName("Reactor 模板");

    /// <summary>
    /// 应用图标。<c>Package.Current.Logo</c> 交出来的是
    /// <c>file:///&lt;安装目录&gt;/Assets/…</c>，也可能是清单里那个带反斜杠的
    /// 相对路径（<c>Assets\StoreLogo.png</c>）；两种都<b>原样</b>往下传。
    /// </summary>
    /// <remarks>
    /// 官方给包内资源的通道是 <c>ms-appx:///相对路径</c>，而这里拿到的是
    /// <c>file:///</c> —— 映射由<b>框架</b>做：alpha.5 起 <c>Image</c> 的
    /// <c>Source</c> 与 <c>BitmapIcon</c> 的 <c>UriSource</c> 都过
    /// <c>Internal/PackUri.cs</c>（顺带把反斜杠换成正斜杠、补齐三斜杠）。
    /// 之前这里有一份就地实现，是因为引用的老包里没那段逻辑；包升上来后就该删，
    /// 留着只会变成"两处各改一半"的坑。
    /// </remarks>
    private static readonly string AppLogo = ReadPackage(
        p => p.Logo?.ToString(),
        "Assets/StoreLogo.png");
    private static readonly string Version = ReadPackage(
        p => $"{p.Id.Version.Major}.{p.Id.Version.Minor}.{p.Id.Version.Build}.{p.Id.Version.Revision}",
        "1.0.0.0");
    private static readonly string Publisher = ReadPackage(p => p.PublisherDisplayName, "yunmoxing");

    /// <summary>
    /// 自检模式下把两个 <c>SettingsExpander</c> 初始展开。
    /// </summary>
    /// <remarks>
    /// 主题 / 材质的 <c>RadioButtons</c> 放在展开器的<b>折叠区</b>里，不展开就不进可视树，
    /// 自检在树上找不到它们，等于只测了最简单的那个控件。而"点了没反应"的病根
    /// 恰恰在 <c>RadioButtons</c> 上：真人点一下它<b>发两发</b>事件（先"取消选中"的
    /// -1，再真正的新值），<c>ComboBox</c> 只发一发——一发的手势复现不出这个病。
    /// <para>
    /// 只在"自检开着 + 起始页被覆盖成设置页"时展开：两者同时成立才可能是无人值守的
    /// 自检跑，正常使用（没有 probe-page.txt）完全是折叠的，页面观感不变。
    /// </para>
    /// </remarks>
    private static readonly bool ExpandForSelfTest = false;

    public override Element Render()
    {
        DefineStyles();

        // 四项设置都从持久化里取初值：模板的 SettingsPage.LoadUI() 干的就是
        // 把 SettingsManager 的值读回控件——只留在内存里的话，重启就打回默认值。
        var saved = AppSettings.Current;

        // 页面栈（XAML 版本是 Frame 的 BackStack）
        //
        // 起始页可以被 LocalState\probe-page.txt 覆盖成 settings：自检要在真控件上
        // 点，而受控控件只在设置页上；靠人手动点进去的话，"这次到底点了没有"本身
        // 就成了待验证项——日志里连一行回调都没有时，分不清是"没点"还是"点了没到"。
        // 文件不存在就照旧从主页起，正常使用时这条路径完全不参与。
        var (stack, setStack) = UseState(new[] { Probe.StartPage ?? PageHome });
        var (themeIndex, setThemeIndex) = UseState((int)saved.Theme);
        var (materialIndex, setMaterialIndex) = UseState((int)saved.Material);
        var (paneIndex, setPaneIndex) = UseState((int)saved.Pane);
        var (soundOn, setSoundOn) = UseState(saved.Sound);
        var (windowActive, setWindowActive) = UseState(true);
        // 诊断条的手动刷新。Probe 是静态类，它内部的变化不会自己催一帧，
        // 所以留一个只用来"再渲染一次"的计数器。
        var (tick, setTick) = UseState(0);
        // 诊断条的展开状态：它压在内容底部，挡住设置页最后两项时先收起来，
        // 别让"用来排查的东西"自己变成"点不到"的原因。
        // 默认收起：展开态有 ~250px，会压住设置页底部的项。收起态只留一行
        // 「命中 N ｜ 最近一跳」，既挡不住操作，又随时能回答"回调到没到"。
        var (barOpen, setBarOpen) = UseState(false);

        var current = stack[^1];
        var canGoBack = stack.Length > 1;

        void Navigate(string target)
        {
            Probe.Hit($"① 回调 navigate={target}（当前 {stack[^1]}）");

            if (string.Equals(stack[^1], target, StringComparison.Ordinal))
            {
                Probe.Hit("　 └ 与当前页相同，提前返回（这一跳是设计如此，不是丢事件）");
                return;
            }

            setStack(stack.Append(target).ToArray());
            Probe.Hit($"② setState 栈深 {stack.Length + 1}");
        }

        void GoBack()
        {
            Probe.Hit($"① 回调 返回（栈深 {stack.Length}）");

            if (stack.Length > 1)
            {
                setStack(stack[..^1].ToArray());
                Probe.Hit($"② setState 栈深 {stack.Length - 1}");
            }
        }

        // 改一项 = 改 UI state + 落盘（模板是 SettingsManager 的 setter 里直接 Save）。
        // 这里不需要额外通知外壳：state 就在本组件里，改了自然会重渲染整棵树，
        // 主题 / 材质 / 导航栏位置都是本组件的属性。
        //
        // Probe.Hit 的三跳就是"点击之后"的完整因果链：
        //   ① 回调（本函数被调用，值来自控件）
        //   ② setState（值交给框架）
        //   ③ 落盘 + 读回（值真的写进了设置）
        // 之后 ④ 渲染（Render 里的 Probe.Render）与 ⑤ 受控下发（框架日志 Patch 通道）
        // 各自记一跳。链断在哪一环，看诊断条上最后一行停在哪就知道。
        void ChangeTheme(int v)
        {
            Probe.Hit($"① 回调 theme={v}（旧 {themeIndex}）");
            setThemeIndex(v);
            Probe.Hit($"② setState theme={v}");
            AppSettings.Update(x => x.Theme = (AppTheme)v);
            Probe.Hit($"③ 落盘 Theme={(AppTheme)v} → 读回 {AppSettings.Current.Theme}");
        }

        void ChangeMaterial(int v)
        {
            Probe.Hit($"① 回调 material={v}（旧 {materialIndex}）");
            setMaterialIndex(v);
            Probe.Hit($"② setState material={v}");
            AppSettings.Update(x => x.Material = (AppMaterial)v);
            Probe.Hit($"③ 落盘 Material={(AppMaterial)v} → 读回 {AppSettings.Current.Material}");
        }

        void ChangePane(int v)
        {
            Probe.Hit($"① 回调 pane={v}（旧 {paneIndex}）");
            setPaneIndex(v);
            Probe.Hit($"② setState pane={v}");
            AppSettings.Update(x => x.Pane = (PanePosition)v);
            Probe.Hit($"③ 落盘 Pane={(PanePosition)v} → 读回 {AppSettings.Current.Pane}");
        }

        // 声音这一项除了改 state + 落盘，还要把值推给 XAML 的元素音效开关
        // （模板：SoundToggle_Toggled → MainPage.ApplySettings）。
        // 注意这里不写"启动时设置一次"的 UseEffect——那已经在 App.OnLaunched 里做过；
        // 放到 Render 里是"UI 建完之后才设"，时机无效（见 Services/ElementSound.cs）。
        void ChangeSound(bool v)
        {
            Probe.Hit($"① 回调 sound={v}（旧 {soundOn}）");
            setSoundOn(v);
            Probe.Hit($"② setState sound={v}");
            AppSettings.Update(x => x.Sound = v);
            Probe.Hit($"③ 落盘 Sound={v} → 读回 {AppSettings.Current.Sound}");
            ElementSound.Apply(v);
        }

        // 挂载后再设一次。参考模板设了两处：App.OnLaunched（启动时）+
        // MainPage.ApplySettings（Loaded 时）——这里 deps 为空，只在挂载那一次跑，
        // 等价于 Loaded。早的那次在 UI 建好之前，晚的那次兜底；
        // "有些控件进页面时不响、拨一下开关才响"就是少了这一次。
        UseEffect(() => ElementSound.Apply(soundOn));

        // 窗口失焦时标题栏应用名淡到 0.5（模板 MainPage_CoreWindowActivated）。
        // deps 为空 = 只在挂载时订阅一次，cleanup 里退订。
        UseEffect(() =>
        {
            var coreWindow = CoreWindow.GetForCurrentThread();
            void OnActivated(CoreWindow sender, WindowActivatedEventArgs args) =>
                setWindowActive(args.WindowActivationState != CoreWindowActivationState.Deactivated);

            coreWindow.Activated += OnActivated;
            return () => coreWindow.Activated -= OnActivated;
        });

        var menuItems = new[] { new NavigationViewItemData("主页", "\uE80F", PageHome) };

        // 内容区：面包屑（仅设置页显示）+ 当前页
        // 对照模板 MainPage.xaml：外层 StackPanel Spacing=8 / Padding="24,16,24,16"，
        // 里面的 BreadcrumbBar 用 ItemTemplate = TextBlock + TitleTextBlockStyle
        // （28px Semibold，样式自带字号，所以这里不再叠一个 ItemFontSize）。
        // 现在是真 WinUI 2 BreadcrumbBar：分隔符、条目按钮由官方模板给，
        // 条目外观走官方 ItemTemplate（XamlReader.Load 出来的 DataTemplate）——
        // 别改成在 BreadcrumbBar 上设 FontSize，也别把 TextBlock 当 item 喂进
        // ItemsSource（后者首次布局就崩 0x800F1000，见 BreadcrumbBarHandler 注释）。
        var content = Grid(
            new[] { GridSize.Star() },
            new[] { GridSize.Auto, GridSize.Star() },
            HStack(8, BreadcrumbBar(
                    new[] { "设置" },
                    itemStyleKey: "TitleTextBlockStyle"))
                .Padding(24, 16, 24, 16)
                .VAlign(VerticalAlignment.Center)
                .IsVisible(current == PageSettings)
                .Grid(row: 0),
            // 传进去的是"改 state + 落盘"的版本，不是裸 setter。
            Frame(current == PageSettings
                    ? SettingsBody(themeIndex, ChangeTheme, materialIndex, ChangeMaterial,
                        paneIndex, ChangePane, soundOn, ChangeSound, ExpandForSelfTest)
                    : HomeBody(),
                // 模板的 ContentFrame 用 Frame.Navigate 的默认过渡；这里是换 Content，
                // 过渡由 Frame.ContentTransitions 提供（见 FrameElement 注释）。
                transition: PageTransition.Entrance,
                // 页面栈深度：返回时（深度变小）走返回方向的过渡，并播
                // ElementSoundKind.GoBack——XAML 版本返回是 ContentFrame.GoBack()，
                // 有反向过渡 + 专属返回音；不传这个就一律按"进入下一页"处理，
                // 返回的观感和声音都跟 XAML 对不上。
                stackDepth: stack.Length)
                .Grid(row: 1));

        // 一帧的 state 快照：交给 Probe 去重后记第 ④ 跳。
        var snapshot = $"theme={themeIndex} mat={materialIndex} " +
                       $"pane={paneIndex} sound={soundOn} page={current}";
        Probe.Render(snapshot);

        return Group(
            // ── 标题栏拖拽区（32px，透明）─────────────────────────
            Grid(
                new[] { GridSize.Star() },
                new[] { GridSize.Star() },
                // 模板 MainPage.xaml 的标题栏：Image 16x16 Margin="0,0,12,0"
                // + 应用名 FontSize=12（CaptionTextBlockStyle 就是 12px）。
                HStack(
                    Image(AppLogo).Size(16, 16).Margin(right: 12).VAlign(VerticalAlignment.Center),
                    TextBlock(AppName)
                        .Caption()
                        .VAlign(VerticalAlignment.Center)
                        // 失焦时淡到 0.5（模板 CoreWindow.Activated 里改的就是这个）。
                        .Opacity(windowActive ? 1.0 : 0.5)
                ).Margin(16, 0, 16, 0).VAlign(VerticalAlignment.Center)
            ).Height(32)
             .Background(new SolidColorBrush(Colors.Transparent))
             .VAlign(VerticalAlignment.Top)
             .TitleBar(),

            // ── 主内容区（避开标题栏 32px）────────────────────────
            Grid(
                new[] { GridSize.Star() },
                new[] { GridSize.Star() },
                NavigationView(
                    content,
                    menuItems,
                    // 对齐 MainPage.xaml.cs 的 ApplySettings()：
                    // s.PanePosition == "Top" ? Top : Left —— 另一侧是 Left 不是 Auto。
                    // Auto 会在窗口变窄时自己折成紧凑面板，内容区左边缘跟着动，
                    // 看上去就是"内容没对齐"；Left 是恒定的。
                    paneDisplayMode: paneIndex == 1 ? NavPaneDisplayMode.Top : NavPaneDisplayMode.Left,
                    selectedIndex: current == PageSettings ? -1 : 0,
                    onItemInvoked: index => Navigate(index < 0 ? PageSettings : PageHome),
                    onBackRequested: GoBack,
                    isBackButtonVisible: true,
                    isBackEnabled: canGoBack,
                    isSettingsVisible: true
                ).AutomationId("MainNavigationView")
            ).Margin(0, 32, 0, 0),

            // ── 诊断条（默认不挂）──────────────────────────────
            // 挂上去会压住内容底部，而设置页最后两项正好在那儿 → "用来排查的东西"
            // 自己变成"点不到"的原因，比原病更难判断。默认走纯日志（见 Probe.LogPath），
            // 要屏上读数时把 Probe.ShowBar 改成 true。null 会被 Group 过滤掉。
            Probe.ShowBar
                ? DiagnosticBar(snapshot, barOpen, () => setBarOpen(!barOpen), () => setTick(tick + 1))
                : null
        )
        .OwnsTitleBar()
        .Theme(themeIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default })
        .Backdrop(materialIndex == 1 ? BackdropKind.DesktopAcrylic : BackdropKind.Mica);
    }

    // ── 主页 ──────────────────────────────────────────────────

    /// <remarks>
    /// 严格对照模板 <c>Pages/HomePage.xaml</c>：外层 <c>Grid</c>，里面一层
    /// <c>StackPanel HorizontalAlignment="Center" VerticalAlignment="Center"
    /// Spacing="12"</c>，最里面是标题与按钮（各自也写了 <c>Center</c>）。
    /// 模板主页<b>没有</b>限宽层，这里同样不加。
    /// </remarks>
    private static Element HomeBody() =>
        Group(
            VStack(12,
                // .Uid 对应 XAML 的 x:Uid：挂载时按 Uid/Property 查
                // Strings/<语言>/Resources.resw 并覆盖这里的文本（见 README）。
                // 标识符照抄参考实现（HomePageWelcomeText），不要自己另起一套：
                // 资源标识符一改，对翻译团队就等于删旧条目 + 加新条目。
                TextBlock("欢迎来到主页，这是一个 UWP 模板应用")
                    .Uid("HomePageWelcomeText")
                    .FontSize(24)
                    .HAlign(HorizontalAlignment.Center),
                Button("查看Github仓库", () => OpenLink(RepoUrl))
                    .Width(180)
                    .HAlign(HorizontalAlignment.Center)
                    .Accent())
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center));

    // ── 设置页 ────────────────────────────────────────────────

    /// <remarks>
    /// 布局严格对照模板 <c>Pages/SettingsPage.xaml</c>：
    /// <list type="bullet">
    /// <item><c>ScrollViewer Padding="20,20,20,0"</c> + 关掉横向滚动。</item>
    /// <item><c>Grid MaxWidth="1200"</c>：里面那层 <c>StackPanel Spacing="32"</c>
    ///       是<b>三个分组之间</b>的唯一间隔来源（"外观 / 声音 / 关于"）。</item>
    /// <item>每个分组是 <c>StackPanel Spacing="3"</c>，组标题 14px SemiBold + 下边距 6。</item>
    /// <item>每张卡片 <c>MinHeight="70"</c>，且<b>撑满限宽容器</b>——模板每张
    ///       <c>Expander</c> / <c>SettingsCard</c> 都显式写了
    ///       <c>HorizontalAlignment="Stretch"</c>（已下沉为卡片类元素的默认行为：
    ///       少了它卡片只按内容宽收缩，在 1200 的容器里靠左堆着，观感就是"限宽没生效"）。</item>
    ///       <b>SettingsCard 必须显式给上下的 Padding</b>（这里用 16,16,16,16）——
    ///       模板抄来的 <c>Padding="16,0"</c> 在窄宽度下没有上下呼吸空间（见下）。</item>
    /// </list>
    /// <b>坑（已修）</b>：组间距不能靠"样式里的 Margin + 本地 Margin"叠加——
    /// 依赖属性优先级里本地值压过样式值，两者写同一个属性时样式的 32px 会被整个吃掉，
    /// 只剩 StackPanel 的 3~4px，看上去就是"卡片组之间没间隔了"。
    /// </remarks>
    /// <param name="expandGroups">
    /// 初始展开两个分组。正常是 <c>false</c>（折叠，与模板一致）；
    /// 只在自检模式下由 <see cref="MainPage.ExpandForSelfTest"/> 传 <c>true</c>，
    /// 好让折叠区里的 <c>RadioButtons</c> 进可视树、被自检点得到。
    /// </param>
    private static Element SettingsBody(
        int themeIndex, Action<int> setThemeIndex,
        int materialIndex, Action<int> setMaterialIndex,
        int paneIndex, Action<int> setPaneIndex,
        bool soundOn, Action<bool> setSoundOn,
        bool expandGroups = false) =>
        ScrollViewer(
            // Grid 包裹 StackPanel（与 XAML 版本一致：避免展开/折叠时的布局抖动）
            Group(
                // 模板就是一个 StackPanel（Spacing=4），节标题与卡片混排：
                // 组间距来自标题的 Margin(0,32,0,8)（首组为 0,0,0,8），不是嵌套 StackPanel。
                VStack(4,
                    // ── 外观 ──────────────────────────────────
                    SectionHeader("外观", uid: "AppearanceGroup", isFirst: true),

                    SettingsExpander(
                        header: "应用主题",
                            description: "选择要显示的应用主题",
                            headerIcon: FontIcon("\uE706", fontSize: 20),
                            // 正常折叠；自检模式展开，好让 RadioButtons 进可视树（见 expandGroups）。
                            isExpanded: expandGroups,
                            items: new Element?[]
                            {
                                SettingsCard(
                                    content: RadioButtons(
                                        new[] { "跟随系统", "浅色", "深色" },
                                        Optional<int>.Of(themeIndex),
                                        setThemeIndex)
                                        // 模板里 RadioButtons 是 Padding="12,0,0,12"：
                                        // 左缩进不需要（内容已与 Header 同列），底部 12 是
                                        // 展开区与卡片下边缘的呼吸空间，别省。
                                        .Padding(0, 0, 0, 12),
                                    contentAlignment: SettingsCardContentAlignment.Left),
                            })
                            .MinHeight(70),

                        SettingsExpander(
                            header: "背景材质",
                            description: "选择要显示的应用材质",
                            headerIcon: FontIcon("\uE2B1", fontSize: 20),
                            isExpanded: expandGroups,
                            items: new Element?[]
                            {
                                SettingsCard(
                                    content: RadioButtons(
                                        new[] { "Mica", "Acrylic" },
                                        Optional<int>.Of(materialIndex),
                                        setMaterialIndex)
                                        .Padding(0, 0, 0, 12),
                                    contentAlignment: SettingsCardContentAlignment.Left),
                            })
                            .MinHeight(70),

                    SettingsCard(
                        header: "导航栏位置",
                        description: "选择要显示的导航栏位置",
                        headerIcon: FontIcon("\uF594", fontSize: 20),
                        content: ComboBox(
                            new[] { "左侧", "顶部" },
                            Optional<int>.Of(paneIndex),
                            setPaneIndex)
                            // 模板写的是 MinWidth="160"（不是固定 Width）：
                            // 条目文字变长时允许自己撑开，不会被裁掉。
                            .MinWidth(160)
                            .HAlign(HorizontalAlignment.Right))
                        .MinHeight(70)
                        .Padding(16, 16),

                    // ── 声音 ──────────────────────────────────
                    SectionHeader("声音", uid: "SoundGroup"),

                    SettingsCard(
                        header: "控件声音",
                        description: "控制应用中的交互提示音",
                        headerIcon: FontIcon("\uE767", fontSize: 20),
                        content: ToggleSwitch(
                            Optional<bool>.Of(soundOn),
                            setSoundOn).HAlign(HorizontalAlignment.Right))
                        .MinHeight(70)
                        .Padding(16, 16),

                    // ── 关于 ──────────────────────────────────
                    SectionHeader("关于", uid: "AboutGroup"),

                    SettingsExpander(
                        header: AppName,
                        // 不要给 BitmapIcon 设 Width / Height：它不像 Image 那样把位图
                        // 缩放适配，设了尺寸就是把 106px 的原图裁出一个角，看着就像
                        // "图标缩没了"。原版（UWP-Blank-Template 的
                        // <BitmapIcon ShowAsMonochrome="False"/>）同样不给尺寸，
                        // 交给宿主（卡片的图标呈现器）去缩放。
                        headerIcon: BitmapIcon(AppLogo, showAsMonochrome: false),
                        description: $"©{DateTime.Now.Year} {Publisher}。保留所有权利。",
                        content: TextBlock(Version)
                            .Foreground(ThemeResource.Brush("TextFillColorSecondaryBrush")),
                        items: new Element?[]
                        {
                            SettingsCard(
                                // 模板的 StackPanel 写了 Spacing="8"，两个 HyperlinkButton
                                // 之间靠这 8px 分开（不是 0）。
                                // 名字要显式给：内容是 TextBlock 时，UIA 的 Name 回退
                                // 取不进更深一层（StackPanel 里的 TextBlock 更是完全取不到），
                                // 于是这个按钮对屏幕阅读器和自动化脚本来说是<b>无名</b>的。
                                content: VStack(8,
                                    HyperlinkButton(TextBlock("GitHub 仓库"), () => OpenLink(RepoUrl))
                                        .AutomationName("GitHub 仓库"),
                                    HyperlinkButton(TextBlock("NuGet 包页"), () => OpenLink(NugetUrl))
                                        .AutomationName("NuGet 包页")),
                                contentAlignment: SettingsCardContentAlignment.Left),
                        })
                        .MinHeight(70),

                    // 图标放在 HyperlinkButton **里面**：整块（图标+文字）都是热区，
                    // 图标也会跟着变下划线/变色。模板把 FontIcon 放在按钮外面
                    // （StackPanel 里并列），点图标是没有任何反应的。
                    HyperlinkButton(
                        HStack(8,
                            FontIcon("\uED15", fontSize: 20).VAlign(VerticalAlignment.Center),
                            TextBlock("问题反馈").VAlign(VerticalAlignment.Center)),
                        () => OpenLink(FeedbackUrl))
                        .AutomationName("问题反馈")
                        .Padding(0)
                        .HAlign(HorizontalAlignment.Left)
                        .VAlign(VerticalAlignment.Center)
                        // 模板：Margin="0,8,0,0"
                        .Margin(0, 8, 0, 0)
                )
                // 模板 StackPanel 的三件套：<c>MaxWidth="1000"</c> +
                // <c>HorizontalAlignment="Stretch"</c> + <c>Margin="20,0,20,36"</c>。
                // 限宽完全交给布局系统（Stretch 撑满 → MaxWidth 夹窄），不需要任何宽度绑定。
                .MaxWidth(ContentMaxWidth)
                .HAlign(HorizontalAlignment.Stretch)
                .Margin(20, 0, 20, 36)),
            // 关键：横向滚动关掉。只要横向可滚动，ScrollViewer 就用无限宽测量内容，
            // 卡片栈的宽度会随"最宽的子项"漂移——折叠 ~220px、展开瞬间撑到上限。
            horizontalScrollBar: ScrollBarVisibility.Disabled,
            horizontalScroll: ScrollMode.Disabled,
            // 模板 SettingsPage.xaml 的 ScrollViewer 没有 Padding（左右 20 由 StackPanel
            // 的 Margin 提供），这里同样不加。
            horizontalContent: HorizontalAlignment.Stretch);

    /// <summary>
    /// 底部诊断条：<b>把「点击之后每一跳」摆在屏幕上</b>，不依赖翻日志文件。
    /// </summary>
    /// <remarks>
    /// 布局上它压在内容底部（<c>VAlign=Bottom</c> + 半透明深底），所以给了"收起"：
    /// 挡住要点的设置项时先收起来——用来排查的东西不能自己变成"点不到"的原因。
    /// <para>
    /// 读数怎么看：<b>先盯"命中"那个数</b>。
    /// <list type="bullet">
    ///   <item>点完它<b>不涨</b>：事情断在 控件 → 闸门 → 回调 那一半，页面代码没跑到。
    ///         这时看下面 <c>Input</c> 那一段——框架会把"为什么吞"写在那儿
    ///         （回声 / 未就绪 / 越界不下发）。</item>
    ///   <item>它<b>涨了</b>而界面没变：断在 setState 之后。看本页链有没有第 ④ 跳
    ///         （没有 = 回调之后没重渲染），有则看 <c>Patch</c> 段有没有"受控下发"
    ///         （没有 = 受控值被策略挡回）。</item>
    /// </list>
    /// </para>
    /// </remarks>
    private static Element DiagnosticBar(string snapshot, bool open, Action toggle, Action refresh) =>
        Grid(
            new[] { GridSize.Star() },
            new[] { GridSize.Auto, GridSize.Auto },
            // 正文十几行，套一层 ScrollViewer：不套的话 MaxHeight 会把最想看的
            // 那几行裁掉，而裁哪几行由高度决定、不由重要性决定。
            ScrollViewer(
                // 收起时只留一行摘要：命中数 + 最近一跳。够判断"回调到没到"。
                TextBlock(open ? Probe.Report(snapshot) : $"命中 {Probe.Hits} ｜ 最近：{Probe.Last}")
                    .FontSize(11)
                    .Foreground(new SolidColorBrush(Colors.White))
                    .Margin(8, 6, 8, 0))
                .MaxHeight(open ? 240 : 30)
                .Grid(row: 0),
            HStack(8,
                Button(open ? "收起" : "展开", toggle),
                Button("清空", () => { Probe.Reset(); refresh(); }),
                Button("刷新", refresh))
                .Margin(8, 6, 8, 8)
                .Grid(row: 1))
        .Background(new SolidColorBrush(Color.FromArgb(228, 0, 0, 0)))
        .HAlign(HorizontalAlignment.Stretch)
        .VAlign(VerticalAlignment.Bottom)
        .MaxHeight(open ? 320 : 100);

    /// <summary>
    /// 组标题：14px SemiBold + 下边距 8（模板里节标题的写法）。
    /// </summary>
    /// <remarks>
    /// <b>边距写本地值是刻意的，样式里不再抄一遍。</b>这里要按 <c>isFirst</c> 给两套值
    /// （首组去掉顶部 32），而依赖属性优先级中<b>本地值高于样式值</b>——样式里一旦也写
    /// <c>Margin</c>，那份就成了永远读不到的死数：有人改它，界面纹丝不动，
    /// 排查时只会奔着"没生效"去找别的解释。单一事实来源放在这里。
    /// </remarks>
    private static Element SectionHeader(string text, string? uid = null, bool isFirst = false) =>
        TextBlock(text)
            .Uid(uid ?? string.Empty)
            .ApplyStyle("SettingsSectionHeaderTextBlockStyle")
            // 模板给第一个标题单独写了 Margin="0,0,0,8"（去掉顶部 32）；
            // 其余标题在它上方留出 32 的组间距。
            .Margin(isFirst ? new Thickness(0, 0, 0, 8) : new Thickness(0, 32, 0, 8));

    /// <summary>
    /// 节标题样式：只负责在 <c>BodyStrongTextBlockStyle</c> 上叠字重，<b>不含边距</b>
    /// （对齐模板 <c>SettingsSectionHeaderTextBlockStyle</c>，重复定义以先定义者为准）。
    /// </summary>
    /// <remarks>
    /// 边距由 <see cref="SectionHeader"/> 给本地值，理由写在那里：<b>同一个属性不要两处都写</b>。
    /// SettingsBody 的分组之间同理：组间距来自标题的 Margin，不靠"样式 Margin + 本地 Margin"相叠。
    /// </remarks>
    private static void DefineStyles()
    {
        StyleSheet.Define("SettingsSectionHeaderTextBlockStyle", b => b
            .Target<TextBlock>()
            .BasedOn("BodyStrongTextBlockStyle"));
    }

    private static async void OpenLink(string url)
    {
        // 与模板一致：打开外链前先弹确认框（Dialogs/ExternalOpenDialog），
        // 主按钮"是"才真正跳转；默认聚焦"否"。
        var confirmed = await ReactorDialog.ShowAsync(
            ContentDialog(
                title: "此应用尝试打开外部应用",
                message: "是否允许？",
                primaryButtonText: "是",
                secondaryButtonText: "否",
                defaultButton: ContentDialogButton.Secondary,
                messageFontSize: 14));

        if (confirmed != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            Reactor.Uwp.Hosting.ReactorApplication.Trace($"[app] 打开外链失败: {url} - {ex.Message}");
        }
    }

    /// <summary>
    /// 应用名：清单里是 <c>ms-resource:AppDisplayName</c>，<c>Package.Current.DisplayName</c>
    /// 由运行时解析成本地化字符串；万一它没解析（未打包运行、PRI 没跟上）会原样返回
    /// <c>ms-resource:...</c>，这时按官方做法自己查资源表兜底，而不是把原始引用串
    /// 显示给用户。
    /// </summary>
    private static string ResolveAppName(string fallback)
    {
        // 资源表优先：应用名的权威来源就是 resw 里的 AppDisplayName
        // （清单的 ms-resource:AppDisplayName 指向同一个键，系统也是查它）。
        // 先查资源可以彻底避开"系统没解析出 ms-resource、原样返回引用串"这一档。
        if (ReadLocalized("AppDisplayName") is { } localized)
        {
            return localized;
        }

        // 资源没跟上（未打包 / PRI 缺失）时才退回清单属性，且同样挡掉未解析的引用串。
        var name = ReadPackage(p => p.DisplayName, string.Empty);

        return !string.IsNullOrWhiteSpace(name) &&
               !name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            ? name
            : fallback;
    }

    /// <summary>按资源标识符查字符串；没这条资源返回 <c>null</c>。</summary>
    private static string? ReadLocalized(string key)
    {
        try
        {
            // GetForViewIndependentUse 而不是 GetForCurrentView：后者要求当前线程
            // 有 CoreWindow，静态初始化阶段调用会直接抛。
            var value = Windows.ApplicationModel.Resources.ResourceLoader
                .GetForViewIndependentUse()
                .GetString(key);

            // 键不存在时 GetString 返回空串（不抛）。
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ReadPackage(Func<Package, string?> read, string fallback)
    {
        try
        {
            return read(Package.Current) ?? fallback;
        }
        catch (Exception)
        {
            // 未打包运行（如调试部署失败）时 Package.Current 会抛异常。
            return fallback;
        }
    }
}
