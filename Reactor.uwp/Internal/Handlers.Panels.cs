using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 绝对定位画布（对应 <see cref="Canvas"/>）。
/// </summary>
/// <remarks>
/// 子元素的位置不在容器身上，而在<b>子元素自己身上</b>（<c>Canvas.Left</c> 等附加
/// 属性），所以这边的 <c>AttachChild</c> 只做一件事：把元素上那份
/// <see cref="CanvasAttached"/> 落到刚造出来的（或复用的）控件上。
/// 与 <c>GridHandler</c> 处理 <c>GridAttached</c> 是同一个形状。
/// </remarks>
internal sealed class CanvasHandler : ElementHandler<CanvasElement, Canvas>
{
    protected override Canvas Mount(Reconciler reconciler, CanvasElement element)
    {
        var canvas = new Canvas();
        reconciler.BuildChildren(canvas, element, element.Children);
        return canvas;
    }

    protected override void Update(
        Reconciler reconciler,
        CanvasElement oldElement,
        CanvasElement newElement,
        Canvas control) =>
        reconciler.PatchPanelChildren(control, newElement, oldElement.Children, newElement.Children);

    protected override IReadOnlyList<Element?>? ChildrenOf(CanvasElement element) => element.Children;

    protected override Panel? PanelOf(Canvas control) => control;

    public override void AttachChild(UIElement parent, UIElement child, Element childElement)
    {
        if (childElement.Modifiers?.Canvas is not { } attached)
        {
            return;
        }

        Canvas.SetLeft(child, attached.Left);
        Canvas.SetTop(child, attached.Top);
        Canvas.SetZIndex(child, attached.ZIndex);
    }
}

/// <summary>
/// 缩放容器（对应 <see cref="Viewbox"/>）：把一个子元素整体缩放到可用空间里。
/// </summary>
/// <remarks>
/// 它是单子元素容器，走 <c>PatchSingleChild</c>。<c>Viewbox</c> 继承
/// <c>FrameworkElement</c> 而不是 <c>ContentControl</c>，所以在
/// <c>SingleChildAccessor</c> 里登记过——不登记就每轮重建整棵子树。
/// </remarks>
internal sealed class ViewboxHandler : ElementHandler<ViewboxElement, Viewbox>
{
    protected override Viewbox Mount(Reconciler reconciler, ViewboxElement element)
    {
        var viewbox = new Viewbox();
        ApplyProps(viewbox, null, element);

        if (element.Child is not null)
        {
            viewbox.Child = reconciler.Build(element.Child);
        }

        return viewbox;
    }

    protected override void Update(
        Reconciler reconciler,
        ViewboxElement oldElement,
        ViewboxElement newElement,
        Viewbox control)
    {
        ApplyProps(control, oldElement, newElement);
        reconciler.PatchSingleChild(control, oldElement.Child, newElement.Child);
    }

    protected override Element? SingleChildOf(ViewboxElement element) => element.Child;

    /// <summary>挂载时 <paramref name="oldElement"/> 为 null（无条件全写），更新时逐字段 diff。</summary>
    private static void ApplyProps(Viewbox viewbox, ViewboxElement? oldElement, ViewboxElement newElement)
    {
        if (oldElement is null)
        {
            if (newElement.Stretch is { } stretch)
            {
                viewbox.Stretch = stretch;
            }

            if (newElement.StretchDirection is { } direction)
            {
                viewbox.StretchDirection = direction;
            }

            return;
        }

        PropWriter.Set(oldElement.Stretch, newElement.Stretch, value =>
        {
            if (value is { } stretch)
            {
                viewbox.Stretch = stretch;
            }
        });

        PropWriter.Set(oldElement.StretchDirection, newElement.StretchDirection, value =>
        {
            if (value is { } direction)
            {
                viewbox.StretchDirection = direction;
            }
        });
    }
}

/// <summary>
/// 不等大小换行网格（对应 <see cref="VariableSizedWrapGrid"/>）。
/// </summary>
/// <remarks>
/// 与 <c>Canvas</c> 一样，跨格写在<b>子元素</b>身上（<c>RowSpan</c> /
/// <c>ColumnSpan</c> 附加属性）。它<b>没有</b> <c>SetRow</c> / <c>SetColumn</c>——
/// 格子位置由换行顺序自己定，这也是 <see cref="WrapSpanAttached"/> 里只有两格的
/// 原因（不是复用 <c>GridAttached</c>，那个会多出两个在这里没有落点的字段）。
/// </remarks>
internal sealed class VariableSizedWrapGridHandler
    : ElementHandler<VariableSizedWrapGridElement, VariableSizedWrapGrid>
{
    protected override VariableSizedWrapGrid Mount(
        Reconciler reconciler, VariableSizedWrapGridElement element)
    {
        var grid = new VariableSizedWrapGrid();
        ApplyProps(grid, null, element);
        reconciler.BuildChildren(grid, element, element.Children);
        return grid;
    }

    protected override void Update(
        Reconciler reconciler,
        VariableSizedWrapGridElement oldElement,
        VariableSizedWrapGridElement newElement,
        VariableSizedWrapGrid control)
    {
        ApplyProps(control, oldElement, newElement);
        reconciler.PatchPanelChildren(control, newElement, oldElement.Children, newElement.Children);
    }

    protected override IReadOnlyList<Element?>? ChildrenOf(VariableSizedWrapGridElement element) =>
        element.Children;

    protected override Panel? PanelOf(VariableSizedWrapGrid control) => control;

    public override void AttachChild(UIElement parent, UIElement child, Element childElement)
    {
        if (childElement.Modifiers?.WrapSpan is not { } span)
        {
            return;
        }

        VariableSizedWrapGrid.SetRowSpan(child, span.RowSpan);
        VariableSizedWrapGrid.SetColumnSpan(child, span.ColumnSpan);
    }

    private static void ApplyProps(
        VariableSizedWrapGrid grid,
        VariableSizedWrapGridElement? oldElement,
        VariableSizedWrapGridElement newElement)
    {
        if (oldElement is null)
        {
            if (newElement.Orientation is { } orientation)
            {
                grid.Orientation = orientation;
            }

            if (newElement.ItemWidth is { } width)
            {
                grid.ItemWidth = width;
            }

            if (newElement.ItemHeight is { } height)
            {
                grid.ItemHeight = height;
            }

            if (newElement.MaximumRowsOrColumns is { } max)
            {
                grid.MaximumRowsOrColumns = max;
            }

            return;
        }

        PropWriter.Set(oldElement.Orientation, newElement.Orientation, value =>
        {
            if (value is { } orientation)
            {
                grid.Orientation = orientation;
            }
        });

        PropWriter.Set(oldElement.ItemWidth, newElement.ItemWidth, value =>
        {
            if (value is { } width)
            {
                grid.ItemWidth = width;
            }
        });

        PropWriter.Set(oldElement.ItemHeight, newElement.ItemHeight, value =>
        {
            if (value is { } height)
            {
                grid.ItemHeight = height;
            }
        });

        PropWriter.Set(oldElement.MaximumRowsOrColumns, newElement.MaximumRowsOrColumns, value =>
        {
            if (value is { } max)
            {
                grid.MaximumRowsOrColumns = max;
            }
        });
    }
}

/// <summary>
/// 相对布局面板（对应 <see cref="RelativePanel"/>）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>Canvas</c> / <c>VariableSizedWrapGrid</c> 不同：它的附加属性有两类，
/// 一类只问面板（<c>AlignXxxWithPanel</c>，布尔），另一类要指向<b>另一个兄弟控件</b>。
/// 后者的落点不在 <c>AttachChild</c> 里——那个钩子只拿得到自己这一个子元素，
/// 拿不到兄弟，所以这里改成"孩子们都造好之后再整体重落一遍"。
/// </para>
/// <para>
/// 兄弟在元素树里是<b>下标</b>（见 <see cref="RelativeAttached"/>）。下标换控件这一步
/// 在 <c>PatchPanelChildren</c> 之后做，此时 <c>panel.Children</c> 与元素
/// <c>Children</c> 的顺序是对齐的。
/// </para>
/// <para>
/// 每轮<b>全量重落</b>（不设的项落回默认），所以"删掉一条关系"真的会失效，
/// 不会留下上一轮的旧值。
/// </para>
/// </remarks>
internal sealed class RelativePanelHandler : ElementHandler<RelativePanelElement, RelativePanel>
{
    protected override RelativePanel Mount(Reconciler reconciler, RelativePanelElement element)
    {
        var panel = new RelativePanel();
        ApplyProps(panel, null, element);
        reconciler.BuildChildren(panel, element, element.Children);
        ApplyRelations(panel, element.Children);
        return panel;
    }

    protected override void Update(
        Reconciler reconciler,
        RelativePanelElement oldElement,
        RelativePanelElement newElement,
        RelativePanel control)
    {
        ApplyProps(control, oldElement, newElement);
        reconciler.PatchPanelChildren(control, newElement, oldElement.Children, newElement.Children);

        // Patch 可能插入 / 移动过子项，下标与控件的对应关系要重新落一遍。
        ApplyRelations(control, newElement.Children);
    }

    protected override IReadOnlyList<Element?>? ChildrenOf(RelativePanelElement element) => element.Children;

    protected override Panel? PanelOf(RelativePanel control) => control;

    private static void ApplyProps(
        RelativePanel panel,
        RelativePanelElement? oldElement,
        RelativePanelElement newElement)
    {
        if (oldElement is null)
        {
            if (newElement.Padding is { } padding)
            {
                panel.Padding = padding;
            }

            if (newElement.Background is not null)
            {
                panel.Background = newElement.Background;
            }

            return;
        }

        PropWriter.Set(oldElement.Padding, newElement.Padding, value =>
        {
            if (value is { } padding)
            {
                panel.Padding = padding;
            }
        });

        PropWriter.SetRef(oldElement.Background, newElement.Background, value =>
        {
            if (value is not null)
            {
                panel.Background = value;
            }
        });
    }

    /// <summary>把每个子元素上的 <see cref="RelativeAttached"/> 全量落一遍（null = 全落默认）。</summary>
    private static void ApplyRelations(RelativePanel panel, IReadOnlyList<Element?>? children)
    {
        if (children is null)
        {
            return;
        }

        // 元素里有 null 占位 / 组件包装时两边数量可能对不上，对不上就整轮跳过——
        // 关系错位比没有关系更难查。
        if (panel.Children.Count != children.Count)
        {
            return;
        }

        for (var i = 0; i < children.Count; i++)
        {
            ApplyOne(panel, panel.Children[i], children[i]?.Modifiers?.Relative);
        }
    }

    private static void ApplyOne(RelativePanel panel, UIElement child, RelativeAttached? rel)
    {
        RelativePanel.SetAlignLeftWithPanel(child, rel?.AlignLeftWithPanel ?? false);
        RelativePanel.SetAlignTopWithPanel(child, rel?.AlignTopWithPanel ?? false);
        RelativePanel.SetAlignRightWithPanel(child, rel?.AlignRightWithPanel ?? false);
        RelativePanel.SetAlignBottomWithPanel(child, rel?.AlignBottomWithPanel ?? false);
        RelativePanel.SetAlignHorizontalCenterWithPanel(child, rel?.AlignHorizontalCenterWithPanel ?? false);
        RelativePanel.SetAlignVerticalCenterWithPanel(child, rel?.AlignVerticalCenterWithPanel ?? false);

        RelativePanel.SetAbove(child, Sibling(panel, rel?.Above));
        RelativePanel.SetBelow(child, Sibling(panel, rel?.Below));
        RelativePanel.SetLeftOf(child, Sibling(panel, rel?.LeftOf));
        RelativePanel.SetRightOf(child, Sibling(panel, rel?.RightOf));
        RelativePanel.SetAlignLeftWith(child, Sibling(panel, rel?.AlignLeftWith));
        RelativePanel.SetAlignTopWith(child, Sibling(panel, rel?.AlignTopWith));
        RelativePanel.SetAlignRightWith(child, Sibling(panel, rel?.AlignRightWith));
        RelativePanel.SetAlignBottomWith(child, Sibling(panel, rel?.AlignBottomWith));
        RelativePanel.SetAlignHorizontalCenterWith(child, Sibling(panel, rel?.AlignHorizontalCenterWith));
        RelativePanel.SetAlignVerticalCenterWith(child, Sibling(panel, rel?.AlignVerticalCenterWith));
    }

    /// <summary>
    /// 下标换成兄弟控件。越界（删了那一格却忘了改关系）给 null——
    /// 官方收到 null 就是"这条关系不成立"，比抛异常或随便挑一个强。
    /// </summary>
    private static object? Sibling(Panel panel, int? index) =>
        index is { } i && i >= 0 && i < panel.Children.Count ? panel.Children[i] : null;
}
