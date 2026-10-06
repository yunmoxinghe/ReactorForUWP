using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Windows.Storage;

namespace Reactor.Gallery;

/// <summary>
/// 设置页跨组件共享的那份值。<b>连写入口一起带</b>——只带值的话页面读得到但改不了。
/// </summary>
/// <remarks>
/// 写入口默认给空实现而不是 <c>null</c>：拿不到上下文时（比如某个页面被单独渲染）
/// <c>UseContext</c> 会返回这里的默认实例，此时控件仍要能正常工作，只是改了不落盘。
/// 给 <c>null</c> 的话那套控件会直接失去回调。
/// </remarks>
internal sealed class AppSettings
{
    /// <summary>0 跟随系统 / 1 浅色 / 2 深色。</summary>
    public int Theme { get; init; }

    public bool AutoUpdate { get; init; }

    /// <summary>控件音效开关，落到 <see cref="SoundService"/>。</summary>
    public bool Sound { get; init; }

    public Action<int> SetTheme { get; init; } = _ => { };

    public Action<bool> SetAutoUpdate { get; init; } = _ => { };

    public Action<bool> SetSound { get; init; } = _ => { };
}

/// <summary>
/// 示例的"真实后端"：<c>ApplicationData.LocalSettings</c>。
/// </summary>
/// <remarks>
/// <b>为什么设置要放在外壳而不是页面里。</b>主题必须挂在<b>根元素</b>上才对整个
/// 窗口生效（<c>RequestedTheme</c> 只作用于该元素及其子树），而设置页只是窗口里的
/// 一个子页——状态放在页面上就只有那一页会变主题。所以由
/// <see cref="SampleShell"/> 持有状态并 <c>Provide</c> 下去，页面只负责读写。
/// <para>
/// <b>持久化与 UI 状态的分工。</b>UI 的当下值仍由外壳的 <c>UseState</c> 持有
/// （改它才会重渲染）；这里只负责跨启动那一层——写盘与读盘。
/// 两者混成一个"会自动重渲染的存储"会让重渲染时机变得不可预测，
/// 那是回显类 bug 最常见的来源。
/// </para>
/// </remarks>
internal static class SettingsStore
{
    /// <summary>外壳 <c>Provide</c>、后代 <c>UseContext</c> 的那个键。</summary>
    public static readonly Context<AppSettings> Settings = new(new AppSettings());

    /// <summary>
    /// 拿不到 <c>ApplicationData</c> 时的退路（进程内内存）。
    /// </summary>
    /// <remarks>
    /// 框架自己的注释就写着"非 UI 线程访问 <c>ApplicationData</c> 可能失败"，
    /// 无包标识时更是直接抛。而 <see cref="SampleShell"/> 是在首次 <c>Render()</c>
    /// 里读它的——那时抛异常就是<b>启动即崩</b>，连个日志都来不及留。
    /// 设置页这种"锦上添花"的功能不该有把示例带崩的资格，所以退到内存：
    /// 本次会话内照样能改、能生效，只是不跨启动。
    /// </remarks>
    private static readonly Dictionary<string, object?> Fallback = new();

    private static bool TryGet(string key, out object? value)
    {
        try
        {
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out value);
        }
        catch (Exception)
        {
            return Fallback.TryGetValue(key, out value);
        }
    }

    private static void Put(string key, object? value)
    {
        Fallback[key] = value;

        try
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
        }
        catch (Exception)
        {
            // 已经写进内存退路了，本次会话仍然一致。
        }
    }

    public static int LoadTheme() =>
        TryGet(KeyTheme, out var raw) && raw is int value && value is >= 0 and <= 2
            ? value
            : 0;

    public static void SaveTheme(int theme)
    {
        // 越界值不落盘：下次启动读回来的是个没人认得的下标，
        // ComboBox 会显示成"什么都没选"，而原因在上一轮就已经丢了。
        Put(KeyTheme, theme is >= 0 and <= 2 ? theme : 0);
    }

    public static bool LoadAutoUpdate() => !TryGet(KeyAutoUpdate, out var raw) || raw is not false;

    public static void SaveAutoUpdate(bool on) => Put(KeyAutoUpdate, on);

    /// <summary>
    /// 没设过返回 <c>false</c>——桌面上的默认态本来就听不到声音，
    /// 界面显示"关"与实际听感一致（见 <see cref="SoundService"/> 的依据）。
    /// </summary>
    public static bool LoadSound() => TryGet(KeySound, out var raw) && raw is true;

    public static void SaveSound(bool on) => Put(KeySound, on);

    /// <summary>
    /// 清掉应用自己的临时文件夹。
    /// </summary>
    /// <returns>删掉的文件数。</returns>
    /// <remarks>
    /// 只碰 <c>TemporaryFolder</c>：<c>LocalFolder</c> 里躺着用户数据和这份设置本身，
    /// "不会删除个人数据"这句 description 不能是空话。
    /// </remarks>
    public static async Task<int> ClearTempAsync()
    {
        IReadOnlyList<StorageFile> files;

        try
        {
            files = await ApplicationData.Current.TemporaryFolder.GetFilesAsync();
        }
        catch (Exception)
        {
            // 拿不到临时文件夹（与拿不到 LocalSettings 同源）：报"没有可清理的"，
            // 不把异常冒到 UI 上——按钮点了却崩掉比什么都不做糟得多。
            return 0;
        }

        var removed = 0;

        foreach (var file in files)
        {
            try
            {
                await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                removed++;
            }
            catch (Exception)
            {
                // 单个文件被占用不该让整个清理失败。
            }
        }

        return removed;
    }

    private const string KeyTheme = "theme";

    private const string KeyAutoUpdate = "autoUpdate";

    private const string KeySound = "sound";
}
