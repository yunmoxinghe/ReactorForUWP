# Reactor.Uwp 0.1.0-alpha.9

**主题：同一个形状犯了六次 —— 事件委托里拿 `sender` 查表、`Unmount` 只摘键不摘订阅。**

这一版没有新的真机症状驱动。起因是把"点了没反应"这一类做一个**全目录巡视**：
`RadioButtonsHandler` 那次 RCW 身份漂移（`CtlId` 从 `#1` 跳 `#2`）修完之后，
同一个写法在另外六个事件上还留着。它们不发病，只是**还没碰到**触发条件 ——
这决定了一件事：**这六处修复在真机上验不出来**，判别力只能由源码级契约承担。
本版把这句话实测坐实了（见 §4）。

---

## 1. 那个形状

三个要素凑齐就犯：

1. handler 用一张**按控件实例键控**的表存回调（`WeakTable` / `ConditionalWeakTable`，
   **引用相等**）；
2. 事件委托里用回调给的 **`sender`** 去查这张表；
3. `Unmount` 只 `Remove` 表键，**不 `-=` 解绑**事件。

后果分两条，一条是急性、一条是慢性：

| 条 | 触发 | 表现 |
|---|---|---|
| **急性** | WinRT 把同一个原生对象交成**另一个托管包装** | `TryGetValue(sender)` 落空 → 委托静默 return → **"点了没反应"**，且**永久**失效（事件确实抛了，只是订阅这一侧查不到键） |
| **慢性** | 同一个原生控件**被重新挂载** | `Rebind` 拿"表键在不在"当"订阅过没有"的守卫；`Unmount` 摘了键却没解绑 → 守卫判成"没挂过" → **再挂一份** → 此后每发事件都是**双份回调** |

第二条尤其阴：`setState` 走两遍、落盘两遍、渲染两遍，界面上表现为"跳一下又跳回去"，
而**终态不变量一条都不会红**（第二次是同值）。

## 2. 修法（统一形状，六处一致）

```
委托体内：  一律用订阅时捕获的 control 查表，sender 一概不碰
订阅守卫：  Callbacks.ContainsKey  →  专用委托弱表（Handlers / ClickHandlers /
           ToggledHandlers）—— 它只在挂订阅时写、只在解绑时删，与订阅同起同落
Unmount 内：control.Xxx -= handler;  Handler表.Remove(control);  Callbacks.Remove(control)
```

落点：

| 文件 | 事件 | 新增的委托弱表 |
|---|---|---|
| `Handlers.Tree.cs` | `ItemInvoked` / `Expanding` / `Collapsed` | `Handlers`（三元组） |
| `Handlers.Template.cs` | HyperlinkButton `Click`、SettingsCard `Click` | `ClickHandlers` |
| `Handlers.Controls.cs` | ToggleSwitch `Toggled` | `ToggledHandlers` |
| `Handlers.Controls.cs` | RadioButton `Checked` / `Unchecked` | `Handlers` |

### 2.1 顺带修掉的两个可观测性黑洞

- **ToggleSwitch / RadioButton 的 `Mount` 原先挂的是裸回调**
  （`element.OnIsOnChanged` / `element.OnIsCheckedChanged`），要等到第一次 `Update`
  才换成 `Guard(...)`。于是**启动后的首发事件**既没有日志、也没有回声抑制。
  改成 `Mount` 里直接挂 `Guard(control, ...)`。
  注意这**不是**功能 bug：A/B 证明改动前也是同样行为，首发只是"看不见"，不是"错了"。
- **RadioButton 的 `Guard` 是四个受控控件里唯一没埋 `→ 用户回调` 的**。补齐后
  真机回归脚本才能用同一条判据数它（此前只能靠间接证据）。

## 3. 守它的东西：源码级契约

`tests/Reactor.Core.Tests/DetachedContractTests.cs`：

- **全目录扫描** `Reactor.uwp/Internal`，识别三种形态：
  强转 `var x = (Control)s;`、`is` 模式匹配 `s is X y && TryGetValue(y, …)`、直接 `TryGetValue(s)`。
  （第一版是手写白名单，**漏掉了 `Handlers.Tree.cs`** —— 它就藏在第二种形态里，
  正则躲过去了。改全目录之后才抓出来。）
- **每一种形态都做变异验证**：临时把源码改成旧写法，契约必须报警；改完还原。
- **订阅 ⇔ 解绑成对契约**：按事件名统计 `+=` 与 `-=`，覆盖 6 个文件，
  事件名含 `Toggled` / `Checked` / `Unchecked` / `Click` / `Expanding` / `Collapsed`。

关掉任何一处修复，这套契约会红 —— 这是"关掉必须显式 fail"那条的兑现处。

## 4. 真机判别力：实测结论（这一版最该记住的一条）

修完之后做**反向对照**：把这六处还原成旧写法重新编译跑真机，**计数照旧**。
也就是说在这条路径上 `sender` 恰好就是同一个 RCW，真机脚本**没有判别力**。

为了把这个"恰好"变成可观测的量，新增了**裸 sender 对照页**（`UwpApp` 测试外壳
「裸 sender 对照」，`tools/uia/winapp_sender.py`）：同一页上 A 侧走 Reactor handler，
B 侧自己 `new` 控件、自己挂事件、委托里**故意用 `sender` 查表**，并分别记
**收到**（事件确实抛了）与**命中**（按 `sender` 查到了键），外加
`ReferenceEquals(sender, control)`。

实测（3 发 × 2 控件）：

```
A 侧（有修复）：树 3 | 链接 3 | 卡片 3
B 侧（裸 sender）：树 命中3/收到3 同引用=True | 链接 命中3/收到3 同引用=True
```

**结论：该机型该路径上 WinRT 交回同一个 RCW，裸侧按 `sender` 查表全部命中。**
三处修复是**按构造正确**的防御性改动，真机仍验不出来 ——
判别力继续由 §3 的源码级契约承担。

> 卡片的裸侧对照**没取到**，如实不凑：那一列整体落在滚动视口之外
> （取证 `IsOffscreen=True`），而 `SettingsCard` 不支持 invoke pattern、
> 只能靠鼠标 click，click 又要元素的屏幕矩形 —— 屏外元素矩形为 0，点不中。
> 挪列首、给死高度、两列改对称都试过，结论一样：这是**视口限制**，不是控件的问题。

## 5. 工具链：UIA 脚本迁到 `winapp ui`

原先自造的 `comtypes` 封装全部退役（`winapp` CLI 自带 UIA）。
新脚本：`winapp_run.py`（主力回归）、`winapp_key.py`（键盘 vs invoke 正交）、
`winapp_switches.py`（开关 + 单选）、`winapp_rebind.py`（重挂探针）、
`winapp_sender.py`（本版新增的裸 sender 对照）、`winapp_diag.py`（卡死取证）。

踩过的量具坑（都写进了各脚本的注释，别再踩一遍）：

- UWP 顶层窗口宿主是 **`ApplicationFrameHost`**，不能用 `-a UwpApp` 搜，
  必须 `list-windows` 拿 HWND 再 `-w`；
- HWND 值会被复用，**每发都要重解**句柄，且 `stop_app` 之后要 `wait_window_gone`；
- **屏外元素**（`IsOffscreen=True`）的矩形是 0：`invoke` 不受影响，
  `click` 打不中；
- 按名字搜会同时命中控件与它模板里**同名的 Text**（后者尺寸 0）→ 加 `--type` 限定；
- XAML 的键盘输入必须 `send-keys --via send-input`（post-message 到不了）；
- 状态行别用破折号占位，脚本按格式解析，解析不出来会误报"读不到状态行"。

## 6. 状态

- 全量控制台测试：**607 项通过 / 0 失败**；Release + Debug 编译：**0 警告 0 错误**。
- 真机回归：`winapp_run` 5 发、`winapp_key` 12 发、`winapp_switches` 6 发全绿；
  三个反向验证（`--expect 2`）退出码均为 1。

### 6.1 如实未闭环的一项

1. **三处修复的真机验证没有区分力** —— 已由 §4 实测定性为"不可达"，
   不是待办，是结论。

### 6.2 已闭环：诊断量具自检

`winapp_diag.py` 的"多项同时选中" / "CPU 高"两条告警原本**从未被真实场景触发**
（bug 修完就没场景了），属于未经验证的代码 —— 不验的话，等真出事时你不知道它
会不会响、响得对不对，误以为它给的是可信结论，比没有更危险。

所以新增**诊断量具自检页**（测试壳第 23 项 / mode 22）：故意造两个反常场面，
让量具自己响一次。它不是 bug，是给量具用的假场景。

| 场面 | 构造办法 | 实测结果 |
|---|---|---|
| 多项同时选中 | 两个 `groupName` 不同的 `RadioButton` | `甲=True 乙=True` → 报 **2 项同时选中** ✅ |
| CPU 持续高 | 按钮起后台线程空转 6 秒（必须后台：UI 线程转会让 invoke 不返回，采样窗开在 invoke 之后，反而量到 0） | **85% / 88% 单核、持续 ~5s** → 报 CPU 高 ✅ |

自检第一次跑就抓出量具本身的**误报**：拿一个 `Button` 当目标时，它读不到
`IsSelected`，被当成 False 数进"选中项"，凭空报"0 项同时选中（UI 与内部状态脱钩）"。
已修：只在**真读到** `IsSelected` 的项里计数，并把"一项都没选中"与"多项同时选中"
分成两句话。修后复跑确认不再误报。

## 7. 同一版里另外带进来的：示例集对齐官方图库

这一版在 §1–§6 那些修复之外，还把画廊示例按微软官方 WinUI 3 Gallery 的源码
逐条对了一遍（基准与台账见 `docs/parity/winui3-gallery.md`）。**它是 additive 的，
不改任何已有行为**，与上面那六处修复互相独立。

- **152 条属性差异**已落地（尺寸、间距、补官方档位），涉及 51 个示例文件；
  77 条"不适用"与 4 条框架侧偏差都写了理由，不是静默跳过。
- **缺素材 18 处闭环**：原本六个示例全指着同一个应用 logo，导致 Stretch 三档、
  缩放、头像这些档位根本看不出差别。改为按需生成四张替代图
  （`tools/parity/make_sample_media.py`，纯标准库手写 PNG）。
- **覆盖面矩阵**：官方 121 个控件 → 本地已覆盖 72；未覆盖 49 拆成
  教学主题 24 / WinUI 3 独有 15 / **本机有类型但框架没暴露 10**。

### 7.1 顺手补掉的框架属性（纯增量）

对齐到最后剩下的缺口在"框架没开这个属性"上，补了三处，**默认值一律保持现有行为**：

| 补的 | 说明 |
|---|---|
| `ComboBox.Header` / `PlaceholderText` | 元素类与 Handler 早都支持，只是工厂没开形参 |
| `RadioButtons.Header` | 同上 |
| `InfoBar.Title` / `IsOpen` / `IsClosable` | 官方那页几乎条条都有标题；原先只有 `Message` |

`InfoBar.IsOpen` 是**种子值不是受控值**：只在 Mount 写一次，之后归控件自己——
用户点了 × 之后，下一轮重渲染不该把它重新打开。这个"故意不下发"被
`PropertyDriftTests` 的 invariant 当场抓到（`InfoBarElement.IsOpen` 未登记），
按约定写了 `// MOUNT-ONLY: IsOpen` 及论证后转绿。**登记必须连着理由一起写**，
否则就是一个被关掉的报警。

### 7.2 状态

- 全量控制台测试：**607 项通过 / 0 失败**（本版补属性后复跑过）。
- 示例工程隔离编译：**0 警告 0 错误**（示例是打包 AppX，不能就地编译，
  走 `tools/parity/build_probe.py` 复制编译）。
- 覆盖面与类型判定可复现：`tools/parity/coverage.py` + `probe_types.py`。
