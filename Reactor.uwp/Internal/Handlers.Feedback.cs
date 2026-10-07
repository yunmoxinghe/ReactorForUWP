using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// WinUI 2 的 <c>RatingControl</c>：受控 <c>Value</c>。
/// </summary>
/// <remarks>
/// 与 <c>NumberBox</c> / <c>Slider</c> 同形：写 <c>Value</c> 会同步抛
/// <c>ValueChanged</c>，那一发的作者是我们，靠 <see cref="EchoGuard"/> 认下来。
/// 三件事的顺序照抄那两个：先挂带闸的订阅，再写值，写完撤销没人领的登记。
/// <para>
/// <b>只有 <c>Value</c> 走 <c>Update</c>，其余属性只在 <c>Mount</c> 写。</b>
/// 这不是偷懒：<c>IsReadOnly</c> / <c>IsClearEnabled</c> / <c>MaxRating</c>
/// 改了不该动当前评分，而它们<b>没有回执通道</b>（改它们控件不抛 <c>ValueChanged</c>
/// ——只有 <c>MaxRating</c> 变小会夹一次，而那属于"改区间"，本版没暴露成可变的），
/// 放进 <c>Update</c> 就要为它们逐个登记惰性，收益为零。
/// <para>
/// <c>PlaceholderValue</c> 也归这一组（同样只在 <c>Mount</c> 写）：官方 Gallery
/// 里它是"设一次、看效果"的那类演示，不是跟着状态来回切的东西；真要跟着状态切，
/// 先回来把上面那条取舍重做一遍——连同 <c>Caption</c> 一起，别只补这一个。
/// </para>
/// </para>
/// </remarks>
internal sealed class RatingControlHandler : ElementHandler<RatingElement, MuxControls.RatingControl>
{
    private static readonly EchoGuard ValueEcho = new();

    private static readonly WeakTable<MuxControls.RatingControl,
        Windows.Foundation.TypedEventHandler<MuxControls.RatingControl, object>> Handlers = new();

    protected override MuxControls.RatingControl Mount(Reconciler reconciler, RatingElement element)
    {
        var control = new MuxControls.RatingControl
        {
            MaxRating = element.MaxRating,
            IsReadOnly = element.IsReadOnly,
            IsClearEnabled = element.IsClearEnabled,
            Caption = element.Caption ?? string.Empty,
        };

        if (element.PlaceholderValue is { } placeholder)
        {
            control.PlaceholderValue = placeholder;
        }

        if (element.Value.HasValue)
        {
            control.Value = element.Value.Value;
        }

        Rebind(control, element.OnValueChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        RatingElement oldElement,
        RatingElement newElement,
        MuxControls.RatingControl control)
    {
        if (!newElement.Value.HasValue)
        {
            Rebind(control, newElement.OnValueChanged);
            return;
        }

        var target = newElement.Value.Value;

        if (System.Math.Abs(control.Value - target) < 1e-9)
        {
            Rebind(control, newElement.OnValueChanged);
            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 Rating{CtlId.Tag(control)}: {control.Value} → {target}");

        ValueEcho.Expect(control, target);
        control.Value = target;

        // 回调为空时订阅不存在，这一发没人领 → 撤销登记。
        ValueEcho.CancelIfUnconsumed(control);

        Rebind(control, newElement.OnValueChanged);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.RatingControl control)
    {
        ValueEcho.Forget(control);
        Rebind(control, null);
    }

    private static void Rebind(MuxControls.RatingControl control, System.Action<double>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.ValueChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<MuxControls.RatingControl, object> handler = (_, _) =>
        {
            if (ValueEcho.Consume(control, control.Value))
            {
                return;
            }

            callback(control.Value);
        };

        control.ValueChanged += handler;
        Handlers.Set(control, handler);
    }
}

/// <summary>
/// WinUI 2 的 <c>PersonPicture</c>：人物头像。
/// </summary>
/// <remarks>
/// 它<b>没有受控值</b>——展示什么由数据决定，用户改不了它，所以这里一个
/// <c>EchoGuard</c> 都没有，写入一律走"值变了才写"。
/// </remarks>
internal sealed class PersonPictureHandler
    : ElementHandler<PersonPictureElement, MuxControls.PersonPicture>
{
    protected override MuxControls.PersonPicture Mount(
        Reconciler reconciler,
        PersonPictureElement element)
    {
        var control = new MuxControls.PersonPicture
        {
            DisplayName = element.DisplayName ?? string.Empty,
            Initials = element.Initials ?? string.Empty,
            BadgeGlyph = element.BadgeGlyph ?? string.Empty,
            BadgeText = element.BadgeText ?? string.Empty,
            IsGroup = element.IsGroup,
            PreferSmallImage = element.PreferSmallImage,
        };

        if (element.BadgeNumber is { } badge)
        {
            control.BadgeNumber = badge;
        }

        ApplySource(control, element, null);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        PersonPictureElement oldElement,
        PersonPictureElement newElement,
        MuxControls.PersonPicture control)
    {
        if (oldElement.DisplayName != newElement.DisplayName)
        {
            control.DisplayName = newElement.DisplayName ?? string.Empty;
        }

        if (oldElement.Initials != newElement.Initials)
        {
            control.Initials = newElement.Initials ?? string.Empty;
        }

        if (oldElement.BadgeNumber != newElement.BadgeNumber)
        {
            control.BadgeNumber = newElement.BadgeNumber ?? 0;
        }

        if (oldElement.BadgeGlyph != newElement.BadgeGlyph)
        {
            control.BadgeGlyph = newElement.BadgeGlyph ?? string.Empty;
        }

        if (oldElement.BadgeText != newElement.BadgeText)
        {
            control.BadgeText = newElement.BadgeText ?? string.Empty;
        }

        if (oldElement.IsGroup != newElement.IsGroup)
        {
            control.IsGroup = newElement.IsGroup;
        }

        if (oldElement.PreferSmallImage != newElement.PreferSmallImage)
        {
            control.PreferSmallImage = newElement.PreferSmallImage;
        }

        ApplySource(control, newElement, oldElement);
    }

    /// <summary>
    /// 两张图（头像 <c>ProfilePicture</c> 与角标 <c>BadgeImageSource</c>）：
    /// 只在<b>路径字符串变了</b>时重建 <c>BitmapImage</c>。
    /// </summary>
    /// <remarks>
    /// 挂载时 <paramref name="oldElement"/> 传 <c>null</c>，于是"给了路径就写、
    /// 没给就不动"——不给时不把控件已有的图抹成 <c>null</c> 是刻意的：
    /// 这一族别的属性（<c>BadgeText</c> 等）都是"没给就写空串"，那是因为它们
    /// 是同一批互斥内容，清掉才不会残留上一轮的；而图是<b>独立槽位</b>，
    /// 清掉等于把没声明的东西也删了。
    /// </remarks>
    private static void ApplySource(
        MuxControls.PersonPicture control,
        PersonPictureElement newElement,
        PersonPictureElement? oldElement)
    {
        if (oldElement?.ProfilePicture != newElement.ProfilePicture)
        {
            control.ProfilePicture = CreateSource(newElement.ProfilePicture);
        }

        if (oldElement?.BadgeImageSource != newElement.BadgeImageSource)
        {
            control.BadgeImageSource = CreateSource(newElement.BadgeImageSource);
        }
    }

    private static ImageSource? CreateSource(string? source) =>
        PackUri.TryCreate(source) is { } uri ? new BitmapImage(uri) : null;
}
