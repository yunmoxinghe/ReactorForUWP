using Reactor.Uwp.Hosting;

namespace Reactor.Gallery;

// 没有 App.xaml，手写入口：manifest 的 EntryPoint="Reactor.Gallery.App" 激活这个类，
// WinUI 2 资源加载与窗口挂载由 ReactorApplication<TRoot> 完成。
public sealed partial class App : ReactorApplication<SampleShell>
{
    public static void Main(string[] args) =>
        Windows.UI.Xaml.Application.Start(_ => new App());
}
