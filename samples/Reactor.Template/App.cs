using Reactor.Uwp.Hosting;

namespace Reactor.Template;

// 入口：没有 App.xaml，手写 Main；manifest 的 EntryPoint 指向这个类。
// WinUI 2 资源加载、窗口挂载、重渲染调度都由 ReactorApplication<TRoot> 完成。
//
// 换自己的根组件：把 MainPage 改成你的组件类型即可。
public sealed partial class App : ReactorApplication<MainPage>
{
    public static void Main(string[] args) =>
        Windows.UI.Xaml.Application.Start(_ => new App());
}
