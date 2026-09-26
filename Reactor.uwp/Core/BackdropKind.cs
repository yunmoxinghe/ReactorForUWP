namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// 声明式选择器，指定 Reactor 宿主窗口要应用的背景材质。
/// 通过 <see cref="Microsoft.UI.Reactor.BackdropExtensions.Backdrop{T}(T, BackdropKind)"/>
/// 修饰符设置在根元素上，由宿主在挂载根内容时物化。
/// </summary>
/// <remarks>
/// UWP/WinUI2 与 WinUI3 的背景机制完全不同：
/// <list type="bullet">
/// <item>Mica：UWP 用 <c>BackdropMaterial.ApplyToRootOrPageBackground</c> 附加属性，
/// WinUI3 用 <c>Window.SystemBackdrop = MicaBackdrop()</c>。</item>
/// <item>桌面亚克力：UWP 用 <c>AcrylicBrush</c>（<c>BackgroundSource=HostBackdrop</c>）透出桌面，
/// WinUI3 用 <c>DesktopAcrylicBackdrop</c>。</item>
/// <item>应用内亚克力：两者都用 <c>AcrylicBrush</c>，但 UWP 可调
/// <c>TintColor</c>/<c>TintOpacity</c>/<c>TintLuminosityOpacity</c>/<c>FallbackColor</c>。</item>
/// </list>
/// </remarks>
public enum BackdropKind
{
    /// <summary>无背景。清除先前设置的背景。</summary>
    None,

    /// <summary>云母（窗口着色材质）。Win11 22000+ 生效，Win10 回退纯色。</summary>
    Mica,

    /// <summary>云母（Alt 变体）。UWP 无独立的 Alt 变体，物化时回退到 <see cref="Mica"/>。</summary>
    MicaAlt,

    /// <summary>桌面亚克力（窗口级，透出桌面）。UWP 用 <c>AcrylicBrush.BackgroundSource=HostBackdrop</c>。</summary>
    DesktopAcrylic,

    /// <summary>应用内亚克力（薄变体，仅模糊窗口内的 XAML 内容）。UWP 用 <c>AcrylicBrush</c> 默认 <c>Backdrop</c> 源。</summary>
    AcrylicThin,

    /// <summary>透明背景。</summary>
    Transparent,
}