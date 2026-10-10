using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using MuxControls = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// WinUI 2 的 <c>InfoBar</c>：页面内的一条状态提示。
/// </summary>
/// <remarks>
/// 四种严重级别（<c>Informational</c> / <c>Success</c> / <c>Warning</c> /
/// <c>Error</c>）各自有自己的图标与强调色，这是官方定的；
/// 需要让用户看清的结果（操作成功 / 出错了）用它，不要用颜色自制的 TextBlock 代替。
/// <para>
/// 图标那一格还有个 <c>IsIconVisible</c>：关掉它只是<b>不画那个图标</b>，
/// 文字不会因此往左顶——图标那一列是模板里的固定槽位，隐藏的是内容不是格子。
/// </para>
/// <para>
/// <b><c>IsOpen</c> 是种子值，不是受控值。</b>它只在挂载那一刻写一次，之后归控件
/// 自己：用户点了 ×，<c>IsOpen</c> 变 <c>false</c>，下一轮重渲染<b>不会</b>把它
/// 重新打开——那不是状态同步，那是把用户的操作撤回。所以本库不给它下发通道。
/// </para>
/// </remarks>
public sealed class InfoBarBasic : Component
{
    public override Element Render()
    {
        var (closed, setClosed) = UseState(false);
        var (icon, setIcon) = UseState(true);

        return VStack(8,
            // 官方那页几乎条条都有标题：它是"消息上方那行加粗的短句"，
            // 与 Message 是两段不同的文本（一个概括、一个展开）。
            InfoBar("这是一条普通提示，用于说明上下文。", title: "提示"),
            InfoBar("操作成功。", MuxControls.InfoBarSeverity.Success, title: "成功"),
            InfoBar("这个操作稍后会被弃用。", MuxControls.InfoBarSeverity.Warning, title: "警告"),
            InfoBar("保存失败：磁盘不可写。", MuxControls.InfoBarSeverity.Error, title: "错误"),

            // IsClosable：右上角出现 ×。关掉之后它<b>不会再被重新打开</b>——
            // IsOpen 是种子值（只在挂载时写一次），不是受控值，
            // 后续重渲染不会把用户按下的那一下撤回。
            TextBlock("可关闭（IsClosable）").Caption().Subtle(),
            InfoBar("这一条可以被关掉：点了 × 之后，重渲染不会再把它打开。",
                title: "可关闭", isClosable: true),

            TextBlock("图标显不显示（IsIconVisible）").Caption().Subtle(),
            InfoBar("这一条的图标被关掉了，文字位置没变。", isIconVisible: icon),
            Button(icon ? "关掉图标" : "显示图标", () => setIcon(!icon)),

            TextBlock("关掉图标之后文字<b>不会</b>往左顶：图标那一列是模板里的固定槽位，"
                      + "隐藏的是它的内容，格子还在。想让消息顶到左边要改模板，"
                      + "官方没有给这个开关。")
                .Wrap()
                .Caption()
                .Subtle(),

            When(!closed, () =>
                Button("隐藏最后一条", () => setClosed(true))));
    }
}
