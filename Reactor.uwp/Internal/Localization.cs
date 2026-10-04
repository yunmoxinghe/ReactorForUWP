using System;
using System.Collections.Generic;
using Windows.ApplicationModel.Resources;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 本地化：把 XAML 的 <c>x:Uid</c> 语义搬到纯代码里。
/// </summary>
/// <remarks>
/// <para>
/// <b>XAML 那边是怎么做的。</b><c>&lt;TextBlock x:Uid="Greeting"/&gt;</c> 由 XAML
/// <b>编译器</b>展开：生成 <c>ResourceLoader.GetForCurrentView().GetString(
/// "Greeting/Text")</c> 并赋到 <c>Text</c> 上。所以 <c>x:Uid</c> 在运行时<b>不留下
/// 任何痕迹</b>（<c>UIElement</c> 上根本没有 <c>Uid</c> 这个属性可查）——
/// 纯代码建控件时没有人帮你做这一步，必须自己按 <c>Uid/Property</c> 去查。
/// </para>
/// <para>
/// <b>这里的实现。</b>声明 <c>.Uid("Greeting")</c> 之后，挂载时按类型逐个试探
/// 该类型<b>有本地化意义</b>的属性（<c>TextBlock</c> 试 <c>Text</c>、
/// <c>TextBox</c> 试 <c>Text</c> / <c>Header</c> / <c>PlaceholderText</c>、
/// <c>ContentControl</c> 试 <c>Content</c>…），查到就赋值，查不到就不动——
/// 与 XAML 编译器的产物一致（它也是只为 resw 里真实存在的键生成赋值）。
/// </para>
/// <para>
/// <b>为什么不按属性名反射找依赖属性。</b>那需要运行时反射查
/// <c>&lt;Name&gt;Property</c> 静态字段，AOT/裁剪下靠 <c>rd.xml</c> 续命，
/// 且失败是静默的（找不到就什么都不套）。按类型显式列出可本地化属性，
/// 编译期就确定，AOT 安全。
/// </para>
/// <para>
/// <b>只在挂载时跑一次</b>——所以语言切换后必须<b>重建整棵树</b>，光重渲染没用
/// （patch 路径不会重跑 <c>ApplyUid</c>）。见 <see cref="Invalidate"/> 的注释。
/// </para>
/// </remarks>
internal static class Localization
{
    /// <summary>
    /// 资源查找结果缓存：<c>(uid, 属性名) → 值</c>（null = 没这个键）。
    /// 有界（resw 里的键数量），所以是有意的强持有；语言切换时整体丢弃。
    /// </summary>
    /// <remarks>
    /// 比较器<b>必须</b>大小写不敏感：资源标识符（resw 的 Name 列）按官方规则是
    /// 大小写不敏感的，用默认的 <see cref="StringComparer.Ordinal"/> 会让
    /// <c>Greeting/Text</c> 与 <c>greeting/text</c> 变成两条缓存，
    /// 进而同一条资源被查两次——不会错，但缓存形同虚设。
    /// </remarks>
    private static readonly Dictionary<ResourceKey, string?> Cache = new(ResourceKey.Comparer.Instance);

    /// <summary>锁 <see cref="Cache"/> 与 <see cref="_loader"/>（后台线程也可能查资源）。</summary>
    private static readonly object Gate = new();

    private static ResourceLoader? _loader;

    /// <summary>
    /// 用 <c>GetForViewIndependentUse</c> 而不是 <c>GetForCurrentView</c>：
    /// 后者要求当前线程有 CoreWindow（后台线程/早期初始化会直接抛）。
    /// </summary>
    private static ResourceLoader Loader
    {
        get
        {
            lock (Gate)
            {
                return _loader ??= ResourceLoader.GetForViewIndependentUse();
            }
        }
    }

    /// <summary>把 <paramref name="uid"/> 对应的资源套到控件上。</summary>
    public static void ApplyUid(UIElement native, string uid)
    {
        // TextBlock 不是 Control，ContentControl 也不是所有控件——按类型分别兜。
        switch (native)
        {
            case TextBlock block:
                TrySet(uid, "Text", apply: value => block.Text = value);
                break;

            case TextBox box:
                TrySet(uid, "Text", apply: value => box.Text = value);
                TrySet(uid, "Header", apply: value => box.Header = value);
                TrySet(uid, "PlaceholderText", apply: value => box.PlaceholderText = value);
                break;

            case PasswordBox password:
                TrySet(uid, "Header", apply: value => password.Header = value);
                TrySet(uid, "PlaceholderText", apply: value => password.PlaceholderText = value);
                break;

            case AutoSuggestBox suggest:
                TrySet(uid, "Header", apply: value => suggest.Header = value);
                TrySet(uid, "PlaceholderText", apply: value => suggest.PlaceholderText = value);
                break;

            case ContentControl content:
                // Button / CheckBox / RadioButton / HyperlinkButton… 都是 ContentControl。
                TrySet(uid, "Content", apply: value => content.Content = value);
                break;
        }

        // 附加属性：XAML 里 x:Uid 也会给它们套值，但键名是
        // <Uid>/[using:...]<宿主>/<属性>（方括号那段不会被剥掉，makepri 实测）。
        // 只写 "Uid/ToolTip" 是查不到的——这也是纯代码 UI 里本地化"看起来没生效"
        // 最常见的成因。见 Internal/LocalizationKeys.cs。
        TrySet(
            uid,
            "ToolTip",
            owner: LocalizationKeys.Owners.ToolTipService,
            apply: value => ToolTipService.SetToolTip(native, value));

        TrySet(
            uid,
            "Name",
            owner: LocalizationKeys.Owners.AutomationProperties,
            apply: value =>
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(native, value));
    }

    private static void TrySet(string uid, string property, Action<string> apply, string? owner = null)
    {
        if (Lookup(uid, property, owner) is { } value)
        {
            apply(value);
        }
    }

    /// <summary>
    /// 查 <c>Uid/Property</c>；<paramref name="owner"/> 非空时再试附加属性形式的键。
    /// </summary>
    private static string? Lookup(string uid, string property, string? owner)
    {
        var key = new ResourceKey(uid, property);

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        string? value = null;
        foreach (var candidate in LocalizationKeys.Candidates(uid, property, owner))
        {
            if (LookupCore(candidate) is { } found)
            {
                value = found;
                break;
            }
        }

        lock (Gate)
        {
            Cache[key] = value;
        }

        return value;
    }

    private static string? LookupCore(string key)
    {
        try
        {
            var value = Loader.GetString(key);

            // 键不存在时 GetString 返回空串（不抛）。
            // 注意语义：这里把空串视作"没这条资源"，所以 resw 里留空 value
            // 等价于"这条没翻译"。官方 API 不区分这两种情况（没有 TryGetString），
            // 而"留空"本来也不会有人想要显示成空文本，所以合并处理是安全的。
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 语言切换后调用：清缓存 <b>并重建 ResourceLoader</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>两个都必须做，只清缓存是没用的。</b><c>ResourceLoader</c> 在创建时
    /// 会<b>快照</b>当时的 <c>ResourceContext</c>，之后就一直用那份快照查资源。
    /// 所以语言变了而 loader 还是旧的，查出来的仍是旧语言的字符串——
    /// 表现就是"切了语言但界面没变，重启应用才好"。必须把它丢掉重建。
    /// </para>
    /// <para>
    /// <b>光调用这个方法还不够。</b><c>ApplyUid</c> 只在挂载时跑，切语言后走的是
    /// patch 路径，不会重跑它。所以调用方还必须让宿主<b>重建整棵树</b>
    /// （<c>ReactorHost</c> 里是置 <c>ForceRebuild</c>），否则界面依旧不更新。
    /// </para>
    /// </remarks>
    public static void Invalidate()
    {
        lock (Gate)
        {
            Cache.Clear();
            _loader = null;
        }
    }

    /// <summary>缓存键：<c>(uid, 属性)</c>，大小写不敏感比较。</summary>
    private readonly struct ResourceKey : IEquatable<ResourceKey>
    {
        public ResourceKey(string uid, string property)
        {
            Uid = uid;
            Property = property;
        }

        public string Uid { get; }

        public string Property { get; }

        public bool Equals(ResourceKey other) =>
            string.Equals(Uid, other.Uid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Property, other.Property, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is ResourceKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Uid),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Property));

        /// <summary>大小写不敏感的比较器（资源标识符按官方规则大小写不敏感）。</summary>
        public sealed class Comparer : IEqualityComparer<ResourceKey>
        {
            public static readonly Comparer Instance = new();

            public bool Equals(ResourceKey x, ResourceKey y) => x.Equals(y);

            public int GetHashCode(ResourceKey obj) => obj.GetHashCode();
        }
    }
}
