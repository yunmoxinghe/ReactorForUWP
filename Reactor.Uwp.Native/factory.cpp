// Reactor.Uwp.Native —— WinUI 2 在 C# 投影层封死的能力的原生桥。
//
// 背景（2026-09-27 实测定性）：
// 1. Microsoft.UI.Xaml.Controls.IElementFactory（标记接口）在 C# 投影里是 internal（CS0122）；
// 2. Microsoft.UI.Xaml.Controls.IElementFactoryOverrides（GetElementCore/RecycleElementCore）
//    同样对 C# 不可实现；
// 3. 继承 ElementFactory 基类的 C# 对象能通过 ItemsRepeater.ItemTemplate 的赋值验证，
//    但原生 realize 时对 CCW QI 失败 → E_NOINTERFACE → 进程崩溃。
//
// 本 DLL 用 C++/WinRT 实现上述接口（C++ 直接吃 winmd，没有投影可见性限制），
// 把 GetElement/RecycleElement 转发给 C# 的 __stdcall 回调，从而让 UWP 侧也能像
// 官方 WinUI 3 Reactor 那样用 ItemsRepeater + 自定义工厂做真虚拟化。
//
// 回调 ABI（x64 只有一种调用约定）：
//   GetElement:  void* (__stdcall*)(void* ctx, void* data, void* parent) -> UIElement abi（借用，不含引用）
//   Recycle:     void  (__stdcall*)(void* ctx, void* element, void* parent)
// C# 侧的封送约定见 Reactor.uwp/Internal/NativeElementFactory.cs。
//
// ── 代码约定（改动前先读，别退回裸指针写法）───────────────────────────
// 1. 一切 COM 引用都由 RAII 持有（winrt::com_ptr / winrt::Windows::Foundation::IUnknown），
//    不写裸 AddRef / Release。借来的指针用 copy_from 配平、detach_abi 移交——
//    引用计数的配平关系写在注释里，改一处要同时看另一处。
// 2. 一切内核句柄都由 RAII 持有（UniqueHandle），崩溃路径上的早退不能漏 CloseHandle。
// 3. 函数内的字符缓冲一律用调用方传入或栈上局部，**不用函数级 static**：
//    崩溃诊断跑在任意线程（VEH 无线程归属），共享静态缓冲会互相踩。
// 4. 热路径（GetElement / RecycleElement）默认**不写日志**：虚拟化滚动时它们
//    每秒被调用成百上千次，一次 CreateFile2 + WriteFile + CloseHandle 就能把
//    滚动拖垮。流程日志走 Trace()，受 RNAF_SetTrace 门控；崩溃日志走 AppendLog()，
//    那是要留后事的，不受门控。

#include <windows.h>
#include <inspectable.h>
#include <fileapi.h>
#include <libloaderapi.h>
#include <processthreadsapi.h>

#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <string>

#include "winrt/Microsoft.UI.Xaml.Controls.h"
#include "winrt/impl/Windows.UI.Xaml.Controls.2.h"
#include "winrt/Windows.UI.Xaml.h"
#include "winrt/Windows.UI.Xaml.Controls.h"
#include "winrt/Windows.Foundation.h"

using namespace winrt;

// 崩溃诊断要读 PC / SP，寄存器名随架构而变：x64 是 Rip/Rsp，ARM64 是 Pc/Sp。
// 这里统一成宏，避免诊断代码写死某一套寄存器名导致交叉编译不过。
#if defined(_ARM64_) || defined(_M_ARM64)
#define REACTOR_CTX_PC(c) ((c)->Pc)
#define REACTOR_CTX_SP(c) ((c)->Sp)
#else
#define REACTOR_CTX_PC(c) ((c)->Rip)
#define REACTOR_CTX_SP(c) ((c)->Rsp)
#endif

namespace
{
    // 数组长度。不依赖 MSVC 的 _countof（它在 <vcruntime.h> 里，包含顺序一变就可能没了）。
    template <typename T, size_t N>
    constexpr size_t CountOf(T (&)[N]) noexcept { return N; }

    // ── RAII：内核句柄 ────────────────────────────────────────────────
    // INVALID_HANDLE_VALUE 与 nullptr 都是"无效"，两者都不该 CloseHandle。
    class UniqueHandle
    {
    public:
        UniqueHandle() noexcept = default;
        explicit UniqueHandle(HANDLE handle) noexcept : m_handle(handle) {}
        ~UniqueHandle() { Reset(); }

        UniqueHandle(UniqueHandle const&) = delete;
        UniqueHandle& operator=(UniqueHandle const&) = delete;

        UniqueHandle(UniqueHandle&& other) noexcept : m_handle(other.m_handle)
        {
            other.m_handle = nullptr;
        }

        UniqueHandle& operator=(UniqueHandle&& other) noexcept
        {
            if (this != &other)
            {
                Reset(other.m_handle);
                other.m_handle = nullptr;
            }
            return *this;
        }

        [[nodiscard]] bool Valid() const noexcept
        {
            return m_handle != nullptr && m_handle != INVALID_HANDLE_VALUE;
        }

        [[nodiscard]] HANDLE Get() const noexcept { return m_handle; }

        void Reset(HANDLE handle = nullptr) noexcept
        {
            if (Valid())
            {
                ::CloseHandle(m_handle);
            }
            m_handle = handle;
        }

    private:
        HANDLE m_handle = nullptr;
    };

    // ── 诊断日志 ──────────────────────────────────────────────────────
    // g_logPath 只在 RNAF_Create 里写一次、此后只读；写入方用 g_logReady
    // 做发布（acquire/release 配对），让别的线程看到的是"路径已就绪"。
    std::atomic<bool> g_logReady{ false };
    std::atomic<bool> g_verbose{ false };
    std::wstring g_logPath;

    // 崩溃日志：必须写，不受门控（跑到这儿说明进程要没了）。
    void AppendLog(wchar_t const* text) noexcept
    {
        if (!g_logReady.load(std::memory_order_acquire) || text == nullptr)
        {
            return;
        }

        CREATEFILE2_EXTENDED_PARAMETERS params{};
        params.dwSize = sizeof(params);
        params.dwFileAttributes = FILE_ATTRIBUTE_NORMAL;
        params.dwFileFlags = FILE_FLAG_WRITE_THROUGH;

        UniqueHandle file(::CreateFile2(
            g_logPath.c_str(),
            FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            OPEN_ALWAYS,
            &params));
        if (!file.Valid())
        {
            return;
        }

        LARGE_INTEGER distance{};
        if (!::SetFilePointerEx(file.Get(), distance, nullptr, FILE_END))
        {
            return;
        }

        DWORD written = 0;
        ::WriteFile(
            file.Get(),
            text,
            static_cast<DWORD>(wcslen(text) * sizeof(wchar_t)),
            &written,
            nullptr);
    }

    // 流程日志：受 RNAF_SetTrace 门控，热路径上默认一次都不写。
    void Trace(_Printf_format_string_ wchar_t const* format, ...) noexcept
    {
        if (!g_verbose.load(std::memory_order_relaxed))
        {
            return;
        }

        wchar_t buffer[512];
        va_list args;
        va_start(args, format);
        int written = _vsnwprintf_s(buffer, CountOf(buffer), _TRUNCATE, format, args);
        va_end(args);

        if (written >= 0)
        {
            AppendLog(buffer);
        }
    }

    using GetFn = void* (__stdcall*)(void* ctx, void* data, void* parent);
    using RecycleFn = void (__stdcall*)(void* ctx, void* element, void* parent);

    // ItemsRepeater(2.x) 真正 QI 的是这个内部 shim 接口（带 GetElement/RecycleElement 方法），
    // 它不在 public winmd 里（所以 C# 投影完全看不见），GUID 来自官方文档 IElementFactoryShim：
    // Guid(2496484321, 21481, 22817, 184, 236, 221, 185, 57, 109, 65, 142)
    struct __declspec(uuid("94CD53E1-53E9-5921-B8EC-DDB9396D418E"))
        IElementFactoryShim : ::IInspectable
    {
        virtual HRESULT __stdcall GetElement(void* args, void** result) = 0;
        virtual HRESULT __stdcall RecycleElement(void* args) = 0;
    };

    struct NativeFactory
        : winrt::implements<NativeFactory,
            winrt::Microsoft::UI::Xaml::Controls::IElementFactory,
            winrt::Microsoft::UI::Xaml::Controls::IElementFactoryOverrides,
            IElementFactoryShim>
    {
        void* m_ctx = nullptr;
        GetFn m_get = nullptr;
        RecycleFn m_recycle = nullptr;

        NativeFactory(void* ctx, GetFn get, RecycleFn recycle)
            : m_ctx(ctx), m_get(get), m_recycle(recycle) {}

        // 借用 ABI 指针构造临时 args。
        //
        // 配平关系（两处必须成对看）：XAML 借我们的指针，不携带引用；
        // copy_from 补的那一个引用，正好抵掉 args 析构时的那一次 Release。
        static winrt::Microsoft::UI::Xaml::Controls::ElementFactoryGetArgs
        BorrowGetArgs(void* abi)
        {
            winrt::com_ptr<::IUnknown> borrowed;
            borrowed.copy_from(static_cast<::IUnknown*>(abi));

            winrt::Microsoft::UI::Xaml::Controls::ElementFactoryGetArgs args{ nullptr };
            *winrt::put_abi(args) = winrt::detach_abi(borrowed);
            return args;
        }

        static winrt::Microsoft::UI::Xaml::Controls::ElementFactoryRecycleArgs
        BorrowRecycleArgs(void* abi)
        {
            winrt::com_ptr<::IUnknown> borrowed;
            borrowed.copy_from(static_cast<::IUnknown*>(abi));

            winrt::Microsoft::UI::Xaml::Controls::ElementFactoryRecycleArgs args{ nullptr };
            *winrt::put_abi(args) = winrt::detach_abi(borrowed);
            return args;
        }

        winrt::Windows::UI::Xaml::UIElement GetElementImpl(
            winrt::Microsoft::UI::Xaml::Controls::ElementFactoryGetArgs const& args)
        {
            // 注：WinUI 2.8 的 ElementFactoryGetArgs 只有 Data/Parent（无 Index，与 WinUI 3 不同）
            // 取出的两个子对象要活过 Trace 这条语句，所以先落到具名局部变量里。
            auto parent = args.Parent();
            auto data = args.Data();
            Trace(L"[native] GetElement parent=%p data=%p\r\n",
                  winrt::get_abi(parent), winrt::get_abi(data));

            if (!m_get)
            {
                // A/B 测试模式：完全绕开 C# 回调，native 自建元素返回
                Trace(L"[native] test-mode: returning native-built TextBlock\r\n");
                return winrt::Windows::UI::Xaml::Controls::TextBlock();
            }

            void* elementAbi = m_get(m_ctx, winrt::get_abi(args.Data()), winrt::get_abi(args.Parent()));
            if (!elementAbi)
            {
                return nullptr;
            }

            // 【关键】C# 给的是「所属引用」（已 AddRef），但它指向的是托管侧包装的
            // 默认接口指针，不一定是 XAML 期待的 IUIElement 接口指针。
            // 若不 QI 归一化就交给 ItemsRepeater，XAML 会把 ITextBlock 的 vtable 当
            // IUIElement 用 → 槽位错位 → CFG 间接调用校验失败（c0000409 / data=10）。
            // （对照：`return tb;` 那条路径之所以没事，就是因为 C++/WinRT 的派生→基类
            //   隐式转换本身会做一次 as<UIElement>() == QueryInterface。）
            //
            // borrowed 同时承担两件事：接管回调借出的那个引用（异常安全，任何早退都释放），
            // 以及把 QI 归一化表达成一次 as<>() 而不是手写 QueryInterface + Release。
            winrt::com_ptr<::IUnknown> borrowed;
            borrowed.copy_from(static_cast<::IUnknown*>(elementAbi));

            Trace(L"[native] cb returned raw=%p\r\n", elementAbi);

            // QI 失败会抛 hresult_no_interface，由外层 catch 转成 HRESULT 交回 XAML。
            auto ui = borrowed.as<winrt::Windows::UI::Xaml::UIElement>();

            Trace(L"[native] QI(IUIElement) raw=%p -> ui=%p %s\r\n",
                  elementAbi, winrt::get_abi(ui),
                  winrt::get_abi(ui) == elementAbi ? L"(same)" : L"(DIFFERENT)");

            return ui;
        }

        void RecycleElementImpl(
            winrt::Microsoft::UI::Xaml::Controls::ElementFactoryRecycleArgs const& args)
        {
            if (!m_recycle)
            {
                return;
            }
            m_recycle(m_ctx, winrt::get_abi(args.Element()), winrt::get_abi(args.Parent()));
        }

        // IElementFactoryOverrides（C# 路线当年就是 QI 它失败崩溃的）
        winrt::Windows::UI::Xaml::UIElement GetElementCore(
            winrt::Microsoft::UI::Xaml::Controls::ElementFactoryGetArgs const& args)
        {
            return GetElementImpl(args);
        }

        void RecycleElementCore(
            winrt::Microsoft::UI::Xaml::Controls::ElementFactoryRecycleArgs const& args)
        {
            RecycleElementImpl(args);
        }

        // IElementFactoryShim（ItemsRepeater 实际调用路径）
        HRESULT __stdcall GetElement(void* args, void** result) override
        {
            if (result == nullptr)
            {
                return E_POINTER;
            }
            *result = nullptr;

            try
            {
                Trace(L"[native] GetElement enter\r\n");
                auto element = GetElementImpl(BorrowGetArgs(args));
                *result = winrt::detach_abi(element);
                Trace(L"[native] GetElement -> %p\r\n", *result);
                return S_OK;
            }
            catch (...)
            {
                return winrt::to_hresult();
            }
        }

        HRESULT __stdcall RecycleElement(void* args) override
        {
            try
            {
                Trace(L"[native] RecycleElement enter\r\n");
                RecycleElementImpl(BorrowRecycleArgs(args));
                return S_OK;
            }
            catch (...)
            {
                return winrt::to_hresult();
            }
        }
    };

    // 逐次崩溃计数：上限 30 条，避免崩溃风暴把日志写爆。
    std::atomic<long> g_excLogged{ 0 };

    // 模块名缓冲区由调用方提供（见文件头第 3 条：不用函数级 static）。
    // 返回指向 nameBuffer 内文件名起点的指针；拿不到模块时返回固定串。
    wchar_t const* ModuleOf(
        void* addr, unsigned long long* offset, wchar_t* nameBuffer, size_t nameBufferChars)
    {
        *offset = 0;

        HMODULE module = nullptr;
        if (!::GetModuleHandleExW(
                GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                static_cast<LPCWSTR>(addr),
                &module))
        {
            return L"<no-module>";
        }

        if (::GetModuleFileNameW(module, nameBuffer, static_cast<DWORD>(nameBufferChars)) == 0)
        {
            return L"<no-name>";
        }

        wchar_t const* base = nameBuffer;
        for (wchar_t const* p = nameBuffer; *p != L'\0'; ++p)
        {
            if (*p == L'\\' || *p == L'/')
            {
                base = p + 1;
            }
        }

        MEMORY_BASIC_INFORMATION mbi{};
        if (::VirtualQuery(addr, &mbi, sizeof(mbi)))
        {
            *offset = reinterpret_cast<unsigned long long>(addr)
                    - reinterpret_cast<unsigned long long>(mbi.AllocationBase);
        }

        return base;
    }

    BOOL SafeReadQ(unsigned long long addr, unsigned long long* out)
    {
        __try
        {
            *out = *reinterpret_cast<unsigned long long*>(addr);
            return TRUE;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return FALSE;
        }
    }
}

// WINAPI_FAMILY_APP 下声明被门控，但 ntdll 导出在 UWP 进程内可用
extern "C" void* __cdecl AddVectoredExceptionHandler(unsigned long First, long(__stdcall* Handler)(
    struct _EXCEPTION_POINTERS*));

static long CALLBACK VectoredHandler(PEXCEPTION_POINTERS ep)
{
    // 只记录严重异常（AV / fail-fast 等）；0x4xxxxxxx 是调试打印类噪音（OutputDebugString 每次 Trace 触发两个）
    unsigned int code = static_cast<unsigned>(ep->ExceptionRecord->ExceptionCode);
    if (code < 0xC0000000) return EXCEPTION_CONTINUE_SEARCH;
    if (g_excLogged.fetch_add(1, std::memory_order_relaxed) >= 30) return EXCEPTION_CONTINUE_SEARCH;

    CONTEXT* c = ep->ContextRecord;
    wchar_t line[700];
    swprintf_s(line, L"[nativexc] code=0x%08X addr=%p info0=%llu info1=0x%llu tid=%u\r\n",
               code, ep->ExceptionRecord->ExceptionAddress,
               static_cast<unsigned long long>(ep->ExceptionRecord->ExceptionInformation[0]),
               static_cast<unsigned long long>(ep->ExceptionRecord->ExceptionInformation[1]),
               GetCurrentThreadId());
    AppendLog(line);

#if defined(_ARM64_) || defined(_M_ARM64)
    swprintf_s(line, L"[nativexc]   pc=%p sp=%p fp=%p lr=%p x0=%p x1=%p x19=%p x20=%p\r\n",
               reinterpret_cast<void*>(c->Pc), reinterpret_cast<void*>(c->Sp),
               reinterpret_cast<void*>(c->Fp), reinterpret_cast<void*>(c->Lr),
               reinterpret_cast<void*>(c->X0), reinterpret_cast<void*>(c->X1),
               reinterpret_cast<void*>(c->X19), reinterpret_cast<void*>(c->X20));
#else
    swprintf_s(line, L"[nativexc]   rip=%p rsp=%p rax=%p rcx=%p rdx=%p rbx=%p rsi=%p rdi=%p\r\n",
               reinterpret_cast<void*>(c->Rip), reinterpret_cast<void*>(c->Rsp),
               reinterpret_cast<void*>(c->Rax), reinterpret_cast<void*>(c->Rcx),
               reinterpret_cast<void*>(c->Rdx), reinterpret_cast<void*>(c->Rbx),
               reinterpret_cast<void*>(c->Rsi), reinterpret_cast<void*>(c->Rdi));
#endif
    AppendLog(line);

    // RIP 处区域属性 + 机器码倾倒（识别这段"模块外代码"到底是什么）
    {
        MEMORY_BASIC_INFORMATION mbi{};
        ::VirtualQuery(reinterpret_cast<void*>(REACTOR_CTX_PC(c)), &mbi, sizeof(mbi));
        swprintf_s(line, L"[nativexc]   rip-region: base=%p type=0x%lx state=0x%lx prot=0x%lx allocbase=%p\r\n",
                   mbi.BaseAddress, mbi.Type, mbi.State, mbi.Protect, mbi.AllocationBase);
        AppendLog(line);

        // 逐 8 字节安全读并拼出 RIP 附近码流
        unsigned long long q[6] = {};
        BOOL ok = TRUE;
        for (int i = 0; i < 6; ++i)
        {
            ok &= SafeReadQ(static_cast<unsigned long long>(REACTOR_CTX_PC(c)) - 24 + i * 8, &q[i]);
        }
        if (ok)
        {
            unsigned char* p = reinterpret_cast<unsigned char*>(q);
            wchar_t hex[160];
            int pos = 0;
            for (int i = 0; i < 48 && pos < 150; ++i)
            {
                int n = swprintf_s(hex + pos, static_cast<size_t>(160 - pos), L"%02X", p[i]);
                if (n < 0) break;
                pos += n;
            }
            swprintf_s(line, L"[nativexc]   rip-24..rip+23: %s\r\n", hex);
            AppendLog(line);
        }
    }

    // 直接倾倒栈内存（找返回地址 → 定位调用方模块）
    unsigned long long* sp = reinterpret_cast<unsigned long long*>(REACTOR_CTX_SP(c));
    wchar_t moduleName[256];
    for (int i = 0; i < 16; ++i)
    {
        unsigned long long v = 0;
        if (!SafeReadQ(reinterpret_cast<unsigned long long>(sp + i), &v)) break;
        if (v < 0x10000) continue;
        unsigned long long off = 0;
        wchar_t const* m = ModuleOf(reinterpret_cast<void*>(v), &off, moduleName, CountOf(moduleName));
        wchar_t line2[400];
        swprintf_s(line2, L"[nativexc]   [rsp+%02d]=0x%016llx %s+0x%llx\r\n", i * 8, v, m, off);
        AppendLog(line2);
    }

    // 回溯（模块+偏移）
    void* frames[24] = {};
    USHORT n = ::RtlCaptureStackBackTrace(0, 24, frames, nullptr);
    for (USHORT i = 0; i < n; ++i)
    {
        unsigned long long fo = 0;
        wchar_t const* fm = ModuleOf(frames[i], &fo, moduleName, CountOf(moduleName));
        wchar_t line2[400];
        swprintf_s(line2, L"[nativexc]   #%02u %s+0x%llx\r\n", i, fm, fo);
        AppendLog(line2);
    }

    return EXCEPTION_CONTINUE_SEARCH;
}

// 导出：创建原生工厂。out 收到工厂对象的 IUnknown*（一个引用）。
// logPath：崩溃诊断日志路径（必须指向包 LocalState 内），可为空。
extern "C" __declspec(dllexport) HRESULT __stdcall RNAF_Create(
    void* ctx, void* getFn, void* recycleFn, const wchar_t* logPath, void** out)
{
    if (out == nullptr)
    {
        return E_POINTER;
    }
    *out = nullptr;

    if (logPath != nullptr && *logPath != L'\0' && !g_logReady.load(std::memory_order_acquire))
    {
        g_logPath = logPath;
        // 先发布路径，再挂 VEH：否则处理器可能在路径还没写完时就抓到异常。
        g_logReady.store(true, std::memory_order_release);
        AddVectoredExceptionHandler(1, VectoredHandler);
    }

    try
    {
        auto factory = winrt::make<NativeFactory>(
            ctx,
            reinterpret_cast<GetFn>(getFn),
            reinterpret_cast<RecycleFn>(recycleFn));
        *out = winrt::detach_abi(factory);
        return S_OK;
    }
    catch (...)
    {
        return winrt::to_hresult();
    }
}

// 导出：释放 RNAF_Create 借出的那个引用。
extern "C" __declspec(dllexport) void __stdcall RNAF_Release(void* factory)
{
    if (factory == nullptr)
    {
        return;
    }

    // RAII 接管这一次性的裸指针，走 com_ptr 的析构释放，不手写 Release。
    winrt::com_ptr<::IUnknown> owner;
    owner.attach(static_cast<::IUnknown*>(factory));
}

// 导出：开关流程日志（Trace）。默认关——热路径上写文件会把虚拟化滚动拖垮。
// 崩溃日志不受它影响。
extern "C" __declspec(dllexport) void __stdcall RNAF_SetTrace(int enabled)
{
    g_verbose.store(enabled != 0, std::memory_order_relaxed);
}
