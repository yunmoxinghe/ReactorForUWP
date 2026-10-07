using System;
using Windows.UI;
using MuxControls = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    // ── 取值与富文本 ─────────────────────────────────────────────

    /// <summary>
    /// 取色器：受控 <c>Color</c>。
    /// </summary>
    /// <param name="color">当前颜色（<b>受控</b>：给了值才受控，null = 不管它）。</param>
    /// <param name="onColorChanged">颜色变了。</param>
    /// <param name="isAlphaEnabled">透明度那一档在不在。关掉会把 A 拉到 255。</param>
    /// <param name="isAlphaSliderVisible">透明度滑杆在不在。</param>
    /// <param name="isHexInputVisible">十六进制输入框在不在。</param>
    /// <param name="isColorSliderVisible">色相滑杆在不在。</param>
    /// <param name="isColorSpectrumVisible">二维光谱在不在。</param>
    /// <param name="shape">光谱形状：方框还是圆环。</param>
    /// <param name="components">光谱那两轴是哪两个通道。</param>
    public static ColorPickerElement ColorPicker(
        Color? color = null,
        Action<Color>? onColorChanged = null,
        bool isAlphaEnabled = false,
        bool isAlphaSliderVisible = true,
        bool isHexInputVisible = true,
        bool isColorSliderVisible = true,
        bool isColorSpectrumVisible = true,
        MuxControls.ColorSpectrumShape shape = MuxControls.ColorSpectrumShape.Box,
        MuxControls.ColorSpectrumComponents components =
            MuxControls.ColorSpectrumComponents.HueSaturation,
        bool isMoreButtonVisible = true) =>
        new()
        {
            Color = color,
            OnColorChanged = onColorChanged,
            IsAlphaEnabled = isAlphaEnabled,
            IsAlphaSliderVisible = isAlphaSliderVisible,
            IsHexInputVisible = isHexInputVisible,
            IsColorSliderVisible = isColorSliderVisible,
            IsColorSpectrumVisible = isColorSpectrumVisible,
            Shape = shape,
            Components = components,
            IsMoreButtonVisible = isMoreButtonVisible,
        };

    /// <summary>
    /// 富文本编辑框：带格式的文本编辑。<b>文本只出不进</b>。
    /// </summary>
    /// <remarks>
    /// 官方 <c>RichEditBox</c> 没有 <c>Text</c> 属性（文本住在 <c>Document</c> 里），
    /// 所以这里也<b>不提供受控文本</b>：<paramref name="initialText"/> 只在挂载时写
    /// 一次，之后由 <paramref name="onTextChanged"/> 只出不进。理由见
    /// <see cref="RichEditBoxElement"/> 的说明。
    /// </remarks>
    /// <param name="initialText"><b>初始</b>文本；之后不再写回。</param>
    /// <param name="onTextChanged">文本变了（参数是剥掉末尾段落符之后的文本）。</param>
    /// <param name="header">标题。</param>
    /// <param name="placeholderText">占位提示。</param>
    /// <param name="isReadOnly">只读（仍可选中复制，与"不给回调"不是一回事）。</param>
    /// <param name="acceptsReturn">回车是换行还是"确认"。</param>
    /// <param name="isSpellCheckEnabled">要不要拼写检查。</param>
    public static RichEditBoxElement RichEditBox(
        string? initialText = null,
        Action<string>? onTextChanged = null,
        string? header = null,
        string? placeholderText = null,
        bool isReadOnly = false,
        bool acceptsReturn = true,
        bool isSpellCheckEnabled = true) =>
        new()
        {
            InitialText = initialText,
            OnTextChanged = onTextChanged,
            Header = header,
            PlaceholderText = placeholderText,
            IsReadOnly = isReadOnly,
            AcceptsReturn = acceptsReturn,
            IsSpellCheckEnabled = isSpellCheckEnabled,
        };
}
