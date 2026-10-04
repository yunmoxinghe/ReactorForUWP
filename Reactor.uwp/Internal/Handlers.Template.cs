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
/// 面包屑：映射 WinUI 2 原生 <c>BreadcrumbBar</c>——与参考模板 <c>MainPage.xaml</c>
/// 里的 <c>&lt;controls:BreadcrumbBar&gt;</c> 是同一个控件。
/// </summary>
/// <remarks>
/// 分隔符（chevron）、条目按钮、点击反馈、键盘导航、自动化对等全部由官方模板提供，
/// 这里只负责喂 <c>ItemsSource</c>、定条目外观、接 <c>ItemClicked</c>。
/// <para>
/// <b>喂集合走"借真原生向量"</b>：直接给 <c>ItemsSource</c> 赋托管集合，在 AOT 下
/// 过不了布局期的 <c>ItemsSourceView</c>（CsWinRT 建的 CCW 被拒 → 不带托管堆栈的
/// COMException → fast-fail 0xC000027B，<c>Application.UnhandledException</c>
/// 都拦不住）；<c>List&lt;string&gt;</c> 更是直接被拒（"not a supported vector"——
/// 它被投影成 <c>IVector&lt;HSTRING&gt;</c>）。而 <c>ItemsControl.Items</c> 是
/// WinRT 自己的 <c>IObservableVector&lt;IInspectable&gt;</c>，<b>实测可用</b>：
/// 依据 <c>UwpApp/__BreadcrumbProbe.cs</c> 的 mode=3——赋值 OK、布局 OK、
/// 生成 4 个 <c>BreadcrumbBarItem</c>。载体 <c>ItemsControl</c> 不进可视树，
/// 只当向量使。
/// </para>
/// <para>
/// <b>喂进向量的必须是"数据"，不能是 <c>UIElement</c>（实测必崩，别再试）。</b>
/// 之前为了让每条带上 <c>TitleTextBlockStyle</c>，把每项换成了 <c>TextBlock</c>：
/// 第一次布局就 <c>COMException 0x800F1000 —— "Element is already the child of
/// another element."</c>（probe mode=5）。原因是 <c>ItemsControl.Items.Add</c>
/// 已经给那个 TextBlock 置了父，<c>ItemsRepeater</c> 再把它挂进自己的 panel 就是
/// 第二个父——UWP 里 <c>UIElement</c> 天生要占树上一个位置，它当不了数据。
/// </para>
/// <para>
/// <b>条目外观一律走官方的 <c>ItemTemplate</c></b>：<c>BreadcrumbBar</c> 没有
/// <c>ItemStyle</c>（winmd 里查过，只有 <c>ItemTemplate</c> /
/// <c>ItemTemplateSelector</c>），而 <c>BreadcrumbBarItem</c> 的默认样式硬设了
/// <c>FontSize = {ThemeResource BreadcrumbBarItemThemeFontSize}</c>，
/// 继承链到条目就断——在 bar 上设多大字号都不生效。
/// 纯代码没有 <c>FrameworkElementFactory</c>，唯一的正路是
/// <c>XamlReader.Load</c> 一段 DataTemplate（<b>AOT 下实测可用</b>，见 probe
/// mode=7：条目 TextBlock 的 <c>FontSize</c>=28、<c>Style</c>=有）。
/// Load 出来的独立树没有父链，但 <c>{StaticResource}</c> 仍能落到 Application 级资源，
/// 所以 <c>Style="{StaticResource TitleTextBlockStyle}"</c> 照 XAML 原样写即可。
/// </para>
/// </remarks>
internal sealed class BreadcrumbBarHandler
    : ElementHandler<BreadcrumbBarElement, MuxControls.BreadcrumbBar>
{
    private static readonly Dictionary<MuxControls.BreadcrumbBar, Action<int>?> Callbacks = new();

    /// <summary>每个控件的向量载体：只为拿到一个真 WinRT 向量喂 ItemsSource。</summary>
    private static readonly Dictionary<MuxControls.BreadcrumbBar, ItemsControl> Carriers = new();

    /// <summary>
    /// 查不到资源的样式键。存下来是为了别每帧重复 Load 同一个必然失败的字符串
    /// （那会刷满日志）——找不到一次就够了。
    /// </summary>
    private static readonly HashSet<string> UnresolvedStyles = new();

    /// <summary>
    /// <c>DataTemplate</c> 缓存：键是"样式名 + 字号"。
    /// <c>XamlReader.Load</c> 是运行时整段解析 XAML，每次渲染都跑一遍太贵；
    /// 同一个模板实例喂给多个 <c>ItemsRepeater</c> 是允许的，按内容缓存即可。
    /// </summary>
    private static readonly Dictionary<string, Windows.UI.Xaml.DataTemplate> Templates = new();

    protected override MuxControls.BreadcrumbBar Mount(
        Reconciler reconciler, BreadcrumbBarElement element)
    {
        var bar = new MuxControls.BreadcrumbBar();

        var carrier = new ItemsControl();
        Carriers[bar] = carrier;
        SyncItems(carrier.Items, element);

        bar.ItemsSource = carrier.Items;

        if (ResolveItemTemplate(element) is { } template)
        {
            bar.ItemTemplate = template;
        }

        Callbacks[bar] = element.OnItemClicked;
        bar.ItemClicked += OnItemClicked;

        return bar;
    }

    protected override void Update(
        Reconciler reconciler,
        BreadcrumbBarElement oldElement,
        BreadcrumbBarElement newElement,
        MuxControls.BreadcrumbBar control)
    {
        Callbacks[control] = newElement.OnItemClicked;

        if (Carriers.TryGetValue(control, out var carrier))
        {
            SyncItems(carrier.Items, newElement);
        }

        // 模板是引用比较（变了才重写）：写一次 ItemTemplate 会让 ItemsRepeater
        // 把已有条目全部拆了重建，本来没事的两个 int 比较挡掉这些重建。
        var template = ResolveItemTemplate(newElement);
        if (!ReferenceEquals(control.ItemTemplate, template))
        {
            control.ItemTemplate = template!;
        }
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.BreadcrumbBar control)
    {
        control.ItemClicked -= OnItemClicked;
        Callbacks.Remove(control);
        Carriers.Remove(control);
    }

    private static void OnItemClicked(
        MuxControls.BreadcrumbBar sender,
        MuxControls.BreadcrumbBarItemClickedEventArgs args)
    {
        if (Callbacks.TryGetValue(sender, out var callback))
        {
            callback?.Invoke(args.Index);
        }
    }

    /// <summary>
    /// 同步向量内容。内容没变就<b>一个字节都不动</b>——<c>Clear()</c> 会让内部的
    /// ItemsRepeater 把条目全拆了重建，每轮重渲染清一次等于永远在重建。
    /// </summary>
    /// <remarks>
    /// 喂进去的是<b>字符串</b>（数据）。别改回 <c>UIElement</c>：那样一来
    /// <c>Items.Add</c> 会先给它置一个父，ItemsRepeater 再挂就是第二个父，
    /// 第一次布局即 0x800F1000（见类注释，probe mode=5 实证）。
    /// </remarks>
    private static void SyncItems(ItemCollection target, BreadcrumbBarElement element)
    {
        var list = element.Items ?? Array.Empty<string>();

        if (target.Count == list.Count)
        {
            var same = true;
            for (var i = 0; i < list.Count; i++)
            {
                if (target[i] is not string text ||
                    !string.Equals(text, list[i], StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        target.Clear();

        foreach (var text in list)
        {
            target.Add(text);
        }
    }

    /// <summary>
    /// 解析条目模板——等价于模板 <c>MainPage.xaml</c> 里的
    /// <c>&lt;BreadcrumbBar.ItemTemplate&gt;&lt;TextBlock Text="{x:Bind}"
    /// VerticalAlignment="Center" Style="{StaticResource TitleTextBlockStyle}"/&gt;</c>。
    /// </summary>
    /// <returns>
    /// 没有定样式也没定字号时返回 null（用控件自己的默认外观，官方 <c>ItemTemplate</c>
    /// 也是 null），其余情况返回 "</c>DataTemplate</c>"。
    /// </returns>
    private static Windows.UI.Xaml.DataTemplate? ResolveItemTemplate(BreadcrumbBarElement element)
    {
        var styleKey = element.ItemStyleKey is { Length: > 0 } key ? key : null;

        // 样式、字号一个都没给 → 用控件自带的外观（官方 ItemTemplate 此时也是 null）。
        if (styleKey is null && element.ItemFontSize is not { })
        {
            return null;
        }

        // 先看这一档有没有解出来过（同一个 key + 字号只 Load 一次）。
        var cacheKey = styleKey ?? string.Empty;
        if (element.ItemFontSize is { } size)
        {
            cacheKey += "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (Templates.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // 保险：样式真的查得到才写进 XAML。Load 出来的独立树没有父链，
        // key 写错的话 layout 期会因为解析不到资源而崩（且不带托管堆栈）。
        if (styleKey is not null &&
            (UnresolvedStyles.Contains(styleKey) ||
             (StyleSheet.Resolve(styleKey) is null && ThemeResource.Get<Style>(styleKey) is null)))
        {
            if (UnresolvedStyles.Add(styleKey))
            {
                Reactor.Uwp.Hosting.ReactorApplication.Trace(
                    $"[reactor] 面包屑: 资源里没有样式 {styleKey}，跳过（用默认外观）");
            }

            // 连样式都没有又没给字号的话，就彻底用控件自带的外观。
            if (element.ItemFontSize is not { })
            {
                return null;
            }

            styleKey = null;
        }

        try
        {
            var template = (Windows.UI.Xaml.DataTemplate)Windows.UI.Xaml.Markup.XamlReader.Load(
                BuildItemTemplateXaml(styleKey, element.ItemFontSize));

            Templates[cacheKey] = template;
            return template;
        }
        catch (Exception ex)
        {
            // 模板挂不上不能把整棵界面树拖死：退化为默认外观（字号是官方默认的 14px）。
            Reactor.Uwp.Hosting.ReactorApplication.Trace(
                $"[reactor] 面包屑 ItemTemplate 构造失败: [{ex.GetType().Name}] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 拼 <c>DataTemplate</c> 的 XAML。UWP 没有 <c>FrameworkElementFactory</c>，
    /// 这是纯代码里唯一的等价写法（AOT 下实测可用，见类注释）。
    /// </summary>
    /// <remarks>
    /// 文本构造本身在 <see cref="BreadcrumbTemplate"/> 里（纯字符串、不碰 WinRT），
    /// 这样控制台测试能直接断言这段 XAML，不用开 App。
    /// </remarks>
    private static string BuildItemTemplateXaml(string? styleKey, double? fontSize) =>
        BreadcrumbTemplate.BuildXaml(styleKey, fontSize);
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

        // 显式写死为 0（也就是 XAML 的默认值）：GoBack 走的是"重建页面"路径，
        // 不是"恢复缓存的那个实例"。不写死的话一旦有人改了 CacheSize，
        // GoBack 会把上一轮的 Page 原样端回来——那棵树我们已经卸载过了，
        // 端回来就是个空壳。
        frame.CacheSize = 0;

        // XAML 里 <c>&lt;Frame.ContentTransitions&gt;</c> 就是这一层；模板没写，
        // 但显式给一份可以确保 NavigationThemeTransition 的存在（Frame 默认也用它）。
        frame.ContentTransitions = CreateTransitions(element.Transition);

        if (element.Content is { } content)
        {
            var page = reconciler.Build(content);
            Navigate(frame, element.Transition, page, element.StackDepth >= 0);
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

            // 旧页面马上要被推进 Frame 的 BackStack：<b>必须</b>先摘掉它的 Content。
            // 卸载（UnmountNative）是递归的，但它只解绑记账、不动父子关系——
            // 不清 Content 的话整棵原生控件树会被那张弃用的 Page 一直吊着，永不回收。
            host!.Content = null;
        }

        var isBack = IsBackNavigation(oldElement, newElement);

        // 返回走官方 Frame.GoBack()：和模板 ContentFrame.GoBack() 同一条路径，
        // 反向过渡、返回音效、BackStack 出栈全都由 XAML 自己驱动，不用我们模拟。
        if (isBack && control.CanGoBack)
        {
            GoBack(control, reconciler.Build(newContent));
            return;
        }

        // 前进（或"说是返回但 Frame 没有可回退的栈"——两套栈不同步时的兜底）：
        // 走官方 Frame.Navigate，导航过渡只由 Navigate 驱动，直接改 Content 不播。
        Navigate(
            control,
            isBack ? PageTransition.SlideFromLeft : newElement.Transition,
            reconciler.Build(newContent),
            preserveBackStack: newElement.StackDepth >= 0,
            fallbackBackSound: isBack);
    }

    /// <summary>
    /// 返回：走官方 <c>Frame.GoBack()</c>。
    /// </summary>
    /// <remarks>
    /// 这才是与 XAML 模板一致的做法——<c>ContentFrame.GoBack()</c> 与
    /// <c>ContentFrame.Navigate(...)</c> 是两条不同的官方路径：返回会播反向过渡，
    /// 并且（按官方设计）播 <c>ElementSoundKind.GoBack</c>。之前一律用
    /// <c>Navigate</c> 模拟，等于把"返回"做成"进入下一页"，观感和声音都对不上。
    /// <para>
    /// <b>为什么可以用真 GoBack</b>：<c>Frame.CacheSize</c> 默认是 0，官方的
    /// <c>GoBack()</c> 本来就是<b>重建</b>页面，不是恢复缓存实例。所以不需要把上一页
    /// 的原生控件树留着——重建出来的新 Page 直接注入当前元素树即可。
    /// </para>
    /// </remarks>
    private static void GoBack(Frame frame, UIElement? content)
    {
        frame.GoBack();

        Reactor.Uwp.Hosting.ReactorApplication.Trace(
            $"[reactor] Frame.GoBack: 剩余回退栈 {frame.BackStackDepth} 条");

        if (frame.Content is Page page)
        {
            ApplyPageHost(page, content);
        }
    }

    /// <summary>
    /// 这次切页是不是"返回"：页面栈深度比上一次小就是返回。
    /// </summary>
    /// <remarks>
    /// 两边深度都传了（<c>&gt;= 0</c>）才判断；任一侧是默认值 <c>-1</c> 说明上层
    /// 没参与方向标记，一律按前进处理（行为与改动前一致）。
    /// </remarks>
    private static bool IsBackNavigation(FrameElement oldElement, FrameElement newElement) =>
        oldElement.StackDepth >= 0 &&
        newElement.StackDepth >= 0 &&
        newElement.StackDepth < oldElement.StackDepth;

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
    private static void Navigate(
        Frame frame,
        PageTransition transition,
        UIElement? content,
        bool preserveBackStack,
        bool fallbackBackSound = false)
    {
        frame.Navigate(typeof(Page), null, CreateTransitionInfo(transition));

        // preserveBackStack=false（上层没传 StackDepth）：返回不由 Frame 管，
        // 每次 Navigate 都会往 BackStack 压一条而我们从不 GoBack——切页 N 次就攒
        // N 张 Page，每张都吊着一棵原生控件树，纯泄漏。这时清掉。
        // preserveBackStack=true：BackStack <b>就是</b>页面栈，返回靠 GoBack，
        // 清了就退不回去了。
        if (!preserveBackStack && frame.BackStackDepth > 0)
        {
            Reactor.Uwp.Hosting.ReactorApplication.Trace(
                $"[reactor] Frame.Navigate: 清理 {frame.BackStackDepth} 条回退栈");
            frame.BackStack.Clear();
        }

        frame.ForwardStack.Clear();

        if (frame.Content is Page page)
        {
            ApplyPageHost(page, content);
        }

        // 兜底：说是返回但 Frame 没栈可退（两套栈没同步上），退化成了 Navigate——
        // 这条路径上官方不会播返回音，手动补一次。
        if (fallbackBackSound)
        {
            PlayGoBackSound();
        }
    }

    /// <summary>把页面元素树挂进这次导航/回退产出的 <c>Page</c>。</summary>
    private static void ApplyPageHost(Page page, UIElement? content)
    {
        page.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        page.VerticalContentAlignment = VerticalAlignment.Stretch;
        page.Content = content;
    }

    /// <summary>
    /// 返回导航的提示音（<b>兜底</b>：正常路径由 <c>Frame.GoBack()</c> 自己播）。
    /// </summary>
    /// <remarks>
    /// 官方设计文档「返回导航（Back Navigation）」一节：从当前页面导航到应用内前一个
    /// 页面时，应调用 <c>ElementSoundPlayer.Play(ElementSoundKind.GoBack)</c>。
    /// 系统里确实有这个专属音效资源（<c>Windows.UI.Xaml.dll</c> 里的
    /// <c>GoBack_48000Hz</c>），音色与 <c>Invoke</c> 不同——XAML 版点返回听到的
    /// "不一样的音效"就是它。
    /// <para>
    /// 正常路径是 <see cref="GoBack"/> 里的 <c>Frame.GoBack()</c>，音效由官方路径
    /// 驱动。只有"说是返回但 Frame 没栈可退、退化成 Navigate"时才调这里补一次。
    /// </para>
    /// <para>
    /// <b>坑</b>：<c>ElementSoundPlayer</c> 在 <c>Windows.UI.Xaml</c> 命名空间，
    /// 不在 <c>Windows.UI.Xaml.Controls</c>——写成后者报 CS0234，报错信息容易被
    /// 误读成"这个投影程序集里没有该类型"。
    /// </para>
    /// <para>
    /// 不用自己判断开关：<c>Play</c> 内部会看 <c>ElementSoundPlayer.State</c>，
    /// 不是 <c>On</c> 时它自己就不发声。
    /// </para>
    /// </remarks>
    private static void PlayGoBackSound()
    {
        try
        {
            Windows.UI.Xaml.ElementSoundPlayer.Play(Windows.UI.Xaml.ElementSoundKind.GoBack);

            Reactor.Uwp.Hosting.ReactorApplication.Trace(
                $"[reactor] 返回导航: 播 GoBack 音效（State={Windows.UI.Xaml.ElementSoundPlayer.State}）");
        }
        catch (Exception ex)
        {
            // 音效不是关键路径：播不出来也不能把导航拖崩。
            Reactor.Uwp.Hosting.ReactorApplication.Trace(
                $"[reactor] 返回音效播放失败: [{ex.GetType().Name}] {ex.Message}");
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
            PageTransition.SlideFromLeft => new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromLeft,
            },
            _ => new EntranceNavigationTransitionInfo(),
        };
}
