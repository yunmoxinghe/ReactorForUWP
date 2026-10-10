# Reactor.Uwp 0.1.0-alpha.8

**主题：未就绪窗口里点下去的那一发，被"控件漂到 -1"挡在补发复查门外 —— 一次点击永久丢失。**

这是一颗典型的 Heisenbug：真机上"开日志时 11 次点击全部落盘，关日志一次都没落盘"，
而落盘本身（每条 `Info` 都同步开合一次文件，且在 UI 线程上）恰好把那个窗口拖过去。
**仪器一装上，现象就消失** —— 所以这一版不靠真机点击定位，靠的是仿真里的反向对照。

---

## 1. 症状与它的两面

设置页里改「应用主题 / 背景材质 / 导航栏位置」，点了之后：

- state 不变、设置不落盘；
- 控件被受控重放拉回旧值；
- 界面完全正常（控件在树上、可见、可点），不报任何错。

同一个症状在 alpha.7 里已经修过一次（"纠正盖掉用户刚选的新值"），**这次是它的另一副面孔**，
落在同一条链的下游。

### 1.1 为什么偏偏是这三个设置项（真机侧的结构印证）

`samples/Reactor.Template/MainPage.cs:340-395`：

| 设置项 | 控件 | 位置 |
|---|---|---|
| 应用主题 | `RadioButtons`（3 项） | **`SettingsExpander` 折叠区** |
| 背景材质 | `RadioButtons`（2 项） | **`SettingsExpander` 折叠区** |
| 导航栏位置 | `ComboBox`（2 项） | `SettingsCard`（页面导航后才挂载） |

前两项在折叠区里 —— `MainPage.cs:95` 的注释自己就写着"不展开就不进可视树"。
**这正是"未就绪窗口"最长寿的形状**：展开的那一瞬间，控件已经可见可点，
而 repeater 的 `Loaded`（`m_blockSelecting → false`）与控件自身 `Loaded` 派发到框架
之间还隔着一段时间。

第三项虽不在折叠区，但设置页是导航过去才建的，整页控件都要走一遍"挂载时还没进树"，
窗口只是更短。**窗口短到"开个日志就能拖过去"，正是它表现成 Heisenbug 的原因。**

### 1.2 排查过的阴性假设：还有别的控件也在丢点击吗

把所有受控选中 handler 过了一遍，确认**只有两个**走"未就绪吞事件 + `Deferred` 补发"
这套机制，且两者都已修：

| handler | 有没有 NotReady 闸 | 需不需要 Deferred |
|---|---|---|
| `ComboBoxHandler` | 有 | **已修** |
| `RadioButtonsHandler` | 有 | **已修** |
| `NavigationViewHandler` | **没有** | 不需要 —— 它的 `Guard` 只有回声一道，未就绪期间的事件直接放行<br>（`Guard` 见 `Handlers.Controls.cs:1972-1982`；**但 `ReadyGate.IsReady` 它并非不用**，只用在**写入侧**决定是否登记回声，见 `:2014-2017`。精化后的说法见 `docs/official-parity.md` C3） |
| `ItemsViewHandler`（ListView/GridView） | 不用 `ReadyGate` | 不需要 —— `Selector` 只被重入锁与 `IsInit` 挡，与模板是否套上无关（源码注释已写明） |
| `ToggleSwitchHandler` / `RadioButtonHandler` | 无选中闸门 | 不涉及 |

顺带记一笔不一致（本轮没动）：`NavigationView` **不吞**未就绪事件，
另两个**吞**。方向相反，都不是本轮病源，但值得收进后续项。
<br>**2026-10-09 精化**：上面这句太粗了。NavigationView 不是"不用 `ReadyGate`"——
它在**写入侧**用了（`ShouldExpectEcho(..., ReadyGate.IsReady(control), ...)`），
只是**事件侧**的 `Guard` 只有回声一道。即同一个 `ReadyGate` 在两类控件上
承担的职责不同（"事件能不能出去" vs "回声要不要登记"）。
完整的两侧摘录见 `docs/official-parity.md` C3。
<br>这次精化本身也记一笔教训：**我第一次去 `Handlers.Shell.cs` 里 grep 闸门引用，
那里压根没有 NavigationView，得到的"0 处"差点被当成"它不吞"的证据。**
找错文件的阴性结果不是证据。

## 2. 根因链（逐段有 WinUI 2 源码出处）

本地副本 `tools/winui2-ref/dev/RadioButtons/`：

1. **用户在"未就绪窗口"里点了。** `RadioButtons.h:91` 的 `bool m_blockSelecting{ true }`
   只在内部 repeater `Loaded`（`cpp:118-138`）时才置 `false`。在此之前控件已经可见可点，
   但 WinUI 自己拒不接受选中，抛出的事件全是中间态。
   框架用 `ReadyGate`（`IsLoaded`）对齐这一点，窗口内的事件一律吞掉、记进 `Deferred`。

2. **控件随后漂到了 -1。** 让 `Select(-1)` 发生的不是"切换选项"本身——
   `OnChildUnchecked`（`cpp:421-435`）有第二道守卫，只有"被取消的正是当前选中项"才走。
   真正无条件的是 **`OnRepeaterElementClearing`（`cpp:304-315`）**：选中项被虚拟化回收时
   直接 `Select(-1)`，后面没有任何补救。滚动、折叠、切页都走这条路。
   另有一处是 `UpdateItemsSource`（`cpp:516-518`）无条件先 `Select(-1)`。

3. **补发复查把它扔了。** 进树那一刻 `FlushDeferred` 复查，
   旧判据 `ShouldFlushDeferred` 第一行就是：

   ```csharp
   if (currentIndex < 0 || currentIndex != pending) return false;
   ```

   理由是"控件现在什么都没选中，没有可补的发"。**这个理由漏了半句：
   可补的发在 `pending` 里，不在控件上** —— `pending` 记的是用户那次点击的真实意图，
   控件漂到 -1 只是 WinUI 的中间态。于是这一发永远出不来。

## 3. 修法：兑现的权威是 `pending`

`Reactor.uwp/Internal/SelectionGate.cs`：

```csharp
if (pending < 0) return false;                                  // -1 没有"选中了哪项"的意思
if (currentIndex >= 0 && currentIndex != pending) return false;  // 用户改主意，用最新那次
if (currentIndex < 0 && !FlushWhenControlCleared) return false;  // 旧行为
return controlledTarget is not int target || pending != target;
```

**控件此刻的值只用来判"用户有没有改主意"；漂到 -1 不构成放弃的理由。**
补发出去之后 state 一改，紧随其后的渲染会把受控值下发成同一个值，两边收敛——
这正是 `Mount` 里"补发排在重放之前"那条注释描述的收敛路径。

`FlushWhenControlCleared` 是可开关的 toggle，默认开。
按 alpha.6 的 fix discipline，**关掉它必须 fail**，测试里有对应的反向对照。

### 3.2 上游：就绪判定不再只等 `Loaded` 事件

只补下游那道复查是不够的——**"未就绪"本身可能是个永不结束的状态**。

`ReadyGate` 那份就绪标记**唯一的写入者是 `Loaded` 事件的回调**。事件不来，标记就永远
是 false。而 UWP/WinUI 的 `Loaded`/`Unloaded` 有已知的乱序与不配对问题：

- [Win2D#954](https://github.com/microsoft/Win2D/issues/954) 引 Win2D 源码注释：这两个事件
  由 XAML 异步派发，**可能乱序**；元素从树 A 移到树 B 时，树 B 的 `Loaded` 可能先于树 A
  的 `Unloaded` 到达。
- [XamlBehaviors#251](https://github.com/microsoft/XamlBehaviors/issues/251)：同一个 UI pass
  内 remove 再 add 回树，XAML **只发 `Unloaded`、不发 `Loaded`**——"在 Loaded 里订阅、
  在 Unloaded 里退订"的代码会**永久失去订阅，而对象还在正常参与 UI**。

折叠区展开、虚拟化回收、`Frame` 切页都会踩 remove→add 这种形状。

修法是**或上控件此刻的 `IsLoaded`**（抽成纯函数 `ReadyPolicy.IsReady`，能被测试穷举）：

```csharp
public static bool IsReady(bool marked, bool isLoaded, bool trustLive) =>
    marked || (trustLive && isLoaded);
```

**为什么采信 `IsLoaded` 是安全的**：它答的是同一个问题——控件自己的 `Loaded` 抛出来时
模板子树（对 RadioButtons 就是那个内部 `ItemsRepeater`）已经就位，WinUI 正是那一刻解禁
`m_blockSelecting`。所以"此刻在树上"必然蕴含"已经解禁"，不会误放行。

**为什么是"或"而不是"取代"**：控件离树后 `IsLoaded` 会变回 false，而 `m_blockSelecting`
在 WinUI 那边一旦置 false 就不会再变回去。只看 `isLoaded` 会让"进过树、此刻暂时离树"
的控件被重新判成未就绪。

命中时顺手把标记补上并计一笔 `ReadyStats.Healed`——**让"真机上到底发生过几次"成为
可证伪的读数**，而不是埋在纸面上的争论（现有 `ReadyPolicy` 注释里写着这条路径"当前不可达"，
`Healed` 非 0 就能证明它到底可不可达）。

开关 `ReadyGate.TrustLiveIsLoaded`，默认开，关掉必须 fail。

## 4. 契约：INV13

`tests/Reactor.Core.Tests/SelectionGateTests.cs`，复现完整时序：

```csharp
sim.Mount(0);
sim.BeginRepeaterLoad();   // repeater 进树：看得见、点得到，但框架认为没就绪
sim.Click(2);              // 窗口内点第 2 项 → 被"未就绪"闸吞，记 Deferred=2
sim.Recycle(2);            // 虚拟化回收选中项 → 真实控件无条件 Select(-1)
sim.CompleteLoad();        // 复查：此刻控件停在 -1
```

- 正向（开关开）：`state == 2`、控件停在 2、恰好一次回调；
- 反向（开关关）：无回调、`state` 停在 0、控件被拉回 0 —— **复现"点了没反应"**。

反向对照成立是这条契约的凭据：它红了才说明用例真摸到了病。

**与 INV12 的关系**：两条守的是同一个窗口，但点击形状不同。
INV12 的 `cancelFirst: true`（取消在先）不会让控件漂到 -1，**漏了这一半顺序就会静默失效**。

### 一个过程中的自我纠正

第一版想用 `Click(2, cancelFirst: false)` 造出"控件停在 -1"，反向对照**没复现**。
查了仿真才发现 `OnChildUnchecked` 有第二道守卫：切到新项时，旧项的 Unchecked **不会**
把它打回 -1。改用 `Recycle(2)`（虚拟化回收）才对上真实控件。
量具比真实控件更凶的时候，测出来的失败是量具造的假。

## 5. 排查方法论上付出的学费

这次前前后后误判了三次，每一次都值得记下来：

| 误判 | 为什么错 |
|---|---|
| "重打包治好的" | 重打包之后用户说的是"**还是**没反应"，是埋完探针才"又能操作" |
| "探针的心跳在治病" | 抽成纯空心跳做 A/B，救不了。探针那份额外监听只是**看见**了事件，不改变框架行为 |
| "走可视树在治病" | 只走树、不挂订阅的那一档同样无效 |
| **"模糊测试覆盖到了本次修复"** | 加了第四条反向对照（关掉「控件漂到 -1 仍补发」）后**全绿**——不是修法多余，是 fuzz 里 `sim.Load()` 把"repeater 进树"和"框架就绪"两段一次性走完，窗口从来没被打开过。`BeginRepeaterLoad` 此前只出现在两条确定性用例里 |

最后一条是本次最贵的学费，展开见下节。

真正的变量是探针每 400ms 一次的**转储写文件**——它把"未就绪窗口"拖了过去。
这一点 `App.cs` 里那段 A/B 注释早就写明了，只是当时被用来解释别的事。

### 5.1 给模糊测试做体检，一次查出三个洞

补上"窗口"这个动作格（fuzz 的 `case 6: BeginRepeaterLoad()`）之后，顺手给终态不变量
加了第四条 —— **"用户真的换了项、那一刻回调也在场 ⇒ 那一次点击必须进 state"**。
加它的理由比它修掉的 bug 更重要：

> **一次丢失的点击，在终态上和"用户根本没点"长得一模一样。**

点击丢了之后，控件会被受控下发拉回旧值 —— 于是"控件的值有主人"、"state 与控件一致"、
"回调不含 -1"三条**全部成立**，界面规规矩矩。只看最终状态永远抓不到这类事故，
必须把"用户做过什么"和"state 收到什么"对起来看。

这条新不变量一挂上去就红了 36 条，顺藤摸出两个**此前完全没被覆盖**的入口：

**INV14 —— "用户改主意"的判据把我们自己的手当成了用户的手。**
记下 `pending` 之后、`Loaded` 到来之前，中间还夹着一条异步队列：同一手势的"取消选中"
那一发会排一次 `SelectionRestore`，它把控件写回**受控旧值**。于是复查时控件停在受控
目标上，而 `pending` 是用户刚点的那个值 —— 旧判据 `currentIndex != pending` 把它读成
"用户改主意"，那一次点击就凭空消失了。
修法：`ShouldFlushDeferred` 里收紧一格 —— 只有控件停在"**既不是 pending、也不是受控
目标**"的值上才算用户改了主意。开关 `SelectionGate.FlushWhenControlPulledBack`。

**INV15 —— 记下的那一发会过期，而旧代码从来不作废它。**
`ShouldDeferNotReady` 只在"值 != 受控目标"时记账，于是"用户把控件拨回受控目标"这一发
**既不记账、也不作废旧账**，旧账活到进树那一刻被当成最新意图补发出去。
用户看到的样子很反直觉：**他最后点的那一项不生效，先点的那一项反而生效了。**

难点在于**光看值分不开两种来源**：受控目标正好是 B 时，我们把控件写成 B 抛一发 `B`，
用户把控件拨回 B 也抛一发 `B`。所以要有"此刻是不是我们自己在写"这道旗标
（`Applying`，与 `Rebuilding` 同构，只是一个替 WinUI 的 `UpdateItemsSource` 关门、
一个替我们自己的写回关门）。

### 5.2 顺带修好的：契约第十五道失聪

`Applying` 加进去之后，scanner 报"1 个标记开了却不关、也没登记"。查下去发现是
**契约自己失聪**，不是源码错了：它把"窗"这个概念钉死在 `Rebuilding` 这一个变量名上
（`if (flag.Groups[1].Value != "Rebuilding")`），其余一律当单向位。
`Applying` 是窗不是单向位 —— 按单向登记等于撒谎，按它本来的性质就得受
"必须关在 finally"这条管。已把硬编码换成 `WindowFlags` 集合。

顺带说，这个报错本身也抓到了一处真缺陷：我第一版把 `Applying` 用
`Set(true)` … `Remove()` 在正常路径上关窗，**没扛异常**。写入一抛异常窗就永远开着，
该控件之后所有用户点击都会被判成"我们自己写的"—— 又是"点了没反应"。
已改成 `try/finally`。

**结论：这类 bug 靠真机点击定位不了，因为任何够分量的观测手段都会改时序。**
这一版因此把证据链放在仿真里（真机只用于最终确认）。

### 5.3 fuzz 长期没跑过真机实际配置

上面所有批次都把 INV11 的两道防护（作废 / 兑现前复查）**关着**——那是给它们做反向
对照用的。副作用是：那 20000×24 条序列**从来没在真机实际配置下跑过**。
而真机上两道都是无条件开着的（`SelectionRestore.Cancel` 在三个 handler 的放行路径上、
`current >= 0 && current != expected` 在 `SelectionRestore.Schedule` 里）。

补上这一批之后的读数：

- **修复后失败 0 条** —— 真机配置本身是干净的，没有"只在两道防护都在时才现形"的事故
- 关掉 INV14 → 失败 **11 条**（防护关着的那批是 35 条）—— 两道防护开着时确实替
  INV14 挡掉了一部分，但挡不完

## 6. 验证

- 契约测试 **575/575**
  - INV13 判据 toggle 对照 2 条、确定性用例 5 条
  - INV14 判据 toggle 对照 2 条、确定性用例 5 条
  - INV15 确定性用例 3 条（含反向对照）
  - `Applying` 窗的反向对照 1 条（它一开始是裸的，这轮补上）
  - 就绪判据穷举与反向对照 6 条
  - 契约第十五道：`WindowFlags` 集合化 + `Applying` 窗的 finally 判据
  - **真机配置批次**（INV11 两道防护都在）：正向 1 条 + 反向对照 1 条
- 模糊测试的**六条**反向对照全部有牙（关掉修法后同一批 20000×24 序列的失败条数）：

  | 关掉的修法 | 终态失败条数 | 备注 |
  |---|---|---|
  | 吞后回写 | 3836 | |
  | 回声泄漏 | 1178 | |
  | 控件漂到 -1 仍补发（INV13） | **2** | 实际丢失 **60 次**，见下 |
  | 被拉回受控值仍补发（INV14） | 35 | |
  | 过期意图不作废（INV15） | 8 | |
  | **认不出"这一发是我们自己写的"（`Applying`）** | **35** | INV15 的另一半，见下 |
  | 纠正依赖"有没有人监听" | 989 | |

  真机配置批次（INV11 两道防护都在）：修复后 **0 条**，关掉 INV14 → 11 条。

  最后那条是**补上的**：`Applying` 窗一开始是**裸的**——没有反向对照能证明
  "没有它会坏"，于是谁也没法证伪"它其实是冗余的"，将来很可能被当冗余删掉。
  它和 `CancelDeferredOnPullBack` 是同一件事的两半：作废必须先分清作者，
  否则"我们把控件写成受控值"那一发会被当成"用户拨回受控值"，
  **凭空作废一次根本没过期的意图** —— 修法反过来变成新的丢点击来源。

  **INV13 那个 2 条是判据的视力，不是覆盖率的真相。** 终态不变量只看序列跑完之后
  state 兑没兑现最后一次点击，丢失发生在**中段**时会被后续点击掩盖掉。
  给"控件漂到 -1 而被放弃"单独计数之后，同一批序列的**实际丢失是 60 次**——
  差了 30 倍。所以那条反向对照改读计数（门槛 10，实测 60，余量 6 倍）。
  但 60 次 / 480000 步依然罕见，**INV13 的主力防线仍是确定性用例
  `ClickBeforeLoadedWithCancelLast`，fuzz 只保证对它不完全失明。**

  这条同时也是一个通用教训：**终态不变量天然看不见"被后续操作抹平"的中段事故。**
  要数这类东西，就得数事件本身，不能靠终态反推。
- 编译 **0 错 0 警**：`Reactor.uwp`（pack）、Template x64 / arm64、Gallery x64、
  **UwpApp x64**
  —— 这里也记一笔自查事故：一度从 `ls samples/` 就断言"UwpApp 已不存在"，
  而它其实在**仓库根**。按 `samples/` 找不到的东西不等于没有。
- 包 = 源码：nuget 缓存里的 `Reactor.uwp.dll` 能搜到 `FlushWhenControlCleared`、
  `FlushWhenControlPulledBack`、`TrustLiveIsLoaded`、`Applying` 四个新符号
- **真机验证待补**。量具已就位，F5 跑一次即可：
  - `App.cs:63` 单开 `Input` 通道 Trace（Trace 不 Persist，所以**不引入**"每条日志
    同步 Append 一次文件"那个已知干扰源）
  - `MainPage.cs:179` 的 `UseEffect` 里挂 `Heartbeat.Start()`，每 500ms 把 Ring 转出，
    且**只在内容变化时写盘**
  - `ReactorLog.Gate` 走 `Input` 通道，`Pass` 走 `Input` 通道 Info 级 —— 两者都进 Ring，
    所以 INV14 / INV15 新加的留痕也能被 `gate.log` 读到
  - 真机包目录是 `21CE1A0C-1D93-4A28-AE85-B632189C9236_e7hhqg8rddnae\LocalState`
    （**不是** `28AE62C8.FurryXiyi.UWPTemplate` —— 那是 XAML 原生版对照模板，
    本节一度引错了它，见 6.1 那笔事故）
  - 当前基线 `settings.json`：`{"Theme":0,"Material":0,"Pane":0,"Sound":true}`，
    只在 10-08 **22:28**（探针还在的那次）写过一次

  **判据**（三条，缺一不算验过）：
  1. 改「应用主题 / 背景材质 / 导航栏位置」后 `settings.json` **当场变化**
     —— 这是"点了没反应"本身
  2. 出现 `就绪后补发 N` 的留痕。<b>两个地方都能读到</b>：
     `gate.log`（Heartbeat 转出的 Ring），以及 `reactor-startup.log` 里的
     `[Input/INF]` 行（`Pass` 是 Info 级，**直接落盘**，且不被 `log-off.txt` 挡，
     详见 6.4 开头）
  3. `gate.log` 末尾 `ReactorLog.Counters()` 里的 `healed=N` —— 非 0 即证明
     "Loaded 事件不来、靠即时查 IsLoaded 救回来"这条路径真机可达
     （`ReadyPolicy` 的注释现在还写着"当前不可达"）

  **第 0 条（比上面三条都优先）**：`gate.log` **必须出现**。
  它是"这次读数有效"的戳 —— 点过之后 Ring 必然非空，文件必然被写出；
  `heartbeat.txt` 有新时间戳但 `gate.log` 仍不存在，等于**这次没点**，
  上面三条判据全部读不出意义（详见 6.1 那段自查）。

  **操作上有一个必须先知道的点**：`MainPage.cs:99` 的 `ExpandForSelfTest = false`，
  也就是「应用主题」和「背景材质」两个 `SettingsExpander` **默认是折叠的**——
  不展开根本点不到里面的 `RadioButtons`。

  展开那一瞬间正是"未就绪窗口"**最长寿**的形状（`MainPage.cs:95` 的注释：
  "不展开就不进可视树"），所以请**两种点法都试**：

  - 展开后**立刻**点（窗口还在，最能复现原 bug）
  - 展开后**等一秒**再点（窗口已过，原 bug 下这一种本来就是好的）

  修复后两种都该生效。只试后一种等于没验到前一种那一半。

### 6.1 真机读数（10-08，`21CE1A0C-..._e7hhqg8rddnae\LocalState`）

> **先记一笔排查事故**：一开始查的是 `28AE62C8.FurryXiyi.UWPTemplate` 那个目录，
> 它是 **XAML 原生版对照模板**，不是 Reactor 版。Reactor 版 manifest 里
> `Identity Name="21CE1A0C-..."`、`Publisher="CN=yunmoxing"`，包目录是
> `21CE1A0C-1D93-4A28-AE85-B632189C9236_e7hhqg8rddnae`。
> 在错误的目录里读到的"没有 gate.log"一度被当成"真机没跑过"——**是错的**。

已拿到的两份读数：

- **`settings.json` = `{"Theme":0,"Material":0,"Pane":0,"Sound":true}`**，
  三个设置项**全是 0**，且只在 **22:28** 写过一次。
- **`reactor-startup.log`** 显示 app 在 22:39 / 22:51 / 22:57 **又启动了三次**，
  `settings.json` 一次都没变化（注意：**没有证据表明这三次里点过设置项**，见下）。

22:28 正是**探针还在**的那次（有探针时能操作）。

> ⚠ **这一段的效力要往下压一档（自查，第二轮）**
>
> 它一度被写成"删掉探针后三次启动全部失败 = Heisenbug 的真机证据链"。
> **过了**。把 22:57 那次（唯一带着 Heartbeat 的那次）推到底，结论其实更硬，
> 但方向相反 —— **那次压根没有发生任何控件交互**：
>
> - `heartbeat.txt` = `22:57:49 on 500ms 空心跳`，说明量具起来了、节拍在跑
>   （这行是定时器创建**之后**才写的，所以"起来了"是确定的）；
> - `App.cs:64` 的 `SetChannel(Input, Trace)` 无条件执行，任何一次闸门活动
>   都会写进 Ring；
> - 老版 Heartbeat 是"内容变了才写"，初值是空串 —— **Ring 非空就一定会写**，
>   只有 Ring 全程为空才不写。
>
> 于是 `gate.log` 不存在 ⟺ **整整一个进程里 `Input` 通道一次都没被写过**。
> 这个推断不是二义的（一度写成"分不清没点还是没留痕"，是错的：那两种只在
> Ring 为空 / 非空上有区别，而 Ring 为空正是"没活动"）。
> `reactor-startup.log` 里也确实**一行 `[Input/INF]` 都没有**（`Pass` 是 Info 级、
> 会落盘、且不被 `log-off.txt` 挡，见 6.4），两侧互相印证。
>
> 所以 22:39 / 22:51 / 22:57 三次**只有启动、没有操作**，对 Heisenbug
> **贡献不到证据**。它目前的依据仍然只是用户侧的反复观察 + 22:28 那次唯一成功的
> 写入 —— **强提示，非已取证**。
>
> 反过来这也是个好消息：**量具的灵敏度被这一轮顺带证明了**——
> 没操作时它确实什么都不写，不是"写了但看不懂"。
> 下一轮只要**真的去点**，`gate.log` 必然出现；点过却仍不出现，那本身就是一条硬结论
> （事件压根没到闸门，得往控件/命中测试那一层找，不是闸门的问题）。

**`gate.log` 不存在**（22:57 那次），原因是量具本身有个缺陷：Ring 为空时
`text == _lastGate == ""` 恒成立，于是文件**根本不会被创建** ——
"量具没在跑"和"量具在跑但还没留痕"在磁盘上长得一模一样。
已改成**首次无条件写一次**，并带上心跳序号，两件事就此分开。

### 6.2 阴性确认：设置页的下游链路本身是好的

排除"病其实在设置页代码里"这个假设。`MainPage.cs:143-156`：

```csharp
setThemeIndex(v);                              // setState → 重渲染
AppSettings.Update(x => x.Theme = (AppTheme)v); // mutate → Save → 落盘
```

`AppSettings.Update` 里 mutate 完**立刻** `File.WriteAllText`（`AppSettings.cs:80-91`），
没有任何"等一会儿再存"的环节。于是：

> **`settings.json` 没变 ⟺ 那一发回调根本没到达。**

这让 `settings.json` 成为一个干净的二元判据，也把病锁定在框架闸门上 ——
不是页面写法问题，不是落盘时机问题。

### 6.3 一处一度没解释的观察，后来查清了：不是 bug，是顺序

`LocalState\log-off.txt` 存在（10-07 创建）。按 `App.cs:72`，它应把全局设成
`Off`（`Off=0`、`IsEnabled` 是 `level <= Effective`），于是 `Host/Info` 不该落盘 ——
但 `reactor-startup.log` 里每次启动都照样有一行 `[Host/INF] [sound] Apply(...)`。

**原因在 `App.cs` 的行序**：

```
第 35 行  ElementSound.Apply(...)        ← 这一行就写了 [sound]
第 64 行  SetChannel(Input, Trace)
第 72 行  Level = Off（若 log-off.txt 存在）
```

那一条日志写在 `Off` 生效<b>之前</b>，所以必然落盘 —— 每次启动正好一条，
与观察精确吻合。

**结论：`log-off.txt` 那个 A/B 开关是有效的**，当初"关掉日志就复现"那次对照站得住。
而这条日志绕过开关也是**故意的**：`ElementSound.cs:46` 的注释写明
"设没设上必须能查证，否则'不响'到底是因为值没写进去、还是写进去了但控件不发声，全靠猜"。

（一度怀疑过"OnLaunched 没执行"，已排除：若真没执行，`Heartbeat` 那侧也不会是现在这个样子。）

### 6.4 诊断决策树：跑完如果还不行，下一步看哪一行

**先说一个能省掉一趟的读数（第二轮补）**：`就绪后补发` 那一行走的是
`ReactorLog.Pass`（`ReactorLog.cs:204`），**`Info` 级**——它不只在 `gate.log` 里，
也会**直接落进 `reactor-startup.log`**（`Write` 里 `level <= Info` 就 `Persist`）。

更关键的是**它不会被 `log-off.txt` 挡掉**：`App.cs` 第 64 行的
`SetChannel(Input, Trace)` 在第 72 行 `Level = Off` **之前**执行，而 `Effective` 是
"通道级优先"（`ChannelLevels[i] ?? _level`，`ReactorLog.cs:117`），所以全局设成 `Off`
也压不住 `Input` 通道 —— `Input/Info` 行照样进 Ring、照样落盘。

> 于是**验证时不必删 `log-off.txt`**，也**不必为了拿日志而换成另一个配置**。
> 这点很值钱：留着它，app 就停在"当初 bug 能复现"的那一个配置里，
> 验证是同一条件下的对照，不是换了条件重跑一遍。
>
> 反过来看 `Items` 通道就没有这份待遇：那条"面包屑: 下发 N 项"
> （`Handlers.Template.cs:447`）是 `Info` 级但走 `Items` 通道，
> `ChannelLevels[Items]` 为 `null` → 跟随全局 → 被 `Off` 挡掉。
> 这解释了为什么真机 `reactor-startup.log` 里**只有九行 `[Host/INF] [sound]`**、
> 一行面包屑都没有 —— **不是面包屑没下发，是它被日志开关挡了**。
> 要看它得删 `log-off.txt`，或另开 `SetChannel(Items, Info)`。

`gate.log` 的每一行都对应链上的一个具体位置，**不用再猜**：

| 看到的 | 说明 | 下一步 |
|---|---|---|
| **没有 `gate.log`** | 量具没跑，或 app 没吃到新代码；也可能是**根本没点**（Ring 空） | 看同目录 `heartbeat.txt` 有没有新时间戳；没有 = 没部署。**有时间戳但仍无 `gate.log` = 没点，这次读数作废** |
| `reactor-startup.log` 里出现 `[Input/INF] …就绪后补发 …` | **补发兑现**，且证明 `log-off.txt` 没挡住 `Input` 通道 | 与 `settings.json` 是否变化互证；两者应当同时成立 |
| `reactor-startup.log` 仍只有 `[Host/INF] [sound]` | 那一发**根本没到闸门**（`Pass` 一行都没走） | 同上：先确认是不是真的点到了设置项 |
| 只有 `心跳#N`、下面空白 | `Input` 通道开了，但**这一发根本没到闸门** | 确认是不是真的点到了设置项；也可能是 `SetChannel` 没执行 |
| `未就绪，吞 N` **且** `记下 N` 后**没有** `就绪后补发` | 补发没兑现 → 卡在 `ShouldFlushDeferred` | 看下面有没有"补发收手"，按它停在哪一格定位 |
| `补发收手：控件停在 -1，记下的是 N` | **INV13 的修法没生效** | 跑 `bash tools/repack.sh` 看 `FlushWhenControlCleared` 验没验过 |
| `补发收手：控件停在 <受控值>，记下的是 N` | **INV14 的修法没生效** | 同上，验 `FlushWhenControlPulledBack` |
| `用户拨回受控值 N → 作废记下的 M` | INV15 **正常生效** | 不是故障；若它出现在没点过的地方，查 `Applying` 窗 |
| `纠正收手：控件停在 N` | INV11 的兑现前复查正常生效 | 不是故障 |
| counters 里 `healed=0` | `Loaded` 事件都正常到了，`TrustLiveIsLoaded` 那条路径没被用到 | **符合预期**——那条路径本来就写的是"当前不可达" |
| counters 里 `healed>0` | 真机上确实发生过"Loaded 不来、靠即时查 `IsLoaded` 救回来" | 推翻 `ReadyPolicy` 注释里的"当前不可达"，要去改那段注释 |

## 7. 新增：`tools/repack.sh` —— 把"静默吃旧包"变成会报错

本次排查最贵的浪费不是 bug 本身，是**吃了陈旧包**：本地源里的包比源码旧 22 小时，
于是改了框架怎么都不生效，而四步链路里**没有任何一步会报错**。

```bash
bash tools/repack.sh                        # pack → 清缓存 → restore → 验符号 → 编 x64/arm64
bash tools/repack.sh --no-build             # 只到验符号
bash tools/repack.sh --expect SomeNewSymbol # 换掉默认符号清单
```

四步必须串在一起，因为<b>漏任何一步都是同一个结局，且都不报警</b>：
清缓存那步最容易漏（不清的话 restore 认为"已经有了"，直接拿旧的那份）。

**"包 == 源码"这一步是脚本存在的理由**，放在编译之前，尽早暴露：
包体积、时间戳都会骗人，唯一不骗人的是"新加的那个符号在不在 dll 里"。

它有牙齿（已实测）：传一个不存在的符号进去，脚本报 `MISS` 并以**退出码 1** 结束。

### 7.1 同一类陷阱的另一处：`dotnet test` 对本仓库**静默通过**

`tests/Reactor.Core.Tests` 是 `OutputType=Exe` 的**自跑型**工程（自己打印
`通过 N 项，失败 0 项` 并按失败数返回退出码），**不是** VSTest 工程。

于是：

```
$ dotnet test tests/Reactor.Core.Tests/Reactor.Core.Tests.csproj
  正在确定要还原的项目…
  所有项目均是最新的，无法还原。
$ echo $?
0
```

**一条用例都没跑，退出码还是 0。** 这比报错危险得多 —— 它会被读成"测试通过"，
而且 `-v q` 下连上面那两行都没有，输出完全是空的。

正确跑法（**以它的输出为准，别以退出码为准**）：

```bash
dotnet run --project tests/Reactor.Core.Tests/Reactor.Core.Tests.csproj -c Release
# 末行必须是：通过 575 项，失败 0 项。
```

顺带一条同类经验：**验证产物时别用 ASCII 去 grep 字符串字面量**。
`.dll` 里的字面量是 **UTF-16**（`B\0C\0 \0I\0s\0…`），ASCII grep 必然 0 命中，
会让人误判"改动没编进去"。要验就验**标识符**（元数据里是 UTF-8，`grep` 能中），
或者按 UTF-16 拼 pattern。`tools/repack.sh` 验的六个全是标识符，所以它是可靠的。

## 8. 发布后要改的三处

漏改任何一处都不会报错，只是新人照着 README 抄到旧版本：

- `samples/Reactor.Template/Reactor.Template.csproj:59` — 已跟进 alpha.8 ✅
- `Reactor.uwp/README.md:78`（安装片段）— 本次已改 ✅
- `Reactor.uwp/README.md:229`（版本自述）— 本次已改 ✅

## 9. 已知未处理

- ~~`ReactorLog.Persist` 只 Append、没有滚动策略~~ —— **本轮已处理**：
  加上限（默认 1 MB）+ 轮转（只留一代 `.1`），开关 `RotatePersist`。
  <br>注意它**没有契约测试守着**：`ReactorLog` 依赖 `Windows.Storage.ApplicationData`，
  编不进 `net10.0` 测试工程，无法像 `SelectionGate` 那样穷举 + 反向对照。
  改它请手工验一次。
  <br>另外**"每条都开合一次文件"是刻意保留的**——崩在半路时已落盘的那部分比
  缓冲区里没写的最后几十条有用。代价就是它慢，而且**这正是"开日志把时序 bug 治好"
  的机理**（见 5. 学费）。要排查时序类 bug 请用 Trace 级（只进内存 Ring）。
  <br><b>真机侧佐证</b>：`UwpApp` 的 `reactor-startup.log` 实测 **985,774 字节**
  （10-07 一天长到接近 1 MB），正好卡在新上限下面——下次跑它会触发轮转、
  生成 `reactor-startup.log.1`，可以顺手看一眼轮转是否正常。
- `Reactor.uwp/Elements/Native.cs` 仍写着「Factory 引用必须稳定」，与
  `Handlers.Native.cs` 只比 `Token` 的实现不符（本次没动）
- **半个待决项已处理**：`ReadyGate.IsReady` 现在或上控件此刻的 `IsLoaded`，
  "Loaded 事件不来就永久未就绪"这条失效模式已消除（见 3.2）。
  但整套"订阅挂在 `Loaded` 上、`Disarm` 清标记"的做法仍在 —— 官方
  microsoft-ui-reactor 完全不用 `Loaded`/`Unloaded` 管理订阅生命周期
  （订阅一次 + 静态 trampoline，状态走 attached DP），彻底对齐是后续项
- 回声抑制仍是 `EchoGuard`（`Expect`/`Consume` 计数器式）。官方 spec-047 §8.3 已把它们
  迁移到**值比对**臂（`ArmExpectedEcho`/`ShouldSuppressEcho`），理由是计数器一旦配对错位
  就会 strand 并吞掉下一次真实事件。`Consume` 在"不匹配时保留登记"这条也有同样的风险面
- `samples/Reactor.Template/Services/Heartbeat.cs` 是本次排查用的临时量具（心跳 + 快照 +
  闸门留痕），**确认之后要摘掉**；探针 `Probe.cs` / `Probe.Atomic.cs` 已删，
  备份在 `tools/_probe-archive/`
- **策略不一致（本轮没动）**：`NavigationView` <b>不吞</b>未就绪事件，
  `ComboBox` / `RadioButtons` <b>吞</b>。方向相反，都不是本轮病源，
  但同一套受控设施里有两种相反策略，迟早会再出一次"同一个控件换个写法就好了"

- **「面包屑不显示」是独立未处理项，本轮一个字都没改它**（第二轮补记）
  <br>用户报过两次（最早是 alpha.6 那次，本轮"撤掉探针"后又报了一次）。
  **别把它当成"设置项修好了就顺带好了"** —— 两者不在同一条链上。
  <br>已排除的方向：alpha.6 的"每次下发都换全新数据源引用"修法**按源码是对的**，
  不要再动 `ApplyItems`。依据 `tools/winui2-ref/dev/Breadcrumb/BreadcrumbBar.cpp`：
  <br>· `UpdateItemsRepeaterItemsSource()`(cpp:142) 读的是 `ItemsSource()` **依赖属性**
  （永远是最新值），只有"把 feeder 交给 repeater"那一步(cpp:151)要求 repeater 已存在；
  <br>· `OnApplyTemplate()` 末行(cpp:73)与 repeater 自己的 `Loaded`(cpp:96 → cpp:163)
  **都会**按当前 `ItemsSource()` 重建；
  <br>所以那个"每次 new 一个载体"的写法确实让时机变得无关 ——
  `Handlers.Template.cs:402` 那句"这里不需要 Loaded 兜底"经源码核对**站得住**。
  真机上仍不显示，**病因在别处**。
  <br>剩下三个候选（都未验证）：① 条目渲染了但容器尺寸为 0（Collapsed / 没布局）；
  ② `ItemTemplate` 没解析出来（`TitleTextBlockStyle`）；③ 模板压根没应用。
  <br>区分它们需要**同时**看"渲染了几项"和"w/h" —— 已给 `Heartbeat` 的快照加了
  `BC … w=… h=… 渲染=N项` 一行（scan 模式，靠 `heartbeat-scan.txt` 开）。
  <br>⚠ 两个数里**只有"渲染了几项"可信**：`ItemsSource` 的条数在 CsWinRT 投影下
  反复失真（Gallery 的 `LiveProbe.cs:163` 踩过两轮：非泛型 `IEnumerable` 判不出、
  数成 0；换 `IList<object>` 判得出但照样报 0，而可视树里明明渲染着东西）。
  一条假读数逼出的结论全是假的，所以只数可视树里的 `BreadcrumbBarItem`。
  <br>⚠ **别和主验证同一次跑**：scan 模式每 500ms 走一遍可视树，本身就是扰动。
  先干净跑一次验 `settings.json`，确认后再单独开 scan 抓面包屑。
