using System;
using Windows.UI.Xaml;

namespace UwpApp.Services;

/// <summary>
/// 控件声音（XAML 里叫"元素音效"，element sound）的开关。
/// </summary>
/// <remarks>
/// 抄 <c>UWP-Blank-Template</c> 时踩出来的两个坑，都记在这里以免重犯：
/// <list type="number">
/// <item><b>类型在 <c>Windows.UI.Xaml</c>，不在 <c>Windows.UI.Xaml.Controls</c></b>。
///       写成 <c>Windows.UI.Xaml.Controls.ElementSoundPlayer</c> 报的是 CS0234
///       "命名空间中不存在类型"，很容易被误判成"这个工程用不了"。</item>
/// <item><b>必须在创建任何 UI 之前设置</b>。XAML 在应用控件模板时就按当时的
///       <c>ElementSoundPlayer.State</c> 把 <c>ElementSoundMode</c> 定下来了，
///       晚一步设（比如放到页面组件的 <c>UseEffect</c> 里）就不响。
///       参考模板是在 <c>App.OnLaunched</c> 里、<c>rootFrame.Navigate</c> 之前设的，
///       <see cref="App"/> 的 <c>OnLaunched</c> 是同一个位置。</item>
/// </list>
/// 发声的是系统内置控件模板里带 <c>ElementSoundMode</c> 的那些（按钮、开关、
/// 超链接、浮出层等）；自定义控件不会自动有声，别指望这个开关管到它们。
/// </remarks>
public static class ElementSound
{
    public static void Apply(bool on)
    {
        try
        {
            var before = ElementSoundPlayer.State;

            ElementSoundPlayer.State = on
                ? ElementSoundPlayerState.On
                : ElementSoundPlayerState.Off;

            // 写盘（LocalState\reactor-startup.log）：设没设上要能查证，
            // 否则"不响"是没写进去还是写了不发声，全靠猜。
            Reactor.Uwp.Hosting.ReactorApplication.Trace(
                $"[sound] Apply({on}): {before} → {ElementSoundPlayer.State}");
        }
        catch (Exception ex)
        {
            Reactor.Uwp.Hosting.ReactorApplication.Trace($"[sound] Apply({on}) 失败: {ex.Message}");
        }
    }
}
