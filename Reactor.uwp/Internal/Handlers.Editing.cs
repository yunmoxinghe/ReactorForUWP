using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// WinUI 2 的 <c>ColorPicker</c>：受控 <c>Color</c>。
/// </summary>
/// <remarks>
/// <para>
/// 形与 <c>DatePicker.Date</c> 一致：写 <c>Color</c> 会同步抛 <c>ColorChanged</c>，
/// 那一发的作者是我们，靠 <see cref="EchoGuard"/> 认下来。
/// </para>
/// <para>
/// <b>那几个开关为什么全罩进静默窗。</b><c>IsAlphaEnabled</c> 关掉会把 A 拉到 255，
/// <c>ColorSpectrumComponents</c> 换掉会把颜色投影到新的两轴上——与
/// <c>DatePicker</c> 的 <c>MinYear</c> 同形：那一发抛在<b>旧订阅还挂着</b>的时候
/// （<c>Rebind</c> 在最后才换回调），且夹出来的值事先不知道，没法用
/// <c>Expect</c> 配它。窗在"永远没等到事件"时的代价是零，漏罩则是一发假回调，
/// 代价不对称。
/// </para>
/// </remarks>
internal sealed class ColorPickerHandler : ElementHandler<ColorPickerElement, MuxControls.ColorPicker>
{
    private static readonly EchoGuard ColorEcho = new();

    private static readonly WeakTable<MuxControls.ColorPicker,
        Windows.Foundation.TypedEventHandler<
            MuxControls.ColorPicker, MuxControls.ColorChangedEventArgs>> Handlers = new();

    protected override MuxControls.ColorPicker Mount(Reconciler reconciler, ColorPickerElement element)
    {
        var control = new MuxControls.ColorPicker
        {
            IsAlphaEnabled = element.IsAlphaEnabled,
            IsAlphaSliderVisible = element.IsAlphaSliderVisible,
            IsHexInputVisible = element.IsHexInputVisible,
            IsColorSliderVisible = element.IsColorSliderVisible,
            IsColorSpectrumVisible = element.IsColorSpectrumVisible,
            ColorSpectrumShape = element.Shape,
            ColorSpectrumComponents = element.Components,
            IsMoreButtonVisible = element.IsMoreButtonVisible,
        };

        if (element.Color is { } color)
        {
            control.Color = color;
        }

        Rebind(control, element.OnColorChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        ColorPickerElement oldElement,
        ColorPickerElement newElement,
        MuxControls.ColorPicker control)
    {
        // 会把受控值夹走的那一批：罩窗（理由见类注释）。
        using (ColorEcho.Silence(control))
        {
            PropWriter.Set(
                oldElement.IsAlphaEnabled,
                newElement.IsAlphaEnabled,
                value => control.IsAlphaEnabled = value);
            PropWriter.Set(
                oldElement.IsAlphaSliderVisible,
                newElement.IsAlphaSliderVisible,
                value => control.IsAlphaSliderVisible = value);
            PropWriter.Set(
                oldElement.IsHexInputVisible,
                newElement.IsHexInputVisible,
                value => control.IsHexInputVisible = value);
            PropWriter.Set(
                oldElement.IsColorSliderVisible,
                newElement.IsColorSliderVisible,
                value => control.IsColorSliderVisible = value);
            PropWriter.Set(
                oldElement.IsColorSpectrumVisible,
                newElement.IsColorSpectrumVisible,
                value => control.IsColorSpectrumVisible = value);
            PropWriter.Set(
                oldElement.Shape, newElement.Shape, value => control.ColorSpectrumShape = value);
            PropWriter.Set(
                oldElement.Components,
                newElement.Components,
                value => control.ColorSpectrumComponents = value);
        }

        // 它不动 Color（只管那一片展不展开），所以按"改不动受控值"登记为惰性，
        // 而不是塞进上面那个窗——那个窗是给"会把颜色夹走"的开关用的。
        PropWriter.Set(
            oldElement.IsMoreButtonVisible,
            newElement.IsMoreButtonVisible,
            value => control.IsMoreButtonVisible = value);

        if (newElement.Color is not { } target)
        {
            Rebind(control, newElement.OnColorChanged);
            return;
        }

        if (Equals(control.Color, target))
        {
            Rebind(control, newElement.OnColorChanged);
            return;
        }

        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 ColorPicker{CtlId.Tag(control)}: #{control.Color.R:X2}{control.Color.G:X2}" +
            $"{control.Color.B:X2} → #{target.R:X2}{target.G:X2}{target.B:X2}");

        ColorEcho.Expect(control, target);
        control.Color = target;

        // 回调为空时订阅不存在，这一发没人领 → 撤销登记。
        ColorEcho.CancelIfUnconsumed(control);

        Rebind(control, newElement.OnColorChanged);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.ColorPicker control)
    {
        ColorEcho.Forget(control);
        Rebind(control, null);
    }

    private static void Rebind(MuxControls.ColorPicker control, Action<Color>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.ColorChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        Windows.Foundation.TypedEventHandler<
                MuxControls.ColorPicker, MuxControls.ColorChangedEventArgs> handler =
            (_, args) =>
            {
                if (ColorEcho.Consume(control, args.NewColor))
                {
                    return;
                }

                callback(args.NewColor);
            };

        control.ColorChanged += handler;
        Handlers.Set(control, handler);
    }
}

/// <summary>
/// UWP 原生的 <c>RichEditBox</c>：带格式的文本编辑。<b>文本只出不进。</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>文本住在 <c>Document</c> 里，不在 <c>Text</c> 属性上。</b>官方
/// <c>RichEditBox</c> <b>没有</b> <c>Text</c> 属性（实测：赋值 <c>b.Text = "x"</c>
/// 直接 CS1061），读写走 <c>Document.GetText</c> / <c>Document.SetText</c>。
/// </para>
/// <para>
/// <b>为什么这里不装成受控。</b>受控的前提是"写进去的值"与"回读出来的值"是同一个
/// 东西。<c>GetText</c> 以 <c>\r</c> 作段落符，末尾那一个是<b>文档结构</b>
/// （写进去 <c>"abc"</c>、读出来 <c>"abc\r"</c>）——剥掉它只是<b>我们自造的归一化</b>，
/// 控件并不认这份约定。把受控建立在这条只有本框架知道的规则上，与
/// <c>CalendarViewHandler</c> 那句"没有回执通道能把两份状态对齐到可判定"是同一条
/// 规矩：<b>没有可判定的落点，就不装成受控</b>。
/// </para>
/// <para>
/// 于是这里<b>不接 <c>EchoGuard</c></b>：<c>InitialText</c> 只在挂载时写一次
/// （那一刻订阅还没挂上，写了也没人听见），之后 <c>Update</c> 只换回调与配置，
/// 文本一律只出不进。
/// </para>
/// <para>
/// 下面这行是给静态检查看的：<c>InitialText</c> 只在 <c>Mount</c> 里被读，
/// <c>PropertyDriftTests</c> 认这个登记才会放过；让它合法的理由写在上面那段里——
/// 文本一旦能回头写，受控就得建在"剥掉末尾 <c>\r</c>"这条只有本框架知道的
/// 归一化上，而控件并不认这份约定。
/// </para>
/// </remarks>
// MOUNT-ONLY: InitialText
internal sealed class RichEditBoxHandler : ElementHandler<RichEditBoxElement, RichEditBox>
{
    private static readonly WeakTable<RichEditBox, RoutedEventHandler> Handlers = new();

    protected override RichEditBox Mount(Reconciler reconciler, RichEditBoxElement element)
    {
        var control = new RichEditBox
        {
            IsReadOnly = element.IsReadOnly,
            AcceptsReturn = element.AcceptsReturn,
            IsSpellCheckEnabled = element.IsSpellCheckEnabled,
        };

        if (element.Header is { } header)
        {
            control.Header = header;
        }

        if (element.PlaceholderText is { } placeholder)
        {
            control.PlaceholderText = placeholder;
        }

        // 订阅之前写：这一发没人听见（与别处"挂载期写"同一条理由）。
        if (element.InitialText is { } text)
        {
            control.Document.SetText(TextSetOptions.None, text);
        }

        Rebind(control, element.OnTextChanged);
        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        RichEditBoxElement oldElement,
        RichEditBoxElement newElement,
        RichEditBox control)
    {
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);
        PropWriter.Set(
            oldElement.PlaceholderText,
            newElement.PlaceholderText,
            value => control.PlaceholderText = value ?? string.Empty);
        PropWriter.Set(
            oldElement.IsReadOnly, newElement.IsReadOnly, value => control.IsReadOnly = value);
        PropWriter.Set(
            oldElement.AcceptsReturn,
            newElement.AcceptsReturn,
            value => control.AcceptsReturn = value);
        PropWriter.Set(
            oldElement.IsSpellCheckEnabled,
            newElement.IsSpellCheckEnabled,
            value => control.IsSpellCheckEnabled = value);

        // 文本不写回（理由见类注释）：这里只把回调换成捕获了新 state 的闭包。
        Rebind(control, newElement.OnTextChanged);
    }

    protected override void Unmount(Reconciler reconciler, RichEditBox control)
    {
        if (Handlers[control] is { } existing)
        {
            control.TextChanged -= existing;
            Handlers.Remove(control);
        }
    }

    /// <summary>
    /// 读文本，剥掉末尾那个段落符。事件参数不带文本，只能回读。
    /// 多段文本里<b>中间</b>那些 <c>\r</c> 保留——它们是真的换行。
    /// </summary>
    private static string Read(RichEditBox control)
    {
        control.Document.GetText(TextGetOptions.None, out var text);

        return text.Length > 0 && text[^1] == '\r' ? text[..^1] : text;
    }

    private static void Rebind(RichEditBox control, Action<string>? callback)
    {
        if (Handlers[control] is { } existing)
        {
            control.TextChanged -= existing;
            Handlers.Remove(control);
        }

        if (callback is null)
        {
            return;
        }

        RoutedEventHandler handler = (_, _) => callback(Read(control));

        control.TextChanged += handler;
        Handlers.Set(control, handler);
    }
}
