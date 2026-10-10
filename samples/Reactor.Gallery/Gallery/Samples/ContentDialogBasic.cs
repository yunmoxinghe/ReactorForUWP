using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// 模态弹窗：<c>ContentDialog</c>，声明完交给 <c>ReactorDialog.ShowAsync</c> 弹出。
/// </summary>
/// <remarks>
/// UWP 同一时刻只允许一个 <c>ContentDialog</c>；框架内部有闸门，重复调用第二个会
/// 直接返回而不是把进程带走。
/// <para>
/// 事件回调里 <c>async void + 不等</c> 是允许的：结果是异步回来的，
/// 拿到之后 <c>setState</c> 就好。这里刻意用 <c>_ =</c> 丢弃那个 <c>Task</c>，
/// 表示"不参与后续流程"。
/// </para>
/// <para>
/// <b><c>fullSizeDesired</c> 是"申请"不是"保证"</b>：官方按可用高度决定给不给，
/// 窗口不够高时这一笔会被忽略，弹窗仍是常规尺寸。
/// </para>
/// <para>
/// <b><c>isPrimaryButtonEnabled</c> 只管按钮可不可用，不管它在不在。</b>
/// 按钮文字给了才显示；把已显示的按钮置灰是另一件事，官方也是分成两个属性。
/// </para>
/// </remarks>
public sealed class ContentDialogBasic : Component
{
    public override Element Render()
    {
        var (result, setResult) = UseState("还没弹过");

        return VStack(10,
            HStack(10,
                Button("弹出确认框", () =>
                {
                    _ = ReactorDialog.ShowAsync(ContentDialog(
                        title: "确认",
                        message: "要执行这个操作吗？",
                        primaryButtonText: "是",
                        closeButtonText: "否",
                        onResult: value => setResult(value.ToString())));
                }).Accent(),

                Button("铺满整窗（fullSizeDesired）", () =>
                {
                    _ = ReactorDialog.ShowAsync(ContentDialog(
                        title: "铺满",
                        message: "窗口够高时这一档会占满整屏；不够高则被忽略、退回常规尺寸。",
                        primaryButtonText: "知道了",
                        fullSizeDesired: true,
                        onResult: value => setResult($"铺满 → {value}")));
                }),

                Button("主按钮置灰（isPrimaryButtonEnabled）", () =>
                {
                    _ = ReactorDialog.ShowAsync(ContentDialog(
                        title: "不可用",
                        message: "主按钮在，但点不动——「在不在」与「能不能点」是两个属性。",
                        primaryButtonText: "删除",
                        secondaryButtonText: "取消",
                        isPrimaryButtonEnabled: false,
                        defaultButton: ContentDialogButton.Secondary,
                        onResult: value => setResult($"置灰 → {value}")));
                }),

                TextBlock($"上次结果：{result}").Caption().Subtle().VAlign(VerticalAlignment.Center)),

            TextBlock("置灰那一档把默认按钮挪到「取消」上，否则默认焦点会落在一个点不动的按钮。")
                .Caption().Subtle().Wrap());
    }
}
