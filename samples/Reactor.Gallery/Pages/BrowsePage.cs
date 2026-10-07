using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

/// <remarks>类型必须是 public：它出现在 <c>Component&lt;BrowsePageProps&gt;</c> 的基类位置。</remarks>
public sealed record BrowsePageProps(string? CategoryId, Action<string> OpenItem);

/// <summary>
/// 浏览页：按分类铺开的卡片网格（对应 WinUI 3 Gallery 的 All samples 页）。
/// </summary>
/// <remarks>
/// <b>卡片容器用 <c>GridView</c> 而不是自绘容器</b>：横向铺开 + 换行的排布、
/// 键盘方向键在卡片间移动、UIA 上的 item 结构，全由官方那套给出。自己用一堆
/// Button 拼会得到"外表一样、念出来完全不同"的东西。
/// <para>
/// 每张卡片用 <c>SettingsCard</c>（它自带可点击形态 + 图标 + 描述），
/// 而不是自制卡片——这也是 WinUI 自身设置页的写法。
/// </para>
/// </remarks>
public sealed class BrowsePage : Component<BrowsePageProps>
{
    public override Element Render()
    {
        var categoryId = Props.CategoryId ?? string.Empty;
        var categories = UseMemo(() => VisibleCategories(categoryId), categoryId);

        return ScrollViewer(
            VStack(20, ForEach(categories, CategorySection))
                .Padding(24, 24, 24, 32));
    }

    /// <summary>一个分类：标题 + 它的条目网格。</summary>
    private Element CategorySection(GalleryCategory category)
    {
        var note = category.Description;

        return VStack(8,
            VStack(2,
                HStack(8,
                    FontIcon(category.Icon),
                    TextBlock(category.Title).Title()),
                When(note is not null, () => TextBlock(note!).Caption().Subtle())),

            GridView(ForEach(category.Items, ItemCard)));
    }

    /// <summary>一张条目卡：点了就钻进它的详情页。</summary>
    /// <remarks>
    /// <c>AutomationName</c> 是给<b>项容器</b>用的：卡片本身是 <c>SettingsCard</c>
    /// （一个自成 peer 的控件），UWP 不会把它内部那段标题文本推到外层
    /// <c>GridViewItem</c> 上，于是读屏在列表里念出来是空白。
    /// 框架侧把项元素的这个名字转给容器（见 <c>ItemsViewHandler.Initialize</c>），
    /// 这里负责把名字写下来。
    /// </remarks>
    private Element ItemCard(GalleryItem item) =>
        SettingsCard(
                header: item.Title,
                description: Shorten(item.Description),
                contentAlignment: SettingsCardContentAlignment.Left,
                onClick: () => Props.OpenItem(item.Id))
            .MinWidth(280)
            .MaxWidth(320)
            .AutomationName(item.Title);

    private static IReadOnlyList<GalleryCategory> VisibleCategories(string? categoryId) =>
        string.IsNullOrEmpty(categoryId)
            ? SampleIndex.Categories
            : SampleIndex.Categories.Where(c => c.Id == categoryId).ToArray();

    /// <summary>
    /// 卡片描述只用第一句：卡片大小固定，写长了会被截断成一个"…"，
    /// 而完整说明在点进去的那一页上——信息不丢，只是分层。
    /// </summary>
    private static string? Shorten(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var dot = text.IndexOf('。');
        return dot > 0 ? text.Substring(0, dot + 1) : text;
    }
}
