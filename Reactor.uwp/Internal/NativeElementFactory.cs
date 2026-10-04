// Reactor.Uwp.Native 桥的托管侧封装。
//
// 为什么需要它（2026-09 实测定性，勿删）：
//   ItemsRepeater 是 WinUI 2 里唯一真虚拟化的面板，但它的自定义元素工厂必须实现
//   Microsoft.UI.Xaml.Controls.IElementFactory —— 这个接口（以及 IElementFactoryOverrides）
//   在 C# 投影里被标成 internal（CS0122），UWP 项目无法自行实现；继承 ElementFactory
//   基类的 C# 对象虽然能通过 ItemTemplate 的赋值校验，但原生 realize 时对 CCW 做
//   QueryInterface 会失败并直接让进程崩掉（0x80004002 / fail-fast）。
//
//   解法：用 C++/WinRT 写 Reactor.Uwp.Native.dll 实现这些接口（C++ 直接吃 winmd，
//   没有投影可见性限制），再把 GetElement / RecycleElement 转发给这里的托管回调。
//   原生侧同时实现 ItemsRepeater 实际 QI 的内部 shim 接口 IElementFactoryShim
//   （GUID 94CD53E1-53E9-5921-B8EC-DDB9396D418E，不在 public winmd 里）。
//
// 跨 ABI 的两个硬约定（踩过坑，改动前先读）：
//   1. 回调返回的必须是「所属引用」：NativeObject.ThisPtr 不携带引用，必须 Marshal.AddRef。
//   2. 那个 ThisPtr 是托管包装的**默认接口**指针，不是 XAML 要的 IUIElement 指针。
//      直接交给 ItemsRepeater 会让 XAML 把错误接口的 vtable 当 UIElement 用
//      → 槽位落在别处 → CFG 间接调用校验失败（c0000409 / data=10）→ fail-fast。
//      原生侧 GetElementImpl 里的 QueryInterface(IUIElement) 就是做这个归一化，别删。
//
// 注意：WinUI 2.8 的 ElementFactoryGetArgs 只有 Data / Parent 两个成员，
// 没有 WinUI 3 的 Index —— 需要下标时只能自己按 Data 反查。

using System;
using System.Runtime.InteropServices;
using Windows.UI.Xaml;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 把 <see cref="Func{Object, UIElement, UIElement}"/> 形式的托管工厂包装成
/// ItemsRepeater.ItemTemplate 能接受的原生 IElementFactory 实现。
/// </summary>
    internal sealed partial class NativeElementFactory : IDisposable
    {
    private readonly GetElementFn _get;
    private readonly RecycleElementFn _recycle;
    private readonly Func<object?, UIElement?, UIElement> _create;
    private readonly Action<UIElement?, UIElement?>? _recycleManaged;
    private IntPtr _factory;
    private bool _disposed;

    /// <param name="create">按数据项生成元素。<c>(data, parent) => UIElement</c>。</param>
    /// <param name="recycle">元素被回收（滚出视窗进入回收池）时回调，可选。</param>
    /// <param name="logPath">原生诊断日志路径（留空则关闭，正常产品路径应留空）。</param>
    public NativeElementFactory(
        Func<object?, UIElement?, UIElement> create,
        Action<UIElement?, UIElement?>? recycle = null,
        string? logPath = null)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _recycleManaged = recycle;

        // 委托必须由本实例保活：释放后原生侧的 thunk 会变野指针。
        _get = OnGetElement;
        _recycle = OnRecycleElement;

        int hr = NativeMethods.Create(
            IntPtr.Zero,
            Marshal.GetFunctionPointerForDelegate(_get),
            Marshal.GetFunctionPointerForDelegate(_recycle),
            logPath,
            out _factory);
        if (hr < 0 || _factory == IntPtr.Zero)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        // 原生工厂通过 out 参数返回一个 abi 指针（带一个引用），需要包装成能赋给 ItemTemplate 的对象。
        var wrapped = WinRT.MarshalInspectable<object>.FromAbi(_factory);
        if (wrapped is null)
        {
            throw new InvalidOperationException("原生工厂对象无法回包装为 WinRT 对象。");
        }
        ItemTemplate = wrapped;
    }

    /// <summary>
    /// 可直接赋给 <c>ItemsRepeater.ItemTemplate</c> 的对象（原生工厂的 CCW 视图）。
    /// </summary>
    public object ItemTemplate { get; }

    // 二分定位用的临时开关（定位完删除）
    internal static bool EnableRecycleUnwrap = true;

    private IntPtr? _parentPtr;
    private UIElement? _parentView;

    /// <summary>
    /// parent 恒为同一个对象（ItemsRepeater），接管和回收期间会被请求成百上千次。
    /// 每次都新建 RCW 会在 CLR 侧堆出上千个指向同一 COM 对象的托管包装：
    /// 引用计数、终结队列和 GC 压力全部放大，急速滚动下表现出随机 fail-fast。
    /// 这里按指针缓存，只建一次。
    /// </summary>
    private UIElement? ParentOf(IntPtr parent)
    {
        if (parent == IntPtr.Zero)
        {
            return null;
        }
        if (_parentPtr == parent)
        {
            return _parentView;
        }
        var view = TryUnwrap<UIElement>(parent);
        _parentPtr = parent;
        _parentView = view;
        return view;
    }

    private IntPtr OnGetElement(IntPtr ctx, IntPtr data, IntPtr parent)
    {
        UIElement? element;
        IntPtr result = IntPtr.Zero;
        try
        {
            element = _create(TryUnwrap<object>(data), ParentOf(parent));
            if (element is null)
            {
                return IntPtr.Zero;
            }

            // ① 见文件头：必须给出所属引用，原生侧会把这个引用 Release 掉。
            result = ((WinRT.IWinRTObject)element).NativeObject.ThisPtr;
            Marshal.AddRef(result);
            return result;
        }
        catch
        {
            // 铁律：跨反向 P/Invoke 逃出的托管异常会让 CLR 直接 FailFast（0x80131623），
            // 拿不到任何托管堆栈。这里全部吃掉，工厂返回空即视为本次 realize 失败。
            if (result != IntPtr.Zero)
            {
                try { Marshal.Release(result); } catch { /* 已无法补救 */ }
            }
            return IntPtr.Zero;
        }
    }

    private void OnRecycleElement(IntPtr ctx, IntPtr element, IntPtr parent)
    {
        if (_recycleManaged is null)
        {
            return;
        }
        try
        {
            _recycleManaged(
                EnableRecycleUnwrap ? TryUnwrap<UIElement>(element) : null,
                EnableRecycleUnwrap ? TryUnwrap<UIElement>(parent) : null);
        }
        catch
        {
            // 回收回调里的异常绝不能穿到原生帧（XAML realize 路径上没有托管异常处理）
        }
    }

    private static T? TryUnwrap<T>(IntPtr abi) where T : class
    {
        if (abi == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            // 这里是「借用」语义：指针属于 XAML，本回调只是临时取用。
            // MarshalInspectable.FromAbi 不额外 AddRef（它把调用方已持有的引用收编），
            // 所以必须先 AddRef 配平，否则每次回调都偷走对象一个引用——
            // 上百次之后 XAML 对象（比如 ItemsRepeater 自己）会被提前释放，
            // 随后 CLR 检测到堆破坏直接 FailFast（0x80131623）。
            // 与原生侧 BorrowGetArgs / BorrowRecycleArgs 是同一个约定。
            Marshal.AddRef(abi);
            return WinRT.MarshalInspectable<T>.FromAbi(abi);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_factory != IntPtr.Zero)
        {
            // 工厂被 XAML 持有（在 ItemTemplate 生命周期内），释放这里多出的那一个引用。
            NativeMethods.Release(_factory);
            _factory = IntPtr.Zero;
        }
        GC.KeepAlive(_get);
        GC.KeepAlive(_recycle);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr GetElementFn(IntPtr ctx, IntPtr data, IntPtr parent);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RecycleElementFn(IntPtr ctx, IntPtr element, IntPtr parent);

    private static partial class NativeMethods
    {
        [LibraryImport("Reactor.Uwp.Native.dll", EntryPoint = "RNAF_Create",
            StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int Create(
            IntPtr ctx, IntPtr getFn, IntPtr recycleFn, string? logPath, out IntPtr factory);

        [LibraryImport("Reactor.Uwp.Native.dll", EntryPoint = "RNAF_Release")]
        internal static partial void Release(IntPtr factory);
    }
}
