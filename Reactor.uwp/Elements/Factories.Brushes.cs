using Windows.UI;
using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    /// <summary>
    /// 一块亚克力（对齐官方 <c>AcrylicBrush(tintColor, tintOpacity, fallbackColor,
    /// tintLuminosityOpacity)</c>）。返回的是 <b>真的</b>
    /// <see cref="Windows.UI.Xaml.Media.AcrylicBrush"/>，不是"像亚克力的纯色"。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>它是刷子，不是元素。</b>元素那一侧（<c>Fill</c> / <c>Background</c> /
    /// <c>PaneBackground</c>…）收的都是 <see cref="Brush"/> 实例，本方法就是把
    /// "刷子"这一层补上——之前没有它，"给某个属性配一块亚克力"只能走
    /// <c>Native()</c>，因为声明式一侧没有刷子的表达。
    /// </para>
    /// <para>
    /// <b>这一块是"应用内亚克力"（<c>BackgroundSource = Backdrop</c>，官方默认）：</b>
    /// 它模糊的是<b>窗口内、它后面那层 XAML 内容</b>。想透出桌面（<c>HostBackdrop</c>，
    /// 需要窗口本身把背景铺开）走 <c>.Backdrop(BackdropKind.DesktopAcrylic)</c> 那条路
    /// ——那是宿主窗口的事，不是某个属性上的刷子能决定的，所以本方法<b>不</b>收
    /// <c>BackgroundSource</c> 参数（与官方一致）。
    /// </para>
    /// <para>
    /// <c>fallbackColor</c> 与 <c>tintLuminosityOpacity</c> 给了才写：两个都是
    /// "不设就用控件自己的默认"，写死一个值进去等于替官方做默认值决策。
    /// <c>tintLuminosityOpacity</c> 在 UWP 上是 <c>double?</c>，null 就是"交给控件"。
    /// </para>
    /// <para>
    /// <b>要它真的生效，后面必须有东西</b>：亚克力是"取背后那层做模糊"，后面
    /// 什么都没画（比如窗口背景是透明或纯色没铺开）时看到的就是
    /// <c>fallbackColor</c> 那一层纯色——不是 bug，是它唯一的素材没了。
    /// </para>
    /// </remarks>
    public static AcrylicBrush AcrylicBrush(
        Color tintColor,
        double tintOpacity = 0.8,
        Color? fallbackColor = null,
        double? tintLuminosityOpacity = null)
    {
        var brush = new AcrylicBrush
        {
            TintColor = tintColor,
            TintOpacity = tintOpacity,
        };

        if (fallbackColor is { } fallback)
        {
            brush.FallbackColor = fallback;
        }

        if (tintLuminosityOpacity is { } luminosity)
        {
            brush.TintLuminosityOpacity = luminosity;
        }

        return brush;
    }
}
