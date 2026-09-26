using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using MuxControls = Microsoft.UI.Xaml.Controls;
using ToolkitControls = CommunityToolkit.WinUI.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 超链接按钮。Content 为元素时递归构建（模板里是 FontIcon + TextBlock 的组合）。
/// </summary>
internal sealed class HyperlinkButtonHandler : ElementHandler<HyperlinkButtonElement, HyperlinkButton>
{
    private static readonly Dictionary<HyperlinkButton, Action?> Callbacks = new();

    protected override HyperlinkButton Mount(Reconciler reconciler, HyperlinkButtonElement element)
    {
        var button = new HyperlinkButton();
        ApplyContent(reconciler, button, null, element);
        Rebind(button, element.OnClick);
        return button;
    }

    protected override void Update(
        Reconciler reconciler,
        HyperlinkButtonElement oldElement,
        HyperlinkButtonElement newElement,
        HyperlinkButton control)
    {
        ApplyContent(reconciler, control, oldElement, newElement);
        Rebind(control, newElement.OnClick);
    }

    protected override void Unmount(Reconciler reconciler, HyperlinkButton control) => Callbacks.Remove(control);

    private static void ApplyContent(
        Reconciler reconciler,
        HyperlinkButton control,
        HyperlinkButtonElement? oldElement,
        HyperlinkButtonElement newElement)
    {
        if (newElement.NavigateUri is { } uri &&
            !string.Equals(control.Tag as string, uri, StringComparison.Ordinal))
        {
            control.NavigateUri = string.IsNullOrWhiteSpace(uri) ? null : new Uri(uri);
            control.Tag = uri;
        }

        // Content 为元素时递归 diff；否则把 Label 当纯文本（模板两种用法都有）。
        if (newElement.Content is { } content)
        {
            reconciler.PatchSingleChild(control, oldElement?.Content, content);
            return;
        }

        var label = newElement.Label;
        if (!Equals(oldElement?.Label, label))
        {
            control.Content = label;
        }
    }

    private static void Rebind(HyperlinkButton control, Action? callback)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = null;
            control.Click += (s, _) =>
            {
                var link = (HyperlinkButton)s;
                if (Callbacks.TryGetValue(link, out var current))
                {
                    current?.Invoke();
                }
            };
        }

        Callbacks[control] = callback;
    }
}

/// <summary>字体图标。既是普通内容，也可作为卡片/菜单的 HeaderIcon。</summary>
internal sealed class FontIconHandler : ElementHandler<FontIconElement, FontIcon>
{
    protected override FontIcon Mount(Reconciler reconciler, FontIconElement element)
    {
        var icon = new FontIcon { Glyph = element.Glyph };

        if (element.FontFamily is { } family)
        {
            icon.FontFamily = new FontFamily(family);
        }

        if (element.FontSize is { } size)
        {
            icon.FontSize = size;
        }

        return icon;
    }

    protected override void Update(
        Reconciler reconciler,
        FontIconElement oldElement,
        FontIconElement newElement,
        FontIcon control)
    {
        PropWriter.Set(oldElement.Glyph, newElement.Glyph, value => control.Glyph = value);

        if (newElement.FontFamily is { } family && oldElement.FontFamily != family)
        {
            control.FontFamily = new FontFamily(family);
        }

        PropWriter.Set(oldElement.FontSize, newElement.FontSize, value =>
        {
            if (value is { } size)
            {
                control.FontSize = size;
            }
        });
    }
}

/// <summary>位图图标（关于页的应用图标）。</summary>
internal sealed class BitmapIconHandler : ElementHandler<BitmapIconElement, BitmapIcon>
{
    protected override BitmapIcon Mount(Reconciler reconciler, BitmapIconElement element)
    {
        var icon = new BitmapIcon { ShowAsMonochrome = element.ShowAsMonochrome };
        ApplySource(icon, element);
        ApplySize(element, element, icon);
        return icon;
    }

    protected override void Update(
        Reconciler reconciler,
        BitmapIconElement oldElement,
        BitmapIconElement newElement,
        BitmapIcon control)
    {
        PropWriter.Set(
            oldElement.ShowAsMonochrome,
            newElement.ShowAsMonochrome,
            value => control.ShowAsMonochrome = value);

        if (!string.Equals(oldElement.UriSource, newElement.UriSource, StringComparison.Ordinal))
        {
            ApplySource(control, newElement);
        }

        ApplySize(oldElement, newElement, control);
    }

    private static void ApplySize(BitmapIconElement oldElement, BitmapIconElement newElement, BitmapIcon icon)
    {
        PropWriter.Set(oldElement.Width, newElement.Width, value =>
        {
            if (value is { } width)
            {
                icon.Width = width;
            }
        });

        PropWriter.Set(oldElement.Height, newElement.Height, value =>
        {
            if (value is { } height)
            {
                icon.Height = height;
            }
        });
    }

    private static void ApplySource(BitmapIcon icon, BitmapIconElement element)
    {
        if (string.IsNullOrWhiteSpace(element.UriSource))
        {
            return;
        }

        try
        {
            var uri = element.UriSource.Contains("://", StringComparison.Ordinal)
                ? new Uri(element.UriSource)
                : new Uri("ms-appx:///" + element.UriSource.TrimStart('/'));

            icon.UriSource = uri;
        }
        catch (Exception)
        {
            // 与 Image 一致：非法 URI 静默忽略。
        }
    }
}

/// <summary>
/// 面包屑导航。
/// </summary>
/// <remarks>
/// <b>不使用 WinUI 2 的原生 BreadcrumbBar</b>——实测踩坑链如下，留档避免重犯：
/// <list type="number">
/// <item><c>List&lt;string&gt;</c> 直接赋 <c>ItemsSource</c>：赋值当场抛
/// “Argument 'source' is not a supported vector.”（WinRT 侧要的是
/// <c>IVector&lt;IInspectable&gt;</c>，<c>List&lt;string&gt;</c> 投影成的是
/// <c>IVector&lt;HSTRING&gt;</c>）。</item>
/// <item>换成 <c>ObservableCollection&lt;object&gt;</c> 后<b>赋值不再报错</b>，
/// 但 CsWinRT 给托管集合建的 CCW 过不了布局期的 <c>ItemsSourceView</c>：
/// 面包屑从 <c>Collapsed</c> 变 <c>Visible</c>、第一次真正参与 Measure 时才炸，
/// 抛的是没有托管堆栈的 COMException“未指定的错误”，并且进程 fast-fail
/// （退出码 <c>0xC000027B</c>），<c>Application.UnhandledException</c> 里
/// <c>e.Handled = true</c> 也拦不住。</item>
/// </list>
/// 结论：这条 ABI 路径在本机不可用，宁可不碰。这里自建横向
/// <see cref="StackPanel"/>（文本 + “›” 分隔符 + 可点击），视觉与默认
/// BreadcrumbBar 一致，且不会因为集合投影把整个 App 带走。
/// </remarks>
internal sealed class BreadcrumbBarHandler : ElementHandler<BreadcrumbBarElement, StackPanel>
{
    private static readonly Dictionary<StackPanel, Action<int>?> Callbacks = new();

    /// <summary>元素在栈里的顺序稳定，直接按索引回传（与 BreadcrumbBar.ItemClicked 一致）。</summary>
    protected override StackPanel Mount(Reconciler reconciler, BreadcrumbBarElement element)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Callbacks[panel] = element.OnItemClicked;
        Fill(panel, element);
        return panel;
    }

    protected override void Update(
        Reconciler reconciler,
        BreadcrumbBarElement oldElement,
        BreadcrumbBarElement newElement,
        StackPanel control)
    {
        Callbacks[control] = newElement.OnItemClicked;

        // 条目、样式、字号任一变化都要重排文本（字号/样式变了只改属性会漏掉新项）。
        if (ItemsChanged(oldElement.Items, newElement.Items) ||
            oldElement.ItemFontSize != newElement.ItemFontSize ||
            oldElement.ItemStyleKey != newElement.ItemStyleKey)
        {
            Fill(control, newElement);
        }
    }

    protected override void Unmount(Reconciler reconciler, StackPanel control) => Callbacks.Remove(control);

    private static bool ItemsChanged(IReadOnlyList<string>? old, IReadOnlyList<string>? next) =>
        (old?.Count ?? 0) != (next?.Count ?? 0) ||
        (old is not null && next is not null && !old.SequenceEqual(next));

    /// <summary>自建实现：横向文本 + “›” 分隔符，最后一项可点击。</summary>
    private static void Fill(StackPanel panel, BreadcrumbBarElement element)
    {
        panel.Children.Clear();

        // 模板里条目是 SubtitleTextBlockStyle + FontSize 28：样式给字形，字号给大小。
        var itemStyle = element.ItemStyleKey is { Length: > 0 } key ? StyleSheet.Resolve(key) : null;
        var itemFontSize = element.ItemFontSize;

        // 分隔符字号跟着条目缩放（条目 28 时约 17），否则 28px 文字配 12px 的“›”明显失衡。
        var separatorSize = itemFontSize is { } size ? Math.Max(8, size * 0.6) : 12;

        var subtle = ThemeResource.Brush("TextFillColorSecondaryBrush");
        var list = element.Items ?? Array.Empty<string>();

        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "\u203A", // ›
                    FontSize = separatorSize,
                    Margin = new Thickness(4, 0, 4, 0),
                    Foreground = subtle,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var index = i;
            var text = new TextBlock
            {
                Text = list[i],
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (itemStyle is not null)
            {
                text.Style = itemStyle;
            }

            if (itemFontSize is { } fontSize)
            {
                text.FontSize = fontSize;
            }

            text.Tapped += (_, _) =>
            {
                if (Callbacks.TryGetValue(panel, out var callback))
                {
                    callback?.Invoke(index);
                }
            };

            panel.Children.Add(text);
        }
    }
}

/// <summary>
/// WinUI 2 的 Expander：可折叠分组容器。
/// WinUI 2 版本没有 Items / HeaderIcon，图标与标题拼进 Header 内容里。
/// </summary>
internal sealed class ExpanderHandler : ElementHandler<ExpanderElement, MuxControls.Expander>
{
    protected override MuxControls.Expander Mount(Reconciler reconciler, ExpanderElement element)
    {
        var expander = new MuxControls.Expander
        {
            IsExpanded = element.IsExpanded,
        };

        ApplyHeader(reconciler, expander, element);

        if (element.Content is { } content)
        {
            expander.Content = reconciler.Build(content);
        }

        return expander;
    }

    protected override void Update(
        Reconciler reconciler,
        ExpanderElement oldElement,
        ExpanderElement newElement,
        MuxControls.Expander control)
    {
        // IsExpanded 是"用户可改 + state 也在写"的双向属性：只在声明值真的变了时才写，
        // 否则用户手动展开的卡片遇到任何重渲染都会被拽回 state 值（表现为"闪一下合上"）。
        // 官方对这类属性用 Controlled + counter-echo（订阅 Expanding/Collapsed 并抑制
        // 自己写值造成的回声）；这里是它的弱化版：足够解决"被拽回"，但不抑制回声。
        if (oldElement.IsExpanded != newElement.IsExpanded)
        {
            control.IsExpanded = newElement.IsExpanded;
        }

        if (!Equals(oldElement.Header, newElement.Header) ||
            !Equals(oldElement.HeaderIcon, newElement.HeaderIcon))
        {
            ApplyHeader(reconciler, control, newElement);
        }

        reconciler.PatchSingleChild(control, oldElement.Content, newElement.Content);
    }

    protected override Element? SingleChildOf(ExpanderElement element) => element.Content;

    private static void ApplyHeader(
        Reconciler reconciler,
        MuxControls.Expander control,
        ExpanderElement element)
    {
        if (element.HeaderIcon is null)
        {
            control.Header = element.Header;
            return;
        }

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
        };

        if (Reconciler.BuildIcon(reconciler, element.HeaderIcon) is { } icon)
        {
            panel.Children.Add(icon);
        }

        if (element.Header is { } header)
        {
            panel.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });
        }

        control.Header = panel;
    }
}

/// <summary>
/// CommunityToolkit 的 SettingsCard / SettingsExpander 公共部分：
/// 头 / 描述 / 头图标 / 内容 / 内容对齐。
/// </summary>
/// <remarks>
/// <b>图标（<c>HeaderIcon</c>）是内容槽，不是值。</b>
/// 官方 <c>WrapperGenerator.IsSupportedReference</c> 沿基类链找 <c>UIElement</c>，
/// 命中就 return false——即 <c>IconElement</c> 这类 UIElement 派生的属性<b>不会</b>被
/// 映射成"每次重建新实例再赋值"的值属性，而是走内容槽：能复用就 patch。
/// 之前我们把它当值处理（每轮 <c>BuildIcon</c> 造一个新的 FontIcon 换上去），
/// 于是每次重渲染卡片上的图标都被销毁重建 → 实测点一次开关重建 5 个图标 → 闪。
/// </remarks>
internal abstract class SettingsCardHandlerBase<TElement, TControl> : ElementHandler<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement
{
    /// <summary>
    /// 每个卡片上当前挂着的图标（内容槽的"已挂载实例"）。
    /// 静态字段按封闭泛型类型各一份，因此 SettingsCard / SettingsExpander 互不干扰。
    /// </summary>
    private static readonly Dictionary<TControl, (Element Element, IconElement Native)> IconSlots = new();

    protected abstract string? HeaderOf(TElement element);
    protected abstract string? DescriptionOf(TElement element);
    protected abstract Element? HeaderIconOf(TElement element);
    protected abstract Element? ContentOf(TElement element);
    protected abstract SettingsCardContentAlignment AlignmentOf(TElement element);

    protected void ApplyCommon(Reconciler reconciler, TControl control, TElement element)
    {
        // 模板里每张 Expander / SettingsCard 都显式写了 HorizontalAlignment="Stretch"。
        // 少了这一句，卡片只会按内容宽度收缩（几百 px），在 1200 的限宽容器里靠左堆着——
        // 观感上就是"最大宽度限制没生效"。所以这是卡片类元素的默认行为。
        control.HorizontalAlignment = HorizontalAlignment.Stretch;

        SetHeader(control, HeaderOf(element));
        SetDescription(control, DescriptionOf(element));

        if (HeaderIconOf(element) is { } icon)
        {
            ApplyIconSlot(reconciler, control, null, icon);
        }

        SetContentAlignment(control, AlignmentOf(element));
    }

    /// <summary>就地更新用的版本：<b>只写真的变了的字段</b>。</summary>
    /// <remarks>
    /// Header/Description 写同一个字符串不会触发依赖属性变化，但图标一旦被当成
    /// "值"每轮重建，就会真的一次属性变化 → 重绘。所以图标走内容槽（见类注释）。
    /// </remarks>
    protected void ApplyCommon(
        Reconciler reconciler,
        TControl control,
        TElement oldElement,
        TElement newElement)
    {
        var oldHeader = HeaderOf(oldElement);
        var newHeader = HeaderOf(newElement);
        if (!string.Equals(oldHeader, newHeader, StringComparison.Ordinal))
        {
            SetHeader(control, newHeader);
        }

        var oldDescription = DescriptionOf(oldElement);
        var newDescription = DescriptionOf(newElement);
        if (!string.Equals(oldDescription, newDescription, StringComparison.Ordinal))
        {
            SetDescription(control, newDescription);
        }

        ApplyIconSlot(reconciler, control, HeaderIconOf(oldElement), HeaderIconOf(newElement));

        var oldAlignment = AlignmentOf(oldElement);
        var newAlignment = AlignmentOf(newElement);
        if (oldAlignment != newAlignment)
        {
            SetContentAlignment(control, newAlignment);
        }
    }

    /// <summary>
    /// 图标内容槽：能 patch 就 patch，实在对不上才重建控件。
    /// </summary>
    private void ApplyIconSlot(
        Reconciler reconciler,
        TControl control,
        Element? oldIcon,
        Element? newIcon)
    {
        var mounted = IconSlots.TryGetValue(control, out var slot);

        if (newIcon is null)
        {
            if (mounted)
            {
                SetHeaderIcon(control, null);
                IconSlots.Remove(control);
            }

            return;
        }

        // 完全没变（同一个描述实例）→ 一个字节都不碰。
        if (mounted && Equals(slot.Element, newIcon))
        {
            return;
        }

        // 同类型描述 → 就地 patch：复用已挂载的 FontIcon/BitmapIcon，只改属性。
        // 这是"内容槽"与"值属性"的本质差别：值属性换实例，内容槽复用实例。
        if (mounted && Reconciler.CanPatch(slot.Element, newIcon))
        {
            reconciler.Patch(slot.Native, slot.Element, newIcon);
            IconSlots[control] = (newIcon, slot.Native);
            return;
        }

        if (Reconciler.BuildIcon(reconciler, newIcon) is not { } native)
        {
            // 元素不是 IconElement（宿主降级为无图标）。
            if (mounted)
            {
                SetHeaderIcon(control, null);
                IconSlots.Remove(control);
            }

            return;
        }

        SetHeaderIcon(control, native);
        IconSlots[control] = (newIcon, native);
    }

    protected abstract void SetHeader(TControl control, string? header);
    protected abstract void SetDescription(TControl control, string? description);
    protected abstract void SetHeaderIcon(TControl control, IconElement? icon);
    protected abstract void SetContentAlignment(TControl control, SettingsCardContentAlignment alignment);

    protected override void Unmount(Reconciler reconciler, TControl control) => IconSlots.Remove(control);

    protected static ToolkitControls.ContentAlignment ToToolkitAlignment(SettingsCardContentAlignment alignment) =>
        alignment switch
        {
            SettingsCardContentAlignment.Left => ToolkitControls.ContentAlignment.Left,
            SettingsCardContentAlignment.Vertical => ToolkitControls.ContentAlignment.Vertical,
            _ => ToolkitControls.ContentAlignment.Right,
        };
}

/// <summary>设置页的一行设置项（Toolkit SettingsCard）。</summary>
internal sealed class SettingsCardHandler : SettingsCardHandlerBase<SettingsCardElement, ToolkitControls.SettingsCard>
{
    private static readonly Dictionary<ToolkitControls.SettingsCard, Action?> Callbacks = new();

    protected override ToolkitControls.SettingsCard Mount(Reconciler reconciler, SettingsCardElement element)
    {
        var card = new ToolkitControls.SettingsCard { IsEnabled = element.IsEnabled };
        ApplyCommon(reconciler, card, element);

        if (ContentOf(element) is { } content)
        {
            card.Content = reconciler.Build(content);
        }

        card.IsClickEnabled = element.OnClick is not null;
        Rebind(card, element.OnClick);
        return card;
    }

    protected override void Update(
        Reconciler reconciler,
        SettingsCardElement oldElement,
        SettingsCardElement newElement,
        ToolkitControls.SettingsCard control)
    {
        // 只有"声明值真的变了"时才写，避免把控件自己的状态（含用户操作结果）拽回去。
        PropWriter.Set(oldElement.IsEnabled, newElement.IsEnabled, value => control.IsEnabled = value);
        ApplyCommon(reconciler, control, oldElement, newElement);

        reconciler.PatchSingleChild(control, oldElement.Content, newElement.Content);

        var clickEnabled = newElement.OnClick is not null;
        if (control.IsClickEnabled != clickEnabled)
        {
            control.IsClickEnabled = clickEnabled;
        }

        Rebind(control, newElement.OnClick);
    }

    protected override Element? SingleChildOf(SettingsCardElement element) => ContentOf(element);

    protected override string? HeaderOf(SettingsCardElement element) => element.Header;
    protected override string? DescriptionOf(SettingsCardElement element) => element.Description;
    protected override Element? HeaderIconOf(SettingsCardElement element) => element.HeaderIcon;
    protected override Element? ContentOf(SettingsCardElement element) => element.Content;
    protected override SettingsCardContentAlignment AlignmentOf(SettingsCardElement element) =>
        element.ContentAlignment;

    protected override void SetHeader(ToolkitControls.SettingsCard control, string? header) =>
        control.Header = header ?? string.Empty;

    protected override void SetDescription(ToolkitControls.SettingsCard control, string? description) =>
        control.Description = description ?? string.Empty;

    protected override void SetHeaderIcon(ToolkitControls.SettingsCard control, IconElement? icon) =>
        control.HeaderIcon = icon!;

    protected override void SetContentAlignment(
        ToolkitControls.SettingsCard control,
        SettingsCardContentAlignment alignment) =>
        control.ContentAlignment = ToToolkitAlignment(alignment);

    protected override void Unmount(Reconciler reconciler, ToolkitControls.SettingsCard control)
    {
        base.Unmount(reconciler, control);
        Callbacks.Remove(control);
    }

    private static void Rebind(ToolkitControls.SettingsCard control, Action? callback)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = null;
            control.Click += (s, _) =>
            {
                var card = (ToolkitControls.SettingsCard)s;
                if (Callbacks.TryGetValue(card, out var current))
                {
                    current?.Invoke();
                }
            };
        }

        Callbacks[control] = callback;
    }
}

/// <summary>可展开的设置卡片（Toolkit SettingsExpander）。</summary>
internal sealed class SettingsExpanderHandler
    : SettingsCardHandlerBase<SettingsExpanderElement, ToolkitControls.SettingsExpander>
{
    protected override ToolkitControls.SettingsExpander Mount(
        Reconciler reconciler,
        SettingsExpanderElement element)
    {
        var expander = new ToolkitControls.SettingsExpander { IsExpanded = element.IsExpanded };
        ApplyCommon(reconciler, expander, element);

        if (ContentOf(element) is { } content)
        {
            expander.Content = reconciler.Build(content);
        }

        ApplyItems(reconciler, expander, element.Items);
        return expander;
    }

    protected override void Update(
        Reconciler reconciler,
        SettingsExpanderElement oldElement,
        SettingsExpanderElement newElement,
        ToolkitControls.SettingsExpander control)
    {
        // 只在"声明值真的变了"时才写。无条件写会把用户手动展开/折叠的状态拽回 state 值：
        // 点上面任意开关触发重渲染 → 下面展开着的卡片被强行合上（用户看到的就是"闪一下"）。
        if (oldElement.IsExpanded != newElement.IsExpanded)
        {
            control.IsExpanded = newElement.IsExpanded;
        }

        ApplyCommon(reconciler, control, oldElement, newElement);

        reconciler.PatchSingleChild(control, oldElement.Content, newElement.Content);

        ApplyItems(reconciler, control, oldElement.Items, newElement.Items);
    }

    protected override void Unmount(Reconciler reconciler, ToolkitControls.SettingsExpander control) =>
        base.Unmount(reconciler, control);

    protected override Element? SingleChildOf(SettingsExpanderElement element) => ContentOf(element);

    protected override string? HeaderOf(SettingsExpanderElement element) => element.Header;
    protected override string? DescriptionOf(SettingsExpanderElement element) => element.Description;
    protected override Element? HeaderIconOf(SettingsExpanderElement element) => element.HeaderIcon;
    protected override Element? ContentOf(SettingsExpanderElement element) => element.Content;
    protected override SettingsCardContentAlignment AlignmentOf(SettingsExpanderElement element) =>
        element.ContentAlignment;

    protected override void SetHeader(ToolkitControls.SettingsExpander control, string? header) =>
        control.Header = header ?? string.Empty;

    protected override void SetDescription(ToolkitControls.SettingsExpander control, string? description) =>
        control.Description = description ?? string.Empty;

    protected override void SetHeaderIcon(ToolkitControls.SettingsExpander control, IconElement? icon) =>
        control.HeaderIcon = icon!;

    // SettingsExpander 没有 ContentAlignment（只有 SettingsCard 有），
    // 展开区里的卡片各自用自己的对齐方式控制。
    protected override void SetContentAlignment(
        ToolkitControls.SettingsExpander control,
        SettingsCardContentAlignment alignment)
    {
    }

    /// <summary>挂载时构建展开区卡片（无旧控件可复用）。</summary>
    private static void ApplyItems(
        Reconciler reconciler,
        ToolkitControls.SettingsExpander control,
        IReadOnlyList<Element?>? items)
    {
        control.Items.Clear();

        foreach (var item in NonNull(items))
        {
            control.Items.Add(reconciler.Build(item));
        }
    }

    /// <summary>
    /// 就地更新展开区卡片：<b>能 patch 就 patch，绝不整体重建</b>。
    /// </summary>
    /// <remarks>
    /// 之前用"新旧序列整体相等吗"来判断，但元素记录里含 <c>IReadOnlyList</c> /
    /// 委托字段，引用比较恒成立 → 每次重渲染都判定"变了" → <c>Clear</c> + 全量 Build，
    /// 展开区里的卡片连同内容一起销毁重建 → 闪烁 + 展开状态丢失。
    /// 实测（点一次开关）：3 张展开区卡片 + 里面的 RadioButtons / HyperlinkButton 全被重建。
    /// </remarks>
    private static void ApplyItems(
        Reconciler reconciler,
        ToolkitControls.SettingsExpander control,
        IReadOnlyList<Element?>? oldItems,
        IReadOnlyList<Element?>? newItems)
    {
        var oldList = NonNull(oldItems);
        var newList = NonNull(newItems);

        var canPatchInPlace = oldList.Count == newList.Count && control.Items.Count == oldList.Count;
        if (canPatchInPlace)
        {
            for (var i = 0; i < oldList.Count; i++)
            {
                if (!Reconciler.CanPatch(oldList[i], newList[i]) || control.Items[i] is not UIElement)
                {
                    canPatchInPlace = false;
                    break;
                }
            }
        }

        if (canPatchInPlace)
        {
            for (var i = 0; i < oldList.Count; i++)
            {
                reconciler.Patch((UIElement)control.Items[i], oldList[i], newList[i]);
            }

            return;
        }

        control.Items.Clear();

        foreach (var item in newList)
        {
            control.Items.Add(reconciler.Build(item));
        }
    }

    private static List<Element> NonNull(IReadOnlyList<Element?>? items)
    {
        var result = new List<Element>();

        foreach (var item in items ?? Array.Empty<Element?>())
        {
            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }
}

/// <summary>内容页容器（XAML 的 Frame）。页面切换由组件状态驱动。</summary>
internal sealed class FrameHandler : ElementHandler<FrameElement, Frame>
{
    protected override Frame Mount(Reconciler reconciler, FrameElement element)
    {
        var frame = new Frame();

        // XAML 里 <c>&lt;Frame.ContentTransitions&gt;</c> 就是这一层；模板没写，
        // 但显式给一份可以确保 NavigationThemeTransition 的存在（Frame 默认也用它）。
        frame.ContentTransitions = CreateTransitions(element.Transition);

        if (element.Content is { } content)
        {
            var page = reconciler.Build(content);
            Navigate(frame, element.Transition, page);
        }

        return frame;
    }

    protected override void Update(
        Reconciler reconciler,
        FrameElement oldElement,
        FrameElement newElement,
        Frame control)
    {
        var oldContent = oldElement.Content;
        var newContent = newElement.Content;

        // 过渡集合只在类型变化时重建：每轮重渲染都 new 一个会被当成"变了"，
        // 导致过渡被反复初始化。
        if (oldElement.Transition != newElement.Transition)
        {
            control.ContentTransitions = CreateTransitions(newElement.Transition);
        }

        // 页面元素树是注入到"这次导航创建的 Page"里的，不是 Frame.Content 本身。
        var host = control.Content as Page;
        var current = host?.Content as UIElement;

        if (newContent is null)
        {
            if (current is not null)
            {
                reconciler.UnmountNative(current, oldContent ?? EmptyElement.Instance);
                host!.Content = null;
            }

            return;
        }

        // 同一元素类型：就地 patch，不重建、不导航（内容没换，本就不该播过渡）。
        if (current is not null &&
            oldContent is not null &&
            Reconciler.CanPatch(oldContent, newContent))
        {
            reconciler.Patch(current, oldContent, newContent);
            return;
        }

        if (current is not null)
        {
            reconciler.UnmountNative(current, oldContent ?? EmptyElement.Instance);
        }

        // 页面换了：走官方 Frame.Navigate——导航过渡只由 Navigate 驱动，
        // 直接改 Content 是不会播放的。
        Navigate(control, newElement.Transition, reconciler.Build(newContent));
    }

    protected override Element? SingleChildOf(FrameElement element) => element.Content;

    /// <summary>
    /// 页面切换走官方 <c>Frame.Navigate</c>——与模板 <c>ContentFrame.Navigate(typeof(Page))</c>
    /// 同一条路径。
    /// </summary>
    /// <remarks>
    /// 过渡只由 <c>Navigate</c> 驱动：直接给 <c>Frame.Content</c> 赋值时
    /// <c>NavigationThemeTransition</c> 不会播放（它没有导航上下文），这也是
    /// "切页没动画"的根因。导航基类型用 <c>Page</c> 本身——它是 WinRT 投影类型，
    /// 不依赖应用自己的 XAML 元数据，纯代码应用也能解析（自建的 Page 子类则不行）。
    /// 建好的页面元素树注入这次导航创建的 <c>Page.Content</c>。
    /// </remarks>
    private static void Navigate(Frame frame, PageTransition transition, UIElement? content)
    {
        frame.Navigate(typeof(Page), null, CreateTransitionInfo(transition));

        if (frame.Content is Page page)
        {
            page.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            page.VerticalContentAlignment = VerticalAlignment.Stretch;
            page.Content = content;
        }
    }

    /// <summary>构造官方过渡集合（<c>Frame.ContentTransitions</c>）。</summary>
    /// <remarks>
    /// 用 <see cref="NavigationThemeTransition"/>：它就是 <c>Frame.Navigate</c>
    /// 默认使用的那套导航过渡，<see cref="PageTransition"/> 的三个值分别对应它的
    /// 三种 <c>NavigationTransitionInfo</c>，与模板"切页有动画"的观感一致。
    /// </remarks>
    private static TransitionCollection? CreateTransitions(PageTransition transition) =>
        transition switch
        {
            PageTransition.None => null,
            _ => new TransitionCollection
            {
                new NavigationThemeTransition
                {
                    DefaultNavigationTransitionInfo = CreateTransitionInfo(transition),
                },
            },
        };

    private static NavigationTransitionInfo CreateTransitionInfo(PageTransition transition) =>
        transition switch
        {
            // 官方的"不要动画"也是一类 NavigationTransitionInfo，而不是不传。
            PageTransition.None => new SuppressNavigationTransitionInfo(),
            PageTransition.DrillIn => new DrillInNavigationTransitionInfo(),
            PageTransition.SlideFromRight => new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight,
            },
            _ => new EntranceNavigationTransitionInfo(),
        };
}
