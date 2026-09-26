using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>VStack / HStack（对齐官方 StackElement）。</summary>
internal sealed class StackHandler : ElementHandler<StackElement, StackPanel>
{
    protected override StackPanel Mount(Reconciler reconciler, StackElement element)
    {
        var panel = new StackPanel { Orientation = element.Orientation };
        if (element.Spacing is { } spacing)
        {
            panel.Spacing = spacing;
        }

        reconciler.BuildChildren(panel, element, element.Children);
        return panel;
    }

    protected override void Update(
        Reconciler reconciler,
        StackElement oldElement,
        StackElement newElement,
        StackPanel control)
    {
        PropWriter.Set(oldElement.Orientation, newElement.Orientation, value => control.Orientation = value);
        PropWriter.Set(oldElement.Spacing, newElement.Spacing, value =>
        {
            if (value is { } spacing)
            {
                control.Spacing = spacing;
            }
        });

        reconciler.PatchPanelChildren(control, newElement, oldElement.Children, newElement.Children);
    }

    protected override IReadOnlyList<Element?>? ChildrenOf(StackElement element) => element.Children;

    protected override Panel? PanelOf(StackPanel control) => control;
}

/// <summary>Group：不引入布局策略的多子元素容器（渲染为裸 Grid）。</summary>
internal sealed class GroupHandler : ElementHandler<GroupElement, Grid>
{
    protected override Grid Mount(Reconciler reconciler, GroupElement element)
    {
        var grid = new Grid();
        reconciler.BuildChildren(grid, element, element.Children);
        return grid;
    }

    protected override void Update(
        Reconciler reconciler,
        GroupElement oldElement,
        GroupElement newElement,
        Grid control) =>
        reconciler.PatchPanelChildren(control, newElement, oldElement.Children, newElement.Children);

    protected override IReadOnlyList<Element?>? ChildrenOf(GroupElement element) => element.Children;

    protected override Panel? PanelOf(Grid control) => control;
}

/// <summary>二维布局容器：行列定义 + 子元素上的 <c>.Grid(row: …)</c> 附加位置。</summary>
internal sealed class GridHandler : ElementHandler<GridElement, Grid>
{
    protected override Grid Mount(Reconciler reconciler, GridElement element)
    {
        var grid = new Grid();
        ApplyDefinition(grid, element.Definition);
        grid.RowSpacing = element.RowSpacing;
        grid.ColumnSpacing = element.ColumnSpacing;
        reconciler.BuildChildren(grid, element, element.Children);
        return grid;
    }

    protected override void Update(
        Reconciler reconciler,
        GridElement oldElement,
        GridElement newElement,
        Grid control)
    {
        // 行列定义只在"真的变了"时才重建：<see cref="GridDefinition"/> 是 record，
        // 但字段是数组——默认 Equals 走引用比较，每次重渲染都会判"变了" →
        // Clear + 重建全部 Row/ColumnDefinition → 整个 Grid 重新布局（可见的抖动）。
        if (!SameDefinition(oldElement.Definition, newElement.Definition))
        {
            ApplyDefinition(control, newElement.Definition);
        }

        PropWriter.Set(oldElement.RowSpacing, newElement.RowSpacing, value => control.RowSpacing = value);
        PropWriter.Set(oldElement.ColumnSpacing, newElement.ColumnSpacing, value => control.ColumnSpacing = value);
        reconciler.PatchPanelChildren(control, newElement, oldElement.Children, newElement.Children);
    }

    protected override IReadOnlyList<Element?>? ChildrenOf(GridElement element) => element.Children;

    protected override Panel? PanelOf(Grid control) => control;

    /// <summary>把子元素上的 <see cref="GridAttached"/> 落到 Grid.SetRow / SetColumn 上。</summary>
    public override void AttachChild(UIElement parent, UIElement child, Element childElement)
    {
        if (childElement.Modifiers?.Grid is not { } attached || child is not FrameworkElement element)
        {
            return;
        }

        Grid.SetRow(element, attached.Row);
        Grid.SetColumn(element, attached.Column);
        Grid.SetRowSpan(element, attached.RowSpan);
        Grid.SetColumnSpan(element, attached.ColumnSpan);
    }

    /// <summary>行列定义的<b>结构</b>比较（逐轨道比，不看数组引用）。</summary>
    private static bool SameDefinition(GridDefinition? a, GridDefinition? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return PropWriter.SequenceEqual(a.Columns, b.Columns) &&
               PropWriter.SequenceEqual(a.Rows, b.Rows);
    }

    private static void ApplyDefinition(Grid grid, GridDefinition definition)
    {
        if (definition is null)
        {
            return;
        }

        var columns = definition.Columns ?? Array.Empty<GridSize>();
        var rows = definition.Rows ?? Array.Empty<GridSize>();

        grid.ColumnDefinitions.Clear();
        foreach (var column in columns)
        {
            var gridLength = (GridLength)column;
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = gridLength,
                MinWidth = column.Min ?? 0,
                MaxWidth = column.Max ?? double.PositiveInfinity,
            });
        }

        grid.RowDefinitions.Clear();
        foreach (var row in rows)
        {
            var gridLength = (GridLength)row;
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = gridLength,
                MinHeight = row.Min ?? 0,
                MaxHeight = row.Max ?? double.PositiveInfinity,
            });
        }
    }
}

/// <summary>单子元素容器：背景 / 边框 / 圆角。</summary>
internal sealed class BorderHandler : ElementHandler<BorderElement, Border>
{
    protected override Border Mount(Reconciler reconciler, BorderElement element)
    {
        var border = new Border();
        ApplyProps(border, null, element);

        if (element.Child is not null)
        {
            border.Child = reconciler.Build(element.Child);
        }

        return border;
    }

    protected override void Update(
        Reconciler reconciler,
        BorderElement oldElement,
        BorderElement newElement,
        Border control)
    {
        ApplyProps(control, oldElement, newElement);
        reconciler.PatchSingleChild(control, oldElement.Child, newElement.Child);
    }

    protected override Element? SingleChildOf(BorderElement element) => element.Child;

    /// <summary>
    /// 挂载时 <paramref name="oldElement"/> 为 null（无条件全写），更新时逐字段 diff。
    /// </summary>
    private static void ApplyProps(
        Border border,
        BorderElement? oldElement,
        BorderElement newElement)
    {
        // Brush 是引用类型：内容一样但每次 new 都是新实例，无条件赋值会触发一次
        // 真实的依赖属性变化 → 背景重绘。所以走引用比较。
        if (oldElement is null)
        {
            // 首次挂载：无条件全写（对齐官方 OneWay 的 Mount 行为）。
            if (newElement.CornerRadius is { } radius)
            {
                border.CornerRadius = new CornerRadius(radius);
            }

            if (newElement.Background is not null)
            {
                border.Background = newElement.Background;
            }

            if (newElement.BorderBrush is not null)
            {
                border.BorderBrush = newElement.BorderBrush;
            }

            if (newElement.BorderThickness is { } thickness)
            {
                border.BorderThickness = thickness;
            }

            if (newElement.Padding is { } padding)
            {
                border.Padding = padding;
            }

            return;
        }

        PropWriter.Set(oldElement.CornerRadius, newElement.CornerRadius, value =>
        {
            if (value is { } radius)
            {
                border.CornerRadius = new CornerRadius(radius);
            }
        });

        PropWriter.SetRef(oldElement.Background, newElement.Background, value =>
        {
            if (value is not null)
            {
                border.Background = value;
            }
        });

        PropWriter.SetRef(oldElement.BorderBrush, newElement.BorderBrush, value =>
        {
            if (value is not null)
            {
                border.BorderBrush = value;
            }
        });

        PropWriter.Set(oldElement.BorderThickness, newElement.BorderThickness, value =>
        {
            if (value is { } thickness)
            {
                border.BorderThickness = thickness;
            }
        });

        PropWriter.Set(oldElement.Padding, newElement.Padding, value =>
        {
            if (value is { } padding)
            {
                border.Padding = padding;
            }
        });
    }
}
