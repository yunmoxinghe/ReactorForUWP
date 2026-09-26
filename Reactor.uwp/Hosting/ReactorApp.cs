using Windows.UI.ViewManagement;
using WindowsUIApplication = Windows.UI.Xaml.Application;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 顶层入口，对齐官方 Microsoft.UI.Reactor 的 <c>ReactorApp.Run&lt;TRoot&gt;()</c> 写法：
/// <code>
/// public static void Main(string[] args) => ReactorApp.Run&lt;MyRoot&gt;("标题", 800, 600);
/// </code>
/// UWP 要求先构造 Application 再由系统调用 OnLaunched，因此这里仍走
/// <c>Application.Start</c>，只是把官方的窗口参数透传给宿主。
/// </summary>
public static class ReactorApp
{
    private static string? _title;
    private static double? _preferredWidth;
    private static double? _preferredHeight;

    /// <summary>窗口标题（由 <see cref="Run{TRoot}(string, double?, double?)"/> 设置）。</summary>
    internal static string? Title => _title;

    internal static double? PreferredWidth => _preferredWidth;

    internal static double? PreferredHeight => _preferredHeight;

    /// <summary>启动一个以 <typeparamref name="TRoot"/> 为根组件的 Reactor 应用。</summary>
    public static void Run<TRoot>(
        string title = "Reactor App",
        double? width = null,
        double? height = null)
        where TRoot : Microsoft.UI.Reactor.Core.Component, new()
    {
        _title = title;
        _preferredWidth = width;
        _preferredHeight = height;

        WindowsUIApplication.Start(_ => new ReactorAppApplication<TRoot>());
    }

    /// <summary>把窗口标题/首选尺寸应用到当前视图（由宿主在 OnLaunched 中调用）。</summary>
    internal static void ApplyWindowSpec()
    {
        var view = ApplicationView.GetForCurrentView();

        if (_title is { } title)
        {
            view.Title = title;
        }

        if (_preferredWidth is { } w && _preferredHeight is { } h)
        {
            ApplicationView.PreferredLaunchWindowingMode =
                ApplicationViewWindowingMode.PreferredLaunchViewSize;
            ApplicationView.PreferredLaunchViewSize = new Windows.Foundation.Size(w, h);
        }
    }
}

/// <summary>ReactorApp.Run 用的具体 Application（UWP 需要实体类型才能激活）。</summary>
/// <remarks>
/// CsWinRT1028：实现 WinRT 接口的类型（含父类型）必须声明为 partial，
/// 否则 trimming / AOT 下跨 ABI 传递会出问题。
/// </remarks>
internal sealed partial class ReactorAppApplication<TRoot> : global::Reactor.Uwp.Hosting.ReactorApplication<TRoot>
    where TRoot : Microsoft.UI.Reactor.Core.Component, new()
{
}
