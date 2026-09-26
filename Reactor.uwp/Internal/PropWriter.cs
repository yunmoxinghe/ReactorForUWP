using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 属性写入原语：把官方 <c>ControlDescriptor</c> 的三档绑定语义搬到手写 handler 架构上。
/// </summary>
/// <remarks>
/// 官方（<c>src/Reactor/Core/V1Protocol/Descriptor/PropEntry.cs</c> + <c>ControlDescriptor</c>）
/// 不是靠"每个 handler 记得写 if"，而是靠绑定声明决定写入行为：
/// <list type="table">
/// <item><term><c>OneWay</c></term><description>mount 写；update 时<b>值变了才写</b>。</description></item>
/// <item><term><c>OneWayConditional</c></term><description>带"该不该写"谓词；<c>Optional.Unset</c> →
/// <c>ClearValue(dp)</c>，让控件回到自己的原生默认。</description></item>
/// <item><term><c>Initial</c> / <c>InitialOnly</c></term><description>只在 mount 写一次，
/// 之后值归控件自己（种子值属性）。</description></item>
/// </list>
/// 我们这套是手写 handler，没有生成器，所以把这三档做成静态方法：
/// 每个 handler 的 <c>Update</c> 一律走这里，不再直接 <c>control.X = v</c>。
/// <para>
/// 为什么必须走这里：XAML 的依赖属性写同值不会触发变化通知，但<b>引用类型</b>
/// （Brush / Style / ImageSource / IconElement）每次 <c>new</c> 出来再赋进去是
/// 真的"变了" → 触发一次样式重应用 / 重绘 → 列表滚动、卡片重渲染时表现为闪烁。
/// 实测：设置页点一次开关，5 个卡片图标因为每次 <c>BuildIcon</c> 造新控件而全被重建。
/// </para>
/// </remarks>
internal static class PropWriter
{
    // ── 挂载期（mount）作用域 ────────────────────────────────────
    //
    // 为什么需要它：diff-and-write 是拿"控件当前值"比，但控件刚 new 出来时读到的
    // 是依赖属性的**默认值**——默认样式（Style 里的 Setter）要等进入可视树、
    // ApplyTemplate 时才生效。于是"声明值恰好等于默认值"的情况会被 diff 判成
    // "没变化"而跳过写入，等样式一应用，样式值就接管了，声明值被无声吞掉。
    //
    // 实测踩到的就是这个：HyperlinkButton 的 .Padding(0) 想盖掉默认样式的
    // 11,5,11,6，结果 new 出来的 Padding 本来就是 0,0,0,0 → 跳过 → 样式值生效，
    // 按钮还是 11,5,11,6。同理 .Margin(0)、.Opacity(1)、.MinHeight(0)、
    // HorizontalAlignment=Stretch 这些"写默认值"的修饰全都会失效。
    //
    // 结论：**挂载期一律无条件写**（先把本地值钉下来，本地值优先级高于样式值），
    // 只有 update 路径才走 diff-and-write（避免无谓的重绘与状态拽回）。
    // 这与官方 OneWay 描述符的语义一致：mount 写，update 时值变了才写。

    [ThreadStatic]
    private static int _mountDepth;

    /// <summary>当前是否处于挂载期（&gt; 0 时 diff-and-write 退化为无条件写）。</summary>
    internal static bool IsMounting => _mountDepth > 0;

    /// <summary>进入挂载期作用域（可嵌套：递归构建子节点时计数递增）。</summary>
    internal static MountScope BeginMount()
    {
        _mountDepth++;
        return default;
    }

    internal readonly struct MountScope : IDisposable
    {
        public void Dispose() => _mountDepth--;
    }

    /// <summary>
    /// <c>OneWay</c>：update 路径的 diff-and-write。旧值 == 新值就什么都不写。
    /// </summary>
    public static void Set<T>(T oldValue, T newValue, Action<T> set)
    {
        if (!IsMounting && EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            return;
        }

        set(newValue);
    }

    /// <summary>
    /// <c>OneWay</c>（引用版）：按引用比较，用于 Brush / Style / ImageSource 这类
    /// "内容相同但每次 new 都是新实例"的属性——引用相同就不重新赋值。
    /// </summary>
    public static void SetRef<T>(T? oldValue, T? newValue, Action<T?> set)
        where T : class
    {
        if (!IsMounting && ReferenceEquals(oldValue, newValue))
        {
            return;
        }

        set(newValue);
    }

    /// <summary>
    /// <c>OneWay</c>（读回版）：与<b>控件当前值</b>比较后再写。
    /// </summary>
    /// <remarks>
    /// 用于会被控件自身规整的属性（<c>Slider.Value</c> 被 Minimum/Maximum 夹取、
    /// <c>ComboBox.SelectedIndex</c> 可能变 -1），此时"声明值"和"控件值"本就可能不同，
    /// 拿 old element 比会漏写。
    /// </remarks>
    public static void SetLive<T>(Func<T> read, T newValue, Action<T> set)
    {
        if (!IsMounting && EqualityComparer<T>.Default.Equals(read(), newValue))
        {
            return;
        }

        set(newValue);
    }

    /// <summary>
    /// <c>OneWayConditional</c> + <c>Optional</c>：没设（<c>Unset</c>）就
    /// <c>ClearValue</c>，让控件回到原生默认；设了才写，且只在值变化时写。
    /// </summary>
    /// <remarks>
    /// 官方 <c>Optional.Unset</c> 走 <c>ClearValue(dp)</c> 的意义：不写的属性不能是
    /// "上一轮的残留值"，必须回到"好像从来没写过"的状态——否则条件切换
    /// （这一轮传值、下一轮不传）会留下脏值。
    /// </remarks>
    public static void SetOptional<T>(
        DependencyObject target,
        DependencyProperty property,
        Optional<T> oldValue,
        Optional<T> newValue)
    {
        if (!newValue.HasValue)
        {
            if (oldValue.HasValue)
            {
                target.ClearValue(property);
            }

            return;
        }

        // 挂载期把本地值钉下来（见类顶注释）：declare 了就一定要写成本地值，
        // 否则会被随后生效的默认样式值盖掉。
        if (!IsMounting && oldValue.HasValue && EqualityComparer<T>.Default.Equals(oldValue.Value, newValue.Value))
        {
            return;
        }

        target.SetValue(property, newValue.Value);
    }

    /// <summary>
    /// <c>OneWayConditional</c>（可空版）：<c>null</c> 表示"这一轮不写"，不做 ClearValue
    /// （区别于 <see cref="SetOptional"/>），用于"传了才生效"的普通可空属性。
    /// </summary>
    public static void SetWhenPresent<T>(T? oldValue, T? newValue, Action<T> set)
        where T : class
    {
        if (newValue is null)
        {
            return;
        }

        if (!IsMounting && ReferenceEquals(oldValue, newValue))
        {
            return;
        }

        set(newValue);
    }

    /// <summary>
    /// Brush 的 diff-and-write：先比引用，再比"内容"（同为纯色时比颜色）。
    /// </summary>
    /// <remarks>
    /// 背景/前景色在每轮重渲染里都会被重新构造（<c>new SolidColorBrush(color)</c>），
    /// 引用必然不同；若按引用判断就会每帧写一次 → 一次真实变化 → 一次重绘。
    /// 纯色 brush 比颜色即可判定"视觉上没变"，非纯色（渐变/图像刷）退化为引用比较。
    /// </remarks>
    public static void SetBrush(DependencyObject target, DependencyProperty property, Brush brush)
    {
        if (!IsMounting)
        {
            if (ReferenceEquals(target.GetValue(property), brush))
            {
                return;
            }

            if (target.GetValue(property) is SolidColorBrush current &&
                brush is SolidColorBrush next &&
                current.Color == next.Color)
            {
                return;
            }
        }

        target.SetValue(property, brush);
    }

    /// <summary>
    /// 颜色 → <see cref="SolidColorBrush"/>：颜色不变就复用控件上已有的 brush，
    /// 绝不每帧 <c>new</c>。
    /// </summary>
    /// <remarks>
    /// 之前 <c>ApplyBackground</c> 里 <c>new SolidColorBrush(color)</c> 是无条件的，
    /// 每轮重渲染都会造一个新 Brush 赋进依赖属性 → 触发一次真正的属性变化 →
    /// 背景重绘（大面积纯色区域尤其明显）。
    /// </remarks>
    public static void SetColorBrush(
        DependencyObject target,
        DependencyProperty property,
        Windows.UI.Color? color)
    {
        if (color is not { } value)
        {
            return;
        }

        if (!IsMounting && target.GetValue(property) is SolidColorBrush existing && existing.Color == value)
        {
            return;
        }

        target.SetValue(property, new SolidColorBrush(value));
    }

    /// <summary>两个只读序列是否内容相同（元素逐个用默认比较器比）。</summary>
    public static bool SequenceEqual<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        a ??= Array.Empty<T>();
        b ??= Array.Empty<T>();

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }
}
