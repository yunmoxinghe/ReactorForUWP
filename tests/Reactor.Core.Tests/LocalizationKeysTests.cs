using System;

using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// <c>x:Uid</c> 资源键拼法的回归测试。
/// </summary>
/// <remarks>
/// 键名是本地化里最容易错、又最难发现的一环：拼错了不报错，只是"查不到"，
/// 表现为那一条永远不翻译、也不留任何痕迹。这里锁的是三条实测结论
/// （<c>makepri dump -dt detailed</c> 在本仓库的 PRI 上验过）：
/// <list type="number">
/// <item><b>分隔符是斜杠，不是点。</b>resw 的 Name 列写 <c>Greeting.Text</c>，
/// 但 MakePri 把点转成<b>分层</b>，PRI 里真正 uri 是 <c>Resources/Greeting/Text</c>。
/// 所以 <c>uid + "." + property</c> 是死代码，永远查不到。</item>
/// <item><b><c>[using:...]</c> 不会被剥掉</b>，它是键的一部分。
/// resw 写 <c>Tip.[using:Windows.UI.Xaml.Controls]ToolTipService.ToolTip</c>，
/// 进 PRI 是 <c>Tip/[using:Windows.UI.Xaml.Controls]ToolTipService/ToolTip</c>。
/// 网上（含不少高赞回答）说会被剥掉 —— 实测不会。</item>
/// <item><b>附加属性必须带宿主</b>。只写 <c>Tip/ToolTip</c> 命中不了
/// 官方写法生成的键，于是 ToolTip / AutomationProperties.Name 永远不翻译。</item>
/// </list>
/// </remarks>
internal static class LocalizationKeysTests
{
    public static void Run()
    {
        Program.Section("本地化资源键拼法（x:Uid）");

        // 1) 简单标识符：斜杠分层
        Program.Check(
            "简单属性 = Uid/Property（斜杠，不是点）",
            LocalizationKeys.Simple("Greeting", "Text") == "Greeting/Text");

        // 2) 附加属性：[using:...] 原样进键
        var tip = LocalizationKeys.Attached(
            "Tip", LocalizationKeys.Owners.ToolTipService, "ToolTip");

        Program.Check(
            "ToolTip 的键带 [using:...] 宿主（不会被剥掉）",
            tip == "Tip/[using:Windows.UI.Xaml.Controls]ToolTipService/ToolTip");

        var automation = LocalizationKeys.Attached(
            "Greeting", LocalizationKeys.Owners.AutomationProperties, "Name");

        Program.Check(
            "AutomationProperties.Name 的键带 [using:...] 宿主",
            automation == "Greeting/[using:Windows.UI.Xaml.Automation]AutomationProperties/Name");

        // 3) 候选顺序：先简单、后附加
        var candidates = LocalizationKeys.Candidates(
            "Tip", "ToolTip", LocalizationKeys.Owners.ToolTipService);

        Program.Check("附加属性给出两个候选（先简单后附加）", candidates.Length == 2);
        Program.Check("第一个候选是简单形式", candidates[0] == "Tip/ToolTip");
        Program.Check("第二个候选是带宿主的形式", candidates[1] == tip);

        // 4) 没给宿主就只有一个候选（普通属性不该去试附加形式）
        Program.Check(
            "普通属性只有一个候选",
            LocalizationKeys.Candidates("Greeting", "Text").Length == 1);

        // 5) 宿主常量本身：改错一个字符就全部查不到，所以显式锁住
        Program.Check(
            "ToolTipService 宿主常量写法正确",
            LocalizationKeys.Owners.ToolTipService ==
                "[using:Windows.UI.Xaml.Controls]ToolTipService");
        Program.Check(
            "AutomationProperties 宿主常量写法正确",
            LocalizationKeys.Owners.AutomationProperties ==
                "[using:Windows.UI.Xaml.Automation]AutomationProperties");
    }
}
