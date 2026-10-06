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
/// <b>重建的唯一旋钮是 <c>Token</c></b>：
/// <list type="bullet">
///   <item>想换控件就改 <c>Token</c>（这里用一个自增的 int），不要靠换委托。</item>
///   <item>反过来也不要误会"创建委托必须引用稳定"。这句话在
///   <c>Reactor.uwp/Elements/Native.cs</c> 的注释里是这么写的，但<b>与实现不符</b>：
///   <c>Internal/Handlers.Native.cs</c> 的 <c>Update</c> 只比
///   <c>Equals(oldElement.Token, newElement.Token)</c>，<c>Factory</c> 引用不参与比较，
///   所以在 Render 里直接写 <c>Native(() => new NumberBox{...})</c> 也不会每帧重建。
///   这里写成字段是为了让"重建只可能来自 Token"这件事在代码里看得见，
///   不是为了绕开那条不存在的限制。</item>
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
