using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using MuxControls = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>SwipeControl</c>：内容上轻轻一滑，从边上露出几条命令。
/// </summary>
/// <remarks>
/// <para>
/// <b>四个方向各一组命令，模式长在「组」上。</b>官方 <c>SwipeItems.Mode</c>
/// 决定"滑到头直接执行"还是"只露出来等一下"，所以这里右边那组用
/// <c>Execute</c>（一滑到底就删）、左边那组用 <c>Reveal</c>（露出来还要再点一下）。
/// 模式不在单个项上——这是官方的层次，元素照抄。
/// </para>
/// <para>
/// <b>命令整组重建，不逐项 patch。</b>与 <c>CommandBar</c> 同一个取舍：命令项只有
/// 文字 / 图标 / 回调三种数据，没有需要跨帧保留的状态。所以每次都挂在本轮新建的
/// <c>SwipeItem</c> 上，不存在"旧回调没解绑"。
/// </para>
/// <para>
/// <b>图标收的是 <c>IconSource</c>，不是 <c>IconElement</c>。</b>
/// <c>SwipeItem.IconSource</c> 与 <c>TabViewItem.IconSource</c> 是同一类槽位
/// （图标的<b>数据描述</b>，由宿主按需物化），与 <c>AppBarButton.Icon</c> 那类
/// "能站进可视树的控件"不是一回事。这里照旧写 <c>FontIcon(...)</c>，落点由
/// handler 翻译成 <c>FontIconSource</c>。
/// </para>
/// </remarks>
public sealed class SwipeControlBasic : Component
{
    private static readonly string[] Seed = { "收件箱", "已加星标", "草稿", "已发送", "归档" };

    public override Element Render()
    {
        var (rows, setRows) = UseState<IReadOnlyList<string>>(Seed);
        var (log, setLog) = UseState("（还没滑动过）");

        return VStack(12,
            TextBlock("在一行上左右滑动：右边那组是「滑到底直接执行」（Execute），"
                      + "左边那组只是露出来、要再点一下（Reveal）。")
                .Caption().Subtle().Wrap(),

            VStack(6, ForEach(rows, (name, index) => Row(name, index, rows, setRows, setLog))),

            TextBlock($"最近一次：{log}").Caption().Subtle().Wrap(),

            Button("复原列表", () =>
            {
                setRows(Seed);
                setLog("（已复原）");
            }));
    }

    private static Element Row(
        string name,
        int index,
        IReadOnlyList<string> rows,
        Action<IReadOnlyList<string>> setRows,
        Action<string> setLog) =>
        SwipeControl(
                content: Border(
                        HStack(8,
                            FontIcon("\uE715"),
                            TextBlock(name).Body()))
                    .Padding(12, 8)
                    .Background(ThemeResource.Brush("LayerFillColorDefaultBrush")),
                left: SwipeItems(
                    MuxControls.SwipeMode.Reveal,
                    SwipeItem(
                        "置顶",
                        icon: FontIcon("\uE74A"),
                        onInvoked: () =>
                        {
                            var next = new List<string>(rows);
                            var item = next[index];
                            next.RemoveAt(index);
                            next.Insert(0, item);
                            setRows(next);
                            setLog($"「{item}」置顶");
                        })),
                right: SwipeItems(
                    MuxControls.SwipeMode.Execute,
                    SwipeItem(
                        "删除",
                        icon: FontIcon("\uE74D"),
                        background: new SolidColorBrush(Colors.IndianRed),
                        foreground: new SolidColorBrush(Colors.White),
                        behaviorOnInvoked: MuxControls.SwipeBehaviorOnInvoked.Close,
                        onInvoked: () =>
                        {
                            var next = new List<string>(rows);
                            next.RemoveAt(index);
                            setRows(next);
                            setLog($"删掉了「{name}」");
                        })))
            // 官方那一行是 500×68、外圈 12：不写死尺寸它会按内容缩，
            // 「一整行」的手感就没了（滑动手势认的是这一行的宽度）。
            .Width(500)
            .Height(68)
            .Margin(12)
            .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush"));
}
