using System;
using System.Diagnostics;
using Windows.UI.Xaml;

namespace Reactor.Gallery;

/// <summary>
/// 示例里"声音"的<b>真实后端</b>：OS XAML 自带的 <see cref="ElementSoundPlayer"/>。
/// </summary>
/// <remarks>
/// <b>为什么选它而不是自己去放音频文件。</b>它是系统控件音效（焦点移动、调用、
/// 弹窗开关那一套），不需要引入音频文件或第三方库，一个静态属性就是完整后端。
/// API 自 <c>10.0.14393</c>（<c>UniversalApiContract v3</c>）就在，本示例
/// <c>TargetPlatformMinVersion=19041</c>，不存在可用性的问题。
/// <para>
/// <b>关键事实——决定了 UI 该长什么样。</b><c>ElementSoundPlayer.State</c>
/// 的文档 Remarks 原文：
/// <c>"By default, control sounds are played on the Xbox, and are not played on
/// other devices families. You can set ElementSoundPlayerState to On to make your
/// app play sounds on all device families, or set it to Off to disable sounds on
/// all device families."</c>
/// 也就是说<b>桌面上的默认态（<c>Auto</c>）压根不响</b>。这条直接影响两处设计：
/// <list type="number">
///   <item>"开"必须显式设成 <c>On</c>，只留 <c>Auto</c> 等于什么都没接；</item>
///   <item><b>没有把 Auto 做成第三个选项</b>——它在桌面上与"关"听起来毫无区别，
///         那是个没人能感知的装饰品。</item>
/// </list>
/// 所以这里是严格两态：开 = <c>On</c>（桌面也响），关 = <c>Off</c>（明确禁用）。
/// 初始值取 <c>false</c>：桌面用户在没设过时的真实听感就是没有声音，
/// 界面显示"关"与实际一致，不撒谎。
/// <b>代价（写清楚）</b>：首次进入会把原本的 <c>Auto</c> 改成 <c>Off</c>。
/// 在目标平台（UWP 桌面）上两者听感相同，零损失；但在 Xbox 上 <c>Auto</c>
/// 是会响的——本示例只面向桌面，故不接受这个平台差异。
/// </para>
/// <para>
/// <b>试听按钮为什么做成"关时禁用"。</b><c>Play</c> 的文档只有一句
/// <c>"Plays the specified sound."</c>，<b>没说它是否绕开 <c>State</c></b>。
/// 不去猜：让试听只在开关为开时可点，于是调用点必然处于 <c>State == On</c>，
/// 两种可能的语义下都会响。这样不需要任何断言，用户点一下就能自证后端接通。
/// </para>
/// <para>
/// <b>时机属于调用方，不在这里。</b><c>State</c> 只在<b>控件模板被应用的那一刻</b>
/// 起作用（模板据此定 <c>ElementSoundMode</c>），所以必须由 <see cref="App"/>
/// 抢在界面树建立之前设一次；只挂在本类的调用点上是补不回来的。
/// 见 <see cref="App.OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs)"/>。
/// </para>
/// </remarks>
internal static class SoundService
{
    /// <summary>把偏好落到真实后端。</summary>
    /// <remarks>
    /// 吞异常是<b>刻意</b>的：这个调用既出现在 <see cref="App"/> 的启动路径上，
    /// 在这儿抛就是白屏——跟 <see cref="SettingsStore"/> 里给 <c>ApplicationData</c>
    /// 兜底是同一个判据：<b>锦上添花的功能不该有把示例带崩的资格</b>。
    /// 写个静态属性原则上不抛，但那句"原则上"不值得拿启动去赌；
    /// 失败时留 <c>Debug</c> 痕迹，代价仅是本项设置不生效。
    /// </remarks>
    public static void Apply(bool on)
    {
        try
        {
            ElementSoundPlayer.State = on
                ? ElementSoundPlayerState.On
                : ElementSoundPlayerState.Off;

            if (!on)
            {
                return;
            }

            // 文档（Sound / 全局 API 一节）原文：
            // "Enabling ElementSoundPlayer will automatically enable spatial audio
            //  (3D sound) as well. To disable 3D sound (while still keeping the sound
            //  on), disable the SpatialAudioMode of the ElementSoundPlayer."
            // 也就是说"开音效"顺带会把音频渲染切成 3D——那不是桌面示例想要的：
            // 用户期待的是一下标准的点击提示音，不是被空间化了的东西。
            // 这条**集中在开关为真时才设**，且单独容错：万一这个属性的名字/可用性
            // 在未来 SDK 上有出入，最坏的结果是保留 3D，而不是把整个开关带崩。
            try
            {
                ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Gallery] SpatialAudioMode 设置失败（保留默认）：{ex}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gallery] ElementSoundPlayer.State 设置失败：{ex}");
        }
    }

    /// <summary>
    /// 试听一声（<c>ElementSoundKind.Invoke</c>：点击 / 调用音）。
    /// </summary>
    /// <returns>成不成立，以及能直接显示给用户看的结果文字。</returns>
    /// <remarks>
    /// 吞异常而不是让它冒出去：<c>Play</c> 走的是系统音频通道，无音频设备之类的
    /// 环境下抛不抛没法证实。让它把按钮点崩，比"点了没声音"糟得多——而后者
    /// 还能用返回文字说明白发生了什么。
    /// </remarks>
    public static (bool Ok, string Message) TryPreview()
    {
        try
        {
            ElementSoundPlayer.Play(ElementSoundKind.Invoke);
            return (true, "已播放 Invoke 音（听到说明后端接通）");
        }
        catch (Exception ex)
        {
            return (false, $"播放失败：{ex.GetType().Name}");
        }
    }
}
