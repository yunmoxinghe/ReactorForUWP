using Reactor.Uwp.Hosting;
using Windows.ApplicationModel.Activation;

namespace Reactor.Gallery;

// 没有 App.xaml，手写入口：manifest 的 EntryPoint="Reactor.Gallery.App" 激活这个类，
// WinUI 2 资源加载与窗口挂载由 ReactorApplication<TRoot> 完成。
public sealed partial class App : ReactorApplication<SampleShell>
{
    public static void Main(string[] args) =>
        Windows.UI.Xaml.Application.Start(_ => new App());

    /// <summary>
    /// 唯一必须抢在 <c>base.OnLaunched</c> 前面做的事：把持久化的"控件声音"
    /// 落到 XAML 的元素音效开关上。
    /// </summary>
    /// <remarks>
    /// <b>时机是实测出来的，不能晚。</b><c>base.OnLaunched</c> 里会把整棵界面树造出来，
    /// 而控件模板在<b>应用那一刻</b>就按当时的 <c>ElementSoundPlayer.State</c>
    /// 把 <c>ElementSoundMode</c> 定死；树造完之后再改 <c>State</c>，
    /// 已经套好模板的那些控件不跟着变。
    /// <para>
    /// 这条是 <c>Reactor.Template</c> 的 <c>App.OnLaunched</c> / <c>ElementSound</c>
    /// 里记下的坑，位置也一致（参考模板是 <c>AppThemeManager.LoadSettings()</c>，
    /// 在 <c>rootFrame.Navigate</c> 之前）。两个示例对同一个 OS API
    /// 不该给出相反的示范，所以这里补上同一趟早期调用。
    /// </para>
    /// <para>
    /// 运行时的开关仍由 <see cref="SampleShell"/> 的 <c>UseEffect</c> 负责——两趟
    /// 覆盖的时间窗不同：这一趟管<b>已经套模板之前</b>的全局状态，那一趟管用户
    /// 之后的拨动。只留后者的话，刚进界面那一批控件是静音的，
    /// 现象就是"设置里显示开、点按钮没声"，而拨一下开关才恢复。
    /// </para>
    /// </remarks>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        SoundService.Apply(SettingsStore.LoadSound());

        base.OnLaunched(args);
    }
}
