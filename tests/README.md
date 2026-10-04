# 测试

测试分三层，**入口都在这篇里**。挑哪一层看被测的东西能不能脱离 XAML 运行时：
能脱离的必须进第 1 层（改一行跑一次，不用部署），只有真控件行为才进第 2、3 层。

| 层 | 在哪 | 怎么跑 | 覆盖什么 |
|---|---|---|---|
| 1 · 控制台测试集 | `Reactor.Core.Tests/` | `.\tests\run.ps1`（或 `dotnet run --project tests/Reactor.Core.Tests -c Release`） | 纯逻辑：hook / 上下文 / 受控属性回声 / 虚拟化身份 / 真控件映射的文本构造。**不开 App、不部署、退出码非 0 即失败** |
| 2 · App 内测试壳 | `UwpApp/`（菜单见 `TestShell.cs`） | VS F5 部署，或 `.\diag-run.ps1 -Mode N` 无人值守 | 要真 XAML 才能验的：压测 M0~M5、虚拟列表 / Echo 实验室、CoreLoop 回归、元素画廊、XAML/代码 控件对照、设置页复现 |
| 3 · 示例 | `samples/` | 见 `samples/README.md` | 包消费方视角：`Reactor.Template`（起点模板）与 `Reactor.Gallery`（8 页）能编译、能跑，说明包导出的 API 够用 |

## 第 1 层：控制台测试集

```powershell
.\tests\run.ps1                 # Release，退出码 0 = 全过
dotnet run --project tests/Reactor.Core.Tests -c Release
```

没有 NuGet 依赖。框架里**纯逻辑**的源文件以 `Compile Link` 的方式编进来
（见 `Reactor.Core.Tests.csproj` 的 `ItemGroup`），所以它们和用例在同一个程序集里，
`internal` 也能直接断言。

现在的分组（`Program.cs` 里一行一组，加组就加一行）：

| 文件 | 锁的是什么 |
|---|---|
| `HookTests.cs` | `UseState` / `UseEffect` / `UseRef` 的时序与生命周期 |
| `ContextTests.cs` | `Provide` / `Consume` 的作用域与穿透 |
| `EchoGuardTests.cs` | 受控属性：自己写值引发的回声不能被当成用户输入 |
| `VirtualListTests.cs` | 虚拟化身份模型 **key ≠ index**（头部插入后已挂载项不拿错内容） |
| `BreadcrumbTemplateTests.cs` | 面包屑 `ItemTemplate` 的 XAML 文本：只喂数据（喂 UIElement 会 0x800F1000）、样式键转义 |
| `WeakTableTests.cs` | handler 静态状态必须是**弱键**：控件不可达就能被 GC 回收（`Unmount` 漏写也不泄漏） |

**往这一层加用例的前提**：被测代码不碰 `Windows.UI.Xaml`。碰了就抽——
把纯逻辑那一段单独成文件（像 `Internal/BreadcrumbTemplate.cs` 那样），
handler 和用例都引用它。这样"改一行跑一次"的反馈速度才能保住。

## 第 2 层：App 内测试壳（`UwpApp`）

一次部署点菜单跑完，不用为每次对照改代码重新编译。
菜单项与 `diag-run.ps1 -Mode N` 的 N 一一对应（`TestShell.cs` 的 `Cases` 数组顺序）：

| Mode | 页 | 看什么 |
|---|---|---|
| 0 | M0 XAML 基线 | 官方 `DataTemplate` 路径的基线 |
| 1 | M1 池化+折叠 | **要长期守住的那一档**（原生桥工厂 + 折叠） |
| 2~5 | M2 不复用 / M3 裸桥不折叠 / M4 裸桥折叠 / M5 最小回调 | 二分定位用的对照档 |
| 6 | 虚拟列表实验室 | 滚动、插入 / 删除、回收不变量 |
| 7 | Echo 实验室 | 受控属性的回声 |
| 8 | CoreLoop 回归 | 渲染循环 |
| 9~12 | 元素画廊 / A4 演示 / CoreLoop 演示 / Blank 模板页 | 观感与写法 |
| 13 | XAML/代码 控件对照 | **对齐用**：同一控件在 XAML 与代码里的一致性 |
| 14~15 | M1 / M0 慢滚对照 | 判定"快速滚动闪烁"是不是大跨步所致 |
| 16 | 设置页复现 | 冷启动自动进设置页，验整条路径不崩 |

无人值守跑一轮（构建 → 同步产物 → 启动 → 等本轮跑完 → 打印 summary）：

```powershell
.\diag-run.ps1 -Mode 16
```

每轮的 `manifest / config / events.ndjson / summary.json` 归档在
`%LOCALAPPDATA%\Packages\<包>\LocalState\ReactorRuns\<runId>\`。

## 约定

- **新 bug 先补第 1 层的用例**（能脱离 XAML 的部分），补不了才在第 2 层加菜单页
- 第 2 层每加一个菜单项，`diag-run.ps1` 的 Mode 表要跟着更新
- 第 2 层里名字带 Probe 的页（`XamlDiffProbe` / `SettingsNavProbe` / `FactoryProbePage`）
  是**测试资产**，不是产品代码：它们是"为什么这么写"的实证，注释里都写了结论。
  一次性定位用的东西（比如某个 bug 的二分探针）结论写进代码注释后就可以删
