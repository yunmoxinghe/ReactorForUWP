using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.ApplicationModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
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

    private static readonly string AppName = ReadPackage(p => p.DisplayName, "UWP 空白模板");
    private static readonly string AppLogo = ReadPackage(p => p.Logo?.ToString(), "Assets/StoreLogo.png");
    private static readonly string Version = ReadPackage(
        p => $"{p.Id.Version.Major}.{p.Id.Version.Minor}.{p.Id.Version.Build}.{p.Id.Version.Revision}",
        "1.0.0.0");
    private static readonly string Publisher = ReadPackage(p => p.PublisherDisplayName, "");

    public override Element Render()
    {
        DefineStyles();

        // 页面栈（XAML 版本是 Frame 的 BackStack）
        var (stack, setStack) = UseState(new[] { PageHome });
        var (themeIndex, setThemeIndex) = UseState(0);
        var (materialIndex, setMaterialIndex) = UseState(0);
        var (paneIndex, setPaneIndex) = UseState(0);
        var (soundOn, setSoundOn) = UseState(false);

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

        var menuItems = new[] { new NavigationViewItemData("主页", "\uE80F", PageHome) };

        // 内容区：面包屑（仅设置页显示）+ 当前页
        // 对照模板 MainPage.xaml：外层 StackPanel Spacing=4 / Padding="20,16,20,0"，
        // 里面的 BreadcrumbBar 用 ItemTemplate = SubtitleTextBlockStyle + FontSize 28。
        var content = Grid(
            new[] { GridSize.Star() },
            new[] { GridSize.Auto, GridSize.Star() },
            HStack(4, BreadcrumbBar(
                    new[] { "设置" },
                    itemFontSize: 28,
                    itemStyleKey: "SubtitleTextBlockStyle"))
                .Padding(20, 16, 20, 0)
                .VAlign(VerticalAlignment.Center)
                .IsVisible(current == PageSettings)
                .Grid(row: 0),
            Frame(current == PageSettings
                    ? SettingsBody(themeIndex, setThemeIndex, materialIndex, setMaterialIndex,
                        paneIndex, setPaneIndex, soundOn, setSoundOn)
                    : HomeBody(),
                // 模板的 ContentFrame 用 Frame.Navigate 的默认过渡；这里是换 Content，
                // 过渡由 Frame.ContentTransitions 提供（见 FrameElement 注释）。
                transition: PageTransition.Entrance)
                .Grid(row: 1));

        return Group(
            // ── 标题栏拖拽区（32px，透明）─────────────────────────
            Grid(
                new[] { GridSize.Star() },
                new[] { GridSize.Star() },
                // 模板 MainPage.xaml 的标题栏：Image 16x16 Margin="0,0,8,0"
                // + 应用名 FontSize=12（CaptionTextBlockStyle 就是 12px）。
                HStack(
                    Image(AppLogo).Size(16, 16).Margin(right: 8).VAlign(VerticalAlignment.Center),
                    TextBlock(AppName).Caption().VAlign(VerticalAlignment.Center)
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
                    // 模板 MainPage.xaml 写的是 PaneDisplayMode="Auto"，设置项只在
                    // "顶部" 和 "Auto" 之间切（不是 Top/Left）：
                    // MainPage.ApplySettings() → s.PanePosition == "Top" ? Top : Auto。
                    paneDisplayMode: paneIndex == 1 ? NavPaneDisplayMode.Top : NavPaneDisplayMode.Auto,
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
                            .Width(160)
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
                            value =>
                            {
                                setSoundOn(value);
                                ElementSoundPlayer.State = value
                                    ? ElementSoundPlayerState.On
                                    : ElementSoundPlayerState.Off;
                            }).HAlign(HorizontalAlignment.Right))
                        .MinHeight(70)
                        .Padding(16, 16),

                    // ── 关于 ──────────────────────────────────
                    SectionHeader("关于"),

                    SettingsExpander(
                        header: AppName,
                        headerIcon: BitmapIcon(AppLogo, showAsMonochrome: false),
                        description: $"©{DateTime.Now.Year} {Publisher}。保留所有权利。",
                        content: TextBlock(Version)
                            .Foreground(ThemeResource.Brush("TextFillColorSecondaryBrush")),
                        items: new Element?[]
                        {
                            SettingsCard(
                                // 模板是普通 StackPanel（未设 Spacing，即 0），两个
                                // HyperlinkButton 之间靠按钮自身的默认内边距留白。
                                content: VStack(0,
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
