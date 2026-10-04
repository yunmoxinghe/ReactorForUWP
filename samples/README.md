# 示例

两个独立的 UWP 应用，**都只装 NuGet 包，不引用仓库里的框架源码**：

```xml
<PackageReference Include="Reactor.Uwp" Version="0.1.0-alpha.3" />
```

这样写是有意的——示例能编译通过，就说明**包里导出的公共 API 真的够用**。
用 `ProjectReference` 指向 `Reactor.uwp` 会掩盖导出问题（internal 的也能用到，
`UwpApp` 就是靠 `InternalsVisibleTo` 用了 `EchoStats`）。

| 项目 | 用途 |
|---|---|
| `Reactor.Template/` | **起点模板**：一个 App + 一个计数器页，新项目从这里复制 |
| `Reactor.Gallery/` | **示例画廊**：8 个主题页，查某种写法时来这里找 |

两个项目都开着 `PublishAot`（UWP + net10 的 AOT 用平台自带编译链，不额外依赖什么），
只列 **x64**——包的原生桥目前只有 win-x64 预编译产物。

## 本机吃到的是哪个包

**示例必须吃 nuget.org 上的包**，否则"能编译"证明不了任何事。但全局包缓存
（`%USERPROFILE%\.nuget\packages\`）里同名版本一旦被本地包占住，restore 就直接命中它，
再也不会去线下载——本机于是悄悄用上了本地产物，而别人 clone 下来还原不了。

看一眼就知道现在吃的是哪份：

```powershell
Get-Content "$env:USERPROFILE\.nuget\packages\reactor.uwp\<版本>\.nupkg.metadata"
```

`source` 是 `https://api.nuget.org/v3/index.json` 才对；如果是某个本地目录，说明被污染了。
切回在线包：

```powershell
Remove-Item "$env:USERPROFILE\.nuget\packages\reactor.uwp\<版本>" -Recurse -Force
dotnet restore samples/Reactor.Template/Reactor.Template.csproj
```

**验证还没发布的版本时，别让它进全局缓存**——加 `--packages` 指到独立目录：

```bash
dotnet restore samples/Reactor.Template/Reactor.Template.csproj --packages .workbuddy/tmp-pkgs
```

（本地 `dotnet pack` 出来的包想拿给示例试，就临时把 `bin\x64\Release` 当源加上；
用完记得把版本目录从全局缓存里删掉。）

## 怎么跑

UWP 不能 `dotnet run`，要部署：

- **VS**：打开 `ReacrorForUWP.slnx`，把要跑的那个设为启动项目，选 `x64`，F5
- **VS 不部署时的兜底**：`powershell -ExecutionPolicy Bypass -File samples/run.ps1 -Project Template`
  （`-Project Gallery` 跑另一个；脚本补 Assets → 松散注册 → 按包身份激活）
- **命令行只能验编译，不能验运行**：
  ```bash
  dotnet build samples/Reactor.Template/Reactor.Template.csproj -c Debug -p:Platform=x64
  ```

> **`dotnet build` 会毁掉 VS 生成的包布局。** MSIX 那一步（生成 `AppxManifest.xml` /
> `resources.pri` / `Microsoft.UI.Xaml.winmd`）只在 VS 的 MSBuild 里跑，CLI 不参与；
> 而 CLI 的增量清理反过来会把 VS 生成的那些文件删掉。
> 所以别在 VS 跑过之后再用 CLI 构建同一个示例——要验编译就验完再回 VS 重新生成。

开 AOT 后构建是 RID 感知的，输出落在 `bin\x64\Debug\<tfm>\win-x64\`，
部署用的原生桥在 `win-x64\AppX\Reactor.Uwp.Native.dll`。

### 没跑起来先看这里：`0xc0000409` 不是框架的锅

**直接双击 / 调试 `win-x64\*.exe` 必崩**：进程没有 AppX 包身份，`Windows.UI.Xaml.dll`
会直接 fail-fast，退出码 `0xc0000409`（fast-fail 子码 7 = `FAST_FAIL_FATAL_APP_EXIT`），
WER 里 faulting module 是 `Windows.UI.Xaml.dll`、偏移 `0x348aae`，
托管堆栈和 `UnhandledException` 都拦不到——看着像框架崩了，其实只是没打包身份。

判定办法：WER 的 "Faulting package full name" 是空的 = 没身份；
或者拿能跑的 `UwpApp` 从 `win-x64\UwpApp.exe`（而不是 `win-x64\AppX\` 里那个）启动，
同样 `0xc0000409`。

所以 F5 崩了先确认**部署真的发生了**——查 AppX 部署日志最直接：

```powershell
Get-WinEvent -LogName 'Microsoft-Windows-AppXDeploymentServer/Operational' -MaxEvents 40 |
  Select-Object TimeCreated, Id, Message
```

只要没有针对本包 Identity 的 `Register` / `Add` 事件，就说明 VS 压根没部署，
只是把 bin 里的 exe 拉起来了（`.slnx` 里 Deploy 是勾上的，所以通常是别的原因）。

**最常见的挡路石：缺 `Properties/launchSettings.json`。** 这是 VS 决定"F5 怎么启动"
的启动配置文件，里面必须写 `"commandName": "MsixPackage"`，VS 才会走
"构建 → 部署 MSIX 包 → 按包身份激活"。没有这个文件（或用 `commandName: Project`），
F5 退化成"直接执行 bin 里的裸 exe"，于是没有包身份、XAML 立刻 fail-fast:

```json
{
  "profiles": {
    "Reactor.Template": { "commandName": "MsixPackage" }
  }
}
```

仓库里 `UwpApp` 能 F5 就是因为它的 `Properties/launchSettings.json` 写了这一行；
两个示例原先没有这个文件，这就是它们一直 `0xc0000409` 而 UwpApp 没事的全部原因。
VS 里也可以自己配：项目属性 → 调试 → 启动配置文件（或工具栏 Run 下拉框里选带
MSIX 的那个，别选 `commandName: Project` 那个）。

其它该查的：

- **工具栏顶部的 Solution Platform 要选 x64**。官方文档特意提醒：要用顶部那个
  "Active solution platform" 下拉框，而不是 Configuration Manager 里 Deploy 复选框
  旁边那个。平台不对时 VS 会报 "The project needs to be deployed. Please enable
  Deploy in the Configuration Manager"，但那通常不是 Deploy 没勾。

- "输出"窗口（显示：生成）里有没有 `DEPxxxx` 报错
- 包注册上了没：
  ```powershell
  Get-AppxPackage -Name "21CE1A0C-1D93-4A28-AE85-B632189C9236"   # Template
  Get-AppxPackage -Name "E362DB2E-EDAD-4411-B1F6-6355F8CFB661"   # Gallery
  ```

注意松散注册后再让 VS 部署会因同 Identity 冲突失败，先卸掉：
`Get-AppxPackage -Name <Identity> | Remove-AppxPackage`。（`run.ps1` 会自己先卸。）

## Gallery 里有什么

| 页面 | 看点 |
|---|---|
| 快速开始 | `UseState` + 事件 + `When` / `If` 条件渲染，最小可运行形态 |
| 输入与选择 | TextBox / ComboBox / ToggleSwitch / CheckBox / Slider / RadioButtons，`Optional<T>` 受控写法 |
| 布局 | `Grid` 行列（Auto / 星号）、`Border`、对齐与间距 |
| 列表 | `ListView` / `GridView` / `ForEach`，项数少时用这个 |
| 虚拟化长列表 | 5000 项 `VirtualizingList`，重点是 `itemKey`（插入/删除不错位） |
| 设置页 | SettingsCard / SettingsExpander / Expander / ContentDialog |
| 组件 props | `Component<TProps>` 父子传值，record 当 props |
| 原生控件逃生舱 | `Native()`：包还没包住的控件（NumberBox）怎么挂进来 |

## 抄代码时注意

- **入口没有 App.xaml**：`App.cs` 手写 `Main`，manifest 的 `EntryPoint` 指向它
- **`VirtualizingList` 的 `itemKey` 别省**：数据源会插入 / 删除 / 移动时，不给稳定身份
  就会因为下标位移而拿错内容（Gallery 的虚拟化页有对照按钮）
- **`Native()` 的创建委托要写成字段**：引用必须稳定，只有 `Token` 变化才重建控件
- **别急着关 AOT**：它能第一时间暴露反射 / 运行时代码生成这类写法；
  真需要反射就加 `rd.xml` 或 `[DynamicallyAccessedMembers]`
