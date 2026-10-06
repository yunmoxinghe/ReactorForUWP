namespace Reactor.Uwp.Internal;

/// <summary>
/// 受控数值的<b>夹取判据</b>：声明值写进控件后会变成多少。
/// </summary>
/// <remarks>
/// <para>
/// <b>它回答的是"登记回声该用哪个值"。</b>写 <c>Value</c> 之前要先
/// <c>Expect</c> 一个值，好让控件随后抛的那一发被认出来是自己写的。
/// 登记<b>声明值</b>是不行的：控件会把越界的值夹到区间上，回读出来的
/// 是夹取后的值，于是 <c>Consume</c> 判 <c>mismatch</c> —— 那一发就此
/// 被当成用户输入回调出去（state 被一个用户从未选过的值改写）。
/// </para>
/// <para>
/// <b>源码依据（WinUI 2 分支 <c>dev/NumberBox/NumberBox.cpp</c>）。</b>
/// <c>CoerceValue</c> 在 <c>Value</c> 越界且
/// <c>ValidationMode == InvalidInputOverwritten</c>（idl 里枚举的第一项，即默认值）
/// 时 <c>Value(Minimum())</c> 或 <c>Value(max)</c>；<c>OnValuePropertyChanged</c>
/// 随后以 <c>newValue = Value()</c>（夹取之后的值）抛 <c>ValueChanged</c>。
/// 也就是说回读出来的<b>是夹取后的值</b>，不是写进去的那个。
/// </para>
/// <para>
/// <b>改的不是控件的终态。</b>写 <c>declared</c> 会被控件夹成 <c>Coerce(...)</c>，
/// 直接写 <c>Coerce(...)</c> 也是同一个终态——区别只在"我们事先知不知道会变成什么"，
/// 也就是回声登记能不能对上。所以这一条<b>不改变界面行为</b>，只让回声可预测。
/// </para>
/// <para>
/// <b><c>NaN</c> 不夹。</b><c>NumberBox</c> 用 <c>NaN</c> 表示"空"
/// （<c>CoerceValue</c> 开头就是 <c>!std::isnan(value)</c>），
/// 所以这里照它原样返回，别把空值夹成下界。
/// </para>
/// <para>
/// <b>调用方保证 <c>min &lt;= max</c>。</b>两个控件自己也会把反着的区间夹回来
/// （<c>NumberBox</c> 的 <c>CoerceMinimum</c> / <c>CoerceMaximum</c>），
/// 而 handler 是先写 <c>Minimum</c> 再写 <c>Maximum</c>、读完两个边界才调这里，
/// 拿到的已经是控件夹过的顺序。
/// </para>
/// </remarks>
internal static class RangePolicy
{
    /// <summary>把声明值夹进 <c>[min, max]</c>；<c>NaN</c> 原样返回（表示"空"）。</summary>
    public static double Coerce(double declared, double min, double max)
    {
        if (double.IsNaN(declared))
        {
            return declared;
        }

        if (declared < min)
        {
            return min;
        }

        return declared > max ? max : declared;
    }
}
