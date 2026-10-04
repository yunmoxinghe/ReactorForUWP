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
/// 纯代码建控件时没有人帮你做这一步，必须自己按 <c>Uid.Property</c> 去查。
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
/// <b>只在挂载时跑一次</b>：语言切换后 UWP 会重建页面（<c>ResourceContext</c>
/// 变化会触发），不需要每轮重渲染都查资源。
/// </para>
/// </remarks>
internal static class Localization
{
    /// <summary>
    /// 资源查找结果缓存：<c>(uid, 属性名) → 值</c>（null = 没这个键）。
    /// 有界（resw 里的键数量），所以是有意的强持有。
    /// </summary>
    private static readonly Dictionary<(string Uid, string Property), string?> Cache = new();

    private static ResourceLoader? _loader;

    /// <summary>
    /// 用 <c>GetForViewIndependentUse</c> 而不是 <c>GetForCurrentView</c>：
    /// 后者要求当前线程有 CoreWindow（后台线程/早期初始化会直接抛）。
    /// </summary>
    private static ResourceLoader Loader =>
        _loader ??= ResourceLoader.GetForViewIndependentUse();

    /// <summary>把 <paramref name="uid"/> 对应的资源套到控件上。</summary>
    public static void ApplyUid(UIElement native, string uid)
    {
        // TextBlock 不是 Control，ContentControl 也不是所有控件——按类型分别兜。
        switch (native)
        {
            case TextBlock block:
                TrySet(uid, "Text", value => block.Text = value);
                break;

            case TextBox box:
                TrySet(uid, "Text", value => box.Text = value);
                TrySet(uid, "Header", value => box.Header = value);
                TrySet(uid, "PlaceholderText", value => box.PlaceholderText = value);
                break;

            case PasswordBox password:
                TrySet(uid, "Header", value => password.Header = value);
                TrySet(uid, "PlaceholderText", value => password.PlaceholderText = value);
                break;

            case AutoSuggestBox suggest:
                TrySet(uid, "Header", value => suggest.Header = value);
                TrySet(uid, "PlaceholderText", value => suggest.PlaceholderText = value);
                break;

            case ContentControl content:
                // Button / CheckBox / RadioButton / HyperlinkButton… 都是 ContentControl。
                TrySet(uid, "Content", value => content.Content = value);
                break;
        }

        // 通用的两个：ToolTip（附加属性）与 AutomationProperties.Name（读屏名）。
        // XAML 里 <c>x:Uid</c> 也会给它们套值。
        TrySet(uid, "ToolTip", value => ToolTipService.SetToolTip(native, value));
        TrySet(uid, "AutomationProperties.Name", value =>
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(native, value));
    }

    private static void TrySet(string uid, string property, Action<string> apply)
    {
        if (Lookup(uid, property) is { } value)
        {
            apply(value);
        }
    }

    /// <summary>
    /// 查 <c>Uid.Property</c>。
    /// </summary>
    /// <remarks>
    /// 两种分隔符都试：resw 里通常写成 <c>Greeting.Text</c>（点），
    /// 而 PRI 内部是分层结构，<c>ResourceLoader</c> 用斜杠寻址
    /// （<c>Greeting/Text</c>）。两边都能命中，先试斜杠。
    /// </remarks>
    private static string? Lookup(string uid, string property)
    {
        var key = (uid, property);
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var value = LookupCore(uid + "/" + property) ?? LookupCore(uid + "." + property);
        Cache[key] = value;
        return value;
    }

    private static string? LookupCore(string key)
    {
        try
        {
            var value = Loader.GetString(key);

            // 键不存在时 GetString 返回空串（不抛），空串视作"没这条资源"。
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 语言切换后清缓存（资源值变了）。UWP 通常会重建页面，但显式留个口子更稳。
    /// </summary>
    public static void Invalidate() => Cache.Clear();
}
