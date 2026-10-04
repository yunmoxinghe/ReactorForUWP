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
