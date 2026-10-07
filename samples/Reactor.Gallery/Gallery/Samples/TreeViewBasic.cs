using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 树：<c>TreeView</c> 是本版<b>唯一一个"选中结构上就不可写"的控件</b>。
/// </summary>
/// <remarks>
/// <para>
/// WinUI 2 的 <c>TreeView.SelectedItem</c> 与 <c>TreeView.SelectedNode</c> 都
/// <b>只有 getter</b>（对照 <c>Microsoft.UI.Xaml.xml</c>：这两个名字没有配套的
/// <c>...Property</c> 依赖属性，而 <c>SelectionMode</c> 有），
/// <c>TreeViewNode</c> 也没有 <c>IsSelected</c>。也就是说"选中第 N 项"
/// <b>没有任何可编程的入口</b>。既然写不进去，"受控选中"就没有落点——
/// 这里不假装受控：<b>只出不进</b>，靠 <c>ItemInvoked</c> 往外报
/// （<c>TreeView</c> 没有 <c>SelectionChanged</c>，这是官方给的唯一出口）。
/// 与 <c>CalendarView</c> 那份"活集合只出不进"是同一条规矩。
/// </para>
/// <para>
/// <b>节点树在挂载时物化。</b>改结构等于整棵树重来，而这一族连一个可写的选中槽位
/// 都没有，"边跑边改"唯一能兑现的代价是<b>展开态归零</b>。所以运行中要换结构就换
/// 一个 <c>key</c> 让控件重建。展开 / 折叠有通知但没有写入通道，同样只出不进。
/// </para>
/// <para>
/// <b>节点内容只收字符串。</b><c>TreeViewNode.Content</c> 收的是 <c>object</c>，
/// 塞 <c>UIElement</c> 也能显示，但那棵子树就住在协调器视野之外，卸载时没人替它
/// 跑 cleanup——所以这条路不提供。要富内容走官方的
/// <c>ItemsSource</c> + <c>ItemTemplate</c>。
/// </para>
/// </remarks>
public sealed class TreeViewBasic : Component
{
    // <c>expanded</c> 后面要接"一组子节点"，就显式写 <c>children:</c>——
    // 混着写（<c>expanded: true, 子节点, 子节点</c>）编译器会把第一个子节点
    // 当成 <c>expanded</c> 的值去转 bool。
    private static readonly TreeNodeElement Files =
        TreeNode("工作区", expanded: true, children: new[]
        {
            TreeNode("src", expanded: true, children: new[]
            {
                TreeNode("App.cs"),
                TreeNode("MainPage.cs"),
                TreeNode("Shell.cs"),
            }),
            TreeNode("docs", children: new[]
            {
                TreeNode("README.md"),
                TreeNode("架构决策.md"),
            }),
            TreeNode("tests", children: new[]
            {
                TreeNode("EchoContractTests.cs"),
                TreeNode("GalleryIndexTests.cs"),
            }),
        });

    public override Element Render()
    {
        var (picked, setPicked) = UseState("（还没点过）");
        var (trace, setTrace) = UseState("（还没有）");

        return VStack(12,
            TreeView(
                item => setPicked(item ?? "（空）"),
                node => setTrace($"展开：{node}"),
                node => setTrace($"折叠：{node}"),
                Files)
                .Height(220),

            TextBlock($"点到的节点：{picked}").Body(),
            TextBlock($"最近一次展开 / 折叠：{trace}").Caption().Subtle(),

            TextBlock("两个方向都要试：点节点会来 ItemInvoked；点左边的小箭头只来"
                      + " Expanding / Collapsed，不来 ItemInvoked——"
                      + "官方把「展开」和「选中」当成两件事。")
                .Caption().Subtle().Wrap(),

            TextBlock("受控那一行在这里是空的：不是漏了，是官方没有可写的选中槽位。"
                      + "装一个出来只会得到「点了没反应」，而且没人知道为什么。")
                .Caption().Subtle().Wrap());
    }
}
