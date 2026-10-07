using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

/// <summary>
/// 布局面板附加属性的链式写法（与 <see cref="GridExtensions"/> 同一套形状）：
/// <c>TextBlock("A").Canvas(left: 20, top: 8)</c>、
/// <c>Border(...).WrapSpan(rowSpan: 2)</c>、
/// <c>TextBlock("B").Relative(below: 0)</c>。
/// 只有直接放在对应容器下的子元素才有意义，挂在别处会被静默忽略。
/// </summary>
public static class LayoutAttachedExtensions
{
    /// <summary>在 <c>Canvas</c> 里的绝对坐标与叠放次序。</summary>
    public static TElement Canvas<TElement>(
        this TElement element,
        double left = 0,
        double top = 0,
        int zIndex = 0)
        where TElement : Element
    {
        if (element is null) throw new System.ArgumentNullException(nameof(element));

        return (TElement)(element with
        {
            Modifiers = (element.Modifiers ?? new ElementModifiers()) with
            {
                Canvas = new CanvasAttached(left, top, zIndex),
            },
        });
    }

    /// <summary>在 <c>VariableSizedWrapGrid</c> 里占几格。</summary>
    public static TElement WrapSpan<TElement>(
        this TElement element,
        int rowSpan = 1,
        int columnSpan = 1)
        where TElement : Element
    {
        if (element is null) throw new System.ArgumentNullException(nameof(element));

        return (TElement)(element with
        {
            Modifiers = (element.Modifiers ?? new ElementModifiers()) with
            {
                WrapSpan = new WrapSpanAttached(rowSpan, columnSpan),
            },
        });
    }

    /// <summary>
    /// 在 <c>RelativePanel</c> 里的相对位置。兄弟类参数填的是<b>同层子元素的下标</b>。
    /// </summary>
    public static TElement Relative<TElement>(
        this TElement element,
        bool alignLeftWithPanel = false,
        bool alignTopWithPanel = false,
        bool alignRightWithPanel = false,
        bool alignBottomWithPanel = false,
        bool alignHorizontalCenterWithPanel = false,
        bool alignVerticalCenterWithPanel = false,
        int? above = null,
        int? below = null,
        int? leftOf = null,
        int? rightOf = null,
        int? alignLeftWith = null,
        int? alignTopWith = null,
        int? alignRightWith = null,
        int? alignBottomWith = null,
        int? alignHorizontalCenterWith = null,
        int? alignVerticalCenterWith = null)
        where TElement : Element
    {
        if (element is null) throw new System.ArgumentNullException(nameof(element));

        return (TElement)(element with
        {
            Modifiers = (element.Modifiers ?? new ElementModifiers()) with
            {
                Relative = new RelativeAttached
                {
                    AlignLeftWithPanel = alignLeftWithPanel,
                    AlignTopWithPanel = alignTopWithPanel,
                    AlignRightWithPanel = alignRightWithPanel,
                    AlignBottomWithPanel = alignBottomWithPanel,
                    AlignHorizontalCenterWithPanel = alignHorizontalCenterWithPanel,
                    AlignVerticalCenterWithPanel = alignVerticalCenterWithPanel,
                    Above = above,
                    Below = below,
                    LeftOf = leftOf,
                    RightOf = rightOf,
                    AlignLeftWith = alignLeftWith,
                    AlignTopWith = alignTopWith,
                    AlignRightWith = alignRightWith,
                    AlignBottomWith = alignBottomWith,
                    AlignHorizontalCenterWith = alignHorizontalCenterWith,
                    AlignVerticalCenterWith = alignVerticalCenterWith,
                },
            },
        });
    }
}
