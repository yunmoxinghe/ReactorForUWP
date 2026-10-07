using System;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 反馈类控件：<c>RatingControl</c> / <c>PersonPicture</c>。
/// </summary>
/// <remarks>
/// 两个都是 WinUI 2 的真控件（<c>Microsoft.UI.Xaml.Controls</c>），
/// 对应 WinUI 3 Gallery 里「Basic input」与「Media」两页。
/// </remarks>
public static partial class Factories
{
    /// <summary>
    /// 评分。
    /// </summary>
    /// <param name="value">受控值；给 <c>Optional.Unset</c> 表示非受控。</param>
    /// <param name="onValueChanged">值变化回调（官方 <c>ValueChanged</c>）。</param>
    /// <param name="maxRating">一共几颗星。</param>
    /// <param name="isReadOnly">只读：能看不能改。</param>
    /// <param name="isClearEnabled">允许再点一次清成 0。</param>
    /// <param name="caption">右侧那行说明文字。</param>
    /// <param name="placeholderValue">
    /// 底衬浅色星的位置；<c>null</c>（默认）= 不写，控件让它跟着值走。
    /// </param>
    public static RatingElement Rating(
        Optional<double> value = default,
        Action<double>? onValueChanged = null,
        int maxRating = 5,
        bool isReadOnly = false,
        bool isClearEnabled = false,
        string? caption = null,
        double? placeholderValue = null) =>
        new(value, onValueChanged)
        {
            MaxRating = maxRating,
            IsReadOnly = isReadOnly,
            IsClearEnabled = isClearEnabled,
            Caption = caption,
            PlaceholderValue = placeholderValue,
        };

    /// <summary>
    /// 人物头像。
    /// </summary>
    /// <param name="displayName">显示名（没有首字母时按它推）。</param>
    /// <param name="profilePicture">头像图片（优先级最高的一项）。</param>
    /// <param name="initials">首字母缩写。</param>
    /// <param name="badgeNumber">角标数字。</param>
    /// <param name="badgeGlyph">角标字形。</param>
    /// <param name="badgeText">角标文字。</param>
    /// <param name="badgeImageSource">角标图片（与前三种抢同一个位置）。</param>
    /// <param name="isGroup">一组人而不是一个人。</param>
    /// <param name="preferSmallImage">缩到很小时仍然显示图片而不是首字母。</param>
    public static PersonPictureElement PersonPicture(
        string? displayName = null,
        string? profilePicture = null,
        string? initials = null,
        int? badgeNumber = null,
        string? badgeGlyph = null,
        string? badgeText = null,
        string? badgeImageSource = null,
        bool isGroup = false,
        bool preferSmallImage = false) =>
        new(displayName)
        {
            ProfilePicture = profilePicture,
            Initials = initials,
            BadgeNumber = badgeNumber,
            BadgeGlyph = badgeGlyph,
            BadgeText = badgeText,
            BadgeImageSource = badgeImageSource,
            IsGroup = isGroup,
            PreferSmallImage = preferSmallImage,
        };
}
