using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsUIApplication = Windows.UI.Xaml.Application;

namespace Reactor.Uwp.Hosting;

/// <summary>
/// 纯 C# 的 UWP 应用入口基类：替代 App.xaml + code-behind。
/// 负责 WinUI 2 运行时引导（XAML 元数据提供程序 + 控件资源字典），
/// 然后把根组件挂到当前窗口。
/// </summary>
public abstract partial class ReactorApplication : WindowsUIApplication,
    Windows.UI.Xaml.Markup.IXamlMetadataProvider
{
    /// <summary>
    /// MUX 控件的 XAML 元数据提供程序。没有 XAML 编译器生成的 App.g.i.cs 时，
    /// WUX 框架在加载/套用 MUX 控件（如 InfoBar）时会向 Application 查询
    /// IXamlMetadataProvider；不实现则所有 MUX 控件首次布局即原生崩溃
    /// （0xc000027b / stowed E_FAIL），窗口只剩占位叉图。
    /// </summary>
    private readonly Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider _xamlMetadata =
        new();

    /// <inheritdoc/>
    public Windows.UI.Xaml.Markup.IXamlType GetXamlType(Type type) =>
        _xamlMetadata.GetXamlType(type);

    /// <inheritdoc/>
    public Windows.UI.Xaml.Markup.IXamlType GetXamlType(string fullName) =>
        _xamlMetadata.GetXamlType(fullName);

    /// <inheritdoc/>
    public Windows.UI.Xaml.Markup.XmlnsDefinition[] GetXmlnsDefinitions() =>
        _xamlMetadata.GetXmlnsDefinitions();

    /// <summary>创建根组件。</summary>
    protected abstract Component CreateRootComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            BootstrapWinUi2();

            // 窗口关闭时显式退出进程：纯代码宿主缺少标准 App.xaml 的 PLM 生命周期兜底，
            // 不退出会导致每次运行都残留僵尸进程，日积月累锁死 AppX\clrjit.dll（DEP0500）。
            // 注意：CoreApplication.Exit 不能在激活事件处理器里直接调用，这里是在 Closed 回调里调用。
            Window.Current.Closed += (_, _) => CoreApplication.Exit();

            var host = new ReactorHost(CreateRootComponent());
            Window.Current.Content ??= host.Root;
            Window.Current.Activate();
        }
        catch (Exception ex)
        {
            Trace("FATAL: " + ex);
            throw;
        }
    }

    /// <summary>
    /// 纯代码加载 WinUI 2：
    /// 1) 初始化 XamlControlsXamlMetaDataProvider；
    /// 2) 合并 XamlControlsResources —— 等价于 App.xaml 里的
    ///    &lt;XamlControlsResources/&gt;。
    /// 必须在 OnLaunched（而非构造函数）中执行，且早于任何 WinUI 控件创建。
    /// </summary>
    private void BootstrapWinUi2()
    {
        Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider.Initialize();

        if (!Resources.MergedDictionaries.Any(d => d is XamlControlsResources))
        {
            Resources.MergedDictionaries.Add(new XamlControlsResources());
        }
    }

    /// <summary>
    /// 启动期诊断日志，落到 LocalState\reactor-startup.log。
    /// stowed exception (0xC000027B) 不会带托管堆栈，只能靠落盘定位。
    /// 注意：可在任意线程调用；非 UI 线程访问 ApplicationData 可能失败，此时降级为 Debug 输出。
    /// </summary>
    public static void Trace(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{System.Threading.Thread.CurrentThread.ManagedThreadId}] {message}{Environment.NewLine}";

        // 先尝试写调试输出（任何线程都安全）
        System.Diagnostics.Debug.WriteLine(line);

        // 再尝试写文件（可能因线程上下文失败，但不影响进程存活）
        try
        {
            var path = Path.Combine(
                ApplicationData.Current.LocalFolder.Path, "reactor-startup.log");
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var sw = new StreamWriter(fs);
            sw.Write(line);
            sw.Flush();
        }
        catch
        {
            // 静默失败：Debug.WriteLine 已输出，文件写不进去也不崩溃
        }
    }
}

/// <summary>
/// 泛型便捷入口：应用只需写
/// <c>sealed class App : ReactorApplication&lt;MyRootComponent&gt; { }</c>。
/// </summary>
public abstract partial class ReactorApplication<TRoot> : ReactorApplication
    where TRoot : Component, new()
{
    protected sealed override Component CreateRootComponent() => new TRoot();
}
