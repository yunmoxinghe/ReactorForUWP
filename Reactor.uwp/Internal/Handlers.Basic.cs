using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>TextBlock。注意参数名对齐官方：Content 而不是 Text。</summary>
internal sealed class TextBlockHandler : ElementHandler<TextBlockElement, TextBlock>
{
    protected override TextBlock Mount(Reconciler reconciler, TextBlockElement element) =>
        new() { Text = element.Content };

    protected override void Update(
        Reconciler reconciler,
        TextBlockElement oldElement,
        TextBlockElement newElement,
        TextBlock control) =>
        PropWriter.Set(oldElement.Content, newElement.Content, value => control.Text = value);
}

internal sealed class ButtonHandler : ElementHandler<ButtonElement, Button>
{
    protected override Button Mount(Reconciler reconciler, ButtonElement element)
    {
        var button = new Button();
        ApplyContent(reconciler, button, null, element);
        reconciler.RebindButtonClick(button, element.OnClick);
        return button;
    }

    protected override void Update(
        Reconciler reconciler,
        ButtonElement oldElement,
        ButtonElement newElement,
        Button control)
    {
        ApplyContent(reconciler, control, oldElement, newElement);
        reconciler.RebindButtonClick(control, newElement.OnClick);
    }

    protected override void Unmount(Reconciler reconciler, Button control) =>
        reconciler.RebindButtonClick(control, null);

    protected override Element? SingleChildOf(ButtonElement element) => element.Content;

    /// <summary>
    /// 内容落到 <c>Button.Content</c>：<see cref="ButtonElement.Content"/> 优先，
    /// 没有则退成纯文本的 <see cref="ButtonElement.Label"/>。
    /// </summary>
    /// <remarks>
    /// 元素内容是<b>内容槽</b>而不是值：这里走 <c>PatchSingleChild</c>，能就地更新就
    /// 就地更新（整套重建会丢掉正在播放的视觉状态，看起来就是闪）。
    /// </remarks>
    private static void ApplyContent(
        Reconciler reconciler,
        Button control,
        ButtonElement? oldElement,
        ButtonElement newElement)
    {
        if (newElement.Content is not null || oldElement?.Content is not null)
        {
            reconciler.PatchSingleChild(control, oldElement?.Content, newElement.Content);
            return;
        }

        PropWriter.Set(oldElement?.Label, newElement.Label, value => control.Content = value);
    }
}

internal sealed class TextBoxHandler : ElementHandler<TextBoxElement, TextBox>
{
    /// <summary>
    /// <c>Text</c> 是受控属性：用户输入 → 回调 → setState → 重渲染再写回控件。
    /// 没有回声抑制就会变成"写 → 事件 → 回调 → 再写"的回环。
    /// </summary>
    private static readonly EchoGuard TextEcho = new();

    protected override TextBox Mount(Reconciler reconciler, TextBoxElement element)
    {
        var native = new TextBox
        {
            PlaceholderText = element.PlaceholderText ?? string.Empty,
            Header = element.Header,
        };

        if (element.Value.HasValue)
        {
            native.Text = element.Value.Value ?? string.Empty;
        }

        ApplyShape(native, element);
        reconciler.RebindTextChanged(native, element.OnChanged);
        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        TextBoxElement oldElement,
        TextBoxElement newElement,
        TextBox control)
    {
        PropWriter.Set(
            oldElement.PlaceholderText,
            newElement.PlaceholderText,
            value => control.PlaceholderText = value ?? string.Empty);
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);

        // MaxLength 写下去时旧订阅还挂着。它不一定会动 Text（官方文档只说"限制输入
        // 长度"，没说会截断已有文本），但**万一**控件把已有文本截短并抛 TextChanged，
        // 那一发同样是渲染路径内部产生的、不可能是用户输入 —— 与 Slider 那边
        // "夹取家族"同一个理由，开一段静默窗。
        using (TextEcho.Silence(control))
        {
            ApplyShape(control, newElement);
        }

        if (newElement.Value.HasValue)
        {
            var text = newElement.Value.Value ?? string.Empty;
            if (!string.Equals(control.Text, text, StringComparison.Ordinal))
            {
                TextEcho.Expect(control, text);
                control.Text = text;

                // 写入完了若没等到回声，撤销登记。回调为空时 Rebind(null) 会先退订再
                // return（Reconciler.cs:1013-1029），这一发 Consume 连一次都不会被调用；
                // 留着就成了陈旧期望，等用户改回同一个值会被认成回声吞掉。
                TextEcho.CancelIfUnconsumed(control);
            }
        }

        reconciler.RebindTextChanged(
            control,
            newElement.OnChanged is null
                ? null
                : value =>
                {
                    if (TextEcho.Consume(control, value))
                    {
                        return;
                    }

                    newElement.OnChanged(value);
                });
    }

    protected override void Unmount(Reconciler reconciler, TextBox control)
    {
        TextEcho.Forget(control);
        reconciler.RebindTextChanged(control, null);
    }

    /// <summary>
    /// 输入框"形态"那几样：回车语义 / 拼写检查 / 长度上限 / 只读。
    /// </summary>
    /// <remarks>
    /// 全都是"给了才写"（<c>null</c> = 不动它），所以不传就是官方默认值——
    /// 这与 <c>Value</c> 那套 <see cref="Optional{T}"/> 的三态不是一回事：
    /// 这几个属性<b>没有受控语义</b>，用户改不动它们，本库也就不需要回声抑制。
    /// </remarks>
    private static void ApplyShape(TextBox control, TextBoxElement element)
    {
        if (element.AcceptsReturn is { } returns && control.AcceptsReturn != returns)
        {
            control.AcceptsReturn = returns;
        }

        if (element.IsSpellCheckEnabled is { } spell && control.IsSpellCheckEnabled != spell)
        {
            control.IsSpellCheckEnabled = spell;
        }

        if (element.MaxLength is { } max && control.MaxLength != max)
        {
            control.MaxLength = max;
        }

        if (element.IsReadOnly is { } readOnly && control.IsReadOnly != readOnly)
        {
            control.IsReadOnly = readOnly;
        }
    }
}

internal sealed class CheckBoxHandler : ElementHandler<CheckBoxElement, CheckBox>
{
    private static readonly EchoGuard CheckEcho = new();

    protected override CheckBox Mount(Reconciler reconciler, CheckBoxElement element)
    {
        var native = new CheckBox { Content = element.Label };

        if (element.IsChecked.HasValue)
        {
            native.IsChecked = element.IsChecked.Value;
        }

        reconciler.RebindCheckBox(native, element.OnIsCheckedChanged);
        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        CheckBoxElement oldElement,
        CheckBoxElement newElement,
        CheckBox control)
    {
        PropWriter.Set(oldElement.Label, newElement.Label, value => control.Content = value);

        if (newElement.IsChecked.HasValue && control.IsChecked != newElement.IsChecked.Value)
        {
            CheckEcho.Expect(control, newElement.IsChecked.Value);
            control.IsChecked = newElement.IsChecked.Value;

            // 同上：回调为空时 Checked/Unchecked 已被退订（Reconciler.cs:1031-1056），
            // 没人领的登记要撤销，否则下一次真实勾选会被吞。
            CheckEcho.CancelIfUnconsumed(control);
        }

        reconciler.RebindCheckBox(
            control,
            newElement.OnIsCheckedChanged is null
                ? null
                : value =>
                {
                    if (CheckEcho.Consume(control, value))
                    {
                        return;
                    }

                    newElement.OnIsCheckedChanged(value);
                });
    }

    protected override void Unmount(Reconciler reconciler, CheckBox control)
    {
        CheckEcho.Forget(control);
        reconciler.RebindCheckBox(control, null);
    }
}

internal sealed class SliderHandler : ElementHandler<SliderElement, Slider>
{
    /// <summary><c>Value</c>：拖动 → 回调 → setState → 重渲染写回，需要回声抑制。</summary>
    private static readonly EchoGuard ValueEcho = new();

    protected override Slider Mount(Reconciler reconciler, SliderElement element)
    {
        var native = new Slider
        {
            Minimum = element.Min,
            Maximum = element.Max,
        };

        if (element.Value.HasValue)
        {
            native.Value = element.Value.Value;
        }

        // 挂载期还没有订阅者在场，这两个助手因此不必罩窗——
        // 它们的<b>每个调用点</b>都得能说清这一点（第九道按调用点查，见下面各自的注释）。
        ApplySnapping(native, element);
        ApplyLook(native, element);
        reconciler.RebindSlider(native, element.OnValueChanged);
        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        SliderElement oldElement,
        SliderElement newElement,
        Slider control)
    {
        // 写 Minimum / Maximum 会把 Value 夹到新区间（RangeBase 的取值"may be coerced"：
        // 文档给的字面事实是 Minimum > 默认 Maximum 时 Maximum 被拉到等于 Minimum，
        // 说明这几个属性之间确实互相夹；Value 那一端是否同样被夹在生成代码里，
        // 本机拿不到源码，见 docs/winui2-source-notes.md 第 16 节）。
        // 那一发 ValueChanged 抛在**旧订阅还挂着**的时候（RebindSlider 在下面才退订），
        // 而我们事先不知道会被夹成什么值，所以没法用 Expect 去配它 —— 开一段静默窗：
        // 这段窗在渲染路径内部，其中的事件不可能是用户输入。
        using (ValueEcho.Silence(control))
        {
            // Value 会被 Minimum/Maximum 夹取，"声明值"和"控件值"本就可能不同，
            // 所以拿控件当前值比（SetLive 语义），而不是拿旧元素比。
            PropWriter.Set(oldElement.Min, newElement.Min, value => control.Minimum = value);
            PropWriter.Set(oldElement.Max, newElement.Max, value => control.Maximum = value);

            // 步长 / 刻度 / 吸附这三样<b>也会动 Value</b>：它们决定了 Value 被 snap 到
            // 哪一格，改一次就可能把当前值顺手挪一下（与 Min / Max 同一个"夹取"家族），
            // 所以放在同一个窗里——理由与上面那段逐字相同。
            ApplySnapping(control, newElement);
        }

        ApplyLook(control, newElement);

        // 登记/下发都用<b>夹取后</b>的值（RangePolicy 的注释里有源码依据）：
        // 写声明值会被控件夹成别的值，回读出来的那个值对不上登记 → mismatch →
        // 那一发被当成用户输入回调出去。控件的终态不变，变的只是回声变得可预测。
        if (newElement.Value.HasValue)
        {
            var target = RangePolicy.Coerce(newElement.Value.Value, control.Minimum, control.Maximum);

            if (control.Value != target)
            {
                ValueEcho.Expect(control, target);
                control.Value = target;

                // 同上（Reconciler.cs:1058-1074）：回调为空时这一发没人领，
                // 别把登记留成陈旧期望。
                ValueEcho.CancelIfUnconsumed(control);
            }
        }

        reconciler.RebindSlider(
            control,
            newElement.OnValueChanged is null
                ? null
                : value =>
                {
                    if (ValueEcho.Consume(control, value))
                    {
                        return;
                    }

                    newElement.OnValueChanged(value);
                });
    }

    protected override void Unmount(Reconciler reconciler, Slider control)
    {
        ValueEcho.Forget(control);
        reconciler.RebindSlider(control, null);
    }

    /// <summary>
    /// 会动 <c>Value</c> 的那三样（步长 / 刻度 / 吸附）——只能写在静默窗里。
    /// </summary>
    /// <remarks>
    /// <b>它的两个调用点都得能说清为什么安全</b>（第九道是按调用点查的，不是按
    /// 方法名）：一个在 <c>Mount</c> 里（那一刻订阅还没挂上，写了也没人听见），
    /// 一个在 <c>Update</c> 的静默窗里。别再往第三个地方调它。
    /// </remarks>
    private static void ApplySnapping(Slider control, SliderElement element)
    {
        if (element.StepFrequency is { } step && control.StepFrequency != step)
        {
            control.StepFrequency = step;
        }

        if (element.TickFrequency is { } tick && control.TickFrequency != tick)
        {
            control.TickFrequency = tick;
        }

        if (element.SnapsTo is { } snaps && control.SnapsTo != snaps)
        {
            control.SnapsTo = snaps;
        }
    }

    /// <summary>
    /// 不动 <c>Value</c> 的那几样（标题 / 方向 / 刻度画在哪 / 拇指气泡 /
    /// 值增大的方向）。
    /// </summary>
    /// <remarks>
    /// <c>Orientation</c> 默认横向，而元素上的默认值也是横向——"声明值恰好等于
    /// 控件默认值"时这里会跳过写入，那正是想要的结果（同值不写 = 不产生变化通知）。
    /// <para>
    /// 这五样<b>已按（类名, 属性名）登记为惰性</b>（<c>EchoContractTests</c> 的
    /// <c>InertByDesign</c>）：Value 由 <c>Minimum</c> / <c>Maximum</c> /
    /// <c>StepFrequency</c> / <c>SnapsTo</c> 那条链决定，换标题、换方向、
    /// 换刻度画在哪、关掉气泡、把方向反过来都不在那条链上，改不动 Value。
    /// </para>
    /// </remarks>
    private static void ApplyLook(Slider control, SliderElement element)
    {
        PropWriter.Set(control.Header as string, element.Header, value => control.Header = value);

        if (control.Orientation != element.Orientation)
        {
            control.Orientation = element.Orientation;
        }

        if (element.TickPlacement is { } placement && control.TickPlacement != placement)
        {
            control.TickPlacement = placement;
        }

        if (element.IsThumbToolTipEnabled is { } thumb && control.IsThumbToolTipEnabled != thumb)
        {
            control.IsThumbToolTipEnabled = thumb;
        }

        if (control.IsDirectionReversed != element.IsDirectionReversed)
        {
            control.IsDirectionReversed = element.IsDirectionReversed;
        }
    }
}

/// <summary>WinUI 2 的 InfoBar：验证 XamlControlsResources 纯代码加载链路。</summary>
/// <remarks>
/// <c>IsOpen</c> 是<b>种子值</b>，只在 <c>Mount</c> 写一次、<c>Update</c> 里故意不认：
/// 用户点了 × 把它关掉之后，下一轮重渲染<b>不该</b>再把它打开——那不是状态同步，
/// 那是把用户的操作撤回。下面那行登记是<b>有意如此</b>，不是"忘记下发"；
/// 不登记的话 <c>PropertyDriftTests</c> 会当成属性漂移报警。
/// </remarks>
// MOUNT-ONLY: IsOpen
internal sealed class InfoBarHandler : ElementHandler<InfoBarElement, MuxControls.InfoBar>
{
    protected override MuxControls.InfoBar Mount(Reconciler reconciler, InfoBarElement element) =>
        new()
        {
            Message = element.Message,
            Severity = element.Severity,
            IsIconVisible = element.IsIconVisible,
            // Title 在官方是 hstring，不给就写空串（与"不显示标题"等价），
            // 别把 null 递给 WinRT 属性。
            Title = element.Title ?? string.Empty,
            IsClosable = element.IsClosable,
            IsOpen = element.IsOpen,
        };

    protected override void Update(
        Reconciler reconciler,
        InfoBarElement oldElement,
        InfoBarElement newElement,
        MuxControls.InfoBar control)
    {
        PropWriter.Set(oldElement.Message, newElement.Message, value => control.Message = value);
        PropWriter.Set(oldElement.Severity, newElement.Severity, value => control.Severity = value);
        PropWriter.Set(
            oldElement.IsIconVisible,
            newElement.IsIconVisible,
            value => control.IsIconVisible = value);
        PropWriter.Set(
            oldElement.Title,
            newElement.Title,
            value => control.Title = value ?? string.Empty);
        PropWriter.Set(
            oldElement.IsClosable,
            newElement.IsClosable,
            value => control.IsClosable = value);

        // IsOpen 是「种子值」属性（官方 Initial / InitialOnly 语义）：只在 mount 写一次，
        // 之后归控件自己——用户按了关闭按钮就不该被下一轮重渲染重新打开。
        // 同理它不在这里下发：动态改 IsOpen 不会生效，想重新打开得换一个元素实例。
    }
}

/// <summary>
/// 徽章（WinUI 2.8 的 <c>InfoBadge</c>）。
/// </summary>
/// <remarks>
/// <b>这里没有受控值，也不需要回声抑制。</b><c>InfoBadge</c> 不接收用户输入——
/// 它只有"显示什么"这一件事，没有事件可抛。因此这一处的 <c>Value</c> 是
/// 单向的"声明值"，与 <c>Slider.Value</c> 那种"用户改 → 回调 → 写回"不是一回事。
/// <para>
/// 图标走 <c>IconSource</c>（不是 <c>IconElement</c>）：与 <c>TabViewItem.IconSource</c>
/// 同一个规矩，它收的是<b>数据</b>而不是可视元素。所以元素是 <c>FontIcon</c> /
/// <c>BitmapIcon</c>，落到控件上要翻译成 <c>FontIconSource</c> / <c>BitmapIconSource</c>。
/// 能就地改就不换实例：换一个 <c>IconSource</c> 会触发一次完整的重新应用。
/// </para>
/// </remarks>
internal sealed class InfoBadgeHandler : ElementHandler<InfoBadgeElement, MuxControls.InfoBadge>
{
    protected override MuxControls.InfoBadge Mount(Reconciler reconciler, InfoBadgeElement element)
    {
        var badge = new MuxControls.InfoBadge { Value = element.Value };
        ApplyStyle(badge, element.BadgeStyle);
        ApplyIcon(badge, element.Icon);
        return badge;
    }

    protected override void Update(
        Reconciler reconciler,
        InfoBadgeElement oldElement,
        InfoBadgeElement newElement,
        MuxControls.InfoBadge control)
    {
        PropWriter.Set(oldElement.Value, newElement.Value, value => control.Value = value);
        PropWriter.Set(
            oldElement.BadgeStyle,
            newElement.BadgeStyle,
            value => ApplyStyle(control, value));

        // 图标是<b>内容槽</b>，不是值：元素每帧都是新的（工厂里 new 出来的），
        // 按引用比必然每次都"变了"，于是每帧重建一次 IconSource → 闪。
        // 比的是<b>形状</b>（同一种图标、同一个 glyph/Uri），形状没变就只改字段。
        if (!SameIcon(oldElement.Icon, newElement.Icon))
        {
            ApplyIcon(control, newElement.Icon);
        }
    }

    /// <summary>
    /// 套预设样式。
    /// </summary>
    /// <remarks>
    /// <b>预设样式是 XAML 资源，不是静态属性。</b>XAML 里写的是
    /// <c>Style="{StaticResource SuccessBadgeStyle}"</c>——那几个 Style 定义在
    /// <c>InfoBadge</c> 的主题资源字典里，随 <c>XamlControlsResources</c> 一起加载。
    /// 纯代码建控件没有 <c>StaticResource</c> 这个编译期概念，只能按 key 去
    /// <c>Application.Current.Resources</c> 里取。
    /// <para>
    /// <b>取不到就降级，不抛。</b>资源没加载到位时（例如宿主没放
    /// <c>XamlControlsResources</c>）徽章仍是默认外观（就是 Informational 那一档），
    /// 只是换不了色；留一行 Trace 让人查得到，但绝不因此让页面挂掉——
    /// 一个装饰性控件不该有这种杀伤力。
    /// </para>
    /// </remarks>
    private static void ApplyStyle(MuxControls.InfoBadge badge, string style)
    {
        var key = ParseStyle(style) switch
        {
            BadgeStyleKind.Success => "SuccessBadgeStyle",
            BadgeStyleKind.Warning => "WarningBadgeStyle",
            BadgeStyleKind.Critical => "CriticalBadgeStyle",
            BadgeStyleKind.Attention => "AttentionBadgeStyle",
            _ => "InformationalBadgeStyle",
        };

        if (Application.Current?.Resources is { } resources &&
            resources.TryGetValue(key, out var found) && found is Style preset)
        {
            badge.Style = preset;
            return;
        }

        if (!MissingStyles.Contains(key))
        {
            MissingStyles.Add(key);
            ReactorApplication.Trace(
                $"[reactor] InfoBadge 预设样式 {key} 没取到（XamlControlsResources 加载了吗？），用默认外观");
        }
    }

    /// <summary>"这个 key 已经报过一次了"——资源缺失只会报一次，不刷屏。</summary>
    private static readonly HashSet<string> MissingStyles = new(StringComparer.Ordinal);

    private enum BadgeStyleKind
    {
        Informational,
        Success,
        Warning,
        Critical,
        Attention,
    }

    private static BadgeStyleKind ParseStyle(string? style) =>
        Enum.TryParse<BadgeStyleKind>(style, ignoreCase: true, out var kind)
            ? kind
            : BadgeStyleKind.Informational;

    private static bool SameIcon(Element? a, Element? b) => (a, b) switch
    {
        (FontIconElement x, FontIconElement y) => x.Glyph == y.Glyph,
        (BitmapIconElement x, BitmapIconElement y) =>
            x.UriSource == y.UriSource && x.ShowAsMonochrome == y.ShowAsMonochrome,
        (null, null) => true,
        _ => false,
    };

    private static void ApplyIcon(MuxControls.InfoBadge badge, Element? icon)
    {
        switch (icon)
        {
            case FontIconElement font:
                if (badge.IconSource is MuxControls.FontIconSource existing)
                {
                    if (existing.Glyph != font.Glyph)
                    {
                        existing.Glyph = font.Glyph;
                    }

                    return;
                }

                badge.IconSource = new MuxControls.FontIconSource { Glyph = font.Glyph };
                return;

            case BitmapIconElement bitmap:
                if (PackUri.TryCreate(bitmap.UriSource) is not { } uri)
                {
                    badge.IconSource = null;
                    return;
                }

                if (badge.IconSource is MuxControls.BitmapIconSource existingBitmap)
                {
                    existingBitmap.UriSource = uri;
                    existingBitmap.ShowAsMonochrome = bitmap.ShowAsMonochrome;
                    return;
                }

                badge.IconSource = new MuxControls.BitmapIconSource
                {
                    UriSource = uri,
                    ShowAsMonochrome = bitmap.ShowAsMonochrome,
                };
                return;

            default:
                badge.IconSource = null;
                return;
        }
    }

}

/// <summary>
/// 空占位。官方 EmptyElement 对应 null（不产生控件），但 UWP 侧 Build 必须返回
/// UIElement，因此退化为一个零尺寸 Grid。
/// </summary>
internal sealed class EmptyHandler : ElementHandler<EmptyElement, Grid>
{
    protected override Grid Mount(Reconciler reconciler, EmptyElement element) => new();
}

internal sealed class ScrollViewerHandler : ElementHandler<ScrollViewerElement, ScrollViewer>
{
    protected override ScrollViewer Mount(Reconciler reconciler, ScrollViewerElement element)
    {
        var native = new ScrollViewer();
        Apply(native, null, element);

        if (element.Child is not null)
        {
            native.Content = reconciler.Build(element.Child);
        }

        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        ScrollViewerElement oldElement,
        ScrollViewerElement newElement,
        ScrollViewer control)
    {
        Apply(control, oldElement, newElement);
        reconciler.PatchSingleChild(control, oldElement.Child, newElement.Child);
    }

    /// <summary>
    /// 挂载时 <paramref name="oldElement"/> 传 null：首次无条件全写；更新时逐项 diff。
    /// </summary>
    private static void Apply(
        ScrollViewer control,
        ScrollViewerElement? oldElement,
        ScrollViewerElement newElement)
    {
        if (oldElement is null)
        {
            control.HorizontalScrollBarVisibility = newElement.HorizontalScrollBar;
            control.VerticalScrollBarVisibility = newElement.VerticalScrollBar;
            control.HorizontalScrollMode = newElement.HorizontalScroll;
            control.VerticalScrollMode = newElement.VerticalScroll;
            control.HorizontalContentAlignment = newElement.HorizontalContent;
            control.ZoomMode = newElement.Zoom;
            return;
        }

        // 见 ScrollViewerElement 的注释：横向可滚动 = 用无限宽测内容 = 宽度随子项漂移。
        // 这几个属性直接决定布局测量方式，尤其不能无条件重写。
        PropWriter.Set(
            oldElement.HorizontalScrollBar,
            newElement.HorizontalScrollBar,
            value => control.HorizontalScrollBarVisibility = value);
        PropWriter.Set(
            oldElement.VerticalScrollBar,
            newElement.VerticalScrollBar,
            value => control.VerticalScrollBarVisibility = value);
        PropWriter.Set(
            oldElement.HorizontalScroll,
            newElement.HorizontalScroll,
            value => control.HorizontalScrollMode = value);
        PropWriter.Set(
            oldElement.VerticalScroll,
            newElement.VerticalScroll,
            value => control.VerticalScrollMode = value);
        PropWriter.Set(
            oldElement.HorizontalContent,
            newElement.HorizontalContent,
            value => control.HorizontalContentAlignment = value);
        PropWriter.Set(oldElement.Zoom, newElement.Zoom, value => control.ZoomMode = value);
    }
}
