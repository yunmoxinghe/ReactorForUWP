using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Samples.Pages;

/// <summary>
/// 设置页：SettingsCard / SettingsExpander / Expander / ContentDialog。
/// </summary>
/// <remarks>
/// 前三个是 CommunityToolkit 的 SettingsControls，包已经带上依赖，不用另外装。
/// </remarks>
public sealed class SettingsPage : Component
{
    public override Element Render()
    {
        var (theme, setTheme) = UseState(0);
        var (autoUpdate, setAutoUpdate) = UseState(true);
        var (lastResult, setLastResult) = UseState("还没弹过");

        return ScrollViewer(
            VStack(12,
                TextBlock("设置页").FontSize(20),

                SettingsCard(
                    "主题",
                    ComboBox(new[] { "跟随系统", "浅色", "深色" }, Optional<int>.Of(theme), setTheme),
                    description: "重启应用后完全生效",
                    headerIcon: FontIcon("\uE790")),

                SettingsExpander(
                    header: "高级",
                    headerIcon: FontIcon("\uE713"),
                    description: "点一下展开",
                    items: new Element?[]
                    {
                        SettingsCard(
                            "自动更新",
                            ToggleSwitch(Optional<bool>.Of(autoUpdate), setAutoUpdate),
                            contentAlignment: SettingsCardContentAlignment.Left),
                        SettingsCard(
                            "清理缓存",
                            Button("清理", () => setLastResult("缓存已清理")),
                            description: "不会删除个人数据"),
                    }),

                Expander(
                    header: "Expander（通用可展开容器）",
                    content: TextBlock("任意内容都可以塞进展开区，不只是设置项。").Wrap(),
                    isExpanded: false),

                TextBlock("弹窗").FontSize(16),
                Button("弹出确认框", () =>
                {
                    // UWP 同一时刻只允许一个 ContentDialog，ReactorDialog 内部有闸门挡着，
                    // 重复点击不会把进程带走。这里不等它（async void 在事件回调里可用）。
                    _ = ReactorDialog.ShowAsync(ContentDialog(
                        title: "确认",
                        message: "要执行这个操作吗？",
                        primaryButtonText: "是",
                        closeButtonText: "否",
                        onResult: r => setLastResult($"点了：{r}")));
                }),
                TextBlock($"上一次弹窗结果：{lastResult}").Caption()
            ).Padding(16));
    }
}
