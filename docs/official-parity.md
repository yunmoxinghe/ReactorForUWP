# 与官方 Microsoft.UI.Reactor 的对齐状况

> **这份文档是「分层自包含」的——自包含的边界 = 可信度的边界，不是全篇一律。**
>
> - **【原文】级条目**：两侧摘录都抄进来了，**可以只读这一份**，不必再翻源码。
> - **【转述】/【推论】级条目**：**故意不自包含**。它们只给结论和行号，
>   正文里带 🚧 阻断块——读到那儿请**先去官方仓库复核**，别把这份文档当判据。
>
> 为什么不一视同仁地全做成自包含：**自包含会终止怀疑。**
> 索引式逼你跳源码，你还在验证；自包含让人读完就信，怀疑就停了。
> 所以这份文档只把"读完可以不再怀疑"这个待遇给**可独立验证**的内容。
>
> ⚠ 这条规矩不是理论推演，是本轮写这份文档时**实测**出来的：
> 全篇揪出的 4 处不准，**100% 落在【转述】/【推论】/元数据断言上**；
> 【原文】级 6 条**零修订**。给转述穿上自包含的外衣，等于把"我记的/我推的"
> 提升成"文档说的"——那 4 处不准正是这么来的。

---

## 0. 先分清两个「官方」

这是最容易糊掉的一层，也是读这份文档的前提。仓库里实际上有**两个**对标对象：

| | 是什么 | 在仓库里的凭据 |
|---|---|---|
| **官方 Reactor** | `Microsoft.UI.Reactor`，WinUI 3 的声明式 UI 框架。我们要对标的是它的 **API 形状与事件闸门架构** | **没有本地源码副本**（`find` 全仓库无结果）。所有"官方怎么做的"都来自注释中抄录或转述的片段 |
| **官方 WinUI 2 / XAML** | 我们**跑在它上面**的底层控件行为（`RadioButtons.cpp` 等） | 有本地副本：`tools/winui2-ref/`（`dev/` + `dxaml/`）。笔记见 `docs/winui2-source-notes.md`（1758 行） |

**两者的关系是正交的**，不要混着谈：

- "我们的闸门比官方多两道" → 对的是**官方 Reactor**。
- "`ReadyGate` 对齐 `m_blockSelecting`" → 对的是**WinUI 2 平台**，而官方 Reactor 跑在 WinUI 3 上，**根本没有这个烦恼**。

### 0.1 素材可信度分级（务必看）

因为**没有官方源码副本**，下面每条都按素材强度打了标。**这个标本身就是这份文档的一部分**，
别把它当装饰——把转述当原文用，是本仓库栽过跟头的那一类事。

| 标 | 含义 | 怎么用 | 正文里的样子 |
|---|---|---|---|
| **【原文】** | 注释里**逐字抄录**了官方源码片段 | 可直接引为判据；只读本条即可 | 两侧摘录齐全，无阻断 |
| **【转述】** | 只有结论，没有官方原文，来自前人注释 | 结论可用，**改它之前先自己去官方仓库复核一遍** | 🚧 阻断块 |
| **【推论】** | 从上面两类推出来的，官方没直接说过 | 仅供参考，**不能当判据** | 🚧🚧 双阻断块 |
| **【事实·自证】** | 不涉及官方，全仓库内可 grep 验证 | 附验证命令，一键可验 | 代码块给出命令 |

**等级标在标题上，但阻断块在正文里。** 只看标题等级会漏掉警告——
真正读到结论的那一刻必须看见它，所以低可信度条目的正文里都插了 🚧。

---

## A. 已对齐

### A1. 回调出去的值取「控件当前值」，不是事件参数里的下标 【原文】

**官方**（`src/Reactor/Core/Element.cs`，`ComboBoxElement`，逐字抄录在 `SelectionGate.cs:97-102`）：

```csharp
var cb = (WinUI.ComboBox)s!;
if (!Reconciler.TryGetReactorState(cb, out var state)) return;
if (ChangeEchoSuppressor.ShouldSuppressEcho(state, cb.SelectedIndex)) return;
(state.Element as ComboBoxElement)?.OnSelectedIndexChanged?.Invoke(cb.SelectedIndex);
```

注意末行传的是 `cb.SelectedIndex` —— **回读控件**，不是事件参数。`RadioButtons` 走
`ControlledPropEntry.StaticTrampoline`（`PropEntry.cs:277`），形状相同。

**为什么这条要紧**（`SelectionGate.cs:108-112`）：因为回读的是控件真值，
同一手势的两发回调（WinUI 2 的 RadioButtons 一次点击抛两发）会回调**同一个值**，
`setState` 同值不重渲染，**幂等无害**。官方靠这个把"多發事件"这件事消化掉，
不需要额外的去重闸。

**我们**：三个 `Dispatch`（ComboBox / RadioButtons / 其余）里 `value` 取的都是
`control.SelectedIndex`。**已对齐。**

### A2. 属性写入的三档绑定语义 【转述·详尽】

> 🚧 **【转述】——这一条不自包含，别只信它。**
> 三档的**字面语义**来自前人注释（写得详尽，但没抄官方原文），
> "挂载期无条件写 == 官方 `OneWay`"这句同源。
> **要动 A2，先去官方仓库把 `PropEntry.cs` / `ControlDescriptor` 拉下来对一遍。**

**官方**（`src/Reactor/Core/V1Protocol/Descriptor/PropEntry.cs` + `ControlDescriptor`）
不是靠"每个 handler 记得写 if"，而是靠**绑定声明**决定写入行为（摘录自 `PropWriter.cs:15-20`）：

| 档 | 语义 |
|---|---|
| `OneWay` | mount 写；update 时**值变了才写** |
| `OneWayConditional` | 带"该不该写"谓词；`Optional.Unset` → `ClearValue(dp)`，让控件回到原生默认 |
| `Initial` / `InitialOnly` | 只在 mount 写一次，之后值归控件自己（种子值属性） |

**我们**：手写 handler 架构，没有代码生成器，所以把这三档做成 `PropWriter` 的静态方法，
每个 handler 的 `Update` 一律走这里（`PropWriter.cs:22-23`）。

挂载期那条额外规则（`PropWriter.cs:45-47`）也**明确对齐**官方 `OneWay`：
**挂载期一律无条件写**，因为控件刚 new 出来时读到的是依赖属性默认值，默认样式要等
`ApplyTemplate` 才生效；此时若走 diff，"声明值恰好等于默认值"会被判成没变化而跳过，
样式一应用就把声明值无声吞掉。实测踩到过：`HyperlinkButton.Padding(0)` 想盖掉默认样式的
`11,5,11,6`，结果 new 出来的 Padding 本来就是 `0,0,0,0` → 跳过 → 样式值生效，白写。

> 上面两段里要**拆开看**：`HyperlinkButton.Padding(0)` 那段是**实测踩到的**，属【事实】；
> 三档的**字面语义**才是【转述】。别因为实测段可信就顺带信了语义段。

### A3. Core 层 API 形状 【转述·逐项一行】

> 🚧 **【转述】——而且是全篇最危险的一条，因为它看起来最完整。**
> 下面 13 项排得整整齐齐、每行都带行号，很容易被当成"已核实的对齐清单"。
> **实际上它一项官方原文都没有**，全部来自形如"对齐官方 `Microsoft.UI.Reactor.Core.Ref<T>`"
> 的一行式注释——**给出的是"前人认为自己对齐了"，不是"对齐了"**。
>
> 它由 ~13 条独立断言组成，**一条错不影响另一条**，所以别用"抽查了两条是对的"
> 推断整表。要动其中任何一项，去官方仓库单独核那一项。

仓库里这类注释很多，形如"对齐官方 `Microsoft.UI.Reactor.Core.Ref<T>`"。**它们只有一行结论，
没有官方原文对照**，所以整体按【转述】处理——想动其中任何一条，先去官方仓库复核。

| 我们的 | 官方对应 | 出处 |
|---|---|---|
| `Ref<T>` | `Core.Ref<T>` | `Core/Context.cs:7` |
| `Context<T>` | `Context<T>` | `Core/Context.cs:24` |
| `ContextExtensions.Provide` | 同名 | `Core/ContextExtensions.cs:7` |
| `ContextScope`（值栈） | 同名 | `Core/ContextScope.cs:7` |
| `RenderContext.BeginRender(ctx, scope)` | 同名 | `Core/RenderContext.cs:19` |
| `RenderBatcher.MaxRerenderReentrancy = 50` | 同名常量 | `Core/RenderBatcher.cs:41` |
| `EmptyElement` 哨兵 | `EmptyElement` | `Elements/Elements.cs:43` |
| `StackElement`（不是 `StackPanelElement`） | 同名 | `Elements/Elements.cs:33` |
| `BorderElement` / `GridElement` / `GridAttached` | 同名 | `Elements/Elements.New.cs:12-30` |
| `ElementExtensions`（含 `Merge`） | 同名，非 sealed | `Elements/ElementExtensions.cs:15`、`Core/Element.cs:39` |
| `VStack("标题", Button("确定"))` 语法糖 | 同名 | `Core/Element.cs:29` |
| 文本节点参数名 `Content`（不是 `Text`） | 同名 | `Elements/Elements.cs:11` |
| `ScrollViewer` 参数名 `Child` | 同名 | `Elements/Elements.cs:173` |

### A4. 回声抑制的**存在**本身 【原文】

官方 `ExpanderElement` 的注释直接写着 `IsExpanded` 是
**"counter-echo HandCodedControlled over Expanding + Collapsed event"**
（`EchoGuard.cs:12-15`）。即：框架写入前先登记"我期望回读到的值"，控件随后发出的事件
若回读出该值，就判定为自己写的回声——既不回调用户，也不回写控件。

我们的 `EchoGuard` 用法（`EchoGuard.cs:23-29`，逐字）：

```csharp
// 写入前：
if (oldValue != newValue) { Echo.Expect(control, newValue); control.Prop = newValue; }
// 事件里：
if (Echo.Consume(control, control.Prop)) return;   // 是回声，不回调
callback(control.Prop);
```

> ⚠ 注意"**counter-echo**"这个词：官方**自己也是计数器式起步的**。
> 所以下面 C2 那条欠账，不是"我们发明了落后的做法"，而是**官方后来迁走了、我们没跟上**。
> 这个区别很重要，它决定了迁移的风险评估方式。

---

## B. 有意偏离（我们比官方**多**东西）

### B1. 事件闸门：官方 2 道，我们 4 道 【原文】

**官方只有两道**（`SelectionGate.cs:104-105`，紧接 A1 那段原文之后）：

> `RadioButtons` 走 `ControlledPropEntry.StaticTrampoline`（`PropEntry.cs:277`），
> 形状相同：**控件没挂上 → 返回；回声 → 吞；其余一律回调。**
> 没有"取消选中"这道，也没有"未就绪"这道。

**我们有四道**（`SelectionGate.cs:12-37`，枚举 `SelectionVerdict`）：

| 档 | 判据 | 这一发的作者是谁 |
|---|---|---|
| 判据零 `CancelTransient` | 事件参数 `AddedItems` 全为 null | `RadioButtons.cpp:431` 的 `OnChildUnchecked`，不是用户 |
| 判据一 `NotReady` | 控件还没进过可视树 | 对面 `m_blockSelecting` 仍是 true（`RadioButtons.h:91`），全是内部中间态 |
| 判据二 `Rebuilding` | items 正在整批替换 | `RadioButtons.cpp:518` 的 `UpdateItemsSource`，不是用户 |
| 判据三 `Echo` | 值等于我们刚写进去的那个 | 本次受控下发 |

### B2. 为什么多这两道：因为我们多一件事——**排队纠正** 【原文】

这一条是全部偏离的**根**，务必一起读。`SelectionGate.cs:114-119`：

> **我们多出来的两道，对应的是我们比官方多的那一件事：排队纠正。**
> 官方没有"纠正"这个动作，收敛完全靠下一轮渲染
> （`PropEntry.Update`：`current == nv` 就不写，否则 arm 后裸写）。
> 我们有 `ShouldRestoreAfterSuppress`，于是"取消选中"必须被拦住，
> 否则纠正会对着一次真实手势的中间态动手。

以及 `SelectionGate.cs:167-171` 补的代价对比：

> 官方版**没有这一道**：……代价是"点已选中项把控件打到 -1"这类情形
> 只能等下一次 state 变化才拉回，**state 不变就一直停着**；
> 我们不愿接受那个窗口，所以有这一道。

**所以这不是"我们多虑了"，是一个明确的取舍**：我们用"多两道闸 + 排队纠正"
换掉了"中间态可能永久停留"这个窗口。

### B3. 补发（deferred）机制 【推论】

未就绪期间被吞掉、但值不是受控目标的那一发，记下来等进树补发（本轮 alpha.8 的核心，
INV13/14/15，见 `docs/release-notes/alpha.8.md` §3–§4）。

> 🚧🚧 **【推论】——不是判据，别拿它去跟人争，也别拿它当修改依据。**
> 官方既然没有"未就绪"这道闸（B1 有原文），它自然也不需要一个"未就绪期间记下来的补发"。
> 但我没有官方源码直接说"官方没有补发"，**这是从 B1 推出来的**。
>
> 更要紧的是把两件事拆开：**补发机制本身是我们自己的设计**（有 alpha.8 的 19 条 contract
> 守着，是我们**主动选的**，见 B 类性质说明）；**"官方不做这件事"这半句才是推论**。
> 别因为推论那半句不可靠，就顺带否掉设计这半句——也别反过来。

### B4. `ReadyGate`：对齐的是 **WinUI 2 平台**，不是官方 Reactor 【原文·本地有副本】

这是 0 节那张表里最容易认错的一条。它对齐的**不是**官方 Reactor —— 官方 Reactor 跑在
WinUI 3 上，压根没有 `m_blockSelecting` 这回事。

**WinUI 2 源码**（`release/2.8`，本地副本 `tools/winui2-ref/`，摘录自 `ReadyGate.cs:11-17`）：

```
dev/RadioButtons/RadioButtons.h:91       bool m_blockSelecting{ true };
dev/RadioButtons/RadioButtons.cpp:358    if (!m_blockSelecting && !m_currentlySelecting && m_selectedIndex != index)
dev/RadioButtons/RadioButtons.cpp:118-138 OnRepeaterLoaded() 里才 m_blockSelecting = false，
                                          紧跟一次 UpdateSelectedIndex()/UpdateSelectedItem() 做自愈
```

**这段源码说了什么**（`ReadyGate.cs:20-24`）：RadioButtons 内部用 `ItemsRepeater` 渲染每一项。
在 repeater 触发 `Loaded` **之前**，控件拒不接受任何选中——`Select()` 第一行就被挡回来。
写给它的 `SelectedIndex` 会存进依赖属性，但内部选中态此刻还没真的生效。

**托管侧怎么对齐**（`ReadyGate.cs:26-28`）：那个私有 bool 看不见，但有一个等价的可观测量——
**控件自己的 `Loaded`**。控件 Loaded 时它的模板子树（含内部 repeater）已经就位，
WinUI 自己也正是在这一刻解禁。

**为什么这条比"过滤 -1"强**（`ReadyGate.cs:31-39`）：以前的实现是"看见 -1 就丢"，那是在猜。
翻了源码才知道 -1 只是中间态的**一种**，它的来源有三处（cpp:310-317 元素回收 / cpp:516-518
`UpdateItemsSource` 无条件先 `Select(-1)` / cpp:421-435 子项 Unchecked）；
反过来**真实用户操作也可能产生 -1**（清空选择）。按值过滤两头都会错，
**按"控件是否已就绪"过滤才是源码给的那个判据**。

---

## C. 欠账（想对齐但没做）

### C1. 订阅挂在 `Loaded` 上，且**一个 `Unloaded` 都没有** 【事实，仓库内可自证】

> 标题一度写成"挂 `Loaded`/`Unloaded`"，**不准**。实跑 `grep` 的结果是：
> **`Loaded +=` 共 6 处，`Unloaded +=` 共 0 处。** 从来没有成对的退订。

**官方**：完全不用 `Loaded`/`Unloaded` 管理订阅。做法是**订阅一次 + 静态 trampoline**，
状态走 attached DP（`docs/release-notes/alpha.8.md` §9）【转述】。

**我们**：全仓库 6 处订阅，全部只进不退：

| 位置 | 干什么 |
|---|---|
| `Internal/ReadyGate.cs:127` | `control.Loaded += Handler`（就绪闸） |
| `Internal/InputApplier.cs:391` | `framework.Loaded += once` |
| `Internal/Handlers.Overlays.cs:78` | `control.Loaded += loaded` |
| `Internal/Handlers.Overlays.cs:340` | `control.Loaded += loaded` |
| `Internal/Handlers.Views.cs:534` | `control.Loaded += loaded` |
| `Internal/Handlers.Virtual.cs:144` | `scroll.Loaded += (_, _) => Refresh(scroll)` |

**一键复核**（这条是全仓库可自证的，跑一下就知道表有没有过期）：

```bash
cd /d/fluentapps/repos/test/ReactorForUWP
grep -rn "Loaded +="   --include=*.cs Reactor.uwp/ | wc -l   # 当前 6
grep -rn "Unloaded +=" --include=*.cs Reactor.uwp/ | wc -l   # 当前 0
```

> 这张表会漂移。**行号比标识符容易漂，标识符比命令容易漂**——
> 所以复核请跑上面的命令，不要逐个去对行号。

**两件事要分开看**，别混成一条：

1. **UWP/WinUI `Loaded`/`Unloaded` 本身乱序且不配对**（Win2D#954、XamlBehaviors#251）【转述】
   —— 这是平台侧的已知问题。
2. **我们连 `Unloaded` 都没订阅**，所以"不配对"在我们这儿**还没机会发生**；
   真正的现状是**退订路径根本不存在**，清理全靠 `WeakTable` 的弱键
   （如 `ReadyGate.Subs` 的注释所述：`Ready` / `Subs` / `OnReady` 三张表都是弱键）。

**已缓解的部分**（alpha.8 §3.2）：`ReadyGate.IsReady` 现在会**或上控件此刻的 `IsLoaded`**，
"Loaded 事件不来就永久未就绪"这条失效模式已消除。但整套做法仍在。

### C2. `EchoGuard` 仍是计数器式，官方已迁到值比对 【转述】

> 🚧 **【转述】——"官方已迁到值比对"这句没有原文，来自对 spec-047 §8.3 的转述。**
> 我们手上有的是 `ArmExpectedEcho` / `ShouldSuppressEcho` 这两个**函数名**（A1 的官方原文里出现过
> `ShouldSuppressEcho` 的调用，那是【原文】），但"整个机制已从计数器迁到值比对"这个**判断**是转述。
> 要按这条动手改 `EchoGuard`，**先把 spec-047 §8.3 原文找出来**。
> 注意 A4 那条【原文】已经证明官方自己也是 counter-echo 起步的，所以"官方现在的形状"
> 是个会变的目标，转述更容易过期。

**官方**：spec-047 §8.3 已把它迁移到**值比对**臂（`ArmExpectedEcho` / `ShouldSuppressEcho`）。

**我们**：仍是 `Expect`/`Consume` 计数器式（`EchoGuard.cs:44` 起）。
有一层兜底：期望回声有 1 秒有效期（`EchoGuard.cs:54` `WindowMs = 1000`），
超过窗口还没等到的登记值直接作废——宁可多回调一次，也不能丢事件。

**风险面**（`alpha.8.md` §9）：计数器一旦配对错位就会 **strand**，把下一次真实事件判成回声吞掉。
`Consume` 在"不匹配时保留登记"这条有同样的风险面。

> 再次强调 A4 那条：**官方自己也是 counter-echo 起步的**，
> 所以这是"没跟上官方的演进"，不是"我们选错了路线"。

### C3. `ReadyGate` 在「写入侧」和「事件侧」的用法不一致 【事实，仓库内可自证】

> ⚠ **这条一度被写成"NavigationView 不吞未就绪、另两个吞"，方向对但太粗**，
> 而且我第一次找证据找错了文件（去 `Handlers.Shell.cs` 里 grep，那里压根没有
> NavigationView，得到"0 处引用"差点当成证据）。真相在中间，见下。

** NavigationView 的 `Guard`（事件侧）确实只有回声一道**（`Handlers.Controls.cs:1972-1982`，逐字）：

```csharp
private static Action<int>? Guard(MuxControls.NavigationView control, Action<int> callback) =>
    value =>
    {
        if (SelectionEcho.Consume(control, value))
        {
            return;
        }

        callback(value);
    };
```

**但 `ReadyGate` 它并非不用**——用在**写入侧**，决定"这笔受控写入要不要登记回声期望"
（`Handlers.Controls.cs:2014-2017`，逐字）：

```csharp
if (SelectionGate.ShouldExpectEcho(
        index,
        ReadyGate.IsReady(control),
        Rebuilding.TryGetValue(control, out var busy) && busy))
{
    SelectionEcho.Expect(control, index);
}
```

**一键复核**（两个方向都要跑——只跑正向会把"另一个文件里也有"漏掉）：

```bash
cd /d/fluentapps/repos/test/ReactorForUWP
# 正向：NavigationView 的闸门到底在哪
grep -rn "class NavigationViewHandler" --include=*.cs Reactor.uwp/
# 反向：Handlers.Shell.cs 里到底有没有 NavigationView（本轮就是栽在这一步）
grep -c "NavigationView" Reactor.uwp/Internal/Handlers.Shell.cs   # 当前 0
```

> **这条的教训是方法论级的**：第一次取证时只跑了反向 grep，拿到"0 处引用"
> 就当成"NavigationView 不用闸"的证据——**而它只是找错了文件**。
> 得数是 0 的时候，先问"我是不是搜错地方了"，再问"结论是不是成立"。

**所以真实的不一致是这样的**：

| handler | 事件侧（放不放行给用户回调） | 写入侧（要不要登记回声） |
|---|---|---|
| `ComboBoxHandler` / `RadioButtonsHandler` | `SelectionGate` 四道判据齐（含 `NotReady`） | 走 `ShouldExpectEcho`，同一套判据 |
| `NavigationViewHandler` | **只有回声一道**，未就绪事件直接放行 | **走 `ShouldExpectEcho`，用了 `ReadyGate.IsReady` 与 `Rebuilding`** |

**精化后的结论**：不是"一个用闸一个不用闸"，而是
**同一个 `ReadyGate`，在 ComboBox/RadioButtons 上管"事件能不能出去"，在 NavigationView 上只管"回声要不要登记"**。
它还有自己一套 `Rebuilding` **持续标记**（而非时间窗，因为菜单项要等 repeater 加载完才到位，
源码原文见 `NavigationView.cpp` `OnSelectionModelSelectionChanged`：
*"Template has not been applied yet. SelectionModel's selectedIndex state will get properly
updated after the repeater finishes loading."*）。

**都不是本轮病源**，但同一个 `ReadyGate` 在两种控件上承担不同职责，迟早会再出一次
"同一个控件换个写法就好了"。

> 排查提示：想找 NavigationView 的闸门逻辑，去 `Handlers.Controls.cs`（不是
> `Handlers.Shell.cs`）。`Handlers.Shell.cs` 与 NavigationView 无关。

### C4. 面包屑「整条不见」——独立问题，不在本族 【原文·已排除一个方向】

用户报过两次（最早 alpha.6，本轮"撤掉探针"后又报一次）。
**别当成"设置项修好了就顺带好了"**，两者不在同一条链上。

**已排除的方向**：alpha.6 的"每次下发都换全新数据源引用"修法**按源码是对的，不要再动 `ApplyItems`**。
依据 `tools/winui2-ref/dev/Breadcrumb/BreadcrumbBar.cpp`：

- `UpdateItemsRepeaterItemsSource()`（cpp:142）读的是 `ItemsSource()` **依赖属性**（永远最新值），
  只有"把 feeder 交给 repeater"那一步（cpp:151）要求 repeater 已存在；
- `OnApplyTemplate()` 末行（cpp:73）与 repeater 自己的 `Loaded`（cpp:96 → cpp:163）**都会**
  按当前 `ItemsSource()` 重建。

所以"每次 new 一个载体"这个写法确实让时机变得无关，`Handlers.Template.cs:402` 那句
"这里不需要 Loaded 兜底"经源码核对**站得住**。真机上仍不显示，**病因在别处**。

剩下三个候选（都未验证）：① 条目渲染了但容器尺寸为 0；② `ItemTemplate` 没解析出来
（`TitleTextBlockStyle`）；③ 模板压根没应用。

---

## D. 结构天花板

**官方 Reactor 是 WinUI 3，我们是 UWP + WinUI 2。** 这不只是版本号差异，是**双层**：

- `NavigationView` 来自 WinUI 2（`Microsoft.UI.Xaml.Controls`）
- `Frame` 来自系统 XAML（`Windows.UI.Xaml.Controls`）

所以「与官方对齐」永远隔着一层平台差：**到不了 100%**。
评估任何一条差异时，先问一句"这条差异的平台根源是什么"，再决定要不要追。

---

## E. 维护这份文档的规矩

1. **改任何一条之前，先看它的可信度标。**【转述】和【推论】在动手前必须去官方仓库复核，
   别拿前人（或我）的一句话当判据。
2. **补录新条目时，必须同时给"官方怎么做"和"我们怎么做"的两侧摘录。**
   只写一句"与官方不同"的条目等于没写——后人还得去考古，这份文档就白做了。
3. **【原文】级的条目，要连官方源码片段一起抄进来。**
   写进本表的【原文】条目共 **6 条**（已数过，按标题级计）：
   A1、A4、B1、B2、B4、C4。它们最值钱，因为**不依赖记忆**。
   <br>⚠ **但别把这个 6 当成"仓库里【原文】素材的总量"**——它只数了**写进本表的**。
   仓库里还有 `Handlers.Template.cs:375-381`（抄 `BreadcrumbBar.cpp:163-175`）、
   `docs/winui2-source-notes.md` 一千多行笔记等更多【原文】素材没被本表收录。
   **6 是本表的覆盖面，不是仓库的素材量。**
4. **什么时候该删一条**：对齐了就移到 A 类并注明版本；官方改了就更新两侧摘录，别只改结论。
5. **能给"一键复核命令"的，优先给命令，其次才是摘录。**
   摘录会**静默漂移**（源码改了，文档悄悄变旧，没人知道）；命令跑一下就知道过没过期。
   所以【事实·自证】类条目一律附命令（C1、C3 已这么做）。
   行号 < 标识符 < 命令，按稳定性排序，复核时挑最稳的那个。
6. **得数为 0 的时候，先怀疑自己搜错了地方——包括"搜错工具"。**
   本轮 C3 栽在前者：在错的文件里 grep 到"0 处引用"，差点当成结论。
   随后又栽在后者：改完文档用 `bash grep "🚧"` 复核，**返回空**，
   一度以为编辑没生效——实际是 Git Bash 对 emoji 的匹配失效，用 ripgrep 一搜全在。
   **同一个"0 不等于没有"的坑，一轮里踩了两次，只是第二次换成了工具层。**
   参见 C3 那条的一键复核块——它特意给了**正、反两个方向**；
   搜非 ASCII 字符时直接用 ripgrep，别用 `grep`。

---

## F. 一句话总账

| 类 | 条数 | 性质 |
|---|---|---|
| A 已对齐 | 4（Core 形状、写入三档、回调取值、回声抑制的存在） | 稳定 |
| B 有意偏离 | 4（多两道闸、排队纠正、补发、ReadyGate） | **有取舍依据，不要盲目"对齐回去"** |
| C 欠账 | 4（Loaded 订阅、EchoGuard 值比对、策略不一致、面包屑） | 可排期 |
| D 天花板 | 1（WinUI 3 vs UWP+WinUI 2 双层） | 不可消除 |

**B 类是"我们主动选的"，C 类是"我们欠的"。** 这两件事的处理方式完全不同：
动 B 之前要重新论证那个取舍；动 C 只需要排期。混在一起会做出错误的决定。

### F.1 这份文档本身有多可信（横向切片）

上面四类是按**性质**切的，下面按**可信度**再切一刀——两个维度正交，
别用"条数不少"推出"内容可靠"：

| 可信度 | 条数 | 条目 |
|---|---|---|
| 【原文】 | 6 | A1、A4、B1、B2、B4、C4 |
| 【事实·自证】 | 2 | C1、C3（附复核命令） |
| 【转述】 | 3 | A2、A3、C2 |
| 【推论】 | 1 | B3 |

**可直接当判据的只有前两类（8/13）**，后 4 条读的时候要带着 🚧。
本轮实测：揪出的 4 处不准全在后 4 条所在的区域，前 8 条零修订。
