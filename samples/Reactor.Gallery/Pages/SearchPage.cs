using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

/// <summary>搜索结果页 props。</summary>
public sealed record SearchPageProps(string Query, Action<string> OpenItem, Action Back);

/// <summary>
/// 搜索结果：命中的控件条目列表。
/// </summary>
/// <remarks>
/// 结果按"命中标题 / 命中描述 / 命中样例标题"三档排（见 <see cref="SampleIndex.RankOf"/>），
/// 所以排在最前的通常就是你要找的那个控件。
/// <para>
/// 空结果时给出提示而不是空白页：空白页让人分不清"没搜到"和"搜索坏了"。
/// </para>
/// </remarks>
public sealed class SearchPage : Component<SearchPageProps>
{
    public override Element Render()
    {
        var hits = UseMemo(() => SampleIndex.Search(Props.Query), Props.Query);

        return ScrollViewer(
            VStack(14,
                HStack(8,
                    Button("返回", Props.Back),
                    TextBlock($"“{Props.Query}” 的搜索结果").Title()),

                hits.Count == 0
                    ? InfoBar($"没有匹配“{Props.Query}”的示例。可以试试控件名，比如 TextBox、TabView。")
                    : TextBlock($"共 {hits.Count} 项").Caption().Subtle(),

                ForEach(hits, item =>
                    SettingsCard(
                        header: item.Title,
                        description: item.Description,
                        headerIcon: FontIcon("\uE8FD"),

                        // 右侧那一小行写"为什么这一条在这儿"。相关度排序本身是
                        // 隐式的，不写出来就没法判断排序对不对。
                        content: TextBlock(SampleIndex.HitReasonOf(item, Props.Query) ?? string.Empty)
                            .Caption().Subtle(),
                        contentAlignment: SettingsCardContentAlignment.Right,
                        onClick: () => Props.OpenItem(item.Id))))
                .Padding(24, 20, 24, 32));
    }
}
