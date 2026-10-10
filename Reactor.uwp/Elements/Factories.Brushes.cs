using Windows.Foundation;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml.Media;
using MuxMedia = Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    /// <summary>
    /// 一块亚克力（对齐官方 <c>AcrylicBrush(tintColor, tintOpacity, fallbackColor,
    /// tintLuminosityOpacity)</c>）。返回的是 <b>真的</b>
    /// <see cref="Windows.UI.Xaml.Media.AcrylicBrush"/>，不是"像亚克力的纯色"。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>它是刷子，不是元素。</b>元素那一侧（<c>Fill</c> / <c>Background</c> /
    /// <c>PaneBackground</c>…）收的都是 <see cref="Brush"/> 实例，本方法就是把
    /// "刷子"这一层补上——之前没有它，"给某个属性配一块亚克力"只能走
    /// <c>Native()</c>，因为声明式一侧没有刷子的表达。
    /// </para>
    /// <para>
    /// <b>这一块是"应用内亚克力"（<c>BackgroundSource = Backdrop</c>，官方默认）：</b>
    /// 它模糊的是<b>窗口内、它后面那层 XAML 内容</b>。想透出桌面（<c>HostBackdrop</c>，
    /// 需要窗口本身把背景铺开）走 <c>.Backdrop(BackdropKind.DesktopAcrylic)</c> 那条路
    /// ——那是宿主窗口的事，不是某个属性上的刷子能决定的，所以本方法<b>不</b>收
    /// <c>BackgroundSource</c> 参数（与官方一致）。
    /// </para>
    /// <para>
    /// <c>fallbackColor</c> 与 <c>tintLuminosityOpacity</c> 给了才写：两个都是
    /// "不设就用控件自己的默认"，写死一个值进去等于替官方做默认值决策。
    /// <c>tintLuminosityOpacity</c> 在 UWP 上是 <c>double?</c>，null 就是"交给控件"。
    /// </para>
    /// <para>
    /// <b>要它真的生效，后面必须有东西</b>：亚克力是"取背后那层做模糊"，后面
    /// 什么都没画（比如窗口背景是透明或纯色没铺开）时看到的就是
    /// <c>fallbackColor</c> 那一层纯色——不是 bug，是它唯一的素材没了。
    /// </para>
    /// </remarks>
    public static AcrylicBrush AcrylicBrush(
        Color tintColor,
        double tintOpacity = 0.8,
        Color? fallbackColor = null,
        double? tintLuminosityOpacity = null)
    {
        var brush = new AcrylicBrush
        {
            TintColor = tintColor,
            TintOpacity = tintOpacity,
        };

        if (fallbackColor is { } fallback)
        {
            brush.FallbackColor = fallback;
        }

        if (tintLuminosityOpacity is { } luminosity)
        {
            brush.TintLuminosityOpacity = luminosity;
        }

        return brush;
    }

    /// <summary>
    /// 一块径向渐变（对齐官方 <c>RadialGradientBrush(GradientStops, Center,
    /// GradientOrigin, RadiusX, RadiusY, MappingMode, InterpolationSpace,
    /// SpreadMethod)</c>）。返回的是 <b>真的</b>
    /// <see cref="MuxMedia.RadialGradientBrush"/>，不是位图也不是自绘。
    /// </summary>
    /// <param name="stops">
    /// 渐变停靠点：<b>偏移 → 颜色</b>。偏移是 0（圆心）到 1（半径末端）的比例，
    /// 不要求排好序（官方按写进去的顺序合成），也不要求非得有两档。
    /// </param>
    /// <param name="center">圆心相对 bounds 的位置（<c>null</c> = 交给官方默认）。</param>
    /// <param name="gradientOrigin">
    /// <c>SpreadMethod = Reflect</c> / <c>Repeat</c> 时反射 / 重复的<b>起点</b>
    /// （<c>null</c> = 交给官方默认）。<c>Pad</c> 那档下它不起作用。
    /// </param>
    /// <param name="radiusX">横向半径（<c>null</c> = 官方默认）。</param>
    /// <param name="radiusY">纵向半径（<c>null</c> = 官方默认）。</param>
    /// <param name="mappingMode">
    /// 上面那些数字是<b>比例</b>（<c>RelativeToBoundingBox</c>，默认）还是
    /// <b>绝对像素</b>（<c>Absolute</c>）。
    /// </param>
    /// <param name="interpolationSpace">
    /// 在哪个空间里插值（<c>CompositionColorSpace.Rgb</c> 按 RGB 数值插、
    /// <c>Hsl</c> 绕着色相环走，两者的中间色明显不同）。<c>null</c> = 官方默认。
    /// </param>
    /// <param name="spreadMethod">
    /// 超出端点的那部分怎么补：<c>Pad</c>（端点色延伸）/ <c>Reflect</c>（来回镜像）
    /// / <c>Repeat</c>（从头再来）。<c>null</c> = 官方默认。
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>它是 WinUI 2 的类型，不是 UWP 原生的。</b>这是本库第一个落在
    /// <c>Microsoft.UI.Xaml.Media</c>（而不是 <c>Windows.UI.Xaml.Media</c>）下的画笔：
    /// 官方把径向渐变排在 WinUI 那一侧（UWP 的 <c>Media</c> 命名空间里只有线性的
    /// <c>LinearGradientBrush</c>，见 winmd 里 <c>TypeDefinition</c> 的清单），
    /// 所以这里也不做"顺手补一个 UWP 版"的事——对照组不存在，补了就是自创 API。
    /// 它的<b>成员类型反过来全是 UWP 的</b>：<c>GradientStop</c> /
    /// <c>BrushMappingMode</c> / <c>GradientSpreadMethod</c> /
    /// <c>ColorInterpolationMode</c> 都在 <c>Windows.UI.Xaml.Media</c> 里，
    /// 只有刷子自己是 WinUI 的。
    /// </para>
    /// <para>
    /// <b>几何参数一律 <c>null</c> = 不写</b>，与 <see cref="AcrylicBrush"/> 同一条规矩：
    /// 官方这组属性各自有默认值（圆心与渐变原点在正中、半径一半、
    /// <c>MappingMode = RelativeToBoundingBox</c>），抄一个数字进来就是把一个
    /// 可能随版本调整的值当成契约。要改就显式写。
    /// </para>
    /// <b>停靠点集合是一次性写死的。</b>官方的 <c>GradientStops</c> 是<b>只读</b>
    /// 属性（getter 给出控件自己那份集合、没有 setter，实测赋值会编不过），
    /// 所以这里往它给的那份集合里 <c>Add</c>，而不是攒一个新的赋回去。
    /// 顺带一层约束：本方法只在建新刷子时被调用，不存在"往既有集合里追加"
    /// 这回事——刷子是实例，没有 identity 可言。
    /// </para>
    /// <para>
    /// <b>刷子按引用比</b>（<c>PropWriter.SetRef</c>）：每轮渲染 <c>new</c> 一支刷子
    /// 就是一次真的重绘，所以样例里那些刷子一律做成<b>静态字段</b>——
    /// 这条不是优化建议，是"不这么写就每帧重画"。
    /// </para>
    /// </remarks>
    public static MuxMedia.RadialGradientBrush RadialGradientBrush(
        (double Offset, Color Color)[] stops,
        Point? center = null,
        Point? gradientOrigin = null,
        double? radiusX = null,
        double? radiusY = null,
        BrushMappingMode? mappingMode = null,
        CompositionColorSpace? interpolationSpace = null,
        GradientSpreadMethod? spreadMethod = null)
    {
        var brush = new MuxMedia.RadialGradientBrush();

        foreach (var (offset, color) in stops)
        {
            brush.GradientStops.Add(new GradientStop { Offset = offset, Color = color });
        }

        if (center is { } origin)
        {
            brush.Center = origin;
        }

        if (gradientOrigin is { } start)
        {
            brush.GradientOrigin = start;
        }

        if (radiusX is { } rx)
        {
            brush.RadiusX = rx;
        }

        if (radiusY is { } ry)
        {
            brush.RadiusY = ry;
        }

        if (mappingMode is { } mode)
        {
            brush.MappingMode = mode;
        }

        if (interpolationSpace is { } space)
        {
            brush.InterpolationSpace = space;
        }

        if (spreadMethod is { } spread)
        {
            brush.SpreadMethod = spread;
        }

        return brush;
    }
}
