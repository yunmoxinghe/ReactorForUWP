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
using UwpApp.Services;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

/// <summary>
/// 用 Reactor 复刻 <c>UWP-Blank-Template</c> 的 MainPage + HomePage + SettingsPage。
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
public sealed class BlankTemplateApp : Component
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

    private const string QqUrl = "https://qm.qq.com/q/UPnTGW164m";
    private const string DiscordUrl = "https://discord.gg/4NScc8sEzw";
    private const string FeedbackUrl = "https://forms.office.com/r/jzsFaQKCpr";
    private const string RepoUrl = "https://github.com/Furry-Xiyi/UWP-Blank-Template";

    /// <summary>
    /// 应用名。清单的 DisplayName 是字面量（不走 ms-resource），但照样按官方本地化
    /// 做法留一级兜底：系统没解析出名字时去查资源表，而不是显示 <c>ms-resource:</c>
    /// 原始引用串。
    /// </summary>
    private static readonly string AppName = ResolveAppName("UWP 空白模板");

    /// <summary>
    /// 应用图标。<c>Package.Current.Logo</c> 交出来的是
    /// <c>file:///&lt;安装目录&gt;/Assets/…</c>；官方给包内资源的通道是
    /// <c>ms-appx:///相对路径</c>。
    /// </summary>
    private static readonly string AppLogo = ReadPackage(
        p => ToAppx(p.Logo?.ToString()),
        "Assets/StoreLogo.png");

    /// <summary>
    /// <c>file:///&lt;安装目录&gt;/Assets/x.png</c> → <c>ms-appx:///Assets/x.png</c>。
    /// </summary>
    /// <remarks>
    /// 框架的 <c>Image</c> / <c>BitmapIcon</c> 也会做这一步（见
    /// <c>Internal/PackUri.cs</c>），这里就地做掉是为了不依赖加载时机。
    /// 映射幂等：已经是 <c>ms-appx:</c> 的会原样返回。
    /// </remarks>
    private static string? ToAppx(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // URI 只认正斜杠；清单里给的是 Windows 路径分隔符。
        var normalized = raw.Replace('\\', '/').Trim();

        if (!normalized.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        string installPath;

        try
        {
            installPath = Package.Current.InstalledLocation.Path;
        }
        catch (Exception)
        {
            // 未打包运行时取不到安装目录，保持原样。
            return normalized;
        }

        var normalizedInstall = installPath.Replace('\\', '/').TrimEnd('/');
        var at = normalized.IndexOf(normalizedInstall, StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return normalized;
        }

        var relative = normalized[(at + normalizedInstall.Length)..].TrimStart('/');

        return relative.Length == 0 ? normalized : "ms-appx:///" + relative;
    }
    private static readonly string Version = ReadPackage(
        p => $"{p.Id.Version.Major}.{p.Id.Version.Minor}.{p.Id.Version.Build}.{p.Id.Version.Revision}",
        "1.0.0.0");
    private static readonly string Publisher = ReadPackage(p => p.PublisherDisplayName, "");

    /// <summary>
    /// 诊断用：冷启动后自动点进设置页（TestShell 的"设置页复现"用例由此驱动）。
    /// </summary>
    /// <remarks>
    /// 只用于无人值守复现"进设置页"这条路径（等价于人在左侧点一下"设置"），
    /// 省掉每次都重新部署一轮的时间。正常 app 永远保持 false。
    /// </remarks>
    public static bool AutoOpenSettings { get; set; }

    public override Element Render()
    {
        DefineStyles();

        // 四项设置都从持久化里取初值：模板的 SettingsPage.LoadUI() 干的就是
        // 把 SettingsManager 的值读回控件——只留在内存里的话，重启就打回默认值。
        var saved = AppSettings.Current;

        // 页面栈（XAML 版本是 Frame 的 BackStack）
        var (stack, setStack) = UseState(new[] { PageHome });
        var (themeIndex, setThemeIndex) = UseState((int)saved.Theme);
        var (materialIndex, setMaterialIndex) = UseState((int)saved.Material);
        var (paneIndex, setPaneIndex) = UseState((int)saved.Pane);
        var (soundOn, setSoundOn) = UseState(saved.Sound);
        var (windowActive, setWindowActive) = UseState(true);

        var current = stack[^1];
        var canGoBack = stack.Length > 1;

        void Navigate(string target)
        {
            if (string.Equals(stack[^1], target, StringComparison.Ordinal))
            {
                return;
            }

            setStack(stack.Append(target).ToArray());
        }

        void GoBack()
        {
            if (stack.Length > 1)
            {
                setStack(stack[..^1].ToArray());
            }
        }

        // 改一项 = 改 UI state + 落盘（模板是 SettingsManager 的 setter 里直接 Save）。
        void ChangeTheme(int v)
        {
            setThemeIndex(v);
            AppSettings.Update(x => x.Theme = (AppTheme)v);
        }

        void ChangeMaterial(int v)
        {
            setMaterialIndex(v);
            AppSettings.Update(x => x.Material = (AppMaterial)v);
        }

        void ChangePane(int v)
        {
            setPaneIndex(v);
            AppSettings.Update(x => x.Pane = (PanePosition)v);
        }

        // 除了改 state + 落盘，还要把值推给 XAML 的元素音效开关
        // （模板：SoundToggle_Toggled → MainPage.ApplySettings）。
        // 这里不写"启动时设一次"的 UseEffect：那必须在 UI 创建之前做，
        // 已经放到 App.OnLaunched 里了；Render 里的时机太晚，设了不响。
        void ChangeSound(bool v)
        {
            setSoundOn(v);
            AppSettings.Update(x => x.Sound = v);
            ElementSound.Apply(v);
        }

        // 诊断钩子：置了位就自动点一次设置项，用于无人值守复现"进设置页"。
        // 放在这里是唯一"触发导航"的入口，跟人点菜单走的是同一条 Navigate。
        UseEffect(() =>
        {
            if (!AutoOpenSettings)
            {
                return static () => { };
            }

            AutoOpenSettings = false;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            void Tick(object? sender, object e)
            {
                timer.Stop();
                Reactor.Uwp.Hosting.ReactorApplication.Trace("[nav-probe] 自动进入设置页");
                Navigate(PageSettings);
            }

            timer.Tick += Tick;
            timer.Start();
            return () =>
            {
                timer.Stop();
                timer.Tick -= Tick;
            };
        });

        // 挂载后再设一次（等价参考模板的 MainPage.ApplySettings）。
        // 启动时那一次在 App.OnLaunched 里，这里兜底——少了它就会出现
        // "进页面不响、拨一下开关才响"。
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
                        paneIndex, ChangePane, soundOn, ChangeSound)
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
            ).Margin(0, 32, 0, 0)
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
                TextBlock("欢迎来到主页，这是一个 UWP 模板应用")
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
    private static Element SettingsBody(
        int themeIndex, Action<int> setThemeIndex,
        int materialIndex, Action<int> setMaterialIndex,
        int paneIndex, Action<int> setPaneIndex,
        bool soundOn, Action<bool> setSoundOn) =>
        ScrollViewer(
            // Grid 包裹 StackPanel（与 XAML 版本一致：避免展开/折叠时的布局抖动）
            Group(
                // 模板就是一个 StackPanel（Spacing=4），节标题与卡片混排：
                // 组间距来自标题的 Margin(0,32,0,8)（首组为 0,0,0,8），不是嵌套 StackPanel。
                VStack(4,
                    // ── 外观 ──────────────────────────────────
                    SectionHeader("外观", isFirst: true),

                    SettingsExpander(
                        header: "应用主题",
                            description: "选择要显示的应用主题",
                            headerIcon: FontIcon("\uE706", fontSize: 20),
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
                    SectionHeader("声音"),

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
                    SectionHeader("关于"),

                    SettingsExpander(
                        header: AppName,
                        // 不要给 BitmapIcon 设 Width / Height：它不像 Image 那样把位图
                        // 缩放适配，设了尺寸就是把 106px 的原图裁出一个角，看着就像
                        // "图标缩没了"。原版同样不给尺寸，交给宿主去缩放。
                        headerIcon: BitmapIcon(AppLogo, showAsMonochrome: false),
                        description: $"©{DateTime.Now.Year} {Publisher}。保留所有权利。",
                        content: TextBlock(Version)
                            .Foreground(ThemeResource.Brush("TextFillColorSecondaryBrush")),
                        items: new Element?[]
                        {
                            SettingsCard(
                                // 模板的 StackPanel 写了 Spacing="8"，两个 HyperlinkButton
                                // 之间靠这 8px 分开（不是 0）。
                                content: VStack(8,
                                    HyperlinkButton(TextBlock("QQ 群"), () => OpenLink(QqUrl)),
                                    HyperlinkButton(TextBlock("Discord 频道"), () => OpenLink(DiscordUrl))),
                                contentAlignment: SettingsCardContentAlignment.Left),
                        })
                        .MinHeight(70),

                    // 图标放在 HyperlinkButton **里面**：整块（图标+文字）都是热区，
                    // 图标也会跟着变下划线/变色。模板把 FontIcon 放在按钮外面
                    // （StackPanel 里并列），点图标是没有任何反应的。
                    HyperlinkButton(
                        HStack(8,
                            FontIcon("\uED15", fontSize: 20).VAlign(VerticalAlignment.Center),
                            TextBlock("发送反馈").VAlign(VerticalAlignment.Center)),
                        () => OpenLink(FeedbackUrl))
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
    /// 组标题：14px SemiBold + 下边距 6（模板里节标题的写法）。
    /// </summary>
    /// <remarks>
    /// 边距放在<b>命名样式</b>里，本地不要再写一次 <c>.Margin(...)</c>：
    /// 依赖属性优先级中本地值高于样式值，两者同时写同一个属性时样式里的边距会被完全覆盖。
    /// </remarks>
    private static Element SectionHeader(string text, bool isFirst = false) =>
        TextBlock(text)
            .ApplyStyle("SettingsSectionHeaderTextBlockStyle")
            // 模板给第一个标题单独写了 Margin="0,0,0,8"（去掉顶部 32）；
            // 其余标题用样式里的 0,32,0,8。
            .Margin(isFirst ? new Thickness(0, 0, 0, 8) : new Thickness(0, 32, 0, 8));

    /// <summary>
    /// 节标题样式：<c>BodyStrongTextBlockStyle</c> + 下边距 6
    /// （对齐模板 <c>SettingsSectionHeaderTextBlockStyle</c>，重复定义以先定义者为准）。
    /// </summary>
    private static void DefineStyles()
    {
        StyleSheet.Define("SettingsSectionHeaderTextBlockStyle", b => b
            .Target<TextBlock>()
            .BasedOn("BodyStrongTextBlockStyle")
            .Margin(new Thickness(0, 32, 0, 8)));
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

    private static string ResolveAppName(string fallback)
    {
        // 与 Template 同一套顺序：资源表优先，其次清单属性（并挡掉未解析的引用串）。
        if (ReadLocalized("AppDisplayName") is { } localized)
        {
            return localized;
        }

        var name = ReadPackage(p => p.DisplayName, string.Empty);

        return !string.IsNullOrWhiteSpace(name) &&
               !name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            ? name
            : fallback;
    }

    private static string? ReadLocalized(string key)
    {
        try
        {
            var value = Windows.ApplicationModel.Resources.ResourceLoader
                .GetForViewIndependentUse()
                .GetString(key);

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
