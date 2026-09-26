using System;
using System.Diagnostics;
using System.Globalization;
using Windows.UI.Xaml;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 强类型 Grid 轨道尺寸（对齐官方 <c>Microsoft.UI.Reactor.GridSize</c>），
/// 取代易写错的字符串形式（<c>"Auto"</c> / <c>"*"</c> / <c>"200"</c> / <c>"1.5*"</c>）。
/// </summary>
/// <remarks>
/// 官方同款的三类智能构造器：<see cref="Auto"/> / <see cref="Star"/> / <see cref="Px"/>，
/// 并在构造时校验取值（Pixel 不能为负、Star 权重必须 &gt; 0）。
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public readonly record struct GridSize
{
    public GridSize(double value, GridUnitType type, double? min = null, double? max = null)
    {
        if (min.HasValue && !IsFiniteAndNonNegative(min.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(min), min, "Min 必须是有限且非负的数。");
        }

        if (max.HasValue && (max.Value < 0 || double.IsNaN(max.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "Max 必须是有限且非负的数。");
        }

        if (type == GridUnitType.Pixel && !IsFiniteAndNonNegative(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Pixel 尺寸必须是非负有限数。");
        }

        if (type == GridUnitType.Star && (!IsFiniteAndNonNegative(value) || value <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Star 权重必须是大于 0 的有限数。");
        }

        Value = value;
        Type = type;
        Min = min;
        Max = max;
    }

    /// <summary>Pixel 时为像素值；Star 时为权重；Auto 时恒为 1。</summary>
    public double Value { get; }

    /// <summary>轨道类型：Auto / Star / Pixel。</summary>
    public GridUnitType Type { get; }

    public double? Min { get; }

    public double? Max { get; }

    public void Deconstruct(out double value, out GridUnitType type) => (value, type) = (Value, Type);

    /// <summary>Auto 轨道。</summary>
    public static GridSize Auto { get; } = new(1, GridUnitType.Auto);

    /// <summary>星号轨道。<paramref name="weight"/> 默认 1（即 <c>"*"</c>）。</summary>
    public static GridSize Star(double weight = 1) => new(weight, GridUnitType.Star);

    /// <summary>固定像素轨道。</summary>
    public static GridSize Px(double pixels) => new(pixels, GridUnitType.Pixel);

    /// <summary>隐式转换为 <see cref="GridLength"/>，便于与任何 WinUI API 组合。</summary>
    public static implicit operator GridLength(GridSize s) => new(s.Value, s.Type);

    /// <summary>解析轨道字符串：<c>Auto</c> / <c>*</c> / <c>1.5*</c> / <c>200</c>。</summary>
    public static GridSize Parse(string s)
    {
        if (s is null) throw new ArgumentNullException(nameof(s));

        var trimmed = s.Trim();
        if (trimmed.Length == 0)
        {
            throw new FormatException("空的 grid 轨道字符串。");
        }

        if (string.Equals(trimmed, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return Auto;
        }

        if (trimmed == "*")
        {
            return Star();
        }

        if (trimmed.EndsWith('*'))
        {
            var numeric = trimmed[..^1];
            if (numeric.Length == 0)
            {
                throw new FormatException($"非法的 grid 轨道 '{s}'。");
            }

            if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var stars) &&
                stars > 0)
            {
                return Star(stars);
            }

            throw new FormatException($"非法的 grid 轨道 '{s}'：无法解析星号权重。");
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) && px >= 0)
        {
            return Px(px);
        }

        throw new FormatException($"非法的 grid 轨道 '{s}'。");
    }

    public override string ToString() => Type switch
    {
        GridUnitType.Auto => "Auto",
        GridUnitType.Star when Value == 1 => "*",
        GridUnitType.Star => Value.ToString("R", CultureInfo.InvariantCulture) + "*",
        GridUnitType.Pixel => Value.ToString("R", CultureInfo.InvariantCulture),
        _ => $"GridSize({Value}, {Type})",
    };

    private static bool IsFiniteAndNonNegative(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
}
