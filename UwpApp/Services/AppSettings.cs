// 设置持久化：对齐 UWP-Blank-Template 的 SettingsManager（写在 App.xaml.cs 里那个）。
//
// 模板那版是"字符串字段 + 赋值即 Save"，每改一项落一次盘；这里沿用
// samples/Reactor.Template 的强类型枚举写法（同一套 JSON source-gen，AOT 安全），
// 落盘时机一样：改一项存一次。差别只有枚举 vs 字符串，和多了"控件声音"一项。
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace UwpApp.Services;

/// <summary>主题。下标与设置页 RadioButtons 的顺序一致（跟随系统 / 浅色 / 深色）。</summary>
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>背景材质。下标与设置页 RadioButtons 的顺序一致（Mica / Acrylic）。</summary>
public enum AppMaterial
{
    Mica = 0,
    Acrylic = 1,
}

/// <summary>导航栏位置。下标与设置页 ComboBox 的顺序一致（左侧 / 顶部）。</summary>
public enum PanePosition
{
    Left = 0,
    Top = 1,
}

/// <summary>持久化到应用本地目录的设置。属性可变，改完走 <see cref="AppSettings.Update"/>。</summary>
public sealed class Settings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public AppMaterial Material { get; set; } = AppMaterial.Mica;
    public PanePosition Pane { get; set; } = PanePosition.Left;

    /// <summary>
    /// 控件声音。模板的默认值是 <c>true</c>（<c>AppSettings.EnableSound = true</c>）。
    /// </summary>
    public bool Sound { get; set; } = true;
}

// AOT 下不能靠反射序列化，source-gen 上下文是必需品（不是可选优化）。
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>
/// 设置的读写入口，整个模板只有这一处碰磁盘。
/// </summary>
/// <remarks>
/// 为什么不放 <c>UseState</c>：设置是"跨页面、跨组件生命周期"的，
/// 进了组件状态就会随组件卸载丢失，重启也没了。这里用静态单例 + 显式通知。
/// </remarks>
public static class AppSettings
{
    private const string FileName = "settings.json";

    private static Settings? _current;

    public static Settings Current => _current ??= Load();

    /// <summary>改一项：落盘。UI 那边的 state 由页面自己同步。</summary>
    public static void Update(Action<Settings> mutate)
    {
        mutate(Current);
        Save(Current);
    }

    public static void Save(Settings settings)
    {
        _current = settings;

        try
        {
            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings);
            File.WriteAllText(FilePath(), json);
        }
        catch (Exception ex)
        {
            Reactor.Uwp.Hosting.ReactorApplication.Trace($"[AppSettings] 保存失败：{ex.Message}");
        }
    }

    private static Settings Load()
    {
        try
        {
            var path = FilePath();
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize(
                    File.ReadAllText(path), SettingsJsonContext.Default.Settings);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Reactor.Uwp.Hosting.ReactorApplication.Trace($"[AppSettings] 读取失败：{ex.Message}");
        }

        return new Settings();
    }

    private static string FilePath() =>
        Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName);
}
