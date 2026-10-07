using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

/// <summary>首页 props：去浏览页（可选针对某个分类）的入口。</summary>
/// <remarks>
/// props 类型必须是 <c>public</c>：它在 <c>Component&lt;HomePageProps&gt;</c> 里出现在基类的
/// 类型实参位置，而 <c>Component&lt;TProps&gt;</c> 本身是 public——可访问性不能比它低。
/// </remarks>
public sealed record HomePageProps(Action ToBrowse, Action<string> OpenCategory);

/// <summary>
/// 首页：说清楚这是什么，再给几个"从哪儿开始"的入口。
/// </summary>
/// <remarks>
/// 与 WinUI 3 Gallery 的首页同形：一张 hero 区（这是什么）+ 若干入口卡片。
/// 不放导航条里的那些分类链接——那属于左侧的职责，首页只回答"我该从哪儿下手"。
/// </remarks>
public sealed class HomePage : Component<HomePageProps>
{
    public override Element Render()
    {
        var settings = UseContext(SettingsStore.Settings);

        return ScrollViewer(
            VStack(20,
                VStack(6,
                    TextBlock("Reactor.Uwp 示例画廊").TitleLarge(),
                    TextBlock("每个示例都由一块实时预览 + 一份可以直接复制走的源码组成；源码读的就是包里那份文件，不是另抄的样板。")
                        .Wrap()
                        .Subtitle()),

                TextBlock("从这里开始").BodyStrong(),
                SettingsCard(
                    header: "浏览全部示例",
                    description: "按分类看：文本与提示 / 输入与选择 / 集合与虚拟化 / 布局与容器。",
                    headerIcon: FontIcon("\uE8FD"),
                    onClick: Props.ToBrowse),
                SettingsCard(
                    header: "写法指南（整页示例）",
                    description: "状态、受控诊断、props、原生逃生舱——几个需要多讲两句的专题。",
                    headerIcon: FontIcon("\uE8A5"),
                    onClick: () => Props.OpenCategory(SampleIndex.Guide.Id)),

                SettingsCard(
                    header: "搜索",
                    description: "顶部导航条里的搜索框支持按标题与描述查找示例；按 Ctrl+F 可把焦点直接送到那里。",
                    headerIcon: FontIcon("\uE721")),

                // 分类墙：与 WinUI 3 Gallery 首页同一形——hero 讲清这是什么之后，
                // 直接把各个分区摆出来，让人一眼看到"东西都分在哪儿"，
                // 而不必先去导航条里猜分类名。
                TextBlock("按分类浏览").BodyStrong(),
                GridView(ForEach(SampleIndex.MenuCategories, category =>
                    SettingsCard(
                            header: category.Title,
                            description: category.Description,
                            headerIcon: FontIcon(category.Icon),
                            contentAlignment: SettingsCardContentAlignment.Left,
                            onClick: () => Props.OpenCategory(category.Id))
                        .MinWidth(240)
                        .MaxWidth(300)
                        .AutomationName(category.Title))),

                TextBlock("当前设置").BodyStrong(),
                TextBlock($"主题：{settings.Theme switch { 1 => "浅色", 2 => "深色", _ => "跟随系统" }} · " +
                          $"控件音效：{(settings.Sound ? "开" : "关")}")
                    .Caption()
                    .Subtle(),
                TextBlock("改主题试试：切换的成本只有一次重渲染，整棵树按 XAML 的主题继承一起换色。")
                    .Caption()
                    .Subtle())
                .Padding(24, 24, 24, 32));
    }
}
