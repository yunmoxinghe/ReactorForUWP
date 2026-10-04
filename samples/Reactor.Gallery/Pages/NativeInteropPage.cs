using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using MuxControls = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

/// <summary>
/// 逃生舱 <c>Native()</c>：把一棵真实原生控件树挂进 Reactor 布局。
/// </summary>
/// <remarks>
/// 框架是"映射 WinUI 2"路线，元素库不可能包住所有控件。遇到没被包住的控件
/// （这里拿 WinUI 2 的 <c>NumberBox</c> 举例），自己 new 一个挂进来：
/// Reactor 只负责把它放进布局、并在卸载时收走，不参与子树内部更新。
/// <para>
/// <b>两个必须遵守的点</b>：
/// <list type="bullet">
///   <item>创建委托的<b>引用要稳定</b>——写成字段（像下面这样），不要在 Render 里
///   每次 new 一个 lambda，否则引用变化会被当成换了控件。</item>
///   <item>要重建控件就改 <c>Token</c>（这里用一个自增的 int），不要靠换委托。</item>
/// </list>
/// </para>
/// </remarks>
public sealed class NativeInteropPage : Component
{
    private readonly Func<UIElement> _factory = static () => new MuxControls.NumberBox
    {
        Header = "原生 NumberBox（框架还没包它）",
        Value = 5,
        Minimum = 0,
        Maximum = 100,
        SpinButtonPlacementMode = MuxControls.NumberBoxSpinButtonPlacementMode.Compact,
    };

    public override Element Render()
    {
        var (token, setToken) = UseState(0);

        return ScrollViewer(
            VStack(12,
                TextBlock("原生控件逃生舱").FontSize(20),
                TextBlock("Reactor 只管放进布局和卸载收走，控件内部的状态归你自己管。").Wrap(),

                Native(_factory, token, onDispose: _ => { /* 这里解事件、停定时器 */ }),

                HStack(8,
                    Button("重建控件（改 Token）", () => setToken(token + 1)),
                    TextBlock($"Token = {token}").Caption())
            ).Padding(16));
    }
}
