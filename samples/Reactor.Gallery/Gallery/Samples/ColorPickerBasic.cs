using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using MuxControls = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>ColorPicker</c>：受控 <c>Color</c>。
/// </summary>
/// <remarks>
/// <para>
/// 受控在这里的表现与别处同形：拖光谱 → <c>ColorChanged</c> → <c>setColor</c>
/// → 下一轮写回去，而写回去那一趟被框架认成回声，不会再回调一次。
/// </para>
/// <para>
/// <b>「开/关透明度」这个开关会把颜色夹走。</b>关掉 <c>IsAlphaEnabled</c> 时官方会把
/// A 拉到 255——那一发 <c>ColorChanged</c> 的作者是我们，而且事先不知道会被夹成
/// 什么，所以框架在写这类开关时罩了一层静默窗。换 <c>ColorSpectrumComponents</c>
/// 同理（颜色被投影到新的两轴上）。
/// </para>
/// </remarks>
public sealed class ColorPickerBasic : Component
{
    private static readonly MuxControls.ColorSpectrumComponents[] Components =
    {
        MuxControls.ColorSpectrumComponents.HueSaturation,
        MuxControls.ColorSpectrumComponents.HueValue,
        MuxControls.ColorSpectrumComponents.SaturationValue,
    };

    public override Element Render()
    {
        var (color, setColor) = UseState(Color.FromArgb(255, 0, 120, 212));
        var (alpha, setAlpha) = UseState(false);
        var (axis, setAxis) = UseState(0);
        var (more, setMore) = UseState(true);

        return VStack(12,
            HStack(12,
                ColorPicker(
                    color: color,
                    onColorChanged: setColor,
                    isAlphaEnabled: alpha,
                    components: Components[axis],
                    isMoreButtonVisible: more),

                VStack(8,
                    TextBlock("当前颜色").Body(),
                    TextBlock($"#{color.R:X2}{color.G:X2}{color.B:X2}")
                        .Background(color)
                        .Padding(16, 8)
                        .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),
                    TextBlock($"A = {color.A}").Caption().Subtle(),
                    Button(alpha ? "关掉透明度" : "打开透明度", () => setAlpha(!alpha)),
                    Button($"光谱两轴：{Components[axis]}",
                        () => setAxis((axis + 1) % Components.Length)),
                    Button(more ? "关掉「更多」按钮" : "打开「更多」按钮", () => setMore(!more)))),

            TextBlock("「打开透明度」会多出一条 A 滑杆；关掉它时官方把 A 拉到 255 —— "
                      + "那一发不是用户输入，框架在静默窗里写这类开关。")
                .Caption().Subtle().Wrap(),

            TextBlock("关掉「更多」按钮之后，A 滑杆与十六进制框那一片反而<b>常驻</b>了："
                      + "没有按钮可折叠，就只好一直摊开——想要「永远不展开」得把它连同那几个 "
                      + "Visible 一起关掉。")
                .Caption().Subtle().Wrap());
    }
}
