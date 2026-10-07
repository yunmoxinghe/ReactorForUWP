using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 图标元素的物化（<c>IconSource</c> 版）：<c>FontIcon</c> / <c>BitmapIcon</c> /
/// <c>SymbolIcon</c> / <c>ImageIcon</c> → <c>FontIconSource</c> /
/// <c>BitmapIconSource</c> / <c>SymbolIconSource</c> / <c>ImageIconSource</c>。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IconElements"/> 是<b>同一个图标元素的两种落点</b>，区别只在宿主
/// 槽位的类型：官方 <c>TabViewItem.IconSource</c> 与 <c>SwipeItem.IconSource</c> 收
/// <c>IconSource</c>，<c>AppBarButton.Icon</c> 与 <c>MenuFlyoutItem.Icon</c> 收
/// <c>IconElement</c>。这里只管"数据 → 数据"那一次翻译。
/// </para>
/// <para>
/// <b>这里一律新建实例，不做 <c>IconElements</c> 之外的就地复用。</b>调用方（滑动命令
/// 组）本来就是整组重建的：组里每一项都是本轮新建的，复用一个旧 <c>IconSource</c>
/// 反而会让"这一项是不是变了"变成按引用判断（必然每次都变）。
/// 需要就地复用的是 <c>TabViewItem</c> 那类"长期挂着的槽位"——那里有自己的
/// <c>ApplyIconSource</c>，不走这里。
/// </para>
/// </remarks>
internal static class IconSources
{
    /// <summary>把图标元素翻成真的 <c>IconSource</c>；不认识的返回 <c>null</c> 并留痕。</summary>
    public static MuxControls.IconSource? From(Element element)
    {
        switch (element)
        {
            case FontIconElement font:
                return new MuxControls.FontIconSource { Glyph = font.Glyph };

            case BitmapIconElement bitmap:
                // URI 非法时是<b>静默失败</b>（见 PackUri）：这里给 null，
                // 宿主那个槽位就是没有图标，不抛。
                return PackUri.TryCreate(bitmap.UriSource) is { } uri
                    ? new MuxControls.BitmapIconSource
                    {
                        UriSource = uri,
                        ShowAsMonochrome = bitmap.ShowAsMonochrome,
                    }
                    : null;

            case SymbolIconElement symbol:
                return new MuxControls.SymbolIconSource { Symbol = symbol.Symbol };

            case ImageIconElement image:
                // ImageIcon 内部是 Image（原色 + 缩放），槽位这一侧对应
                // ImageIconSource —— 属性名是 ImageSource，不是 Source。
                return PackUri.TryCreate(image.UriSource) is { } imageUri
                    ? new MuxControls.ImageIconSource { ImageSource = new BitmapImage(imageUri) }
                    : null;

            default:
                ReactorApplication.Trace(
                    $"[reactor] 图标只收 FontIcon / BitmapIcon / SymbolIcon / ImageIcon，" +
                    $"收到 {element.GetType().Name}，已忽略");
                return null;
        }
    }
}

/// <summary>
/// 图标元素的物化：<c>FontIcon</c> / <c>BitmapIcon</c> / <c>SymbolIcon</c> /
/// <c>ImageIcon</c> → 真的 <c>IconElement</c>。
/// </summary>
/// <remarks>
/// <b>为什么单独一份。</b>菜单项（<c>MenuFlyoutItem.Icon</c>）与命令条按钮
/// （<c>AppBarButton.Icon</c>）收的都是 <c>IconElement</c>，规矩一模一样；
/// 之前这份逻辑写在 <c>MenuFlyouts</c> 里，命令条要用它就得去依赖"菜单"那个类，
/// 于是"图标怎么造"这件与菜单毫不相干的事被绑在了菜单上。这里把共用部分提出来，
/// 两边各自调它。
/// <para>
/// <b>与 <c>IconSource</c> 的区别别混。</b><c>InfoBadge.Icon</c> 收的是
/// <c>IconSource</c>（<c>FontIconSource</c> 那一族），不是这里的 <c>IconElement</c>。
/// 官方两类槽位并存：<c>IconElement</c> 是能站进可视树的控件，
/// <c>IconSource</c> 是"图标的数据描述"，由宿主按需物化。
/// </para>
/// </remarks>
internal static class IconElements
{
    /// <summary>把图标元素翻成真的 <c>IconElement</c>；不认识的返回 <c>null</c> 并留痕。</summary>
    public static IconElement? From(Element element)
    {
        switch (element)
        {
            case FontIconElement font:
                return FromFont(font);

            case BitmapIconElement bitmap:
                // URI 非法时是<b>静默失败</b>（见 PackUri）：这里给 null，
                // 宿主那个槽位就是没有图标，不抛。
                return PackUri.TryCreate(bitmap.UriSource) is { } uri
                    ? new BitmapIcon { UriSource = uri, ShowAsMonochrome = bitmap.ShowAsMonochrome }
                    : null;

            case SymbolIconElement symbol:
                return new SymbolIcon { Symbol = symbol.Symbol };

            case ImageIconElement image:
                // WinUI 2 的 ImageIcon：内部是 Image，原色 + 按尺寸缩放
                // （与 UWP 原生的 BitmapIcon 是两个控件，别互相顶替）。
                return PackUri.TryCreate(image.UriSource) is { } imageUri
                    ? new MuxControls.ImageIcon { Source = new BitmapImage(imageUri) }
                    : null;

            default:
                ReactorApplication.Trace(
                    $"[reactor] 图标只收 FontIcon / BitmapIcon / SymbolIcon / ImageIcon，" +
                    $"收到 {element.GetType().Name}，已忽略");
                return null;
        }
    }

    /// <summary>
    /// 两个图标元素的<b>形状</b>是不是同一个（同族、同一个 glyph / Uri / 符号）。
    /// </summary>
    /// <remarks>
    /// 图标是<b>内容槽</b>不是值：工厂每帧 <c>new</c> 一个，按引用比必然每次都"变了"。
    /// 逐帧重建 <c>IconElement</c> 会让宿主重新应用一次图标（观感上就是闪），
    /// 所以宿主那一侧要比形状、形状没变就一个字都不写。
    /// <para>
    /// 只比"决定长什么样"的那几个字段，不比 <c>FontSize</c> 之类——后者在
    /// <see cref="FromFont"/> 里是"给了才写"的可选旋钮，形状相同而字号不同时
    /// 走的是另一条路（换实例），这里不掺和。
    /// </para>
    /// </remarks>
    public static bool SameShape(Element? a, Element? b) => (a, b) switch
    {
        (FontIconElement x, FontIconElement y) => x.Glyph == y.Glyph,
        (BitmapIconElement x, BitmapIconElement y) =>
            x.UriSource == y.UriSource && x.ShowAsMonochrome == y.ShowAsMonochrome,
        (SymbolIconElement x, SymbolIconElement y) => x.Symbol == y.Symbol,
        (ImageIconElement x, ImageIconElement y) => x.UriSource == y.UriSource,
        (null, null) => true,
        _ => false,
    };

    private static IconElement FromFont(FontIconElement font)
    {
        var glyph = new FontIcon { Glyph = font.Glyph };

        if (font.FontSize is { } size)
        {
            glyph.FontSize = size;
        }

        if (font.FontFamily is { } family)
        {
            glyph.FontFamily = new FontFamily(family);
        }

        return glyph;
    }
}
