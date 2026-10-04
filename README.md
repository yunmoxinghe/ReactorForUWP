# ReactorForUWP

[![publish](https://github.com/yunmoxinghe/ReactorForUWP/actions/workflows/publish.yml/badge.svg)](https://github.com/yunmoxinghe/ReactorForUWP/actions/workflows/publish.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Reactor.Uwp)](https://www.nuget.org/packages/Reactor.Uwp)

UWP + WinUI 2 的声明式 UI 框架：用 C# 描述界面，不写 XAML。
框架本体以 NuGet 包 **[Reactor.Uwp](https://www.nuget.org/packages/Reactor.Uwp)** 发布，
这个仓库是它的源码 + 测试载体。

**路线是映射 WinUI 2，不是内置控件树**：元素最终落成真实的
`Windows.UI.Xaml` / `Microsoft.UI.Xaml`（WinUI 2）控件，样式、输入法、无障碍、
性能全部是原生的；代价是被 WinUI 2 的能力边界卡住。

## 兼容性契约（两条硬规则）

写代码前先看这两条，它们是本仓库的判定标准，不是口号：

1. **允许、并且要求与官方 Reactor（WinUI 3 版 `microsoft-ui-reactor`）互兼容。**
   API 的形状跟官方对齐：`Component` / `Element`（不可变 record）/ `Render()` /
   `UseState` / `UseEffect` / `UseRef` / `Context` / `Component<TProps>` /
   `Factories` 里的工厂方法名与参数顺序都按官方来。
   **判据**：一份组件代码从官方 Reactor 搬到本框架（或反过来），只需要换
   `using` 与元素所在命名空间，业务逻辑一行都不用改。

2. **实现一律套 WinUI 2，不另起炉灶。**
   每个元素落成真控件，改外观走官方属性 / `Style` / `ItemTemplate`，取资源走官方
   资源解析，行为调官方 API。
   **判据三条**：
   - **真控件**——`VisualTreeHelper` 走出来的节点类型是 `Windows.UI.Xaml.*` 或
     `Microsoft.UI.Xaml.*`，不是我们自己画的 `StackPanel` 拼装。
   - **真行为**——调官方那个 API，而不是"模拟得差不多"。返回导航就是
     `Frame.GoBack()`（不是 `Navigate` 到上一页），音效就是
     `ElementSoundPlayer.Play(ElementSoundKind.GoBack)`。
   - **真资源**——主题资源是活引用（`{ThemeResource}` 的语义，切主题要跟着变），
     不是"渲染那一刻抄一份颜色下来"。

三条里任何一条不满足，就是**替代实现**，必须在代码注释里写明为什么不走官方路径，
否则视为待修的债。

`VirtualizingList` 以前是最大的一处替代实现（自绘 `ScrollViewer` + `Canvas`），
原因是 WinUI **2** 把 `IElementFactory` 标成 internal、C# 侧实现不了；
原生桥 `Reactor.Uwp.Native.dll` 补上之后已改回官方路径：
`ItemsRepeater` + 原生元素工厂。**原生桥加载失败时（产物没落到 AppX）自动回退自绘**
并 Trace 一行——那是部署防御，不是架构分支：项目只支持 x64 / arm64，两个架构都有
原生产物。实测（5000 项）只 realize 48 个容器，虚拟化是生效的。
`ItemKey` 在 `ItemsRepeater` 那条路上不起作用（WinUI 2 的 ItemsRepeater 不做按 key
复用，官方 Reactor 同样如此），只在自绘回退路径上生效。

**对齐清单当前全绿**：

| 项 | 落在哪 | 官方对应 |
|---|---|---|
| 页面导航与返回 | `Frame.Navigate` / `Frame.GoBack()` + 真 BackStack，返回音效 `ElementSoundPlayer.Play(ElementSoundKind.GoBack)` | 同 |
| 受控属性的回声抑制 | `Internal/EchoGuard.cs`（`Expect` 登记期望值 / `Consume` 匹配即吞 / `Forget` 卸载清理），已在 Text / Check / Value / Selection / Toggle 等 7 处接好 | `Controlled<TValue, TArgs>` + counter-echo |
| 虚拟化长列表 | `ItemsRepeater` + 原生元素工厂 | 同 |
| 主题资源 | `ThemeResource` 活引用（`SolidColorBrush` 按键共享，切主题统一改 `Color`） | `{ThemeResource}` 语义 |

已经修掉的那几处留着当反面教材（注释里都写了实证）：面包屑曾用自绘
`StackPanel` + "›"（→ 现为真 `BreadcrumbBar` + `ItemTemplate`）；条目外观曾"把字号设到
`BreadcrumbBar` 上指望继承"（→ `BreadcrumbBarItem` 默认样式硬设了字号，继承链断）；
向量里曾直接塞 `UIElement`（→ 0x800F1000，UIElement 天生要占树上一个位置，当不了数据）。

```csharp
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

## 仓库里有什么

| 路径 | 是什么 |
|---|---|
| `Reactor.uwp/` | 框架本体，就是发布出去的那个包 |
| `Reactor.Uwp.Native/` | C++/WinRT 原生桥（给 `ItemsRepeater` 补 `IElementFactory`），含 x64 / arm64 预编译产物，`build.bat` 可重建 |
| `UwpApp/` | 测试壳：压测 M0~M5、虚拟列表 / Echo 实验室、CoreLoop 回归、元素画廊，一次部署点菜单跑完 |
| `tests/` | 控制台用例（EchoGuard、虚拟列表身份、Hook、Context），不需要开 App |
| `diag-run.ps1` | 无人值守压测：构建 → 同步产物 → 启动 → 等本轮跑完 → 打印 summary |
| `samples/` | 示例：`Reactor.Template`（起点模板）+ `Reactor.Gallery`（8 个主题页），都只装 NuGet 包不引用框架源码，见 `samples/README.md` |
| `tools/` | 辅助脚本 |

## 跑起来

环境：.NET 10 SDK、Windows 10 SDK `10.0.26100`、x64。
UWP 项目不能像普通控制台那样直接跑，要用 Visual Studio 打开
`ReacrorForUWP.slnx`，把 `UwpApp`（测试壳）或 `samples` 下的
`Reactor.Template` / `Reactor.Gallery` 设为启动项目部署运行（F5）。

```bash
# 只构建
dotnet build UwpApp/UwpApp.csproj -c Debug -p:Platform=x64
dotnet build samples/Reactor.Template/Reactor.Template.csproj -c Debug -p:Platform=x64
dotnet build samples/Reactor.Gallery/Reactor.Gallery.csproj -c Debug -p:Platform=x64

# 打框架包（别带 -p:Platform：会把托管程序集编成 x64 专属，arm64 消费方报 CS8012）
dotnet pack Reactor.uwp/Reactor.uwp.csproj -c Release
```

## 发布必须是 AOT

**这个项目的交付形态是原生 AOT，不是 IL。** `UwpApp` 与两个示例都开着
`<PublishAot>true</PublishAot>` + `<DisableRuntimeMarshalling>true</DisableRuntimeMarshalling>`，
所以任何"靠反射 / 运行时代码生成 / 动态加载程序集"的写法都会<b>在构建期</b>炸，
而不是等用户点上去才炸——这正是坚持 AOT 的理由。

```bash
dotnet publish UwpApp/UwpApp.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64
dotnet publish samples/Reactor.Template/Reactor.Template.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64
```

产物是单个原生 exe（本机 x64 实测 10.9 MB，`PE machine = 0x8664`，
同目录<b>没有</b>托管 `UwpApp.dll`——有就说明没编成原生）。
真要反射请显式加 `rd.xml` 或 `[DynamicallyAccessedMembers]`，别关 AOT。

已部署过一次之后，压测可以无人值守：

```powershell
.\diag-run.ps1 -Mode 1     # 0~5 = 压测 M0~M5，6/7 = 两个实验室，见 UwpApp\TestShell.cs
```

每轮的 `manifest / config / events.ndjson / summary.json` 归档在
`%LOCALAPPDATA%\Packages\<包>\LocalState\ReactorRuns\<runId>\`。

## 测试怎么跑

- **不开 App 的那部分**：`dotnet run --project tests/Reactor.Core.Tests -c Release`
- **要眼睛过一遍的那部分**：部署 `UwpApp`，左侧菜单直接切页。压测页会把
  ItemsRepeater 内嵌在页面里跑，页面上有进度行和「重跑 / 停止」

## 已知限制

- **只支持 x64 / arm64**：原生桥 `Reactor.Uwp.Native.dll` 有这两个架构的预编译产物
  （`build.bat` / `build.bat arm64` 可重建），**不做 x86**
- **WinUI 2 的能力边界就是本框架的边界**：它没封装的控件走 `Native()` 逃生舱或自己补。
  已暴露的元素见 `Reactor.uwp/Elements/Factories*.cs`；
  仍缺的（如 `TabView` / `TeachingTip` / `MenuBar`）随时可照现有元素加，
  一个元素 = 一个 record + 一个工厂 + 一个 handler + 一行注册
- **`x:Uid` 只覆盖"有本地化意义"的那几个属性**：`TextBlock.Text`、
  `TextBox` 的 `Text` / `Header` / `PlaceholderText`、
  `ContentControl.Content`、`ToolTip`、`AutomationProperties.Name`
  （清单在 `Internal/Localization.cs`）。XAML 编译器是<b>照 resw 里写了什么</b>
  生成赋值，纯代码没有那份清单，只能按类型试；需要别的属性时用 `Native()`
- API 尚未稳定；xml 文档还没有
- **测试集里没有一项碰真实 XAML 控件**（全是纯逻辑），真控件行为的回归靠
  `UwpApp` 手跑 + 每次改动后 AOT 发布一次

## 发布

包通过 GitHub Actions 走 nuget.org 的 **Trusted Publishing**（OIDC），
仓库里不存 API key。工作流在 `.github/workflows/publish.yml`，
手动触发或推 `v*` tag，详情见 `Reactor.uwp/README.md`。

## 许可

MIT，见 [LICENSE](LICENSE)。
