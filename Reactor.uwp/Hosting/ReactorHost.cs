using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Internal;
using Windows.ApplicationModel.Resources.Core;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Reactor.Uwp.Hosting;

/// <summary>
/// 承载一个根 <see cref="Component"/> 的宿主。
/// 负责：调用 Render()、把 Element 树交给 Reconciler、响应状态变化重渲染。
/// 注意：纯代码（无 XAML 编译器）场景下不能跨 ABI 继承 XAML 控件类型
/// （CsWinRT 不会生成内部接口聚合，虚方法回调会无限递归），
/// 因此这里采用组合：内部持有一个真实的 <see cref="Frame"/>。
/// </summary>
/// <remarks>
/// <para><b>根容器必须是 Frame，不能用 ContentControl，也不要用 Grid。</b>
/// 背景材质的两个 API 类型约束互相打架，只有 Frame 同时满足：</para>
/// <list type="bullet">
/// <item><b>AcrylicBrush 需要 Background 真的被渲染</b>。Control.Background 的官方
/// 备注：该属性"只影响模板把 Background 用作模板 UI 属性输入的控制项"。
/// SDK generic.xaml 里 ContentControl 的模板只有一个裸 ContentPresenter，
/// 没有任何元素 TemplateBinding 到 Background → 设了等于没设；
/// 而 Frame 的模板（generic.xaml 第 13643 行起）在 ContentPresenter 上写了
/// <c>Background="{TemplateBinding Background}"</c>，会真实渲染。</item>
/// <item><b>Mica 需要 Control</b>：WinUI2 的
/// <c>BackdropMaterial.SetApplyToRootOrPageBackground(Control, bool)</c>
/// 只接受 Control，Grid / Border 传进去直接 CS1503。
/// 而 Frame 继承自 ContentControl → 本身是 Control。</item>
/// </list>
/// 参考实现见 Furry-Xiyi/UWP-Blank-Template 的 App.xaml.cs：
/// <c>Window.Current.Content = new Frame()</c>，然后
/// <c>rootFrame.Background = new AcrylicBrush{...}</c> /
/// <c>BackdropMaterial.SetApplyToRootOrPageBackground(rootFrame, true)</c>。
/// </remarks>
public sealed class ReactorHost
{
    private readonly Component _root;
    private readonly Reconciler _reconciler = new();

    /// <summary>合并同一轮消息泵内的多次重渲染请求（见 <see cref="RenderBatcher"/>）。</summary>
    private readonly RenderBatcher _batcher = new();
    private Element? _tree;

    /// <summary>
    /// 下一轮强制整树重建（不是 patch）。语言切换后置位——见 <see cref="OnQualifierChanged"/>。
    /// </summary>
    private bool _forceRebuild;
    private BackdropKind? _lastBackdrop;
    private ElementTheme? _lastTheme;
    private bool _refreshingBackdrop;

    /// <summary>语言等资源限定符的观察源（view-independent：不与某个窗口绑定）。</summary>
    private readonly ResourceContext _resourceContext = ResourceContext.GetForViewIndependentUse();

    /// <summary>
    /// <see cref="ResourceContext.QualifierValues"/> 的订阅委托。
    /// 必须存成字段才能注销；WinRT 事件的 <c>token</c> 注销需要同一个委托实例。
    /// </summary>
    private readonly MapChangedEventHandler<string, string> _qualifierChanged;

    public ReactorHost(Component root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _root.Context.RequestRerender = RequestRerender;

        // Frame：既是 Control（Mica 附加属性可用），模板又绑定了 Background（亚克力可见）。
        Root = new Frame
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

        // 主题切换时重新算亚克力的 tint / fallback 颜色 + 标题栏配色。
        // 注意：这里<b>不要</b>去动 Mica（原因见 RefreshBackdropForTheme 的注释）。
        Root.ActualThemeChanged += (_, _) =>
        {
            Reactor.Uwp.Hosting.ReactorLog.Info(Reactor.Uwp.Hosting.ReactorLogChannel.Host, $"Root.ActualTheme → {Root.ActualTheme}");

            // 活引用画笔（{ThemeResource} 语义）要在材质之前刷：材质那一步会读
            // 主题算 tint，两边都依赖"本轮的主题已经生效"这一前提。
            ThemeResource.RefreshLive();
            RefreshBackdropForTheme();
        };

        // 语言（及缩放/对比度等资源限定符）变化时刷新界面。
        // 订阅必须在这里而不是 Localization 里：ResourceContext 是进程级单例，
        // 委托由本宿主持有，生命周期跟宿主一致，不会把整个控件树挂在静态事件上。
        _qualifierChanged = OnQualifierChanged;
        _resourceContext.QualifierValues.MapChanged += _qualifierChanged;

        Rerender();
    }

    /// <summary>挂到窗口（或任意 XAML 容器）上的真实根控件。</summary>
    public Frame Root { get; }

    /// <summary>
    /// 组件状态变化时请求重渲染（可在任意线程调用）。
    /// </summary>
    /// <remarks>
    /// <b>多次调用会合并成一次渲染</b>（批处理）——语义与 React / 官方 Reactor 一致：
    /// <c>setState</c> 之后<b>不会同步生效</b>，它排到当前调用栈结束之后。
    /// 依赖"改完立刻读到新 UI"的写法要改成在渲染之后读。
    /// <para>
    /// 之前的实现只在<b>非</b> UI 线程分支做合并，UI 线程上连发 N 次 setState 就是
    /// N 次"整棵树 Render + Patch"；一个事件处理里改三个状态，代价直接翻三倍。
    /// </para>
    /// <para>
    /// 统一走 <c>RunAsync</c> 之后，"patch 途中被控件事件同步触发"这个重入场景也
    /// 自动解决了：回调排在 dispatcher 队列里，本轮 <c>Rerender</c> 返回后才会跑，
    /// 内外两层 patch 不会交错改同一棵原生树。
    /// </para>
    /// </remarks>
    public void RequestRerender()
    {
        // 已经排过一轮就把这次请求并进去（返回 false），不再单开一次渲染。
        if (!_batcher.TrySchedule())
        {
            Reactor.Uwp.Hosting.ReactorLog.Schedule("宿主 setState：合并进已排队的那轮");
            return;
        }

        Reactor.Uwp.Hosting.ReactorLog.Schedule("宿主 setState：入队");

        // 无论当前在不在 UI 线程都走 RunAsync：UI 线程上它只是排队，
        // 非 UI 线程上它顺带完成 marshal。
        _ = Root.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            // BeginRender 顺带清掉本轮的排队标志：渲染期间（同步的控件事件回调里）
            // 到来的 setState 必须能排上下一轮，而不是被本轮这个已消费的标志位挡掉。
            _batcher.BeginRender();
            try
            {
                Reactor.Uwp.Hosting.ReactorLog.Frame("宿主整树渲染");
                Rerender();
            }
            finally
            {
                // 必须放 finally：Rerender 抛异常时也要退出渲染态，
                // 否则之后每一次 setState 都被当成"渲染期间自触发"，
                // 攒够 50 次就误报渲染死循环。
                _batcher.EndRender();
            }
        });
    }

    /// <summary>
    /// 资源限定符变化（语言、缩放、对比度…）。
    /// </summary>
    /// <remarks>
    /// <b>为什么必须重建整棵树，光重渲染不行。</b><c>Localization.ApplyUid</c>
    /// 只在<b>挂载</b>时跑一次（对齐 XAML 编译器生成的初始化代码）。
    /// 语言切换后走的是 patch 路径，不会重跑它——界面上所有 <c>.Uid(...)</c>
    /// 的文本会一直停在旧语言。所以这里置 <see cref="_forceRebuild"/> 强制 Build 一遍。
    /// </remarks>
    private void OnQualifierChanged(
        IObservableMap<string, string> sender,
        IMapChangedEventArgs<string> args)
    {
        // 缩放 / 对比度变化 XAML 自己会处理（且重建整树代价太大），这里只认语言。
        if (!string.Equals(args.Key, "Language", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Reactor.Uwp.Hosting.ReactorLog.Info(Reactor.Uwp.Hosting.ReactorLogChannel.Localize, $"资源限定符变化: {args.Key}");

        _ = Root.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            // 顺序要紧：先让 Localization 丢掉缓存与旧的 ResourceLoader
            // （loader 创建时快照了 ResourceContext，不重建就一直返回旧语言的串），
            // 再重建整棵树，让 ApplyUid 重新跑一遍。
            Localization.Invalidate();
            _forceRebuild = true;
            RequestRerender();
        });
    }

    private void Rerender()
    {
        _reconciler.RunPass(() =>
        {
            _root.BeginRender();
            var next = _root.Render();
            _root.EndRender();

            // 顺序要紧：先把主题同步到 Root，再物化背景材质——材质（Mica 着色 /
            // 亚克力 tint）读的都是 Root 的实际主题，晚一步就会拿到上一轮的旧主题。
            ApplyTheme(next);
            ApplyBackdrop(next);

            // 根元素声明 OwnsTitleBar 时，宿主让出标题栏区域的布局权（见该修饰符的注释）。
            ReactorApplication.SetOwnsTitleBar(next.Modifiers?.OwnsTitleBar == true);

            // 语言切换后必须整树重建：Uid 只在挂载时解析（见 OnQualifierChanged）。
            var rebuild = _forceRebuild;
            _forceRebuild = false;

            if (_tree is null || rebuild || Root.Content is not UIElement native ||
                !Reconciler.CanPatch(_tree, next))
            {
                Root.Content = _reconciler.Build(next);
            }
            else
            {
                _reconciler.Patch(native, _tree, next);
            }

            _tree = next;
        });
    }

    /// <summary>
    /// 把根元素上的 <c>Backdrop</c> 修饰符物化到 <see cref="Root"/>（Frame）上。
    /// UWP/WinUI2 没有 <c>Window.SystemBackdrop</c>，各材质映射如下：
    /// Mica/MicaAlt → <c>BackdropMaterial.ApplyToRootOrPageBackground</c> 附加属性；
    /// DesktopAcrylic → <c>AcrylicBrush</c>（<c>BackgroundSource=HostBackdrop</c>，透出桌面）；
    /// AcrylicThin → <c>AcrylicBrush</c>（应用内亚克力）；None/Transparent → 无背景。
    /// </summary>
    /// <remarks>
    /// 亚克力参数<b>逐项照抄</b> Furry-Xiyi/UWP-Blank-Template 的
    /// <c>AppThemeManager.ApplyMaterial</c>：
    /// <list type="bullet">
    /// <item>tint：深色 <c>#2C2C2C</c> / 浅色 <c>#FCFCFC</c>；
    ///       fallback：深色 <c>#2C2C2C</c> / 浅色 <c>#F9F9F9</c>（与 tint 故意不同）。</item>
    /// <item><c>TintOpacity</c>：深色 0.15 / 浅色 0.0——浅色几乎全透。
    ///       早期这里写的是 0.8，那已经不是亚克力了，是一片压深的纯色。</item>
    /// <item><c>TintLuminosityOpacity</c>：深色 0.96 / 浅色 0.85，<b>必须显式给</b>：
    ///       留 null 时系统按 TintColor + TintOpacity 自己推算，与模板观感对不上。</item>
    /// </list>
    /// 排查技巧：把 FallbackColor 临时改成与 tint 反差很大的颜色，
    /// 看到 fallback 色就说明命中了静默回退路径。
    /// </remarks>
    private void ApplyBackdrop(Element root)
    {
        var kind = root.Modifiers?.Backdrop ?? BackdropKind.None;
        if (kind == _lastBackdrop)
        {
            return;
        }

        _lastBackdrop = kind;

        // 材质"看起来没生效"绝大多数是系统策略静默回退，先把环境自检落盘。
        Reactor.Uwp.Hosting.ReactorLog.Info(Reactor.Uwp.Hosting.ReactorLogChannel.Host, $"BACKDROP {kind}: {BackdropDiagnostics.Report()}");

        // 先关闭 Mica 附加属性，避免与 AcrylicBrush 背景叠加。
        Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(
            Root, false);

        switch (kind)
        {
            case BackdropKind.Mica:
            case BackdropKind.MicaAlt:
                // 需要 Windows 11 22000+；低于该版本 BackdropMaterial 内部即回退纯色。
                // 顺序照参考实现（AppThemeManager.ApplyMaterial）：先清 Background，
                // 再挂 Mica——否则上一次的亚克力会残留在材质之上。
                Root.Background = null;
                Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(
                    Root, true);
                break;

            case BackdropKind.DesktopAcrylic:
                // 窗口级亚克力：BackgroundSource=HostBackdrop 采样"窗口后面"的桌面。
                var desktopAcrylic = new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                };

                ApplyAcrylicSpec(desktopAcrylic);
                Root.Background = desktopAcrylic;
                break;

            case BackdropKind.AcrylicThin:
                // 应用内亚克力：默认 BackgroundSource=Backdrop，只模糊"自己下面"的
                // XAML 内容。刷在窗口根元素上时下面没有任何内容，结果是一片纯色调——
                // 这是预期行为，不是 bug。要看到真实模糊，必须压在有内容的图层之上。
                // 模板没有这一档：颜色沿用同一套，"薄"体现在更高的 tint 不透明度。
                var thinAcrylic = new AcrylicBrush();
                ApplyAcrylicSpec(thinAcrylic);
                thinAcrylic.TintOpacity = 0.6;
                Root.Background = thinAcrylic;
                break;

            default:
                // None / Transparent：清空背景。
                Root.Background = null;
                break;
        }
    }

    /// <summary>当前是不是深色主题。</summary>
    /// <remarks>
    /// 判定顺序对齐参考实现的 <c>GetIsDarkTheme()</c>：<c>ActualTheme</c>（系统算好的
    /// 实际值）→ <c>RequestedTheme</c>（本轮刚同步上来的应用主题）→
    /// <c>Application.RequestedTheme</c>。
    /// 中间那步不能省：主题刚改的那一轮里 <c>ActualThemeChanged</c> 还没触发，
    /// 只看 <c>ActualTheme</c> 会慢一拍，正好落在"切了主题但背景没跟上"上。
    /// </remarks>
    private bool IsDarkTheme()
    {
        var theme = Root.ActualTheme;
        if (theme == ElementTheme.Default)
        {
            theme = Root.RequestedTheme;
        }

        if (theme == ElementTheme.Default)
        {
            theme = Windows.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }

        return theme == ElementTheme.Dark;
    }

    /// <summary>
    /// 把模板那套亚克力参数写进画笔（创建与主题刷新共用一处，避免两边漂移）。
    /// </summary>
    private void ApplyAcrylicSpec(AcrylicBrush brush)
    {
        var dark = IsDarkTheme();

        brush.TintColor = dark
            ? Color.FromArgb(255, 44, 44, 44)
            : Color.FromArgb(255, 252, 252, 252);
        brush.FallbackColor = dark
            ? Color.FromArgb(255, 44, 44, 44)
            : Color.FromArgb(255, 249, 249, 249);
        brush.TintOpacity = dark ? 0.15 : 0.0;

        // 类型是 double?（null = 让系统自己推算），模板显式给了值，这里跟着给。
        brush.TintLuminosityOpacity = dark ? 0.96 : 0.85;
    }

    /// <summary>
    /// 把根元素上的 <c>RequestedTheme</c> 同步到 <see cref="Root"/>（Frame）上。
    /// </summary>
    /// <remarks>
    /// <b>只把主题写在根元素（Content 子树）上是不够的</b>：XAML 的
    /// <c>RequestedTheme</c> 只沿树<b>向下</b>继承，设在 Content 上的值影响不到它的
    /// 父容器 <see cref="Root"/>；而背景材质是挂在 <see cref="Root"/> 上的——
    /// Mica 的着色、亚克力的 tint 都取自 <see cref="Root"/> 的实际主题。
    /// 不同步这一步的结果就是：切"浅色 / 深色"时内容变了、材质还停在<b>系统</b>主题，
    /// 也就是"云母没跟随应用主题"。
    /// 参考实现（UWP-Blank-Template 的 AppThemeManager）同样把主题设在窗口根元素上。
    /// 子元素自己写的 <c>RequestedTheme</c> 仍然能覆盖继承值，这里只是补上根这一层。
    /// </remarks>
    private void ApplyTheme(Element root)
    {
        var theme = root.Modifiers?.RequestedTheme ?? ElementTheme.Default;
        if (theme == _lastTheme)
        {
            return;
        }

        _lastTheme = theme;
        Root.RequestedTheme = theme;
    }

    /// <summary>
    /// 实际主题变化后刷新材质：亚克力重算 tint / fallback，标题栏配色重设
    /// （逐项对照参考实现的 <c>AppThemeManager.OnActualThemeChanged</c>）。
    /// </summary>
    /// <remarks>
    /// <b>Mica 这里什么都不做——这是对齐参考实现的关键，不要"顺手补上"。</b>
    /// 参考实现只在 <c>CurrentMaterial == Acrylic</c> 的分支里改画笔，Mica 分支是空的。
    /// <para>
    /// 之前这里给 Mica 加了"先摘后挂"（<c>SetApplyToRootOrPageBackground(false)</c>
    /// → <c>(true)</c>），结果是<b>切主题必崩</b>：
    /// <c>Microsoft.UI.Xaml.dll+0xA578E</c>、<c>0xc0000005</c>、偏移每次都一样。
    /// 原因：<c>BackdropMaterial</c> 内部的 Mica 控制器自己就订阅了
    /// <c>ActualThemeChanged</c>，而本方法正是在那个通知里跑的——在通知过程中把
    /// 控制器拆掉重建，等于让它在自己的回调链里被销毁（MUX 那边没有重入保护），
    /// 轻则死循环（表现为"改主题卡死"），重则访问已释放对象。
    /// Mica 的着色本来就是系统按元素实际主题算的，<see cref="ApplyTheme"/> 把
    /// <c>RequestedTheme</c> 同步到 <see cref="Root"/> 之后，控制器自己会跟上。
    /// </para>
    /// </remarks>
    private void RefreshBackdropForTheme()
    {
        // 重入保护：本方法跑在 ActualThemeChanged 里，里面任何会再次触发主题计算的
        // 写操作都会形成"改背景 → 主题变 → 改背景"的回环，切主题时界面直接卡死。
        if (_refreshingBackdrop)
        {
            return;
        }

        _refreshingBackdrop = true;
        try
        {
            // 亚克力的 tint / fallback 是<b>托管侧</b>按主题算的，系统不管，得手动重算。
            if (Root.Background is AcrylicBrush brush)
            {
                ApplyAcrylicSpec(brush);
            }

            // 标题栏三个按钮的前景色不跟随应用主题（那是系统画的），要手动重设一次。
            ReactorApplication.CustomizeTitleBar();
        }
        finally
        {
            _refreshingBackdrop = false;
        }
    }
}
