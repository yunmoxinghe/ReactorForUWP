# WinUI 2 源码核查笔记

> 本文件记录「**为什么这个 handler 必须这么写**」的源码依据。
> 改 `Reactor.uwp/Internal/Handlers.*.cs` 之前先看这里——这里的每一条都带
> `文件:行号`，能直接翻到 WinUI 的原文去复核。

## 0. 怎么拿到源码（以及为什么必须自己拉一遍）

官方仓库 `microsoft/microsoft-ui-xaml`，**WinUI 2 的发布分支是 `release/2.8`**
（不是 `main`，`main` 已经是 WinUI 3）。

```bash
# 1) 先看分支在不在（本机 http_proxy 会拦 github.com 的 CONNECT，必要时绕开代理）
git ls-remote --heads https://github.com/microsoft/microsoft-ui-xaml.git | grep release

# 2) 拿整棵树（比逐个文件猜路径快得多，路径猜错一次就是 404）
curl -sSL "https://api.github.com/repos/microsoft/microsoft-ui-xaml/git/trees/release/2.8?recursive=1" \
  -o tree28.json

# 3) 按关键字找路径
python -c "import json;d=json.load(open('tree28.json'));\
print([t['path'] for t in d['tree'] if 'Breadcrumb' in t['path']])"

# 4) 拉单个文件
curl -sS "https://raw.githubusercontent.com/microsoft/microsoft-ui-xaml/release/2.8/dev/Breadcrumb/BreadcrumbBar.cpp" \
  -o BreadcrumbBar.cpp
```

**路径不是 `controls/dev/...`，WinUI 2 里是 `dev/<控件名>/...`。**
踩过：按 WinUI 3 的习惯猜 `controls/dev/BreadcrumbBar/BreadcrumbBar.cpp`，三次 404。

**`dev/` 树里没有的控件去 `main` 分支的 DXAML 里找。**
`RadioButtons` / `BreadcrumbBar` / `NavigationView` / `ItemsRepeater` 属于 WinUI 2 库，
在 `release/2.8` 的 `dev/` 下；但 **`ToggleSwitch` 这类 `Windows.UI.Xaml` 的 OS 控件
不在 `dev/` 里**（全树搜 `ToggleSwitch` 只出两个样式 XAML）。它们在同一仓库
`main` 分支的 XAML 内核里：

```bash
# 全树搜（main 是 DXAML 内核，WinUI 2 / WinUI 3 共用这份实现）
curl -sSL "https://api.github.com/repos/microsoft/microsoft-ui-xaml/git/trees/main?recursive=1" -o tree.json
python -c "import json;d=json.load(open('tree.json'));\
print([t['path'] for t in d['tree'] if 'ToggleSwitch' in t['path'] and t['path'].endswith('.cpp')])"
# → dxaml/xcp/dxaml/lib/ToggleSwitch_Partial.cpp
#   配套还要拉 ToggleSwitch.g.cpp（生成类）和 *.h（字段清单）
```

拉下来的副本放在 `tools/winui2-ref/dev/<控件>/` 与 `tools/winui2-ref/dxaml/<控件>/`。

本地副本放在 `tools/winui2-ref/`（**不入库**，见 `.gitignore`）——第三方源码不该进仓库，
但笔记里的行号要能复核，所以保留本地拷贝 + 上面的取回命令。

---

## 1. RadioButtons：选中为什么"点了没反应"

### 1.1 源码事实

| 位置 | 内容 |
|---|---|
| `dev/RadioButtons/RadioButtons.h:91` | `bool m_blockSelecting{ true };` ← **初值就是 true** |
| `dev/RadioButtons/RadioButtons.h:85` | `int m_selectedIndex{ -1 };` |
| `dev/RadioButtons/RadioButtons.h:88` | `bool m_currentlySelecting{ false };` |
| `dev/RadioButtons/RadioButtons.cpp:358` | `if(!m_blockSelecting && !m_currentlySelecting && m_selectedIndex != index)` |
| `dev/RadioButtons/RadioButtons.cpp:118-138` | `OnRepeaterLoaded()`：**唯一**把 `m_blockSelecting = false;` 的地方（cpp:127），紧跟一次 `UpdateSelectedIndex()` 自愈 |
| `dev/RadioButtons/RadioButtons.cpp:69` | repeater 的 `Loaded` 才挂上上面的回调（`m_repeaterLoadedRevoker`） |

### 1.2 这段源码说了什么

RadioButtons 用内部 `ItemsRepeater` 渲染每一项。**在 repeater 触发 `Loaded` 之前，
控件拒不接受任何选中**——`Select()` 第一行就被 `m_blockSelecting` 挡回来。

也就是说：写给它的 `SelectedIndex` **会存进依赖属性**，但内部选中态此刻还没生效；
要等 repeater 进可视树那一刻，WinUI 自己按依赖属性补一次。

折叠容器（`SettingsExpander` 展开区）里的控件挂载时还没进树，于是：

- 挂载时下发的受控值 → 被吞；
- 用户（或别的链路）此刻引发的选中变化 → `SelectionChanged` 照样抛，
  但那全是 WinUI 的内部中间态。

### 1.3 `-1` 是哪来的（三处，都在源码里）

| 位置 | 触发 |
|---|---|
| `RadioButtons.cpp:304-315` | `OnRepeaterElementClearing`：被回收的元素正好是勾选的 → `Select(-1)` |
| `RadioButtons.cpp:516-518` | `UpdateItemsSource()`：**无条件**先 `Select(-1)` |
| `RadioButtons.cpp:421-431` | `OnChildUnchecked`：子项 Unchecked → `Select(-1)` |

### 1.4 决定性的那条：事件参数里带着"选中了什么"

`RadioButtons.cpp:378` 是这么抛事件的：

```cpp
m_selectionChangedEventSource(*this,
    winrt::SelectionChangedEventArgs({ previousSelectedItem }, { newSelectedItem }));
```

**参数就是 `SelectionChangedEventArgs`——有 `AddedItems` / `RemovedItems`。**

而用户点一下，会同时跑**两条独立路径**（`cpp:282/283` 挂的两个 revoker）：

| 路径 | 源码 | 抛出的 args |
|---|---|---|
| 新项 `Checked` | `cpp:282 → 407 → 415` `Select(N)` | `AddedItems = { 真实项 }` |
| 旧项 `Unchecked` | `cpp:283 → 421 → 431` `Select(-1)` | `AddedItems = { null }` |

`Select(-1)` 那发的 `newSelectedItem` 来自 `GetDataAtIndex(-1, true)`，而
**`cpp:401` 显式 `return nullptr`** → `AddedItems` 是 **`{ null }`：Count 为 1，元素是 null**。

两条路径都由 `m_currentlySelecting` 保证互斥，但**谁先谁后没有保证**。实测日志：
`-1` 有时在真值前 3ms、有时在真值后 4ms。于是：

> **值、就绪状态、到达顺序——全都区分不开这两发。只有 `AddedItems` 能。**

### 1.5 由此定下的实现规则（判据零在最前）

1. **判据零：看 `AddedItems` 里有没有非 null 的项。**没有 → 这是"取消选中"，
   作者是 `OnChildUnchecked`，不是用户选中了某一项，**吞掉**。
   - 判据是"有没有非 null"，**不是 `Count > 0`**——取消那发的 Count 就是 1，
     只看数量会把最该拦的一发放过去。
   - 判据依赖 args，**不依赖"为什么会抛"**，所以 ComboBox（未开源的 `Selector`）
     可以共用同一套。
2. **值照旧往依赖属性写**，不走"等 Loaded 了再补"——依赖属性就是 WinUI 自己认的权威，
   它在 `OnRepeaterLoaded` 会自己补。我们额外补一次只在 `ReadyGate.Arm` 的回调里做，且幂等。
3. **未就绪**（`m_blockSelecting == true` 期间）的事件一律不放行 —— 见 `ReadyGate`。
4. **items 整批重建期间**的事件按因果单独拦掉（作者是 `UpdateItemsSource`）。

对应实现：`Internal/Handlers.Controls.cs` 的 `SelectionArgs.SelectedSomething`
+ `RadioButtonsHandler.Dispatch` / `ComboBoxHandler.Dispatch`。

> **⚠️ 走过两次弯路，都记在这儿：**
> ① 先是按值过滤（"`-1` 一律丢"）——那是猜的，会误伤真实的"清空选择"；
> ② 改成按就绪过滤（`ReadyGate`）——**也不够**：控件早就 Loaded 过之后，
>   换选项依然每次都抛 `-1`，照样把 state 打成 -1
>   （症状：点几下之后"回调不再更新"，界面上没有任何一项被选中）。
> **只有按事件参数过滤才站得住**——因为它是唯一不依赖顺序的判据。

### 1.6 依赖属性也会被写成 -1（cpp:381）

`Select()` 里这两行是连着的（`RadioButtons.cpp:376-377`）：

```cpp
SelectedIndex(m_selectedIndex);      // ← 依赖属性，index 是什么就写什么，包括 -1
SelectedItem(newSelectedItem);
m_selectionChangedEventSource(*this, SelectionChangedEventArgs({ prev }, { new }));
```

所以 `SelectedIndex` 这个 DP **确实会出现 -1**（`Select(-1)` 那条路径），
不是在托管侧读错了。推论：

- 一次点击结束后 DP 最终仍会停在真值上（两条路径都跑完），**稳态不会停在 -1**；
- 但**两发之间**是 -1。任何在中间态里读 `SelectedIndex` 的代码都会读到 -1；
- 因此"受控下发"这一步必须记日志：**稳态下它不该出现，出现了就是控件在两轮之间漂了。**
  实现见 `RadioButtonsHandler.ApplySelectedIndex` / `ComboBoxHandler.ApplySelectedIndex`
  里的 `受控下发 #N: x → y`（`Patch` 通道，`Info` 级，只在真的要写时才记）。

### 1.7 完整因果链的埋点（四环缺一不可）

```
事件 → 闸门(Input) → 回调 → setState → 重渲染(Render) → 受控下发(Patch) → 控件
```

| 环 | 通道/级别 | 消息 |
|---|---|---|
| 闸门放行 | `Input` / Info | `RadioButtons#3 → 用户回调 SelectedIndex=2` |
| 闸门吞掉 | `Input` / Trace | `RadioButtons#3 取消选中（AddedItems 无实项），吞 -1` |
| 渲染调度 | `Render` / Trace | `组件 DiagnosticsPage setState：入队 / 合并` |
| 真的渲染了 | `Render` / Info | `帧12 组件 DiagnosticsPage 子树渲染` |
| state vs 控件 | `Render` / Info | `state: radio=2 ... \| RB[0]#3 idx=2 ...` |
| 受控下发 | `Patch` / Info | `受控下发 RadioButtons#3: -1 → 2` |

**以前只有第一环有日志**，"回调在触发但界面不动"完全没法定位。
日志里出现 `回调 → 没有帧` 就是 setState 之后丢渲染；出现 `有帧但 state 没变`
就是闭包/同值；出现 `帧里 state 变了但控件值没跟上` 且**没有**受控下发
就是 patch 没走到 handler。

控件编号 `#N` 由 `Internal/CtlId.cs` 用 `ConditionalWeakTable` 发放：
**不能用 `RuntimeHelpers.GetHashCode`**（AOT 下随 GC 变，会凭空造出"控件每帧重建"）。

---

## 2. BreadcrumbBar：条目为什么整条不见

### 2.1 源码事实

| 位置 | 内容 |
|---|---|
| `dev/Breadcrumb/BreadcrumbBar.cpp:167-170` | 注释原话：*"A new **BreadcrumbIterable** must be created as **ItemsRepeater compares if the previous itemsSource is equals to the new one**"*，紧接着 `m_itemsIterable = winrt::make_self<BreadcrumbIterable>(ItemsSource());` |
| `dev/Breadcrumb/BreadcrumbIterable.h` | 那个类身上**只有** `IIterable<IInspectable>`，一个哑迭代器 |
| `dev/Breadcrumb/BreadcrumbBar.h:74` | `m_itemsSourceAsObservableVectorChanged` —— 全仓库**只有 revoke，从没赋值**，是死代码 |
| `dev/Breadcrumb/BreadcrumbBar.cpp:142` | `UpdateItemsRepeaterItemsSource()` |
| `dev/Breadcrumb/BreadcrumbBar.cpp:73` | `OnApplyTemplate()` 末行调用上面那个 |
| `dev/Breadcrumb/BreadcrumbBar.cpp:96` | `OnBreadcrumbBarItemsRepeaterLoaded()`：repeater 出现时按当前 `ItemsSource()` 重建 |

### 2.2 这段源码说了什么

官方每下发一次条目就 `new` 一个 `BreadcrumbIterable`，而那个类存在的**唯一目的就是让
引用变掉**——因为内部 `ItemsRepeater` 按**引用相等**判断数据源换没换。

于是旧写法错得很具体：给一个长期持有的 `ItemCollection`、之后原地 `Clear()/Add()`——
**引用从头到尾没变**，repeater 认为"数据源没换"，条目数纹丝不动。

唯一能救场的是集合变更通知，而这条路在 BreadcrumbBar 里是残的：

- `m_itemsSourceAsObservableVectorChanged` 是从没接上的死代码；
- 真正接的那条 `ItemsSourceView.CollectionChanged`（cpp:142 附近）处理体开头就要求
  `m_itemsRepeater` 已存在——控件还没 `ApplyTemplate` 时，这一整批更新被**静默丢弃**。

若 repeater 一直不 Loaded（典型：面包屑常驻在 `Collapsed` 容器里），就再没有别的机会补回来。
**这正好对应"面包屑整条不见"。**

### 2.3 由此定下的实现规则

1. **每次下发都换一个新的数据源引用**（`new ItemsControl()` 作载体 → 取 `Items`）。
   用 `ItemCollection` 而不是 `string[]` 是另一层原因：CLR 集合在这条
   AOT + `DisableRuntimeMarshalling` 栈上封送不到 WinRT 侧。
2. **内容没变就一个字节都不动**——换引用会让 repeater 拆重建整套条目，
   每轮渲染都换 = 永远在重建。用 `PropWriter.SequenceEqual` 挡住。
3. **不需要 Loaded 兜底**：依赖属性始终是最新值，repeater 何时出现，
   `OnApplyTemplate`(cpp:73) 和 `OnBreadcrumbBarItemsRepeaterLoaded`(cpp:96) 都会按
   当前 `ItemsSource()` 重建。加了反而是多余动作。

对应实现：`Handlers.Template.cs` 的 `BreadcrumbBarHandler.ApplyItems`。

### 2.4 那个前提核实了，还剩一个没证实的疑问（2026-10 增补）

**整条修复押在一个曾经没有任何证据的前提上**：「每次 `new ItemsControl().Items`
一定是新引用」。当时拿得出的"证据"是运行日志里几行不同的 `#hash`，事后查明分属
**四个不同进程**（每行前面都跟着一行宿主初始化 `[Host/INF] BACKDROP …`，那是会话起点），
而 `GetHashCode` 跨进程本来就不一样——那份证据等于零。

现在这一半**有出处了**（`tools/winui2-ref/dxaml/ItemsControl/ItemsControl.cpp`）：

| 环节 | 位置 | 结论 |
|---|---|---|
| 集合的创建 | `cpp:403-409` `CItemsControl::EnsureItemCollection` | `if (m_pItemCollection == nullptr)` 才 `Create` —— **懒创建，每个控件一份** |
| `Items` 的取值 | `cpp:29-45` `CItemsControl::GetItems` | `EnsureItemCollection()` 后返回 `m_pItemCollection` —— **同一控件永远同一个集合** |

合起来：新建载体 ⇒ 新引用；反过来"长期持有一个集合再原地 `Clear()/Add()`"引用从头到尾不变，
正是旧 bug 的形态。链条的另一半也在本地核实过：`BreadcrumbBar.cpp:80-83`（只有 ItemsSource
属性变更才更新）→ `cpp:142-155`（重建发生在这里）→ `cpp:167-170` 的官方注释
（明说必须新造一个 `BreadcrumbIterable`，因为 ItemsRepeater 会去比引用）。

**还剩一个疑问没有证据**：载体 `ItemsControl` 不进任何可视树，而
`ItemsControl.cpp:9-18` 的析构会 `m_pItemCollection->Clear()` 再 Release。
也就是说"主人一死就把集合清空"——若载体被回收，面包屑会在 GC 时机上凭空变空。
`CItemCollection` 的所有权语义在闭源内核里，**证不了也伪不了**。
处置：按"代价近零"把载体钉进 `WeakTable`（弱键，随控件自动回收），把这个疑问整体绕开。
这是防御性推断，**不是已证实的 bug**，别在别处引述成结论。

**回归网**：`tests/Reactor.Core.Tests/BreadcrumbItemsTests.cs`（之前这条线零覆盖）。
内容 diff 与引用判定都 Link 的是真代码（`Seq` / `ItemsSourcePolicy`），不是副本。
反向对照——把模型换成"复用集合"的写法——同一批 20000 条序列失败 **19232** 条。

---

## 3. SettingsExpander：为什么展开区里的控件"迟到"

来源 `CommunityToolkit/Windows`（MIT），本地副本 `tools/ctk-ref/`。

```
components/SettingsControls/src/SettingsExpander/SettingsExpander.xaml
components/SettingsControls/src/SettingsExpander/SettingsExpander.ItemsControl.cs
```

**它是 `ItemsControl`，展开区用 `ItemsRepeater` 承载子元素**——也就是说展开区的子元素
在展开之前根本不在可视树里。源码注释里连微软自己的 issue 都引了。

这解释了第 1 节那个场景为何是"必现"而不是"偶发"：折叠区里的 `RadioButtons`
挂载时 `m_blockSelecting` 必然还是 `true`，受控值的第一次下发必然被吞。

对应到 Gallery：`Pages/DiagnosticsPage.cs` 里第 ② 组就是它的复现位。

---

## 4. 已成文的坑（别再踩第二次）

| 坑 | 说明 |
|---|---|
| **路径前缀** | WinUI 2 是 `dev/<控件>/`，不是 `controls/dev/<控件>/` |
| **分支** | WinUI 2 = `release/2.8`；`main` 已经是 WinUI 3，拿错分支会得出相反的结论 |
| **别按值猜语义** | `-1` 既是中间态也可能是真实用户操作；判据要用"控件是否已就绪"这种有源码出处的东西 |
| **别拿实例身份当证据** | `RuntimeHelpers.GetHashCode` 在 AOT 下随 GC 变，"控件每帧重建"是假结论 |
| **别读非泛型 `IEnumerable`** | CsWinRT 的投影集合只实现泛型版本，非泛型判恒为 false，数出来的是"探针自己造的 0" |
| **别在 Render 里读可视树** | 读到的是上一帧的控件，与 state 比会得出"受控值没同步"的假结论；要读就在 `UseEffect` 里（它在 `RenderContext.EndRender` 跑，patch 已落盘） |

---

## 5. 由测试自动发现的两个 bug（2026-10）

来源：`tests/Reactor.Core.Tests/` 的闭环仿真 + 2000 条随机序列。
**两个都是人工点击极难定位的类型**——都只在特定值/特定到达顺序上复现。

### bug A：吞掉"取消选中"之后没有把受控值纠正回控件

| | |
|---|---|
两个源码可引的触发点：

**① 点当前已选中项**（`OnChildUnchecked`, `cpp:421-435`）
点已勾选的那一项时，`IsChecked` 只有 `true → false`，**只发 Unchecked，不发 Checked**
——所以 `OnChildChecked`（`cpp:407`）压根不在场，`m_selectedIndex != index` 那条守卫
用不上。`OnChildUnchecked` 的守卫是"被取消的正是当前选中项"，此时成立 →
`Select(-1)`（`cpp:431`）。而 `Select(-1)` 里的
`GetDataAtIndex(-1, true)`（`cpp:382-405`）取不到元素、**没有任何一项被勾回来**，
依赖属性照样被写成 `-1`（`cpp:376-377`）。
→ **WinUI 2 里点已选中的那一项，确实会清空选中**。这不是我们的 bug，是它的行为。

**② 选中项被虚拟化回收**（`OnRepeaterElementClearing`, `cpp:304-315`）
被回收的元素如果是勾选中的，直接 `Select(-1)`，且**后面没有任何补救**。
滚动、折叠、切页都走这条路。

| | |
|---|---|
| **闸门** | 判对了：按判据零吞掉 |
| **留下** | 控件停在"无选中"，state 仍是旧值 → **界面上"点了没反应"** |
| **修法** | `SelectionRestore.Schedule`——只在 `CancelTransient` 这一道上，排到派发器下一轮把受控值写回 |

必须**延迟**：这一发事件是在 `m_currentlySelecting` 期间派发的（`Select` 用
`gsl::finally` 撑到返回为止，见 `cpp:361-365`），当场写 `SelectedIndex` 会被守卫挡掉，写了也白写。

只对 `CancelTransient` 纠正，其余三道不能碰（未就绪/重建中的值是真实值；回声会自激）。

**越界值不纠正。** `GetDataAtIndex` 对越界下标同样返回 `nullptr`，事件照样以
"AddedItems 无实项"抛回来 → 判据零命中 → 再纠正 → 再抛。越界时控件无法兑现这个值，
是唯一可能自激的情形，所以 `SelectionRestore.Schedule` 里先拿 `Items.Count` 挡一道
（回归用例见 INV8，并用 `OutOfRangeEvents` 计数证明该场景真的被跑到了）。

### bug B：受控写入在"不会抛事件"时泄漏回声登记

| | |
|---|---|
| **触发** | 折叠区挂载（未 Loaded）时下发受控值；或重建 items 期间写回 |
| **机制** | `EchoGuard.Expect` 登记了，但这一发事件会被"未就绪"/"重建中"那道吞掉，<br>`Consume` **永远不会被调用** → 登记一直挂着 |
| **后果** | 之后用户点回**同一个值**时，`Consume` 匹配上这条陈旧登记 → 判成回声 → 吞掉 |
| **表现** | "点了没反应"，且**只在该值上复现**，换个值就正常。<br>用户报的"必须先切换一次材质其他设置才生效"是同一枚硬币——<br>切材质触发的那次重渲染顺带改了控件值，后续操作才重新有反应 |
| **修法** | `SelectionGate.ShouldExpectEcho`——只在"回声真会走到回声道"时才登记 |

回声道是四道闸的**最后一道**，只有前三道全放行才会 `Consume`。
所以登记条件与"回声道可达"严格等价即可。刻意复用 `Decide` 而不是另写条件，
保证闸门将来改顺序时不会错位。

### 反向对照（别再被"假绿"骗）

这两处修法在仿真里都是**开关**（`WriteBackOnSuppress` / `LeakEchoRegistration`），
测试会把它关掉再跑同一批 2000 条序列并断言**必须有失败**：
关掉回写 → 失败 311 条；恢复无条件登记 → 失败 1 条。

一条修不修都绿的用例等于没写。本项目已在量具失真上栽过多次
（`GetHashCode` 指纹 / 非泛型 `IEnumerable` / NuGet 与 ProjectReference 混用 /
符号链接日志 / 帧日志刷屏 / 模型自己编行为 / 反向对照掉牙 / **把异步机制简化成同步**），
每次都是"看着在测，其实没测到"。
**新增回归用例时，必须同时给出它"不修就坏"的对照。**

**一个不容易用眼睛看出来的失真形态：防御代码没有反向对照。**
`SelectionRestore.Schedule` 的异步体第一行是 `now.Index != expected`（"这中间目标改过就别动手"），
它此前**零覆盖**——仿真当年把回写简化成"和渲染共用同一个 pending 旗标"，执行时机永远紧跟
下一次渲染，**快照不可能陈旧**，删掉那行一行测试都不会红。
把回写建成"独立队列 + 快照"（与真代码同构）之后，这道复查才有了对照：
去掉它，同一批序列失败 **4340** 条（最小复现见 INV9）。
判据可以直接拿去用：**随手删掉某句防御代码，看有没有测试变红；没有就是没被守护。**

### 「证明新实例」时 GetHashCode 会骗第二次

面包屑修复（`Handlers.Template.cs` 的 `ApplyItems`）要满足 WinUI 的硬要求：
**每次下发都必须是全新的数据源引用**。当时拿日志来证，打印
`新数据源 #{carrier.GetHashCode():X}`，四个 `#hash` 看着各不相同，像极了证据。

**它是假的。** 那四行分属**四个不同进程**——判据是每行前面都跟着一行
`[Host/INF] BACKDROP ...`（宿主初始化，即会话起点）。而 `GetHashCode`
**跨进程本来就不一样**，四个号不等价于任何东西。

改归：**任何给人看的实例身份一律用 `CtlId.Tag(...)`**（引用相等的稳定编号，
同一实例永远同号、号只增不减），禁令已写进 skill。同时把诊断页的面包屑从
写死两层改成**可增减条目**——否则同一个会话里永远只有一次下发，
这条性质**根本无从验证**。用户点两下"加/减一项"，看 Items 通道出现两个不同编号，
才是真凭实据。

---

## 6. ToggleSwitch：为什么"这里的写一定会有回声"（以及第三个 bug）

### 6.1 源码事实（副本见 `tools/winui2-ref/dxaml/ToggleSwitch/`）

| 位置 | 内容 |
|---|---|
| `ToggleSwitch_Partial.cpp:347-350` | `OnPropertyChanged2` 的 `ToggleSwitch_IsOn` 分支**直接**调 `OnToggledProtected()`，中间无任何条件 |
| `ToggleSwitch.g.cpp:335-351` | `OnToggledProtected()` → `OnToggled()` |
| `ToggleSwitch.g.cpp:316-331` | `OnToggled()` → `OnToggledImpl()` |
| `ToggleSwitch_Partial.cpp:623-641` | `OnToggledImpl()` → `pToggledEventSource->Raise(...)`；raise **之后**才 `if (!m_isDragging) UpdateVisualState(TRUE)` |
| `ToggleSwitch_Partial.h:145-197` | 全部成员字段：`m_isDragging` / `m_wasDragged` / `m_isPointerOver` 与若干位移量 |

**结论：`Toggled` 对每一次 `IsOn` 变更都抛**——不分古今（用户拨还是框架写）、
不分控件在不在可视树里、不检查模板是否已应用。
而且**没有 `RadioButtons` 那种 `m_blockSelecting` / `m_currentlySelecting`**，
既不吞事件，也不防重入。

所以 ToggleSwitch 的闸门**只有 `EchoGuard` 一道**——
"一次操作恰好一次回调"这条底线在它身上完全押在回声判据上，
这里也是整机最先破的地方。当初在没有源码时不敢下笔，是对的：
猜"未套模板时会不会抛"就是押注，而现在两个分支都写进了测试。

### 6.2 bug C：回调为空时的受控写入泄漏回声登记（三个 handler 共有）

| | |
|---|---|
| **触发** | 受控写入发生在**回调为空**的那一轮（`Update` 写值时<br>`Callbacks[control]` 还是上一次 `Rebind` 存进去的 `null`） |
| **机制** | 三个 handler 都是"**先写值、再 `Rebind`**"的顺序；而事件处理器是<br>`current?.Invoke(...)` / `Dispatch` 首句 `if (callback is null) return;`——<br>回调为空时**连 `EchoGuard.Consume` 都不会被调用** |
| **后果** | `Expect` 登记的期望永远没人领，一直挂到 TTL 或下次写入覆盖 |
| **表现** | 之后回调被装上、用户拨/点到**同一个值** → `Consume` 匹配上这条陈旧登记 → 判成回声吞掉。<br>又因为渲染是**批处理**的（`RenderBatcher` 排队到下一帧），<br>同帧内的第二次操作会在第一次的 `setState` 落地之前到达，<br>于是"连拨两次只剩一次反应" |
| **修法** | `EchoGuard.CancelIfUnconsumed`——写入**之后**若发现同步回声没来消费，就撤销登记 |

为什么放在写入**之后**而不是之前：这一刻"同步事件 → 匹配 → 消费"已经跑完了，
能区分"有回声"与"没回声"。放在之前等于什么都不知道。

为什么代价是可接受的：若某控件的事件其实是**异步**的（晚于这次检查），
撤销之后它会走到 `NotExpected` → 多回调一次。而那个值正是我们刚写进去的受控值，
`setState` 同值不重渲染，多出来的是一次空转。
反过来"漏撤销"吞掉的是真实用户操作。
**宁可多回调一次，也不能吞掉真实用户操作**——这与 `EchoGuard` 的 TTL 注释一贯。

已落到三处：`ToggleSwitchHandler.Update`、`ComboBox.ApplySelectedIndex`、
`RadioButtons.ApplySelectedIndex`。

### 6.3 量具自身的两处失真（本轮踩到，记下来）

1. **最终一致性不是好探针。** 回调为空期间用户操作的变化没人转告 state，
   state 与控件分开是**正确**的（受控控件的承诺是下一次渲染拽回来）。
   一开始直接断言 `state == IsOn`，报出一堆假失败；
   又一度改成"先强制渲染再断言"，结果把真 bug 一起抹平——
   **反向对照从 2270 条掉到 0 条，牙没了**。
   最终方案：用 `UnreportedUserActions` 精确记账（记录"没人接的事件"），
   只有为零时才断言一致性；真正的探针是 **per-step 回调次数**。
2. **模型自己也会编行为。** `UserClick` 原本对 index==old 的情况也补发一次
   `Checked`，于是"点当前项"竟能回调出值——那是模型编的，WinUI 里点已勾选项
   `IsChecked` 只是 `true→false`，**只发 `Unchecked`**。
   模型失真会以假乱真地"证明"代码有问题，也会掩盖真问题。

---

## 7. 别再踩第二次（量具纪律，2026-10 增补）

| 坑 | 教训 |
|---|---|
| **反向对照掉了要立刻察觉** | 加了新机制（如 seal）之后，旧 bug 的反向对照可能被"顺手擦掉"，<br>变成假的绿。复现旧行为时要把**所有**相关开关一起复位。 |
| **断言的强度要匹配语义** | "回调为空期间用户改了控件" = 期望的不一致，不是 bug。<br>把期望的不一致算作失败，比漏检更耗人。 |
| **per-step 断言优于终态断言** | "点了没反应"发生在某一步；终态一致性会被后续的他救（或缺失的下一次渲染）掩盖。 |
| **改完量具要重跑反向对照** | 本轮一次误删 `sim.Drain()` 让最后的受控下发没跑，<br>一致性断言立刻开始报"不收敛"——症状像 bug，根因在量具。 |
| **模型要逐条对源码** | 模型的每一条自由都应有出处；没有出处的分支要显式标注为假设，<br>并把假设的两个面都跑一遍。 |

---

## 8. ElementSoundPlayer："开声音"到底要不要显式打开

示例"声音"开关的真实后端。它是 **OS XAML**（`Windows.UI.Xaml`）的类，**不在 WinUI 2 的
`dev/` 树里**，所以在仓库源码里找不到——事实只能取 API 文档。

| 事实 | 出处 |
|---|---|
| `State` 默认 `Auto` | `ElementSoundPlayer.State` Property Value |
| 「默认只有 Xbox 上响，其他 device family 不响；<br>设 `On` 才在所有 family 响，设 `Off` 则一律不响」 | 该属性 Remarks 原文 |
| 「if set to Off… sound will **never** play」 | Sound（全局 API 一节） |
| 「启用 ElementSoundPlayer **会自动启用空间音频**」；<br>要保留普通声音须 `SpatialAudioMode = ElementSpatialAudioMode.Off` | 同上（枚举名经编译证实） |
| `Volume` 默认 **1.0**（不是 0） | `ElementSoundPlayer.Volume` Property Value |
| 自 `10.0.14393` / `UniversalApiContract v3` 起可用 | Windows requirements |

三条直接决定 UI 该怎么长：

1. **桌面上的默认态就是不响**，所以"开"必须显式设 `On`——留着 `Auto` 等于什么都没接；
2. 因此**没把 `Auto` 做成第三个选项**：它在桌面上与"关"听不出区别，是装饰品；
3. `Off` 时按文档连 `Play` 也不会响，所以**试听按钮必须做成"关时禁用"**——
   否则用户点一下没声音，又是一次"没接后端"的观感。禁用之后调用点必然处于
   `State == On`，两种可能的语义下都会响，不需要任何断言就能自证。

代价写在此处备查：首次进入会把原本的 `Auto` 改成 `Off`。目标平台（UWP 桌面）
上两者听感相同，故接受；Xbox 上不成立。

---

## 9. 「写了一笔没人领的回声登记」是接线的通病（2026-10 增补）

bug C 修完才发现它不是那三个控件的性质，而是**接线的性质**。把仓库里所有 `Expect(`
扫出来是 9 处，全部同一种形状：`Expect → 写属性 → Rebind`。

### 9.1 事实来源在本仓库，不在 WinUI

| 事实 | 出处 |
|---|---|
| 四个 `Rebind*` 一律**先退订**（`-= existing`），回调为 `null` 时直接 `return`（不重订） | `Reactor.uwp/Internal/Reconciler.cs:1013-1092` |
| `PasswordBox` / `NumberBox` 自己的 `Rebind` 同样如此 | `Handlers.Input.cs:91-101`、`:390-401` |
| `RadioButton` 形状不同：**订阅常驻**，但回调为空时 `Invoke` 是 `current?.Invoke` 空转，<br>`Consume` 一次都不会被调用 | `Handlers.Controls.cs:588-598` |

结论：**回调为空 ⟹ 这一发登记没人领**。它会在 `EchoGuard` 里活到 TTL（1 秒），
期间用户一旦把控件改回那个值，就被判成回声吞掉 —— 又一张"点了没反应"的面孔。

### 9.2 NavigationView：这一发大概率**压根没有回声**

修之前先确认了它不是"异步回声"（那样撤销就误伤了）。拉的是
`release/2.8 dev/NavigationView/NavigationView.cpp`：

- `SelectedItem` 依赖属性变更 → 同步进 `OnSelectedItemPropertyChanged`（`cpp:3253`）
  → `ChangeSelection` → 内部 `SelectionModel` 抛 `SelectionChanged`
  → `OnSelectionModelSelectionChanged`（`cpp:271`）；
- 而那里第一件事就是早退（`cpp:277-291`）：
  `if (m_shouldIgnoreNextSelectionChange || selectedItem == SelectedItem() || !m_appliedTemplate) return;`

即**经由 API 选中不抛 `SelectionChanged`**。既然等不到，撤销就不会误伤。

### 9.3 由此定下的规则

1. **每个 `Expect` 都必须有人负责把它领走**（`CancelIfUnconsumed`）或压根不登记
   （条件登记，如 `SelectionGate.ShouldExpectEcho`）；
2. 这条规则由 `tests/Reactor.Core.Tests/EchoContractTests.cs` 在**源码层面**强制执行，
   不靠人记得 —— 这是仿真守不住的部分：仿真只能覆盖"已经想到的控件"；
3. `Slider` / `NumberBox` 还有各自的加罪项：`Value` 会被 `Minimum` / `Maximum` 夹取，
   写下去可能被夹成别的值，登记的值永远等不到匹配项。

---

## 10. 「控件进树了吗」这个问题，仓库里曾经有两份答复

### 10.1 自证循环

`ReadyGate.Arm` 原本这样决定要不要订阅 `Loaded`：

```csharp
if (IsReady(control)) { onReady?.Invoke(control); return; }
control.Loaded += Handler;      // ← IsReady 偏偏是这个 Handler 置上的
```

也就是拿**订阅的结果**去决定**要不要订阅**。这类循环很难一眼看出它有病，
因为它只在这一种输入下断：控件真实状态已经是"已进树"，而我们那份副本还是 false。
断掉之后的后果不报错、不报日志——只是这个控件**永久停在未就绪**，
期间的选中事件被判据一（`SelectionVerdict.NotReady`）一发不留地吞掉，
用户看到的就是"点了没反应"。

### 10.2 权威信号在同一个仓库里就有

XAML 自己的答案是 `FrameworkElement.IsLoaded`。它不是我翻 SDK 猜出来的：
同仓库的 `InputApplier.ApplyFocus` 已经在用它判断"此刻能不能 `Focus`"——
同一个问题（`控件进树了吗`）在这里早就有人答复过。两处各答一份，就会各答一半：
一个信 API、一个信自己抄的副本，而副本只在"订阅的那条路上"被更新。

（`IsLoaded` 在这套 projection 里确实可用——改完 `ReadyGate` 编译 0 警告 0 错误，
且 `InputApplier` 早在用。）

### 10.3 可达性要如实写

这条**今天不可达**：`Arm` 只在两个 `Mount` 里被调用，而
`Reconciler.BuildChildren`（`Reconciler.cs:203-208`）是先 `Build` 后 `Children.Add`，
`Arm` 一律先于入树。所以这不是"今天正在复现的 bug"，是把正确性从
**押在调用顺序上**换成**押在控件自己的状态上**。

`Arm` 自己的注释写着"例如 Update 路径新建的控件恰好已在树里"——它**预期**会被从
Update 调用。真走到那天，旧写法是无声故障，新写法会记一笔 `ReadyStats.AlreadyLoaded`，
让"这条路径到底有没有被走到"变成屏幕上一个能看的数字，而不是纸面上的争论。

### 10.4 一条**阴性**结果也要写清楚

给 `ReadyPolicy.Decide` 做真实源码变异时，三个变异里两个红、
**第三个纹丝不动**（把 `subscribed` 与 `alreadyReady` 两个分支调换，246 项仍全绿）。

原因不是测试没写好，是那一对条件**不可能同时为真**：就绪结算会一并解掉订阅，
所以"已就绪"的控件手上必然没有订阅。

处理办法是**改注释，不是编理由**。原先我在 `ReadyPolicy` 里给这个顺序写了一段
"必须先判订阅，否则……"的因果说明——那段说明是我想当然的，现在改成照实写：
它没有不变式守护，留着这个顺序只因为它更好读。

教训：注释里每一句"否则就会……"都得能指着某次变红/不变红的实测证据说话。
写不出证据的那部分，是作者当时的脑补，留着只会误导下一个读代码的人。

---

## 11. 「有没有人监听」不该参与「受不受控」的决定

### 11.1 同一句早退，连着跳过两件事

`RadioButtons` / `ComboBox` 的 `Dispatch` 原都以 `if (callback is null) return;` 开头。
这句本意只是"没人接就不要回调"，但它站在门口，于是后面的**四道判据 + 纠正**一起被跳过。
跳过判据的后果上一轮已经修过（`CancelIfUnconsumed`）；跳过的第二件事这一轮才看见：

- 挂了回调：点当前已选中项 → 被判据零吞掉 → 排一次纠正 → **看着没事**；
- 没挂回调：同一发事件无人接，state 没变 ⇒ 不重渲染 ⇒ **永久停在 -1**。

用户那边的差别是"点了一下选中态没了，再也点不回来"，而这取决于
**同一个控件给没给 `OnSelectedIndexChanged`**。这类 bug 最难提：换个写法就好了。

### 11.2 判据的签名会引导下一个读代码的人

抽出来的纯函数写成这样：

```csharp
public static bool ShouldRestoreAfterSuppress(SelectionVerdict verdict) =>
    verdict == SelectionVerdict.CancelTransient;
```

**签名里刻意不带 `hasCallback`**。写成 `(verdict, hasCallback)` 就是在邀请后来者把两件事
重新捆起来——而这两件事在语义上没有关系：一个是"属性受不受控"（控件自己的承诺），
一个是"变更要不要通知别人"（调用方的选择）。把它们绑在一起，
同一个病就会在两个页面上给出两个答案。

### 11.3 两条判据必须互为补充，单留一条会失明

模糊测试的最终一致性判据原来是：

```
UnreportedUserActions > 0 || State == ControlIndex
```

前半句是一刀切：**只要中途出过一次"没人监听的用户点击"，此后所有偏差都免检**——
包括真 bug。换成两条各自精确的判据之后，同一批改法的反向对照普遍变敏感，
其中"纠正绑到监听上"那条从 178 条跳到 1039 条，专跑的纯无回调批次从 21 条跳到 3961 条。

**旧的 21 条说的不是这条路径罕见，是旧判据一直在替 bug 打掩护。**

但两条判据缺一不可，实测过：

| 单留哪条 | 漏掉什么 | 实测后果 |
|---|---|---|
| 只留 `Explained`（控件值必须有主人：受控目标或末次点击） | 陈旧回写把用户**此后**的选择盖回去 | 那条反向对照从 4384 条掉到 **0** |
| 只留 `Faithful`（state 与控件一致，只豁免末次点击） | —— | 两条互补后各自都重新咬住 |

唯一的原因是：回写恰恰会把控件拉回受控目标，落在 `Explained` **允许**的头一种里。
**一条判据允许什么，就是它看不见什么**——设计判据时先看它放行哪一类值。

### 11.4 防线够不到的地方，得换个办法

做真实源码变异时，三个变异里前两个变红，**第三个只能原地跳过**：
它改的是 `Handlers.Controls.cs` 里的早退，而仿真 Link 进来的是 `SelectionGate.cs`
——handler 依赖 UWP 类型，**根本编不进 net10 测试工程**。

于是那一处修法只能靠源码扫描守：`RestoreIsNotGatedOnListener` 扫每个
`SelectionRestore.Schedule(` 站点，要求它走纯判据、且所属 `Dispatch` 里不许有
`callback` 早退。把早退塞回两个 handler 的门口各做一次变异，两次都指名到行号变红。

教训：**仿真绿不代表那份代码没事**——它只能守住被 Link 进来的那部分。
对 Link 不进来的文件，唯一的防线是把它当文本读。

---

## 12. `Selector`（ListView / GridView / ComboBox 的共同基类）：受控 `SelectedIndex` 的三条硬事实

`RadioButtons` 属于 WinUI 2 库（`release/2.8` 的 `dev/` 下），而
**`ListView` / `GridView` / `ComboBox` 是 `Windows.UI.Xaml` 的 `Selector`**，
真身在 `main` 分支的 DXAML 内核：`dxaml/xcp/dxaml/lib/Selector_Partial.cpp`。

### 12.0 这一次源码是怎么拿到的（以及行号怎么验）

| 环节 | 情况 |
|---|---|
| `curl` / `gh` | **全部出网失败**（`github.com` / `api.github.com` / `raw.githubusercontent.com` 一律 `000`，`gh` 的 token 也已过期） |
| 实际取回方式 | 只能走 `WebFetch`（服务端取），因此**拿不到可落盘的本地副本**，`tools/winui2-ref/` 下没有这一份 |
| 行号可信度 | WebFetch 返回的行号**不是权威的**，所以只用"两次窗口取回的重叠区互相印证"来定：取 1-140 与 130-200 两个窗口，130-140 这 11 行逐字一致 ⇒ 这一段的行号可用。<br>**凭这一条，只有 `144-148` 敢写进注释**；其余引用一律按函数名给，不臆造行号 |

> 这是本文件第一次出现"拿不到本地副本"的情况。以后复核时先看这一节，
> 别把"没写行号"当成"没查过源码"。

### 12.1 三条硬事实

| # | 事实 | 位置 / 原文 |
|---|---|---|
| 1 | **受控写回会同步抛 `SelectionChanged`**。`SelectedIndex` 依赖属性变更 → `OnSelectedIndexChanged` → … → `EndChange` 里 `didChange` 分支**同步**调 `InvokeSelectionChanged` → `OnSelectionChanged(args)` → `RaiseSelectionChanged` → `pEventSource->Raise(...)` | `Selector_Partial.cpp:144-148`（DP 分派，行号已验）；余者按函数名：`Selector::EndChange` / `InvokeSelectionChanged` / `RaiseSelectionChanged` |
| 2 | **越界写入会被拒**。`if (newValue >= -1 && newValue < (INT) nCount)` 才真选中，否则整条 `undoChange`（改回旧值）；且 `nCount != 0` 时以 `IFC(E_INVALIDARG);` 收尾（注释原文：*Only throw if the value was actually out of range and there are already existing items.*）。`nCount == 0` 时改存 `m_selectedIndexValueSetBeforeItemsAvailable`，等 items 到位再补 | `Selector::OnSelectedIndexChanged` |
| 3 | **改 items 会牵动选中**。`NotifyOfSourceChanged` 的 Reset 分支先把不再存在的选中 `Unselect` 掉，再调 `SelectAllSelectedSelectorItems` 把仍然 `IsSelected` 的容器加回来，最后 `EndChange`——**这一窗口里既可能抛"没有实项"的事件，也可能抛"有实项"的事件** | `Selector::NotifyOfSourceChanged`（CollectionChange_Reset 分支） |

两条推论，直接决定了 handler 怎么写：

- **没有 `ReadyGate` 的用武之地。** `OnSelectedIndexChanged` 开头的提前返回只有
  `!IsSelectionReentrancyAllowed()`（重入锁）与 `IsInit()`（正在解析 XAML）两个条件，
  **与模板套没套上无关**——不像 `RadioButtons` 那道 `m_blockSelecting`。
  所以 ListView / GridView 的 `Dispatch` 里"就绪"那一位直接给 `true`，
  不是偷懒，是这里没有那个问题要问。
- **`-1` 是合法值**（`newValue >= -1` 的左边就是它），意思是"清空选中"。
  写回策略（`SelectionPolicy.ShouldApply`）因此**不能**把负值一律拒掉，
  否则 state 说"没选中"而控件还亮着某一项。清空的副作用由
  `SelectionRestore`（`target.Index < 0` 不纠正）那一边拦，不在这儿拦。

### 12.2 由此修掉的第 12 处 bug

`ItemsViewHandler`（`ListView` / `GridView` 共用的基类）**一份受控设施都没接**：
写 `SelectedIndex` 之前不登记回声、事件里不消费、`Unmount` 里也没有 `Forget`，
更没有"改 items 期间"的遮蔽和越界守卫。

为什么"每一条登记都有人领"那道契约（第 9 节）**一声没吭**：它开头那句
`if (expected.Count == 0) continue;`——**压根没登记的类直接被跳过**。
补的第四道契约（"写了受控属性 + 有回执通道 ⇒ 三件套齐全"）扫出来的是：
12 个受控站点里**只有它一个**缺三件套。

顺带修掉同一枚硬币的另一面：`ComboBox` / `RadioButtons` 的**直接写回路径**
此前也没有越界守卫（只有 `SelectionRestore` 那条延迟纠正挡着），
现在两处与 ListView 共用同一份 `SelectionPolicy`。

---

## 13. `SettingsExpander.IsExpanded`：声明了的事件可能是死的（2026-10 增补）

这一节记的是**一次核查的结果**，也是一条方法上的提醒：**别凭控件"应该"有回调就动手接**。

### 13.1 事实（本地参考源码，可复查）

`tools/ctk-ref/` 是 CommunityToolkit 的本地副本：

| 位置 | 内容 |
|---|---|
| `SettingsExpander.Events.cs:12`、`:17` | 声明了 `public event EventHandler? Expanded;` 与 `Collapsed;` |
| 全树搜索 `.Invoke` / `OnExpanded` / `OnCollapsed` | **零命中** —— 这两个事件没有任何一处被抛 |
| `SettingsExpander.xaml:263` | `ToggleButton.IsChecked` 与 `IsExpanded` 是 **TwoWay 绑定**（`:84` 同） |
| `SettingsExpander.cs:69` `OnIsExpandedChanged(oldValue, newValue)` | 依赖属性变更回调，**只**做一件事：`:72` 抛 automation peer 事件 |

于是整条链路是这样闭合的：

```
用户点表头
  → ToggleButton.IsChecked 变（TwoWay 绑定，xaml:263）
  → IsExpanded 依赖属性变
  → OnIsExpandedChanged（.cs:69）
  → 只抛 automation peer 事件（.cs:72）
  → CLR 事件 Expanded / Collapsed：没人抛，订阅了也收不到
```

**结论：`IsExpanded` 在这一版里只能是 `defaultValue`（非受控）语义。**
不是"还没接"，是**接不上**——回执通道不存在。真要受控，得用
`RegisterPropertyChangedCallback` 去盯 `IsExpanded` 这个依赖属性本身。

### 13.2 顺带纠正了一条无出处的注释

`Handlers.Template.cs` 的 `ExpanderHandler.Update` 里原来写着：
"官方对这类属性用 Controlled + counter-echo（订阅 Expanding/Collapsed 并抑制回声）"。

这句话在本仓库里**没有任何依据**：

- `tools/winui2-ref/dev/` 只有 `Breadcrumb`、`ItemsRepeater`、`NavigationView`、
  `RadioButtons` 四个目录，**没有 Expander**；
- 全树搜 `Expanding` 也是零命中；
- 所以"官方怎么做的"这件事**没有查过**，不能写。

已改成如实描述：元素与工厂没有回调参数 ⇒ 非受控语义 ⇒ 只在声明值真的变了时才写，
并注明"不对官方做法作任何断言"。**注释说一套、代码做一套，比没有注释更危险**——
下一个读代码的人会以为"回声抑制已经做了"，于是真出问题时先排除掉正确的方向。

### 13.3 由此补的第五道契约

第四道契约（"写了受控属性 + 有回执通道 ⇒ 三件套齐全"）有两个形状缺陷，都在这一轮补上：

1. **回执通道的判据太松。** 原来只查"类里出现过这个事件名"，
   于是 `Visibility.Collapsed` 会被当成 `IsExpanded` 的回执通道（同一个词）。
   现在一律要求**订阅的证据**：`<Event> +=`，或 `Rebind*` 那个把订阅收进去的助手。
2. **`EchoProne` 是手写清单，清单外的属性它根本不认识。**
   这就是 ListView / GridView 静默两个版本的同一个成因——只是这次漏的是"属性"而不是"类"。
   于是补第五道：**源码里每一个"用户可改属性"的写回点，必须在 `EchoProne` 里，
   或者被显式登记进 `UncontrolledByDesign`（附理由）**。
   两种归宿的分工是：真受控 → 补登记与三件套；非受控 → 登记理由。
   两处都没有 = 漏了，报警。

自查方式照例是两半：往真实源码里塞一个没登记的属性（`IsSelected`）必须报警（7 个文件各一处）；
把 `UncontrolledByDesign` 整份拿掉，`IsPaneOpen` 必须立刻变成"没人认领"——
**证明那份登记是承重的，不是写上去好看的**。

清点结果（不限接收者名地扫了 `Reactor.uwp/Internal/*.cs`）：
用户可改、且真的被 handler 写入的属性共 **9** 个 ——
`SelectedIndex` / `SelectedItem` / `IsChecked` / `IsOn` / `Text` / `Password` / `Value` /
`IsExpanded` / `IsPaneOpen`。其中前 8 个在 `EchoProne` 内；`IsExpanded` **同时**
也在非受控登记里（它在 `EchoProne` 里是为了"哪天接上了就自动生效"，
非受控登记记的是为什么现在接不上），`IsPaneOpen` 只在非受控登记里。

另外三个命中不是受控属性，别被它们带偏：
`Index`（`Handlers.Virtual` 里 `Slot` / `Mounted` 的内部簿记字段，不是控件属性）、
`IsEnabled`（用户改不动）、`IsTabStop`（在 `InputApplier`，不是 handler）。

---

## 15. `x:Uid` 那笔写发生在订阅之后（2026-10 增补，第 13 处）

### 15.1 次序是源码实证，不是推断

`Reconciler.Build`（`Internal/Reconciler.cs`）的三步顺序，逐行读出来是这样：

| 序 | 位置 | 做什么 |
|---|---|---|
| ① | `Reconciler.cs:70` | `using var mount = PropWriter.BeginMount();` |
| ② | `Reconciler.cs:85` | `handler.Mount(...)` —— `TextBoxHandler` 在里面先 `native.Text = …`（`Handlers.Basic.cs:66`），**再** `RebindTextChanged(native, OnChanged)`（`:69`）订阅 |
| ③ | `Reconciler.cs:102` → `:1245` | `ApplyModifiers` → `Localization.ApplyUid`，且**只在 `PropWriter.IsMounting` 时跑**；`Localization.cs:83` 给 `TextBox` 写 `box.Text = resw 里的值` |

也就是说：**第 ③ 步的写落在第 ② 步的订阅之后。** `RebindTextChanged` 是立即
`textBox.TextChanged += handler`（`Reconciler.cs:1062`），没有延迟。

于是那一笔写抛出来的 `TextChanged` **没有任何回声登记**——`ApplyUid` 拿不到
`TextBoxHandler` 的 `TextEcho`（那是 handler 的私有静态字段），也不该拿到。
结果是被当成用户输入回调出去：`OnChanged("来自 resw 的初始文案")` → `setState(...)`。

界面上：输入框的初始值被 resw 里的串**顶掉**，还**多一次 `OnChanged`**。
若 `OnChanged` 里做校验或记脏标记，就凭空多一条脏记录。

### 15.2 修法与它的边界

`RebindTextChanged` 的闭包里加一道：

```csharp
if (PropWriter.IsMounting || !textBox.IsLoaded) { …Gate…; return; }
onChanged(((TextBox)s).Text);
```

两条判据**互补**，不是冗余：

- `PropWriter.IsMounting` —— 若 `TextChanged` 同步抛（依赖属性变更回调的路径），
  事件在我们自己的写值语句返回前就到了，此时 `Build` 还在栈上；
- `!IsLoaded` —— 若那一发延后到树挂上**之前**才抛，靠这一条兜住。

**已知边界**：两者都假——事件延后到 `Loaded` **之后**才抛——时拦不住，
那一发与真实键入无从区分。它的形状不是"记成挂载期冒出去"，而是
**被当成一次用户输入**。这条边界写进了 `MountOrderTests` 的用例里，
不假装能拦。

**一个语义后果要说清**：修好之后 resw 的值会留在控件上，直到下一次 patch
由 state 接管。这与 `x:Uid` 作为"初始值覆盖"的语义一致（`ApplyUid` 本来就排在
代码写值之后，注释里写明了要跟 XAML 编译器生成的顺序一致）。
真正被修掉的是那次假回调和 state 被凭空改写。

### 15.3 为什么前六道都没抓到

关键分工：**第五道管的是"属性名认不认得"，不管"写回点在谁手里"。**
`Text` 早就在 `EchoProne` 里，所以第五道一声不吭；而其余五道的扫描口径都是
`Handlers.*.cs`，`Localization.cs` 根本不在视野内。

这跟第 12 处（`ItemsViewHandler` 整个类不在视野内）是同一个成因，
只是这次漏的是**文件**而不是**类**。所以本轮顺手把"口径"这件事做成了一条纪律：
**每写一道扫描型契约，都要把它的扫描范围反过来量一次**——扩到全树跑一遍，
把多出来的东西逐个归类（该管的 / 不该管的 / 形状不同的），并把结论写进注释。

量下来全树只有 3 处 handler 之外的"用户可改属性"写回命中：

| 位置 | 结论 |
|---|---|
| `Localization.cs:79` `block.Text`（`TextBlock`） | `TextBlock` 没有文本回执通道，写它不产生回声 → 不构成问题 |
| `Localization.cs:83` `box.Text`（`TextBox`） | **真洞口**，已修 |
| `Core/RenderContext.cs` 的 `hook.Value`、`Internal/WeakTable.cs` 的 `box.Value` | 内部字段，不是控件属性 → 靠"接收者必须被声明成控件类型"排除 |

### 15.4 第七道契约：登记必须指向真实存在的中和手段

光把洞口登记下来是不够的——"登记了但没修"最容易蒙混过关。
所以每条登记都带一个**锚点**（`AnchorMethod` + `AnchorPattern`），
契约会去那个方法体里找这段正则，找不到就报警：

| 登记 | 锚点 | 含义 |
|---|---|---|
| `TextBlock.Text` | `RebindTextChanged\(TextBox` | 唯一的文本订阅入口只认 `TextBox`，`TextBlock` 走不进来 |
| `TextBox.Text` | `PropWriter\.IsMounting\s*\|\|\s*!textBox\.IsLoaded` | 就是上面那道屏蔽 |

实测集合必须**恰好等于**登记表：多了报警（新洞口没人认领），少了也报警（登记失效）。

变异做**两个方向**，这是这一道的关键：

- 删掉 `PropWriter.IsMounting || !textBox.IsLoaded` 那一行 → **必须红**；
- 只删掉旁边那行 `ReactorLog.Gate` → **不该红**。

后者证明锚点盯的是**机制**，不是日志。只做第一个方向的话，
"把锚点配到一行无关代码上"这种失聪照样能蒙混过去。


---

## 16. 改 `Minimum` / `Maximum` 会把受控值夹走（2026-10 增补，第 14 处）

### 16.0 这一次源码是怎么拿到的

| 环节 | 情况 |
|---|---|
| `curl` / `gh` | 照旧全部 `000`（本机 `http_proxy` 拦 `github.com` 的 CONNECT） |
| 实际取回方式 | `WebFetch`（服务端取） |
| 取回的份 | ① `release/2.8/dev/NumberBox/NumberBox.cpp`（WinUI 2，我们的 `NumberBox` 就是这一支）<br>② `release/2.8/dev/NumberBox/NumberBox.idl`<br>③ `main/dxaml/xcp/dxaml/lib/RangeBase_Partial.cpp`（`Slider` 的真身，`Windows.UI.Xaml` 内核） |
| 没取到的 | `RangeBase.g.cpp`（**生成**代码，仓库里没有），以及 `Windows.UI.Xaml` 的 `PasswordBox` 行为 —— 下面各自标了"这一段没有源码" |

> 路径这次是**先列目录再取文件**：`WebFetch` 猜路径连着 404 了三次
> （`main/dev/...` 是 WinUI 2 时代的布局，WinUI 3 的 `main` 已经把 `dev/` 去掉了）。
> 用 `/tmp/tree.json`（之前 `curl` 成功那次留下的全树清单）确认 `NumberBox/NumberBox.cpp`
> 才拿到。以后别再手猜路径——**猜错一次就是 404，404 和"网络被拦"长得一模一样**。

### 16.1 `NumberBox`：改边界 → `CoerceValue` → 抛 `ValueChanged`（有源码）

```cpp
void NumberBox::OnMinimumPropertyChanged(const winrt::DependencyPropertyChangedEventArgs& args)
{
    CoerceMaximum();
    CoerceValue();          // ← 改下界就夹一次
    UpdateSpinButtonEnabled();
    ReevaluateForwardedUIAName();
}

void NumberBox::OnMaximumPropertyChanged(const winrt::DependencyPropertyChangedEventArgs& args)
{
    CoerceMinimum();
    CoerceValue();          // ← 改上界也夹一次
    ...
}

void NumberBox::CoerceValue()
{
    const auto value = Value();
    if (!std::isnan(value) && !IsInBounds(value) &&
        ValidationMode() == winrt::NumberBoxValidationMode::InvalidInputOverwritten)
    {
        const auto max = Maximum();
        if (value > max) { Value(max); } else { Value(Minimum()); }
    }
}
```

`Value(...)` → `OnValuePropertyChanged` → `newValue != oldValue` 时
`m_valueChangedEventSource(*this, *valueChangedArgs)` —— **事件里的值是夹取后的**。

两个附带事实（都是后面要用到的）：

- **`ValidationMode` 的默认值就是 `InvalidInputOverwritten`**：idl 里
  `NumberBoxValidationMode { InvalidInputOverwritten, Disabled }` 第一项即默认，
  而我们的 handler 从没设过它 ⇒ 夹取生效。
- **改两个边界 = 两次夹取机会**（`On*PropertyChanged` 各调一次 `CoerceValue`）。
  这是"为什么不能用一次 `Expect` 顶替静默窗"的判决依据。

### 16.2 `Slider`（`RangeBase`）：内核那一份拿到了，夹取那一段没拿到

`main/dxaml/xcp/dxaml/lib/RangeBase_Partial.cpp` 里能读到的：

- `put_Minimum` / `put_Maximum` 只做 `EnsureValidDoubleValue`，转给生成类；
- `OnPropertyChanged2`：`Minimum` → `RangeBaseGenerated::OnMinimumChangedProtected`，
  `Maximum` → `…OnMaximumChangedProtected`；
- `Value` → `OnValueChangedImpl`，注释原文 *Raises the ValueChanged routed event.*，
  里面 `pEventSourceNoRef->Raise(...)`。

**"改边界会不会夹 `Value`"这一段在生成代码里（`RangeBase.g.cpp`），仓库里没有这份文件，
本机也取不到** —— 所以这一条对 `Slider` 是**文档级**依据，不是源码级：
`RangeBase.Value` 的文档写着 *"may be coerced"*，`Minimum` 的备注给了一个字面例子
（*Minimum 大于默认 Maximum 时，Maximum 被设成等于 Minimum*），说明这几个属性之间
**确实互相夹**。

结论按强度分开记：`NumberBox` 有源码；`Slider` 只有文档。修法对两者一视同仁，
是因为**（a）** 静默窗即使永远没等到事件，代价也是零（它只在渲染路径里开着）；
**（b）** 反过来，若 `Slider` 真的会夹而没罩窗，漏的就是一发假回调 —— 代价不对称，
所以按"宁可罩上"处理。

### 16.3 由此修掉的第 14 处（两半，两个 handler 共用）

`Slider`（`Handlers.Basic.cs`）与 `NumberBox`（`Handlers.Input.cs` 的 `ApplyRange`）
的 `Update` 都是"**先写区间 → 再写受控值 → 最后才 `Rebind`（退订旧的、挂新的）**"。
于是写区间的那一刻，**上一轮的订阅还挂着**，夹取抛出的 `ValueChanged` 会被旧回调接住：

| 半 | 触发 | 为什么旧的 `Expect` 挡不住 |
|---|---|---|
| ① 改区间把值夹了 | `Minimum` / `Maximum` 写成新区间 | 夹出来的值**事先不知道**，且两个边界各夹一次 —— 一次登记装不下两发 |
| ② 受控值本身被夹 | state 声明了一个越界的值 | 登记的是声明值，回读的是夹取后的值 ⇒ `mismatch`，那一发照样出去 |

两半各有各的修法：

- ① `EchoGuard.Silence(control)` —— **静默窗**。不猜值，只凭"这段窗在渲染路径内部、
  里面不可能有真实用户输入"来判。
- ② `RangePolicy.Coerce` —— 登记/下发都改用**夹取后**的值。
  注意它**不改变控件终态**（写声明值会被控件夹成同一个值），改的只是"回声变得可预测"。

② 这条其实早有预兆：`Slider` 的注释里已经写着"写下去可能被夹成别的值，登记永远
等不到匹配"——但当时只配了 `CancelIfUnconsumed`（管"事件没来"），
**没管"事件来了但值不对"**。这是同一个现象漏掉的一半。

### 16.4 第八道契约

> 写了 `Minimum` / `Maximum` 的地方，必须在静默窗里写 —— 除非（a）这个控件没有
> 受控值（`ProgressBar` / `ProgressRing` 也写 `Minimum`，但它们没有回调，没人接那一发）；
> （b）调用点在 `Mount` 里（那一刻订阅还没挂上，写了也没人听见）。

合成样本六档 + 真实源码变异**两个方向**（删掉窗 / 把窗挪到写入之后）——
后者是为了证明这条契约盯的是**位置**，不是"类里有没有出现 `Silence`"。

> 顺带记一个扫描器的坑：方法头的正则原本写成"修饰符可有可无"，
> 于是**行首的一个普通调用**（`ApplyRange(control, n);`）会被判成方法头，
> 回溯就地中断，"调用点没罩窗"这一档直接失聪。改成**必须带至少一个修饰符**才认。
---

## 17. 改「不是受控值」的属性，控件却把受控值改了（2026-10 增补，第 15 处）

### 17.0 这一轮的取证路径

| 环节 | 情况 |
|---|---|
| `curl` / `gh` / `--noproxy` | 照旧全部 `000`（本机 `http_proxy` 拦 `github.com` 的 CONNECT，绕开代理也是 000） |
| 实际取回方式 | `WebFetch`（服务端取） |
| 取回的份 | ① `main/dxaml/xcp/dxaml/lib/ListViewBase_Partial.cpp`<br>② `main/dxaml/xcp/dxaml/lib/Selector_Partial.cpp`（**反向**结论：这个文件里没有 SelectionMode）<br>③ `release/2.8/dev/NavigationView/NavigationView.cpp` |
| **没取到的** | `ListViewBase::OnSelectionModeChanged` 的**函数体**（文件被截断，只拿到调用点）；`RadioButton.cpp`（404） |

> **先问"在不在基类"再来回找。** 第一次直奔 `Selector_Partial.cpp` 找 SelectionMode，
> 拿回来的答复是：`SelectionMode` **不是 `Selector` 基类的依赖属性**，
> `OnPropertyChanged2` 里只有 `SelectedIndex / SelectedItem / SelectedValue /
> SelectedValuePath / IsSynchronizedWithCurrentItem / ItemsControl_ItemsHost` 六个分支。
> 它是 `ListViewBase` 引入的。**在错误的类里找不到，和"这一条不存在"长得一模一样。**

### 17.1 `ListViewBase`：改 `SelectionMode` 会更新所有选中相关属性（有源码）

```cpp
// ListViewBase::OnPropertyChanged2
case KnownPropertyIndex::ListViewBase_SelectionMode:
    {
        // Call OnSelectionModeChanged when the selection mode changes.
        IFC(OnSelectionModeChanged(
            static_cast<xaml_controls::ListViewSelectionMode>(args.m_pOldValue->AsEnum()),
            static_cast<xaml_controls::ListViewSelectionMode>(args.m_pNewValue->AsEnum())));

        // Call the Selector::UpdateVisibleAndCachedItemsSelectionAndVisualState function
        // OnSelectionModeChanged will update all Selection related properties
        IFC(UpdateVisibleAndCachedItemsSelectionAndVisualState(false /* updateIsSelected */));
        break;
    }
```

注释是**源码里逐字写着**的：`OnSelectionModeChanged will update all Selection related
properties`。至于具体收敛成什么（Multiple → Single 留第一个、→ None 全清），
在 `OnSelectionModeChanged` 的函数体里，**那一段没取到**——所以这一条只有"会动"，
没有"动成几"、也没有"动几次"。这一点决定了修法只能是开窗，不能是猜值（见 16.2 那套
代价不对称的论证，此处同理）。

### 17.2 `NavigationView`：清空 `MenuItems` 会把选中一起带走（有源码）

```cpp
void NavigationView::OnSelectionModelSelectionChanged(...)
{
    auto selectedItem = selectionModel.SelectedItem();

    // Ignore this callback if:
    // 1. the SelectedItem property of NavigationView is already set to the item
    //    being passed in this callback. ...
    // 2. Template has not been applied yet. ...
    if (m_shouldIgnoreNextSelectionChange || selectedItem == SelectedItem() || !m_appliedTemplate)
    {
        return;
    }
    ...
    SetSelectedItemAndExpectItemInvokeWhenSelectionChangedIfNotInvokedFromAPI(selectedItem);
}
```

`MenuItems` 是 `Clear()` 之后重新 `Add` 的。清空把选中容器一起带走：
`SelectionModel.SelectedItem()` 变 `nullptr`，而 NavigationView **自己的** `SelectedItem`
依赖属性此刻还是旧值 ⇒ `selectedItem == SelectedItem()` 不成立 ⇒ **不早退** ⇒ 继续走
`SetSelectedItemAndExpectItemInvoke…` → `ChangeSelection` → `RaiseSelectionChangedEvent`。

反过来，这也解释了"经由 API 选中"为什么大多**没有**回声（Mount 那条注释里记过）：
那时 `selectedItem == SelectedItem()` 成立，直接 `return`。

**两条结论合起来才是完整的一条**：清空会抛；而"清空之后补发一次"会因为
`SelectedItem` 已经是 `nullptr`、新值非 `nullptr` 而**照样抛**（所以要 `Expect`）。
前后两发的作者都是我们。

### 17.3 由此修掉的第 15 处（三笔源码级 + 两笔"证据不足"）

| # | 位置 | 依据 | 处理 |
|---|---|---|---|
| ① | `ItemsViewHandler.Update` 写 `SelectionMode` | 17.1（源码） | 静默窗 |
| ② | `NavigationViewHandler.Update` 的 `ApplyMenuItems` | 17.2（源码） | 静默窗 **+** 重建后补发受控选中值 |
| ③ | `NavigationViewHandler.Update` 写 `PaneDisplayMode` | 同一个 `UpdateRepeaterItemsSource`：Top / Left 切换会把另一侧的 repeater 源置空、这一侧重建 | 静默窗 |
| ④ | `PasswordBoxHandler.Update` 写 `MaxLength` | **没有源码**（Windows.UI.Xaml 那一支不开源），文档只说它约束输入 | 静默窗（代价不对称） |
| ⑤ | `RadioButtonHandler.Update` 写 `GroupName` | **没有源码**（`RadioButton.cpp` 404） | 静默窗（代价不对称） |

② 的**第二半不是假回调，是丢状态**：只按"声明值变了才写"判的话，重建把选中清空之后
永远补不回来（声明值压根没变）⇒ 选中态永久丢失，界面表现为"换了一次菜单项，
导航条再没有选中项"。这一半此前没人管——旧判据是 `oldElement.SelectedIndex !=
newElement.SelectedIndex`，把 `menuChanged` 加进去才接得上。

④⑤ 的依据强度要照实分档写：**源码级**只有 ①②③，④⑤ 是"不知道会不会动，
所以按会动处理"。理由与 16.2 完全相同——窗在"永远没等到事件"时的代价是零，
漏罩则是一发假回调。

### 17.4 第九道契约：把「按名字认」反过来，改成「默认全管、豁免要登记」

第八道只认 `Minimum` / `Maximum` 两个名字。于是 `SelectionMode`、`MaxLength`、
`GroupName` 这些同样会牵动受控值的写入**一条都进不了视野**——
**按名字认，漏掉的是名单之外的全部**。第 15 处就是这么漏的。

第九道把判据反过来：

> 有受控值（`EchoGuard`）的 handler 里，**每一处**非受控属性的写入，
> 要么在静默窗里、要么在 `InertByDesign` 里登记过（登记要写理由）。

于是以后新增一处属性写入，这条契约会先红，逼出一句"它会不会牵动受控值"的答复。
登记一条的门槛是**说得出它为什么改不动受控值**；说不出就不登记，开窗。

自证：合成样本五档（窗内 / 没窗 / 已登记 / 没有受控值的类 / 挂载期）+
真源码变异两个方向（删窗 / 把窗挪到写入之后）+
**反向对照：把 `InertByDesign` 整份当作不存在，真源码里必须冒出违约**——
否则那份登记是摆设。

### 17.5 这一轮扫描器踩到的两个坑

**坑一：多窗方法会「串窗」。** 判据原本是"从写入行往上回溯，先遇到窗就算合规"。
一个方法里有两道窗时，**前面那道会把后面那道罩的写入一并认领**：
`NavigationView.Update` 里 `PaneDisplayMode` 的窗在前、`MenuItems` 的窗在后，
删掉后者，扫描器照样判"罩住了"——变异测试因此**假绿**。
改成算窗的**块范围**（`using { … }` 之间），写入行要真的落在里面才算。
（是第九道把它逼出来的：第八道只管 Min / Max，恰好没有多窗方法。）

**坑二：变异要按视野分派。** 罩的是 `Minimum` / `Maximum` → 第八道必须报警；
其余 → 第九道必须报警。不分派的话，新增的那些窗会变成"第八道没报警"的假红——
但它本来就不该管它们。

---

## 18. 集合写入是**方法调用**形状，前两道契约一条都看不见（2026-10 增补）

### 18.0 这一轮从哪儿开始

第 17 节那条线（改非受控属性牵动受控值）收尾时留下一个疑问：
`ComboBox.ReplaceItems` 里那两行 `Items.Clear()` / `Items.Add(item)`，
第九道契约判它"合规"——**它是真合规，还是压根没被看见？**

答案是后者。于是有了第十道。

### 18.1 形状盲区

第八、九道的判据都是 `X.Prop = v` 这种**赋值**形式：

```csharp
private static readonly Regex AnyWrite =
    new(@"\.([A-Za-z]\w*)\s*(?<![=!<>+\-*/])=(?![=>{])", RegexOptions.Compiled);
```

而"改集合"是**方法调用**形式：`control.Items.Clear()`、`nav.MenuItems.Add(item)`、
`bar.ItemsSource = carrier.Items`。前两道一条都匹配不到。

**这一条漏得比"少认了几个属性名"严重得多**：换集合恰好是牵动受控选中值
**最典型**的动作——第 17 节那个行为模型（`SiblingWriteSim`）建模的就是它：
`Clear` 把选中冲成 -1，那一发抛在旧订阅还挂着的时候。
所以第九道把"最有嫌疑的一类写入"整类漏掉了，
而它给出的绿灯让人误以为那一类已经被管住。

> 教训与四·二十八（"按名字白名单认写入点"）是同一条的另一半：
> **按形状认写入点，漏掉的是别的形状的全部。**
> 两次都是同一个动作救的场——把判据反过来（默认全管，豁免要登记）。

### 18.2 28 处集合写入逐条归类

第十道扫 `Reactor.uwp/Internal/*.cs`（66 个文件），命中 28 处。逐条落点：

| 处 | 位置 | 归类 | 依据 |
|---|---|---|---|
| 2 | `ComboBox.ReplaceItems` | `Rebuilding` 标记块内 | 有机制 |
| 2 | `RadioButtons.ReplaceItems` | `Rebuilding` 标记块内 | 有机制 |
| 2 | `NavigationView.ApplyMenuItems` | 辅助方法 → **调用点**在静默窗内 | 第 17 节加的窗（1484）与挂载期（1396） |
| 2 | `Reconciler.PatchItems` | 辅助方法 → **调用点**（`Handlers.Controls.cs:1159`）在 `Rebuilding` 块内 | **跨文件**才查得到 |
| 3 | 三处 `Initialize` / `Mount` 里的 `Items.Add` | 挂载期免检 | 那一刻订阅还没挂上 |
| 1 | `AutoSuggestBox` 的 `ItemsSource` | 惰性登记 | 回执是 `SuggestionChosen` / `TextChanged`，换候选不抛 |
| 2 | `BreadcrumbBar` 的 `carrier.Items` / `bar.ItemsSource` | 惰性登记 | 载体不进树、无订阅；真控件那笔走 `ItemsSource`，回执是 `ItemClicked`（只在点击时抛） |
| 2 | `SettingsExpander.ApplyItems`（挂载） | 惰性登记 | 不是 `Selector`，`Items` 变动不抛选中类事件 |
| 2 | `SettingsExpander.ApplyItems`（重建） | 惰性登记 | 同上 |
| 2 | `Expander.ApplyHeader` 的 `panel.Children` | 惰性登记 | `Panel` 没有选中类回执通道 |
| 6 | `Reconciler` 的 `panel.Children` | 惰性登记 | 同上 |
| 2 | `VirtualizingListHandler` 的 `canvas.Children` | 惰性登记 | `Canvas` 没有选中通道 |

**没有一处是"裸写"的。** 也就是说这一轮没有挖出新的真洞口，
挖出来的是**一份此前不存在的清单**——以前只能靠人记得
"改集合要关起来"，现在契约会逼你答复。

### 18.3 惰性登记为什么要按（类名, 属性名）

同一个属性名 `Items`：
- 在 `ComboBoxHandler` 上是**要管的**（`Selector.Items`，`Clear` 会冲掉选中）；
- 在 `SettingsExpanderHandler` 上是**不用管的**（展开区卡片容器，不是 `Selector`）。

只按属性名登记，会把前者一起豁免掉——那这份登记就成了
"给最危险的那条发免死金牌"。所以登记表是三元组：

```csharp
private static readonly (string Class, string Property, string Reason)[] InertCollectionByDesign =
{
    ("Reconciler", "Children", "布局面板的子元素：Panel 没有选中类回执通道，改 Children 不抛 SelectionChanged"),
    ("SettingsExpanderHandler", "Items", "展开区卡片容器：SettingsExpander 不是 Selector，Items 变动不抛选中类事件"),
    // …
};
```

（第九道那份 `InertByDesign` 只按属性名记，是因为它只扫 `Handlers.*.cs`，
那些类名恰好不冲突。**这是一处已知的不一致**，等它哪天扩视野时会咬人。）

### 18.4 逼出扫描器的两个真洞

**洞一：调用点正则把点前缀排除了。**

```csharp
var call = new Regex(@"(?<![\w.])" + Regex.Escape(method) + @"\s*\(", …);   // 旧
var call = new Regex(@"(?<![\w])"  + Regex.Escape(method) + @"\s*\(", …);   // 新
```

真源码里的调用点是带接收者的：**`reconciler.PatchItems(control, …)`**，
前面恰好是个点，于是"前面不许是点"这条把**唯一那一处调用点**排除掉了。
后果：`AllCallSitesGuarded` 一个调用点都找不到 → 返回 `true` →
"调用点裸着"那一档永远不报警；变异测试跟着**假绿**。

第九道的 `AllCallSitesSilenced` 也用的旧写法，一并改了。
它之所以一直没暴露，是因为**合成样本里用的是不带点的写法**
（`ApplyRange(control, n);`）——样本和真源码长不一样，
样本就测不出真源码那条路径上的洞。

> 单记一条：**合成样本要长得像真源码，包括"带不带接收者"这种细节。**
> 样本替你测的，只有它自己走过的那条路。

**洞二：跨文件的变异，只在同文件里数违约 → 假绿。**

删掉 `Handlers.Controls.cs` 里那行 `Rebuilding.Set(control, true)` 之后，
真正冒出来的违约在**另一个文件**里（`Reconciler.PatchItems` 那两行）。
只在被改的那个文件里数，永远数不到，于是这处变异被判"没多报"。

改法：变异后把新内容**替换进"所有文件"列表**，再数**全局**总数。

顺带得按"这个块罩住了什么"分派（与四·三十同一条）：
删掉 `Slider` 那道只罩着 `Minimum` / `Maximum` 的窗，
第十道本来就不该红——要求它红就是假红。
判据是块内有没有集合写入，**或者**块内有没有一个"定义体里含集合写入"的调用。

### 18.5 想过但**否决**的方案：渲染期闸门（记下来免得以后重犯）

排查途中顺着 `PatchCore` 的次序往下摸：

```
handler.TryUpdate（写受控属性 → Rebind 换订阅）
  ↓
ApplyModifiers（Style / RequestedTheme / Visibility / Padding / …）   ← 新订阅已挂上
  ↓
Localization.ApplyUid（仅 IsMounting 时跑）
```

第二步那些写入**不在任何按控件的抑制块里**（窗和 `Rebuilding` 都在 handler
内部，那时早关了）。于是想到一个更根本的招：

> 加一个 `PropWriter.IsPatching`（渲染期标记），把第 13 处那条
> `IsMounting || !IsLoaded` 的屏蔽从"挂载期"扩到"整个渲染期"，
> 一次性罩住 `ApplyModifiers` 的全部写入，以及未来新增的任何 modifier。

**否决，两条理由：**

1. **拦不住它最该拦的那一发。** `Visibility = Collapsed` 的后果是
   **布局驱动**的：折叠 → 内部 repeater 在**下一帧**才回收元素 →
   那时 `PatchCore` 早已返回、闸门早关。第 13 处记为"已知边界"的那条
   （窗罩不住延后到关窗之后的异步事件），在这里不是边缘情形，是**主情形**。
   加了闸门会让人以为罩住了，实际没有——比不加更糟。
2. **那一发已经被判据零罩住了。** repeater 回收走的是
   `RadioButtons.cpp:310-317` 的 `Select(-1)`，它抛的事件
   `AddedItems` 是空的 → `SelectionArgs.SelectedSomething(args)` 为假 →
   `SelectionVerdict.NoItem` → `Suppress`。再加一层是重复记账。

**结论：时间窗（静默窗 / `Rebuilding` / `IsMounting`）只管得住同步后果；
异步后果得靠"控件当时处于什么状态"这类持续判据**（`ReadyGate` 就是这种）。
以后若要加"渲染期闸门"，先回答：它罩不罩得住异步后果？

> 顺带记一条排查结论：`ApplyUid` 那行有 `&& PropWriter.IsMounting` 的门槛
> （`Reconciler.cs:1243`），所以第 13 处那道屏蔽**覆盖到了全部 `ApplyUid`
> 场景**——Update 路径上它压根不跑。这条一开始没看清，
> 以为第 13 处漏了 Patch 那一半，查下来是没漏。

---

## 19. NavigationView（WinUI 2.8）：抑制为什么必须是持续标记

### 19.1 早退条件（逐字核对过）

```cpp
void NavigationView::OnSelectionModelSelectionChanged(
    const winrt::SelectionModel& selectionModel,
    const winrt::SelectionModelSelectionChangedEventArgs& e)
{
    auto selectedItem = selectionModel.SelectedItem();

    // Ignore this callback if:
    // 1. the SelectedItem property of NavigationView is already set to the item
    //    being passed in this callback. This is because the item has already been selected
    //    via API and we are just updating the m_selectionModel state to accurately reflect the new selection.
    // 2. Template has not been applied yet. SelectionModel's selectedIndex state will get properly updated
    //    after the repeater finishes loading.
    // TODO: Update SelectedItem comparison to work for the exact same item datasource scenario
    if (m_shouldIgnoreNextSelectionChange || selectedItem == SelectedItem() || !m_appliedTemplate)
    {
        return;
    }
```

三条各导出一条结论，都直接决定我们的接线：

| 条件 | 结论 | 对 handler 的影响 |
|---|---|---|
| `selectedItem == SelectedItem()` | **经 API 选中不抛事件** | `Expect` + `CancelIfUnconsumed` 大概率**恒空转**（登记永远等不到消费）。留着是防御：真抛了它就是第一道保险 |
| `!m_appliedTemplate` | **未就绪期间一发都不抛** | 判据一（NotReady）在 NavigationView 上是控件自己兑现的 |
| `after the repeater finishes loading` | **那一发不是取消，是<b>推后</b>**：repeater 加载完之后 `selectedIndex` 会被"正确更新"，届时照抛，而且选中的是实项 | 判据零（`SelectedItemContainer` 守卫）拦不住它；它由**判据一**接住——repeater 是模板子树的一部分，子先于父，所以那一发回来时控件尚未 `Loaded`（见 §20） |

> **2026-10-06 修订**：这一节原先写的是"后果的到场时间在我们返回之后 →
> 时间窗罩不住 → 必须用持续标记"。建完行为模型（`RebuildEchoSim`）后发现
> **依据错位**：`finally` 关门同样在 `Update` 返回那一刻，持续标记也罩不住"延后"。
> 第 19 节的修法本身是对的，但它承重在<b>另一档</b>——
> 补发受控值那一发的<b>回读值猜不准</b>，`Expect` 匹配不上，只能靠区间。
> 详见 `docs/release-notes/alpha.6.md` §19.0。

### 19.2 取消选中那一发的事件参数

```cpp
void NavigationView::RaiseSelectionChangedEvent(
    winrt::IInspectable const& nextItem, bool isSettingsItem,
    NavigationRecommendedTransitionDirection recommendedDirection)
{
    auto eventArgs = winrt::make_self<NavigationViewSelectionChangedEventArgs>();
    eventArgs->SelectedItem(nextItem);
    eventArgs->IsSettingsSelected(isSettingsItem);
    if (auto container = NavigationViewItemBaseOrSettingsContentFromData(nextItem))
    {
        eventArgs->SelectedItemContainer(container);
    }
    ...
    m_selectionChangedEventSource(*this, *eventArgs);
}
```

`nextItem` 为 `nullptr` 时 `if (auto container = ...)` 不成立 →
**`SelectedItemContainer` 不会被赋值**（保持 null）。
于是 handler 里那句 `args.SelectedItemContainer is NavigationViewItem item`
天然等价于判据零（"`AddedItems` 里没有实项"）。
NavigationView 的事件参数**没有 `AddedItems`**，共用的 `SelectedSomething` 接不上去，
但这个守卫替它兑现了同一件事——这就是"登记豁免"而不是"接上"的依据。

### 19.3 没取到的部分（不猜）

`OnMenuItemsSourceCollectionChanged` 与 `UpdateSelectionForMenuItems` 的函数体
在这份文件里没取到（取回时被截断）。能拿到的是**同类对照**——
Footer 那条路的完整函数体：

```cpp
void NavigationView::OnFooterItemsSourceCollectionChanged(const winrt::IInspectable&, const winrt::IInspectable&)
{
    UpdateFooterRepeaterItemsSource(false /*sourceCollectionReset*/, true /*sourceCollectionChanged*/);

    // Pane footer items changed. This means we might need to reevaluate the pane layout.
    UpdatePaneLayout();
}
```

末尾那句 `UpdatePaneLayout()` 是**布局失效**；主菜单那条路的
`UpdateRepeaterItemsSource` 末尾同样是 `InvalidateTopNavPrimaryLayout()` / `UpdatePaneLayout()`。
两者都把真正的工作推到下一帧。
**这足以判定"后果不同步"，不需要猜那两个函数体。**

但"不同步"**不等于"该由持续标记接住"**：持续标记的 `finally` 关门也在
`Update` 返回那一刻，一样罩不住下一帧。不同步的那一发真正的接手者是
**判据一（未就绪）**——见 §20。把这两件事混为一谈，是第 19 节最初那条理由
写错的根源。

### 19.4 三处口径不一致（都已修）

同一套概念在两道契约里有两种口径，迟早会咬人：

- **已修**：抑制块。第九道只认 `Silence` 窗，第十道认窗 + `Rebuilding` 标记。
  改用持续标记之后，第九道立刻报了两处**假**违约（`PaneDisplayMode` / `Icon`）。
- **已修（第 21 节）**：惰性登记。第九道按属性名记，第十道按（类名, 属性名）记。
  按名字记的后果是**一条登记、十二个类受益**——真源码里写 `Header` 的类有九个、
  写 `Content` 的有四个。`Content` 在 `CheckBox` 上是标签、在 `NavigationView` 上
  是**整棵当前页面子树**，两种语义混进一条豁免。改成按（类名, 属性名）记之后
  是 24 条 / 12 个类，并加了**逐条撤登记**的反向对照：光"整份撤掉必须红"
  挡不住反向的腐烂（往表里塞没人用的条目，整份照样绿）。
- **已修（第 21 节）**：扫描范围。第九、十一道扫 `Handlers.*.cs`（7 个），
  第十道扫全树（66 个）。第十一道拉平之后命中数没变（仍是 4 个受控选中类），
  但**"没扫到"和"扫了没问题"是两件事**——前者随时可能变成后者。
  第九道那条不用拉：它按"类里有没有 `EchoGuard`"准入，而 `EchoGuard`
  只存在于 `Handlers.Basic/Controls/Input.cs` 三个文件里，全树扫结果一样。

---

## 20. NavigationView（WinUI 2.8）：那一发回来时控件还没就绪

§19 留下的真问题。早退条件第二条是 `!m_appliedTemplate`，它的注释接着说
`selectedIndex` 会在 **repeater finishes loading** 之后被正确更新——
于是那一发**会**回来，只是晚一点。

**它回来时在什么时刻？** 关键在 XAML 的 Loaded 次序：**子先于父**。
repeater 是 `NavigationView` 模板子树里的一个元素，它的 `Loaded` 早于
`NavigationView` 自己的 `Loaded`。而 `ReadyGate` 的就绪判据正是"控件自己的
`Loaded` 到了没"（§：`ReadyGate` 类注释——`m_blockSelecting` 在
`RadioButtons::OnRepeaterLoaded()` 里才解禁，那个时刻同样早于控件 Loaded，
托管侧用控件 `Loaded` 对齐是**偏保守**的）。

所以：

```
我们写 SelectedItem
   → 模板还没套好：OnSelectionModelSelectionChanged 早退（一发都不抛）
   → repeater 加载完：selectedIndex 被正确更新 → 抛 SelectionChanged（实项！）
   → NavigationView 自己 Loaded  →  ReadyGate.IsReady 变 true
                    ↑
        那一发落在这一段里：IsReady 仍是 false
```

三个推论：

1. 判据零**拦不住**它——`nextItem` 不是 nullptr，`SelectedItemContainer` 有值；
2. 持续标记**也拦不住**它——`finally` 关门在 `Update` 返回那一刻，早于这一发；
3. **判据一拦得住**——那一发回来时 `IsReady` 还是 `false`。

`NavigationViewHandler` 在 Mount 里 `ReadyGate.Arm` 了，事件入口却没人问
`IsReady`——**装了闸门没接线**。补上两个入口各一句早退即可
（`Rebuilding` 检查之后、`SelectedItemContainer` 守卫之前）。

**教训（可复用）**：源码注释里"某件事会在 X 之后发生"这种话，
要同时读出**两件事**——"会延后"和"延后到什么时候为止"。
只读前半句会把 `!m_appliedTemplate`（一条早退）误当成
"这一发不存在"，从而给"不用接判据一"发了一张假豁免。

---

## 21. 已知边界：`Rebuilding` 是布尔，`Silence` 是深度计数器

同一个语义（"这一段的作者是渲染路径，不是用户"）在本仓库里有两套实现，
健壮性不一样：

| 机制 | 类型 | 可嵌套 |
|---|---|---|
| `EchoGuard.Silence` 窗 | `WeakTable<object, int>` 深度计数 | ✅ 内层关掉不影响外层 |
| `Rebuilding` 持续标记 | `WeakTable<TControl, bool>` | ❌ 内层 `Set(false)` 会把外层的门一起关掉 |

**今天咬不到人**：四处 `Rebuilding.Set(…, true)` 的区间里
（`ComboBox.ReplaceItems`、`RadioButtons.ReplaceItems`、`ItemsView.Update`、
`NavigationView.Update`）都不会再进同一个控件的同一段，嵌套不可达。

**为什么不去加计数器**：按本项目一贯的原则——加机制之前先查已有判据能不能覆盖。
嵌套不可达时改成计数器是给一个不存在的问题买保险，反而多一处要维护的状态。
**登记成已知边界比改代码诚实。** 真出现嵌套路径时，症状是"重建期间的用户点击
被吞掉一次"（内层提前关门），而不是"漏罩"——方向反过来，认得出。

顺带记一条同源的观察：**同一次排查里把两处口径拉平，往往只有一处真咬人**。
第 21 节那轮修了三处口径不一致（抑制块 / 惰性登记 / 扫描范围），
只有"抑制块"那一处当时就报了假违约，另外两处是纯预防。
但"没扫到"和"扫了没问题"是两件事，预防性的拉平不该因为"现在没红"就不做。

---

## 22. 第十二道契约：订阅侧（本轮没修 bug，是补防线）

### 22.1 为什么现在才想到

第四~十一道守的全是**写**：往控件上写什么、写在什么窗里、要不要夹取。
它们的共同前提是"这个 handler 已经接好了判据"。而受控闭环的另一半——
**接**的那一侧（`X.Event += h`）——没有任何一道契约在看。

这一条与 WinUI 内核源码无关（不像前几节那样要翻 `NavigationView.cpp`），
它是**接线性质**的问题：`Rebind` 每次 Patch 都被调用，用来把回调换成
捕获了新 state 的闭包——它天然就是"每帧路径"。里面一个裸 `+=`
就是每渲染一次叠一层，用户点一下回调跑 N 遍。

### 22.2 判据与口径

| 写法 | 判据 | 数量 |
|---|---|---|
| (a) 表驱动 | `+=` 罩在 `if (!Table.ContainsKey(control))` 里 | 12 |
| (b) 配对退订 | **同一个方法体内**有 `.<同一事件> -=` | 15 |
| (c) 一次性 | 登记（所在方法每实例至多跑一次） | 8 |

全树 **35** 个订阅点。（c）那 8 条按（文件, 事件）登记，
**实测集合必须恰好等于登记表**——这条同时挡住"漏登记"和"塞失效条目"。

> **（b）的口径是方法级，不是类级。** `+=` 在 `Update`、`-=` 只在 `Unmount`
> 这种真漏，放到类级就蒙混过关；而它正是这一道要拦的形状。
> 代价是 `BreadcrumbBar`（`+=` 在 `Mount`、`-=` 在 `Unmount`）得走登记——
> 它安全靠的是"Mount 每实例只跑一次"，**不是靠配对**。这一点在登记里写明了。

### 22.3 变异的两种假象（值得单记）

第一版按"所有含 `ContainsKey` 的行"与"所有 `-=` 行"批量拆，踩到两个坑：

1. **已豁免对象自己的配套代码**：拆 `BreadcrumbBar.Unmount` 的 `-=`
   本就不该有事（那个订阅点靠 Mount 一次性过关）→ 判"没报"是**假红**；
2. **`+=` 与 `-=` 在同一行**：`InputApplier.ApplyKeyEvents` 把两个委托当参数
   传给 `RebindKeyEvent`，整行注释掉连订阅点一起抹掉，违约数**不升反降**，
   看起来跟真漏一模一样。

改成**从订阅点反查保护者**：守卫改守卫那一行；配对就把方法体内该事件的
**全部** `-=` 一起改（改一处留一处仍成对，会假绿）。
拆法用"替换行内字符串"，不整行注释、不删行（删行会让括号配平塌掉）。
结果：**8 处守卫 + 15 处配对，全部承重**。

### 22.4 排查工具自己会改坏源码换行

变异是真往源码里写再 `finally` 还原。原来的还原用 `File.WriteAllLines`
（一律按 `Environment.NewLine` 重拼），而这个仓库是**混合换行**的
（`Core/` 11、`Elements/` 19、`Internal/` 20、`Hosting/` 3 个文件是裸 LF）。

隐患：哪天有人往 `SelectionPolicy.cs` 这种 LF 文件里加一处订阅，
跑一次测试就把整个文件改成 CRLF，`git status` 会把整个文件显示为已改——
**与"变异没还原"长得一模一样**，非常容易被误判成第二次事故（第 22 节那种）。

已改成逐字节还原（`ReadAllBytes` → `WriteAllBytes`），第七道 `Break()` 同样处理。
验证也升级：跑完用 `sha256sum -c` 核对被变异的 9 个文件，**全部 OK**。

## 23. 第十三道契约：三件套的**相对次序**

### 23.1 前十二道漏掉的是什么

三件套是「`Expect` 登记 → 下发（`control.Prop = …`）→ `CancelIfUnconsumed` 撤销」。
前十二道盯了其中两步，**都只看"有没有"，不看"排在哪"**：

- 第一道：`LineIsAnswered` 在 25 行窗口里做**包含判断**（有没有
  `XxxEcho.CancelIfUnconsumed(` 这个串），位置一概不问；
- 第四道：只问三件套齐不齐。

次序错了是同一个 bug 的两副面孔：

| 排错的方式 | 机制 | 结局 |
| --- | --- | --- |
| 撤销在下发**之前** | 回声此刻还没到，撤销看见「没人领」，把刚登记的期望**立刻抹掉** | 控件同步抛事件时表里已空 → `NotExpected` |
| 登记在下发**之后** | 写入那一刻表里没有期望 | 同样 `NotExpected` |

终点一样：**框架自己的写入被当成用户输入回调出去** → `setState` → 重渲染
→ 抖动，或覆盖掉输入框里刚敲进去的字。两副面孔在三件套上**一件不少**，
所以第一、四道照样全绿。

### 23.2 判据

对每个登记点，取它之后第一条**同一个 guard** 的撤销，要求两者之间
**存在一条以同一控件为接收者的下发**。

- **接收者必须比对**：区间里一条 `other.Value = …` 同样长得像下发，
  不比对就会让越位蒙混过关（样本里有这一档）；
- **没有撤销的不算次序违约**——交给第一道，两条分工，
  免得同一处漏子被两个扫描器各数一遍、把变异计数搅浑。

**现状：真源码 12 处登记点，0 处越位**（守护型契约）。

### 23.3 变异用"对调"而不是"删行"，是为了能反向对照

「登记 ↔ 下发」「撤销 ↔ 下发」各 12 处，**24 处全部承重**（0 → 1）。

对调之后三件套**一件不少**，只是位置变了——于是可以拿第一道去量同一批变异：
**实测 0 处**。这一条同时证明：

1. 第十三道咬的是次序，不是"少了一件"；
2. 它不是重复防线——第一道对此确实是瞎的。

> 若用删行做变异，第一道会一起红，就分不清这两件事了。
> 这也反过来说明：**"变异手法"本身要按"你想证明什么"来挑**，
> 不是随便把源码弄坏就算有牙。

### 23.4 认不出的边界

认的是**相对位置**，认不出「区间里那条下发到底是不是受控下发」。
那一层由第四道（三件套齐全）与第五道（`EchoProne` 名单）兜着。

## 24. 第十四道契约：读侧——判据的**结果**必须真的用于拦截

### 24.1 写侧排对了，读侧照样可以全废

`Consume` 返回 `true` 表示「这是我们自己写的回声，别回调用户」，
而**没有任何类型或编译器机制强迫你用这个返回值**：

| 写法 | 为什么废 |
| --- | --- |
| `Echo.Consume(control, value);` 当独立语句 | 返回值扔掉，等于没判 |
| `if (Echo.Consume(…)) { Log(); }` 后面照样 `callback(value)` | 判了却不拦 |

判据：① 调用必须在 `if (` 的条件里；② 块体里必须有**停下来**的动作——
`return`，或把**条件里出现过**的标识符改判掉（`verdict = Echo` 那 4 处）。

**现状：12 处判据点，0 处形同虚设。**

### 24.2 判据二的两个坑

- **只看"体内第一行"会假红**：`ToggleSwitchHandler.Guard` 是
  「先 `ReactorLog.Gate("…回声，吞 IsOn=…")` 再 `return`」，
  第一行不是 `return` → 本轮第一次跑正是报了它。改成看**整个块体**。
- **只认"块内有赋值"又会假绿**：块里一句 `var tag = …` 也是赋值，
  算成"停下来"等于没查。所以改判必须改**条件表达式里出现过**的标识符。

变异同理：**只改块体第一行没用**（`ToggleSwitch` 那处改掉日志行、
后面的 `return` 还在）。要改就**整段**换掉。

### 24.3 两条反向对照把分工钉死

变异两个方向各 12 处，**都不删 `Consume` 这个串**（它还在源码里）：
① 搬进 `if` 体内当独立语句；② 块体整段换成一句日志。**24 处全部承重。**

- **(a)** 同一批变异，第四道（三件套齐不齐）**实测 0 处**——
  `Consume` 字符串还在，它照样判绿；
- **(b)** 把 `Consume` **换名**（站点消失），第四道**实测 12 处全报**。

结论：**第四道盯存在性，第十四道盯用法**，互补而不重复。

## 25. 第十五道契约：开了的窗必须关，且关闭要扛异常

### 25.1 「罩没罩住」和「关不关」是两件事

第八、九、十道问的都是「这次写入有没有落在窗里」，**默认窗会关**。
而窗不关的代价是**永久**的：

| 形状 | 不关的后果 |
| --- | --- |
| `Silence` 深度计数器不 `Dispose` | 该控件**之后所有用户输入**一律被判成"我们自己写的" |
| `Rebuilding` 持续标记不关 | 该控件**之后所有选中事件**一律被吞 |

而且**只在前面抛过一次异常时才现形**——所以关闭必须走 `finally` / `Dispose`，
不能走正常路径。判据两种形状：`Silence` 必须包 `using (`；
`Rebuilding.Set(…, true)` 的配对 `Set(…, false)` 必须落在 `finally` 块里。

### 25.2 不是每个 `Set(ctl, true)` 都是窗

扫出来 6 处标记开启，4 处是 `Rebuilding`（窗），另 **2 处语义上单向、
开了本来就不该关**：

- `FocusRequested`（`InputApplier.cs`）—— 一次性幂等标记，关掉反而会在
  下次挂载时**重复请求焦点**；
- `Ready`（`ReadyGate.cs`）—— 就绪位，取消走 `Disarm` 整条摘除，不是置回 `false`。

沿用第十道那条思路：**默认全管，例外登记**，实测集合必须恰好等于登记表。

### 25.3 反向对照：为什么前三道一条都报不出来

`SilenceOpen` 正则认的是 `.Silence(` 这个串，**不认 `using`**。
把 `using` 拆掉之后，写入在第八、九道眼里**仍然被罩着**——
实测它们的判据照旧返回 `true`。

> 这一条很值得记：**"老契约看得见同一段代码"不等于"老契约看得见同一个问题"**。
> 它认的是串，串还在它就绿。要证明新契约不是重复防线，
> 必须拿老契约的**判据函数**直接去量变异后的样本，而不是靠推理。

## 26. 第十六道契约：三件套必须作用在**同一个控件**上

### 26.1 没人看过 `Consume` 括号里的第一个参数

`Consume` 靠「登记值 == 回读值」判回声，**键是控件**。而：

- 第二、四道数的是**表名**（`TextEcho` 有没有人 `Consume`、有没有 `Forget`）；
- 第十三道比对的是**下发语句的接收者**（`control.Value = …` 里那个 `control`）。

**没有任何一道看过 `Consume` 的第一个参数。** 写错它就是静默失效：
键不同 → 查不到 → 一律 `NotExpected` → 框架每次写入都被当成用户输入回调出去。
症状与"没写 `Consume`"一样，但三件套一件不少，第二、四道照样全绿。

**判据**：按类、按表收集三件套的控件参数，并集必须只有一个元素。
**现状：12 组三件套（36 处），0 处串台**（守护型，本轮没修 bug）。

### 26.2 第一版被反向对照查出是重复防线

第一版第十六道判的是「登记过的表必须在 `Unmount` 里被同一张表 `Forget`」——
合成样本五档全绿，看着是个没人管的维度。

接上反向对照后立刻烧起来：抹掉 `Forget`，**第四道也报了**。查下去才发现
**第二道 `LifetimeViolations` 早就按表名判了「漏 `Forget` / 清错表 / 没 `Unmount`」**，
那 12 处还配了真源码逐行变异。第一版是它的**超集**，整块撤掉重写。

> **反向对照不只是"证明自己不是重复防线"的仪式，它真的能抓出你刚写出来的
> 重复契约。** 只写合成样本、不做反向对照的话，这条会一路绿灯混进契约网，
> 占着 9 条测试却什么都拦不住——而且**比没有更糟**，因为它会让人以为这个维度
> 已经有人管了。

### 26.3 变异与反向对照

把三件套的**第一个参数** `control` 换成 `sender`（36 处），表名与三件套**一件没动**：
**36 处全部承重**。反向对照两条**都实测 0 反应**：第二道按表名判、第四道数三件套，
都看不见参数换没换。

分工：**老契约盯"有没有这张表"，第十六道盯"这张表作用在谁身上"。**

### 26.4 认不出的边界

按**类**切分 → 跨类（A 类登记、B 类消费）看不见；只比参数**名字** →
`var c = control;` 之后用 `c` 登记认不出来。

## 27. 第十七道契约：登记的值必须就是**下发**下去的那个值

### 27.1 第十三道管次序，没人管值

第十三道只要求「登记与撤销之间存在一条**以同一控件为接收者**的下发」，
**不看下发的是什么值**。`Expect(control, a); control.Value = b;` 在它眼里合格，
而回读的是 `b`、登记的是 `a` → 永远 `NotExpected` → 每次写入都被当用户输入。
三件套齐全、次序也对，前十六道全绿。

判据：取登记与撤销之间第一条以该控件为接收者的下发，右值必须等于登记值。
**现状：12 处登记，11 处同口径，1 处间接（已登记豁免），0 处真对不上。**

### 27.2 NavigationView 是合法的间接口径

`Handlers.Controls.cs:1584` 登记下标、下发 `SelectedItem`（对象）——
因为 NavigationView **只有对象这一个写入口**，而回读侧 `Guard` 已转回 `int` 下标，
两端同口径。那处注释还写明这一发本就没有回声。

**默认全管，例外登记**，实测集合必须恰好等于登记表（第十、十二、十五道同构）。

### 27.3 又踩了一次"豁免对象不能拿来变异"

第十二道那条老坑本轮重现：NavigationView 那处本来就是"不同"，
换掉右值**仍然不同** → 不承重（假红）。job 收集必须显式跳过豁免对象，
最终 **11 处**而非 12 处。

### 27.4 反向对照

换掉右值后，第十三道的 `WriteBetween` 只看接收者分组、右值没进正则 →
那条下发在它眼里**仍然在窗口里**，**实测 0 反应**。
分工：**第十三道盯"写在哪"，第十七道盯"写的是什么"。**

## 28. 第十八道契约：抑制窗必须罩在**它自己那个控件**上

### 28.1 "有没有窗"和"窗罩在谁身上"是两个维度

`SilencedBefore` 认 `.Silence(` 这个**串**，不看控件参数。
`using (X.Silence(other)) { control.Minimum = 0; }` 在它眼里"被罩住了"，
而抑制是按键查表的——键错了那一发根本没人管，回声照常回调出去。

**现状：5 处静默窗 + 4 处持续标记，0 处罩错人**（守护型）。

### 28.2 被罩的动作有三种形状

只认 `X.Prop =` 时 9 处里只有 7 处进视野，漏的两处是：
集合动作（`control.Items.Clear()`）和把控件当实参交给 helper
（`reconciler.PatchItems(control, …)`、`ApplyRange(control, newElement)`）。
持续标记罩的往往是这两种，不是赋值。

### 28.3 三个正则坑（都是假红）

1. **`(?:…)?` 是贪婪的** —— `ApplyRange(control, newElement)` 抓到的是
   `newElement` 而不是 `control`。改成取整串实参再找标识符。
2. **`Xxx(y)` 会把 `if (modeChanged)` 认成调用** —— 按函数名黑名单排掉关键字；
   字符串字面量也要先抠掉（`Gate("hello")` 里的 `hello`）。
3. **有接收者的动作必须判完就走** —— `control.Items.Add(item)` 的实参是 `item`，
   让实参形状复判同一行就会误报。按"有没有明确接收者"排序，前两种匹配到就 `continue`。

### 28.4 反向对照

换掉抑制的作用对象后，第八、九道**仍然判"被罩着"**（`SilencedBefore` 实测 true）。
分工：**第八、九道盯"写入有没有落在窗里"，第十八道盯"窗罩的是谁"。**

## 29. 第十九道契约：静默窗必须开在**自家**那张表上

`EchoGuard` 的 `_pending` / `_silenced` 是**实例字段**，每个受控属性一份
（`ValueEcho` / `TextEcho` / `SelectionEcho` … 各是一张表）。
`Consume` 判回声时先看 `_silenced[control] > 0` → **只认自己那张表的窗**。
而 `SilenceOpen` 正则只认 `.Silence(` 这个串，表名不在里面 →
`using (TextEcho.Silence(control)) { control.Maximum = 9; }` 在第八、九、十八道眼里
照样"被罩着"，实际那一发回声走 `ValueEcho.Consume`，窗没开 → `NotExpected`。

判据：取窗所在的类，收集它 `Consume` 用的表（没有就退回 `Expect`），
窗开的表必须落在集合里。**现状 5 处全过。**

### 29.1 反向对照

换表名（10 处 = 5 处 × 2 个方向）**全部承重**；
第十八道（只比控件参数）与第八、九道（只认串）**实测 0 反应**。
分工：**第十八道盯"罩在谁身上"，第十九道盯"开在谁家"。**

## 30. 第二十道契约：闸门判完之后必须真的**拦下来**

`if (SelectionGate.Suppress(verdict))` 这一行**没有 `Consume`**，
所以从来没进过第十四道的视野（契约里 `Suppress` 此前提及 0 次）。
判了却不拦 → `NotReady` / `Rebuilding` / `CancelTransient` / `Echo` 四类中间态
**原样回调给用户** → setState → 把用户刚选的值拽回去。

判据：块内必须有一句**直接属于这个块**的 `return`；
里层 `if` 里的不算，单行 `if (x) return;` 也不算（后者是本轮收紧出来的）。

### 30.1 反向对照

两个方向 × 3 处 = 6 处变异**全承重**，第十四道**实测 0 反应**。
**弃用的方向**：把条件换成恒假会让 `SelectionGate.Suppress(` 这个串消失，
站点自己没了、违约数不升反平——换条件 = 拆站点，不能当变异方向。
