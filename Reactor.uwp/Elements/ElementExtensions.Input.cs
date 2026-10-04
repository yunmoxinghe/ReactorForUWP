using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 键盘可达性 / 无障碍 / 投影 相关的链式修饰符。
/// </summary>
/// <remarks>
/// 这一段对应 XAML 里<b>每个元素都能写</b>的那批属性：<c>TabIndex</c>、
/// <c>KeyboardAccelerators</c>、<c>KeyDown</c>、<c>ContextFlyout</c>、
/// <c>AccessKey</c>、<c>AutomationProperties.*</c>、<c>UIElement.Shadow</c>、
/// 附加属性 <c>ElementSoundMode</c>。
/// 之前这套 API 只有 <c>AutomationName</c> / <c>AutomationId</c>，其余全是空的——
/// 于是"控件是真的，但键盘走不通、读屏读不出"。
/// <para>
/// 全部是<b>同名映射</b>：没有一个是自己监听按键再手动派发的替代实现。
/// </para>
/// </remarks>
public static partial class ElementExtensions
{
    // ── Tab 顺序与焦点 ──────────────────────────────────────────

    /// <summary>Tab 顺序（<c>TabIndex</c>）。只在 <c>Control</c> 上生效。</summary>
    public static T TabIndex<T>(this T el, int index) where T : Element =>
        Set(el, m => m with { TabIndex = index });

    /// <summary>是否参与 Tab 导航（<c>IsTabStop</c>）。只在 <c>Control</c> 上生效。</summary>
    public static T IsTabStop<T>(this T el, bool enabled = true) where T : Element =>
        Set(el, m => m with { IsTabStop = enabled });

    /// <summary>指针交互时自动取焦点（<c>AllowFocusOnInteraction</c>）。</summary>
    public static T AllowFocusOnInteraction<T>(this T el, bool enabled = true) where T : Element =>
        Set(el, m => m with { AllowFocusOnInteraction = enabled });

    /// <summary>
    /// 挂载并完成首次 <c>Loaded</c> 后请求一次焦点（<c>Focus(FocusState.Programmatic)</c>）。
    /// </summary>
    public static T FocusOnMount<T>(this T el, bool enabled = true) where T : Element =>
        Set(el, m => m with { FocusOnMount = enabled });

    /// <summary>访问键（<c>AccessKey</c>，Alt+字符）。</summary>
    public static T AccessKey<T>(this T el, string key) where T : Element =>
        Set(el, m => m with { AccessKey = key });

    // ── 键盘快捷键 ──────────────────────────────────────────────

    /// <summary>加一个键盘快捷键（<c>&lt;KeyboardAccelerator/&gt;</c>）。可链式叠加。</summary>
    public static T KeyboardAccelerator<T>(
        this T el,
        VirtualKey key,
        VirtualKeyModifiers modifiers = VirtualKeyModifiers.None,
        Action? onInvoked = null,
        bool isEnabled = true) where T : Element =>
        Set(el, m => m with
        {
            KeyboardAccelerators = (m.KeyboardAccelerators ?? Array.Empty<KeyboardAcceleratorSpec>())
                .Append(new KeyboardAcceleratorSpec(key, modifiers, onInvoked, isEnabled))
                .ToArray(),
        });

    /// <summary>一次性设置整组快捷键（覆盖之前声明的）。</summary>
    public static T KeyboardAccelerators<T>(
        this T el, params KeyboardAcceleratorSpec[] accelerators) where T : Element =>
        Set(el, m => m with { KeyboardAccelerators = accelerators });

    /// <summary>Ctrl+S 这类助记：默认带 Control 修饰键。</summary>
    public static T CtrlShortcut<T>(this T el, VirtualKey key, Action? onInvoked = null)
        where T : Element =>
        el.KeyboardAccelerator(key, VirtualKeyModifiers.Control, onInvoked);

    /// <summary>按键按下（<c>KeyDown</c>）。</summary>
    public static T OnKeyDown<T>(this T el, Action<KeyRoutedEventArgs> handler) where T : Element =>
        Set(el, m => m with { OnKeyDown = handler });

    /// <summary>按键抬起（<c>KeyUp</c>）。</summary>
    public static T OnKeyUp<T>(this T el, Action<KeyRoutedEventArgs> handler) where T : Element =>
        Set(el, m => m with { OnKeyUp = handler });

    // ── 右键浮出层 ──────────────────────────────────────────────

    /// <summary>
    /// 右键 / 长按弹出的浮出层（<c>ContextFlyout</c>），一般传 <c>MenuFlyout</c>。
    /// </summary>
    /// <remarks>
    /// 传进来的 <see cref="FlyoutBase"/> 实例要<b>稳定</b>（用字段或 state 存），
    /// 每轮重渲染都 new 一个的话，引用比对判"变了"就会反复重挂。
    /// </remarks>
    public static T ContextFlyout<T>(this T el, FlyoutBase flyout) where T : Element =>
        Set(el, m => m with { ContextFlyout = flyout });

    // ── AutomationProperties ────────────────────────────────────

    /// <summary><c>AutomationProperties.HelpText</c>：读屏补充描述。</summary>
    public static T AutomationHelpText<T>(this T el, string text) where T : Element =>
        Set(el, m => m with { AutomationHelpText = text });

    /// <summary><c>AutomationProperties.FullDescription</c>：完整描述。</summary>
    public static T AutomationFullDescription<T>(this T el, string text) where T : Element =>
        Set(el, m => m with { AutomationFullDescription = text });

    /// <summary><c>AutomationProperties.ItemStatus</c>：如"已下载"。</summary>
    public static T AutomationItemStatus<T>(this T el, string status) where T : Element =>
        Set(el, m => m with { AutomationItemStatus = status });

    /// <summary><c>AutomationProperties.ItemType</c>：如"邮件"。</summary>
    public static T AutomationItemType<T>(this T el, string type) where T : Element =>
        Set(el, m => m with { AutomationItemType = type });

    /// <summary><c>AutomationProperties.Level</c>：层级，1 起。</summary>
    public static T AutomationLevel<T>(this T el, int level) where T : Element =>
        Set(el, m => m with { AutomationLevel = level });

    /// <summary>
    /// <c>AutomationProperties.PositionInSet</c> / <c>SizeOfSet</c>：集合内位置与总数，1 起。
    /// </summary>
    public static T AutomationSetPosition<T>(this T el, int positionInSet, int sizeOfSet)
        where T : Element =>
        Set(el, m => m with
        {
            AutomationPositionInSet = positionInSet,
            AutomationSizeOfSet = sizeOfSet,
        });

    /// <summary><c>AutomationProperties.LiveSetting</c>：实时区域策略。</summary>
    public static T AutomationLiveSetting<T>(this T el, AutomationLiveSetting setting)
        where T : Element =>
        Set(el, m => m with { AutomationLiveSetting = setting });

    /// <summary>
    /// <c>AutomationProperties.AccessibilityView</c>：纯装饰元素应设
    /// <see cref="AccessibilityView.Raw"/>，从读屏视图里去掉。
    /// </summary>
    public static T AutomationAccessibilityView<T>(this T el, AccessibilityView view)
        where T : Element =>
        Set(el, m => m with { AutomationAccessibilityView = view });

    /// <summary>
    /// <c>AutomationProperties.AcceleratorKey</c>：<b>只告知</b>读屏快捷键文本，
    /// 不注册快捷键（注册用 <see cref="KeyboardAccelerator{T}"/>）。
    /// </summary>
    public static T AutomationAcceleratorKey<T>(this T el, string key) where T : Element =>
        Set(el, m => m with { AutomationAcceleratorKey = key });

    // ── 本地化 ──────────────────────────────────────────────────

    /// <summary>
    /// 本地化标识（XAML 的 <c>x:Uid</c>）。挂载时按 <c>Uid.Property</c> 从
    /// <c>Resources.resw</c> 取值并套到控件上。
    /// </summary>
    /// <remarks>
    /// resw 里写 <c>&lt;data name="Greeting.Text"&gt;</c>，这里声明
    /// <c>TextBlock("").Uid("Greeting")</c> 即可——不存在的键不会有任何动作。
    /// 注意：<c>x:Uid</c> 会<b>覆盖</b>你在代码里写的同属性值（与 XAML 行为一致，
    /// 编译器生成的赋值在初始化末尾）。
    /// </remarks>
    public static T Uid<T>(this T el, string uid) where T : Element =>
        Set(el, m => m with { Uid = uid });

    // ── 投影与控件级声音 ────────────────────────────────────────

    /// <summary>
    /// <c>UIElement.Shadow</c>：投影。传 <c>new ThemeShadow()</c> 拿 WinUI 2 的主题投影。
    /// </summary>
    /// <remarks>
    /// 投影要<b>有人接收</b>才看得见：要么把背景元素加进
    /// <c>ThemeShadow.Receivers</c>，要么让元素抬到 Z 轴（<c>Translation</c>）
    /// 或留出间距。实例请保持稳定（字段 / state），每轮 new 会反复重挂。
    /// </remarks>
    public static T Shadow<T>(this T el, Shadow shadow) where T : Element =>
        Set(el, m => m with { Shadow = shadow });

    /// <summary>
    /// 控件级声音策略（附加属性 <c>ElementSoundMode</c>），覆盖全局
    /// <c>ElementSoundPlayer.State</c>。
    /// </summary>
    public static T ElementSoundMode<T>(this T el, ElementSoundMode mode) where T : Element =>
        Set(el, m => m with { ElementSoundMode = mode });
}
