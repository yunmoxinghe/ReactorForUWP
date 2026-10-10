using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>SemanticZoom</c>：同一批数据的"细看"与"总览"两种视图。
/// </summary>
/// <remarks>
/// <para>
/// <b>两个槽位只收 <c>ListView</c> / <c>GridView</c>。</b>官方这两个属性的类型
/// 是 <c>ISemanticZoomInformation</c>——缩放要靠"这一组里当前是哪一项"才能对上，
/// 容器必须自己报得出这个信息。塞一个 <c>Grid</c> 进去的话 handler 会留空并留痕，
/// 不会抛。
/// </para>
/// <para>
/// <b>本例只演示"两个槽位 + 受控切换"这一层。</b>真正的语义缩放还要给
/// <c>ZoomedInView</c> 配分组数据源（<c>CollectionViewSource</c> + <c>GroupStyle</c>），
/// 那样点总览里的"蔬菜"才会真的滚到蔬菜那一组。本库没有包那一层
/// （它是<b>数据源</b>的事，不是控件的事），需要时走 <c>Native()</c>。
/// </para>
/// <para>
/// <b>受控那一发是异步的。</b>写 <c>IsZoomedInViewActive</c> 触发的是带动画的切换，
/// <c>ViewChangeCompleted</c> 晚于这次调用才到；按 <c>EchoGuard</c> 的既定取舍，
/// 多出来的是一次"值相同、不重渲染"的空转回调，不是抖动。
/// </para>
/// </remarks>
public sealed class SemanticZoomBasic : Component
{
    private static readonly string[] Groups = { "水果", "蔬菜", "坚果", "谷物" };

    public override Element Render()
    {
        var (zoomedIn, setZoomedIn) = UseState(true);
        var (canChange, setCanChange) = UseState(true);

        return VStack(12,
            TextBlock("点内容里那个缩小键（右下角）、捏合，或者用下面的按钮切换两种视图。")
                .Caption().Subtle().Wrap(),

            SemanticZoom(
                    zoomedInView: GridView(Groups.SelectMany(Items).Select(Card).ToArray()),
                    zoomedOutView: ListView(Groups.Select(GroupRow).ToArray()),
                    isZoomedInViewActive: zoomedIn,
                    canChangeViews: canChange,
                    onIsZoomedInViewActiveChanged: setZoomedIn)
                .Height(500)
                .WithBorder(ThemeResource.Brush("CardStrokeColorDefaultBrush")),

            HStack(8,
                Button(zoomedIn ? "切到「总览」" : "切回「细看」", () => setZoomedIn(!zoomedIn)),
                Button(canChange ? "允许换视图：开" : "允许换视图：关", () => setCanChange(!canChange)),
                TextBlock($"当前：{(zoomedIn ? "细看" : "总览")}").Body()
                    .VAlign(VerticalAlignment.Center)),

            TextBlock("下面这一行是回调写进 state 的读数；受控值没给（传 null）的话"
                      + "控件自己管这一位，点了也照样切，只是 state 不知道。")
                .Caption().Subtle().Wrap());
    }

    private static string[] Items(string group) =>
        group switch
        {
            "水果" => new[] { "苹果", "香蕉", "葡萄" },
            "蔬菜" => new[] { "番茄", "黄瓜", "菠菜" },
            "坚果" => new[] { "核桃", "杏仁", "腰果" },
            _ => new[] { "小麦", "燕麦", "玉米" },
        };

    private static Element Card(string name) =>
        Border(TextBlock(name).Body())
            .Padding(12, 6)
            .Background(ThemeResource.Brush("LayerFillColorDefaultBrush"));

    private static Element GroupRow(string group) =>
        Border(
                VStack(2,
                    TextBlock(group).Body(),
                    TextBlock($"{Items(group).Length} 项").Caption().Subtle()))
            .Padding(12, 8);
}
