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
// C# 侧的封送约定见 UwpApp 侧探针 / Reactor.uwp 后续封装。

#include <windows.h>
#include <inspectable.h>
#include <fileapi.h>
#include <libloaderapi.h>
#include <processthreadsapi.h>
#include <string>
#include "winrt/Microsoft.UI.Xaml.Controls.h"
#include "winrt/impl/Windows.UI.Xaml.Controls.2.h"
#include "winrt/Windows.UI.Xaml.h"
#include "winrt/Windows.UI.Xaml.Controls.h"
#include "winrt/Windows.Foundation.h"

using namespace winrt;

// 前置声明：GetElement 流程日志在结构体定义中使用
static void AppendLog(const wchar_t* text);

namespace
{
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

        // 借用 ABI 指针构造临时 args：包装析构时会 Release，借用时必须补 AddRef 配平
        static winrt::Microsoft::UI::Xaml::Controls::ElementFactoryGetArgs
        BorrowGetArgs(void* abi)
        {
            winrt::Microsoft::UI::Xaml::Controls::ElementFactoryGetArgs args{ nullptr };
            *winrt::put_abi(args) = abi;
            reinterpret_cast<::IUnknown*>(abi)->AddRef();
            return args;
        }
        static winrt::Microsoft::UI::Xaml::Controls::ElementFactoryRecycleArgs
        BorrowRecycleArgs(void* abi)
        {
            winrt::Microsoft::UI::Xaml::Controls::ElementFactoryRecycleArgs args{ nullptr };
            *winrt::put_abi(args) = abi;
            reinterpret_cast<::IUnknown*>(abi)->AddRef();
            return args;
        }

        winrt::Windows::UI::Xaml::UIElement GetElementImpl(
            winrt::Microsoft::UI::Xaml::Controls::ElementFactoryGetArgs const& args)
        {
            // 诊断：记录 XAML 请求的 index / data，排查 data 为 null 的成因
            {
                // 注意：WinUI 2.8 的 ElementFactoryGetArgs 只有 Data/Parent（无 Index，与 WinUI 3 不同）
                void* parentAbi = winrt::get_abi(args.Parent());
                void* dataAbi = winrt::get_abi(args.Data());
                wchar_t line[160];
                swprintf_s(line, L"[native] GetElement parent=0x%p data=0x%p\r\n", parentAbi, dataAbi);
                AppendLog(line);
            }
            // 注：Text 不设值（OS Controls 投影 2.h 缺 consume 定义，C3779）
            if (!m_get)
            {
                // A/B 测试模式：完全绕开 C# 回调，native 自建元素返回
                winrt::Windows::UI::Xaml::Controls::TextBlock tb;
                AppendLog(L"[native] test-mode: returning native-built TextBlock\r\n");
                return tb;
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
            {
                wchar_t line[128];
                swprintf_s(line, L"[native] cb returned raw=0x%p\r\n", elementAbi);
                if (g_traceFlow) AppendLog(line);
            }
            winrt::Windows::UI::Xaml::UIElement ui{ nullptr };
            HRESULT hr = reinterpret_cast<::IUnknown*>(elementAbi)->QueryInterface(
                winrt::guid_of<winrt::Windows::UI::Xaml::UIElement>(), winrt::put_abi(ui));
            // 回调借出的那个引用到此为止（成功后由 ui 持有，失败后必须释放）
            reinterpret_cast<::IUnknown*>(elementAbi)->Release();
            if (FAILED(hr))
            {
                wchar_t line[128];
                swprintf_s(line, L"[native] QI(IUIElement) FAILED hr=0x%08X\r\n", (unsigned)hr);
                AppendLog(line);
                throw winrt::hresult_error(hr);
            }
            {
                // 引用计数体检：AddRef+Release 读回当前计数（只读，净效果为零）
                auto unkEl = reinterpret_cast<::IUnknown*>(elementAbi);
                unsigned long rawRc = unkEl->AddRef(); unkEl->Release();
                unsigned long uiRc = 0;
                if (winrt::get_abi(ui))
                {
                    auto unkUi = reinterpret_cast<::IUnknown*>(winrt::get_abi(ui));
                    uiRc = unkUi->AddRef(); unkUi->Release();
                }
                wchar_t line[192];
                swprintf_s(line, L"[native] QI(IUIElement) raw=0x%p rc=%u -> ui=0x%p rc=%u %s\r\n",
                           elementAbi, rawRc, winrt::get_abi(ui), uiRc,
                           winrt::get_abi(ui) == elementAbi ? L"(same)" : L"(DIFFERENT)");
                AppendLog(line);
            }
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
            try
            {
                AppendLog(L"[native] GetElement enter\r\n");
                auto element = GetElementImpl(BorrowGetArgs(args));
                void* raw = winrt::get_abi(element);
                wchar_t line[128];
                swprintf_s(line, L"[native] cb returned element=0x%p\r\n", raw);
                AppendLog(line);
                *result = winrt::detach_abi(element);
                AppendLog(L"[native] detached, returning to caller\r\n");
                return S_OK;
            }
            catch (...)
            {
                *result = nullptr;
                return winrt::to_hresult();
            }
        }

        HRESULT __stdcall RecycleElement(void* args) override
        {
            try
            {
                AppendLog(L"[native] RecycleElement enter\r\n");
                RecycleElementImpl(BorrowRecycleArgs(args));
                AppendLog(L"[native] RecycleElement done\r\n");
                return S_OK;
            }
            catch (...)
            {
                return winrt::to_hresult();
            }
        }
    };
}

// 导出：创建原生工厂。out 收到工厂对象的 IUnknown*（一个引用）。
// logPath：崩溃诊断日志路径（必须指向包 LocalState 内），可为空。
static std::wstring g_logPath;
static volatile long g_excLogged = 0;
static bool g_traceFlow = false;   // 逐次流程日志开关：默认关，避免拖慢时序影响崩溃复现

static void AppendLog(const wchar_t* text)
{
    if (g_logPath.empty()) return;
    CREATEFILE2_EXTENDED_PARAMETERS p = {};
    p.dwSize = sizeof(p);
    p.dwFileAttributes = FILE_ATTRIBUTE_NORMAL;
    p.dwFileFlags = FILE_FLAG_WRITE_THROUGH;
    HANDLE h = CreateFile2(g_logPath.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           OPEN_ALWAYS, &p);
    if (h == INVALID_HANDLE_VALUE) return;
    SetFilePointer(h, 0, nullptr, FILE_END);
    DWORD written = 0;
    WriteFile(h, text, (DWORD)(wcslen(text) * sizeof(wchar_t)), &written, nullptr);
    CloseHandle(h);
}

static const wchar_t* ModuleOf(void* addr, unsigned long long* off)
{
    HMODULE h = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            (LPCWSTR)addr, &h))
        return L"<no-module>";
    static wchar_t name[256];
    GetModuleFileNameW(h, name, 256);
    const wchar_t* base = name;
    for (const wchar_t* p = name; *p; ++p) if (*p == L'\\') base = p + 1;
    MEMORY_BASIC_INFORMATION mbi;
    if (VirtualQuery(addr, &mbi, sizeof(mbi)))
        *off = (unsigned long long)addr - (unsigned long long)mbi.AllocationBase;
    else
        *off = 0;
    return base;
}

// WINAPI_FAMILY_APP 下声明被门控，但 ntdll 导出在 UWP 进程内可用
extern "C" void* __cdecl AddVectoredExceptionHandler(unsigned long First, long(__stdcall* Handler)(
    struct _EXCEPTION_POINTERS*));

static BOOL SafeReadQ(unsigned long long addr, unsigned long long* out)
{
    __try
    {
        *out = *(unsigned long long*)addr;
        return TRUE;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return FALSE;
    }
}

static long CALLBACK VectoredHandler(PEXCEPTION_POINTERS ep)
{
    // 只记录严重异常（AV / fail-fast 等）；0x4xxxxxxx 是调试打印类噪音（OutputDebugString 每次 Trace 触发两个）
    unsigned int code = (unsigned)ep->ExceptionRecord->ExceptionCode;
    if (code < 0xC0000000) return EXCEPTION_CONTINUE_SEARCH;
    if (InterlockedIncrement(&g_excLogged) > 30) return EXCEPTION_CONTINUE_SEARCH;
    CONTEXT* c = ep->ContextRecord;
    wchar_t line[700];
    swprintf_s(line, L"[nativexc] code=0x%08X addr=%p info0=%llu info1=0x%llu tid=%u\r\n",
               code, ep->ExceptionRecord->ExceptionAddress,
               (unsigned long long)ep->ExceptionRecord->ExceptionInformation[0],
               (unsigned long long)ep->ExceptionRecord->ExceptionInformation[1],
               GetCurrentThreadId());
    AppendLog(line);
    swprintf_s(line, L"[nativexc]   rip=%p rsp=%p rax=%p rcx=%p rdx=%p rbx=%p rsi=%p rdi=%p\r\n",
               (void*)c->Rip, (void*)c->Rsp, (void*)c->Rax, (void*)c->Rcx, (void*)c->Rdx,
               (void*)c->Rbx, (void*)c->Rsi, (void*)c->Rdi);
    AppendLog(line);
    // RIP 处区域属性 + 机器码倾倒（识别这段"模块外代码"到底是什么）
    {
        MEMORY_BASIC_INFORMATION mbi {};
        VirtualQuery((void*)c->Rip, &mbi, sizeof(mbi));
        swprintf_s(line, L"[nativexc]   rip-region: base=%p type=0x%lx state=0x%lx prot=0x%lx allocbase=%p\r\n",
                   mbi.BaseAddress, mbi.Type, mbi.State, mbi.Protect, mbi.AllocationBase);
        AppendLog(line);
        // 逐 8 字节安全读并拼出 RIP 附近码流
        unsigned long long q[6];
        int ok = 1;
        for (int i = 0; i < 6; ++i)
            ok &= SafeReadQ((unsigned long long)c->Rip - 24 + i * 8, &q[i]);
        if (ok)
        {
            unsigned char* p = (unsigned char*)q;
            wchar_t hex[160]; int pos = 0;
            for (int i = 0; i < 48 && pos < 150; ++i)
                pos += swprintf_s(hex + pos, 160 - pos, L"%02X", p[i]);
            swprintf_s(line, L"[nativexc]   rip-24..rip+23: %s\r\n", hex);
            AppendLog(line);
        }
    }
    // 直接倾倒栈内存（找返回地址 → 定位调用方模块）
    unsigned long long* sp = (unsigned long long*)c->Rsp;
    for (int i = 0; i < 16; ++i)
    {
        unsigned long long v = 0;
        if (!SafeReadQ((unsigned long long)(sp + i), &v)) break;
        if (v < 0x10000) continue;
        unsigned long long off = 0;
        const wchar_t* m = ModuleOf((void*)v, &off);
        wchar_t line2[400];
        swprintf_s(line2, L"[nativexc]   [rsp+%02d]=0x%016llx %s+0x%llx\r\n", i * 8, v, m, off);
        AppendLog(line2);
    }
    // 回溯（模块+偏移）
    void* frames[24];
    USHORT n = RtlCaptureStackBackTrace(0, 24, frames, nullptr);
    for (USHORT i = 0; i < n; ++i)
    {
        unsigned long long fo = 0;
        const wchar_t* fm = ModuleOf(frames[i], &fo);
        wchar_t line2[400];
        swprintf_s(line2, L"[nativexc]   #%02u %s+0x%llx\r\n", i, fm, fo);
        AppendLog(line2);
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

extern "C" __declspec(dllexport) HRESULT __stdcall RNAF_Create(
    void* ctx, void* getFn, void* recycleFn, const wchar_t* logPath, void** out)
{
    if (logPath && *logPath && g_logPath.empty())
    {
        g_logPath = logPath;
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
    if (factory)
    {
        reinterpret_cast<::IUnknown*>(factory)->Release();
    }
}
