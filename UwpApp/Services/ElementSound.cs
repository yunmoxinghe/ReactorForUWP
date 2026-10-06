using System;
using Windows.UI.Xaml;

namespace UwpApp.Services;

/// <summary>
/// 控件声音（XAML 里叫"元素音效"，element sound）的开关。
/// </summary>
/// <remarks>
/// 抄 <c>UWP-Blank-Template</c> 时踩出来的三个坑，都记在这里以免重犯：
/// <list type="number">
/// <item><b>类型在 <c>Windows.UI.Xaml</c>，不在 <c>Windows.UI.Xaml.Controls</c></b>。
///       写成 <c>Windows.UI.Xaml.Controls.ElementSoundPlayer</c> 报的是 CS0234
///       "命名空间中不存在类型"，很容易被误判成"这个工程用不了"。</item>
/// <item><b>必须在创建任何 UI 之前设置</b>。XAML 在应用控件模板时就按当时的
///       <c>ElementSoundPlayer.State</c> 把 <c>ElementSoundMode</c> 定下来了，
///       晚一步设（比如放到页面组件的 <c>UseEffect</c> 里）就不响。
///       参考模板是在 <c>App.OnLaunched</c> 里、<c>rootFrame.Navigate</c> 之前设的，
///       <see cref="App"/> 的 <c>OnLaunched</c> 是同一个位置。</item>
/// <item><b>"开"不等于"听到标准提示音"</b>。Sound 一篇的原文：
///       <c>"Enabling ElementSoundPlayer will automatically enable spatial audio
///       (3D sound) as well. To disable 3D sound (while still keeping the sound on),
///       disable the SpatialAudioMode of the ElementSoundPlayer."</c>
///       <c>SpatialAudioMode</c> 默认是 <c>Auto</c>，也就是"音频开着时空间音频跟着开"。
///       只设 <c>State = On</c>，桌面听到的就是被空间化过的一层；
///       要"开"且不要 3D，得显式 <c>SpatialAudioMode = Off</c>
///       （需 <c>UniversalApiContract v6</c> / <c>10.0.17134</c>，本工程下限 19041）。
///       这条三处同一份实现都要有：<c>Reactor.Gallery</c> 的 <c>SoundService</c>、
///       <c>Reactor.Template</c> 的 <c>ElementSound</c>、以及这里——
///       漏一份，手工验证时听到的就是另一种声音，排查方向会被带跑。</item>
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

            if (!on)
            {
                return;
            }

            // 把"开"顺带打开的空间音频关掉，保住桌面上的标准点击音（见上面的坑 3）。
            // 单独容错：这个属性比 State 晚两代才有（1803 / contract v6），
            // 万一哪套 SDK 上取不到，最坏结果是保留 3D，而不是把整个开关带崩。
            try
            {
                ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
            }
            catch (Exception ex)
            {
                Reactor.Uwp.Hosting.ReactorApplication.Trace(
                    $"[sound] SpatialAudioMode 设置失败（保留默认）: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Reactor.Uwp.Hosting.ReactorApplication.Trace($"[sound] Apply({on}) 失败: {ex.Message}");
        }
    }
}
