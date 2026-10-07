using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 数值类控件：<c>Slider</c> / <c>NumberBox</c> / <c>Progress</c>。
/// </summary>
/// <remarks>
/// <b>拖动是连续回调</b>：鼠标拖动期间 <c>ValueChanged</c> 会连续触发，
/// 因此回调里必须是廉价的动作——直接绑到一个昂贵操作（重渲染一大棵子树、
/// 发一次网络请求）会让拖动整段卡住。这里只改一行文字，所以直接 setState。
/// <para>
/// <c>Progress</c> 传 <c>null</c> 得到不确定进度那个形态（动起来的那条）；
/// 传数值则是确定进度。二者是同一个 XAML 控件的两种状态，不是一个额外的控件。
/// </para>
/// </remarks>
public sealed class SliderAndProgress : Component
{
    public override Element Render()
    {
        var (volume, setVolume) = UseState(30.0);

        return VStack(10,
            Slider(Optional<double>.Of(volume), 0, 100, setVolume),
            TextBlock($"音量：{volume:F0}%").Caption().Subtle(),

            NumberBox(Optional<double>.Of(volume), setVolume, header: "同上，另一种输入方式"),

            TextBlock("进度").Body(),
            Progress(volume),
            ProgressRing());
    }
}
