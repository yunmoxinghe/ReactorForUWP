using System;
using System.Collections.Generic;
using Windows.ApplicationModel;

namespace Reactor.Uwp.Hosting;

/// <summary>
/// 运行时"我到底跑在哪一份二进制上"的自检：WinUI 2 框架包版本、VCLibs 版本、进程架构。
/// </summary>
/// <remarks>
/// <b>为什么需要它。</b>WinUI 2（<c>Microsoft.UI.Xaml</c>）的原生实现<b>不在 AppX 里</b>——
/// AppX 目录只有 <c>Microsoft.UI.Xaml.winmd</c> 与 <c>Microsoft.UI.Xaml.Projection.dll</c>，
/// 真正的实现是 MSIX 框架包依赖（AppxManifest 里的
/// <c>&lt;PackageDependency Name="Microsoft.UI.Xaml.2.8" …/&gt;</c>，由 NuGet 包的
/// <c>AppxPackageRegistration</c> 写入）。
///
/// 而 <c>PackageDependency</c> <b>只有 MinVersion，没有 MaxVersion</b>：MSIX 层面锁不住上限，
/// AppContainer 取的是"满足下限的最高版本"。所以 NuGet 包里写死的那个基线
/// （2.8.7 → <c>MicrosoftUIXamlAppxVersion</c> = 8.2501.31001.0）<b>不等于</b>实际加载的版本；
/// 机器上装了更高的就会跑更高的那份（本机实测 x64 跑的是 8.2511.26001.0，
/// 而 NuGet 基线还停在 8.2501.31001.0）。
///
/// 后果是"同一份代码、同一台机器"这个前提会静默失效：框架包由 Store 独立更新，
/// 一次 A/B 对照只要跨越那次更新，就等于偷偷换了一个变量。这里把实际版本落盘，
/// 让每次对照实验的版本轴是<b>显式</b>的，不必靠回忆。
///
/// 顺带记 VCLibs：它的 Debug / Release 变体（<c>Microsoft.VCLibs.140.00.Debug</c>）
/// 直接影响原生崩溃（含 CFG 校验失败 c0000409）的栈回溯质量。
///
/// 与 <see cref="BackdropDiagnostics"/> 同样：<b>诊断代码不得影响主流程</b>，一律 try/catch 全吞。
/// </remarks>
public static class RuntimeDiagnostics
{
    /// <summary>生成一行紧凑的自检结论，便于随启动日志落盘。</summary>
    public static string Report()
    {
        var parts = new List<string>
        {
            $"OS {BackdropDiagnostics.OsBuild()}",
            $"arch {Architecture()}",
        };

        var deps = FrameworkDependencies();
        if (deps.Count == 0)
        {
            // 读不到是异常信号（多半不是 UWP 宿主 / 权限受限），明说出来而不是省掉，
            // 否则"没打印"和"没有依赖"会混成同一件事。
            parts.Add("deps <读不到>");
        }
        else
        {
            foreach (var dep in deps)
            {
                parts.Add($"{Alias(dep.Id.Name)} {Format(dep.Id.Version)}");
            }
        }

        return string.Join(" | ", parts);
    }

    /// <summary>本进程实际依赖的框架包。读不到返回空。</summary>
    public static IReadOnlyList<Package> FrameworkDependencies()
    {
        try
        {
            // AppContainer 内可读自己包的依赖；非打包进程（裸 exe）会抛。
            return Package.Current.Dependencies;
        }
        catch (Exception)
        {
            return Array.Empty<Package>();
        }
    }

    /// <summary>进程架构（X64 / Arm64 / X86 …），读不到返回 <c>unknown</c>。</summary>
    private static string Architecture()
    {
        try
        {
            return Package.Current.Id.Architecture.ToString().ToLowerInvariant();
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    /// <summary>把框架包全名压成短别名，避免每行都被 <c>Microsoft.</c> 前缀撑爆。</summary>
    private static string Alias(string fullName) =>
        fullName.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal) ? "winui2"
        : fullName.Contains("VCLibs", StringComparison.Ordinal) ? "vcLibs"
        : fullName;

    /// <summary><c>PackageVersion</c> 的四段式字符串（如 <c>8.2511.26001.0</c>）。</summary>
    private static string Format(PackageVersion v) =>
        $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
}
