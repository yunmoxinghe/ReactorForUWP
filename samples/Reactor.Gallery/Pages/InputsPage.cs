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

        // x:Uid 取证用：resw 里的 DiagUidBox/Text 会在<b>订阅之后</b>写进这个框
        // （ApplyUid 排在 Build 末尾）。那一笔写没有回声登记，会被当成用户输入
        // 回调出去 —— 界面上是"初始值被 resw 顶掉，还多一次 OnChanged"。
        // 修好之后计数该停在 0：挂载期的回执不算用户输入。
        var (uidText, setUidText) = UseState("代码里的初始值");
        var (uidHits, setUidHits) = UseState(0);

        // 改区间取证：把上限压到当前值<b>以下</b>，控件会把 Value 夹到新上限，
        // 那一发 ValueChanged 抛在**旧订阅还挂着**的时候（Rebind 在 Update 最后才退订）。
        // 修好之前它会被当成用户输入回调出去：计数 +1、state 被改成 40 ——
        // "没人拖过滑块，值却变了"。修好之后计数不该涨。
        var (rangeMax, setRangeMax) = UseState(100.0);
        var (rangeValue, setRangeValue) = UseState(80.0);
        var (rangeHits, setRangeHits) = UseState(0);

        return ScrollViewer(
            VStack(12,
                TextBlock("输入与选择").FontSize(20),

                TextBox(
                    Optional<string>.Of(text),
                    setText,
                    placeholderText: "随便打点什么",
                    header: "文本框"),
                TextBlock($"当前输入：{text}").Caption(),

                TextBlock("x:Uid 文本框（挂载时不该回调）").FontSize(16),
                TextBox(
                    Optional<string>.Of(uidText),
                    v =>
                    {
                        setUidHits(uidHits + 1);
                        setUidText(v);
                    },
                    placeholderText: "本地化占位提示",
                    header: "本地化文本框")
                    .Uid("DiagUidBox"),
                TextBlock($"OnChanged 次数：{uidHits}（挂载时不该涨）；当前值：{uidText}").Caption(),
                Button("回调计数归零", () => setUidHits(0)),

                ComboBox(Colors, Optional<int>.Of(combo), setCombo)
                    .AutomationName("颜色选择"),
                TextBlock($"选中：{Colors[combo]}").Caption(),

                ToggleSwitch(Optional<bool>.Of(isOn), setIsOn, "开", "关", "开关"),
                CheckBox(Optional<bool?>.Of(agreed), v => setAgreed(v), "我同意"),

                Slider(Optional<double>.Of(volume), 0, 100, setVolume),
                TextBlock($"音量：{volume:F0}").Caption(),

                TextBlock("改区间：把上限压到当前值以下").FontSize(16),
                Slider(
                    Optional<double>.Of(rangeValue),
                    0,
                    rangeMax,
                    v =>
                    {
                        setRangeHits(rangeHits + 1);
                        setRangeValue(v);
                    }),
                TextBlock($"当前值：{rangeValue:F0}　上限：{rangeMax:F0}　" +
                          $"OnValueChanged 次数：{rangeHits}").Caption(),
                HStack(8,
                    Button("上限 → 40（小于当前值 80）", () => setRangeMax(40)),
                    Button("上限 → 100", () => setRangeMax(100)),
                    Button("回调计数归零", () => setRangeHits(0))),
                TextBlock("点『上限 → 40』时次数不该涨；涨了 = 夹取那一发被当成用户输入" +
                          "（见 release-notes 第 16 节）。state 仍是 80、滑块停在 40 是预期的：" +
                          "声明值越界时控件只会夹到边界。").Caption(),

                RadioButtons(new[] { "第一", "第二", "第三" }, Optional<int>.Of(radio), setRadio),
                TextBlock($"单选：{radio}").Caption(),

                TextBlock("进度条（null = 不确定进度）").FontSize(16),
                Progress(volume),
                ProgressRing(volume)
            ).Padding(16));
    }
}
