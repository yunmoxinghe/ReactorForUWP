using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 搜索输入的真控件：<c>AutoSuggestBox</c> + 候选列表。
/// </summary>
/// <remarks>
/// 候选列表由控件自己展示（下拉面板、键盘上下选择、屏幕阅读器朗读候选数量），
/// 这里只负责"输入变化时给它一份候选"。用 <c>TextBox</c> 加 <c>ListView</c> 拼一个
/// 看起来像的下拉，是典型的替代实现：键盘导航与 UIA 的结构都不一样。
/// <para>
/// 画廊顶部那个"搜索示例"的搜索框就是同一个控件（挂在
/// <c>NavigationView.AutoSuggestBox</c> 上），可以对照着看。
/// </para>
/// <para>
/// 框里那个放大镜是 <c>QueryIcon</c>。官方这个槽位收的是 <c>IconElement</c>
/// （<b>不是</b> <c>IconSource</c>），所以传的就是 <c>FontIcon</c> /
/// <c>SymbolIcon</c> 那几个——与 <c>InfoBadge</c> 那里要翻译一次不一样。
/// </para>
/// </remarks>
public sealed class AutoSuggestBasic : Component
{
    private static readonly string[] Cities =
    {
        "北京", "上海", "广州", "深圳", "成都", "杭州", "南京", "武汉", "西安", "重庆",
    };

    public override Element Render()
    {
        var (query, setQuery) = UseState(string.Empty);
        var (picked, setPicked) = UseState("还没提交过");
        var (fillText, setFillText) = UseState(true);

        // 候选是<b>由当前文本算出来的</b>，所以它不留 state —— 留了就有两份真相，
        // 而这两份在异步回调里迟早会不一致。
        var suggestions = Cities
            .Where(city => query.Length == 0 || city.Contains(query))
            .ToArray();

        return VStack(10,
            AutoSuggestBox(
                Optional<string>.Of(query),
                suggestions,
                placeholderText: "输入城市名，看候选列表",
                header: "AutoSuggestBox",
                onTextChanged: setQuery,
                onQuerySubmitted: text => setPicked(text),
                queryIcon: FontIcon("\uE721"),
                updateTextOnSelect: fillText),
            CheckBox(Optional<bool?>.Of(fillText), setFillText,
                "点候选时把它填进输入框（UpdateTextOnSelect）"),
            TextBlock($"草稿文本：{query}").Caption().Subtle(),
            TextBlock($"最后一次提交：{picked}").Caption().Subtle(),
            TextBlock("关掉它再点候选：仍然会抛 <c>QuerySubmitted</c>（带 ChosenSuggestion），"
                      + "只是框里的字不变——适合「输入是筛选条件、候选是跳转目标」那种用法。")
                .Wrap()
                .Caption()
                .Subtle(),

            AutoSuggestBox(
                suggestions: Cities,
                placeholderText: "换一个符号图标（SymbolIcon）",
                queryIcon: SymbolIcon(Symbol.Find)),

            AutoSuggestBox(
                suggestions: Cities,
                placeholderText: "不给 QueryIcon（就没有图标）"),
            TextBlock("默认那一档是官方的放大镜；这里显式给三种写法，"
                      + "是为了让「图标是内容槽、每帧都是新实例」这件事看得见——"
                      + "本库按<b>形状</b>比（同族 + 同一个 glyph / 符号），形状没变就不重建，"
                      + "否则每帧换一次图标会闪。")
                .Wrap()
                .Caption()
                .Subtle());
    }
}
