using System;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 拼 <c>x:Uid</c> 对应的资源键（纯字符串，不依赖 XAML 运行时，可脱离 UI 断言）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单独抽出来。</b>键名格式是本地化里最容易搞错、又最难发现的一环
/// ——拼错了不报错，只是"查不到"，表现为那一条永远不翻译。
/// 抽成纯函数才能在没有 UWP 运行时的测试里锁住格式。
/// </para>
/// <para>
/// <b>分隔符是斜杠，不是点。</b>resw 的 Name 列写 <c>Greeting.Text</c>，
/// 但 MakePri 会把点转成<b>分层</b>，PRI 里真正的 uri 是
/// <c>Resources/Greeting/Text</c>（<c>makepri dump -dt detailed</c> 实测）。
/// 所以代码侧必须用斜杠寻址；<c>uid + "." + property</c> 是死代码。
/// </para>
/// <para>
/// <b>附加属性：<c>[using:...]</c> 是键的一部分，不会被剥掉。</b>
/// resw 里写 <c>Tip.[using:Windows.UI.Xaml.Controls]ToolTipService.ToolTip</c>，
/// 打进 PRI 后是 <c>Tip/[using:Windows.UI.Xaml.Controls]ToolTipService/ToolTip</c>
/// ——方括号那段原样保留，只有点变斜杠。网上（包括不少 StackOverflow 回答）
/// 说它会被剥掉，实测<b>不会</b>。
/// </para>
/// </remarks>
internal static class LocalizationKeys
{
    /// <summary>
    /// 附加属性宿主在 resw / PRI 里的写法。
    /// 只有列在这里的属性才会去试附加属性形式的键（与 <see cref="Localization"/>
    /// 「按类型显式列出可本地化属性」的设计一致：不靠反射，AOT 安全）。
    /// </summary>
    public static class Owners
    {
        /// <summary><c>ToolTipService.ToolTip</c>。</summary>
        public const string ToolTipService =
            "[using:Windows.UI.Xaml.Controls]ToolTipService";

        /// <summary><c>AutomationProperties.Name</c> 等。</summary>
        public const string AutomationProperties =
            "[using:Windows.UI.Xaml.Automation]AutomationProperties";
    }

    /// <summary>简单标识符：<c>{uid}/{property}</c>（如 <c>Greeting/Text</c>）。</summary>
    public static string Simple(string uid, string property) => uid + "/" + property;

    /// <summary>
    /// 附加属性：<c>{uid}/{owner}/{property}</c>
    /// （如 <c>Tip/[using:Windows.UI.Xaml.Controls]ToolTipService/ToolTip</c>）。
    /// </summary>
    public static string Attached(string uid, string owner, string property) =>
        uid + "/" + owner + "/" + property;

    /// <summary>
    /// 按优先级列出一个 <c>(uid, 属性)</c> 该试的键。
    /// 先简单标识符、后附加属性形式 —— 命中即止。
    /// </summary>
    public static string[] Candidates(string uid, string property, string? owner = null) =>
        owner is null
            ? new[] { Simple(uid, property) }
            : new[] { Simple(uid, property), Attached(uid, owner, property) };
}
