using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

/// <summary>
/// 应用设置页（左侧导航底部那一项进入）。
/// </summary>
/// <remarks>
/// 与 XAML 模板一一对应，差别只来自"这些是用 C# 表达的"：用到的还是同样的
/// <c>SettingsCard</c> / <c>SettingsExpander</c> 那些 Toolkit 控件。
/// <para>
/// <b>状态不在这里。</b>主题要挂在<b>根元素</b>上才对整个窗口生效
/// （<c>RequestedTheme</c> 只作用于该元素及其子树），所以由外壳持有并通过 Context
/// 下发——这里只负责读写。这样"换个页面设置就丢"和"只有本页变主题"两个毛病一起消失。
/// </para>
/// </remarks>
public sealed class AppSettingsPage : Component
{
    private static readonly string[] ThemeNames = { "跟随系统", "浅色", "深色" };

    public override Element Render()
    {
        var settings = UseContext(SettingsStore.Settings);
        var (lastResult, setLastResult) = UseState("还没操作过");

        return ScrollViewer(
            VStack(14,
                TextBlock("设置").TitleLarge(),

                SettingsCard(
                    header: "主题",
                    description: "立即生效，并写入本地设置（下次启动保持）。整棵树会按 XAML 的主题继承一起换色。",
                    headerIcon: FontIcon("\uE790"),
                    contentAlignment: SettingsCardContentAlignment.Vertical,
                    content: RadioButtons(
                        ThemeNames,
                        Optional<int>.Of(settings.Theme),
                        settings.SetTheme)),

                SettingsCard(
                    header: "控件音效",
                    description: "控件自带的系统音效。设为开之后，所有控件都会播对应的操作音。",
                    headerIcon: FontIcon("\uE7F3"),
                    content: HStack(8,
                        ToggleSwitch(Optional<bool>.Of(settings.Sound), settings.SetSound),
                        Button("试听", () =>
                            {
                                var (_, message) = SoundService.TryPreview();
                                setLastResult(message);
                            })
                            .Disabled(!settings.Sound))),

                // 快捷键是"只有按了才知道有"的那类功能，写没写出来区别很大：
                // 不写出来等于没有。两条都是挂在外壳根元素上的官方 KeyboardAccelerator，
                // 不是页面自己监听按键。
                SettingsCard(
                    header: "键盘快捷键",
                    description: "Ctrl + F —— 把焦点送到顶部搜索框；Alt + ← —— 返回上一层。",
                    headerIcon: FontIcon("\uE92E")),

                SettingsExpander(
                    header: "高级",
                    description: "点一下展开",
                    headerIcon: FontIcon("\uE713"),
                    items: new Element?[]
                    {
                        SettingsCard(
                            header: "自动更新",
                            content: ToggleSwitch(Optional<bool>.Of(settings.AutoUpdate), settings.SetAutoUpdate),
                            contentAlignment: SettingsCardContentAlignment.Left),
                        SettingsCard(
                            header: "清理缓存",
                            description: "只清应用临时文件夹，不动个人数据。",
                            content: Button("清理", () =>
                            {
                                // 异步但不等：async void 在事件回调里是允许的，
                                // 结果回来再 setState。
                                _ = ClearAsync(setLastResult);
                            })),
                    }),

                SettingsExpander(
                    header: "关于",
                    description: "这个示例本身就是框架的一份测试计划。",
                    headerIcon: FontIcon("\uE946"),
                    items: new Element?[]
                    {
                        SettingsCard(
                            header: "框架引用方式",
                            description: "ProjectReference（仓库里的源码），不是 NuGet 包——这样改完框架能立刻看到效果。"),
                        SettingsCard(
                            header: "示例数量",
                            description: $"分类 {SampleIndex.Categories.Count} 个，控件条目 {SampleIndex.AllItems.Count()} 项。"),
                    }),

                TextBlock($"上一次操作结果：{lastResult}").Caption().Subtle())
                .Padding(24, 20, 24, 32));
    }

    private static async Task ClearAsync(Action<string> report)
    {
        var removed = await SettingsStore.ClearTempAsync();
        report($"已清理 {removed} 个临时文件");
    }
}
