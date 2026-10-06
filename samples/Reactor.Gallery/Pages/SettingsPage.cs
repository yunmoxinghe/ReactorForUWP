using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Pages;

/// <summary>
/// 设置页：SettingsCard / SettingsExpander / Expander / ContentDialog。
/// </summary>
/// <remarks>
/// 前三个是 CommunityToolkit 的 SettingsControls，包已经带上依赖，不用另外装。
/// <para>
/// <b>页面不再自己持有设置状态。</b>主题要挂在根元素上才对整个窗口生效，
/// 状态由 <see cref="SampleShell"/> 持有并通过 <see cref="SettingsStore.Settings"/>
/// 下发，这里只负责读写。这样"换个页面设置就丢"和"只有本页变主题"两个毛病
/// 一起消失了。
/// </para>
/// </remarks>
public sealed class SettingsPage : Component
{
    public override Element Render()
    {
        var settings = UseContext(SettingsStore.Settings);
        var (lastResult, setLastResult) = UseState("还没操作过");

        return ScrollViewer(
            VStack(12,
                TextBlock("设置页").FontSize(20),

                SettingsCard(
                    "主题",
                    ComboBox(
                        new[] { "跟随系统", "浅色", "深色" },
                        Optional<int>.Of(settings.Theme),
                        settings.SetTheme),
                    description: "立即生效，并写入本地设置（下次启动保持）",
                    headerIcon: FontIcon("\uE790")),

                SettingsCard(
                    "控件音效",
                    HStack(8,
                        ToggleSwitch(
                            Optional<bool>.Of(settings.Sound),
                            settings.SetSound),
                        Button("试听", () =>
                            {
                                var (_, msg) = SoundService.TryPreview();
                                setLastResult(msg);
                            })
                            // 关的时候按钮禁用：这样调用点必然处于 State==On，
                            // 不必假设 Play 到底受不受 State 影响（详见 SoundService）。
                            .Disabled(!settings.Sound)),
                    description: "控件自带的系统音效（ElementSoundPlayer）。桌面默认态是不响的，" +
                                 "设为开后才会在所有设备上播放；已关掉它会顺带启用的空间音频。" +
                                 "写入本地设置，下次启动保持",
                    headerIcon: FontIcon("\uE7F3")),

                SettingsExpander(
                    header: "高级",
                    headerIcon: FontIcon("\uE713"),
                    description: "点一下展开",
                    items: new Element?[]
                    {
                        SettingsCard(
                            "自动更新",
                            ToggleSwitch(
                                Optional<bool>.Of(settings.AutoUpdate),
                                settings.SetAutoUpdate),
                            contentAlignment: SettingsCardContentAlignment.Left),
                        SettingsCard(
                            "清理缓存",
                            Button("清理", async () =>
                            {
                                // 异步但不等：async void 在事件回调里是允许的，
                                // 结果回来再 setState（和下面弹窗一个套路）。
                                var removed = await SettingsStore.ClearTempAsync();
                                setLastResult($"已清理 {removed} 个临时文件");
                            }),
                            description: "只清应用临时文件夹，不动个人数据"),
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
                TextBlock($"上一次操作结果：{lastResult}").Caption()
            ).Padding(16));
    }
}
