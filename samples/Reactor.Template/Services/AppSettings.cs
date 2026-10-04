using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace Reactor.Template.Services;

/// <summary>主题：与 XAML 的 <c>ElementTheme</c> 一一对应，只是去掉了 Default 之外的语义。</summary>
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>背景材质：云母或桌面亚克力，对应 <c>BackdropKind</c> 的两个值。</summary>
public enum AppMaterial
{
    Mica = 0,
    Acrylic = 1,
}

/// <summary>导航栏位置。</summary>
public enum PanePosition
{
    Left = 0,
    Top = 1,
}

/// <summary>持久化到应用本地目录的设置。属性都是可变的，改完调 <see cref="AppSettings.Save"/>。</summary>
public sealed class Settings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public AppMaterial Material { get; set; } = AppMaterial.Mica;
    public PanePosition Pane { get; set; } = PanePosition.Left;

    /// <summary>
    /// 控件声音。与 <c>UWP-Blank-Template</c> 一致，默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 这一项除了持久化，还要推给 XAML 的元素音效开关 <see cref="ElementSound"/>。
    /// 之所以不在本类里直接写 <c>ElementSoundPlayer.State</c>：它属于 XAML 投影，
    /// 且必须在 UI 创建之前设置——那是 <see cref="App.OnLaunched"/> 的职责，
    /// Services 这层不该知道"UI 建到哪一步了"。
    /// </remarks>
    public bool Sound { get; set; } = true;
}

// AOT 下不能靠反射序列化，source-gen 上下文是必需品（不是可选优化）。
// 加新设置项时改 Settings 即可，这里不用动。
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>
/// 设置的读写入口。整个模板只有这一处碰磁盘。
/// </summary>
/// <remarks>
/// 为什么不用框架的状态管理：设置是"跨页面、跨组件生命周期"的，
/// 放进 <c>UseState</c> 会随组件卸载丢失。这里用静态单例 + 显式通知，
/// 页面拿到通知后自己重渲染。
/// </remarks>
public static class AppSettings
{
    private const string FileName = "settings.json";

    private static Settings? _current;

    public static Settings Current => _current ??= Load();

    /// <summary>改完设置调一次：落盘 + 让界面重渲染。</summary>
    public static void Update(Action<Settings> mutate, Action? onChanged = null)
    {
        var settings = Current;
        mutate(settings);
        Save(settings);
        onChanged?.Invoke();
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
            System.Diagnostics.Debug.WriteLine($"[AppSettings] 保存失败：{ex.Message}");
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
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettings] 读取失败：{ex.Message}");
        }

        return new Settings();
    }

    private static string FilePath() =>
        System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName);
}
