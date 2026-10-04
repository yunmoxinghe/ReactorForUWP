# Reactor.Uwp

UWP + WinUI 2 的声明式 UI 框架：用 C# 描述界面，不写 XAML。

- **仓库**：https://github.com/yunmoxinghe/ReactorForUWP
- **包**：https://www.nuget.org/packages/Reactor.Uwp
- **问题反馈**：https://github.com/yunmoxinghe/ReactorForUWP/issues
- **许可**：MIT

仓库里各目录的用途（这个包只含 `Reactor.uwp/`）：

| 路径 | 是什么 |
|---|---|
| `Reactor.uwp/` | 框架本体，就是本包 |
| `Reactor.Uwp.Native/` | C++/WinRT 原生桥源码 + x64 / arm64 预编译产物 |
| `UwpApp/` | 测试壳：压测 M0~M5、虚拟列表 / Echo 实验室等，手动验证用 |
| `tests/` | 控制台用例，`dotnet run` 即跑 |
| `diag-run.ps1` | 无人值守压测脚本，带 `-Mode` |

## 路线

**映射 WinUI 2，不是内置控件树。** 元素最终都落成真实的 `Windows.UI.Xaml` /
`Microsoft.UI.Xaml`（WinUI 2）控件：样式、输入法、无障碍、性能全部是原生的。
代价是被 WinUI 2 的能力边界卡住——它缺的控件这里也缺。

## 兼容性契约（两条硬规则）

1. **允许、并且要求与官方 Reactor（WinUI 3 版 `microsoft-ui-reactor`）互兼容。**
   `Component` / `Element` / `Render()` / `UseState` / `UseEffect` / `UseRef` /
   `Context` / `Component<TProps>` / `Factories` 的工厂方法，命名与参数顺序都按官方来。
   判据：一份组件代码从官方 Reactor 搬过来（或搬回去），只换 `using` 与元素命名空间，
   业务逻辑一行不改。

2. **实现一律套 WinUI 2。** 真控件、真行为、真资源：
   - 出来的节点是 `Windows.UI.Xaml.*` / `Microsoft.UI.Xaml.*`，不是自绘拼装；
   - 行为调官方那个 API（返回导航是 `Frame.GoBack()`，不是 `Navigate` 回上一页）；
   - `ThemeResource.Brush()` 给的是**活引用**：切主题时颜色跟着变，
     不是渲染那一刻抄下来的快照（`SolidColorBrush` 按资源键共享，主题变化时统一改 `Color`）。

不满足其中一条就是替代实现，注释里必须写明为什么不走官方路径。

`VirtualizingList` 走的就是官方那条路：`ItemsRepeater` + 原生元素工厂
（WinUI 2 把 `IElementFactory` 标成 internal、C# 实现不了，由
`Reactor.Uwp.Native.dll` 补上）。原生桥加载失败时（产物没落到 AppX）自动回退到自绘
（ScrollViewer + Canvas）并 Trace 一行；`ItemKey` 只在回退路径上生效。
注：项目只支持 x64 / arm64，**不做 x86**，两个架构都有原生产物。

**对齐清单当前全绿**：导航与返回走 `Frame.GoBack()` + 真 BackStack；受控属性走
`Internal/EchoGuard.cs`（`Expect` 登记 / `Consume` 匹配即吞 / `Forget` 清理，7 处接好），
对应官方 `Controlled<TValue, TArgs>` + counter-echo；虚拟化走 `ItemsRepeater` +
原生元素工厂；主题资源是活引用。

`EchoGuard` 有一条语义别改回去：**只有匹配成功才消费登记**。TextBox 的粘贴 /
IME / selection replacement 会连发多个 `TextChanged`，无条件删除登记会让第二发
被当成用户输入 → 回调 → setState → 重渲染把刚粘进去的文本覆盖掉。

## 安装

```xml
<PackageReference Include="Reactor.Uwp" Version="0.1.0-alpha.5" />
```

消费方项目要求（与本机工程一致）：

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
  <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
  <UseUwp>true</UseUwp>
  <!-- alpha.4 起可写 x64;arm64；更早的包只有 win-x64 的原生桥 -->
  <Platforms>x64;arm64</Platforms>
  <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
</PropertyGroup>
```

包自带 `Microsoft.UI.Xaml 2.8.7` 与 `CommunityToolkit.Uwp.Controls.SettingsControls`
依赖，不用另外装。

## 用法

```csharp
using Microsoft.UI.Reactor.Core;
using Reactor.Uwp.Hosting;
using static Microsoft.UI.Reactor.Factories;

// 入口：没有 App.xaml，手写 Main 启动
public sealed partial class App : ReactorApplication<CounterPage>
{
    public static void Main(string[] args) =>
        Windows.UI.Xaml.Application.Start(_ => new App());
}

public sealed class CounterPage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(
            TextBlock($"Count: {count}"),
            HStack(
                Button("-", () => setCount(count - 1)),
                Button("+", () => setCount(count + 1))));
    }
}
```

manifest 里把入口指向这个类：

```xml
<Application Id="App" Executable="$targetnametoken$.exe" EntryPoint="MyApp.App" />
```

组件用 `Render()` 描述一次界面，之后状态变化由框架 diff 出最小改动打到真实控件上。
`UseState` / `UseEffect` / `Component<TProps>` / `Provide-Context` 都有。

直接操作原生控件用 `Native(Func<UIElement> factory)` 逃生舱：控件自己造，
框架只负责放进布局和卸载时收走（库还没包住的控件走这条路）。

## 已知限制（alpha）

- **只支持 x64 / arm64，不做 x86**：原生桥带这两个架构的预编译产物。
  `Reactor.Uwp.Native.dll` 用于给 `ItemsRepeater` 提供 C# 实现的
  `IElementFactory`（WinUI 2 的 C# 投影把该接口标成了 `internal`，只能从原生侧补）。
  两个架构都随包分发在 `runtimes\win-x64\native\` 与 `runtimes\win-arm64\native\`，
  由包内的 `build\Reactor.uwp.targets` 复制到真正会加载它的 `AppX\` 目录。
  缺失架构不打包——那种情况下自定义工厂不可用，回退自绘，其余功能不受影响。
- **WinUI 2 的能力边界就是本框架的边界**：它没提供的控件（如完整的
  `TabView` / `SplitView` 封装）需要走 `Native()` 或自己补。
  键盘可达性与 `AutomationProperties` 已经照 XAML 同名属性补齐（见
  `Elements/ElementExtensions.Input.cs`）；`x:Uid` 也接了
  （`Internal/Localization.cs`），但只能覆盖"有本地化意义"的那几个属性
- API 尚未稳定，minor 版本内可能变。

## 状态

首个 alpha。已验证的核心链路：纯 C# 启动与 WinUI 2 资源加载、元素 diff/patch、
Frame 导航与过渡、设置页（SettingsCard / SettingsExpander）、
ItemsRepeater 虚拟化（含回收不变量校验）。
未做：NuGet 上的正式版、xml 文档。

## 发布（Trusted Publishing）

包通过 GitHub Actions 发布，走 nuget.org 的 Trusted Publishing（OIDC），
仓库里不存 API key。工作流见 `.github/workflows/publish.yml`，
手动触发或推 `v*` tag 均可。

nuget.org 侧的策略（`Account → Trusted Publishing`）需要四个值：

| 字段 | 值 |
|---|---|
| Repository Owner | `yunmoxinghe` |
| Repository | `ReactorForUWP` |
| Workflow File | `publish.yml`（只填文件名，不带路径） |
| Environment | 留空 |

仓库侧只留一个 secret：`NUGET_USER` = nuget.org 的**用户名** `yunmoxing`
（不是邮箱，也不是 GitHub 上的 `yunmoxinghe`）。

两点提醒：策略是**按包所有者**生效的，不限于单个包 id，
所以 scope 建议用 glob 限定到 `Reactor.Uwp`；
私有仓库的策略初次只有 7 天临时激活期，
首次成功登录（不必真的推包）后才会永久绑定 GitHub 的 repo/owner ID。
