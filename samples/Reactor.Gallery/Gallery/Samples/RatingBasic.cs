using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>RatingControl</c>：评分（WinUI 2 真控件），受控 <c>Value</c>。
/// </summary>
/// <remarks>
/// <b>受控那一档：给值 + 给回调</b>，值由 state 回写。与 <c>Slider</c> 同形，
/// 拖动 / 连续点击时 <c>ValueChanged</c> 会连续抛，回调里别做重活。
/// <para>
/// <b>只读要用 <c>isReadOnly: true</c>，不是把回调摘掉。</b>摘掉回调之后控件照样能点，
/// 只是点了没人理——用户看到的是"点了没反应"，而不是"这是只读的"。
/// 官方这个属性同时也是无障碍层面的一句真话（自动化对等信息里就是只读）。
/// </para>
/// <para>
/// <c>isClearEnabled</c> 是官方那个"再点一次当前值把它清成 0"的开关，
/// 默认是关的——打开了才允许把评分打回 0。
/// </para>
/// </remarks>
public sealed class RatingBasic : Component
{
    public override Element Render()
    {
        var (score, setScore) = UseState(3.0);

        return VStack(14,
            TextBlock("受控").Body(),
            // 评分控件自己没有文字，无障碍名字得自己给（UIA 自检会查"可聚焦的都有名字"）。
            Rating(Optional<double>.Of(score), setScore, caption: $"{score:F0} 分")
                // 官方每一档都显式 HorizontalAlignment="Left"：默认 Stretch 会把星级
                // 拉满整行，看起来像"每颗星都变宽了"。
                .AutomationName("评分（受控）")
                .HAlign(HorizontalAlignment.Left),
            TextBlock($"当前 {score:F0} 分（点同一颗星会不会清成 0，看下面的开关）")
                .Caption().Subtle(),

            TextBlock("允许清成 0").Body(),
            Rating(Optional<double>.Of(score), setScore, isClearEnabled: true, caption: "isClearEnabled")
                .AutomationName("评分（可清零）")
                .HAlign(HorizontalAlignment.Left),
            TextBlock("打开这一档之后再点一次当前那颗星，值会回到 0。").Caption().Subtle(),

            TextBlock("只读").Body(),
            Rating(Optional<double>.Of(4.0), isReadOnly: true, caption: "只读：能看不能改")
                .AutomationName("评分（只读）")
                .HAlign(HorizontalAlignment.Left),

            TextBlock("一共十颗星").Body(),
            Rating(Optional<double>.Of(score), setScore, maxRating: 10, caption: "maxRating: 10")
                .AutomationName("评分（十颗星）")
                .HAlign(HorizontalAlignment.Left),

            TextBlock("底衬浅色星").Body(),
            Rating(
                    Optional<double>.Of(3.0),
                    isReadOnly: true,
                    caption: "Value 3 / PlaceholderValue 5",
                    placeholderValue: 5.0)
                .AutomationName("评分（底衬五颗星，实心三颗）")
                .HAlign(HorizontalAlignment.Left),
            TextBlock("官方默认让底衬跟着值走（一样满），要「3 分压在 5 颗浅色星上」这个对照"
                + "就得显式给 placeholderValue——这是官方 Gallery 里那档演示的形状。")
                .Caption().Subtle().Wrap(),

            TextBlock("这一档只在挂载时写：本库的评分控件把「配置」与「受控值」分了两层，"
                + "配置那一层不进 patch 通道（理由见 RatingControlHandler 的注释）。")
                .Caption().Subtle().Wrap());
    }
}
