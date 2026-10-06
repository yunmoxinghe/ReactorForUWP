using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsUIApplication = Windows.UI.Xaml.Application;

namespace Reactor.Uwp.Hosting;

/// <summary>
/// 纯 C# 的 UWP 应用入口基类：替代 App.xaml + code-behind。
/// 负责 WinUI 2 运行时引导（XAML 元数据提供程序 + 控件资源字典），
/// 然后把根组件挂到当前窗口。
/// </summary>
public abstract partial class ReactorApplication : WindowsUIApplication,
    Windows.UI.Xaml.Markup.IXamlMetadataProvider
{
    /// <summary>
    /// 所有控件库的 XAML 元数据提供程序（按顺序查询，先命中者胜）。
    /// </summary>
    /// <remarks>
    /// <b>每个第三方控件库都必须在这里登记，否则其控件首次布局即原生崩溃。</b>
    /// 有 XAML 编译器时，生成的 App.g.i.cs 会把所有引用库的 Provider 串起来；
    /// 我们纯代码（无 App.xaml）没有这一层，只能自己串。
    /// 漏登记的代价：控件 <c>new</c> 得出来、属性也设得上，但一轮到套用模板 /
    /// 首次 Measure —— 框架按名字向 Application 查 <c>IXamlMetadataProvider</c>
    /// 拿到 null —— 就抛不带托管堆栈的 COMException“未指定的错误”
    /// （0x80004005），进程 fast-fail 退出 0xc000027b，
    /// <c>UnhandledException</c> 里 <c>e.Handled = true</c> 也拦不住。
    /// 症状与"集合投影写错"几乎一样，排查时先看这里。
    /// </remarks>
    private static readonly List<Windows.UI.Xaml.Markup.IXamlMetadataProvider> XamlMetadataProviders =
        new()
        {
            // WinUI 2（MUX）：NavigationView / BreadcrumbBar / RadioButtons / InfoBar …
            new Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider(),

            // CommunityToolkit SettingsControls：SettingsCard / SettingsExpander。
            // 类型名是 CsWinRT 给 WinRT 组件生成的（“Rns” + 下划线命名空间）。
            new CommunityToolkit.WinUI.Controls.SettingsControlsRns
                .CommunityToolkit_WinUI_Controls_SettingsControls_XamlTypeInfo
                .XamlMetaDataProvider(),
        };

    /// <summary>登记额外的控件库元数据提供程序（必须在窗口内容创建前调用）。</summary>
    public static void RegisterXamlMetadataProvider(
        Windows.UI.Xaml.Markup.IXamlMetadataProvider provider)
    {
        if (provider is not null)
        {
            XamlMetadataProviders.Add(provider);
        }
    }

    /// <inheritdoc/>
    public Windows.UI.Xaml.Markup.IXamlType GetXamlType(Type type)
    {
        foreach (var provider in XamlMetadataProviders)
        {
            if (provider.GetXamlType(type) is { } found)
            {
                return found;
            }
        }

        // 返回 null 是有意义的：XAML 用 null 表示"这个 Application 不认识该类型"。
        return null!;
    }

    /// <inheritdoc/>
    public Windows.UI.Xaml.Markup.IXamlType GetXamlType(string fullName)
    {
        foreach (var provider in XamlMetadataProviders)
        {
            if (provider.GetXamlType(fullName) is { } found)
            {
                return found;
            }
        }

        return null!;
    }

    /// <inheritdoc/>
    public Windows.UI.Xaml.Markup.XmlnsDefinition[] GetXmlnsDefinitions() =>
        XamlMetadataProviders
            .SelectMany(provider => provider.GetXmlnsDefinitions() ?? Array.Empty<Windows.UI.Xaml.Markup.XmlnsDefinition>())
            .ToArray();

    /// <summary>创建根组件。</summary>
    protected abstract Component CreateRootComponent();

    /// <summary>全局未处理异常处理器：完整托管堆栈落盘（含递归的 InnerException）。</summary>
    private void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 先把堆栈同步 flush 落盘，再决定要不要让它按默认流程崩溃退出。
        Trace($"UNHANDLED EXCEPTION 0x{e.Exception.HResult:X8}: " + Flatten(e.Exception));

        // 原生 XAML（布局 / 渲染 / SetTitleBar 等）抛出来的 COMException 常常
        // 不带任何托管堆栈——日志里只有一个 “未指定的错误”，等于没有线索，
        // 而且 UWP 一旦走到未处理异常就直接终止进程（0xc000027b），
        // 一次运行只能暴露一个问题。诊断期间对这类异常先吞掉（去重、限量），
        // 让进程活着，好在同一次运行里收集到后续信息。
        // 诊断期曾在这里吞掉异常（e.Handled = true）以换取同一次运行里多收集一点线索。
        // 崩溃根因已定位（第三方控件库漏登记 XamlMetadataProvider），恢复默认行为：不吞。
        Trace(DumpVisualTree());
    }

    /// <summary>
    /// 诊断用：把窗口可视树打成缩进文本。
    /// 原生异常（典型是“已指定错误的 E_FAIL”，如重复挂载、控件缺模板）
    /// 没有任何托管堆栈，只能靠这棵树定位是哪个控件出的问题。
    /// </summary>
    private static string DumpVisualTree()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[reactor] 可视树快照:");

        void Walk(Windows.UI.Xaml.DependencyObject? node, int depth, int index)
        {
            if (node is null || depth > 30 || sb.Length > 40000)
            {
                return;
            }

            var name = node.GetType().Name;
            var extra = string.Empty;

            if (node is Windows.UI.Xaml.FrameworkElement fe)
            {
                if (!string.IsNullOrEmpty(fe.Name))
                {
                    name = $"{fe.Name}:{name}";
                }

                if (fe.Visibility != Windows.UI.Xaml.Visibility.Visible)
                {
                    extra = $" <{fe.Visibility}>";
                }
            }

            sb.AppendLine($"  {new string(' ', depth * 2)}[{index}] {name}{extra}");

            try
            {
                var count = Windows.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < count; i++)
                {
                    Walk(Windows.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i), depth + 1, i);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  {new string(' ', depth * 2)}<遍历中断: {ex.Message}>");
            }
        }

        try
        {
            Walk(Windows.UI.Xaml.Window.Current?.Content, 0, 0);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"<无法遍历: {ex.Message}>");
        }

        return sb.ToString();
    }

    private static string Flatten(Exception? ex)
    {
        var sb = new System.Text.StringBuilder();
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            sb.AppendLine($"  [{cur.GetType().FullName}] {cur.Message}");
            sb.AppendLine(cur.StackTrace);
        }

        return sb.ToString();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 全局兜底：Activate() 之后异步抛出的异常（布局/渲染）不经过下方 try 块，
        // 未处理会 fail-fast（0xc000027b）且不带托管堆栈。这里捕获写盘、临时吞掉以观其变。
        UnhandledException += OnUnhandledException;

        try
        {
            BootstrapWinUi2();

            // 窗口关闭时显式退出进程：纯代码宿主缺少标准 App.xaml 的 PLM 生命周期兜底，
            // 不退出会导致每次运行都残留僵尸进程，日积月累锁死 AppX\clrjit.dll（DEP0500）。
            // 注意：CoreApplication.Exit 不能在激活事件处理器里直接调用，这里是在 Closed 回调里调用。
            Window.Current.Closed += (_, _) => CoreApplication.Exit();

            // 若通过 ReactorApp.Run 启动，应用标题与首选窗口尺寸。
            Microsoft.UI.Reactor.ReactorApp.ApplyWindowSpec();

            var host = new ReactorHost(CreateRootComponent());
            Window.Current.Content ??= host.Root;
            ExtendIntoTitleBar(host.Root);
            Window.Current.Activate();
        }
        catch (Exception ex)
        {
            Trace("FATAL: " + ex);
            throw;
        }
    }

    /// <summary>
    /// 让背景材质（Mica / 亚克力）延伸到标题栏区域，否则标题栏会留一条纯色带、
    /// 材质只出现在内容区。做法与 UWP 模板一致：扩展视图进标题栏 +
    /// 把标题栏按钮背景设为透明，再用根 Frame 的 Padding 把内容压回标题栏下方。
    /// </summary>
    private static Windows.UI.Xaml.Controls.Frame? _rootFrame;
    private static bool _ownsTitleBar;

    // 页面声明"我自己管理标题栏"时（根元素上的 .OwnsTitleBar()），
    // 宿主不再自动下压——否则模板式布局会被下压两次（宿主 32px + 页面 32px）。
    internal static void SetOwnsTitleBar(bool owns)
    {
        if (_ownsTitleBar == owns)
        {
            return;
        }

        _ownsTitleBar = owns;
        UpdateTitleBarPadding();
    }

    private static void UpdateTitleBarPadding()
    {
        if (_rootFrame is null)
        {
            return;
        }

        try
        {
            var height = CoreApplication.GetCurrentView().TitleBar.Height;
            _rootFrame.Padding = _ownsTitleBar ? new Thickness(0) : new Thickness(0, height, 0, 0);
        }
        catch (Exception ex)
        {
            Trace("UpdateTitleBarPadding failed: " + ex.Message);
        }
    }

    private static void ExtendIntoTitleBar(Windows.UI.Xaml.Controls.Frame root)
    {
        try
        {
            _rootFrame = root;

            CustomizeTitleBar();

            // Frame 的模板把 Padding 绑到 ContentPresenter，
            // 所以顶栏高度走 Padding：材质仍然铺满整窗，内容被压到标题栏下面。
            UpdateTitleBarPadding();
            CoreApplication.GetCurrentView().TitleBar.LayoutMetricsChanged +=
                (_, _) => UpdateTitleBarPadding();
        }
        catch (Exception ex)
        {
            Trace("ExtendIntoTitleBar failed: " + ex.Message);
        }
    }

    /// <summary>
    /// 标题栏延伸到客户区 + 三个胶囊按钮的配色（对齐模板的
    /// <c>AppThemeManager.CustomizeTitleBar</c>）。
    /// </summary>
    /// <remarks>
    /// 只把按钮<b>背景</b>刷成透明是不够的：前景色还是系统默认的深色，
    /// 应用切到深色主题后就是"深字压深底"，三个按钮几乎看不见。
    /// 所以前景 / 失焦前景 / 悬停 / 按下都要按当前明暗重设——
    /// 主题切换时由 <see cref="ReactorHost"/> 的 <c>ActualThemeChanged</c> 重新调用。
    /// </remarks>
    internal static void CustomizeTitleBar()
    {
        try
        {
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;

            var titleBar = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView().TitleBar;

            // 必须把按钮背景刷成透明，否则三个胶囊按钮是不透明色块。
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            var dark = IsDarkTheme();
            var foreground = dark ? Colors.White : Colors.Black;

            // 失焦按钮用不透明灰（与 WinUI 3 模板一致，比"半透明前景"稳定）。
            var inactiveForeground = dark
                ? Color.FromArgb(255, 128, 128, 128)
                : Color.FromArgb(255, 160, 160, 160);

            var hoverBackground = dark
                ? Color.FromArgb(20, 255, 255, 255)
                : Color.FromArgb(20, 0, 0, 0);

            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
            titleBar.ButtonHoverBackgroundColor = hoverBackground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor =
                Color.FromArgb(30, hoverBackground.R, hoverBackground.G, hoverBackground.B);
            titleBar.ButtonPressedForegroundColor = foreground;
        }
        catch (Exception ex)
        {
            Trace("CustomizeTitleBar failed: " + ex.Message);
        }
    }

    /// <summary>与 <see cref="ReactorHost"/> 里那份同序的明暗判定（这份是静态的）。</summary>
    private static bool IsDarkTheme()
    {
        var theme = _rootFrame?.ActualTheme ?? ElementTheme.Default;
        if (theme == ElementTheme.Default)
        {
            theme = _rootFrame?.RequestedTheme ?? ElementTheme.Default;
        }

        if (theme == ElementTheme.Default)
        {
            theme = WindowsUIApplication.Current.RequestedTheme == ApplicationTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }

        return theme == ElementTheme.Dark;
    }

    /// <summary>
    /// 纯代码加载 WinUI 2：
    /// 1) 初始化 XamlControlsXamlMetaDataProvider；
    /// 2) 合并 XamlControlsResources —— 等价于 App.xaml 里的
    ///    &lt;XamlControlsResources/&gt;。
    /// 必须在 OnLaunched（而非构造函数）中执行，且早于任何 WinUI 控件创建。
    /// </summary>
    private void BootstrapWinUi2()
    {
        Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider.Initialize();

        if (!Resources.MergedDictionaries.Any(d => d is XamlControlsResources))
        {
            Resources.MergedDictionaries.Add(new XamlControlsResources());
        }
    }

    /// <summary>
    /// 诊断日志。落到 <c>LocalState\reactor-startup.log</c>。
    /// stowed exception (0xC000027B) 不会带托管堆栈，只能靠落盘定位。
    /// 可在任意线程调用；非 UI 线程访问 <c>ApplicationData</c> 可能失败，此时只留 Debug 输出。
    /// </summary>
    /// <remarks>
    /// <b>本方法只是兼容外壳。</b>框架内部已全部改用 <see cref="ReactorLog"/>（有通道、有级别、
    /// 有内存环形缓冲）；新代码请直接用 <see cref="ReactorLog"/>，别再往这里加前缀约定。
    /// </remarks>
    public static void Trace(string message) =>
        ReactorLog.Write(ReactorLogChannel.Host, ReactorLogLevel.Info, message);
}

/// <summary>
/// 泛型便捷入口：应用只需写
/// <c>sealed class App : ReactorApplication&lt;MyRootComponent&gt; { }</c>。
/// </summary>
public abstract partial class ReactorApplication<TRoot> : ReactorApplication
    where TRoot : Component, new()
{
    protected sealed override Component CreateRootComponent() => new TRoot();
}
