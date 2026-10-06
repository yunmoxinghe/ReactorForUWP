---
name: uwp-agent-coding-practices
description: 给 AI coding agent 的 UWP/WinUI 2 开发规范，以及"官方那套 WinUI agent 规范在本仓库是反的"这条关键校正。用于让 AI 生成/评审本仓库（net10.0-windows10.0.26100.0 + UWP + WinUI 2.8.7）代码之前，或在给 agent 装插件/MCP/写 instructions 之前。也覆盖"应用内接 AI/agent 能力"的可用性边界与未验证清单。
agent_created: true
---

# 给 agent 的 UWP 开发规范 —— 先把方向拨正

> **一句话**：微软官方那套 Agentic AI 工具（Learn MCP、`winui@awesome-copilot` 插件、WinUI 3 instructions）
> 全部是 **UWP → WinUI 3** 方向的。本仓库是 **UWP + WinUI 2.8.7**，照套等于把正确的 UWP 代码改成错的。
> 装工具可以，**规则必须反向覆盖**（见 §2）。

本仓库实测栈（`Reactor.uwp` / `UwpApp` / `samples/*` 全部一致）：

```
TargetFramework            = net10.0-windows10.0.26100.0   ← UWP TFM，不是 net10.0-windows10.0.26100 的 WASDK 桌面
TargetPlatformMinVersion   = 10.0.19041.0
PackageReference           = Microsoft.UI.Xaml 2.8.7        ← WinUI 2（UWP 上的库控件），不是 WinUI 3
```

---

## 一、官方给 agent 供上下文的三件套（可用，但要知道它推你去哪）

| 手段 | 拿到什么 | 命令 / 端点 | 备注 |
|---|---|---|---|
| **Microsoft Learn MCP Server** | 官方 API 文档、文章、示例，直接喂给 agent | `https://learn.microsoft.com/api/mcp` | 免费、免鉴权。写进 MCP 配置后要手动 Trust 才生效 |
| **WinUI agent plugin** | 8 个 skill + `winui-dev` agent，覆盖 scaffold→build→run→test→package→migrate | Copilot CLI：`gh copilot plugin install winui@awesome-copilot`<br>Claude Code：`claude plugin marketplace add microsoft/win-dev-skills` → `claude plugin install winui@win-dev-skills` | 装到 `~\.copilot\installed-plugins\`。**不接 VS Code Copilot Chat** |
| **Windows App Development CLI** | 命令行建/跑/打包/签名/发布 | `winget install Microsoft.winappcli --source winget` | 插件的前置依赖 |

`winui-dev` 默认加载 `winui-design` + `winui-dev-workflow`，其余按需：

`winui-setup`（`/winui-setup` 手动触发，不自动加载）· `winui-dev-workflow` · `winui-design`（带 WinUI Gallery / CommunityToolkit 控件检索）· `winui-code-review` · `winui-ui-testing`（UI Automation 测试）· `winui-packaging`（MSIX + Store）· `winui-wpf-migration` · `winui-session-report`

**为什么需要它们（官方原话的实质）**：UWP 的训练数据（Stack Overflow、教程、sample）远多于 WinUI 3，
agent 默认会退回 UWP 写法 —— 所以插件用 instructions 把 `Microsoft.UI.Xaml` / `DispatcherQueue` / `ContentDialog`
这些"正确"答案钉进上下文。**这个偏置对我们是反的**：我们恰好要 UWP 写法。

---

## 二、反向对照表：官方规则 vs 本仓库该怎么写

左边是官方/插件会输出的"现代 WinUI 3 答案"；**右边才是本仓库要的**。写/审代码时按右列判。

| 场景 | 官方插件会写（WinUI 3） | **本仓库（UWP）要用** | 说明 |
|---|---|---|---|
| 控件命名空间 | `Microsoft.UI.Xaml.Controls.*` | 内置控件 `Windows.UI.Xaml.Controls.*`；**只有 WinUI 2 库控件**才 `Microsoft.UI.Xaml.Controls.*`（XAML 里习惯写 `muxc:` 前缀区分） | 两套并存，别做全局替换 |
| 派发线程 | `DispatcherQueue.TryEnqueue` | `CoreDispatcher.RunAsync(...)`（UWP 原生） | 两者 UWP 里都在；**不要为了"现代化"去改现有 `CoreDispatcher` 代码** |
| 对话框 | `ContentDialog` + **必设 `XamlRoot`**；禁用 `MessageDialog` | `MessageDialog`（`Windows.UI.Popups`）**合法可用**；`ContentDialog` **没有 `XamlRoot` 这个属性**，设了编译不过 | `XamlRoot` 是 WinUI 3 才引入的 |
| 窗口 | `AppWindow` / `App.MainWindow` 静态属性 | `Window.Current`、`CoreApplication.GetCurrentView()`、`ApplicationView.*`（`TryResizeView` 等） | `AppWindow` 在 UWP 里不存在 |
| 文件/文件夹 Picker | `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)` | **不需要**拿 HWND，直接 `await picker.PickSingleFileAsync()` | 那套 interop 是 WinUI 3 桌面（无 CoreWindow）的必需步骤 |
| 弹窗/Popup 定位 | `XamlRoot` / `AppWindow` 相关 | UWP 的 `Popup` / `Flyout` 依附可视树即可 | 同上 |
| 后台任务 | Windows App SDK 后台任务 API | `BackgroundTaskBuilder`（UWP 那套） | 打包模型不同，别混 |
| 应用生命周期 | WinUI 3 无 `Suspending`/`Resuming` 直映 | UWP 有完整 `Suspending` / `Resuming` | 移植方向相反时这条是"要人工审"的项 |
| MVVM | CommunityToolkit.Mvvm、x:Bind 编译绑定 | 同左，通用 | 这条两边一致 |

**一句话判据**：看到 agent 写 `XamlRoot`、`AppWindow`、`InitializeWithWindow`、`using Microsoft.UI.Xaml.Controls;` 覆盖内置控件 —— 先停下来对照这张表。

---

## 三、本仓库的 agent 工作循环（命令是定死的）

| 项目类型 | 构建/运行 | 说明 |
|---|---|---|
| **UWP 项目**（本仓库 `Reactor.uwp` / `UwpApp` / `samples/*`） | 可以直接 `dotnet build`（仓库里有 `diag-run.ps1`） | 例外见下 |
| **WinUI 3 / Windows App SDK 项目**（`DockedTools`、`ReactorTestApp`） | 走 `winapp run .`，**不要自己 `dotnet build`** | 用户的硬规则；要跑就派子代理或让他自己点 |

两条环境纪律（血泪，详见 `uwp-runtime-evidence-debugging`）：

- **AppX 目录只有部署时才刷新**，`dotnet build` 不动 `bin/.../AppX/AppxManifest.xml` 与 `resources.pri`。
  改了 resw/manifest 要 `winapp run .` 才算验证过；打包验证加 `-p:GenerateAppxPackageOnBuild=true`。
- **别用 `dotnet build` + 松散注册去跑 `samples/`**：会把 VS `F5` 的部署顶掉，用户的验证链路就断了。

**交付前必须有构建证据**：把 `dotnet build` / `winapp run` 的真实输出贴出来再说"改好了"。
PowerShell 输出容易被吞 —— 落文件再读（`... | Out-File <path> -Encoding utf8`）。

---

## 四、agent 生成代码的红线（本仓库版）

1. **不写 XAML**。本框架是"用 C# 描述界面"（`Reactor.uwp`），UI 一律走框架的 element/handler，不要生成 `.xaml`。
   真的需要 XAML（UwpApp 壳层）时也别顺手把控件改成 `muxc:`。
2. **AOT 友好**。栈上有 `PublishAot` + `DisableRuntimeMarshalling` + CsWinRT：不要生成依赖运行时反射、
   `MakeGenericType`、动态序列化的代码；WinRT 互操作走 CsWinRT 投影而不是手写 COM 互操作。
3. **双架构原生桥**。项目带 `runtimes/win-x64|win-arm64/native/`，新增原生代码两个架构都要有，
   缺一个时 x64 上完全正常、只在 arm64 消费方炸。
4. **MinVersion 19041**。用到更高版本 API 必须做运行时存在性检查，别裸调。
5. **别把"官方 WinUI 3 规范"当普适规则写进注释/文档**。写依据时要注明它属于哪个栈。
6. **改一行就要能说依据出处**（源码行号或官方文档）；说不出就是猜 —— 参照 `uwp-runtime-evidence-debugging` 的律令。

---

## 五、应用内接 AI / agent 能力：边界与**未验证**清单

这一块是"在 UWP 应用里集成 AI agent 能力"，与 §1~§4（给 agent 的开发规范）是两件事。
**下面每条都标注了验证状态，没验证的不许当事实写进代码注释。**

| 能力 | 官方集成路径 | 本仓库（UWP 栈） | 状态 |
|---|---|---|---|
| **Windows AI APIs**（`Microsoft.Windows.AI`；`LanguageModel.GetReadyState()` → `EnsureReadyAsync()` → `GenerateResponseAsync()`） | **Windows App SDK**（文档原话：通过 WASDK 里的 Windows AI APIs 集成） | UWP 项目走不走得通**未验证** | ⚠️ 未验证 |
| 模型更替 | Phi Silica（LAF token）→ **Aion Instruct**（无需 LAF）。2026-10 起 Insider 推送，2026-11 零售推送并移除 Phi Silica | 属于版本/时间敏感信息，用到时**重新查文档** | ⚠️ 时效性强 |
| 区域 | 官方明示 **Phi Silica 在中国不可用** | 同上 | 已确认（文档） |
| **App Actions / Agent Launcher** | 需 **MSIX package identity** + `uap3:Extension`（`com.microsoft.windows.ai.actions` / `com.microsoft.windows.ai.agentInfo`）+ `com2:Extension` COM 激活 + SDK 10.0.26100.0+；`action_id` 必须与 action 定义里的 `id` **逐字相同**，否则静默不注册 | UWP 包有 identity，但这条路径是否支持 UWP **未验证**（官方示例是 packaged WinUI 3 / WPF / WinForms） | ⚠️ 未验证 |
| **ONNX Runtime** | `InferenceSession` 跑本地模型，选 execution provider | 与 UI 栈无关，理论上跨栈可用 | 未在仓库内验证 |

**纪律**：要在本仓库落这些能力，先做可用性验证（最小 POC + 实机证据），再谈实现。
不要在注释里写"UWP 支持 X"或"UWP 不支持 X"—— 两者目前都没有本仓库的证据。

---

## 六、行动前自查

- [ ] 我要写/审的代码，**TFM 是 UWP 还是 WASDK 桌面**？（决定了 §2 表走左列还是右列）
- [ ] 有没有出现 `XamlRoot` / `AppWindow` / `InitializeWithWindow` / 全局 `Microsoft.UI.Xaml`？→ 对照 §2
- [ ] 我给 agent 装/写了 instructions 吗？它会不会把本仓库往 WinUI 3 推？→ 用 §2 反向覆盖
- [ ] 构建/运行证据拿到了吗？AppX 是真的刷新了吗？（`winapp run .`）
- [ ] 如果是 WinUI 3/WASDK 项目，我是不是擅自 `dotnet build` 了？（应走 `winapp run`）
- [ ] 涉及 AI/agent 能力的断言，**是"已验证"还是"我看文档觉得"**？（§5 的 ⚠️ 项一律按未验证处理）

---

## 七、配套

- 运行时取证、六条律令、发版验包 → `.workbuddy/skills/uwp-runtime-evidence-debugging/SKILL.md`
- 本地化键名 / `x:Uid` / resw → `.workbuddy/skills/uwp-winui-localization/SKILL.md`
- 官方文档入口（供 Learn MCP 之外兜底）：`https://learn.microsoft.com/windows/apps/develop/ai-assisted/`
