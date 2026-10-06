# Reactor.Uwp 0.1.0-alpha.7

> 一句话：**修掉「设置项点了弹回原样」——alpha.6 引的那笔纠正，会盖掉用户刚选的新值。**
> 修法不是把 alpha.6 的纠正撤掉（撤了会退回 alpha.5 那个「点已勾选项后没有任何一项选中」的洞），
> 而是在它身上加两道防线：**在途纠正可被作废**、**兑现前复查控件**。
> 真机上验过了：Template 内置自检在真实控件上跑完整手势，5/5 PASS。

## 1. 纠正盖掉用户新选的值

| | |
|---|---|
| **症状** | 点设置项，界面**弹回原样**；或者「先切一次别的设置，这一项才生效」 |
| **机制** | 一次真实点击在 `SelectedIndex` / `SelectedItem` 上是**两发**手势：第一发 `-1`（取消选中）、第二发才是新值。第一发被判 `CancelTransient` 吞掉，并按 alpha.6 的修法**排了一次异步纠正**（写回受控原值）；第二发若也被别的闸拦下（控件尚未 `Loaded`、或还在回声窗内），用户的新值就**进不了 state**，而那笔在途纠正仍在下一轮兑现 → 把控件写回**原值** |
| **真机证据** | `受控下发 ComboBox#5: 1 → 0`：控件当时在 `1`（用户刚选的），写进去的是 `0`（受控原值）。这正是「点了没反应」的那一刻 |
| **修法** | 两道防线，见下 |

### 防线一：真实选中放行时，把在途的纠正作废

`SelectionRestore.Cancel(control, pending)` —— 放行真实选中的那一刻把 `pending` 标记关掉。
纠正兑现时先看标记：关着就说明这期间来过一次真实选中，那一笔已经把用户的选择交给了 state，**这次纠正已经过时**，直接 return。

### 防线二：兑现前复查控件此刻停在哪

```csharp
var current = readIndex(control);
if (current >= 0 && current != expected)
{ ReactorLog.Gate($"纠正收手：控件停在 {current}（受控目标是 {expected}）"); return; }
apply(control, expected);
```

控件此刻若停在一个**有主人**的值上（非负、且不等于受控目标），那就是用户**刚选的**值——
覆盖它正是「点了弹回原样」。第一道防线若因第二发被吞而没触发（`Cancel` 不会被调用），只剩这道能挡住它。

### 为什么不全盘照抄官方 microsoft-ui-reactor

官方的事件入口只有**两道守卫**：`TryGetReactorState` + `ChangeEchoSuppressor.ShouldSuppressEcho`，
回调值取 `readBack(control)`；**没有**「取消选中」判据、没有未就绪闸、也没有纠正动作，
收敛完全靠下一轮渲染的 `PropEntry.Update`。

照抄之后 **27 项测试红**：点**已选中**的项会把 `-1` 回调出去，主题 / 材质被写成 `-1`
（那是 alpha.5 的洞，alpha.6 第 1 节专门修的）。所以官方那套语义在这里不成立——纠正必须留，
只在它身上加防线。

### 契约：`pending` 表由各 handler 自建

`pending` 是「按控件建的表」，而按控件建的表有一条硬契约：**必须在 `Unmount` 里摘掉**
（否则控件走了、条目还在）。`SelectionRestore` 不是 handler，没有 `Unmount` 这个时机，
所以表放在各 handler 里，摘除自然落在它们各自的 `Unmount` 里——这张表也就进了那条契约的视野。
同一条契约在测试里有专门的扫描项（501 项里的那几条「定位到…（扫描 66 个文件）」）。

## 2. 测试仿真里一个掩盖 bug 的顺序错

`ControlledSelectionSim.Drain` 原先是**先渲染、后纠正**，与真机顺序**相反**——
真机是纠正排在派发器上、晚于当轮手势。顺序一反，用户的新值总能在纠正之前落到 state，
这个 bug 就被永久掩盖了。改成**先纠正、后渲染**。

新增 **INV11「两段手势」**（`Click(2, cancelFirst: true)` 模拟先 `-1` 再新值）：

- 断言 state 与控件都是 `2`、回调**恰好一次**且不含 `-1`
- 三条**反向对照**（不修就坏）：
  1. 两道防线全关 → 必须复现回写
  2. 只留作废、拆掉复查 → 必须挡住（第二发被吞时只剩复查能挡）
  3. 只留复查、拆掉作废 → 必须挡住

反向对照比的是**回写次数**（`RestoreWriteBackCount`）与是否出现 `restore →` 日志，
不是最终值——最终值在这几种情况下恰好都一样，看不出差别。

## 3. 模板加了真机自检

`samples/Reactor.Template/Services/Probe.cs`：**只有** `LocalState\probe-page.txt` 写着 `settings` 才跑，
启动 3 秒后在**真实控件**上跑手势，报告落 `selftest.log`，跑完把四项设置还原。平时完全不参与。

跑什么：

| 项 | 看什么 |
|---|---|
| 两段手势 | 先 `-1` 再新值，记每一步的回调次数与控件值；再等 1.5s 看**值会不会被纠正改回**——这是判据 |
| 勾**单个 RadioButton** | 真人点的是它，不是设 `SelectedIndex`。两条路径都测了 |
| 体检 | 控件尺寸 / 位置 / 是否在视口内、`ScrollViewer` 可滚范围（看得见但滚不动 = 点不到下面几项） |
| 主题落地 | `RequestedTheme` 下发 → `ActualTheme` 是否**真的变了**（值改了 ≠ 界面变了，「点了不生效」的另一种面孔） |

两个实现上的坑：

- 自检**全程走派发器排队**，不用 `await`。第一版用了 `await Task.Delay`，醒来 `Window.Current.Content`
  已经是 `null`，整份报告只写出一句「可视树还没挂上」
- 体检里的 `VisualTreeHelper.FindElementsInHostCoordinates` 在 **AOT 下抛 `NotSupportedException`**
  （给 `ICollection<UIElement>` 取 helper 类型失败），改成几何检查（中心点 vs 窗口矩形），不再把自己搞崩

## 验证

- 契约测试 **501/501**（新增 6 项 INV11）
- 真机自检 **5/5 PASS**：ComboBox（导航栏位置）、RadioButtons（应用主题）两条路径、
  RadioButtons（背景材质）两条路径、主题落地
- 五份编译 **0 错 0 警**：Template x64 / arm64、Gallery x64 / arm64、UwpApp x64
- 包 = 源码：nuget 缓存里的 `Reactor.uwp.dll` 与 `bin/Release` 产物逐字节一致，二进制里能搜到 `纠正收手`

## 发布后要改的三处

漏改任何一处都不会报错，只是新人照着 README 抄到旧版本：

- `samples/Reactor.Template/Reactor.Template.csproj:59`
- `Reactor.uwp/README.md:68`（安装片段）
- `Reactor.uwp/README.md:145`（版本自述）

## 已知未处理

- `ReactorLog.Persist` 只 Append、没有滚动策略
- `Reactor.uwp/Elements/Native.cs` 仍写着「Factory 引用必须稳定」，与 `Handlers.Native.cs`
  只比 `Token` 的实现不符（本次没动）
- `Reactor.uwp/bin/x64/Release/...` 是一份陈旧产物（不含本次修复），不影响包（包用 `bin/Release` 那份），
  但 Gallery 走 `ProjectReference`，建议清掉 stale 输出
