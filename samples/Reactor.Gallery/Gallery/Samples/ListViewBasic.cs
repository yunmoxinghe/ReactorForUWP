using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 列表选择：<c>ListView</c> 的下标受控写法。
/// </summary>
/// <remarks>
/// <c>SelectedIndex</c> 是受控属性，选中变化会回调；反过来写回控件那一趟由框架
/// 接住并判为回声（否则每轮渲染都会把选择拽回旧值）。这几刀echo 的实际计数
/// 可以在左侧「受控控件诊断」那一页实时看到。
/// <para>
/// <b>项数多的时候别用它</b>：<c>ListView</c> 会为每一项建控件。
/// 上千项请走 <c>VirtualizingList</c>（见同一分类下的示例）。
/// </para>
/// </remarks>
public sealed class ListViewBasic : Component
{
    private static readonly string[] Names =
    {
        "打开库存", "写入日志", "刷新缓存", "重建索引", "校验签名",
    };

    public override Element Render()
    {
        var (index, setIndex) = UseState(0);

        return VStack(8,
            ListView(
                Optional<int>.Of(index),
                setIndex,
                ForEach(Names, name => TextBlock(name))),
            TextBlock(index < 0
                    ? "没有选中项"
                    : $"选中：{Names[index]}（第 {index} 项）")
                .Caption()
                .Subtle());
    }
}
