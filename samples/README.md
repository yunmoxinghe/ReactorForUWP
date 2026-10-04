# 示例

两个独立的 UWP 应用，**都只装 NuGet 包，不引用仓库里的框架源码**：

```xml
<PackageReference Include="Reactor.Uwp" Version="0.1.0-alpha.2" />
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

## 怎么跑

UWP 不能 `dotnet run`，要部署：

- **VS**：打开 `ReacrorForUWP.slnx`，把要跑的那个设为启动项目，选 `x64`，F5
- **命令行只验证编译**：
  ```bash
  dotnet build samples/Reactor.Template/Reactor.Template.csproj -c Debug -p:Platform=x64
  dotnet build samples/Reactor.Gallery/Reactor.Gallery.csproj -c Debug -p:Platform=x64
  ```

开 AOT 后构建是 RID 感知的，输出落在 `bin\x64\Debug\<tfm>\win-x64\`，
部署用的原生桥在 `win-x64\AppX\Reactor.Uwp.Native.dll`。

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
