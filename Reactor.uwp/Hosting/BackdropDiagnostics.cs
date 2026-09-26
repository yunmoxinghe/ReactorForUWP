using System;
using Windows.Foundation.Metadata;
using Windows.System.Power;
using Windows.System.Profile;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace Reactor.Uwp.Hosting;

/// <summary>
/// 背景材质（Mica / 亚克力）"看起来没生效"时的运行时自检。
/// </summary>
/// <remarks>
/// XAML 在下列情况下会**静默**把材质替换成纯色，UI 上完全看不出原因
/// （参见 Acrylic material / Mica material 文档的 Usability and adaptability）：
/// <list type="bullet">
/// <item>用户在"设置 &gt; 个性化 &gt; 颜色"里关闭了透明效果 —— 所有材质失效。</item>
/// <item>节电模式开启 —— 亚克力失效（Mica 不受影响）。</item>
/// <item>高对比度主题 —— 所有材质失效。</item>
/// <item>低端硬件 / 远程桌面 / 虚拟机 —— 合成器无法与桌面混合，回退纯色。</item>
/// <item>窗口失焦（deactivate）—— 只有**背景亚克力**会回退成 FallbackColor，
/// 应用内亚克力不受影响；Mica 也会回退成中性色。</item>
/// <item>Mica 需要 Windows 11（build ≥ 22000），低于该版本一律回退纯色。</item>
/// </list>
/// 另外两个与系统策略无关、纯属用法问题的常见"不生效"：
/// <list type="bullet">
/// <item>把**应用内亚克力**（BackgroundSource=Backdrop）刷在窗口根元素上：
/// 它模糊的是"自己下面的 XAML 内容"，根元素下面什么都没有，结果就是一片纯色调，
/// 看起来跟没生效一样。要看到效果必须让它压在有内容的图层之上。</item>
/// <item>FallbackColor 与 TintColor 设成同一个颜色：一旦回退，画面与正常渲染几乎一样，
/// 无法区分"生效了"还是"回退了"。</item>
/// </list>
/// </remarks>
public static class BackdropDiagnostics
{
    /// <summary>生成一行紧凑的自检结论，便于 Trace 落盘或直接显示在 UI 上。</summary>
    public static string Report()
    {
        var os = OsBuild();

        return string.Join(" | ",
            $"OS {os}",
            $"acrylicApi {Flag(AcrylicApiPresent())}",
            $"mica {Flag(MicaSupported(os))}",
            $"transparent {Flag(TransparencyEnabled())}",
            $"batterySaver {Flag(BatterySaverOn())}",
            $"highContrast {Flag(HighContrast())}",
            $"active {Flag(WindowActive())}");
    }

    /// <summary>Windows 内部版本号（如 26100）。解析失败返回 0。</summary>
    public static int OsBuild()
    {
        try
        {
            var raw = AnalyticsInfo.VersionInfo.DeviceFamilyVersion;
            if (ulong.TryParse(raw, out var v))
            {
                return (int)((v >> 16) & 0xFFFF);
            }
        }
        catch (Exception)
        {
            // 诊断代码不应影响主流程
        }

        return 0;
    }

    /// <summary>系统是否启用透明效果（设置 &gt; 个性化 &gt; 颜色）。</summary>
    public static bool TransparencyEnabled()
    {
        try
        {
            return new UISettings().AdvancedEffectsEnabled;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>节电模式是否开启（开启时亚克力被禁用）。</summary>
    public static bool BatterySaverOn()
    {
        try
        {
            return PowerManager.EnergySaverStatus == EnergySaverStatus.On;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>是否处于高对比度主题。</summary>
    public static bool HighContrast()
    {
        try
        {
            return new AccessibilitySettings().HighContrast;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>本窗口是否处于前台激活态（失焦时背景亚克力会回退纯色）。</summary>
    public static bool WindowActive()
    {
        try
        {
            return Window.Current?.CoreWindow?.ActivationMode
                   == Windows.UI.Core.CoreWindowActivationMode.ActivatedInForeground;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>UWP 的 AcrylicBrush 是否可用（Fall Creators Update / 16299 起）。</summary>
    public static bool AcrylicApiPresent() =>
        ApiInformation.IsTypePresent("Windows.UI.Xaml.Media.AcrylicBrush");

    /// <summary>Mica 是否可用：WinUI 2 的 BackdropMaterial + Windows 11（22000+）。</summary>
    public static bool MicaSupported(int osBuild) =>
        osBuild >= 22000 &&
        ApiInformation.IsTypePresent("Microsoft.UI.Xaml.Controls.BackdropMaterial");

    private static string Flag(bool ok) => ok ? "ok" : "NO";
}
