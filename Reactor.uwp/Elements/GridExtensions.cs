using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Grid 附加属性的链式写法（对齐官方 <c>GridExtensions</c>）：
/// <c>TextBlock("hello").Grid(row: 1, column: 2)</c>。
/// 只有直接放在 <c>Grid(...)</c> 下的子元素才有意义。
/// </summary>
public static class GridExtensions
{
    public static TElement Grid<TElement>(
        this TElement element,
        int row = 0,
        int column = 0,
        int rowSpan = 1,
        int columnSpan = 1)
        where TElement : Element
    {
        if (element is null) throw new System.ArgumentNullException(nameof(element));

        return (TElement)(element with
        {
            Modifiers = (element.Modifiers ?? new ElementModifiers()) with
            {
                Grid = new GridAttached(row, column, rowSpan, columnSpan),
            },
        });
    }
}
