using System;
using Windows.UI.Xaml;

namespace Reactor.Template.Services;

/// <summary>
/// 控件声音（XAML 里叫"元素音效"，element sound）的开关。
/// </summary>
/// <remarks>
/// 抄 <c>UWP-Blank-Template</c> 时踩出来的三个坑，都记在这里以免重犯：
/// <list type="number">
/// <item><b>类型在 <c>Windows.UI.Xaml</c>，不在 <c>Windows.UI.Xaml.Controls</c></b>。
///       写成 <c>Windows.UI.Xaml.Controls.ElementSoundPlayer</c> 报的是 CS0234
///       "命名空间中不存在类型"，很容易被误判成"只装 NuGet 包的应用工程用不了"，
///       于是把功能删掉——其实一直能用。</item>
/// <item><b>必须在创建任何 UI 之前设置</b>。XAML 在应用控件模板时就按当时的
///       <c>ElementSoundPlayer.State</c> 把 <c>ElementSoundMode</c> 定下来了，
///       晚一步设（比如只放到组件 <c>UseEffect</c> 里）就不响。
///       参考模板是在 <c>App.OnLaunched</c> 里、<c>rootFrame.Navigate</c> 之前设的，
///       <see cref="App"/> 的 <c>OnLaunched</c> 是同一个位置。</item>
/// <item><b>"开"不等于"听到标准提示音"</b>。Sound 一篇的原文：
///       <c>"Enabling ElementSoundPlayer will automatically enable spatial audio
///       (3D sound) as well. To disable 3D sound (while still keeping the sound on),
///       disable the SpatialAudioMode of the ElementSoundPlayer."</c>
///       ——<c>SpatialAudioMode</c> 默认是 <c>Auto</c>，语义是"音频开着时空间音频
///       跟着开"。只设 <c>State = On</c>，桌面用户听到的就是被空间化过的一层，
///       而不是一下干净的点击音；要在"开"的同时不要 3D，得显式设
///       <c>SpatialAudioMode = Off</c>。这条需要 <c>UniversalApiContract v6</c>
///       （<c>10.0.17134</c>），本示例的下限是 19041，够。
///       <c>Reactor.Gallery</c> 的 <c>SoundService</c> 已经这么做了，两边保持一致：
///       同一个偏好在两个示例里不该给出两种听感。</item>
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

            // 写盘（LocalState\reactor-startup.log）：设没设上必须能查证，
            // 否则"不响"到底是因为值没写进去、还是写进去了但控件不发声，全靠猜。
            Reactor.Uwp.Hosting.ReactorApplication.Trace(
                $"[sound] Apply({on}): {before} → {ElementSoundPlayer.State}");

            if (!on)
            {
                return;
            }

            // 跟上一条坑：把"开"顺带打开的空间音频关掉，保住桌面上的点击音。
            // 单独容错：这个属性比 State 晚两代才加进来（1803 / contract v6），
            // 万一在某个 SDK 组合上不可用，最坏结果是保留 3D，
            // 而不是把整个开关带崩——后者的代价是"点了开关没反应"。
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
            // 音效开关不是关键路径：设不上就当没开，别把界面拖崩。
            Reactor.Uwp.Hosting.ReactorApplication.Trace($"[sound] Apply({on}) 失败: {ex.Message}");
        }
    }
}
