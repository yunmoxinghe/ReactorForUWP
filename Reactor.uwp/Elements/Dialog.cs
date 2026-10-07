using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Reactor.Uwp.Internal;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 模态弹窗，对应 XAML 的 <c>&lt;ContentDialog&gt;</c>。
/// </summary>
/// <remarks>
/// <b>它不进可视树</b>：ContentDialog 一旦被挂在 Panel 上，<c>ShowAsync</c> 就会失败，
/// 而且它的显示时机是"用户点了某个按钮"而不是"页面渲染到某处"。所以它<b>不注册
/// handler</b>（<c>ElementHandlerRegistry</c> 里的元素都是"父容器要把它摆进 Children"的），
/// 而是由 <see cref="ReactorDialog.ShowAsync"/> 走独立的构建路径——
/// 与 XAML 版的用法一一对应：
/// <c>&lt;Dialogs:ExternalOpenDialog/&gt;</c>（声明）+
/// <c>await new ExternalOpenDialog().ShowAsync()</c>（弹出）。
/// <para>
/// 参考模板 Furry-Xiyi/UWP-Blank-Template 的 <c>Dialogs/ExternalOpenDialog.xaml</c>：
/// <c>Style=DefaultContentDialogStyle</c>、<c>DefaultButton=Secondary</c>、
/// 内容是带换行的 14px <c>TextBlock</c>，主按钮"是"、次按钮"否"。
/// </para>
/// </remarks>
public sealed record ContentDialogElement(
    string? Title = null,
    Element? Content = null,
    string? Message = null) : Element
{
    /// <summary>主按钮文字（模板里是"是"）。留空则不显示该按钮。</summary>
    public string? PrimaryButtonText { get; init; }

    /// <summary>次按钮文字（模板里是"否"）。留空则不显示该按钮。</summary>
    public string? SecondaryButtonText { get; init; }

    /// <summary>关闭按钮文字。留空则不显示该按钮。</summary>
    public string? CloseButtonText { get; init; }

    /// <summary>默认聚焦的按钮（模板里是 <see cref="ContentDialogButton.Secondary"/>）。</summary>
    public ContentDialogButton DefaultButton { get; init; } = ContentDialogButton.Primary;

    /// <summary>
    /// 是否铺满整窗（官方 <c>FullSizeDesired</c>）。
    /// </summary>
    /// <remarks>
    /// <b>它是"申请"不是"保证"。</b>官方按可用高度决定给不给：窗口不够高时
    /// 这一笔会被忽略，弹窗仍是常规尺寸。所以别用它来"确保"大弹窗，
    /// 内容真要放很多东西时该做的是内容自己能滚。
    /// </remarks>
    public bool FullSizeDesired { get; init; }

    /// <summary>主按钮可不可用（官方 <c>IsPrimaryButtonEnabled</c>）。</summary>
    public bool IsPrimaryButtonEnabled { get; init; } = true;

    /// <summary>次按钮可不可用（官方 <c>IsSecondaryButtonEnabled</c>）。</summary>
    public bool IsSecondaryButtonEnabled { get; init; } = true;

    /// <summary>
    /// 弹窗样式键，默认 <c>DefaultContentDialogStyle</c>（WinUI 2 的
    /// <c>XamlControlsResources</c> 提供）。找不到就保持原生默认外观。
    /// </summary>
    public string? StyleKey { get; init; } = "DefaultContentDialogStyle";

    /// <summary><see cref="Message"/> 文本的字号；null 时用 14（与模板一致）。</summary>
    public double? MessageFontSize { get; init; }

    /// <summary>关闭后回传用户选择（<see cref="ContentDialogResult"/>）。</summary>
    public Action<ContentDialogResult>? OnResult { get; init; }
}

/// <summary>
/// 把 <see cref="ContentDialogElement"/> 物化成真实的 <see cref="ContentDialog"/> 并显示。
/// </summary>
/// <remarks>
/// UWP 同时只允许一个 ContentDialog 打开，重复调用会抛异常；这里用一个静态闸门挡住，
/// 重复请求返回 <see cref="ContentDialogResult.None"/> 并记一条日志，
/// 绝不让弹窗把进程带走。
/// </remarks>
public static class ReactorDialog
{
    private static bool _isShowing;

    /// <summary>弹出 <paramref name="element"/> 描述的对话框，返回用户点了哪个按钮。</summary>
    public static async Task<ContentDialogResult> ShowAsync(ContentDialogElement element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (_isShowing)
        {
            global::Reactor.Uwp.Hosting.ReactorLog.Warn(global::Reactor.Uwp.Hosting.ReactorLogChannel.Host, "已有弹窗在显示，忽略本次请求（UWP 同时只允许一个 ContentDialog）");
            return ContentDialogResult.None;
        }

        var dialog = Build(element);

        _isShowing = true;
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            global::Reactor.Uwp.Hosting.ReactorLog.Error(global::Reactor.Uwp.Hosting.ReactorLogChannel.Host, $"弹窗显示失败: {ex.GetType().Name}: {ex.Message}");
            result = ContentDialogResult.None;
        }
        finally
        {
            _isShowing = false;
        }

        try
        {
            element.OnResult?.Invoke(result);
        }
        catch (Exception ex)
        {
            global::Reactor.Uwp.Hosting.ReactorLog.Error(global::Reactor.Uwp.Hosting.ReactorLogChannel.Host, $"弹窗回调异常: {ex.GetType().Name}: {ex.Message}");
        }

        return result;
    }

    /// <summary>元素 → 真实 <see cref="ContentDialog"/>（不挂进任何父容器）。</summary>
    private static ContentDialog Build(ContentDialogElement element)
    {
        var dialog = new ContentDialog
        {
            DefaultButton = element.DefaultButton,
            FullSizeDesired = element.FullSizeDesired,
            IsPrimaryButtonEnabled = element.IsPrimaryButtonEnabled,
            IsSecondaryButtonEnabled = element.IsSecondaryButtonEnabled,
        };

        if (element.StyleKey is { Length: > 0 } key)
        {
            if (StyleSheet.Resolve(key) is { } style)
            {
                dialog.Style = style;
            }
            else
            {
                global::Reactor.Uwp.Hosting.ReactorLog.Warn(global::Reactor.Uwp.Hosting.ReactorLogChannel.Resource, $"弹窗样式未找到: {key}");
            }
        }

        if (element.Title is { } title)
        {
            dialog.Title = title;
        }

        if (element.PrimaryButtonText is { } primary)
        {
            dialog.PrimaryButtonText = primary;
        }

        if (element.SecondaryButtonText is { } secondary)
        {
            dialog.SecondaryButtonText = secondary;
        }

        if (element.CloseButtonText is { } close)
        {
            dialog.CloseButtonText = close;
        }

        if (element.Content is { } content)
        {
            // 内容是任意元素树：走一次独立的构建（弹窗不属于页面那棵原生树，
            // 所以用自己的 Reconciler 实例，不与页面的 patch 状态互相干扰）。
            dialog.Content = new Reconciler().Build(content);
        }
        else if (element.Message is { } message)
        {
            dialog.Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = element.MessageFontSize ?? 14,
            };
        }

        return dialog;
    }
}
