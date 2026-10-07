using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>NumberBox</c> 的"输入怎么被解释"那一族：表达式 / 校验模式 / 回绕 / 小数位。
/// </summary>
/// <remarks>
/// <para>
/// <b>表达式是官方替你算的。</b><c>acceptsExpression</c> 打开后输入 <c>1+2*3</c>，
/// 失焦时由 WinUI 的 <c>NumberBox</c> 自己求值再填回去——本框架不 parse 任何算式。
/// 回调里拿到的<b>已经是算完的数值</b>。
/// </para>
/// <para>
/// <b>校验模式的两种性格是一对对照。</b>两个框绑的是<b>同一个 state</b>：
/// 在 <c>Disabled</c> 那一个里输入 <c>999</c>，控件<b>什么都不做</b>——
/// <c>Value</c> 保持原样、也不回调，于是"输了 999 界面还是 100"会真的发生。
/// 这是官方给的那一档，不是 bug；<c>InvalidInputOverwritten</c>（默认）
/// 才会把它改写成边界值。
/// </para>
/// <para>
/// <c>decimalPlaces</c> 是 <c>NumberFormatter</c> 的简化入口（内部按需建一个
/// <c>DecimalFormatter</c>）。要货币符号、千分位这类更花的东西，
/// 官方那条路是直接给 <c>NumberFormatter</c> 一个对象——本库没有为它开入口，
/// 需要时走 <c>Native()</c>。
/// </para>
/// </remarks>
public sealed class NumberBoxOptions : Component
{
    public override Element Render()
    {
        var (expr, setExpr) = UseState(0.0);
        var (clamped, setClamped) = UseState(50.0);
        var (wrapped, setWrapped) = UseState(0.0);
        var (money, setMoney) = UseState(12.5);

        return VStack(12,
            TextBlock("表达式").Caption().Subtle(),
            NumberBox(Optional<double>.Of(expr), setExpr,
                header: "输 1+2*3 再失焦",
                acceptsExpression: true),
            TextBlock($"算出来 {expr:0.##}").Caption().Subtle(),

            TextBlock("校验模式（两个框共用同一个 state）").Caption().Subtle(),
            NumberBox(Optional<double>.Of(clamped), setClamped,
                header: "Disabled：输 999 也不纠正",
                min: 0,
                max: 100,
                validationMode: MuxControls.NumberBoxValidationMode.Disabled),
            NumberBox(Optional<double>.Of(clamped), setClamped,
                header: "InvalidInputOverwritten（默认）",
                min: 0,
                max: 100,
                validationMode: MuxControls.NumberBoxValidationMode.InvalidInputOverwritten),
            TextBlock($"同一个 state：{clamped:0.##}").Caption().Subtle(),

            TextBlock("回绕与小数位").Caption().Subtle(),
            NumberBox(Optional<double>.Of(wrapped), setWrapped,
                header: "0..5 回绕（加减按钮点到头就绕回来）",
                min: 0,
                max: 5,
                isWrapEnabled: true,
                spinButtonPlacementMode: MuxControls.NumberBoxSpinButtonPlacementMode.Inline),
            NumberBox(Optional<double>.Of(money), setMoney,
                header: "两位小数",
                decimalPlaces: 2),
            TextBlock($"回绕 {wrapped:0.##} / 小数 {money:0.00}").Caption().Subtle());
    }
}
