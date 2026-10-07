using System;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 命名样式 fluent（对齐官方 Microsoft.UI.Reactor 的
/// <c>ElementExtensions.NamedStyles.cs</c> / Spec 039 §17）。
/// </summary>
/// <remarks>
/// 把 XAML 里最常见的 <c>Style="{StaticResource XxxTextBlockStyle}"</c> 提升为链式方法，
/// 省掉每次都要写 <c>.ApplyStyle("XxxTextBlockStyle")</c>。
/// <para>
/// <b>关于"仅 mount 生效"</b>：官方实现把样式走 <c>OnMount</c>，所以条件切换样式在就地更新时
/// 不会生效。本实现把样式键放进 <see cref="ElementModifiers.StyleKey"/>，
/// 由协调器在 mount 与 update 两条路径上统一应用，因此条件样式是生效的——
/// 这比官方更适合"设置页按状态换样式"的场景。
/// </para>
/// </remarks>
public static partial class ElementExtensions
{
    /// <summary>
    /// 挂一个命名样式（自定义样式表 → 应用资源字典，找不到则保持默认外观）。
    /// </summary>
    public static T ApplyStyle<T>(this T el, string styleKey) where T : Element =>
        Set(el, m => m with { StyleKey = styleKey });

    /// <summary>挂一个现成 <see cref="Style"/> 实例（先注册进样式表再按键引用）。</summary>
    public static T ApplyStyle<T>(this T el, string styleKey, Action<StyleBuilder> build)
        where T : Element
    {
        StyleSheet.Define(styleKey, build);
        return el.ApplyStyle(styleKey);
    }

    // ── 文本样式（WinUI 类型斜坡）────────────────────────────────

    /// <summary><c>CaptionTextBlockStyle</c> — 12px 次要说明文字。</summary>
    public static T Caption<T>(this T el) where T : Element => el.ApplyStyle("CaptionTextBlockStyle");

    /// <summary><c>BodyTextBlockStyle</c> — 14px 正文。</summary>
    public static T Body<T>(this T el) where T : Element => el.ApplyStyle("BodyTextBlockStyle");

    /// <summary><c>BodyStrongTextBlockStyle</c> — 14px 加粗正文（设置页节标题的常用基底）。</summary>
    public static T BodyStrong<T>(this T el) where T : Element => el.ApplyStyle("BodyStrongTextBlockStyle");

    /// <summary><c>SubtitleTextBlockStyle</c> — 20px 副标题。</summary>
    public static T Subtitle<T>(this T el) where T : Element => el.ApplyStyle("SubtitleTextBlockStyle");

    /// <summary><c>TitleTextBlockStyle</c> — 28px 标题。</summary>
    public static T Title<T>(this T el) where T : Element => el.ApplyStyle("TitleTextBlockStyle");

    /// <summary><c>TitleLargeTextBlockStyle</c> — 40px 大标题（WinUI 2.8+）。</summary>
    public static T TitleLarge<T>(this T el) where T : Element => el.ApplyStyle("TitleLargeTextBlockStyle");

    /// <summary><c>HeaderTextBlockStyle</c> — 46px 页眉。</summary>
    public static T Header<T>(this T el) where T : Element => el.ApplyStyle("HeaderTextBlockStyle");

    /// <summary><c>BaseTextBlockStyle</c> — 只带字体族/字号斜坡基底，用于自定义派生。</summary>
    public static T BaseText<T>(this T el) where T : Element => el.ApplyStyle("BaseTextBlockStyle");

    // ── 按钮样式 ────────────────────────────────────────────────

    /// <summary><c>AccentButtonStyle</c> — 强调色主按钮。</summary>
    public static T Accent<T>(this T el) where T : Element => el.ApplyStyle("AccentButtonStyle");

    /// <summary><c>SubtleButtonStyle</c> — 无背景弱化按钮。</summary>
    public static T Subtle<T>(this T el) where T : Element => el.ApplyStyle("SubtleButtonStyle");

    /// <summary><c>TextBlockButtonStyle</c> — 行内链接外观（"了解更多"）。</summary>
    public static T TextLink<T>(this T el) where T : Element => el.ApplyStyle("TextBlockButtonStyle");

    // ── 文本外观（非样式，直接落到属性上）────────────────────────

    /// <summary>换行方式。</summary>
    public static T TextWrapping<T>(this T el, Windows.UI.Xaml.TextWrapping wrapping) where T : Element =>
        Set(el, m => m with { TextWrapping = wrapping });

    /// <summary>自动换行（<see cref="Wrap"/> 的便捷写法）。</summary>
    public static T Wrap<T>(this T el) where T : Element =>
        Set(el, m => m with { TextWrapping = Windows.UI.Xaml.TextWrapping.Wrap });

    /// <summary>不换行。</summary>
    public static T NoWrap<T>(this T el) where T : Element =>
        Set(el, m => m with { TextWrapping = Windows.UI.Xaml.TextWrapping.NoWrap });

    /// <summary>字重。</summary>
    public static T FontWeight<T>(this T el, Windows.UI.Text.FontWeight weight) where T : Element =>
        Set(el, m => m with { FontWeight = weight });

    /// <summary>半粗体（SemiBold）— WinUI 节标题常用。</summary>
    public static T SemiBold<T>(this T el) where T : Element =>
        Set(el, m => m with { FontWeight = Windows.UI.Text.FontWeights.SemiBold });

    /// <summary>粗体。</summary>
    public static T Bold<T>(this T el) where T : Element =>
        Set(el, m => m with { FontWeight = Windows.UI.Text.FontWeights.Bold });

    /// <summary>文本对齐。</summary>
    public static T TextAlignment<T>(this T el, Windows.UI.Xaml.TextAlignment alignment) where T : Element =>
        Set(el, m => m with { TextAlignment = alignment });

    /// <summary>超出容器时怎么截断（<c>TextTrimming</c>）。</summary>
    public static T TextTrimming<T>(this T el, Windows.UI.Xaml.TextTrimming trimming)
        where T : Element =>
        Set(el, m => m with { TextTrimming = trimming });

    /// <summary>
    /// 彩色字形（emoji 那类）按彩色画还是单色画（<c>IsColorFontEnabled</c>）。
    /// </summary>
    public static T ColorFont<T>(this T el, bool enabled = true) where T : Element =>
        Set(el, m => m with { IsColorFontEnabled = enabled });

    /// <summary>字距（<c>CharacterSpacing</c>），单位 1/1000 em。</summary>
    public static T CharacterSpacing<T>(this T el, int spacing) where T : Element =>
        Set(el, m => m with { CharacterSpacing = spacing });

    /// <summary>最大行数（超出按 <c>TextTrimming</c> 截断）。</summary>
    public static T MaxLines<T>(this T el, int maxLines) where T : Element =>
        Set(el, m => m with { MaxLines = maxLines });

    // ── 窗口标题栏 ────────────────────────────────────────────

    /// <summary>
    /// 把该元素设为窗口的自定义标题栏拖拽区（<c>Window.Current.SetTitleBar</c>）。
    /// 需要宿主先开启 <c>ApplicationViewTitleBar.ExtendViewIntoTitleBar</c>。
    /// </summary>
    public static T TitleBar<T>(this T el) where T : Element =>
        Set(el, m => m with { IsTitleBar = true });

    /// <summary>请求主题（Light / Dark / Default），作用于该元素及其子树。</summary>
    public static T Theme<T>(this T el, ElementTheme theme) where T : Element =>
        Set(el, m => m with { RequestedTheme = theme });

    /// <summary>
    /// 声明该页面自己管理标题栏区域（宿主不再自动下压一个标题栏高度）。
    /// 只在根元素上生效。
    /// </summary>
    public static T OwnsTitleBar<T>(this T el) where T : Element =>
        Set(el, m => m with { OwnsTitleBar = true });
}
