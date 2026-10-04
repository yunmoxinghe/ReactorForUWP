using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

/// <summary>
/// 输入类元素：TextBox / ComboBox / ToggleSwitch / CheckBox / Slider / RadioButtons。
/// </summary>
/// <remarks>
/// 值一律用 <c>Optional&lt;T&gt;.Of(...)</c> 传：不传（<c>default</c>）表示"这一轮不指定，
/// 保留控件当前值"，可以避免每轮渲染把用户正在输入的内容顶回去。
/// 这里演示的是受控写法（给值 + 给回调）。
/// </remarks>
public sealed class InputsPage : Component
{
    private static readonly string[] Colors = { "红色", "绿色", "蓝色" };

    public override Element Render()
    {
        var (text, setText) = UseState(string.Empty);
        var (combo, setCombo) = UseState(0);
        var (isOn, setIsOn) = UseState(false);
        var (agreed, setAgreed) = UseState(false);
        var (volume, setVolume) = UseState(30.0);
        var (radio, setRadio) = UseState(1);

        return ScrollViewer(
            VStack(12,
                TextBlock("输入与选择").FontSize(20),

                TextBox(
                    Optional<string>.Of(text),
                    setText,
                    placeholderText: "随便打点什么",
                    header: "文本框"),
                TextBlock($"当前输入：{text}").Caption(),

                ComboBox(Colors, Optional<int>.Of(combo), setCombo),
                TextBlock($"选中：{Colors[combo]}").Caption(),

                ToggleSwitch(Optional<bool>.Of(isOn), setIsOn, "开", "关", "开关"),
                CheckBox(Optional<bool?>.Of(agreed), v => setAgreed(v), "我同意"),

                Slider(Optional<double>.Of(volume), 0, 100, setVolume),
                TextBlock($"音量：{volume:F0}").Caption(),

                RadioButtons(new[] { "第一", "第二", "第三" }, Optional<int>.Of(radio), setRadio),
                TextBlock($"单选：{radio}").Caption(),

                TextBlock("进度条（null = 不确定进度）").FontSize(16),
                Progress(volume),
                ProgressRing(volume)
            ).Padding(16));
    }
}
