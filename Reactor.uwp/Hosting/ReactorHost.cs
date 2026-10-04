using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Internal;
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
    private Element? _tree;
    private bool _renderQueued;
    private BackdropKind? _lastBackdrop;
    private ElementTheme? _lastTheme;
    private bool _refreshingBackdrop;

    // 每轮渲染后强制同步布局：能把 XAML 原生侧的布局异常逼到托管堆栈里（否则
    // 日志只剩一句"未指定的错误"）。代价是每轮一次同步布局，且它跑在主题传播的
    // 尾巴上，切主题时会把开销放大——定位完就该关掉，改 true 重新启用。
    // 用 static readonly 而不是 const：const false 会让下面那整块变成不可达代码（CS0162）。
    private static readonly bool LayoutProbe = false;

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
            ReactorApplication.Trace($"[reactor] Root.ActualTheme → {Root.ActualTheme}");

            // 活引用画笔（{ThemeResource} 语义）要在材质之前刷：材质那一步会读
            // 主题算 tint，两边都依赖"本轮的主题已经生效"这一前提。
            ThemeResource.RefreshLive();
            RefreshBackdropForTheme();
        };

        Rerender();
    }

    /// <summary>挂到窗口（或任意 XAML 容器）上的真实根控件。</summary>
    public Frame Root { get; }

    /// <summary>组件状态变化时请求重渲染（可在任意线程调用）。</summary>
    public void RequestRerender()
    {
        if (Root.Dispatcher.HasThreadAccess)
        {
            // patch 途中被控件事件同步触发：推迟到本轮结束后再跑，
            // 否则内外两层 patch 会交错改同一棵原生树（同类崩溃的根因）。
            // 注意必须用协调器的标志位：宿主渲染与子组件重渲染共用同一把锁，
            // 否则宿主驱动的一轮渲染不会被识别为"渲染中"。
            if (_reconciler.InRenderPass)
            {
                _ = Root.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, Rerender);
                return;
            }

            Rerender();
            return;
        }

        // 非 UI 线程：marshal 回 UI 线程并合并同一轮消息泵内的多次请求。
        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;
        _ = Root.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            _renderQueued = false;
            Rerender();
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

            if (_tree is null || Root.Content is not UIElement native ||
                !Reconciler.CanPatch(_tree, next))
            {
                Root.Content = _reconciler.Build(next);
            }
            else
            {
                _reconciler.Patch(native, _tree, next);
            }

            _tree = next;

            // 诊断：XAML 原生侧的异常（布局 / 渲染 / SetTitleBar）通常不带托管堆栈，
            // 日志里只剩一句“未指定的错误”。这里主动同步触发一次布局，
            // 让异常在托管帧里抛出，堆栈里至少能看出是布局阶段、由哪次重渲染引起。
            // 定位完成后应删掉这段（每次渲染强制布局有性能代价）。
            if (LayoutProbe)
            {
                try
                {
                    Root.UpdateLayout();
                }
                catch (Exception ex)
                {
                    ReactorApplication.Trace(
                        $"[reactor] 布局阶段异常: [{ex.GetType().Name}] 0x{ex.HResult:X8} {ex.Message}\n{ex.StackTrace}");

                    // 只知道"布局阶段炸了"没用，必须知道是哪个元素炸的。
                    ReactorApplication.Trace(LocateLayoutFailure(Root));
                }
            }
        });
    }

    /// <summary>
    /// 定位导致布局失败的最小子树：<b>逐层二分</b>。
    /// 做法是把某个子树的 <c>Visibility</c> 临时置成 <c>Collapsed</c>，
    /// 再跑一次根布局——不抛了就说明元凶在这棵子树里，然后继续往下钻。
    /// </summary>
    /// <remarks>
    /// 只在根布局已抛异常时才跑，正常渲染路径零开销。
    /// 二分会临时改动可见性并强制布局，属于"反正要崩"的兜底诊断，定位完应删掉。
    /// </remarks>
    private static string LocateLayoutFailure(UIElement root)
    {
        var report = new System.Text.StringBuilder("[reactor] 最小失败子树定位:\n");

        // 先确认异常可复现：不可复现的话二分没有意义。
        var repeat = new System.Text.StringBuilder("  可复现性: ");
        for (var i = 0; i < 3; i++)
        {
            repeat.Append(TryUpdateLayout(root) ? "ok " : "FAIL ");
        }

        report.AppendLine(repeat.ToString());

        var node = (DependencyObject)root;
        var path = new List<string>();

        for (var depth = 0; depth < 40; depth++)
        {
            var culprit = FindCulpritChild(root, node, report);
            if (culprit is null)
            {
                report.AppendLine($"  → 失败源就在本节点: {Describe(node)}  路径 {string.Join("/", path)}");
                break;
            }

            path.Add(Describe(culprit));

            int children;
            try
            {
                children = VisualTreeHelper.GetChildrenCount(culprit);
            }
            catch
            {
                children = 0;
            }

            if (children == 0)
            {
                report.AppendLine($"  → 叶子元凶: {string.Join("/", path)}");
                break;
            }

            node = culprit;
        }

        return report.ToString();
    }

    /// <summary>逐个隐藏子节点并重试根布局，返回"隐藏它就不抛"的那个子节点。</summary>
    private static DependencyObject? FindCulpritChild(
        UIElement root,
        DependencyObject node,
        System.Text.StringBuilder report)
    {
        int count;
        try
        {
            count = VisualTreeHelper.GetChildrenCount(node);
        }
        catch (Exception ex)
        {
            report.AppendLine($"  <遍历中断 {Describe(node)}: {ex.Message}>");
            return null;
        }

        for (var i = 0; i < count; i++)
        {
            DependencyObject child;
            try
            {
                child = VisualTreeHelper.GetChild(node, i);
            }
            catch
            {
                continue;
            }

            if (child is not UIElement ui || ui.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var original = ui.Visibility;
            ui.Visibility = Visibility.Collapsed;
            var ok = TryUpdateLayout(root);
            ui.Visibility = original;

            if (ok)
            {
                report.AppendLine($"  隐藏 #{i} {Describe(child)} 后不再抛 → 元凶在这棵子树里");
                return child;
            }
        }

        return null;
    }

    private static bool TryUpdateLayout(UIElement root)
    {
        try
        {
            root.UpdateLayout();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Describe(DependencyObject node)
    {
        var name = node.GetType().Name;
        return node is FrameworkElement { Name.Length: > 0 } fe ? $"{fe.Name}:{name}" : name;
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
        ReactorApplication.Trace($"BACKDROP {kind}: {BackdropDiagnostics.Report()}");

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
