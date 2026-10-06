# Reactor.Uwp 0.1.0-alpha.6

> 一句话：**修掉受控控件的三个 bug——它们都是"点了没反应"的不同面孔。**
> 这三个 bug 都不是人工复现出来的，是 `tests/Reactor.Core.Tests` 的模糊测试自己撞出来的，
> 每个都配了"关掉修法就失败"的反向对照。

## 修了什么

### 1. 吞掉「取消选中」之后，没把控件纠正回来

| | |
|---|---|
| **症状** | 点**当前已选中**的项，或选中项被虚拟化回收之后，界面上**没有任何一项处于选中**，但 state 明明有值 |
| **机制** | 点已勾选项时 `IsChecked` 只是 `true→false`，**只发 `Unchecked`**，所以 `OnChildChecked`(`cpp:407`) 压根不在场；`OnChildUnchecked`(`cpp:421`) 直接 `Select(-1)`，而 `Select(-1)` 里 `GetDataAtIndex(-1, true)` 取不到元素、**没有任何一项被勾回来**。依赖属性于是被写成 `-1`（`RadioButtons.cpp:376-377`），事件以 `AddedItems = { null }` 抛出。虚拟化回收走同一条路（`cpp:304`）。<br>**这是 WinUI 2 自己的行为，我们只是不该让它把受控控件带偏。** |
| **闸门** | 判据零（`AddedItems` 里无实项）**判对了**，正确吞掉 |
| **留下的问题** | state 保住了，控件却停在"无选中"——两边对不上 |
| **修法** | `SelectionRestore.Schedule`：只在 `CancelTransient` 这一道上，排到派发器下一轮把受控值写回 |

**为什么必须延迟**：这发事件是在 `Select` 的 `m_currentlySelecting`(`cpp:364-368`，`gsl::finally` 撑到返回为止) 期间派发的，此刻当场写 `SelectedIndex` 会被 `Select` 第一行守卫(`cpp:358`) 挡掉——**写了也白写**。

只对 `CancelTransient` 纠正，其余三道不能碰（未就绪 / 重建中的值是真实值；回声会自激）。

### 2. 受控写入在「不会抛事件」的时刻泄漏回声登记

| | |
|---|---|
| **症状** | 某个特定的值上点了没反应，**换个值就正常** |
| **机制** | 折叠区挂载时（尚未 `Loaded`）下发受控值会登记 `EchoGuard.Expect`，但此刻 WinUI 的 `m_blockSelecting` 挡住 `Select`(`RadioButtons.h:91`)，**事件根本不会来，这条登记永远等不到消费**。之后用户点回**同一个值**，`Consume` 就匹配上那条陈旧登记，被判成回声吞掉 |
| **修法** | `SelectionGate.ShouldExpectEcho`：只在"回声真会走到回声道"时才登记（复用 `Decide`，将来改闸门顺序不会错位） |

这条也是"必须先切换一次材质，其他设置才生效"的同一枚硬币——切材质那次重渲染顺手改了控件值，后续才重新有反应。

### 3. 回调为空时的受控写入同样泄漏（三个 handler 共有）

| | |
|---|---|
| **机制** | 三个 handler 都是"**先写值、再 `Rebind`**"，而事件处理器是 `current?.Invoke(...)` / `Dispatch` 首句 `if (callback is null) return;` —— 回调为空时**连 `Consume` 都不会被调用**，`Expect` 登记的期望永远没人领 |
| **后果** | 之后回调装上、用户拨 / 点到**同一个值** → 匹配陈旧登记 → 判成回声吞掉 |
| **加重项** | 渲染是**批处理**的（`RenderBatcher` 排队到下一帧），同帧内第二次操作会在第一次 `setState` 落地前到达 → **连拨两次只剩一次反应** |
| **修法** | `EchoGuard.CancelIfUnconsumed`：写入**之后**若发现同步回声没来消费，就撤销登记 |

放在写**之后**是因为那一刻"同步事件 → 匹配 → 消费"已经跑完，才能区分有无。代价方向是对的：万一某个控件的事件是异步的，漏撤销后只会**多回调一次**，而那个值正是刚写进去的受控值（`setState` 同值不重渲染，一次空转）；反过来漏撤销吞掉的是**真实用户操作**。

这不是 `ToggleSwitch` 独有的病，选中类（`RadioButtons` / `ComboBox`）结构完全相同，一并修了。
而顺着这条线往下摸，发现它压根不是那三个控件的性质——见下一节。

### 3.1 同一份接线的其余九个站点（这条才是通则）

把全仓库的 `Expect(` 扫一遍，同一形状的站点还有 **9 处**：

| 站点 | 位置 |
|---|---|
| `TextBox.Text` | `Handlers.Basic.cs:90` |
| `CheckBox.IsChecked` | `Handlers.Basic.cs:144` |
| `Slider.Value` | `Handlers.Basic.cs:205` |
| `RadioButton.IsChecked` | `Handlers.Controls.cs:562` |
| `NavigationView.SelectedItem`（Mount / Update） | `Handlers.Controls.cs:1207` / `:1273` |
| `PasswordBox.Password` | `Handlers.Input.cs:77` |
| `AutoSuggestBox.Text` | `Handlers.Input.cs:185` |
| `NumberBox.Value` | `Handlers.Input.cs:330` |

**不是控件的行为，是接线的行为**——依据全在本仓库，不用猜：

- `Reconciler.cs:1013-1092`：四个 `Rebind*` 一律先 `-= existing` 退订，再在回调为 `null` 时 `return`（不重订）。
  于是"这一发回声有没有人领"，**只看这一轮有没有回调**。
- `Handlers.Input.cs:91-101` / `:390-401`（`PasswordBox` / `NumberBox` 自己的 `Rebind`）同样如此；
- `Handlers.Controls.cs:588-598`（`RadioButton`）形状不同：**订阅一旦挂上就不再退**，
  但回调为空时 `Invoke` 走 `current?.Invoke` 空转，`Consume` 一样一次都不会被调用。
- 所以统统是"`Expect` 登记了，而这一轮没人来领"。

两处还有各自的加罪项：

- **NavigationView**：写入 `SelectedItem` 大概率**压根不抛事件**——会同步走到
  `OnSelectionModelSelectionChanged`（`release/2.8 dev/NavigationView/NavigationView.cpp:271`），
  而那里在 `selectedItem == SelectedItem()` 时直接 `return`（`cpp:277-291`），
  即"经由 API 选中"是不发 `SelectionChanged` 的。撤销它不存在"误伤真回声"的风险。
- **Slider / NumberBox**：`Value` 会被 `Minimum` / `Maximum` 夹取，写下去可能被夹成别的值，
  登记的那个值永远等不到匹配项。

**防线不靠人记得。** 除了仿真（两种接线性状各 20000 条 × 24 步，反向对照未修复时失败
**2273** / **1036** 条），还加了一条**源码级契约自检**（`tests/Reactor.Core.Tests/EchoContractTests.cs`）：
每出现一条 `<守卫>.Expect(`，其后 25 行内必须有同一个守卫的 `CancelIfUnconsumed(`，
否则就是"照抄了同一份接线却忘了配套" —— 测试直接变红并列出文件名行号。
扫描器本身也有反向对照：喂一段明知的违约样本，它必须抓出来。

### 4. 顺带：受控值越界的纠正守卫

items 变少后 state 仍指向不存在的下标时，`GetDataAtIndex` 同样返回 `nullptr`、事件照样以"无实项"抛回，而纠正写不进去 → 会自激。已在 `SelectionRestore.Schedule` 里用 `Items.Count` 挡一道。

---

### 5. 修掉了取证本身的一处失真（这条会让上面的结论不可信）

面包屑那条修复要满足 WinUI 的硬要求——**每次下发都必须是全新的数据源引用**。
之前的取证打印 `新数据源 #{GetHashCode():X}`，从日志看四个 `#hash` 各不相同，像极了证据，
但**它是假的**：那四行分属四个不同进程（每行前都跟着一行宿主初始化 `[Host/INF] BACKDROP ...`，
那才是会话边界），而 `GetHashCode` **跨进程本来就不一样**。

改归：`Handlers.Template.cs` 改用 `CtlId.Tag(...)`（引用相等的稳定编号，同一实例永远同号、
号只增不减），日志里 `#12 → #37` 才代表真的换了实例。
同时把 Gallery 诊断页的面包屑从写死两层改成**可增减条目**——同一会话只下发一次的话，
这条性质**根本无从验证**。

> 这是本项目第 N 次栽在 `GetHashCode` 上（第一次是 AOT 下 `RuntimeHelpers.GetHashCode`
> 当实例指纹），已定成硬规矩：**给人看的实例身份一律 `CtlId`，不许 `GetHashCode`。**

### 6. 面包屑那条终于有了回归网（此前零覆盖）

上面三个 bug 都有自动化测试盯着，**只有面包屑这条没有**——它是最早报的那个
（「面包屑导航不见了」），却常年只有一段引源码的注释守着。

先看清楚修复到底押在什么上面：**每次 `new ItemsControl().Items` 必须是新引用**。
这一半以前只有一份假证据（见上一节），现在补到了源码：

| 环节 | 位置 | 结论 |
|---|---|---|
| 集合的创建 | `dxaml/xcp/core/core/elements/ItemsControl.cpp:403-409` | `EnsureItemCollection` 懒创建，**每个控件一份** |
| `Items` 的取值 | 同上 `cpp:29-45` `CItemsControl::GetItems` | 同一控件永远返回同一个集合 |

合起来就是：新建载体 = 新引用；长期持有一个集合再原地 `Clear()/Add()` = 引用不变
= 内部 `ItemsRepeater` 认为数据源没换 = 条目不动。

**顺带堵了一个我没法给出证据的疑问。** `ItemsControl.cpp:9-18` 的析构会
`Clear()` 掉那个集合，而载体 `ItemsControl` 不进任何可视树、不被任何东西引用。
`CItemCollection` 的所有权语义在闭源内核里，这条**证不了也伪不了**——
所以按"代价近零"把载体钉进 `WeakTable`（弱键，随控件自动回收），把疑问整体绕开。
这是防御性推断，请勿在别处引述成"已修复的 bug"。

**新增回归网**：`tests/Reactor.Core.Tests/BreadcrumbItemsTests.cs` +
`BreadcrumbSim.cs`（20000 条 × 12 步）。
内容 diff 用 `Seq.SequenceEqual`、引用判定用 `ItemsSourcePolicy.WillTriggerRebuild`，
两个都是 **handler 正在调用的那一份**（Link 进测试），不是抄过去的副本。
反向对照——换成"复用同一个集合"的写法——同批序列失败 **19232** 条。

---

### 7. 补上了两处此前缺失的量具（不是新 bug，是"某些代码从此有了守护"）

1. `SelectionRestore.Schedule` 异步体那句 `now.Index != expected`
   ——"这中间受控目标被改过就别动手"。它此前**零覆盖**：仿真把回写简化成"跟渲染共用
   同一个 pending 旗标"，执行时机永远紧跟下一次渲染，快照不可能陈旧，**删掉那行一行测试都不会红**。
   把回写建成"独立队列 + 快照"（与真代码同构）后，这道复查才有了对照：去掉它，
   同批 20000 条序列失败 **4340** 条，最小复现是 INV9（用户点了第 3 项，界面闪回第 1 项）。
2. 选中类模糊测试的 `Drain()` 改成**随机**触发（此前每步必 flush）。渲染是批处理排队的，
   "这次操作有没有赶上这一帧"在真机上是随机的；每步都 flush 等于假设每次操作后必定紧跟
   一次渲染，一整片"两次操作落在同一帧"的交错序从未被测到。
   放宽之后**没有发现新 bug**——这是一条有价值的阴性结果。

---

### 8. 回声登记那张静态表，会把已经卸载的控件钉住

这是把上一节的线往下摸了一层：前面修的都是"登记的内容对不对"，这一条是**装登记的容器本身**。

`EchoGuard` 是每个受控属性一份的 `static readonly`，键是**真实控件**，之前存在
`Dictionary<object, ...>` 里。字典是静态的，寿命比控件长，于是"控件能不能回收"这件事
完全押在另一件事上：**有没有人在该 handler 的 `Unmount` 里记得调 `Forget`**
——而 `Unmount` 会不会被调到，是 `Reconciler` 的覆盖问题，`EchoGuard` 自己保证不了。
任何一条漏掉的路（绕开 `Reconciler.UnmountTree` 的路径、将来新增 handler 忘了写 `Unmount`）
都会让整棵控件子树连同它的 `DataContext`、命令、宿主页面被永久钉住。

改法是换了存储，没改语义：`WeakTable<object, (object? Value, long Tick)>`
（项目里已经在用的一份 `ConditionalWeakTable` 封装）。判据回到控件自己：
**控件不可达 → 条目自动消失**，`Forget` 从"不写就泄漏"降级成"提前释放的加速手段"。
同一个坑这个项目在 `WeakTable` 那份注释里已经记过一次，这次是同一张药方用到第二处。

数字（`EchoLifetimeTests`）：同样登记 2 万个随即失去引用的控件，
`Dictionary` 版**每轮**净留存约 **2.3 MB**（第二轮照涨不误），弱键版第二轮不涨
（实测为负——第一轮留下的容器碎片在这轮之前也被回收了）。
这里有个坑值得单记：**`ConditionalWeakTable` 的内部容器不随条目消失而收缩**，
第一轮之后会留一块常驻，所以只看"一轮之后有多大"会把一次性开销误判成泄漏。
区分两者的指标是**第二轮还涨不涨**——泄漏的定义就是每轮都在长。

### 9. 契约补上"卸载"那一半，并且拿真实源码做逐行变异

上一节那条源码级契约只锁了"登记必须有人领"（`Expect` 之后要有 `CancelIfUnconsumed`）。
对称的一半此前**没有任何东西在守**：

- 每个 handler 用过的守卫，必须在它自己的 `Unmount` 里被 `Forget`；
- 每个登记过的守卫，必须在同类里至少有一处 `Consume`（否则这套登记永远匹配不上，
  抑制形同虚设，还白白多一次写入）；
- handler 用了守卫却压根没写 `Unmount`，同样报警——这是照抄新受控属性时最常见的漏法。

新增 `EchoContractTests` 的第二段守这三条。扫描现状：**11 个受控 handler 全部合规**，
所以它今天没有抓到东西；它防的是下一次新增。为保证它不是摆设，做了两层自证：

1. **合成样本**：缺 `Forget` 的类、没有 `Unmount` 的类必须被抓；合格的类不许误报。
2. **真实源码逐行变异**（只在内存里改，不落盘）：把仓库里每一处真实的
   `Forget` / `Consume` 行各删掉一行（共 **22 处**）喂回扫描器，
   要求它**指名道姓**地报警（报警里带出守卫名与原因），一处不许溜过——22/22 全红。

### 9.1 第三道契约：纠正不许被「有没有人监听」牵着走

上一道契约的强度有个硬上限：**仿真 Link 不进来 `Handlers.Controls.cs`**（它依赖 UWP
类型，根本编不进 `net10` 测试工程），所以那边怎么改仿真都不红。做变异验证时当场撞上：
三个变异里前两个变红，**第三个只能原地跳过**（它改的正是 handler 里的早退）。

补 `RestoreIsNotGatedOnListener`：扫描每个 `SelectionRestore.Schedule(` 站点，要求
①它走 `SelectionGate` 的纯判据（不许就地抄一个 `verdict == CancelTransient`）；
②从所属 `Dispatch` 的方法头到这一行之间，**不许出现 `if (callback is null)`**。
把早退塞回 `ComboBox` / `RadioButtons` 的门口各做了一次变异，两次都
**指名到文件行号**变红。

### 9.2 顺手把两条模糊测试判据换掉了（旧的太宽容）

旧的最终一致性判据是 `UnreportedUserActions > 0 || State == ControlIndex`——
**序列级一刀切**：只要中途发生过一次"没人监听的用户点击"，此后**所有**偏差都被赦免，
包括真 bug。换成两条各自精确的判据：

| 判据 | 允许什么 | 拦什么 |
|---|---|---|
| `Explained` | 控件停在**受控目标**或**用户最后一次点击**的值上 | 第三种值（典型是 `-1`）——没有主人的偏离 |
| `Faithful` | `state == 控件`，或控件停在最后一次点击的值上 | 回写把用户**此后**的选择盖回去 |

**两条缺一不可**：只留 `Explained` 时，陈旧回写把用户的新选择盖回受控目标，恰好落在
它允许的头一种里——实测那条反向对照从 **4384 条掉到 0**。

换取的结果是同一批改法整体变敏感（见上一节表格），其中这一轮新增的那条差距最大：
混合序列 178 → **1039**；专跑的"纯无回调"批次 21 → **3961**。
**旧的 21 条不是证明这条路径罕见，是证明旧判据一直在替 bug 打掩护。**

---

### 10. 就绪闸拿"自己那份标记"判断要不要订阅 —— 自证循环

前三道闸门（`SelectionGate`）本身是对的，但它们**吃什么**才是关键。判据一
`isReady` 来自 `ReadyGate`，而 `ReadyGate.Arm` 是这样决定要不要订阅 `Loaded` 的：

```csharp
if (IsReady(control)) { onReady?.Invoke(control); return; }
control.Loaded += Handler;      // ← IsReady 偏偏是这个 Handler 置上的
```

**用"订阅的结果"反过来决定"要不要订阅"。** 这条链子只有一种断法：控件其实**已经
进过树**（`FrameworkElement.IsLoaded == true`），而我们那份标记还是 `false`。
此时它判"没就绪，挂上去等"，而 `Loaded` 已经抛过了 —— **永远等不到**，控件就此永久
停在"未就绪"，那期间的事件被判据一（`NotReady`）一发不留地吞掉。

界面表现就是：这个控件**点了没反应**，且不报任何错、不留任何日志。

**可达性写在前面，别高估这条修复。** 今天 `Arm` 只在两个 `Mount` 里被调用，而
`Reconciler.BuildChildren`（`Reconciler.cs:203-208`）是先 `Build` 后 `Children.Add`，
即 `Arm` 一律先于入树，那条致命路径**当前不可达**。但 `Arm` 自己的注释写着
"例如 Update 路径新建的控件恰好已在树里"——它**预期**会被从 Update 调用。
这条修的是"把会不会出错押在调用顺序的巧合上"。

**改法：权威信号换成 XAML 自己的 `IsLoaded`**，而不是我们抄的副本。这不是猜出来的
API——同仓库的 `InputApplier.ApplyFocus` 已经在用它判断"此刻能不能 `Focus`"，同一个
问题（`控件进树了吗`）在这个仓库里早就有答复了，两处各答一份就会各答一半。

另外顺手补齐两件事，都是同一处接线的形态问题：

- **幂等订阅**：每次 `Arm` 生成的委托都是新实例（闭包捕获 `onReady`），`Arm` 两次就会
  `+=` 两个不同的委托，`Loaded` 来时回调**跑两遍**、`ReadyStats.Ready` 翻倍。现在
  手上有没有订阅会被记住（`Subs` 表），第二次 `Arm` 只换回调。
- **计数不再虚高**：`ReadyOnce`（早就绪过）**不**重复计入 `Ready`，否则那个读数
  就从"有几个控件真的进过树"变成"被 Arm 了多少次"。

新增 `ReadyPolicy.Decide`（纯函数，Link 进测试工程）+ `ReadyArmTests`：

| 反对照（关掉哪一半） | 违规条数 / 20000 条序列 |
|---|---|
| 不看 `IsLoaded`（退回自证循环） | 卡死 **430**、一个 Loaded 回调两次 **11014** |
| 不幂等订阅 | 一个 Loaded 回调两次 **6630** |

三条真实源码变异（改完跑完立刻还原 + sha256 校验）：不看 `IsLoaded` → **5 项红**；
`ReadyOnce` 也算进就绪数 → **1 项红**；把 `subscribed` 与 `alreadyReady` 两个分支调换 →
**246 项仍然全绿**。最后这条是条有价值的阴性结果：那一对条件**不可能同时为真**
（就绪结算会一并解掉订阅），所以那个顺序没有任何不变式在守——
注释已经改成照实说明，没有给它编一个"不这样就会坏"的理由。

还补了一条**可证伪的读数**：`ReadyStats.AlreadyLoaded`（诊断页第⑥块会显示成
`alreadyLoaded+1`）。它只统计"`Arm` 时发现控件其实已经在树上"的次数——
真机上这个数非 0，就证明那条路径确实被走到了，而不是纸面上的推理。

### 11. 「受控纠正」被绑到了「有没有人监听」上

两个 `Dispatch`（`RadioButtons` / `ComboBox`）都以这一句开头：

```csharp
if (callback is null) { return; }      // ← 连后面的纠正一起跳过
```

纠正兑现的是**"这个属性由 state 说了算"**这份承诺，跟有没有人挂
`OnSelectedIndexChanged` **无关**。绑上去之后，同一个吞法在两个页面上给出两个结论：

| | 挂了回调 | 没挂回调 |
|---|---|---|
| 点当前已选中项 → 对面 `OnChildUnchecked`(cpp:431) 把控件拨到 -1 | 排一次 `SelectionRestore` 纠正回来，**看着没事** | 事件无人接，state 没变 ⇒ 不重渲染 ⇒ **永久停在"什么都没选中"** |

后者的用户描述就是"点了一下，选中态没了，再也点不回来"，而且是**只在不给回调时才复现**
——同一个控件换个写法就好了，这类 bug 最难描述清楚。

改法：`callback` 从门口挪到门尾（`callback?.Invoke`），四道判据与纠正都不再看它；
同时把"该纠正哪一道"抽成纯判据 `SelectionGate.ShouldRestoreAfterSuppress(verdict)`，
**签名里刻意不带 `hasCallback`**——带上就是在诱导后来者把两件事重新捆起来。

---

### 12. 第 13、14 个站点：ListView / GridView 连一份都没接

顺着第 3.1 节那条线往下摸到底，清点结果是：**12 个受控站点里，11 个合规，
只有 `ItemsViewHandler`（`ListView` / `GridView` 共用的基类）一份设施都没有**——
不登记回声、不消费、`Unmount` 里也没有 `Forget`，更没有"改 items 期间"的遮蔽和越界守卫。

它和 `ComboBox` 共用 XAML 内核里同一个基类 `Selector`，而 `Selector` 的三条行为
（写回同步抛事件 / 越界写入被拒 / 改 items 会牵动选中）都已查到源码，
见 `docs/winui2-source-notes.md` 第 12 节。于是三件事都成立：

| 症状 | 机制 | 修法 |
|---|---|---|
| **受控写回被当成用户输入回调出去** | `SelectedIndex` 依赖属性变更 → `EndChange` 同步抛 `SelectionChanged`，而这里没有任何回声抑制 | `EchoGuard`：`Expect` → 写 → `Consume`（并照例 `CancelIfUnconsumed` / `Unmount` 里 `Forget`） |
| **换一次数据源，回调凭空跑一次、选中态被清掉** | 集合 Reset 时 `Selector` 自己会 `Unselect` 再把仍选中的项加回来，走的是同一条会抛事件的路 | 改 items 的那段时间标 `Rebuilding`，判据走"重建中"那道闸 |
| **items 变少后受控值越界 → 写入被拒**（`E_INVALIDARG`，变更整条 undo） | `OnSelectedIndexChanged` 只接受 `[-1, Items.Count)` | 新增 `SelectionPolicy.ShouldApply`（纯函数，Link 进测试），`ComboBox` / `RadioButtons` 的直接写回路径也一并换上它 |

**顺带修掉同一枚硬币的另一面**：`ComboBox` / `RadioButtons` 的直接写回路径此前也没有越界守卫
（只有 `SelectionRestore` 那条延迟纠正挡着）。现在三处共用一份策略，答案只有一个出处。

**`-1` 是合法值**（源码判据是 `newValue >= -1`），意思是"清空选中"——
写回策略不能把负值一律拒掉，否则 state 说"没选中"而控件还亮着某一项。
清空的副作用由 `SelectionRestore`（`target.Index < 0` 不纠正）那一边拦。

**为什么这里不用 `ReadyGate`。** `RadioButtons` 有 `m_blockSelecting`（模板没套上时押住 `Select`），
所以要先问控件进没进树；而 `Selector` 的 `OnSelectedIndexChanged` 只被"重入锁"和
"正在解析 XAML"两个条件提前返回，与模板无关——这里没有那个问题要问，就绪那一位直接给 `true`。

**前三道契约为什么一声没吭。** 第 9 节那条"每一条登记都有人领"的扫描器，
开头就是 `if (expected.Count == 0) continue;`——**压根没登记的类直接被跳过**。
补的第四道契约（`EveryControlledWriteIsGuarded`）反过来问：
"这个类写了受控属性、且有回执通道，回声三件套（Expect/Consume/Forget）齐不齐？"
扫描结果：**12 个站点，只有它一个缺**。它自己带两层自证——
合成样本（缺三件套必须红；写了属性但没有回执通道的类，如 `TextBlock` 写 `Text`，不许误报），
以及**真实源码逐行变异 36 处**（每个站点 × 三个 token，把该类里该 token 的行全部抹掉）必须指名报警。
变异过程中还揪出一个更隐蔽的问题：识别类块的正则不认 `abstract`，
**`ItemsViewHandler` 整个类从来没进过任何一条契约的视野**（它的行被算进了前一个类）。
`TopClass` 已修，两处都留下了说明。

---

### 13. 量具自己也得被量：第五道契约，管的是"清单"

第 12 节那条线摸到底之后，回头检查**第四道契约自己**，发现它有两个形状缺陷——
和它抓到的那个 bug 是同一类，只是这回漏的是"属性"而不是"类"。

**其一，回执通道的判据太松。** 原来只查"类里出现过这个事件名"。
而 `IsExpanded` 的通道写的是 `Expanding|Collapsed`——`Visibility.Collapsed` 里也有
`Collapsed`，一个用了 `Visibility.Collapsed` 的类会被误判成"有回执通道"，
进而被要求有回声三件套（冤枉），或者更糟：把该管的类放过去。
现在一律要求**订阅的证据**：`<Event> +=`，或把订阅收进去的那个 `Rebind*` 助手。

**其二，`EchoProne` 是手写清单，清单外的属性它根本不认识。**
这就是 ListView / GridView 静默两个版本的同一个成因。于是补第五道：

> 源码里每一个"用户可改属性"的写回点，必须在 `EchoProne` 里，
> 或者被**显式登记**进 `UncontrolledByDesign`（附理由）。两处都没有 = 漏了，报警。

两种归宿的分工写死在登记里：**真受控** → 补登记 + 回声三件套；**非受控** → 登记理由。
想把它从非受控升成受控，得先在元素与工厂上加回调参数，那时它该进 `EchoProne`
并**同时**从 `UncontrolledByDesign` 里删掉。

清点下来，用户可改且被 handler 写入的属性共 **9** 个：8 个在 `EchoProne` 内，
`IsPaneOpen` 登记为非受控（`NavigationView` 工厂没有开合回调参数）。

**`IsExpanded` 为什么是非受控——这次有源码了。** 原先
`Handlers.Template.cs` 的注释写着"官方对这类属性用 Controlled + counter-echo
（订阅 Expanding/Collapsed 并抑制回声）"，这句话在本仓库里**没有任何依据**：
`tools/winui2-ref/dev/` 只有 `Breadcrumb` / `ItemsRepeater` / `NavigationView` /
`RadioButtons`，**没有 Expander**，全树搜 `Expanding` 零命中。
经本地 `tools/ctk-ref` 核实：`SettingsExpander` 确实**声明**了 `Expanded` / `Collapsed`
（`SettingsExpander.Events.cs:12`、`:17`），但**全树没有任何一处 `.Invoke`**——是死事件。
用户点表头走的是 `SettingsExpander.xaml:263` 的 TwoWay 绑定，落到
`SettingsExpander.cs:69` 的 `OnIsExpandedChanged`，而它只抛 automation peer 事件（`:72`）。

**所以不是"还没接"，是接不上**：回执通道不存在，订阅了也收不到。
那条无出处的注释已改成如实描述（不对官方做法作任何断言）。
注释说一套、代码做一套比没注释更危险——下一个读代码的人会以为回声抑制已经做了。

自查照例两半：往真实源码里塞一个没登记的属性（`IsSelected`）必须报警（7 个文件各一处）；
把 `UncontrolledByDesign` 整份拿掉，`IsPaneOpen` 必须立刻变成"没人认领"——
**证明那份登记是承重的，不是写上去好看的**。

### 14. 第六道契约：按控件建的表 / 服务类登记，登记了就得摘

第二道只管 `EchoGuard` 的 `Expect → Forget`。但 handler 里按控件建的表远不止回声登记：
`Callbacks`（用户回调）、`Targets`（受控目标）、`Rebuilding`（正在换 items）、
`Carriers`（承载控件）、`States`（虚拟化状态），外加 `reconciler.RebindTextChanged`
那一族——表在 `Reconciler` 里、handler 这边看不见，但登记/摘除是同一对动作。

> 判据：类里出现 `<表>.Set(` / `<表>[x] =` / `reconciler.RebindXxx(x, 非 null)`，
> 该类的 `Unmount` 里就必须出现**同一张表**的 `Remove`，或同一个 `RebindXxx(x, null)`。

清点下来 **18 个类**落在这条契约的视野里，全部合规。

**这条契约为什么仍然值得写。** 这些表现在**都是弱键的**，所以漏摘不再是"永久钉住
整棵子树"，只是退化成"条目要等控件被回收才消失"（`Forget` 的注释里记过这个降级）。
真正会被挡住的是另一半：`Reconciler` 那几张是 `Dictionary<控件, 委托>`——**强键**，
漏摘就是真泄漏。也就是说这条契约守的是"弱键只是兜底、不是许可"这件事。

**第二类是"服务类登记"：`ReadyGate.Arm` / `ReadyGate.Disarm`。**
`ReadyGate` 那三张表（`Ready` / `Subs` / `OnReady`）在服务类自己那里，handler 看不见，
看得见的只有"登记了"这个动作——所以这一路按**动作配对**来判，不按表。
它此前是**完全没人守**的：ComboBox / RadioButtons 两处 `Arm` 都配了 `Disarm`，
但那只是写对了，删掉那行没有任何测试会红。

自查两半：合成样本五层（健康不报警 / 忘了 `Remove` 报警 / 整个 `Unmount` 都不写报警 /
**字符串为键的模板缓存不许误报** / `Arm` 了没 `Disarm` 报警）；真实源码**逐条**删掉那
**25** 处摘除语句，每次都必须指名报警。逐条而不是每个类一次——否则"删掉一行还剩另一行"
会被误判成扫描器瞎。

**口径是查过的，不是随手定的。** 把扫描范围从 `Handlers.*.cs` 扩到整个
`Reactor.uwp/Internal/*.cs` 做了一次对照，只多出两个类：

| 类 | 结论 |
|---|---|
| `InputApplier` | 没有 `Unmount`，靠每次 `Apply` 自己清（`Accelerators.Remove` / `RebindKeyEvent` 退订），且表是弱键 → 不归这条管 |
| `ReadyGate` | 服务类，已由上面新增的 `Arm` / `Disarm` 这一对覆盖 |

也就是说 `Handlers.*.cs` 这个口径在**当前这份源码**上没有漏掉按控件建表的地方，
而这个结论是量出来的，不是"应该没问题"。

**顺带修掉一个潜伏的失聪**：`UnmountRange` 只认"签名行以 `;` 结束"的表达式体，
于是 `ButtonHandler.Unmount` 那种**跨行**写法（签名一行、`=> ...;` 在下一行）
会被判成空方法体，进而"这里什么都没释放"被误报成"压根没卸载过"。
第二道契约此前靠"Button 没有回声登记"绕开了它，一直没红过。现已抽出 `MethodRange`
统一处理三种方法体形状（块体 / 单行表达式体 / 跨行表达式体）。

---

### 15. 第 13 处：`x:Uid` 那笔写发生在订阅之后（handler 之外的第一个受控写回点）

前六道契约的扫描口径都是 `Handlers.*.cs`。这一轮把口径扩到 `Reactor.uwp` **全树**
量了一遍，多出来的真洞口就一处：**`Localization.ApplyUid` 给 `x:Uid` 套 resw 里的
`Uid/Text`**（`Localization.cs:83`）。

`Build` 的次序是（源码实证，不是推断）：

```
PropWriter.BeginMount()                    // Reconciler.cs:70
  └ handler.Mount(...)                     // :85
        ├ native.Text = element.Value      // TextBoxHandler 先写初值（此刻还没订阅）
        └ reconciler.RebindTextChanged(native, OnChanged)   // 订阅挂上
  └ ApplyModifiers(native, ...)            // :102
        └ Localization.ApplyUid(native, uid)   // :1245，只在挂载期跑
              └ box.Text = resw 里的值     // Localization.cs:83 ← 订阅之后！
```

那一笔写抛出来的 `TextChanged` **没有任何回声登记**——`ApplyUid` 拿不到
`TextBoxHandler` 的 `TextEcho`（那是 handler 的私有静态字段）。于是它被当成用户输入
回调出去：`OnChanged("来自 resw 的初始文案")` → `setState(...)`。

界面上的样子：输入框的初始值被 resw 里的串**顶掉**，还**多一次 `OnChanged`**。
若 `OnChanged` 里做校验或记脏标记，就会凭空多一条脏记录——同一族"点了没反应"的
另一副面孔：**没人动过它，它自己变了**。

**修法**（`Reconciler.RebindTextChanged` 的闭包）：

```csharp
if (PropWriter.IsMounting || !textBox.IsLoaded) { …Gate…; return; }
onChanged(((TextBox)s).Text);
```

两条判据是**互补**的，不是冗余：

- `PropWriter.IsMounting` —— 若 `TextChanged` 同步抛（依赖属性变更回调的路径），
  事件在我们自己的写值语句返回前就到了，此时 `Build` 还在栈上；
- `!IsLoaded` —— 若那一发延后到树挂上**之前**才抛，靠这一条兜住。

**已知边界**（写死在模型里，不假装能拦）：两者都假——即事件延后到 `Loaded` **之后**
才抛——时拦不住，那一发会与真实键入无从区分。它的形状不是"记成挂载期冒出去"，
而是**被当成一次用户输入**。

**顺带说清一个语义后果**：修好之后 resw 的值会留在控件上，直到下一次 patch 由 state
接管。这与 `x:Uid` 作为"初始值覆盖"的语义一致（`ApplyUid` 本来就排在代码写值之后），
真正被修掉的是那次假回调和 state 被凭空改写。

**为什么第五道没抓到它**：第五道管的是**属性名**认不认得，`Text` 早就在 `EchoProne`
里了；它不管**写回点在谁手里**。同一个属性名在 handler 里写是受三件套保护的，
在 handler 外写则完全不在其余六道的视野内。

于是补**第七道契约**：

> handler 之外写用户可改属性的地方，必须逐一登记；且登记里写的"靠什么中和"
> （`AnchorMethod` + `AnchorPattern`）必须在真实源码里真的还在。

实测集合必须**恰好等于**登记表：多了报警（新洞口没人认领），少了也报警（登记失效）。
光登记一句话不够——每条登记都带锚点，**删掉那段中和代码就会红**，
这样"登记了但没修"藏不住。

自证两半：合成两段源码（handler 外冒出一处没登记的受控写回 → 必须报警；
`hook.Value = x` 这种内部字段 → 不许误报）；真实源码变异**两个方向**——
删掉 `PropWriter.IsMounting || !textBox.IsLoaded` 那一行**必须红**，
只删掉旁边那行 `ReactorLog.Gate` **不该红**（证明锚点盯的是机制，不是日志）。

行为级另加 `MountOrderSim` / `MountOrderTests`，把上面那条次序固化成可执行的：
挂载不回调 / 用户敲字照旧回调 / 关掉修法必须出现假回调且 state 被改写 /
**反事实**：把那笔写挪到订阅之前，不修也没假回调（证明**病因是顺序**，
而不是"resw 不该写 `Text`"）/ 已知边界如实记录。

---

### 16. 第 14 处：改区间把受控值夹走了，那一发被当成用户输入

顺着第 15 节那条线（「`handler` 之外还有写受控属性的地方」）继续往下摸，
这一轮摸到的是**另一种「不是用户动的值」**：改 `Minimum` / `Maximum` 会把受控件的
`Value` **夹**进新区间，而夹出来的那一发 `ValueChanged` 抛在**旧订阅还挂着**的时候。

`Slider` 与 `NumberBox` 的 `Update` 都是同一个次序：

```
① 写 Minimum / Maximum      ← 上一轮的订阅还在（Rebind 在最后才退订）
② 写受控 Value（Expect → 写 → 撤销没人领的登记）
③ Rebind：退订旧的、挂新的
```

`NumberBox` 那一侧有源码（`release/2.8/dev/NumberBox/NumberBox.cpp`，
见 `docs/winui2-source-notes.md` 第 16 节）：`OnMinimumPropertyChanged` /
`OnMaximumPropertyChanged` **各自**调一次 `CoerceValue`，后者在越界且
`ValidationMode == InvalidInputOverwritten`（idl 里的默认值，我们没有改过它）时
`Value(Minimum())` / `Value(max)`，随后 `OnValuePropertyChanged` 以
**夹取之后**的值抛 `ValueChanged`。`Slider` 那一侧只拿到文档级依据
（内核的 `RangeBase_Partial.cpp` 有，`RangeBase.g.cpp` 是生成代码、仓库里没有），
笔记里按强度分开记了，没有混着说。

两半各有各的机制，合起来才是完整的一条：

| 半 | 症状 | 为什么原有的 `Expect` 挡不住 | 修法 |
|---|---|---|---|
| ① 改区间把值夹了 | 收紧上限 / 抬高下限的那一刻，回调凭空跑一次，state 被改成边界值 | 夹出来的值**事先不知道**，而两个边界各调一次 `CoerceValue` —— 一次登记装不下两发 | `EchoGuard.Silence(control)` **静默窗**：不猜值，只凭「这段窗在渲染路径内部、里面不可能有真实用户输入」来判 |
| ② 受控值本身被夹 | state 声明了一个越界的值（典型是区间先收紧了） | 登记的是声明值，回读的是夹取后的值 ⇒ `mismatch`，那一发照样回调出去 | `RangePolicy.Coerce`：登记 / 下发都改用**夹取后**的值 |

**② 不是新发现的现象，是老现象漏掉的一半。** `Slider` 的注释里早就写着
「写下去可能被夹成别的值，登记永远等不到匹配」——但当时只配了 `CancelIfUnconsumed`
（管「事件没来」），没管「**事件来了但值不对**」。

**② 不改变控件终态。** 写声明值会被控件夹成 `Coerce(...)`，直接写 `Coerce(...)` 也是
同一个终态；变的只是「回声变得可预测」。这点值得单独说，否则会被读成改了下发的语义。

**静默窗为什么不用「预测夹取值」代替**：`CoerceValue` 走不走取决于
`ValidationMode`、`NaN`、越界方向三件事，我们自己再算一遍就可能跟控件算的不一样；
而窗在「永远没等到事件」时的代价是**零**（它只在渲染路径里开着）。
反过来，若真的会夹而没罩窗，漏的就是一发假回调——代价不对称。

**行为模型** `RangeCoerceSim` / `RangeCoerceTests` 用的是 Link 进来的**真**
`EchoGuard`（含 `Silence`）与**真** `RangePolicy`：定向 8 组 + 两组无关对照 +
20000 条 × 8 步随机序列。两个开关**各管一段**，分别关掉、分别计数：

| 只关掉 | 受影响序列 / 假回调 |
|---|---|
| 静默窗 | 19982 条 / 93998 次 |
| 登记夹取后的值（退回旧写法） | 8545 条 / 9814 次 |

只断言「关掉修法必须红」的话，「哪个开关坏、坏在哪儿」会一直是一笔糊涂账，
所以两档分开计量，并额外断言两个数字**不相等**（证明不是同一个开关在兜两处）。
无关对照两档：区间变了但值仍在区间内（两边都该是 0）、以及把「控件会夹取」这个前提
也关掉（假回调必须消失——否则说明病因认错了）。

**第八道契约**守着第 ① 半（第 ② 半由第四道契约 + `RangePolicy` 的单元测试守）：
写了 `Minimum` / `Maximum` 的地方必须在静默窗里写，两种免检——
控件没有受控值（`ProgressBar` / `ProgressRing` 也写 `Minimum`，但它们没有回调，
没人接那一发）、调用点在 `Mount` 里（那一刻订阅还没挂上）。
自证是合成样本六档 + 真实源码变异**两个方向**（删掉窗 / 把窗**挪到写入之后**）；
后者证明这条契约盯的是**位置**，不是「类里有没有出现过 `Silence`」。

**顺带多了一个计数 `silenced`**（诊断页第⑥块会显示）：涨它说明这一发落在静默窗里，
**不是异常**。它和 `matched` 的区别值得分清：`matched` 是「猜中了值」，
`silenced` 是「压根没猜值、只凭时间窗挡下」。那块仪器自检的键名清单
八个 → 九个，格式哨兵照旧用真的 `EchoStats.Snapshot()` 造样本。

---

### 17. 第 15 处：改「不是受控值」的属性，控件却把受控值改了

顺着第 16 节那条线（"改区间把受控值夹走了"）继续泛化，就撞上它的一般形式：
**写了一个不是受控值的属性，控件却因为这一笔把受控值改了**——
而那一发事件抛在**旧订阅还挂着**的时候（`Rebind` 在 `Update` 最后才换回调）。

这一族在本版里找到五个成员，按证据强度分两档：

| # | 位置 | 依据 | 处理 |
|---|---|---|---|
| ① | `ItemsViewHandler.Update` 写 `SelectionMode` | **源码**：`ListViewBase_Partial.cpp` 的 `ListViewBase_SelectionMode` 分支 → `OnSelectionModeChanged`，注释原文 `will update all Selection related properties` | 静默窗 |
| ② | `NavigationViewHandler.Update` 的 `ApplyMenuItems`（`MenuItems` 是 Clear + 重建） | **源码**：`NavigationView.cpp` 的 `OnSelectionModelSelectionChanged`，早退条件 `m_shouldIgnoreNextSelectionChange \|\| selectedItem == SelectedItem() \|\| !m_appliedTemplate` 在"清空"时不成立 → 继续抛 | 静默窗 **+** 重建后补发受控选中值 |
| ③ | `NavigationViewHandler.Update` 写 `PaneDisplayMode` | 同一个 `UpdateRepeaterItemsSource`：Top / Left 切换会把另一侧的 repeater 源置空、这一侧重建 | 静默窗 |
| ④ | `PasswordBoxHandler.Update` 写 `MaxLength` | **无源码**（Windows.UI.Xaml 那一支不开源；文档只说它约束输入，没说会不会改已有内容） | 静默窗（代价不对称） |
| ⑤ | `RadioButtonHandler.Update` 写 `GroupName` | **无源码**（`RadioButton.cpp` 404） | 静默窗（代价不对称） |

④⑤ 的"不知道会不会动，所以按会动处理"与第 16 节 `Slider` 那一半是同一个论证：
**窗在"永远没等到事件"时的代价是零，漏罩则是一发假回调**。依据强度在源码笔记里
分档记着，没有混着说。

**② 的第二半不是假回调，是丢状态。** 旧判据是
`oldElement.SelectedIndex != newElement.SelectedIndex`——重建把选中清空之后，
声明值压根没变，于是永远不补这一笔 ⇒ 选中态**永久丢失**，界面表现为
"换了一次菜单项，导航条再没有选中项"。把 `menuChanged` 加进判据才接得上。
这一类此前没有任何契约盯它：假回调有计数、有契约，丢状态没有。

**第九道契约（第八道的泛化）。** 第八道只认 `Minimum` / `Maximum` 两个名字，
于是 ①④⑤ 这些同样会牵动受控值的写入**一条都进不了视野**——
**按名字认，漏掉的是名单之外的全部**。第九道把判据反过来：

> 有受控值（`EchoGuard`）的 handler 里，**每一处**非受控属性的写入，
> 要么在静默窗里、要么在 `InertByDesign` 里登记过（登记要写理由）。

于是以后新增一处属性写入，这条契约会先红，逼出一句"它会不会牵动受控值"的答复。
目前登记了 12 个惰性属性（`Header` / `PlaceholderText` / `Content` / `OnContent` /
`OffContent` / `IsItemClickEnabled` / `IsPasswordRevealButtonEnabled` / `IsPaneOpen` /
`IsSettingsVisible` / `IsBackButtonVisible` / `IsBackEnabled` / `AlwaysShowHeader`），
未登记的 18 处全部在窗里。自证：合成样本五档 + 真源码变异两个方向（删窗 / 挪窗）+
**反向对照：把惰性登记整份当作不存在，真源码里必须冒出违约**。

**行为模型** `SiblingWriteSim` / `SiblingWriteTests` 用的是 Link 进来的**真**
`EchoGuard`（含 `Silence`）与**真** `SelectionPolicy`：定向 10 组 + 无关对照 +
20000 条 × 8 步随机序列。两个开关**各管一段**、分别关掉、分别计数：

| 只关掉 | 受影响序列 / 计数 |
|---|---|
| 静默窗 | 19997 条 / 95009 次假回调 |
| 重建后补发 | 18529 条 / 44268 帧丢选中 |

外加三条"分段正确"的断言：只关窗时丢选中那一类仍是 0、只关补发时假回调那一类仍是 0、
两个数**不相等**（相等往往意味着同一个开关在兜两处）；以及无关对照——
把"控件会自己动选中"这个前提也关掉，两类问题必须同时消失（否则病因认错了）。

**顺带**：`ListView(...)` 工厂加了一个带 `selectionMode` 的重载（可选，不破坏现有
调用），列表页用它做了取证入口——「切到禁止选中 / 切回单选」+ 同一个回调计数。

---

### 18. 第十道契约：集合写入是**方法调用**形状，前两道一条都看不见

收第 17 节时留了个疑问：`ComboBox.ReplaceItems` 里那两行
`Items.Clear()` / `Items.Add(item)`，第九道判它"合规"——
**它是真合规，还是压根没被看见？**

答案是后者。第八、九道的判据都是 `X.Prop = v` 这种**赋值**形式，
而"改集合"是**方法调用**形式（`control.Items.Clear()`、
`nav.MenuItems.Add(item)`、`bar.ItemsSource = …`），一条都匹配不到。

**这一条漏得比"少认了几个属性名"严重**：换集合恰好是牵动受控选中值
**最典型**的动作——第 17 节那个行为模型建模的就是它：`Clear` 把选中冲成 -1，
那一发抛在旧订阅还挂着的时候。第九道等于把最有嫌疑的一整类漏在视野外，
还给出绿灯让人以为它管住了。

**第十道**沿用第九道那条已经验证有效的思路：**默认全管，豁免要登记**
（登记按**（类名, 属性名）**——`Items` 在 ComboBox 上要管、
在 SettingsExpander 上不用管，只按属性名记会给最危险那条发免死金牌）。
抑制块两种都认：`EchoGuard.Silence` 的窗与 `Rebuilding` 标记位——
后者是 ComboBox / RadioButtons / ItemsView 一直在用的老机制，与窗等价。

**扫出来 28 处，逐条落点**（`docs/winui2-source-notes.md` 第 18 节有全表）：

| 归类 | 处数 | 说明 |
|---|---|---|
| 在 `Rebuilding` 标记块里 | 4 | ComboBox / RadioButtons 的 `ReplaceItems` |
| 辅助方法 → **调用点**被罩住 | 4 | `NavigationView.ApplyMenuItems`（窗）、`Reconciler.PatchItems`（**跨文件**，调用点在 ItemsView 的 `Rebuilding` 里） |
| 挂载期免检 | 3 | 那一刻订阅还没挂上 |
| 登记为惰性 | 17 | `Children`（Panel / Canvas，没有选中通道）、`SettingsExpander.Items`（不是 `Selector`）、`BreadcrumbBar` 的载体与 `ItemsSource`、`AutoSuggestBox.ItemsSource` |

**这一轮没有挖出新的真洞口**——28 处里没有一处是裸写的。挖出来的是一份
**此前不存在的清单**：以前"改集合要关起来"只靠人记得，现在契约会逼你答复。
它顺带把"跨文件"那层也证成了：`Reconciler.PatchItems` 是被
`ItemsViewHandler` 的 `Rebuilding` 罩住的，这件事以前只存在于读过代码的人脑子里。

**量具自证**：合成样本七档（Rebuilding 块 / 静默窗 / 裸写 / 挂载期 / 惰性登记 /
辅助方法调用点罩住 / 辅助方法调用点裸着）+ 真源码变异（删掉罩着集合写入的抑制块
必须多报，**跨文件计数**）+ 反向对照（把惰性登记当不存在，真源码必须冒出违约）。

**它逼出扫描器两个真洞，都是"之前一直假绿"的那种：**

1. **调用点正则把点前缀排除了。** 真源码里的调用点带接收者
   （`reconciler.PatchItems(…)`），而正则是 `(?<![\w.])`——
   于是**唯一那一处调用点被排除**，"调用点裸着"那一档永远不报警。
   第九道的 `AllCallSitesSilenced` 同一个写法，一并修了。
   它藏得住是因为**合成样本用的是不带点的写法**（`ApplyRange(control, n);`）：
   样本替你测的，只有它自己走过的那条路。
2. **跨文件变异只在同文件里数违约。** 删掉 Controls 里那行
   `Rebuilding.Set(control, true)`，真正冒出来的违约在 `Reconciler.cs` 里，
   只在被改的文件里数永远数不到 → 假绿。改成替换进"所有文件"再数全局总数。

**顺带否决了一个更激进的方案**（记下来免得以后重犯，详见笔记 18.5）：
加 `PropWriter.IsPatching` 把第 13 处那道屏蔽从挂载期扩到**整个渲染期**，
一次性罩住 `ApplyModifiers` 的全部写入。否决理由是
`Visibility = Collapsed` 的后果是**布局驱动**的——折叠后 repeater 在**下一帧**
才回收元素，那时闸门早关了；第 13 节记为"已知边界"的那条在这里是主情形不是边缘。
而且那一发（`Select(-1)`）已经被判据零罩住（`AddedItems` 为空 → `Suppress`），
再加一层是重复记账。**时间窗只管得住同步后果；异步后果得靠持续判据
（`ReadyGate` 那种"控件当时处于什么状态"）。**

> 另一条排查结论顺便记一下：`ApplyUid` 那行有 `&& PropWriter.IsMounting`
> 的门槛，所以第 13 节那道屏蔽覆盖到了**全部** `ApplyUid` 场景——
> Update 路径上它压根不跑。一开始以为第 13 处漏了 Patch 那一半，查下来没漏。

---

每个修法在模型里都做成**开关**，关掉后跑同一批 **20000 条 × 24 步**序列必须失败：

| 关掉的修法 | 失败数 |
|---|---|
| 吞后回写 | 2262 |
| 回声泄漏（选中类） | 1629 |
| 吞后回写（ToggleSwitch） | 1817 |
| 回声泄漏（ToggleSwitch） | 2472 |
| 受控写入后不撤销登记（其余九个站点的模型） | 2273 / 1036（两种接线形状） |
| `EchoGuard` 退回强键 `Dictionary` | `EchoLifetimeTests` 两条变红（GC + 内存各一） |
| 契约缺「卸载」那一半 | 真实源码 24 处变异全部哑掉 |
| 第六道契约（按控件建的表要摘）缺失 | 真实源码 25 处摘除语句，删任意一条都无人报警 |
| 挂载期回执不屏蔽（`x:Uid` 那一笔） | 挂载即回调 1 次、state 被 resw 凭空改写；关掉修法后 `MountOrderTests` 两条变红 |
| 静默窗缺失（改区间把受控值夹了） | `RangeCoerceTests` 随机序列 19982 条 / 93998 次假回调（只关这一个开关） |
| 登记夹取后的值退回旧写法 | 同一批序列 8545 条 / 9814 次假回调（只关这一个开关） |
| 静默窗缺失（改兄弟属性 / 重建 items 把选中牵走） | `SiblingWriteTests` 随机序列 19997 条 / 95009 次假回调（只关这一个开关） |
| 重建 items 后不补发受控选中值 | 同一批序列 18529 条 / 44268 帧丢选中（只关这一个开关） |
| 就绪闸不看 `IsLoaded` | 卡死 430 条、重复回调 11014 条 |
| 就绪闸不幂等订阅 | 重复回调 6630 条 |
| 纠正被绑到「有没有人监听」 | 混合序列 1039 条；纯无回调序列 **3961** 条 |
| ListView/GridView：受控写回不抑制回声 | **19368** 条序列出现"受控写回被当成用户输入" |
| ListView/GridView：没有越界守卫 | **18607** 条序列把写不进去的值写了下去 |
| ListView/GridView：改 items 期间不遮蔽 | **8432** 条序列凭空回调用户 |

（后三行是 `ListViewSelectionTests` 的反向对照：三个开关各自只红它该管的那一条，
另两条保持 0——只断言"至少有一条变红"的话，"哪个开关坏、坏在哪儿"会一直是一笔糊涂账。）

**这一轮顺手换了两条判据**（见下一节），同一批改法因此整体变敏感：
吞后回写 2262 → **3897**，陈旧检查 4384 → **4928**。旧的数字不是错的，
是它在新判据下显得太宽容了。

一条修不修都绿的用例等于没写。这个项目已在量具失真上栽过五次（`GetHashCode` 指纹 / 非泛型 `IEnumerable` / NuGet 与 ProjectReference 混用 / 符号链接日志 / 帧日志刷屏），所以新增用例必须自带"不修就坏"的对照。

---

## 新增的真实 WinUI 源码依据

- `ToggleSwitch` **不在 WinUI 2 的 `dev/` 树里**（它是 `Windows.UI.Xaml` 的 OS 控件）。真身在 `main` 分支的 XAML 内核：`ToggleSwitch_Partial.cpp:347-350`（`IsOn` 一变就 `OnToggledProtected()`）→ `ToggleSwitch.g.cpp:335-351` → `Partial.cpp:633` 的 `Raise`，**全程不检查模板是否已应用**；头文件里也没有 `m_blockSelecting` 那种闸门。所以 `Toggled` 对每一次 `IsOn` 变更都抛。
- `Selector`（`ListView` / `GridView` / `ComboBox` 的共同基类）同样在 `main` 分支的 XAML 内核
  `dxaml/xcp/dxaml/lib/Selector_Partial.cpp`：**受控写回同步抛事件**（`144-148` 的 DP 分派 +
  `EndChange` 里同步调 `InvokeSelectionChanged`）、**越界写入会整条 undo 并以 `E_INVALIDARG` 收尾**、
  **改 items 会牵动选中**。
  这一份是本机 `curl` 出网被拦的情况下经 WebFetch 取回的，**没有可落盘的本地副本**；
  行号只有 `144-148` 一处由两次窗口取回的重叠区互相印证过，其余按函数名引用（详见源码笔记第 12 节）。
- `ElementSoundPlayer` 连源码都没有（全局 OS API），事实取自文档：**桌面上的默认态 `Auto` 就是不响**，只有 Xbox 响。
- `NumberBox`（`release/2.8/dev/NumberBox/NumberBox.cpp`，WinUI 2 分支）**改边界会夹 `Value`**：`OnMinimumPropertyChanged` / `OnMaximumPropertyChanged` **各自**调一次 `CoerceValue`；后者在越界且 `ValidationMode == InvalidInputOverwritten`（idl 里该枚举第一项，即默认值）时 `Value(Minimum())` / `Value(max)`，随后 `OnValuePropertyChanged` 以**夹取之后**的值抛 `ValueChanged`。
- `Slider` 的真身 `RangeBase`（`main/dxaml/xcp/dxaml/lib/RangeBase_Partial.cpp`）：内核这份取到了，`Value` 变更 → `OnValueChangedImpl`（注释原文 *Raises the ValueChanged routed event.*）。**但"改边界会不会夹 `Value`"在生成代码 `RangeBase.g.cpp` 里，仓库没有、本机取不到** —— 这一条对 `Slider` 只有文档级依据（*"may be coerced"* + `Minimum` 备注里"Maximum 被设成等于 Minimum"那个字面例子），已在源码笔记里按强度分开记。
- `ListViewBase_SelectionMode`（`main/dxaml/xcp/dxaml/lib/ListViewBase_Partial.cpp`）：改 `SelectionMode` → `OnSelectionModeChanged`，注释原文 *will update all Selection related properties*。**它的函数体没取到**（文件截断），所以只知道"会动"，不知道"动成几 / 动几次"——这决定了修法只能是开窗。附带一条反向结论：`SelectionMode` **不在**基类 `Selector` 上（`Selector_Partial.cpp` 的 `OnPropertyChanged2` 只有六个分支，没有它）。
- `NavigationView::OnSelectionModelSelectionChanged`（`release/2.8/dev/NavigationView/NavigationView.cpp`）：早退条件是 `m_shouldIgnoreNextSelectionChange || selectedItem == SelectedItem() || !m_appliedTemplate`。清空 `MenuItems` 时 `SelectedItem()` 还是旧值、`selectedItem` 已是 `nullptr` ⇒ **不早退** ⇒ 继续走 `RaiseSelectionChangedEvent`；而"经由 API 选中"之所以大多没回声，正是因为这个条件**成立**。
- `SettingsExpander.IsExpanded` 这一次用的是**本地**参考源码（`tools/ctk-ref`，可复查）：
  `Expanded` / `Collapsed` 事件在 `SettingsExpander.Events.cs:12`、`:17` 被声明，
  但**全树没有任何一处 `.Invoke`**（死事件）；用户点表头走
  `SettingsExpander.xaml:263` 的 TwoWay 绑定 → `SettingsExpander.cs:69` 的
  `OnIsExpandedChanged`，而那里只抛 automation peer 事件（`:72`）。
  结论是 `IsExpanded` **接不上**受控，不是还没接。
  同一轮也确认了 `tools/winui2-ref/dev/` **没有 Expander**，所以原先注释里
  "官方用 counter-echo"那句话**没有依据**，已删。

详见 `docs/winui2-source-notes.md`。

---

## 示例侧（`Reactor.Gallery`，不进包）

设置页接上了真实后端，不再是本地 state 假装保存：

- **主题**：真正落到根元素并由宿主同步到窗口与背景材质，重启保持；
- **清理缓存**：真的删 `TemporaryFolder` 并回报条数；
- **控件音效**：接 `ElementSoundPlayer`（`State = On/Off`），带**试听**按钮可自证。

**诊断页新增「⑥ 回声 / 闸门计数（看增量）」**

真机复验以前要靠肉眼：点一下控件，看界面动没动、回调涨没涨。这里把它换成读数——
点「记基线」→ 点控件 → 回来看**只列出涨了的那些计数**及其含义：

| 涨的是 | 这一发事件的去向 |
|---|---|
| `notExpected` | 按用户输入放行（正常路径，后面应该跟着回调） |
| `matched` | 被判成**回声**吞掉 ⚠ 用户操作却涨它 = 「点了没反应」的当场证据 |
| `suppressed` | 被「未就绪 / items 重建中」拦下（只该在页面刚出现或换数据源时涨） |
| `sealed` | 受控写入没等到回声、登记被撤销（本版那 12 处修复在工作） |
| `expired` | 登记超窗作废；`mismatch` = 有登记但值不等（多为真实输入的中间态） |
| **一项都没涨** | 事件压根没到框架，去 Input 通道看有没有 Input 行 |

**输入页新增了「改区间」的取证入口**：一个上限可变的滑块（当前值 80）+ 「上限 → 40」按钮 +
一行 `OnValueChanged 次数`。点「上限 → 40」时那个计数**不该涨**——涨了就说明控件自己
夹出来的那一发又被当成用户输入回调出去了（第 16 节复发）。这一条和列表页那条是同一种
取证思路：**专挑在简单操作下看不出来、只有某个特定动作才出问题的那些**。
（此时 state 仍是 80、滑块停在 40 是预期的：声明值越界时控件只会夹到边界，
要看的是计数不涨。）

**列表页新增了受控 ListView 的取证入口**：一个「只留前 3 项 / 显示全部 8 项」按钮，
和一行 `OnSelectedIndexChanged 回调次数`。换数据源时那个计数**不该涨**——
涨了就说明控件自己发出的那一发又被当成用户输入回调出去了（第 12 节那个 bug 复发）。
这一条之所以值得单独做：它是本轮三个 bug 里唯一在简单页面上**看不出来**的——
用户点一下再点一下都正常，只有"数据源变了"才出问题。

之所以要看**增量**而不是绝对值：这些计数单调只增，一次操作的区别只在涨了哪几项。
而且要配轮询（500ms）才有意义——「点了没反应」那一次恰恰是 state 没变、不重渲染的，
没有轮询的话这屏数字停在**上一次**，在最需要取证的时候失明。轮询要带 cleanup 停表，
否则页面下树后计时器会把整棵树钉住。

这块新算法（`DiagnosticsCounters`）虽然是纯字符串处理，也拉进 net10 测试工程做了
**仪器自检**（10 项）：`ReactorLog.Counters()` 的格式一变，解析器会安静地拆出空字典，
界面上表现为"永远零增量"——而那会被读成"事件没到框架"。所以第一条例用**真的**
`EchoStats.Snapshot()` / `ReadyStats.Snapshot()` 拼样本，钉住八个键名，
并要求键名在两个 Stat 类源码里各有出处；反向对照是换掉分隔符后哨兵必须报警。
（也因此 `ReadyStats` 从依赖 XAML 的 `ReadyGate.cs` 里拆了出来——不然它 Link 不进
net10 测试工程，样本就只能照抄字面量，格式一改两边一起改，哨兵就瞎了。）

---

## 变更文件

**框架（会进包）**

- `Reactor.uwp/Internal/SelectionGate.cs` —— 新增 `ShouldExpectEcho`、`ShouldRestoreAfterSuppress`
- `Reactor.uwp/Internal/EchoGuard.cs` —— 新增 `CancelIfUnconsumed`、`EchoStats.Sealed`；装登记的表从 `Dictionary` 换成弱键 `WeakTable`（详见第 8 节）
- `Reactor.uwp/Internal/Handlers.Controls.cs` —— 新增 `SelectionRestore`；`RadioButtons` / `ComboBox` / `ToggleSwitch` 三处接入纠正与条件登记；越界守卫；两处 `Dispatch` 的 `callback` 早退挪到门尾（详见第 11 节）
- `Reactor.uwp/Internal/Handlers.Basic.cs` —— `TextBox.Text` / `CheckBox.IsChecked` / `Slider.Value` 三处写入后撤销没人领的登记
- `Reactor.uwp/Internal/Handlers.Input.cs` —— `PasswordBox` / `AutoSuggestBox` / `NumberBox` 三处同上
- `Reactor.uwp/Internal/Handlers.Controls.cs` —— `RadioButton.IsChecked`、`NavigationView.SelectedItem`（Mount + Update）四处同上
- `Reactor.uwp/Internal/Handlers.Template.cs` —— 面包屑数据源取证改用 `CtlId`（去掉会骗人的 `GetHashCode`）；新增 `ItemsSourcePolicy` 的引用自检；数据源载体钉进 `WeakTable`
- `Reactor.uwp/Internal/Seq.cs`（新） —— 序列 diff 的**唯一**事实来源，可被 net10 测试 Link
- `Reactor.uwp/Internal/ItemsSourcePolicy.cs`（新） —— 数据源引用的引用相等策略 + 运行时自检
- `Reactor.uwp/Internal/PropWriter.cs` —— `SequenceEqual` 改为转发到 `Seq`（去掉第二份实现）
- `Reactor.uwp/Internal/ReadyPolicy.cs`（新） —— 就绪闸「要不要订阅 `Loaded`」的判据，纯函数可被 net10 测试 Link
- `Reactor.uwp/Internal/ReadyGate.cs` —— `Arm` 改用 `IsLoaded` 当权威信号 + 幂等订阅（详见第 10 节）
- `Reactor.uwp/Internal/ReadyStats.cs`（新） —— 从 `ReadyGate.cs` 拆出，好让计数能被测试 Link；新增 `AlreadyLoaded`
- `Reactor.uwp/Internal/SelectionPolicy.cs`（新） —— 受控下标的**写回策略**（越界不许下发），纯整数判据，可被 net10 测试 Link；`ComboBox` / `RadioButtons` / `ListView` / `GridView` 共用一份
- `Reactor.uwp/Internal/Handlers.Controls.cs` —— `ItemsViewHandler` 接上全部受控设施（回声抑制 + 改 items 期间遮蔽 + 越界守卫 + `Unmount` 清理）；`ComboBox` / `RadioButtons` 的直接写回改用 `SelectionPolicy`
- `Reactor.uwp/Reactor.uwp.csproj` —— 包描述注释补上第 11、12 节（原注释只写到"三个 bug"，会让人以为这一版就只修了那三条）
- `Reactor.uwp/Internal/Reconciler.cs` —— `RebindTextChanged` 的闭包加「挂载期 / 未 `IsLoaded` 不承认回执」（第 13 处：`Localization.ApplyUid` 那笔写落在订阅之后，详见第 15 节）
- `Reactor.uwp/Internal/EchoGuard.cs` —— 新增**静默窗** `Silence(control)`（`Consume` 在窗内一律判为回声）与计数 `EchoStats.Silenced`（详见第 16 节）
- `Reactor.uwp/Internal/RangePolicy.cs`（新） —— 受控数值的夹取判据（登记 / 下发都用夹取后的值），纯 `double` 运算，可被 net10 测试 Link
- `Reactor.uwp/Internal/Handlers.Basic.cs` —— `Slider` 的 `Update`：写 `Minimum` / `Maximum` 时开静默窗；受控写入改用 `RangePolicy.Coerce`
- `Reactor.uwp/Internal/Handlers.Input.cs` —— `NumberBox` 同上（`ApplyRange` 的调用点开静默窗）；`PasswordBox` 的 `MaxLength` 写入开窗（第 15 处 ④，无源码依据、按代价不对称处理）
- `Reactor.uwp/Internal/Handlers.Controls.cs` —— 第 15 处 ①②③⑤：`ItemsViewHandler` 写 `SelectionMode` 开窗；`NavigationViewHandler` 的 `ApplyMenuItems` 开窗**且**重建后补发受控选中值、`PaneDisplayMode` 开窗；`RadioButtonHandler` 的 `GroupName` 开窗
- `Reactor.uwp/Elements/Factories.New.cs` —— `ListView(...)` 加一个带 `selectionMode` 的重载（可选，不破坏现有调用），供示例取证

**示例与文档**

- `samples/Reactor.Gallery/` —— 新增 `SoundService.cs`；`AppSettings.cs` / `SampleShell.cs` / `Pages/SettingsPage.cs` 接真实后端；`Pages/DiagnosticsPage.cs` 的面包屑改为可增减条目（取证入口），新增「⑥ 计数增量」面板（含轮询与 cleanup）；`Pages/ListsPage.cs` 的 ListView 加「只留前 3 项」按钮与回调计数（第 12 节的取证入口）
- `samples/Reactor.Gallery/Pages/DiagnosticsCounters.cs`（新） —— 计数增量的算法，不碰 XAML，因此能被 net10 测试 Link
- `docs/winui2-source-notes.md` —— 第 5~8 节，及第 2 节新增的「2.4 前提核实与遗留疑问」
- `docs/release-notes/alpha.6.md` —— 本文件

**测试（不入生成器产物）**

- `tests/Reactor.Core.Tests/ControlledSelectionSim.cs`、`ControlledToggleSim.cs`、`BreadcrumbSim.cs`、`ConditionalRebindSim.cs`
- `tests/Reactor.Core.Tests/SelectionGateTests.cs`、`ToggleEchoTests.cs`、`BreadcrumbItemsTests.cs`、`RebindEchoTests.cs`、`EchoContractTests.cs`
- `tests/Reactor.Core.Tests/EchoLifetimeTests.cs`（新）—— 键的生命周期：GC 断言 + 内存对照
- `tests/Reactor.Core.Tests/DiagnosticsCountersTests.cs`（新）—— 诊断显示屏的仪器自检（含格式哨兵）
- `tests/Reactor.Core.Tests/ReadyArmTests.cs`（新，含 `ReadyArmSim`）—— 就绪闸：8 条穷举 + 2 条定向 + 20000 条随机序列，两组反对照各自计量
- `tests/Reactor.Core.Tests/ListViewSelectionSim.cs` + `ListViewSelectionTests.cs`（新）—— `Selector` 形状（无模板闸门、越界写入被拒、改 items 牵动选中）的受控闭环仿真：7 条定向 + 20000 条 × 24 步随机序列，三个开关各自反向对照
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增第四道契约「写了受控属性 + 有回执通道 ⇒ 三件套齐全」（合成样本两层自证 + 真实源码 36 处变异）；`TopClass` 正则补上 `abstract`（此前 `ItemsViewHandler` 整个类不在任何契约的视野内）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增第五道契约「用户可改属性的写回点必须在 `EchoProne` 或 `UncontrolledByDesign` 里」（真实源码 7 处变异 + 登记承重自证）；回执通道的判据从"出现过事件名"收紧成"真的有订阅"（`Visibility.Collapsed` 此前会被误判成 `IsExpanded` 的通道）
- `Reactor.uwp/Internal/Handlers.Template.cs` —— `ExpanderHandler` / `SettingsExpanderHandler` 的 `IsExpanded` 注释改为如实描述（`defaultValue` 非受控语义 + 本地源码依据），删掉原先那句无出处的"官方用 counter-echo"
- `Reactor.uwp/README.md`（**打进 NuGet 包**）—— 受控站点数从写死的"7 处"改成机器可校验的标记 `<!-- CONTROLLED-SITES: 12 -->`（由 `EchoContractTests` 比对扫描结果，改了不更新就红）；「状态」一节从"首个 alpha"更新到 alpha.6，并写明 AOT 在本机未打通及原因
- `ControlledSelectionSim.cs` —— 异步回写改为"独立队列 + 快照"；选中类序列加入随机帧 flush；无回调时也照做判定与纠正；新增 `Explained` / `Faithful` 两条判据取代原先的一刀切豁免
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增第六道契约「按控件建的表（或 `reconciler.Rebind*`）登记了必须在 `Unmount` 里摘掉」（合成样本五层自证 + 真实源码 25 处逐条变异；新增服务类 `Arm`/`Disarm` 配对一路）；`UnmountRange` 抽出 `MethodRange`，补上**跨行表达式体**的识别（`ButtonHandler.Unmount` 此前被判成空方法体）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增第七道契约「handler 之外写用户可改属性的地方必须逐一登记，且登记指向的中和手段必须真的还在源码里」；扫描口径从 `Handlers.*.cs` 扩到 `Reactor.uwp` 全树（新增 `FrameworkFiles` / `Relative`），接收者必须被声明成控件类型（否则 `hook.Value` 这类内部字段会淹没真违约）
- `tests/Reactor.Core.Tests/MountOrderSim.cs` + `MountOrderTests.cs`（新）—— 挂载顺序模型：5 条定向（含反事实"把那笔写挪到订阅之前"证明病因是顺序、以及已知边界如实记录）
- `samples/Reactor.Gallery/Pages/InputsPage.cs` + 两处 `Strings/*/Resources.resw` —— 新增 `x:Uid` 文本框取证入口（`DiagUidBox`）：挂载时 `OnChanged` 次数该停在 0
- `tests/Reactor.Core.Tests/RangeCoerceSim.cs` + `RangeCoerceTests.cs`（新）—— 「改区间把受控值夹了」的行为模型：定向 8 组 + 两组无关对照 + 20000 条 × 8 步随机序列，两个开关各管一段、分别反向对照（19982 / 8545 条）
- `tests/Reactor.Core.Tests/EchoGuardTests.cs` —— 静默窗本身三条性质（窗内判回声、可嵌套、窗内不吃掉为真实写入准备的登记）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增第八道契约「写 `Minimum` / `Maximum` 必须在静默窗里」（合成样本六档 + 真实源码变异两个方向：删窗 / 把窗挪到写入之后）
- `tests/Reactor.Core.Tests/DiagnosticsCountersTests.cs` + `samples/Reactor.Gallery/Pages/DiagnosticsCounters.cs` —— 计数键名八个 → 九个（`silenced`）
- `tests/Reactor.Core.Tests/SiblingWriteSim.cs` + `SiblingWriteTests.cs`（新）—— 「改兄弟属性 / 重建 items 把受控选中值牵走」的行为模型：定向 10 组 + 无关对照 + 20000 条 × 8 步随机序列，两个开关各管一段、分别反向对照（19997 / 18529 条），外加三条"分段正确"的断言
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增第九道契约「非受控属性的写入必须在窗里或登记为惰性」（第八道的泛化；合成样本五档 + 真源码变异两个方向 + 惰性登记承重对照）；顺带修掉扫描器"多窗方法串窗"的假绿
- `samples/Reactor.Gallery/Pages/ListsPage.cs` —— 列表页加「切到禁止选中 / 切回单选」取证入口（复用同一个回调计数）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增第十道契约「集合写入（`Items.Clear()` / `MenuItems.Add(…)` / `ItemsSource =`）必须在抑制块里或登记为惰性」（补第八、九道的**形状**盲区——那两道只认赋值形式）；惰性登记按（类名, 属性名）；合成样本七档 + 真源码变异（**跨文件**计数）+ 惰性登记承重对照；顺带修掉扫描器两处假绿：调用点正则 `(?<![\w.])` 把带接收者的调用点全排除、跨文件变异只在同文件数违约
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增**第十一道契约**「受控选中类必须逐件答复判据簇五件套」（合成样本三档 + 真源码变异 15 处 + 豁免反向对照）；顺带修掉第九道"只认 `Silence` 窗、不认 `Rebuilding` 持续标记"的失聪
- `Reactor.uwp/Internal/Handlers.Controls.cs` —— `NavigationViewHandler` 接上判据簇：写回走 `SelectionPolicy.ShouldApply`、登记走 `SelectionGate.ShouldExpectEcho`、重建改用 `Rebuilding` **持续标记**（替换原来的 `Silence` 时间窗），并按类名登记两件豁免（详见第 19 节）
- `samples/Reactor.Gallery/Pages/ListsPage.cs` —— 列表页加 NavigationView「换菜单 / 切显示模式」取证入口
- `tests/Reactor.Core.Tests/RebuildEchoSim.cs` + `RebuildEchoTests.cs` —— 新增「重建 items 之后补发受控值那一发」的行为模型（Link 真 `EchoGuard` / `SelectionGate` / `SelectionPolicy`）：定向两档 + 反向对照三档（只关修法 / 不补发 / 控件压根不动）+ 用户操作对照 + 随机序列 200 轮；**它把第 19 节的理由从"异步"纠正成了"回读值猜不准"**
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 第十一道从五件套变**六件套**（新增 `NotReady` 一件）；新增**类级变异**（整类删掉某一件的全部落点，21 组）——补"一个类里有多处落点、逐行删不动"那个洞
- `Reactor.uwp/Internal/Handlers.Controls.cs` —— `NavigationViewHandler` 的两个事件入口（`SelectionChanged` / `ItemInvoked`）补上 `!ReadyGate.IsReady(control)` 早退（详见第 20 节）
- `Reactor.uwp/Internal/Reconciler.cs` —— **第 22 节事故后重建**：`RebindTextChanged` 的闭包恢复第 15 节那笔屏蔽（`PropWriter.IsMounting || !textBox.IsLoaded`）+ `ReactorLog.Gate`，日志文案从旧包的 dll 字符串堆里捞回；补 `using Reactor.Uwp.Hosting`（原文案与注释是重写的，功能面由 427 项测试与 `MountOrderSim` 验证等价）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 第九道惰性登记从**属性名**改成**（类名, 属性名）**（与第十道拉平，24 条 / 12 个类）；新增两条带牙的检查：**逐条撤登记每条都必须承重**（挡"往表里塞没人用的条目"那种反向腐烂）、**同名属性在没登记的类里写必须报警**（证明颗粒度真改了）；合成样本改为自带豁免表，不再往真登记表里塞假条目；第十一道的扫描口径从 7 个文件拉到全树 66 个；修两处过时措辞（套件实为六件、计数提示里"其中"其实不是子集）（详见第 21 节）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增**第十二道契约**：事件订阅必须幂等（表驱动守卫 / 同方法配对退订 / 登记一次性），全树 35 个订阅点 = 12 + 15 + 8，豁免集合必须**恰好等于**登记表；合成样本四档；真源码变异从订阅点反查保护者（8 处守卫 + 15 处配对全部承重）。另把变异与第七道 `Break()` 的还原改成**逐字节**（详见第 23 节）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增**第十三道契约**「三件套的相对次序必须是 登记 → 下发 → 撤销」（合成样本四档 + 真源码 12 处登记点 × 2 个方向 = 24 处**对调**变异全部承重）；变异改用对调而非删行，从而能加一条**反向对照**：同一批变异第一道必须一条都看不见（实测 0 处），证明这不是重复防线；新增 `MutateSwap`（同样逐字节还原）（详见第 24 节）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增**第十四道契约**「回声判据的结果必须真的用于拦截」（合成样本四档 + 真源码 12 处 × 2 方向 = 24 处变异全部承重 + **两条反向对照**：同一批变异第四道必须看不见、把 `Consume` 换名第四道必须报警）；判据二从"体内第一行"改为"整个块体 + 改判必须是条件里出现过的标识符"，修掉 `ToggleSwitchHandler.Guard`（先记日志再 `return`）的假红；`Mutate` 抽成 `MutateCounted` 支持**多个计数器**（详见第 25 节）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增**第十五道契约**「抑制用的窗开了必须关，且关在 `finally` / `Dispose` 上」（合成样本四档 + 真源码 5 处 `using` + 4 处 `finally` = 9 处变异全部承重）；新增单向标记登记表（`FocusRequested` / `Ready`，实测集合必须恰好等于登记表）；反向对照确认第八 / 九道认的是 `.Silence(` 这个串、**不认 `using`**，因此看不见"窗没关"（详见第 26 节）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增**第十六道契约**「同一张回声表的 `Expect` / `Consume` / `Forget` 必须作用在**同一个控件**上」（合成样本四档 + 真源码 36 处换参数变异全部承重，第二、四道反向对照实测 0 反应）；**第一版（登记↔清理按表配对）经反向对照查出是第二道的超集，已整块撤销**（详见第 27 节）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增**第十七道契约**「`Expect` 登记的值必须就是实际下发的那个值」（合成样本三档 + 真源码 11 处换右值变异全部承重，第十三道反向对照实测 0 反应）；NavigationView 的 `SelectedItem`（对象写入、下标登记）登记为间接口径豁免（详见第 28 节）
- `tests/Reactor.Core.Tests/EchoContractTests.cs` —— 新增**第十八道契约**「抑制（静默窗 / 持续标记）罩住的写入必须就是它自己那个控件」（合成样本三档 + 真源码 9 处换作用对象变异全部承重，第八 / 九道反向对照实测 0 反应）；认三种被罩动作形状：赋值、集合动作、把控件交给 helper（详见第 29 节）
- 合计 **495 项通过，0 失败**（第 23 节新增 8 条、第 24 节新增 8 条、第 25 节新增 9 条、第 26 节新增 9 条、第 27 节新增 7 条、第 28 节新增 7 条、第 29 节新增 6 条、第 30 节新增 7 条、第 31 节新增 7 条）

> 第 13、14 两节只动了测试与文档，**没有改框架源码**，所以 `artifacts/` 里那份
> alpha.6 仍然有效（已复核：包内 dll `sha256` `b3cad63d…` 与本地构建一致，189859 字节）。
> 对照第 12 节那次——改了框架源码却没重打包，版本号是新的、行为是旧的。
>
> 第 21 节只动了测试与文档，本不需要重打包；但**第 22 节的自伤与恢复改到了框架源码**
> （`Reconciler.cs` 的第 15 节修复被重建回去），所以本轮**又重打了一次**
> （190862，`86a98f1e…`），并按两层缓存流程重验了消费方 x64 / arm64。

> `EchoContractTests` 把源码也纳入了被测对象：它直接读 `Reactor.uwp/Internal/Handlers.*.cs`
> 检查每条 `Expect` 是否有人负责撤销、每个守卫是否在 `Unmount` 里被 `Forget` 且有人 `Consume`。
> 它自己也带两道自证：合成样本（缺 `Forget` / 没写 `Unmount` 必须红）、
> 以及**照着真实源码逐行删**（24 处，一处删一行）必须全部指名报警。
> 以后新增受控属性照抄同一份接线却漏了配套，这里会红。
>
> 第三道契约 `RestoreIsNotGatedOnListener` 是特例：仿真**够不到** `Handlers.Controls.cs`
> （UWP 类型编不进 `net10`），所以那边只能靠源码扫描守。把早退塞回门口做过两次变异，
> 两次都指名到行号变红。

---

## 19. NavigationView：补发受控值那一发，Expect 兜不住（第十一道契约）

> **2026-10-06 修订**：这一节最初把理由写成"那一发是异步的、时间窗罩不住"。
> 建完行为模型（`RebuildEchoSim`）之后发现**依据错位**，理由已改写。
> 修法本身不变，但承重在哪一档变了，见下面 19.0。

**症状**：NavigationView 换一次菜单项（或「左侧 ↔ 顶部」切换）时，
`OnSelectedIndexChanged` 凭空多回调一次；选中态也可能被清空。

**根因**：补发受控值那一发<b>落在抑制区间外面，而且它的回读值猜不准</b>。

`NavigationView` 的菜单由 `ItemsRepeater` 承载，整批替换之后控件会自己重算选中——
重算成什么只有控件知道。旧代码的形状是：

```csharp
if (menuChanged)
{
    using (SelectionEcho.Silence(control)) { ApplyMenuItems(control, newElement.MenuItems); }
}                                            // ← 窗在这里就关了
if (inRange && (menuChanged || oldElement.SelectedIndex != index))
{
    SelectionEcho.Expect(control, index);    // ← 补发站在窗外，只能靠 Expect
    control.SelectedItem = control.MenuItems[index];
    SelectionEcho.CancelIfUnconsumed(control);
}
```

补发时控件若真的停在写入的那个值，`Expect` + `Consume` 这一对兜得住；
**若控件自己收敛成了别的值，登记的值就匹配不上**，那一发直接跑进用户回调。
这正是第 18 节那条原则的又一个实例：

> `Expect` 要求"我知道写下去会回读出什么值"；
> **收敛成什么只有控件知道的那一类，只能开窗（或开持续标记）**。

### 19.0 行为模型把"理由"钉死在哪一档

`RebuildEchoSim` 用<b>真</b> `EchoGuard` / `SelectionGate` / `SelectionPolicy`
跑这个次序，两个开关分开计数：

| 补发之后控件的行为 | 只罩 `Clear + Add`（旧形状） | 罩到补发完（现形状） |
|---|---|---|
| 停在写入的那个值（`ReapplyConverges = true`） | 不漏（`Expect` 兜住了） | 不漏 |
| **自己收敛成别的值**（`ReapplyConverges = false`） | **漏 1 次** | 不漏 |

所以修法承重的是<b>第二行</b>，不是"异步"。另外单独验过一档：
把"清空选中"那一发延后到下一帧（`DefersToNextFrame`），
它<b>照样不漏</b>——那一发是取消选中，判据零本来就接得住，与区间无关。
**"时间窗罩不住异步后果"这条理由在本处不成立**（第 18 节 18.5 那条结论本身没被推翻，
只是不该套在这里）。真正与"异步"有关的是第 20 节那一处。

**修法**：换成持续标记 `Rebuilding`，且**开到受控值补发完**才关：

```csharp
if (rebuild) Rebuilding.Set(control, true);
try
{
    if (modeChanged) control.PaneDisplayMode = ToPaneMode(newElement.PaneDisplayMode);
    if (menuChanged)  ApplyMenuItems(control, newElement.MenuItems);
    ApplySelectedItem(control, newElement.SelectedIndex);   // 补发也在门内
}
finally { if (rebuild) Rebuilding.Set(control, false); }
```

`SelectionChanged` 与 `ItemInvoked` 两个入口都在最前面查这个标记——
重建同样会走 `ChangeSelection → RaiseItemInvoked`，那一发也不是用户。

### 19.1 五件套：三件接上、两件登记

`NavigationViewHandler` 是四个受控选中控件里**唯一一件判据都没接**的。
第十一道契约把它顶出来之后，逐件答复：

| 件 | 处置 | 依据 |
|---|---|---|
| `SelectionPolicy.ShouldApply` | **接上** | 控件没有 `SelectedIndex` 属性，当前值取 `MenuItems.IndexOf(SelectedItem)`（`-1` = 无选中）；判据本身照旧共用 |
| `SelectionGate.ShouldExpectEcho` | **接上** | 重建中不下发 → 不登记，避免留下陈旧期望 |
| `Rebuilding` 持续标记 | **接上** | 本次修复（替换原来的 `Silence` 时间窗） |
| `SelectionGate.Decide` | **登记豁免** | 事件参数不是 `SelectionChangedEventArgs`（没有 `AddedItems`），`SelectedSomething` 接不上；但三道判据各有兑现：判据零靠 `SelectedItemContainer` 守卫（`RaiseSelectionChangedEvent` 在 `nextItem` 为 nullptr 时**不设** `SelectedItemContainer`）／判据二靠 `Rebuilding`／**判据一原先写的是"靠 `!m_appliedTemplate` 早退天然兑现"，那条理由不成立，已由第 20 节改成 `ReadyGate.IsReady` 接上** |
| `ShouldRestoreAfterSuppress` | **登记豁免** | 它只对 `CancelTransient` 生效，那是 RadioButtons `OnChildUnchecked`（cpp:431）特有的"点已选中项 → `Select(-1)`"；NavigationView 点已选中项走 `ItemInvoked`，`SelectionChanged` 根本不抛 |

**"豁免"不是"不管"**：两件都写了可追溯到源码的理由，且契约带反向对照——
把豁免整份拿掉，真源码必须立刻冒出违约（否则那份登记是多余的）。

### 19.2 顺带逼出第九道自己的失聪

改用持续标记之后，第九道立刻报了两处**假**违约
（`PaneDisplayMode = …` 与 `item.Icon = …`）。
原因是它判定"在抑制块里"时只认 `SilenceOpen`（`.Silence(`），
不认 `Rebuilding.Set(…, true)`——而第十道两者都认。

改法：第九道的 `SilencedBefore` 与第十道的 `InGuardBlock` 用同一个口径。
**这是已修的失聪，不是源码退步**——单独记一笔，是因为同一种"两道口径不一致"
还挂着另一条（第九道按属性名登记、第十道按（类名, 属性名））。

### 19.3 反过来看：这条契约为什么现在才出现

前十道守的全是**写入**那一侧：写了受控值得有人领回声、写了别的东西得在抑制块里。
它们的共同前提是"这个 handler 已经接了判据"。于是存在这样一种漏法：
每一条写入都规规矩矩地开了窗（第八、九、十道全绿），判据本身却一件没接。
`NavigationViewHandler` 就是这样躺在仓库里的——
它靠 `Silence` 窗 + `SelectedItemContainer` 守卫一直没出事，
但那是运气：窗罩得住的部分恰好是同步的那一半。

---

## 20. NavigationView：事件入口漏了「就绪」这一问（第十一道补第六件）

**症状**：进页面时 `OnSelectedIndexChanged` 凭空回调一次——
而那一发的值正是我们自己刚写进去的受控值。模板通常拿它切页，
表现就是"初始页被改掉"或"启动时选中跳一下"。

**根因**：源码（`NavigationView.cpp`，`OnSelectionModelSelectionChanged`）里那条
早退是三选一，第二条是：

```cpp
// 2. Template has not been applied yet. SelectionModel's selectedIndex state will get
//    properly updated after the repeater finishes loading.
if (m_shouldIgnoreNextSelectionChange || selectedItem == SelectedItem() || !m_appliedTemplate)
{
    return;
}
```

上一轮我把 `!m_appliedTemplate` 读成了"未就绪期间一发都不抛，所以判据一不用接"。
**读漏了后半句**：早退只是把那一发**推后**，不是取消——
`selectedIndex` 会在 **repeater 加载完之后**被"正确更新"，
那一发届时会真的抛出来，而且它选中的是**实项**（`nextItem` 不是 nullptr），
判据零（`SelectedItemContainer` 守卫）**拦不住**。

关键在次序：**repeater 是控件模板子树的一部分，子先于父**——
repeater 的 `Loaded` 早于控件自己的 `Loaded`。
所以那一发回来时 `ReadyGate.IsReady(control)` 仍是 `false`，
这一问**问得到**它。而第 19 节改完之后的 `Rebind` 里只查了 `Rebuilding`，
没查 `IsReady`：`ReadyGate.Arm` 装上了，却没有人问它——半套接线。

**修法**：两个事件入口（`SelectionChanged` / `ItemInvoked`）在最前面补一问。
重建同样会走 `ChangeSelection → RaiseItemInvoked`，所以两个入口都要补：

```csharp
if (Rebuilding.TryGetValue(control, out var busy) && busy) { return; }
if (!ReadyGate.IsReady(control)) { return; }   // ← 本次补的
```

**契约**：第十一道从五件套变**六件套**，新增 `NotReady` 一件
（认 `ReadyGate.IsReady`）。`ItemsViewHandler`（ListView / GridView）登记豁免，
理由是 `Selector` 没有 `RadioButtons` 那道 `m_blockSelecting` 模板闸门
（`OnSelectedIndexChanged` 的早退只问重入锁与 `IsInit()`），
**"未就绪"在 `Selector` 上没有答案可问**——不是省略，是没有那个问题。

顺带补了**类级变异**：有些件在一个类里有好几处落点
（`ReadyGate.IsReady` 在 `NavigationView` 里就有三处），
逐行删不动它——删一处剩下的还在，要求报警就是假红。
整类删掉该件的全部落点才算真变异。本轮类级变异 21 组，全部承重。

**行为模型**：`RebuildEchoSim` 新增 `MountDefersRestore` / `GateOnReady` 两档——

| 挂载期那一发延后回来 | 事件入口不问就绪 | 事件入口问就绪 |
|---|---|---|
| 假回调 | **1 次** | 0 次 |

---

## 21. 惰性登记的颗粒度：从「属性名」改成「（类名, 属性名）」

**这一节不改框架，只改量具。** 框架侧本轮零改动，所以没有重打包。

### 21.1 旧口径的问题

第九道（非受控属性写入要么在窗里、要么登记为惰性）的登记表是按**属性名**记的，
第十道从一开始就按**（类名, 属性名）**记。同一套概念两种颗粒度，笔记里一直挂着。

按属性名记的后果是：**一条登记，十二个类受益**。真源码里写 `Header` 的类有九个，
写 `Content` 的有四个——只要 `Content` 登记一次，`NavigationViewHandler` 里那笔
`nav.Content = …`（它不是标签，是**整棵当前页面子树**）也被免掉了。
同一个属性名在 A 控件上是文字、在 B 控件上是子树，按名字记等于把两种语义混成一条豁免。

改成按（类名, 属性名）记之后，真源码逐条逼出来的清单是 **24 条、12 个类**：

| 类 | 登记的属性 |
|---|---|
| `TextBoxHandler` | `Header` `PlaceholderText` |
| `CheckBoxHandler` | `Content` |
| `ComboBoxHandler` | `Header` `PlaceholderText` |
| `ToggleSwitchHandler` | `Header` `OnContent` `OffContent` |
| `RadioButtonHandler` | `Content` |
| `RadioButtonsHandler` | `Header` |
| `ItemsViewHandler` | `Header` `IsItemClickEnabled` |
| `NavigationViewHandler` | `Header` `IsPaneOpen` `IsSettingsVisible` `IsBackButtonVisible` `IsBackEnabled` `AlwaysShowHeader` |
| `PasswordBoxHandler` | `Header` `PlaceholderText` `IsPasswordRevealButtonEnabled` |
| `AutoSuggestBoxHandler` | `Header` `PlaceholderText` |
| `NumberBoxHandler` | `Header` |

`NavigationViewHandler/Content` **故意不登记**——那一笔在 `Mount` 里，本来就免检；
登记它反而会留一条"看起来有豁免"的假条目。

### 21.2 两把新牙

光把颗粒度改细还不够，这次补了两条反向对照：

- **逐条撤登记**：24 条豁免，撤掉任意一条，真源码里都必须立刻冒出至少一处违约。
  原来的"整份撤掉必须红"只证明了**整份**有东西在托着——二十几条里只要有一条真被写到，
  它就绿。这挡不住反向的腐烂：往表里塞没人用的条目（比如把某个其实落在窗里、
  根本不需要豁免的写入也登记上），整份对照照样绿，而那条假豁免会在将来真有人写它
  的时候免掉一次该报的警。**24 条逐条验过，每条都承重，且没有一条多余**
  （缺一条则"每一处写入都在窗里或已登记"那一项会红）。
- **同名属性在没登记的类里写 → 必须报警**：合成样本里两个类都写 `Header`，
  只登记其中一个，另一个必须报。这条是"颗粒度真的改了"的判据——
  换了个类照样不报，就说明还是按属性名在记。

  > 顺带一个做法上的修正：合成样本**不再往真登记表里塞假条目**。
  > 豁免表做成了可注入的参数，样本自带自己那份（`("DemoAnyHandler", "Header")`）。

### 21.3 顺带拉平第十一道的扫描口径

第十一道（判据簇）原来也只扫 `Handlers.*.cs` 七个文件，第十道扫全树 66 个。
扩到全树之后命中数**没变**（仍是 4 个受控选中类、0 违约）——
但"没扫到"和"扫了没问题"是两件事，前者随时可能变成后者。口径统一后是
「扫描 66 个文件，命中 4 个」。

### 21.4 两处措辞错误

- 判据簇实际是**六件**（第 20 节加了 `NotReady`），但契约的消息和注释里还在写
  「五件套」。套件数与文档不一致，等于给读的人一个错的计数。已全部改成六件。
- 第九道那条计数提示写的是「命中 19 处，其中 27 处走了登记」——这两个数
  **不相交**（19 处靠抑制块罩住、27 处走了登记，加起来 46 才是总数）。
  "其中"意味着子集，是错的。已改成「19 处靠抑制块罩住、27 处走了惰性登记」。

---

## 22. 一次自伤与两条被它逼出来的结论

> 这一节记录的是**我（AI）自己造成的一次破坏**与从它身上查出来的两件事。
> 写下来是因为两条结论都影响发版动作，不该只留在对话里。

### 22.1 发生了什么

为了查"上一份包与本地构建的 `sha256` 为什么不一致"，我用一段临时脚本
**直接改了真源码**（`Reactor.uwp/Internal/Reconciler.cs`：删掉一行看能不能复现那个哈希），
随后一步写错把它**截断成 0 字节**（62081 字节的工作区版本没了）。

恢复过程：

1. `git status` 显示该文件是 `M`（相对 `HEAD` 有未提交改动）——
   所以 `git checkout` 会连 alpha.6 的改动一起丢掉，不能用；
2. 文件已空，`git diff HEAD` 把 **HEAD 那一侧的全文**当删除行输出 ——
   用它能精确还原出 HEAD 版本（57169 字节）；
3. 丢掉的 4912 字节是**从未提交过**的工作区改动，git 里没有；
4. 补回的判据来自契约：跑一遍测试，第七道立刻指名
   `RebindTextChanged 里找不到 PropWriter\.IsMounting\s*\|\|\s*!textBox\.IsLoaded`
   ——这就是第 15 节那笔修复，缺的正是它；
5. 日志文案从**上一份包里的 dll 字符串堆**里捞了回来
   （微软中间语言的 `#US` 堆里存着字面量）：
   `TextBox{0} 挂载期回执（IsMounting={1}，IsLoaded={2}），不算用户输入`。

**现状**：495 项测试全过、Gallery 0 警告 0 错误。功能面已验证等价，
但**注释与日志的措辞是我重写的，不可能与丢失版本逐字相同**。
若真机第 6 项（本地化文本框进页面 `OnChanged` 应为 0）有异常，优先怀疑这里。

### 22.2 结论一：跑测试时不要同时构建框架 —— 测试会临时改真源码

第七道的变异对照 `Break()`（`EchoContractTests.cs:3415`）是**往真源码里写**的：
删掉一行 → 重新判定 → `finally` 还原。这是它"有牙"的代价。

于是：**在测试进程把那一行临时删掉的窗口里，任何并发的框架构建都会把变异后的源码编进去**。
我把"构建框架"与"跑测试"放在同一批并发调用里，就撞出了三个互相不同的 dll 哈希。
发版动作上要记住：**打包之前不要并发跑测试**；打包之后也不要。

### 22.3 结论二：`sha256` 比对必须限定在同一次构建内

同一份源码，`--no-incremental` 连编两次也能给出不同哈希
（本轮实测：`00b7eb0d…` 与 `0804e8a0…`）。所以发前校验表第 4 行的正确读法是：

- **能比**：打包之后**立刻**拿 `bin/` 里那份与包里那份比 —— 它们是同一次构建的产物
  （本轮都等于 `86a98f1e…`）；
- **不能比**：拿文档里记的哈希去对另一次构建，对不上是正常的，**不代表包过期**；
- **跨次要比就比语义**：大小 + 符号集（本轮包内与另一次本地构建都是 350720 字节、
  2540 个共有符号，差异只落在 MVID / PDB GUID 那几十个字节上）。

判定"包是不是旧构建的产物"，看的应当是**内容**（比如修复用符号在不在包里、
那句 `挂载期回执` 字面量在不在），而不是哈希。

---

## 23. 第十二道契约：订阅的那一侧（订阅必须幂等）

**这一节没有修 bug。** 它是把此前完全没人管的那一半纳入防线，并顺手记下
排查过程中发现的两个隐患（一个在框架之外，一个在排查工具自己身上）。

### 23.1 前十一道全从"写"出发

第四~十一道守的都是**往控件上写什么、写在什么窗里**。它们的共同前提是
"这个 handler 已经接好了判据"。而受控闭环的另一半——**接**的那一侧
（`X.Event += h`）——没有任何一道契约在看。

危险只在一种形状上：订阅点**每次 Patch 都会走到**，却没有幂等保护。
`Rebind` 正是这种：它每次 Patch 都被调用，用来把回调换成捕获了新 state 的闭包。
里面一个裸 `+=` 就是每渲染一次叠一层，用户点一下回调跑 N 遍。
症状是"点了之后状态跳来跳去"——与受控值被写歪同一类表现、不同入口。

### 23.2 判据与口径

幂等有两种写法，**两种都认**（只认一种会逼人改写法而不是改语义）：

| 写法 | 判据 | 真源码 |
|---|---|---|
| (a) 表驱动 | `+=` 罩在 `if (!Table.ContainsKey(control))` 里，只订一次、之后换表里的委托 | 12 处 |
| (b) 配对退订 | **同一个方法体内**存在 `.<同一事件> -=` | 15 处 |
| (c) 一次性 | 两条都不是 → 必须登记，写明"所在方法每个实例至多跑一次" | 8 处 |

> **（b）的口径是"同一个方法内"，不是"同一个类里"。** 这个差别是这一道的关键：
> `+=` 在 `Update`、`-=` 只在 `Unmount` 这种真漏，放到"同一个类"就蒙混过关了——
> 而它正是这一道要拦的形状。合成样本里专门留了一档反向对照盯着它。
>
> 代价是 `BreadcrumbBar` 得走登记：它的 `+=` 在 `Mount`、`-=` 在 `Unmount`，
> **安全靠的是"Mount 每实例只跑一次"，不是靠配对**。登记里写明了这一点。

全树扫出 **35 个订阅点 = 12 + 15 + 8**，实测的（文件, 事件）豁免集合
**必须恰好等于**登记表（多一条算"失效"、少一条算"没人认领"，
所以既漏不掉新的、也塞不进没用的）。

### 23.3 牙：变异瞄得准才咬得到

真源码逐处拆保护，**8 处守卫 + 15 处配对，全部承重**。

第一版我按"所有含 `ContainsKey` 的行"和"所有 `-=` 行"两批去拆，立刻踩到两个坑：

- **已豁免订阅自己的 `-=`**（`BreadcrumbBar.Unmount` 那条）——那个订阅点本来
  就是靠 "Mount 只跑一次" 过关的，拆它的 `-=` 当然不该有事，判"没报"是**假红**；
- **`+=` 与 `-=` 在同一行**（`InputApplier.ApplyKeyEvents` 把两个委托当参数传进
  `RebindKeyEvent`）——整行注释掉连订阅点一起没了，违约数**不升反降**，
  照样判"没报"，而且看起来跟真漏一模一样。

改成**从订阅点反查"是谁在保护它"**：守卫就改守卫那一行；配对就把方法体内
该事件的**全部** `-=` 一起改掉（改一处留一处仍然成对，会假绿）。
拆法一律用"替换行内字符串"，不整行注释。

### 23.4 顺带修好的隐患：排查工具自己会改坏源码换行

变异是**真往源码里写**再还原（`finally`）。原来的还原用 `File.WriteAllLines`，
它一律按 `Environment.NewLine` 重拼 —— 而这个仓库是**混合换行**的
（`Core/` 11 个、`Elements/` 19 个、`Internal/` 20 个、`Hosting/` 3 个文件是裸 LF）。

也就是说：哪天有人往 `SelectionPolicy.cs` 这种 LF 文件里加一个订阅，
跑一次测试就会把整个文件悄悄改成 CRLF，`git status` 会把整个文件显示为已改
——**与"变异没还原"长得一模一样**，非常容易被误判成第二次事故。

已改成**逐字节还原**（`File.ReadAllBytes` → 还原时 `WriteAllBytes`），
`Break()`（第七道那支）同样处理。本轮验证方式也升级了：跑完测试后用
`sha256sum -c` 逐个核对被变异的 9 个文件，**全部 OK**。

---

## 24. 第十三道契约：三件套的**相对次序**

### 24.1 前十二道漏掉的是什么

受控写入的三件套是「`Expect` 登记 → 下发 → `CancelIfUnconsumed` 撤销」。
前十二道盯了其中两步，都**没有盯次序**：

- **第一道**只问「`Expect` 之后 25 行窗口里有没有 `CancelIfUnconsumed` 这个串」
  （`LineIsAnswered`），位置一概不问；
- **第四道**只问「三件套齐不齐」。

而次序错了是同一个 bug 的两副面孔：

| 排错的方式 | 机制 | 结局 |
| --- | --- | --- |
| 撤销排在下发**之前** | 那一发回声此刻还没到，撤销看见「没人领」，把刚登记的期望**立刻抹掉** | 控件同步抛事件时表里已空 → `NotExpected` |
| 登记排在下发**之后** | 写入那一刻表里没有期望 | 同样 `NotExpected` |

两副面孔的终点完全一样：**框架自己的写入被当成用户输入回调出去**
→ `setState` → 重渲染 → 表现为抖动、或覆盖掉输入框里刚敲进去的字。

两处源码在三件套上**一件不少**，所以第一、四道在那两种写法下**照样全绿**。

### 24.2 判据与口径

对每个登记点，取它之后第一条**同一个 guard** 的撤销，要求两者之间
**存在一条以同一控件为接收者的下发**（`control.Prop = …`）。

- **接收者必须比对**：区间里一条写给别的控件的赋值（`other.Value = …`）
  同样长得像下发，不比对就会让「受控下发其实在撤销之后」蒙混过关；
- **没有撤销的不算次序违约**——那是第一道的职责。两条分工，
  免得同一处漏子被两个扫描器各数一遍、把变异计数搅浑。

**现状：真源码 12 处登记点，0 处越位。** 这条契约是守护型的，
它钉住的是「以后新增一处受控写入时，次序必须一并答复」。

### 24.3 牙：24 处对调变异 + 反向对照

变异手法是**对调两行**，不是删行：

- 「登记 ↔ 下发」对调 → 登记排到了下发之后（12 处）；
- 「撤销 ↔ 下发」对调 → 撤销排到了下发之前（12 处）。

**24 处全部承重**（违约数 0 → 1）。

用对调而不是删行，是为了能同时做**反向对照**：三件套**一件不少**，
只是位置变了——于是可以拿第一道去量同一批变异，验证它**一条都看不见**
（实测 0 处）。这一条同时证明了两件事：

1. 第十三道咬的就是次序，不是「少了一件」；
2. 它不是重复防线——第一道对此确实是瞎的。

### 24.4 认不出的边界（不假装能拦）

这条契约认的是**相对位置**，认不出「区间里那条下发到底是不是受控下发」。
那一层由第四道（三件套齐全）与第五道（受控属性必须在 `EchoProne` 名单里）兜着。

---

## 25. 第十四道契约：读侧 —— 回声判据的**结果**必须真的用于拦截

### 25.1 第十三道管写侧，这一道管读侧

三件套的「登记 → 下发 → 撤销」排对了，事件回来那一侧照样可以全废：
`Consume` 返回 `true` 表示「这是我们自己写的回声，别回调用户」，
而这个返回值**没有任何类型或编译器机制强迫你用它**。两种写法让它形同虚设：

| 写法 | 为什么废 |
| --- | --- |
| `Echo.Consume(control, value);` 当独立语句 | 返回值直接扔掉，等于没判 |
| `if (Echo.Consume(…)) { Log(); }` 后面照样 `callback(value)` | 判了却不拦 |

两者结果一模一样：**框架自己的写入每次都被当成用户输入回调出去**
→ `setState` → 重渲染 → 抖动 / 覆盖掉刚敲进去的字。

**判据**：① 调用必须出现在 `if (` 的条件里；
② 那个 `if` 的块体里必须有**停下来**的动作——`return`，
或把**条件里出现过**的标识符改判掉（`verdict = SelectionVerdict.Echo` 那 4 处）。

**现状：真源码 12 处判据点，0 处形同虚设。**

### 25.2 判据二的两个坑（都是本轮踩出来的）

- **只看「体内第一行」会假红**。`ToggleSwitchHandler.Guard` 的写法是
  先记一行 `ReactorLog.Gate("…回声，吞 IsOn=…")` 再 `return`——
  第一行不是 `return`，按"第一行"判就误报了（本轮第一次跑正是报了它）。
  改成看**整个块体**。
- **只认「块内有赋值」又会假绿**。块里随便一句 `var tag = …` 也是赋值，
  算成"停下来"等于没查。所以改判必须改的是**条件表达式里出现过的标识符**。

### 25.3 牙：24 处变异 + 两条反向对照

变异两个方向各 12 处，**都不删 `Consume` 这个串**（它还在源码里）：

1. 整段搬进 `if` 体内当独立语句 → 返回值被丢弃；
2. 把块体**整段**换成一句日志 → 判了却不拦。
   （只改第一行没用：`ToggleSwitch` 那处是「先记日志再 `return`」，
   改掉第一行后面的 `return` 还在，照样判合规。）

**24 处全部承重。** 于是可以同时做两条反向对照：

- **(a)** 同一批变异，第四道（三件套齐不齐）必须**一条都看不见** —— 实测 0 处。
  因为 `Consume` 字符串还在，第四道照样判绿；
- **(b)** 反过来把 `Consume` **换名**（站点消失），第四道必须**立刻红** —— 实测 12 处全报。

两条合起来把分工钉死：**第四道盯存在性，第十四道盯用法**，互补而不重复。

---

## 26. 第十五道契约：开了的窗必须关，且关闭路径要扛异常

### 26.1 这一道盯的是「关没关」，不是「罩没罩住」

第八、九、十道问的都是「这次写入有没有落在窗里」，它们**默认窗是会关的**。
而窗一旦开了不关，代价是**永久**的：

| 形状 | 不关的后果 |
| --- | --- |
| `Silence` 深度计数器不 `Dispose` | 该控件**之后所有用户输入**一律被判成"我们自己写的" → 点了没反应 |
| `Rebuilding` 持续标记不关 | 该控件**之后所有选中事件**一律被吞 |

两者都是"某个控件从此哑掉"，而且**只在前面抛过一次异常时才现形**——
所以关闭必须走 `finally`（或 `using` 的 `Dispose`），不能走正常路径。

**判据（两种形状）**：
① `X.Silence(ctl)` 必须包在 `using (` 里——`Dispose` 就是关窗；
② `Rebuilding.Set(ctl, true)` 的配对 `Set(ctl, false)` 必须落在 `finally` 块里。

### 26.2 单向标记必须登记

不是每个 `Set(ctl, true)` 都是窗。扫出来 6 处标记开启，其中 4 处是 `Rebuilding`（窗），
另外 2 处**语义上单向，开了本来就不该关**：

| 标记 | 位置 | 为什么不用关 |
| --- | --- | --- |
| `FocusRequested` | `InputApplier.cs` | 一次性幂等标记：请求过焦点就永远为真，关掉反而会在下次挂载时重复请求 |
| `Ready` | `ReadyGate.cs` | 就绪位：Arm 之后单向置真，取消走的是 `Disarm` 整条摘除，不是置回 `false` |

沿用第十道那条思路：**默认全管，例外登记**，且实测集合必须**恰好等于**登记表
（同时挡"漏登记"和"塞失效条目"）。

**现状：5 处静默窗 + 4 处持续标记，0 处会漏；2 个单向标记全部有人认领。**

### 26.3 牙与反向对照

变异两个方向，**都不删 `.Silence(` / `Rebuilding.Set(` 这两个串本身**：

- 拆掉 `using`（5 处）→ 窗不再 `Dispose`；
- `finally` 换成 `catch`（4 处）→ 只在抛异常时关，正常返回时根本不关。

**9 处全部承重。**

反向对照有一条特别值得记：`SilenceOpen` 这个正则认的是 `.Silence(` 这个串，
**不认 `using`**。所以把 `using` 拆掉之后，写入在第八、九道眼里**仍然被罩着**——
实测确认它们的判据照旧返回 `true`。也就是说"窗没关"这件事前三道**一条都报不出来**，
这一道不是重复防线。

---

## 27. 第十六道：三件套必须作用在**同一个控件**上

### 27.1 这一道盯的是「作用在谁身上」，不是「齐不齐」

`Consume` 靠「登记值 == 回读值」判回声，而**键是控件**。第二、四道数的是
**表名**（`TextEcho` 有没有人 `Consume`、有没有在 `Unmount` 里 `Forget`），
第十三道比对的是**下发语句的接收者**（`control.Value = …` 里那个 `control`）。
**没有任何一道看过 `Consume` 括号里的第一个参数。**

而写错这个参数是**静默失效**：键不同 → 表里查不到 → 一律 `NotExpected` →
框架自己写的每一次都被当成用户输入回调出去。症状与"压根没写 `Consume`"
一模一样，但代码上三件套一件不少——正是第二、四道全绿却仍在抖动的那一类。
典型错法是嵌套 handler 里 `control` 与 `parent` / `sender` 混用，或复制粘贴改了一半。

**判据**：按类、按表收集三件套各自的控件参数，要求它们的**并集只有一个元素**。

**现状：12 组三件套（36 处），0 处串台。** 同样是守护型，本轮没修 bug。

### 27.2 本轮最大的收获：反向对照把**我自己写的重复契约**抓了出来

第十六道的第一版不是这个——它判的是「登记过的表必须在 `Unmount` 里被同一张表
`Forget`」，看着是个没人管的维度，合成样本五档也全绿。

但反向对照一接上就烧了：抹掉 `Forget` 之后**第四道也报了**。查下去才发现，
**第二道 `LifetimeViolations` 早就按表名判了「漏 `Forget` / 清错表 / 没 `Unmount`」**
（`Handlers` 那 12 处早就配了真源码逐行变异）。第一版第十六道是它的**超集**——
重复防线，直接整块撤掉重写。

也就是说：**反向对照不只是"证明自己不是重复防线"的仪式，它真的能抓出你自己
刚写出来的重复契约。** 如果只写合成样本、不做反向对照，这条会一路绿灯地混进
契约网，占着 9 条测试却什么都拦不住——而且**比没有更糟**，因为它会让人以为
这个维度已经有人管了。

### 27.3 牙与反向对照

变异把三件套的**第一个参数**从 `control` 换成 `sender`（36 处：
12 `Expect` + 12 `Consume` + 12 `Forget`），表名与三件套**一件都没动**。
**36 处全部承重。**

反向对照两条，**都实测 0 反应**：

- 第二道（`LifetimeViolations`）按表名判，看不见参数换没换；
- 第四道（三件套齐全）同样只看表名。

两条合起来说明：**老契约盯的是"有没有这张表"，第十六道盯的是"这张表作用在谁身上"**。

### 27.4 认不出的边界（不假装能拦）

按**类**切分，所以"登记在 A 类、消费在 B 类"的跨类写法看不见（当前源码没有）。
另外只比参数**名字**，不追别名——`var c = control;` 之后用 `c` 登记，这条认不出来。

---

## 28. 第十七道：登记的值必须就是**下发**下去的那个值

### 28.1 第十三道管次序，没人管值

第十三道要求「登记与撤销之间存在一条**以同一控件为接收者**的下发」，
**完全不看下发的是什么值**。于是：

```csharp
ValueEcho.Expect(control, a);
control.Value = b;              // ← 登记的是 a，下发的是 b
ValueEcho.CancelIfUnconsumed(control);
```

在它眼里完全合格。而这是**静默失效**：回读的是下发之后控件**实际变成**的值
（`b`），登记的却是 `a` → 永远对不上 → 一律 `NotExpected` →
框架自己的每一次写入都被当成用户输入回调出去。**三件套一件不少、次序也对**，
前十六道全绿。

**判据**：取登记与撤销之间第一条以该控件为接收者的下发，右值必须与登记值一致。

**现状：12 处登记，11 处同口径，1 处间接（已登记豁免），0 处真对不上。** 守护型，本轮没修 bug。

### 28.2 间接口径要登记：NavigationView 的 `SelectedItem`

`Handlers.Controls.cs:1584` 登记的是**下标**，下发的却是 `SelectedItem`（对象）：

```csharp
SelectionEcho.Expect(control, index);
control.SelectedItem = index < 0 ? null : control.MenuItems[index];
```

这不是 bug——NavigationView **只有 `SelectedItem`（对象）这一个写入口**，
而回读侧的 `Guard` 已经把对象转回 `int` 下标，两端仍是同一口径。
（那处注释还说明：这一发大概率根本没有回声，所以 `CancelIfUnconsumed` 紧跟。）

沿用第十、十二、十五道的老办法：**默认全管，例外登记**，实测集合必须恰好等于登记表。

### 28.3 牙与反向对照

变异把下发的**右值**换掉（11 处），接收者、属性、三件套、次序**一件都没动**。
**11 处全部承重。**

反向对照：第十三道的 `WriteBetween` 只比对 `WriteProp` 的**接收者**分组，
右值根本没进正则 → 换掉右值后那条下发在它眼里**仍然在窗口里**，
**实测 0 反应**。分工钉死：**第十三道盯"写在哪"，第十七道盯"写的是什么"**。

> 本轮又踩了一次第十二道那条老坑：豁免对象（NavigationView）自己的配套代码
> **不能拿来变异**——它本来就是"不同"，换了右值仍然不同，改了不承重（假红）。
> job 收集时得显式跳过豁免对象，最终 11 处而非 12 处。

### 28.4 认不出的边界（不假装能拦）

判的是**字符串相等**，所以 `Expect(control, v)` + `control.Value = v + 0`
这类"语义相等、写法不同"会误报（当前源码没有）。
另外它比的是**下发值 vs 登记值**，不是"回读值 vs 登记值"——
回读侧在事件回调里、跨作用域，静态比不了，那一层靠真机验证。

---

## 29. 第十八道：抑制窗必须罩在**它自己那个控件**上

### 29.1 第八、九道问的是「有没有落在窗里」，没问「窗罩的是谁」

`SilencedBefore` 认的是 `.Silence(` 这个**串**，**不看它开在哪个控件上**。于是：

```csharp
using (ValueEcho.Silence(other))     // ← 窗开在别的控件上
{
    control.Minimum = 0;             // ← 在第八、九道眼里"这一发被罩住了"
}
```

而抑制跟回声判据一样是**按键查表**的——键错了就静默失效：那一发**根本没人管**，
控件照常抛事件，回声不被抑制 → 抖动。代码上窗开着、写入也在窗内，第八、九道全绿。

这与第十六道（三件套的控件口径）是同一个形状换了个位置：
**"有没有窗"和"窗罩在谁身上"是两个维度。**

**现状：5 处静默窗 + 4 处持续标记，0 处罩错人。** 守护型，本轮没修 bug。

### 29.2 被罩住的"动作"有三种形状，只认赋值会漏掉一半

第一版只认 `X.Prop =`，结果 9 处里只有 7 处进视野，剩下两处是：

| 形状 | 实例 |
| --- | --- |
| 集合动作 | `control.Items.Clear()` / `control.Items.Add(item)`（持续标记罩的是重建列表） |
| 交给 helper | `reconciler.PatchItems(control, …)` / `ApplyRange(control, newElement)` |

后一种里控件是**实参**，改控件的活儿在 helper 内部。少了这一半，
4 处持续标记里有 1 处（`Handlers.Controls.cs:1156`）压根进不了视野。

### 29.3 本轮踩的两个正则坑（都是假红）

**坑一：贪婪量词把第一个实参取成了最后一个。**

```csharp
(?:[^()]*?,\s*)?([A-Za-z_]\w*)\s*[,)]      // ← `?` 是贪婪的
```

对 `ApplyRange(control, newElement)`，那个可选组**优先匹配一次**，
于是抓到的是 `newElement` 而不是 `control`。改成取整串实参再找标识符。

**坑二：`Xxx(y)` 会把 `if (modeChanged)` 也认成调用。**

一次报出三处假违约（`if (modeChanged)` / `if (menuChanged)` / `if (rebuild)`）。
按函数名黑名单排掉关键字。另外字符串字面量要先抠掉——
`ReactorLog.Gate("hello")` 里的 `hello` 会被当成"在改别的控件"。

**坑三（判据排序）：有接收者的动作必须判完就走。**

`control.Items.Add(item)` 的实参是 `item`，若让实参形状去复判同一行，
就会被误报成"在改别的控件"——可它明明在改 `control`。
所以按"有没有明确接收者"排序：赋值 / 集合动作判完 `continue`，
**只有前两种都没匹配时**才看实参形状。

### 29.4 牙与反向对照

变异把抑制的**作用对象**换掉（9 处 = 5 窗 + 4 标记），
写入、三件套、`using` / `finally`**一件都没动**。**9 处全部承重。**

反向对照：正因为 `SilenceOpen` 认的是串，换掉控件之后那条写入在第八、九道眼里
**仍然被罩着**——拿 `SilencedBefore` 直接量，**实测照旧返回 `true`（0 反应）**。

### 29.5 认不出的边界（不假装能拦）

只比参数**名字**，不追别名；helper 内部到底改的是不是这个控件，
看的是"实参里有没有它"，不是真的跟踪调用。

---

## 30. 第十九道契约：静默窗必须开在**自家**那张表上

第十八道管的是"窗罩在谁身上"，这一道管的是"窗开在**谁家**"。
`EchoGuard` 里 `_pending` 与 `_silenced` 都是**实例字段**——
静态字段 `ValueEcho` / `TextEcho` / `SelectionEcho` … 各是一张独立的表，
而 `Consume` 判回声的第一件事是看 `_silenced[control] > 0`，
也就是**只认自己那张表的窗**。

### 30.1 为什么前十八道看不见

`SilenceOpen` 这个正则认的是 `.Silence(` 这个**串**，表名根本不在正则里。
于是：

```csharp
using (TextEcho.Silence(control)) { control.Maximum = 9; }
```

写 `Maximum` 会把受控的 `Value` 夹进新区间（`CoerceValue`），
那一发回声走的是 `ValueChanged` → `ValueEcho.Consume`；
而 `ValueEcho` 的窗**没开**，`TextEcho` 的窗开了却永远不会有回声落在它身上。
结局是 `NotExpected` → 回调出去 → 抖动。
代码上窗开着、写入也在窗内，第八、九、十八道全绿。

### 30.2 判据

取窗所在的那个类，收集它 `Consume` 用的表——`Consume` 是"接"的那一侧，
回声回来走的就是它；类里没有 `Consume` 时退回 `Expect` 的集合。
窗开的表**必须落在这个集合里**。

**现状 5 处静默窗，全部开在自家那张表上，0 处越界**——守护型，本轮没修 bug。

### 30.3 牙与反向对照

两个方向、各 5 处，**共 10 处变异**，控件参数、写入、`using` / `finally`
一件都没动，**全部承重**：

- **方向一**：换成一个真实存在、但本类不消费的表（复制粘贴改一半的典型）；
- **方向二**：换成一个压根不存在的表（改名漏了一处）。

反向对照两条**都实测 0 反应**：第十八道只比控件参数（换表名不影响它），
第八、九道只认 `.Silence(` 这个串（串还在就照旧判"被罩着"）。
分工钉死：**第十八道盯「罩在谁身上」，第十九道盯「开在谁家」。**

### 30.4 认不出的边界（不假装能拦）

按**类**切分，所以一个类里有多张表时，只要窗开在其中任何一张被 `Consume`
的表上就放行（当前源码每个类只有一张）。
另外它不判断"窗里那一写到底会扰动哪个受控属性"——那一层靠类级归属兜。

---

## 31. 第二十道契约：闸门判完之后必须真的**拦下来**

第十四道盯的是回声那一半（`if (… .Consume(…))`），这一道盯闸门那一半
（`if (SelectionGate.Suppress(verdict))`）。

### 31.1 为什么第十四道看不见它

第十四道的判据要求"以 `if (` 开头**且**行里有 `.Consume(`"。
`if (SelectionGate.Suppress(verdict))` 这一行**没有 `Consume`**，
所以它从来没进过第十四道的视野——契约文件里 `Suppress` 这个 token
此前出现 **0 次**。

而把这一半做废的代价是**中间态泄漏**：闸门吞的是四类——
`NotReady`（模板没就位）、`Rebuilding`（整批换 items）、
`CancelTransient`（取消选中那一发）、`Echo`。判了却不拦，
这四类就**原样回调给用户** → setState → 重渲染 → 把用户刚选中的值拽回去。

### 31.2 判据

块体内必须有一句**直接属于这个块**的 `return`：

- 嵌在更里层的 `if` 里**不算**——那个 `if` 不满足时照样往下走；
- 单行形式 `if (x) return;` **也不算**——那是条件退出，不是"判了就拦"。

第二条是本轮**收紧**出来的：初版只查"行里有没有 `return` 这个词"，
于是 `if (false) return;` 能一路绿灯混过去。收紧成"这一行**以 `return` 起头**"。

**现状 3 处闸门，全部真的退出了**——守护型，本轮没修 bug。

### 31.3 牙与反向对照

两个方向 × 3 处 = **6 处变异**，全部承重：

- **方向一**：把那句 `return;` 换成一行日志（判了却不拦）；
- **方向二**：降级成 `if (false) return;`（看着"块里明明有 return"）。

反向对照：第十四道**实测 0 反应**——它只认 `.Consume(` 那一行。

**本轮弃用的方向**：原本想用"把条件换成恒假"当方向二，
但那样 `SelectionGate.Suppress(` 这个串就没了，
**站点自己消失、违约数不升反平**——与第十八道记过的是同一个陷阱。
**换条件 = 拆站点，不能当变异方向。**

### 31.4 认不出的边界（不假装能拦）

不看条件本身写对没有（`!Suppress` / `verdict != Pass` 这类取反认不出来），
只管"判成该吞之后有没有真的退出"。那一层靠 `SelectionGate` 自己的单测。

---

## 升级注意

无破坏性变更，公共 API 面没有变化（新增的都是 `internal`）。

**尚未在真机上验证。** 建议在升级前用 Gallery 跑一遍下面这些动作：

1. 点**当前已选中**的项 —— 选中态不应消失；
2. 展开折叠区后点**默认值** —— 应有反应；
3. 音效开关切到「开」再点**试听** —— 应能听到 `Invoke` 音；
4. 诊断页 🍞 面包屑点**加一项 / 减一项**，Items 通道里应看到**两个不同的 `#编号`**
   （同号 = 数据源引用没换 = 「面包屑条目不更新」复发）。
5. 列表页 ListView：先点「回调计数归零」，点列表里任意一项（计数变 1），
   再点「只留前 3 项」——**计数不该再涨**；
   **涨了** = 控件自己发出的那一发又被当成用户输入回调出去了（第 12 节复发）。
   若此时选中的是第 4 项及以后，选中态落到"无"是正常的（那个下标已经不存在）。
6. 输入页「本地化文本框」：一进页面 `OnChanged 次数` 就该是 0，**敲字才涨**
   （一进页面就是 1 且框里变成 resw 的串 = 第 15 节复发）。
7. 输入页「改区间」那个滑块：点「回调计数归零」→ 拖一下（次数变 1）→
   点「上限 → 40」——**次数不该再涨**（涨了 = 夹取那一发被当成用户输入，第 16 节复发）。
   此时 state 仍是 80、滑块停在 40 是预期的：声明值越界时控件只会夹到边界。
8. 列表页 ListView：点「回调计数归零」→ 点一项（次数变 1）→
   点「切到禁止选中」→ 再点「切回单选」——**这两个动作期间计数都不该涨**
   （涨了 = 改 `SelectionMode` 那一发被当成用户输入，第 17 节复发）。
   切回单选后原来那一项应当还在选中态——
   若选中丢了，是"重建 items 后没补发受控选中值"那一半（第 17 节 ② 的第二半）。
9. 列表页最下方 NavigationView「重建取证」：点「导航回调计数归零」→
   点菜单里任意一项（计数变 1）→ 点「只留前 3 项菜单」→
   再点「切到顶部导航」——**这两下计数都不该涨**
   （涨了 = 菜单重建那一发被当成用户输入，第 19 节复发）。
   点回「恢复 8 项菜单」后，原来选中的那一项应当还在选中态。

上面的每一步都可以不看界面、只看读数：**进页先点一次「⑥ 计数增量」里的『记基线』，
操作后再回来**。第 1、2 项期望 `notExpected+1`（这一发按用户输入放行）并跟着一次回调；
若出现 `matched+1` 而没有回调，说明它仍被当成回声吞掉——那句话会直接写在屏幕上。
第 4 项期望 `Items` 通道两行带不同编号；两项都不涨则是事件没到框架（命中/布局环节）。

---

## 分批提交清单（供手动 git 用）

改动共 **66 项**（29 改 + 37 新增，`git status --porcelain -uall` 数出来的）。按"每一批单独能编译、能过测试"切成 6 批。

（上一版这里写 52 项，是把 `?? docs/` 这种**被折叠的目录**当成一条算的；用 `-uall` 展开才是真数。）

次序不是按目录切的，是按**符号依赖**定的——下面每条都标了它凭什么在这个位置：

| 批次 | 内容 | 凭什么在这一批 |
|---|---|---|
| **1 观测/工具地基**（纯新增，不碰行为） | `Internal/CtlId.cs`、`Internal/Seq.cs`、`Internal/ItemsSourcePolicy.cs` | 三者都只引用自己，无任何依赖 |
| **2 就绪闸与判据簇**（纯新增） | `Internal/ReadyPolicy.cs`、`ReadyStats.cs`、`ReadyGate.cs`、`SelectionGate.cs`、`SelectionPolicy.cs`、**`RangePolicy.cs`**、`Hosting/ReactorLog.cs` | **必须同一批**：三份 `Ready*` 互相引用；`SelectionGate → ReadyGate`；`SelectionPolicy → SelectionGate`；`ReactorLog → ReadyStats` |
| **3 框架行为改动（会进包）** | `Handlers.Basic/Controls/Input/Template/Virtual.cs`、`EchoGuard.cs`、`Reconciler.cs`、`PropWriter.cs`、`WinRtValue.cs`、`InputApplier.cs`、`ReactorApplication.cs`、`ReactorHost.cs`、`Elements/Dialog.cs`、`Elements/StyleSheet.cs`、`Reactor.uwp.csproj`、**`Reactor.uwp/README.md`** | 依赖第 1、2 批（`PropWriter → Seq`；`Handlers.Template → CtlId/Seq/ItemsSourcePolicy/ReactorLog`；`Handlers.Controls → SelectionPolicy/SelectionGate/ReadyGate/CtlId/ReactorLog`） |
| **4 测试 + 被 Link 的示例算法**（不入包） | `tests/Reactor.Core.Tests/*` 全部（含 `Program.cs`、`csproj`）、`samples/Reactor.Gallery/Pages/DiagnosticsCounters.cs` | 依赖前三批。测试工程 **Link 了** `EchoGuard.cs`、`SelectionGate.cs`、`SelectionPolicy.cs`、**`RangePolicy.cs`**、`Seq.cs`、`ItemsSourcePolicy.cs`、`ReadyPolicy.cs`、`ReadyStats.cs` 与 `DiagnosticsCounters.cs` —— 所以 `DiagnosticsCounters.cs` **不能**留在第 5 批，它得跟着测试走 |
| **5 Gallery 示例其余部分**（不入包） | `AppSettings.cs`、`SoundService.cs`、`Pages/DiagnosticsPage.cs`、`Pages/LiveProbe.cs`、`Pages/SettingsPage.cs`、`Pages/ListsPage.cs`（含第 17 节的「改模式」取证入口）、**`Pages/InputsPage.cs`**（含第 16 节的「改区间」取证入口）、**`Strings/en-US/Resources.resw` / `Strings/zh-CN/Resources.resw`**、`SampleShell.cs`、`Reactor.Gallery.csproj` | 依赖第 2、3 批；`DiagnosticsPage` 用到第 4 批的 `DiagnosticsCounters`；`InputsPage` 的 `x:Uid` 取证入口依赖第 3 批 `Reconciler` 的挂载期屏蔽（resw 与页面必须同批，否则入口读不到资源） |
| **6 文档与技能** | `docs/release-notes/alpha.6.md`、`docs/winui2-source-notes.md`、`.workbuddy/skills/uwp-runtime-evidence-debugging/SKILL.md`、`.gitignore` | 不含代码，不影响编译 |

两处容易踩的次序约束，单独拎出来：

- **`README.md` 必须落在第 3 批（或更早），不能跟文档一起走。**
  第 4 批的 `EchoContractTests` 会读它、比对里面的受控站点数；README 落在测试之后，
  第 4 批那个 commit 单独 checkout 出来就是红的。
- **`DiagnosticsCounters.cs` 属于第 4 批，不属于第 5 批**（它被测试工程 Link）。
  按目录分会把 `samples/` 整个丢到最后，测试批次立刻编不过。

验证方式说明白：次序是**按符号引用查出来的**（逐个新类型查"谁定义、谁引用"），
不是按名字猜的；最终态已验证——框架 0 警告 0 错误、Gallery 0 警告 0 错误、测试 **495** 项全过。
**没有**逐个 commit checkout 出来编译过（本机不做 git 操作），所以这是静态依赖分析的结果，
不是逐批实测。

---

## 发版流程与发布后待办

**本机已做的发前校验**（勿省，两条各自拦过真事故）：

| 校验 | 本机结果 | 漏了会怎样 |
|---|---|---|
| `machine=0x14c`（纯 IL） | 通过（原生桥各为 `0x8664` / `0xaa64`） | 带 `-p:Platform=x64` 打包 → arm64 消费方 `CS8012` |
| `runtimes/win-x64` + `win-arm64` 原生桥都在包里 | 通过 | 历史上漏装过一次，CI 为此加了专用步骤 |
| 修复用类型真在包 dll 里（`SelectionRestore` / `ShouldExpectEcho` / `CancelIfUnconsumed` / `ItemsSourcePolicy` / `SelectionPolicy` / `RangePolicy` / **`Silence`** / **`ApplySelectedItem`**） | 通过 | 版本号变了但改动没进包，消费方行为照旧 |
| 包内 dll 与本地构建 `sha256` 一致 | 通过（本轮 `86a98f1e…`，190862 字节；**打包后立刻**比对） | 包是旧构建的产物，改的东西没进去<br>（第 22.3 节：这条只能限定在同一次构建内比） |
| 消费方（`Reactor.Template`）临时升到 `alpha.6` 的本地包能编译 | 通过：`x64` 与 `arm64` 两个平台各 0 错误（已还原回 `alpha.5`）<br>第 13、16、17 节各重打包之后<b>各又验了一遍</b>（刷新本地源 → 清缓存 → 两个平台 → 还原）。
<br><b>第 18 节这一轮逮到一次真事故</b>：第 17 节打包（11:12）之后框架源码又被改过
（11:35），到本轮校验时包内 dll 是 `072bfd46…` 而本地构建已经是 `bd3abe31…` ——
**包过期了 23 分钟而没人发现**，发前校验表上还写着"通过"。
已重打（190717）并按两层缓存流程重验消费方（解出的 dll 确认是 `bd3abe31…`）。
<br><b>第 19 节又重打了一次</b>（190996，`d53e83c7…`）：那一节改的是框架源码
（`NavigationViewHandler` 接判据簇），打包是发版前最后一步，流程照旧走一遍
（刷新本地源 → 清两层缓存 → 删 `obj/` → x64 / arm64 → 解出的 dll 确认 `d53e83c7…` → 还原）。
<br><b>第 20 节又重打了一次</b>（190990，`803ddab8…`）：那一节给两个事件入口补了
`ReadyGate.IsReady`，同样是框架源码改动，流程再走一遍。
<br>这说明**"包内 dll 与本地构建一致"这条校验只在打完那一刻成立**，
之后任何框架改动都会让它失效 —— 它是<b>发版前最后一步</b>的校验，不是一次性的。
判定办法不变：每次发版前重跑一次 sha 比对，对不上就重打。<br>注意 <b>两层缓存</b>：`~/.nuget/packages/reactor.uwp/<版本>` 之外，本机还有一个本地源目录 `D://fluentapps//local-nuget`（`dotnet nuget list source` 里叫 `LocalFluentApps`）——<b>只清前者不够</b>，restore 会从后者拿回旧的，编译照样 0 错误，**验的是旧包**。这一轮真踩到了：清完缓存后消费方解出来的 dll 仍是 `71b55be2…`，把新包复制进本地源、再删缓存与 `obj/`，才变成 `b159cd4e…`。<br>判定办法：还原后去 `~/.nuget/packages/reactor.uwp/<版本>/lib/*/Reactor.uwp.dll` 上取 `sha256`，与包里那份比对 | 不知道本项目 API 面是否真能被外部消费，发上去才发现就晚了 |
| `project.assets.json` 里两个 RID 都解析到 `native` 资产、且导入了 `build\Reactor.uwp.targets` | 通过 | 包结构对但落盘链断了 → 真机上缺 `Reactor.Uwp.Native.dll` |

> 第 12、13、15、16、**19**、**20**、**22** 节的框架改动都落在打包之后，所以 `artifacts/` 那份是**重打**的
> （188292 → 189029 → 189059 → 190019 → 190542 → 190708 → 190717 → 190996 → 190990 → **190862**，十轮）。改了框架源码却忘了重打包 →
> 版本号是新的、行为是旧的，正是第 3、4 行要拦的事故，这一轮真的踩到了两次。
>
> 关于第 4 行的哈希：**每次重新编译 `MVID` 都是一个新的 GUID**，所以 dll 的
> `sha256` 必然变。这条比的是「包里这份 == 本机刚构建这份」，**不是**「包有固定指纹」；
> 拿文档里记的哈希去对下一次构建，对不上是正常的。
> 第 22 节实测得更细：**同一份源码 `--no-incremental` 连编两次也会给出不同哈希**，
> 所以比对必须限定在同一次构建内（详见 22.3）。

**待用户操作**

1. 真机跑一遍上面**十一项**（第 5 项是列表页 ListView 的回调计数，
   第 6 项是输入页 `x:Uid` 文本框的「OnChanged 次数」——**进页面就该是 0，
   敲字才涨**；若一进页面就是 1 且框里变成 resw 的串，就是第 15 节那个 bug 复发；
   **第 7 项**是输入页「改区间」那个滑块：点「回调计数归零」→ 拖动一下（次数变 1）→
   再点「上限 → 40」——**次数不该再涨**；涨了 = 夹取那一发被当成用户输入（第 16 节复发）。
   此时 state 仍是 80、滑块停在 40 是预期的：声明值越界时控件只会夹到边界）；
   **第 8 项**是列表页 ListView 的「切到禁止选中 / 切回单选」——
   两个方向各切一次，**回调计数都不该涨**（涨了 = 改模式那一发被当成用户输入，
   第 17 节复发）。切到「禁止选中」时选中态消失、切回来时恢复到原来的那一项
   是预期的——要看的是计数不涨、且切回来选中态还在）；
   **第 9 项**是列表页最下方 NavigationView「重建取证」的「导航回调计数」——
   点一项（次数变 1）之后，「只留前 3 项菜单」和「切到顶部导航」这两下
   **次数都不该涨**（涨了 = 菜单重建那一发被当成用户输入，第 19 节复发）；
   **第 10 项（第 20 节新增）**是**进页面那一下**——
   列表页是 NavigationView 之下的一个页面，切到列表页之后
   「导航回调计数」**就该是 0**，不该凭空是 1。
   是 1 = repeater 加载完补抛的那一发跑进了用户回调（第 20 节那个 bug）。
   这一项与第 9 项的区别：第 9 项看的"重建"，第 10 项看的"挂载"，
   两处走的是同一个事件入口、不同的闸门（前者靠 `Rebuilding`，后者靠 `ReadyGate`）；
   **第 11 项（第 23 节新增，用现成的探针）**：列表页 ListView 点「回调计数归零」后，
   **在几个页面之间来回切十几次**（让同一个控件经历很多次 Patch），
   再回来点一项——计数应当**恰好是 1**。
   是 N（且 N 随切换次数增长）= `Rebind` 里的订阅叠了 N 层，
   也就是第 23 节那条形状的真机表现。这一节没有改框架源码，
   所以它是纯回归验证——但它是唯一一个"前 23 节都没在真机上看过"的维度；
2. `git tag v0.1.0-alpha.6 && git push origin v0.1.0-alpha.6`（触发 `publish.yml`，
   或先 Actions → publish → 勾 dry-run 验 OIDC 链路）；
3. 包上 nuget.org 之后，把这三处的 `alpha.5` 改成 `alpha.6`——**漏改任何一处都不会报错**，
   只是新人照着 README 抄到旧版本：
   - `samples/Reactor.Template/Reactor.Template.csproj:59`
   - `Reactor.uwp/README.md:60`
   - `samples/README.md:6`
