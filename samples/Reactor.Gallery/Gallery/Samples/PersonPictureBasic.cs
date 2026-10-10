using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Reactor.Gallery.Samples;

/// <summary>
/// <c>PersonPicture</c>：人物头像（WinUI 2 真控件）。
/// </summary>
/// <remarks>
/// <b>显示什么由官方控件的优先级决定，不由这里的参数顺序决定。</b>官方的顺序是：
/// 有 <c>ProfilePicture</c> 就显示图 → 没有就显示 <c>Initials</c> → 再没有才按
/// <c>DisplayName</c> 自己推出首字母。所以"给了名字却显示不出字"，通常是被
/// 更高优先级那一项顶住了。
/// <para>
/// 角标那一格是<b>四种内容互相替换</b>：<c>BadgeNumber</c>（数字）/
/// <c>BadgeGlyph</c>（字形）/ <c>BadgeText</c>（文字）/ <c>BadgeImageSource</c>（图片）
/// ——给了数字就不显示字形，与 <c>InfoBadge</c> 那里的 <c>Value</c> / <c>Icon</c>
/// 是同一种"多选一"的形状。谁赢由官方控件定，别指望叠加显示。
/// </para>
/// </remarks>
public sealed class PersonPictureBasic : Component
{
    /// <summary>生成的头像替代图（avatar.png，透明背景，见 tools/parity/make_sample_media.py）。</summary>
    private const string Source = "ms-appx:///Assets/SampleMedia/avatar.png";

    public override Element Render()
    {
        return VStack(14,
            // 头像自己不带文字，无障碍名字得自己给（UIA 自检会查"可聚焦的都有名字"）。
            TextBlock("三种内容来源").Caption().Subtle(),
            HStack(12,
                Cell("只给名字（首字母自己推）",
                    PersonPicture(displayName: "Ada Lovelace").AutomationName("Ada Lovelace")),
                Cell("给首字母",
                    PersonPicture(displayName: "Grace Hopper", initials: "GH").AutomationName("Grace Hopper")),
                Cell("一组人",
                    PersonPicture(displayName: "研发组", isGroup: true).AutomationName("研发组（群组）"))),

            TextBlock("角标（同一个位置，三种内容互相替换）").Caption().Subtle(),
            HStack(12,
                Cell("数字",
                    PersonPicture(displayName: "Alan Turing", badgeNumber: 3).AutomationName("Alan Turing，3 条未读")),
                Cell("字形",
                    PersonPicture(displayName: "Alan Turing", badgeGlyph: "\uE8BD").AutomationName("Alan Turing，忙碌")),
                Cell("文字",
                    PersonPicture(displayName: "Alan Turing", badgeText: "忙").AutomationName("Alan Turing，状态：忙")),
                Cell("图片",
                    PersonPicture(displayName: "Alan Turing", badgeImageSource: Source)
                        .AutomationName("Alan Turing，角标是图片"))),

            TextBlock("给图片（优先级最高的一项）").Caption().Subtle(),
            HStack(12,
                Cell("有图就显示图",
                    PersonPicture(displayName: "Ada Lovelace", profilePicture: Source)
                        .AutomationName("Ada Lovelace，有头像")),
                Cell("图与首字母同时给",
                    PersonPicture(displayName: "Ada Lovelace", profilePicture: Source, initials: "AL")
                        .AutomationName("Ada Lovelace，头像优先于首字母")),
                Cell("小尺寸仍显示图",
                    PersonPicture(displayName: "Ada Lovelace", profilePicture: Source, preferSmallImage: true)
                        .AutomationName("Ada Lovelace，小尺寸仍显示图"))),
            TextBlock("最后那个「preferSmallImage」要在头像缩到很小（约 64px 以下）时才看得出差别："
                + "默认那一档会为了「认得出字」而退回首字母，打开它则坚持显示图。")
                .Caption().Subtle().Wrap());
    }

    private static Element Cell(string label, Element picture) =>
        VStack(4,
            picture,
            TextBlock(label).Caption().Subtle());
}
