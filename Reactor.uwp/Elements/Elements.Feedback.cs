using System;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 评分控件，对应 WinUI 2 的 <c>Microsoft.UI.Xaml.Controls.RatingControl</c>。
/// </summary>
/// <remarks>
/// <b>受控与非受控的分界线和别处一样是那个 <see cref="Optional{T}"/></b>：
/// 给了值 + 给了回调就是受控（值由 state 回写），只给值就是种子值。
/// 官方的 <c>ValueChanged</c> 在拖动过程中会连续抛，与 <c>Slider</c> 同形，
/// 所以回调里别做重活。
/// <para>
/// <c>IsReadOnly</c> 那一档（"只展示不让改"）是官方控件的属性，不是把回调
/// 摘掉能顶替的：摘掉回调之后控件照样能点，只是点了没人理——用户看到的是
/// "点了没反应"，而不是"这是只读的"。
/// </para>
/// </remarks>
public sealed record RatingElement(
    Optional<double> Value = default,
    Action<double>? OnValueChanged = null) : Element
{
    /// <summary>一共几颗星（XAML 的 <c>MaxRating</c>，官方默认 5）。</summary>
    public int MaxRating { get; init; } = 5;

    /// <summary>只读（XAML 的 <c>IsReadOnly</c>）：可展示、不可改。</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>允许再点一次把值清成 0（XAML 的 <c>IsClearEnabled</c>）。</summary>
    public bool IsClearEnabled { get; init; }

    /// <summary>右侧那行说明文字（<c>Caption</c>，如"3.5 分 / 128 人评价"）。</summary>
    public string? Caption { get; init; }

    /// <summary>
    /// 底衬那一层"浅色星"的位置（XAML 的 <c>PlaceholderValue</c>）。
    /// </summary>
    /// <remarks>
    /// 官方默认 <c>-1</c>，含义是"跟着 <c>Value</c> 走"——所以本元素上用
    /// <b>null 表示不写</b>，而不是把 <c>-1</c> 抄成默认值：写了 <c>-1</c> 与不写
    /// 在官方那里是同一件事，但"不写"不会把控件自己的默认顶掉。
    /// 真正要"底衬比当前值更满"（官方 Gallery 的对比演示）时才给它，例如
    /// <c>Value = 3</c> 配 <c>PlaceholderValue = 5</c>：看到 3 颗实心星压在 5 颗浅色星上。
    /// </remarks>
    public double? PlaceholderValue { get; init; }
}

/// <summary>
/// 人物头像，对应 WinUI 2 的 <c>Microsoft.UI.Xaml.Controls.PersonPicture</c>。
/// </summary>
/// <remarks>
/// <b>展示顺序由官方控件决定，不由这里的参数顺序决定。</b>官方的优先级是：
/// <c>ProfilePicture</c> 有图就显示图；没有图退到 <c>Initials</c>；再没有才按
/// <c>DisplayName</c> 自己推出首字母。所以"给了 <c>DisplayName</c> 却显示不出字"
/// 通常是初值被更高优先级那一项顶住了，去看那一项。
/// <para>
/// <c>BadgeNumber</c> / <c>BadgeGlyph</c> / <c>BadgeText</c> 是同一个角标位的
/// 三种内容，与 <c>InfoBadge</c> 那里一样是<b>互相替换</b>的：给了数字就不显示字形。
/// </para>
/// </remarks>
public sealed record PersonPictureElement(string? DisplayName = null) : Element
{
    /// <summary>
    /// 头像图片（包内路径或绝对 URI，走 <c>PackUri</c>）。
    /// </summary>
    /// <remarks>
    /// 它是官方优先级里<b>最高</b>的那一项：给了图就显示图，
    /// <see cref="Initials"/> 与 <see cref="DisplayName"/> 都退到后面。
    /// </remarks>
    public string? ProfilePicture { get; init; }

    /// <summary>首字母缩写（如 <c>"AB"</c>）。不给时按 <c>DisplayName</c> 推。</summary>
    public string? Initials { get; init; }

    /// <summary>角标里的数字。</summary>
    public int? BadgeNumber { get; init; }

    /// <summary>角标里的字形（Segoe MDL2 码位，如 <c>"\uE8BD"</c>）。</summary>
    public string? BadgeGlyph { get; init; }

    /// <summary>角标里的文字。</summary>
    public string? BadgeText { get; init; }

    /// <summary>这是"一组人"而不是一个人（XAML 的 <c>IsGroup</c>）：换成多人那个剪影。</summary>
    public bool IsGroup { get; init; }

    /// <summary>
    /// 角标里的<b>图片</b>（包内路径或绝对 URI，走 <c>PackUri</c>）。
    /// </summary>
    /// <remarks>
    /// 它与 <see cref="BadgeNumber"/> / <see cref="BadgeGlyph"/> / <see cref="BadgeText"/>
    /// 抢的是<b>同一个角标位</b>：同时给了几样，显示哪一样由官方控件自己定。
    /// 所以这四样是"四选一"，不是"叠加显示"。
    /// </remarks>
    public string? BadgeImageSource { get; init; }

    /// <summary>
    /// 图片比"首字母"更优先（XAML 的 <c>PreferSmallImage</c>）。
    /// </summary>
    /// <remarks>
    /// 官方默认 <c>false</c>：头像被缩到很小（小于约 64px 那一档）时，为了
    /// <b>还认得出字</b>，它会退回显示 <see cref="Initials"/> 而不是继续显示
    /// <c>ProfilePicture</c>——小尺寸下图片细节已经糊了，首字母反而清楚。
    /// 打开这一项等于说"再小也要看图"。
    /// </remarks>
    public bool PreferSmallImage { get; init; }
}
