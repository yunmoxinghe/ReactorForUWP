using System;
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
    private bool _inRenderPass;
    private BackdropKind? _lastBackdrop;

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

        // 主题切换时重新算亚克力的 tint / fallback 颜色。
        Root.ActualThemeChanged += (_, _) => RefreshAcrylicTint();

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
            if (_inRenderPass)
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
        _inRenderPass = true;
        try
        {
            _root.BeginRender();
            var next = _root.Render();
            _root.EndRender();

            ApplyBackdrop(next);

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
        }
        finally
        {
            _inRenderPass = false;
        }
    }

    /// <summary>
    /// 把根元素上的 <c>Backdrop</c> 修饰符物化到 <see cref="Root"/>（Frame）上。
    /// UWP/WinUI2 没有 <c>Window.SystemBackdrop</c>，各材质映射如下：
    /// Mica/MicaAlt → <c>BackdropMaterial.ApplyToRootOrPageBackground</c> 附加属性；
    /// DesktopAcrylic → <c>AcrylicBrush</c>（<c>BackgroundSource=HostBackdrop</c>，透出桌面）；
    /// AcrylicThin → <c>AcrylicBrush</c>（应用内亚克力）；None/Transparent → 无背景。
    /// </summary>
    /// <remarks>
    /// 参数取自 Furry-Xiyi/UWP-Blank-Template 的 AppThemeManager.ApplyMaterial：
    /// TintOpacity 0.8、tint 与 fallback 按明暗主题取 #202020 / #F3F3F3、
    /// 不显式设 TintLuminosityOpacity（系统会按 TintColor + TintOpacity 自动推算）。
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
                // 挂 Mica 时必须清掉 Background，否则亚克力残留在材质之上。
                Microsoft.UI.Xaml.Controls.BackdropMaterial.SetApplyToRootOrPageBackground(
                    Root, true);
                Root.Background = null;
                break;

            case BackdropKind.DesktopAcrylic:
                // 窗口级亚克力：BackgroundSource=HostBackdrop 采样"窗口后面"的桌面。
                Root.Background = new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                    TintColor = TintForTheme(),
                    TintOpacity = 0.8,
                    FallbackColor = TintForTheme(),
                };
                break;

            case BackdropKind.AcrylicThin:
                // 应用内亚克力：默认 BackgroundSource=Backdrop，只模糊"自己下面"的
                // XAML 内容。刷在窗口根元素上时下面没有任何内容，结果是一片纯色调——
                // 这是预期行为，不是 bug。要看到真实模糊，必须压在有内容的图层之上。
                Root.Background = new AcrylicBrush
                {
                    TintColor = TintForTheme(),
                    TintOpacity = 0.6,
                    FallbackColor = TintForTheme(),
                };
                break;

            default:
                // None / Transparent：清空背景。
                Root.Background = null;
                break;
        }
    }

    /// <summary>按当前实际主题给出亚克力的 tint / fallback 颜色（明暗自适应）。</summary>
    private Color TintForTheme()
    {
        var theme = Root.ActualTheme;
        if (theme == ElementTheme.Default)
        {
            theme = Windows.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }

        return theme == ElementTheme.Dark
            ? Color.FromArgb(255, 32, 32, 32)
            : Color.FromArgb(255, 243, 243, 243);
    }

    /// <summary>
    /// 实际主题变化后刷新已有亚克力画笔的 tint / fallback
    /// （对照参考实现的 OnActualThemeChanged）。
    /// </summary>
    private void RefreshAcrylicTint()
    {
        if (Root.Background is not AcrylicBrush brush)
        {
            return;
        }

        var tint = TintForTheme();
        brush.TintColor = tint;
        brush.FallbackColor = tint;
    }
}
