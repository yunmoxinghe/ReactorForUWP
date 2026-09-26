using System;
using System.Collections.Generic;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 表示「显式赋值」或「未设置」两种状态，用于区分「未传入」和「传入默认值」。
/// 对引用类型，传 <c>null</c> 是「显式 null（显式赋值）」，而 <see cref="Unset"/> 才表示「未传入」。
/// 因此 <c>with { Background = null }</c> 会得到 <see cref="Of"/>(null)，而非 <see cref="Unset"/>。
/// </summary>
/// <typeparam name="T">存储值的类型。</typeparam>
public readonly struct Optional<T> : IEquatable<Optional<T>>
{
    private readonly bool _hasValue;
    private readonly T _value;

    /// <summary>是否包含显式赋值。</summary>
    public bool HasValue => _hasValue;

    /// <summary>存储的值；未设置时抛 <see cref="InvalidOperationException"/>。</summary>
    public T Value => _hasValue ? _value : throw new InvalidOperationException("Optional<T> is Unset");

    /// <summary>存储的值；未设置时返回 <c>default(T)</c>。</summary>
    public T GetValueOrDefault() => _value;

    /// <summary>存储的值；未设置时返回 <paramref name="fallback"/>。</summary>
    public T GetValueOrDefault(T fallback) => _hasValue ? _value : fallback;

    /// <summary>未设置状态，等价于 <c>default(Optional&lt;T&gt;)</c>。</summary>
    public static Optional<T> Unset => default;

    /// <summary><see cref="Unset"/> 的别名（等价）。</summary>
    public static Optional<T> Empty => Unset;

    /// <summary>创建一个包含显式赋值的 optional。</summary>
    public static Optional<T> Of(T value) => new(value);

    private Optional(T value)
    {
        _hasValue = true;
        _value = value;
    }

    /// <summary>隐式装箱：把值转为包含显式赋值的 optional。</summary>
    public static implicit operator Optional<T>(T value) => new(value);

    /// <summary><see cref="GetValueOrDefault(T)"/> 的别名（等价）。</summary>
    public T OrDefault(T value) => GetValueOrDefault(value);

    /// <summary>两个 optional 是否表示相同的状态与值。</summary>
    public bool Equals(Optional<T> other)
    {
        if (_hasValue != other._hasValue)
            return false;
        return !_hasValue || EqualityComparer<T>.Default.Equals(_value, other._value);
    }

    public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (!_hasValue)
            return 0;

        var valueHash = _value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(_value);
        return unchecked((valueHash * 397) ^ 1);
    }

    public override string ToString() => _hasValue ? _value?.ToString() ?? "null" : "Unset";

    public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

    public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);
}