using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 宫格展示：<c>GridView</c> 与 <c>ListView</c> 是同一个家族，
/// 差别只在于项如何排列（横向铺开并自动换行）。
/// </summary>
/// <remarks>
/// 项的内容可以是任意元素树，所以"图片卡"这种形态（上面一张图 + 下面一行字）
/// 用 <c>VStack</c> 放进项里即可——这也是「画廊」首页那排卡片的做法。
/// </remarks>
public sealed class GridViewBasic : Component
{
    private static readonly (string Glyph, string Name)[] Tools =
    {
        ("\uE8A5", "截图"),
        ("\uE7C3", "录音"),
        ("\uE70F", "编辑"),
        ("\uE8B7", "分享"),
        ("\uE74E", "标签"),
    };

    public override Element Render()
    {
        var (index, setIndex) = UseState(0);

        return VStack(8,
            GridView(
                Optional<int>.Of(index),
                setIndex,
                ForEach(Tools, tool => VStack(6,
                        FontIcon(tool.Glyph),
                        TextBlock(tool.Name).Caption())
                    .Margin(5))),
            TextBlock(index < 0 ? "没有选中项" : $"选中：{Tools[index].Name}")
                .Caption()
                .Subtle());
    }
}
