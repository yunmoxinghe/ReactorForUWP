using System;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

/// <summary>
/// A4 基础控件验收页：TextBox / CheckBox / Slider 的受控/非受控语义。
/// 验收点：
/// 1. 受控 TextBox：输入 → onChanged → setState → 回写（正向），「重置文本」按钮验证反向驱动；
/// 2. 非受控 TextBox：不带 value（Optional.Unset），控件自主持仓，自由输入；
/// 3. 受控 CheckBox：bool? 状态，勾选/取消触发回显；
/// 4. 受控 Slider：「设为 80」按钮验证反向驱动 + 拖动验证正向。
/// </summary>
public sealed class A4Demo : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("Hello");
        var (isChecked, setIsChecked) = UseState<bool?>(true);
        var (sliderValue, setSliderValue) = UseState(50.0);

        return VStack(
            TextBlock("A4 受控控件验证") with { Key = "title" },

            // 受控 TextBox：双向绑定 + 反向驱动
            TextBox(text, setText, "请输入文本", header: "受控 TextBox") with { Key = "text" },
            TextBlock($"Text 回显: {text}") with { Key = "text-echo" },
            Button("重置文本为 'reset'", () => setText("reset")) with { Key = "btn-reset" },

            // 非受控 TextBox：value=Unset，验证 Optional 的「未传入」语义
            TextBox(placeholderText: "非受控输入，自由输入") with { Key = "text-free" },

            // 受控 CheckBox：bool? 状态
            CheckBox(isChecked, v => setIsChecked(v), "启用开关") with { Key = "check" },
            TextBlock($"Check 回显: {isChecked}") with { Key = "check-echo" },

            // 受控 Slider：正向（拖动）+ 反向（按钮设值）
            Slider(sliderValue, 0, 100, setSliderValue) with { Key = "slider" },
            TextBlock($"Slider 回显: {sliderValue:0.0}") with { Key = "slider-echo" },
            Button("Slider 设为 80", () => setSliderValue(80.0)) with { Key = "btn-slider" }
        );
    }
}