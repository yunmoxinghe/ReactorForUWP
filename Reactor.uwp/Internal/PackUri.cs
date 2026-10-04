using System;

using Windows.ApplicationModel;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 把"包内资源的各种写法"统一成 XAML 能真正加载的绝对 URI。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要这一步。</b>XAML 里写 <c>&lt;Image Source="Assets/Logo.png"/&gt;</c>
/// 能用，靠的是 <b>XAML 解析器</b>拿页面自己的 base URI 把相对路径补全。
/// 官方 <c>Image.Source</c> 文档说得很直白：代码里<b>没有</b>这个捷径，
/// 必须自己显式构造带 <c>ms-appx:</c> 的绝对 URI——WinRT 不接受
/// <c>UriKind.Relative</c>。纯代码建控件（也就是本框架做的事）全落在这条路上。
/// </para>
/// <para>
/// <b>两个真实的坑。</b>
/// </para>
/// <list type="number">
/// <item>
/// <b>相对路径 / 反斜杠。</b>包清单里写的是 Windows 路径
/// <c>&lt;Logo&gt;Assets\StoreLogo.png&lt;/Logo&gt;</c>，反斜杠会一路带进
/// <c>Windows.Foundation.Uri</c> 被当成普通字符（或转义成 %5C），解析失败时
/// <b>不抛异常、不报错，图就是不出来</b>。URI 的世界里只有正斜杠。
/// </item>
/// <item>
/// <b><c>file:///</c> 指进安装目录。</b><c>Package.Current.Logo</c> 交出来的不是
/// 相对路径，而是 <c>file:///&lt;安装目录&gt;/Assets/...</c> 这种绝对文件 URI
/// （实测：<c>file:///D:/.../AppX/Assets/SmallTile.scale-150.png</c>）。
/// 它能"碰巧"加载，但官方给包内资源的通道是 <c>ms-appx:///相对路径</c>
/// （Raymond Chen：「要引用包里的内容不需要取路径，用 ms-appx 协议就行」）。
/// 这里统一把安装目录下的 <c>file:///</c> 映射成 <c>ms-appx:///</c>。
/// </item>
/// </list>
/// <para>
/// <b>注意：不要给 <c>BitmapIcon</c> 设 Width / Height。</b>它不像 <c>Image</c>
/// 那样把位图缩放适配——设了尺寸就是把原图<b>裁</b>出一个角。位图图标的自然尺寸
/// 来自解码后的位图，要缩放交给宿主（Viewbox / 卡片的图标呈现器）。
/// 所以 URI 一定要能真正加载：加载不出来自然尺寸就是 0，图标彻底看不见。
/// </para>
/// </remarks>
internal static class PackUri
{
    /// <summary>清单里 <c>ms-appx:</c> 的写法，用于识别"已经是包内 URI"。</summary>
    private const string AppxScheme = "ms-appx:";

    /// <summary>
    /// 把 <paramref name="source"/> 变成绝对 URI；失败返回 <c>null</c>（调用方静默忽略）。
    /// </summary>
    /// <remarks>
    /// 接受四种输入：指向安装目录的 <c>file:///</c>（映射成 <c>ms-appx:</c>）、
    /// 其它绝对 URI（<c>http://</c> 等，原样保留）、带 <c>ms-appx:</c> 但斜杠个数
    /// 不规范的、以及纯相对路径（按 <c>ms-appx:///</c> 补全）。
    /// </remarks>
    public static Uri? TryCreate(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        try
        {
            // 清单/文件系统给的是 Windows 路径分隔符；URI 只认正斜杠。
            var normalized = source.Replace('\\', '/').Trim();

            if (normalized.Contains("://", StringComparison.Ordinal))
            {
                return new Uri(MapInstallLocationToAppx(normalized) ?? normalized);
            }

            if (normalized.StartsWith(AppxScheme, StringComparison.OrdinalIgnoreCase))
            {
                // ms-appx:/Assets/x.png → ms-appx:///Assets/x.png（必须是三斜杠）。
                normalized = AppxScheme + "///" + normalized[AppxScheme.Length..].TrimStart('/');
                return new Uri(normalized);
            }

            return new Uri(AppxScheme + "///" + normalized.TrimStart('/'));
        }
        catch (Exception)
        {
            // 对齐官方行为：非法 URI 静默忽略，不让一张图拖垮整棵树。
            return null;
        }
    }

    /// <summary>
    /// <c>file:///&lt;安装目录&gt;/Assets/x.png</c> → <c>ms-appx:///Assets/x.png</c>；
    /// 不是安装目录下的 <c>file:///</c>（比如用户自己选的本地文件）返回 <c>null</c> 保持原样。
    /// </summary>
    private static string? MapInstallLocationToAppx(string source)
    {
        if (!source.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string installPath;

        try
        {
            installPath = Package.Current.InstalledLocation.Path;
        }
        catch (Exception)
        {
            // 未打包运行时取不到安装目录，保持原样。
            return null;
        }

        if (string.IsNullOrEmpty(installPath))
        {
            return null;
        }

        var normalizedInstall = installPath.Replace('\\', '/').TrimEnd('/');
        var at = source.IndexOf(normalizedInstall, StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return null;
        }

        var relative = source[(at + normalizedInstall.Length)..].TrimStart('/');

        return relative.Length == 0 ? null : AppxScheme + "///" + relative;
    }
}
