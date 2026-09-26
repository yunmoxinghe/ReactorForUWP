using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Reactor.Uwp.Internal;

/// <summary>TextBlock。注意参数名对齐官方：Content 而不是 Text。</summary>
internal sealed class TextBlockHandler : ElementHandler<TextBlockElement, TextBlock>
{
    protected override TextBlock Mount(Reconciler reconciler, TextBlockElement element) =>
        new() { Text = element.Content };

    protected override void Update(
        Reconciler reconciler,
        TextBlockElement oldElement,
        TextBlockElement newElement,
        TextBlock control) =>
        PropWriter.Set(oldElement.Content, newElement.Content, value => control.Text = value);
}

internal sealed class ButtonHandler : ElementHandler<ButtonElement, Button>
{
    protected override Button Mount(Reconciler reconciler, ButtonElement element)
    {
        var button = new Button { Content = element.Label };
        reconciler.RebindButtonClick(button, element.OnClick);
        return button;
    }

    protected override void Update(
        Reconciler reconciler,
        ButtonElement oldElement,
        ButtonElement newElement,
        Button control)
    {
        PropWriter.Set(oldElement.Label, newElement.Label, value => control.Content = value);
        reconciler.RebindButtonClick(control, newElement.OnClick);
    }

    protected override void Unmount(Reconciler reconciler, Button control) =>
        reconciler.RebindButtonClick(control, null);
}

internal sealed class TextBoxHandler : ElementHandler<TextBoxElement, TextBox>
{
    /// <summary>
    /// <c>Text</c> 是受控属性：用户输入 → 回调 → setState → 重渲染再写回控件。
    /// 没有回声抑制就会变成"写 → 事件 → 回调 → 再写"的回环。
    /// </summary>
    private static readonly EchoGuard TextEcho = new();

    protected override TextBox Mount(Reconciler reconciler, TextBoxElement element)
    {
        var native = new TextBox
        {
            PlaceholderText = element.PlaceholderText ?? string.Empty,
            Header = element.Header,
        };

        if (element.Value.HasValue)
        {
            native.Text = element.Value.Value ?? string.Empty;
        }

        reconciler.RebindTextChanged(native, element.OnChanged);
        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        TextBoxElement oldElement,
        TextBoxElement newElement,
        TextBox control)
    {
        PropWriter.Set(
            oldElement.PlaceholderText,
            newElement.PlaceholderText,
            value => control.PlaceholderText = value ?? string.Empty);
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);

        if (newElement.Value.HasValue)
        {
            var text = newElement.Value.Value ?? string.Empty;
            if (!string.Equals(control.Text, text, StringComparison.Ordinal))
            {
                TextEcho.Expect(control, text);
                control.Text = text;
            }
        }

        reconciler.RebindTextChanged(
            control,
            newElement.OnChanged is null
                ? null
                : value =>
                {
                    if (TextEcho.Consume(control, value))
                    {
                        return;
                    }

                    newElement.OnChanged(value);
                });
    }

    protected override void Unmount(Reconciler reconciler, TextBox control)
    {
        TextEcho.Forget(control);
        reconciler.RebindTextChanged(control, null);
    }
}

internal sealed class CheckBoxHandler : ElementHandler<CheckBoxElement, CheckBox>
{
    private static readonly EchoGuard CheckEcho = new();

    protected override CheckBox Mount(Reconciler reconciler, CheckBoxElement element)
    {
        var native = new CheckBox { Content = element.Label };

        if (element.IsChecked.HasValue)
        {
            native.IsChecked = element.IsChecked.Value;
        }

        reconciler.RebindCheckBox(native, element.OnIsCheckedChanged);
        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        CheckBoxElement oldElement,
        CheckBoxElement newElement,
        CheckBox control)
    {
        PropWriter.Set(oldElement.Label, newElement.Label, value => control.Content = value);

        if (newElement.IsChecked.HasValue && control.IsChecked != newElement.IsChecked.Value)
        {
            CheckEcho.Expect(control, newElement.IsChecked.Value);
            control.IsChecked = newElement.IsChecked.Value;
        }

        reconciler.RebindCheckBox(
            control,
            newElement.OnIsCheckedChanged is null
                ? null
                : value =>
                {
                    if (CheckEcho.Consume(control, value))
                    {
                        return;
                    }

                    newElement.OnIsCheckedChanged(value);
                });
    }

    protected override void Unmount(Reconciler reconciler, CheckBox control)
    {
        CheckEcho.Forget(control);
        reconciler.RebindCheckBox(control, null);
    }
}

internal sealed class SliderHandler : ElementHandler<SliderElement, Slider>
{
    /// <summary><c>Value</c>：拖动 → 回调 → setState → 重渲染写回，需要回声抑制。</summary>
    private static readonly EchoGuard ValueEcho = new();

    protected override Slider Mount(Reconciler reconciler, SliderElement element)
    {
        var native = new Slider
        {
            Minimum = element.Min,
            Maximum = element.Max,
        };

        if (element.Value.HasValue)
        {
            native.Value = element.Value.Value;
        }

        reconciler.RebindSlider(native, element.OnValueChanged);
        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        SliderElement oldElement,
        SliderElement newElement,
        Slider control)
    {
        // Value 会被 Minimum/Maximum 夹取，"声明值"和"控件值"本就可能不同，
        // 所以拿控件当前值比（SetLive 语义），而不是拿旧元素比。
        PropWriter.Set(oldElement.Min, newElement.Min, value => control.Minimum = value);
        PropWriter.Set(oldElement.Max, newElement.Max, value => control.Maximum = value);

        if (newElement.Value.HasValue && control.Value != newElement.Value.Value)
        {
            ValueEcho.Expect(control, newElement.Value.Value);
            control.Value = newElement.Value.Value;
        }

        reconciler.RebindSlider(
            control,
            newElement.OnValueChanged is null
                ? null
                : value =>
                {
                    if (ValueEcho.Consume(control, value))
                    {
                        return;
                    }

                    newElement.OnValueChanged(value);
                });
    }

    protected override void Unmount(Reconciler reconciler, Slider control)
    {
        ValueEcho.Forget(control);
        reconciler.RebindSlider(control, null);
    }
}

/// <summary>WinUI 2 的 InfoBar：验证 XamlControlsResources 纯代码加载链路。</summary>
internal sealed class InfoBarHandler : ElementHandler<InfoBarElement, MuxControls.InfoBar>
{
    protected override MuxControls.InfoBar Mount(Reconciler reconciler, InfoBarElement element) =>
        new()
        {
            Message = element.Message,
            Severity = element.Severity,
            IsOpen = true,
        };

    protected override void Update(
        Reconciler reconciler,
        InfoBarElement oldElement,
        InfoBarElement newElement,
        MuxControls.InfoBar control)
    {
        PropWriter.Set(oldElement.Message, newElement.Message, value => control.Message = value);
        PropWriter.Set(oldElement.Severity, newElement.Severity, value => control.Severity = value);

        // IsOpen 是「种子值」属性（官方 Initial / InitialOnly 语义）：只在 mount 写一次，
        // 之后归控件自己——用户按了关闭按钮就不该被下一轮重渲染重新打开。
    }
}

/// <summary>
/// 空占位。官方 EmptyElement 对应 null（不产生控件），但 UWP 侧 Build 必须返回
/// UIElement，因此退化为一个零尺寸 Grid。
/// </summary>
internal sealed class EmptyHandler : ElementHandler<EmptyElement, Grid>
{
    protected override Grid Mount(Reconciler reconciler, EmptyElement element) => new();
}

internal sealed class ScrollViewerHandler : ElementHandler<ScrollViewerElement, ScrollViewer>
{
    protected override ScrollViewer Mount(Reconciler reconciler, ScrollViewerElement element)
    {
        var native = new ScrollViewer();
        Apply(native, null, element);

        if (element.Child is not null)
        {
            native.Content = reconciler.Build(element.Child);
        }

        return native;
    }

    protected override void Update(
        Reconciler reconciler,
        ScrollViewerElement oldElement,
        ScrollViewerElement newElement,
        ScrollViewer control)
    {
        Apply(control, oldElement, newElement);
        reconciler.PatchSingleChild(control, oldElement.Child, newElement.Child);
    }

    /// <summary>
    /// 挂载时 <paramref name="oldElement"/> 传 null：首次无条件全写；更新时逐项 diff。
    /// </summary>
    private static void Apply(
        ScrollViewer control,
        ScrollViewerElement? oldElement,
        ScrollViewerElement newElement)
    {
        if (oldElement is null)
        {
            control.HorizontalScrollBarVisibility = newElement.HorizontalScrollBar;
            control.VerticalScrollBarVisibility = newElement.VerticalScrollBar;
            control.HorizontalScrollMode = newElement.HorizontalScroll;
            control.VerticalScrollMode = newElement.VerticalScroll;
            control.HorizontalContentAlignment = newElement.HorizontalContent;
            return;
        }

        // 见 ScrollViewerElement 的注释：横向可滚动 = 用无限宽测内容 = 宽度随子项漂移。
        // 这几个属性直接决定布局测量方式，尤其不能无条件重写。
        PropWriter.Set(
            oldElement.HorizontalScrollBar,
            newElement.HorizontalScrollBar,
            value => control.HorizontalScrollBarVisibility = value);
        PropWriter.Set(
            oldElement.VerticalScrollBar,
            newElement.VerticalScrollBar,
            value => control.VerticalScrollBarVisibility = value);
        PropWriter.Set(
            oldElement.HorizontalScroll,
            newElement.HorizontalScroll,
            value => control.HorizontalScrollMode = value);
        PropWriter.Set(
            oldElement.VerticalScroll,
            newElement.VerticalScroll,
            value => control.VerticalScrollMode = value);
        PropWriter.Set(
            oldElement.HorizontalContent,
            newElement.HorizontalContent,
            value => control.HorizontalContentAlignment = value);
    }
}
