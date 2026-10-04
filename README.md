# ReactorForUWP

[![publish](https://github.com/yunmoxinghe/ReactorForUWP/actions/workflows/publish.yml/badge.svg)](https://github.com/yunmoxinghe/ReactorForUWP/actions/workflows/publish.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Reactor.Uwp)](https://www.nuget.org/packages/Reactor.Uwp)

UWP + WinUI 2 的声明式 UI 框架：用 C# 描述界面，不写 XAML。
框架本体以 NuGet 包 **[Reactor.Uwp](https://www.nuget.org/packages/Reactor.Uwp)** 发布，
这个仓库是它的源码 + 测试载体。

**路线是映射 WinUI 2，不是内置控件树**：元素最终落成真实的
`Windows.UI.Xaml` / `Microsoft.UI.Xaml`（WinUI 2）控件，样式、输入法、无障碍、
性能全部是原生的；代价是被 WinUI 2 的能力边界卡住。

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
| `Reactor.Uwp.Native/` | C++/WinRT 原生桥（给 `ItemsRepeater` 补 `IElementFactory`），含 x64 预编译产物，`build.bat` 可重建 |
| `UwpApp/` | 测试壳：压测 M0~M5、虚拟列表 / Echo 实验室、CoreLoop 回归、元素画廊，一次部署点菜单跑完 |
| `tests/` | 控制台用例（EchoGuard、虚拟列表身份、Hook、Context），不需要开 App |
| `diag-run.ps1` | 无人值守压测：构建 → 同步产物 → 启动 → 等本轮跑完 → 打印 summary |
| `tools/` | 辅助脚本 |

## 跑起来

环境：.NET 10 SDK、Windows 10 SDK `10.0.26100`、x64。
UWP 项目不能像普通控制台那样直接跑，要用 Visual Studio 打开
`ReacrorForUWP.slnx`，把 `UwpApp` 设为启动项目部署运行（F5）。

```bash
# 只构建
dotnet build UwpApp/UwpApp.csproj -c Debug -p:Platform=x64

# 打框架包
dotnet pack Reactor.uwp/Reactor.uwp.csproj -c Release -p:Platform=x64
```

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

- **原生桥只有 x64 预编译产物**：x86 / arm64 下自定义 `IElementFactory` 不可用，
  其余功能不受影响
- **WinUI 2 的能力边界就是本框架的边界**：它没封装的控件走 `Native()` 逃生舱或自己补
- API 尚未稳定；xml 文档、多架构原生产物还没有
- AOT 发布在部分环境下没验成（`link.exe` 取不到 SDK 库路径），
  建议在 Developer PowerShell 里跑一次：
  `dotnet publish UwpApp/UwpApp.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64`

## 发布

包通过 GitHub Actions 走 nuget.org 的 **Trusted Publishing**（OIDC），
仓库里不存 API key。工作流在 `.github/workflows/publish.yml`，
手动触发或推 `v*` tag，详情见 `Reactor.uwp/README.md`。

## 许可

MIT，见 [LICENSE](LICENSE)。
