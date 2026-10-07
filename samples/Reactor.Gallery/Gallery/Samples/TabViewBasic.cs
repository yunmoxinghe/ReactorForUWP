using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using MuxControls = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// WinUI 2 的 <c>TabView</c>：页签条 + 一个内容区。
/// </summary>
/// <remarks>
/// 画廊每个示例下面的源码框（<c>Reactor 源码</c> / <c>WinUI 2 对照</c>）就是它。
/// 官方 <c>TabView</c> 自带的东西这里一件都没替它做：键盘左右箭头切页签、
/// <c>Tab</c> / <c>TabItem</c> 的自动化角色、深浅色资源。
/// <para>
/// <b>内容为什么不放在 <c>TabViewItem</c> 上。</b>WinUI 的 <c>TabView</c> 继承
/// <c>Control</c>，没有 <c>Content</c>；官方把内容交给 <c>TabViewItem.Content</c>。
/// 但把 <c>UIElement</c> 塞进 <c>TabItems</c> 集合会撞上"一个元素两个父"的老问题
/// （<c>0x800F1000</c>）。所以这里由 Reactor 侧把页签条与内容区纵向拼起来：
/// 一次只有一份内容树，切页签时走 patch，而不是常驻 N 棵隐藏子树。
/// </para>
/// <para>
/// 页签条的<b>样子</b>由容器上两个模式决定：
/// <list type="bullet">
///   <item><c>TabWidthMode</c>：页签宽度怎么算。默认是 <c>Equal</c>（等宽），
///         想让「文字长的页签宽一点」要显式给 <c>SizeToContent</c>。</item>
///   <item><c>CloseButtonOverlayMode</c>：关闭按钮什么时候冒出来（
///         <c>Auto</c> 只在选中的那个页签上显示，<c>Always</c> 一直显示）。
///         它<b>只管时机</b>——能不能关是页签自己的 <c>IsClosable</c>。</item>
/// </list>
/// </para>
/// </remarks>
public sealed class TabViewBasic : Component
{
    public override Element Render()
    {
        var (index, setIndex) = UseState(0);
        var (widthMode, setWidthMode) = UseState(0);
        var (closeMode, setCloseMode) = UseState(0);

        var contents = new[]
        {
            TextBlock("第一个页签的内容。").Wrap(),
            TextBlock("第二个页签的内容——切页签时只是这一份内容被换掉了，不是两份同时存在。").Wrap(),
        };

        var widthModes = new[] { "Equal（等宽）", "SizeToContent（按文字）", "Compact（收窄）" };
        var closeModes = new[]
        {
            "Auto（只在选中的那个上）",
            "OnPointerOver（移上去才显示）",
            "Always（一直显示）",
        };

        return VStack(16,
            TabView(
                new[]
                {
                    Tab("概览", FontIcon("\uE8A1")),
                    Tab("详情", FontIcon("\uE946")),
                },
                Optional<int>.Of(index),
                setIndex,
                content: contents[index].Padding(0, 12, 0, 0)),

            TextBlock("页签宽度").Caption().Subtle(),
            TabView(
                new[]
                {
                    Tab("短", FontIcon("\uE8A1")),
                    Tab("这是一个很长的页签标题", FontIcon("\uE946")),
                },
                tabWidthMode: widthMode switch
                {
                    1 => MuxControls.TabViewWidthMode.SizeToContent,
                    2 => MuxControls.TabViewWidthMode.Compact,
                    _ => MuxControls.TabViewWidthMode.Equal,
                }),
            RadioButtons(widthModes, Optional<int>.Of(widthMode), setWidthMode),
            TextBlock("默认那一档是 Equal，<b>不等</b>于「按文字长短」——"
                      + "要让长标题占得宽一点，得自己切到 SizeToContent。")
                .Wrap().Caption().Subtle(),

            TextBlock("关闭按钮的冒出时机").Caption().Subtle(),
            TabView(
                new[]
                {
                    Tab("可关闭", isClosable: true),
                    Tab("也可关闭", isClosable: true),
                },
                closeButtonOverlayMode: closeMode switch
                {
                    1 => MuxControls.TabViewCloseButtonOverlayMode.OnPointerOver,
                    2 => MuxControls.TabViewCloseButtonOverlayMode.Always,
                    _ => MuxControls.TabViewCloseButtonOverlayMode.Auto,
                }),
            RadioButtons(closeModes, Optional<int>.Of(closeMode), setCloseMode),
            TextBlock("这里两个页签都给了 isClosable：关不关得掉是页签自己的属性，"
                      + "上面那个模式只决定按钮<b>什么时候可见</b>。")
                .Wrap().Caption().Subtle());
    }
}
