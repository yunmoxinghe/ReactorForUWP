using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    // ── 布局补完 ───────────────────────────────────────────────

    /// <summary>
    /// 绝对定位画布。子元素用 <c>.Canvas(left: …, top: …)</c> 报坐标。
    /// </summary>
    public static CanvasElement Canvas(params Element?[] children) =>
        new(FilterChildren(children));

    /// <summary>
    /// 把一份内容整体缩放到可用空间里。
    /// </summary>
    public static ViewboxElement Viewbox(
        Element? child,
        Stretch stretch = Stretch.Uniform,
        StretchDirection stretchDirection = StretchDirection.Both) =>
        new(child) { Stretch = stretch, StretchDirection = stretchDirection };

    /// <summary>
    /// 不等大小换行网格（官方 <see cref="VariableSizedWrapGrid"/>）。
    /// 子元素用 <c>.WrapSpan(rowSpan: …, columnSpan: …)</c> 跨格。
    /// </summary>
    public static VariableSizedWrapGridElement WrapGrid(
        double? itemWidth = null,
        double? itemHeight = null,
        int? maximumRowsOrColumns = null,
        Orientation orientation = Orientation.Horizontal,
        params Element?[] children) =>
        new(FilterChildren(children))
        {
            ItemWidth = itemWidth,
            ItemHeight = itemHeight,
            MaximumRowsOrColumns = maximumRowsOrColumns,
            Orientation = orientation,
        };

    /// <summary>
    /// 相对布局面板。子元素用 <c>.Relative(below: 0, alignRightWithPanel: true)</c>
    /// 这类式子表达"相对谁"。
    /// </summary>
    public static RelativePanelElement RelativePanel(params Element?[] children) =>
        new(FilterChildren(children));
}
