using Reactor.Template.Services;
using Reactor.Uwp.Hosting;
using Windows.ApplicationModel.Activation;

namespace Reactor.Template;

// 入口：没有 App.xaml，手写 Main；manifest 的 EntryPoint 指向这个类。
// WinUI 2 资源加载、窗口挂载、重渲染调度都由 ReactorApplication<TRoot> 完成。
// 标题栏延伸（ExtendViewIntoTitleBar）与按钮配色也是宿主做的，不用自己写。
//
// 换自己的根组件：把 MainPage 改成你的组件类型即可。
// 主题 / 背景材质在 MainPage（.Backdrop）和 Services/AppSettings.cs 里，
// 都是"改一处就生效"的写法，不用在这里初始化。
public sealed partial class App : ReactorApplication<MainPage>
{
    public static void Main(string[] args) =>
        Windows.UI.Xaml.Application.Start(_ => new App());

    /// <summary>
    /// 唯一必须抢在 <c>base.OnLaunched</c> 前面做的事：把持久化的"控件声音"
    /// 落到 XAML 的元素音效开关上。
    /// </summary>
    /// <remarks>
    /// <c>base.OnLaunched</c> 里会 <c>new ReactorHost(...)</c> 把整棵界面树造出来，
    /// 那之后再设 <c>ElementSoundPlayer.State</c> 就不响了——控件模板已经按旧值
    /// 把 <c>ElementSoundMode</c> 定死。原因和类型位置都写在
    /// <see cref="ElementSound"/> 的注释里。
    /// <para>
    /// 参考模板的对应位置是 <c>App.OnLaunched</c> 里的
    /// <c>AppThemeManager.LoadSettings()</c>——在 <c>rootFrame.Navigate</c> 之前。
    /// </para>
    /// </remarks>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ElementSound.Apply(AppSettings.Current.Sound);

        base.OnLaunched(args);
    }
}
