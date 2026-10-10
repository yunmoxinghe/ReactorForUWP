using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>RepeatButton</c>：按住不放会<b>连续</b>触发点击的按钮。
/// </summary>
/// <remarks>
/// 它继承 <c>ButtonBase</c>，只有 <c>Click</c>，<b>没有</b>选中态——
/// 与 <c>ToggleButton</c> 是两条不同的路。
/// <para>
/// <c>delay</c> 是"按住多久之后开始连发"，<c>interval</c> 是"之后每隔多久发一次"，
/// 两个都是毫秒。都不给的时候就是官方默认：先点一下，稍后开始连发。
/// </para>
/// <para>
/// <b>回调会被反复调用，这是这个控件的全部意义，不是 bug。</b>所以回调里别做重活，
/// 也别在里头发请求——按住一秒可能就是十几次。
/// </para>
/// </remarks>
public sealed class RepeatButtonBasic : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(14,
            TextBlock("按住不放").Body(),
            // 官方那一例是「按钮 + 右边一行读数」横排（读数 Margin 8、垂直居中），
            // 读数跟着按下的次数实时变，不是按完再看。
            HStack(8,
                RepeatButton(
                    "按住我",
                    onClick: () => setCount(count + 1),
                    delay: 400,
                    interval: 80),
                TextBlock($"点了 {count} 次")
                    .Caption()
                    .Subtle()
                    .Margin(8, 0, 0, 0)
                    .VAlign(VerticalAlignment.Center)),

            TextBlock("调步长用").Body(),
            HStack(8,
                RepeatButton("−", onClick: () => setCount(count - 1), delay: 400, interval: 80),
                RepeatButton("+", onClick: () => setCount(count + 1), delay: 400, interval: 80)),
            TextBlock("这是官方给这个控件的典型用法：加减号那种「按住就一直加」的步长器。")
                .Caption().Subtle(),

            TextBlock("归零").Body(),
            Button("清成 0", () => setCount(0)));
    }
}
