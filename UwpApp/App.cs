using System;
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace UwpApp;

// 纯 C# 入口：manifest 的 EntryPoint="UwpApp.App" 激活这个无 XAML 的类，
// WinUI 2 引导与窗口挂载全部由 ReactorApplication<TRoot> 完成。
// 没有 App.xaml 就没有 XAML 编译器生成的 Main，这里手写等价入口。
public sealed partial class App : ReactorApplication<TestShellApp>
{
    // 等价于经典 UWP App.g.cs 中生成的入口：启动 XAML 框架并创建 Application 实例，
    // 随后系统回调 OnLaunched。
    //
    // 根组件固定为 TestShellApp：所有测试 / 演示都在里面的菜单里选，
    // 不要再为了换一个页面改这里（改一次就要重编 + 重部署一轮）。
    //
    // 标题栏扩展（ExtendViewIntoTitleBar）由宿主在 OnLaunched 里统一处理，
    // 页面里用 .TitleBar() 指定拖拽区、用 .OwnsTitleBar() 接管顶部布局。
    public static void Main(string[] args)
    {
        Windows.UI.Xaml.Application.Start(_ => new App());
    }

    /// <summary>
    /// 抢在 <c>base.OnLaunched</c> 之前把持久化的"控件声音"落到 XAML 的元素音效开关。
    /// </summary>
    /// <remarks>
    /// <c>base.OnLaunched</c> 里会 <c>new ReactorHost(...)</c> 建出整棵界面树，
    /// 之后设 <c>ElementSoundPlayer.State</c> 就不响了。参考模板的对应位置是
    /// <c>App.OnLaunched</c> 里的 <c>AppThemeManager.LoadSettings()</c>。
    /// </remarks>
    protected override void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs args)
    {
        UwpApp.Services.ElementSound.Apply(UwpApp.Services.AppSettings.Current.Sound);
        ApplyTraceSwitch();

        base.OnLaunched(args);
    }

    /// <summary>
    /// 无人值守排查用的日志开关：<c>LocalState\trace-input.txt</c> 存在就把
    /// <c>Input</c> 通道降到 <c>Trace</c> 并落盘。
    /// </summary>
    /// <remarks>
    /// 为什么要它：闸门的"吞掉"是 <c>Trace</c> 级、不落盘，真机上只看得见放行、
    /// 看不见被吞——而"点了没反应"恰恰在被吞那一侧。UIA 脚本靠这个文件取证，
    /// 排完删掉文件就回到默认（<c>PersistTrace</c> 默认关）。
    /// <para>
    /// 只开 <c>Input</c> 一个通道：全局降到 Trace 会把每帧都打进文件，把要看的那几行淹掉。
    /// </para>
    /// </remarks>
    private static void ApplyTraceSwitch()
    {
        try
        {
            // 同步判存在即可：LocalFolder.Path 是可直接 stat 的真实路径
            // （ReactorLog 落盘用的就是它）。TryGetItemAsync 在这儿的同步上下文里
            // 拿不到结果，别用。
            // 文件<b>内容</b>是通道名列表（逗号/空格分隔，如 <c>Input,Patch</c>）。
            // 空文件 = 全通道降到 Trace（会很吵，只在小窗口里这么开）。
            var path = System.IO.Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path, "trace-input.txt");
            if (!System.IO.File.Exists(path))
            {
                return;
            }

            var spec = System.IO.File.ReadAllText(path).Trim();
            var names = spec.Length == 0
                ? null
                : spec.Split(new[] { ',', ' ', ';' }, System.StringSplitOptions.RemoveEmptyEntries);

            foreach (var channel in System.Enum.GetValues<ReactorLogChannel>())
            {
                if (names is null || names.Contains(channel.ToString(),
                                                   System.StringComparer.OrdinalIgnoreCase))
                {
                    ReactorLog.SetChannel(channel, ReactorLogLevel.Trace);
                }
            }

            ReactorLog.PersistTrace = true;
            ReactorLog.Info(ReactorLogChannel.Host, $"[trace-switch] Trace 落盘已开：{spec}");
        }
        catch
        {
            // 诊断开关读不到就按默认走，不影响启动。
        }
    }
}
// 第一个验收组件：纯 C# 描述 UI，含一个 WinUI 2 控件（InfoBar）
// 验证 XamlControlsResources 纯代码加载链路。
public sealed class CounterPage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(
            InfoBar($"计数器已启动，当前值：{count}"),
            TextBlock($"Count: {count}"),
            HStack(
                Button("-", () => setCount(count - 1)),
                Button("+", () => setCount(count + 1))
            )
        );
    }
}
