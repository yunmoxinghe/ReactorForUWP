using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using MuxControls = Microsoft.UI.Xaml.Controls;
using Reactor.Uwp.Hosting;

namespace Reactor.Uwp.Internal;

/// <summary>
/// 受控 <c>SelectedIndex</c> 的目标值。弱表的值必须是引用类型，<c>int</c> 装不进去，
/// 所以套一层盒子。
/// </summary>
internal sealed class SelectedTarget
{
    public SelectedTarget(int index) => Index = index;

    public int Index { get; }
}

/// <summary>
/// <c>SelectionChangedEventArgs</c> 的判读：<b>这一发是"选中了东西"，还是"只是取消了选中"</b>。
/// </summary>
/// <remarks>
/// <b>这是整个受控选择链里最硬的一条判据，因为它来自事件参数而不是值。</b>
/// <para>
/// 出处（<c>tools/winui2-ref/dev/RadioButtons/</c>）：
/// <list type="bullet">
///   <item><c>RadioButtons.cpp:378</c>　事件就是这么抛的：
///         <c>SelectionChangedEventArgs({ previousSelectedItem }, { newSelectedItem })</c>
///         ——<b>参数里有 AddedItems / RemovedItems</b>。</item>
///   <item><c>RadioButtons.cpp:407-431</c>　用户点一下会同时走<b>两条独立路径</b>：
///         新项 Checked → <c>Select(N)</c>；旧项 Unchecked → <c>Select(-1)</c>。</item>
///   <item><c>RadioButtons.cpp:382-401</c> <c>GetDataAtIndex(-1, …)</c> 显式
///         <c>return nullptr</c> → 取消那发的 <c>AddedItems</c> 是
///         <b><c>{ null }</c>（Count 为 1，元素是 null）</b>。</item>
/// </list>
/// </para>
/// <para>
/// <b>为什么非它不可。</b>两条路径都被 <c>m_currentlySelecting</c> 保护（互斥），但
/// <b>谁先谁后没有保证</b>——实测日志里 <c>-1</c> 有时在真值前 3ms、有时在真值后 4ms。
/// 于是：<c>SelectedIndex</c> 的值、控件的就绪状态、到达顺序<b>全都无法区分</b>这两发，
/// 只有 <c>AddedItems</c> 能。按值过滤（丢 -1）会误伤真实的"清空选择"；
/// 取"最后一发"则有一半概率拿到 -1，把 state 打成非法值——表现就是
/// <b>"点了之后回调不再更新"</b>（state 停在 -1，界面上没有任何一项被选中）。
/// </para>
/// </remarks>
internal static class SelectionArgs
{
    /// <summary>
    /// 这一发是否真的选中了一个东西。<c>false</c> = 只是取消了选中，不应回调给用户。
    /// </summary>
    /// <remarks>
    /// 判据是"<b>有没有非 null 的项</b>"而不是"<c>Count &gt; 0</c>"：
    /// 取消那一发的 <c>AddedItems</c> 是 <c>{ null }</c>，<c>Count</c> 是 1，
    /// 只看数量会把最需要拦掉的那一发放过去。
    /// </remarks>
    public static bool SelectedSomething(SelectionChangedEventArgs args)
    {
        var added = args.AddedItems;

        for (var i = 0; i < added.Count; i++)
        {
            if (added[i] is not null)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 「取消选中」被闸门吞掉之后，<b>把受控值纠正回控件</b>。
/// </summary>
/// <remarks>
/// <b>它补的是闸门留下的那个洞。</b>闸门判对了（那一发确实不是用户意图，必须吞），
/// 但吞掉只保住了 <c>state</c>——<b>控件自己已经被拨走了</b>。出事的那一步是：
/// 两个触发点，都抄自 <c>tools/winui2-ref/dev/RadioButtons/RadioButtons.cpp</c>：
/// <list type="number">
///   <item><b>点当前已选中项。</b>点已勾选的那一项时 <c>IsChecked</c> 只有
///         <c>true → false</c>，<b>只发 Unchecked、不发 Checked</b>——
///         <c>OnChildChecked</c>（cpp:407）压根不在场。
///         <c>OnChildUnchecked</c>（cpp:421-435）守的是"被取消的正是当前选中项"，
///         此刻成立 → <c>Select(-1)</c>（cpp:431）。而 <c>Select(-1)</c> 里的
///         <c>GetDataAtIndex(-1, true)</c>（cpp:382-405）取不到元素，
///         <b>没有任何一项被勾回来</b>，依赖属性照样被写成 <c>-1</c>（cpp:376-377）。
///         <b>即 WinUI 2 里点已选中项确实会清空选中</b>——这是它的行为，不是我们的 bug。</item>
///   <item><b>选中项被虚拟化回收。</b><c>OnRepeaterElementClearing</c>（cpp:304-315）
///         见被回收的元素是勾选中的，直接 <c>Select(-1)</c>，后面没有任何补救。
///         滚动、折叠、切页都走这条路。</item>
/// </list>
/// 两种情况下事件都以 <c>AddedItems = { null }</c> 抛出，闸门按判据零正确吞掉 →
/// <b>回调不来，state 还是旧值，但控件停在"无选中"</b>。
/// 结果就是用户看到的"点了没反应"：state 有值、界面无选中，两边对不上，
/// 而且<b>再点同一项也不会有变化</b>（内部已是 -1，点它反而会重新选中并放行，
/// 于是表现为"点两下才有一次反应"）。
/// <para>
/// <b>为什么必须延迟而不是当场写。</b>这一发事件是在 WinUI 的
/// <c>m_currentlySelecting</c> 期间派发的（<c>Select</c> 用 <c>gsl::finally</c>
/// 把它撑到返回为止，cpp:361-365）。此刻写 <c>SelectedIndex</c> 会被
/// <c>Select</c> 的第一行守卫挡掉——<b>写了也白写</b>。所以排到派发器上下一轮。
/// </para>
/// <para>
/// <b>为什么只对"取消选中"这一道纠正。</b>其余三道不能碰：
/// <list type="bullet">
///   <item>未就绪 / 重建中：控件此刻的值就是它该有的值，纠正等于把用户操作抹掉；</item>
///   <item>回声：值本来就对，纠正会自激成死循环。</item>
/// </list>
/// </para>
/// <para>
/// <b>不会自激。</b>纠正写回触发的那发事件 <c>AddedItems</c> 里有实项 → 判据零放行 →
/// 判据三（回声）命中 → 被吞，且它的判定是 <see cref="SelectionVerdict.Echo"/>，
/// 不再触发纠正。一轮即止。
/// <para>
/// 唯一例外是<b>受控值越界</b>：越界下标取不到数据，事件照样以"无实项"抛回来 →
/// 判据零命中 → 再纠正 → 再抛。所以 <see cref="Schedule"/> 里先用条目数挡一道——
/// 越界时控件根本无法兑现这个值，纠正没有意义。回归用例 INV8。
/// </para>
/// </remarks>
internal static class SelectionRestore
{
    /// <summary>
    /// 排到派发器上下一轮，把受控目标值写回控件。
    /// </summary>
    /// <param name="control">控件。</param>
    /// <param name="targets">该 handler 的受控目标表。</param>
    /// <param name="itemCount">控件当前的条目数（越界判断要用）。</param>
    /// <param name="apply">写回动作（各 handler 自己的 <c>ApplySelectedIndex</c>）。</param>
    public static void Schedule<TControl>(
        TControl control,
        WeakTable<TControl, SelectedTarget> targets,
        Func<TControl, int> itemCount,
        Action<TControl, int> apply)
        where TControl : DependencyObject
    {
        // 没有受控目标 = 这个控件不受控，用户怎么拨都是他自己的事，不纠正。
        if (!targets.TryGetValue(control, out var target) || target is null || target.Index < 0)
        {
            return;
        }

        var expected = target.Index;

        // 越界值不纠正。<c>GetDataAtIndex</c> 取不到数据（<c>cpp:401</c> 对 -1 显式
        // <c>return nullptr</c>，越界同理），写进去也不会真的选中，事件却照样以
        // "AddedItems 无实项"抛回来 → 判据零命中 → 再纠正 → 再抛。
        // 越界时控件无法兑现这个值，纠正没有意义，且是唯一可能自激的情形。
        if (expected >= itemCount(control))
        {
            return;
        }

        var dispatcher = control.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            // 回写是异步的：这中间可能已经 Unmount（目标被清掉），
            // 也可能受控值已经被改过。两种情况都不要再动手。
            if (!targets.TryGetValue(control, out var now) || now is null || now.Index != expected)
            {
                return;
            }

            apply(control, expected);
        });
    }
}

/// <summary>下拉框。</summary>
/// <remarks>
/// <b>受控时序与 <see cref="RadioButtonsHandler"/> 同源，但依据不同：</b>RadioButtons 那份
/// 有 WinUI 源码行号可引，ComboBox 属于 XAML 核心的 <c>Selector</c>（未开源）。能确定的是
/// 它同样具备那三个特征——① items 在下拉首次打开前不 realize；② 重建 items 会重置选中；
/// ③ 受控写回会触发 <c>SelectionChanged</c> 回声。所以这里套同一套闸门，
/// 不是把它的行为假定成 RadioButtons，而是<b>这套闸门对任何 Selector 都成立</b>：
/// 未进树时的事件没人能定义它的语义，重建期间的选中变化有明确因果作者。
/// </remarks>
internal sealed class ComboBoxHandler : ElementHandler<ComboBoxElement, ComboBox>
{
    private static readonly WeakTable<ComboBox, Action<int>?> Callbacks = new();

    /// <summary><c>SelectedIndex</c> 受控：写回会触发 SelectionChanged，需要回声抑制。</summary>
    private static readonly EchoGuard SelectionEcho = new();

    /// <summary>最近一次下发的受控值：Loaded 那一刻要重放。</summary>
    private static readonly WeakTable<ComboBox, SelectedTarget> Targets = new();

    /// <summary>正在整批重建 items：期间的选中事件是 Selector 的副作用，不是用户操作。</summary>
    private static readonly WeakTable<ComboBox, bool> Rebuilding = new();

    protected override ComboBox Mount(Reconciler reconciler, ComboBoxElement element)
    {
        var combo = new ComboBox
        {
            Header = element.Header,
            PlaceholderText = element.PlaceholderText ?? string.Empty,
        };

        foreach (var item in element.Items ?? Array.Empty<string>())
        {
            combo.Items.Add(item);
        }

        // 先挂带闸的回调，再写 SelectedIndex——这次受控写回不能冒出去当用户输入。
        Rebind(combo, element.OnSelectedIndexChanged);
        ApplySelectedIndex(combo, element.SelectedIndex);

        ReadyGate.Arm(combo, ctl =>
        {
            if (Targets.TryGetValue(ctl, out var target))
            {
                ApplySelectedIndex(ctl, target.Index);
            }
        });

        return combo;
    }

    protected override void Update(
        Reconciler reconciler,
        ComboBoxElement oldElement,
        ComboBoxElement newElement,
        ComboBox control)
    {
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);
        PropWriter.Set(
            oldElement.PlaceholderText,
            newElement.PlaceholderText,
            value => control.PlaceholderText = value ?? string.Empty);

        if (!PropWriter.SequenceEqual(oldElement.Items, newElement.Items))
        {
            ReplaceItems(control, newElement.Items);
        }

        ApplySelectedIndex(control, newElement.SelectedIndex);

        Rebind(control, newElement.OnSelectedIndexChanged);
    }

    protected override void Unmount(Reconciler reconciler, ComboBox control)
    {
        ReadyGate.Disarm(control);
        SelectionEcho.Forget(control);
        Callbacks.Remove(control);
        Targets.Remove(control);
        Rebuilding.Remove(control);
    }

    /// <summary>
    /// 整批替换 items。旧实现在这里先记旧值、加完再单独写回一次，两步之间是
    /// Selector 自己重置选中的窗口；改成把整段关进标记位里，写回也落在门内：
    /// 反倒不用关心中间那一发什么时候来。
    /// </summary>
    private static void ReplaceItems(ComboBox control, IReadOnlyList<string>? items)
    {
        var previous = Targets.TryGetValue(control, out var target)
            ? target.Index
            : control.SelectedIndex;

        Rebuilding.Set(control, true);
        try
        {
            control.Items.Clear();
            foreach (var item in items ?? Array.Empty<string>())
            {
                control.Items.Add(item);
            }

            // Clear 会把选中冲掉，加完之后按<b>受控目标值</b>补回去；
            // 用户没给受控值才退回"保持原来的下标"。
            ApplySelectedIndex(control, previous);
        }
        finally
        {
            Rebuilding.Set(control, false);
        }
    }

    /// <summary>受控值落盘：记下目标值（Loaded 兜底要用），值没变就别碰控件。</summary>
    private static void ApplySelectedIndex(ComboBox control, Optional<int> index)
    {
        if (!index.HasValue)
        {
            Targets.Remove(control);
            return;
        }

        ApplySelectedIndex(control, index.Value);
    }

    private static void ApplySelectedIndex(ComboBox control, int index)
    {
        Targets.Set(control, new SelectedTarget(index));

        // 写回策略不看单个控件，看的是它们共同的基类 <c>Selector</c>
        // （见 SelectionPolicy 那段注释）：值已经对了不必写，越界了不许写。
        if (!SelectionPolicy.ShouldApply(control.Items.Count, control.SelectedIndex, index))
        {
            if (SelectionPolicy.IsOutOfRange(control.Items.Count, index))
            {
                ReactorLog.Gate(
                    $"ComboBox{CtlId.Tag(control)} 受控值 {index} 越界" +
                    $"（Items.Count={control.Items.Count}），不下发");
            }

            return;
        }

        // 同 RadioButtons：稳态下不该出现，出现即表示控件在两轮之间漂了。
        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 ComboBox{CtlId.Tag(control)}: {control.SelectedIndex} → {index}");

        // 只在"这一发回声真会走到回声那道闸"时才登记。
        // 否则这条期望会永远挂着，等用户点回同一个值时把真实操作判成回声吞掉。
        // 判据见 SelectionGate.ShouldExpectEcho / INV7。
        if (SelectionGate.ShouldExpectEcho(
                index,
                ReadyGate.IsReady(control),
                Rebuilding.TryGetValue(control, out var busy) && busy))
        {
            SelectionEcho.Expect(control, index);
        }

        control.SelectedIndex = index;

        // 写入完了再回头看一眼：这一发的回声到现在还没人来领就撤销登记。
        // 触发的情形比其余 handler 少——ComboBox 通常当场就被 Consume 掉——
        // 但"写了却没抛"是 <c>Selector</c> 允许发生的事（例如内部选中其实已经一样了，
        // <c>EndChange</c> 的 <c>didChange</c> 为假就一发都不抛）。
        // 留着一条没人领的登记，等用户之后选到同一个下标，会被判成框架自己的回声吞掉。
        if (SelectionEcho.CancelIfUnconsumed(control))
        {
            ReactorLog.Gate(
                $"ComboBox{CtlId.Tag(control)} 本次下发无回声 → 撤销登记");
        }
    }

    /// <summary>
    /// 事件 → 用户回调的四道闸门，与 <see cref="RadioButtonsHandler.Dispatch"/> 同构。
    /// </summary>
    /// <remarks>
    /// <b>判据零（事件参数）对 ComboBox 同样成立。</b><c>ComboBox</c> 是 XAML 核心的
    /// <c>Selector</c>（未开源，拿不到行号），但它抛的同样是
    /// <c>SelectionChangedEventArgs</c>：<c>Items.Clear()</c> 那一发的
    /// <c>AddedItems</c> 是空的。判据不依赖"为什么会抛"，只依赖"参数里有没有真东西"，
    /// 所以可以共用。
    /// </remarks>
    private static void Dispatch(ComboBox control, SelectionChangedEventArgs args, Action<int>? callback)
    {
        var value = control.SelectedIndex;
        var tag = CtlId.Tag(control);

        // 前三道交给纯判据（可单测，见 SelectionGate）。回声那道单独判：
        // Consume 有副作用，必须确保前三道都没命中时才调用，否则会白吃登记。
        var verdict = SelectionGate.Decide(
            SelectionArgs.SelectedSomething(args),
            ReadyGate.IsReady(control),
            Rebuilding.TryGetValue(control, out var busy) && busy);

        if (verdict == SelectionVerdict.Pass && SelectionEcho.Consume(control, value))
        {
            verdict = SelectionVerdict.Echo;
        }

        if (SelectionGate.Suppress(verdict))
        {
            ReadyStats.Suppressed++;
            ReactorLog.Gate($"ComboBox{tag} {SelectionGate.Reason(verdict)}，吞 {value}");

            // 受控纠正不属于"有人监听才做的事"——见 ShouldRestoreAfterSuppress
            // 那段注释：这一行放在这里，而不是在门口用 callback 短路掉整道闸。
            if (SelectionGate.ShouldRestoreAfterSuppress(verdict))
            {
                ReactorLog.Gate($"ComboBox{tag} 纠正回受控值（控件停在 {value}）");
                SelectionRestore.Schedule(control, Targets, c => c.Items.Count, ApplySelectedIndex);
            }

            return;
        }

        ReactorLog.Pass($"ComboBox{tag} → 用户回调 SelectedIndex={value}");
        callback?.Invoke(value);
    }


    private static void Rebind(ComboBox control, Action<int>? callback)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = null;
            control.SelectionChanged += (s, args) =>
            {
                var cb = (ComboBox)s;
                if (Callbacks.TryGetValue(cb, out var current))
                {
                    Dispatch(cb, args, current);
                }
            };
        }

        Callbacks[control] = callback;
    }
}

/// <summary>开关（UWP 的 Toggled 事件是唯一的变更通知，没有 IsOnChanged）。</summary>
internal sealed class ToggleSwitchHandler : ElementHandler<ToggleSwitchElement, ToggleSwitch>
{
    private static readonly WeakTable<ToggleSwitch, Action<bool>?> Callbacks = new();

    /// <summary><c>IsOn</c> 受控：写回会触发 Toggled，需要回声抑制。</summary>
    private static readonly EchoGuard ToggleEcho = new();

    protected override ToggleSwitch Mount(Reconciler reconciler, ToggleSwitchElement element)
    {
        var toggle = new ToggleSwitch
        {
            Header = element.Header,
            OnContent = element.OnContent,
            OffContent = element.OffContent,
        };

        if (element.IsOn.HasValue)
        {
            toggle.IsOn = element.IsOn.Value;
        }

        Rebind(toggle, element.OnIsOnChanged);
        return toggle;
    }

    protected override void Update(
        Reconciler reconciler,
        ToggleSwitchElement oldElement,
        ToggleSwitchElement newElement,
        ToggleSwitch control)
    {
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);
        PropWriter.Set(oldElement.OnContent, newElement.OnContent, value => control.OnContent = value);
        PropWriter.Set(oldElement.OffContent, newElement.OffContent, value => control.OffContent = value);

        if (newElement.IsOn.HasValue && control.IsOn != newElement.IsOn.Value)
        {
            ReactorLog.Info(
                ReactorLogChannel.Patch,
                $"受控下发 ToggleSwitch{CtlId.Tag(control)}: {control.IsOn} → {newElement.IsOn.Value}");

            ToggleEcho.Expect(control, newElement.IsOn.Value);
            control.IsOn = newElement.IsOn.Value;

            // 写入完了再看一眼：如果这一发回声到现在还没人来领，就把它撤销。
            // 典型成因是 <c>Rebind</c> 排在写入之后、而这一轮
            // <c>OnIsOnChanged</c> 为空——事件处理器里 <c>current?.Invoke(...)</c>
            // 直接空转，<c>Consume</c> 连一次都不会被调用。
            // 留着这条登记，等用户之后拨到同一个值，<c>Consume</c> 会把它认成
            // 框架自己的回声吞掉——表现就是"点了没反应"（INV-T6 有反向对照）。
            if (ToggleEcho.CancelIfUnconsumed(control))
            {
                ReactorLog.Gate(
                    $"ToggleSwitch{CtlId.Tag(control)} 本次下发无回声（回调为空）→ 撤销登记");
            }
        }

        Rebind(control, Guard(control, newElement.OnIsOnChanged));
    }

    protected override void Unmount(Reconciler reconciler, ToggleSwitch control)
    {
        ToggleEcho.Forget(control);
        Callbacks.Remove(control);
    }

    /// <summary>
    /// <c>Toggled</c> 是开关唯一的变更通知（没有 <c>IsOnChanged</c>），
    /// 所以回声抑制是它唯一的闸门；同样要把决策记下来——否则"点了之后到底
    /// 有没有进回调"这一段是黑的（ToggleSwitch 是四个受控控件里唯一没埋过的）。
    /// </summary>
    private static Action<bool>? Guard(ToggleSwitch control, Action<bool>? callback) =>
        callback is null
            ? null
            : value =>
            {
                var tag = CtlId.Tag(control);

                if (ToggleEcho.Consume(control, value))
                {
                    ReactorLog.Gate($"ToggleSwitch{tag} 回声，吞 IsOn={value}");
                    return;
                }

                ReactorLog.Pass($"ToggleSwitch{tag} → 用户回调 IsOn={value}");
                callback(value);
            };

    private static void Rebind(ToggleSwitch control, Action<bool>? callback)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = null;
            control.Toggled += (s, _) =>
            {
                var toggle = (ToggleSwitch)s;
                if (Callbacks.TryGetValue(toggle, out var current))
                {
                    current?.Invoke(toggle.IsOn);
                }
            };
        }

        Callbacks[control] = callback;
    }
}

/// <summary>单选按钮：Checked / Unchecked 两个事件合成为 bool 回调。</summary>
internal sealed class RadioButtonHandler : ElementHandler<RadioButtonElement, RadioButton>
{
    private static readonly WeakTable<RadioButton, Action<bool>?> Callbacks = new();

    /// <summary><c>IsChecked</c> 受控：写回会触发 Checked/Unchecked，需要回声抑制。</summary>
    private static readonly EchoGuard CheckEcho = new();

    protected override RadioButton Mount(Reconciler reconciler, RadioButtonElement element)
    {
        var radio = new RadioButton { Content = element.Label };

        if (element.GroupName is { } group)
        {
            radio.GroupName = group;
        }

        if (element.IsChecked.HasValue)
        {
            radio.IsChecked = element.IsChecked.Value;
        }

        Rebind(radio, element.OnIsCheckedChanged);
        return radio;
    }

    protected override void Update(
        Reconciler reconciler,
        RadioButtonElement oldElement,
        RadioButtonElement newElement,
        RadioButton control)
    {
        PropWriter.Set(oldElement.Label, newElement.Label, value => control.Content = value);

        if (newElement.GroupName is { } group && oldElement.GroupName != group)
        {
            // 改组名 = 让控件重新去组里做一次互斥，组内已有选中项时自己可能被取消
            // （Checked / Unchecked 就是这么来的）。而本 handler 的订阅是**常驻**的
            // （Rebind:588-598 只换回调、从不退订），所以这一刻一定有人接那一发。
            // 具体会不会取消、取消几次没有源码可查（Windows.UI.Xaml 的 RadioButton
            // 不开源），按第 14 节那条代价不对称处理：开窗，不猜。
            using (CheckEcho.Silence(control))
            {
                control.GroupName = group;
            }
        }

        if (newElement.IsChecked.HasValue && control.IsChecked != newElement.IsChecked.Value)
        {
            CheckEcho.Expect(control, newElement.IsChecked.Value);
            control.IsChecked = newElement.IsChecked.Value;

            // 撤销没人领的登记。这个控件的订阅是**常驻**的（Rebind:588-598 只换回调，
            // 不退订），但回调为空时 Invoke 走的是 current?.Invoke 空转 —— Guard 都没包，
            // Consume 一样一次都不会被调用，于是这条路同样会漏出陈旧期望。
            CheckEcho.CancelIfUnconsumed(control);
        }

        Rebind(control, Guard(control, newElement.OnIsCheckedChanged));
    }

    protected override void Unmount(Reconciler reconciler, RadioButton control)
    {
        CheckEcho.Forget(control);
        Callbacks.Remove(control);
    }

    private static Action<bool>? Guard(RadioButton control, Action<bool>? callback) =>
        callback is null
            ? null
            : value =>
            {
                if (CheckEcho.Consume(control, value))
                {
                    return;
                }

                callback(value);
            };

    private static void Rebind(RadioButton control, Action<bool>? callback)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = null;
            control.Checked += (s, _) => Invoke((RadioButton)s, true);
            control.Unchecked += (s, _) => Invoke((RadioButton)s, false);
        }

        Callbacks[control] = callback;
    }

    private static void Invoke(RadioButton control, bool value)
    {
        if (Callbacks.TryGetValue(control, out var current))
        {
            current?.Invoke(value);
        }
    }
}

/// <summary>WinUI 2 的 RadioButtons 分组控件。</summary>
/// <remarks>
/// <b>受控 <c>SelectedIndex</c> 的时序，官方源码是写死的（WinUI 2 <c>release/2.8</c>，
/// 本地副本见 <c>tools/winui2-ref/dev/RadioButtons/</c>）：</b>
/// <list type="bullet">
///   <item><c>RadioButtons.h:91</c>　<c>bool m_blockSelecting{ true }</c>——
///         内部 repeater 没 Loaded 之前，<c>Select()</c>(cpp:358) 一律直接返回，
///         <b>写给它的 SelectedIndex 会进依赖属性，但内部选中态此刻还没生效</b>。</item>
///   <item><c>RadioButtons.cpp:118-138</c>　repeater Loaded 那一刻解禁，并立刻用
///         <c>UpdateSelectedIndex()</c>(cpp:543) <b>从依赖属性回读</b>自愈。</item>
///   <item><c>RadioButtons.cpp:516-518</c>　<c>UpdateItemsSource()</c> 无条件先
///         <c>Select(-1)</c>——这是 items 重建必然附赠的一发。</item>
/// </list>
/// 由此定下本 handler 的两条硬规则，其余取舍都是它们的推论：
/// <list type="number">
///   <item><b>值照旧往依赖属性写</b>，不走"等 Loaded 了再补"的路——依赖属性就是
///         WinUI 自己认的权威，Loaded 时它会自己补，我们再补是多余且容易写歪。</item>
///   <item><b>未就绪期间的事件一发都不放行</b>（判据见 <see cref="ReadyGate"/>）；
///         items 重建引发的那次 <c>-1</c> 单独按因果拦掉，不靠猜。</item>
/// </list>
/// </remarks>
internal sealed class RadioButtonsHandler : ElementHandler<RadioButtonsElement, MuxControls.RadioButtons>
{
    private static readonly WeakTable<MuxControls.RadioButtons, Action<int>?> Callbacks = new();

    /// <summary><c>SelectedIndex</c> 受控：写回会触发 SelectionChanged，需要回声抑制。</summary>
    private static readonly EchoGuard SelectionEcho = new();

    /// <summary>最近一次下发的受控值：Loaded 那一刻要重放，幂等所以要它做等价比较。</summary>
    private static readonly WeakTable<MuxControls.RadioButtons, SelectedTarget> Targets = new();

    /// <summary>
    /// 正在整批重建 items。期间的选中事件全是 <c>UpdateItemsSource()</c> 的副作用
    /// （cpp:518 那句无条件 <c>Select(-1)</c>），不是用户操作。
    /// </summary>
    private static readonly WeakTable<MuxControls.RadioButtons, bool> Rebuilding = new();

    protected override MuxControls.RadioButtons Mount(
        Reconciler reconciler,
        RadioButtonsElement element)
    {
        var control = new MuxControls.RadioButtons { Header = element.Header };

        foreach (var item in element.Items ?? Array.Empty<string>())
        {
            control.Items.Add(item);
        }

        // 顺序不能反：回调先挂上（带闸），再写 SelectedIndex。
        // 这次"受控写回"发出的 SelectionChanged：没进树时会被就绪闸吞掉，
        // 已进树时会被 EchoGuard 认成回声——两条路都不会冒到用户那里。
        Rebind(control, element.OnSelectedIndexChanged);
        ApplySelectedIndex(control, element.SelectedIndex);

        // 进树那一刻：WinUI 自己会按依赖属性自愈，这里重放一次是双保险
        // （幂等——选中值已经对了就什么都不做）。
        ReadyGate.Arm(control, ctl =>
        {
            if (Targets.TryGetValue(ctl, out var target))
            {
                ApplySelectedIndex(ctl, target.Index);
            }
        });

        return control;
    }

    protected override void Update(
        Reconciler reconciler,
        RadioButtonsElement oldElement,
        RadioButtonsElement newElement,
        MuxControls.RadioButtons control)
    {
        PropWriter.Set(oldElement.Header, newElement.Header, value => control.Header = value);

        if (!PropWriter.SequenceEqual(oldElement.Items, newElement.Items))
        {
            ReplaceItems(control, newElement.Items);
        }

        ApplySelectedIndex(control, newElement.SelectedIndex);

        Rebind(control, newElement.OnSelectedIndexChanged);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.RadioButtons control)
    {
        ReadyGate.Disarm(control);
        SelectionEcho.Forget(control);
        Callbacks.Remove(control);
        Targets.Remove(control);
        Rebuilding.Remove(control);
    }

    /// <summary>
    /// 整批替换 items。源码里这一步会走到 <c>UpdateItemsSource() → Select(-1)</c>，
    /// 那一发必须关在门里，否则用户会收到一次凭空出现的"选中被清空"。
    /// </summary>
    private static void ReplaceItems(
        MuxControls.RadioButtons control, IReadOnlyList<string>? items)
    {
        // WinUI 的 PropertyChanged 回调是同步派发的，所以这个标记位能精确覆盖整个窗口。
        Rebuilding.Set(control, true);
        try
        {
            control.Items.Clear();
            foreach (var item in items ?? Array.Empty<string>())
            {
                control.Items.Add(item);
            }
        }
        finally
        {
            Rebuilding.Set(control, false);
        }
    }

    /// <summary>
    /// 把受控值落到控件上。<b>没有值时要清掉登记</b>——否则控件已经不受控了，
    /// Loaded 兜底还会把旧目标值按回去。
    /// </summary>
    private static void ApplySelectedIndex(
        MuxControls.RadioButtons control, Optional<int> index)
    {
        if (!index.HasValue)
        {
            Targets.Remove(control);
            return;
        }

        ApplySelectedIndex(control, index.Value);
    }

    private static void ApplySelectedIndex(MuxControls.RadioButtons control, int index)
    {
        Targets.Set(control, new SelectedTarget(index));

        // 同 ComboBox：受控值的合法性由 <c>Selector</c> 的写回策略统一回答。
        if (!SelectionPolicy.ShouldApply(control.Items.Count, control.SelectedIndex, index))
        {
            if (SelectionPolicy.IsOutOfRange(control.Items.Count, index))
            {
                ReactorLog.Gate(
                    $"RadioButtons{CtlId.Tag(control)} 受控值 {index} 越界" +
                    $"（Items.Count={control.Items.Count}），不下发");
            }

            return;
        }

        // Info 级但只在"真的要写"时才记：正常稳态下这一行不该出现。
        // 反复出现 = 控件在两轮之间自己漂走了（state 与控件对不上），
        // 这正是"点了没反应"当场最难取证的一环。
        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 RadioButtons{CtlId.Tag(control)}: {control.SelectedIndex} → {index}");

        // 同 ComboBox：只在回声真会走到回声那道闸时才登记，避免泄漏陈旧期望
        // 把之后一次真实用户点击判成回声吞掉（判据见 SelectionGate.ShouldExpectEcho）。
        if (SelectionGate.ShouldExpectEcho(
                index,
                ReadyGate.IsReady(control),
                Rebuilding.TryGetValue(control, out var busy) && busy))
        {
            SelectionEcho.Expect(control, index);
        }

        control.SelectedIndex = index;

        // 同 ComboBox / ToggleSwitch：写入完了若没等到回声，撤销登记。
        // 少了这一步，"受控 + 没给 OnSelectedIndexChanged" 的控件会把这次写入的
        // 期望一直挂着，等用户点回同一个值时被认成回声吞掉（INV6/INV7 那条线）。
        if (SelectionEcho.CancelIfUnconsumed(control))
        {
            ReactorLog.Gate(
                $"RadioButtons{CtlId.Tag(control)} 本次下发无回声（回调为空）→ 撤销登记");
        }
    }

    /// <summary>
    /// 事件 → 用户回调的四道闸门。<b>顺序即因果链，不能调。</b>
    /// </summary>
    /// <remarks>
    /// <list type="number">
    ///   <item><b>判据零（事件参数）</b>：<c>AddedItems</c> 里没有真东西 → 这是"取消选中"，
    ///         作者是 <c>OnChildUnchecked</c>(cpp:431)，不是用户选中了某一项。
    ///         <b>这一道必须在最前，且必须靠 args 判</b>——取消与选中两发的顺序没有保证，
    ///         值与就绪状态都区分不开它们（详见 <see cref="SelectionArgs"/>）。</item>
    ///   <item><b>判据一（未就绪）</b>：控件还没进过可视树 → 对面 <c>m_blockSelecting</c>
    ///         还是 true（<c>RadioButtons.h:91</c>），此刻的事件全是内部中间态。</item>
    ///   <item><b>判据二（重建中）</b>：items 整批替换引发的那一发，作者是
    ///         <c>UpdateItemsSource</c>(cpp:518)，不是用户。</item>
    ///   <item><b>判据三（回声）</b>：值等于我们刚写进去的那个，作者是本次下发。</item>
    /// </list>
    /// </remarks>
    private static void Dispatch(
        MuxControls.RadioButtons control,
        SelectionChangedEventArgs args,
        Action<int>? callback)
    {
        var value = control.SelectedIndex;
        var tag = CtlId.Tag(control);

        // 同 ComboBoxHandler.Dispatch：前三道走纯判据，回声那道单独判，
        // 保证 Consume 的副作用只在"真走到这一道"时才发生。
        var verdict = SelectionGate.Decide(
            SelectionArgs.SelectedSomething(args),
            ReadyGate.IsReady(control),
            Rebuilding.TryGetValue(control, out var busy) && busy);

        if (verdict == SelectionVerdict.Pass && SelectionEcho.Consume(control, value))
        {
            verdict = SelectionVerdict.Echo;
        }

        if (SelectionGate.Suppress(verdict))
        {
            ReadyStats.Suppressed++;
            ReactorLog.Gate($"RadioButtons{tag} {SelectionGate.Reason(verdict)}，吞 {value}");

            // 同 ComboBox：只补"取消选中"这一道。典型触发是<b>点当前已选中项</b>——
            // 那一发把控件打到 -1，界面上就"什么都没选中"了，而 state 还是旧值。
            // 受控纠正不受"有没有人监听"影响（见 ShouldRestoreAfterSuppress）。
            if (SelectionGate.ShouldRestoreAfterSuppress(verdict))
            {
                ReactorLog.Gate($"RadioButtons{tag} 纠正回受控值（控件停在 {value}）");
                SelectionRestore.Schedule(control, Targets, c => c.Items.Count, ApplySelectedIndex);
            }

            return;
        }

        ReactorLog.Pass($"RadioButtons{tag} → 用户回调 SelectedIndex={value}");
        callback?.Invoke(value);
    }

    private static void Rebind(MuxControls.RadioButtons control, Action<int>? callback)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = null;
            control.SelectionChanged += (s, args) =>
            {
                var rb = (MuxControls.RadioButtons)s;
                if (Callbacks.TryGetValue(rb, out var current))
                {
                    Dispatch(rb, args, current);
                }
            };
        }

        Callbacks[control] = callback;
    }
}

/// <summary>
/// 进度条。Value 为 null 表示不确定进度。
/// 与 ProgressRing 一样统一用 WinUI 2 的控件（<c>Microsoft.UI.Xaml.Controls.ProgressBar</c>），
/// 保证两者视觉风格一致、且都支持确定进度；UWP 原生 ProgressBar 的样式在
/// WinUI 2 主题资源下会退化成旧版外观。
/// </summary>
internal sealed class ProgressHandler : ElementHandler<ProgressElement, MuxControls.ProgressBar>
{
    protected override MuxControls.ProgressBar Mount(Reconciler reconciler, ProgressElement element)
    {
        var bar = new MuxControls.ProgressBar
        {
            Minimum = element.Minimum,
            Maximum = element.Maximum,
            IsIndeterminate = element.IsIndeterminate,
            ShowError = element.ShowError,
            ShowPaused = element.ShowPaused,
        };

        if (element.Value is { } value)
        {
            bar.Value = value;
        }

        return bar;
    }

    protected override void Update(
        Reconciler reconciler,
        ProgressElement oldElement,
        ProgressElement newElement,
        MuxControls.ProgressBar control)
    {
        PropWriter.Set(oldElement.Minimum, newElement.Minimum, value => control.Minimum = value);
        PropWriter.Set(oldElement.Maximum, newElement.Maximum, value => control.Maximum = value);
        PropWriter.Set(
            oldElement.IsIndeterminate,
            newElement.IsIndeterminate,
            value => control.IsIndeterminate = value);
        PropWriter.Set(oldElement.ShowError, newElement.ShowError, value => control.ShowError = value);
        PropWriter.Set(oldElement.ShowPaused, newElement.ShowPaused, value => control.ShowPaused = value);

        if (newElement.Value is { } value && control.Value != value)
        {
            control.Value = value;
        }
    }
}

/// <summary>
/// 进度环。UWP 原生 ProgressRing 只有 IsActive、不支持确定进度，
/// 因此这里用 WinUI 2 的 ProgressRing（同官方一样支持 Value/Min/Max）。
/// </summary>
internal sealed class ProgressRingHandler : ElementHandler<ProgressRingElement, MuxControls.ProgressRing>
{
    protected override MuxControls.ProgressRing Mount(Reconciler reconciler, ProgressRingElement element)
    {
        var ring = new MuxControls.ProgressRing
        {
            Minimum = element.Minimum,
            Maximum = element.Maximum,
            IsActive = element.IsActive,
            IsIndeterminate = element.IsIndeterminate,
        };

        if (element.Value is { } value)
        {
            ring.Value = value;
        }

        return ring;
    }

    protected override void Update(
        Reconciler reconciler,
        ProgressRingElement oldElement,
        ProgressRingElement newElement,
        MuxControls.ProgressRing control)
    {
        PropWriter.Set(oldElement.Minimum, newElement.Minimum, value => control.Minimum = value);
        PropWriter.Set(oldElement.Maximum, newElement.Maximum, value => control.Maximum = value);
        PropWriter.Set(oldElement.IsActive, newElement.IsActive, value => control.IsActive = value);
        PropWriter.Set(
            oldElement.IsIndeterminate,
            newElement.IsIndeterminate,
            value => control.IsIndeterminate = value);

        if (newElement.Value is { } value && control.Value != value)
        {
            control.Value = value;
        }
    }
}

/// <summary>图片。Source 是字符串，运行时解析为 Uri；非法 URI 静默忽略。</summary>
internal sealed class ImageHandler : ElementHandler<ImageElement, Image>
{
    protected override Image Mount(Reconciler reconciler, ImageElement element)
    {
        var image = new Image { Source = CreateSource(element.Source) };

        if (element.Width is { } width)
        {
            image.Width = width;
        }

        if (element.Height is { } height)
        {
            image.Height = height;
        }

        if (ParseStretch(element.Stretch) is { } stretch)
        {
            image.Stretch = stretch;
        }

        return image;
    }

    protected override void Update(
        Reconciler reconciler,
        ImageElement oldElement,
        ImageElement newElement,
        Image control)
    {
        if (!string.Equals(oldElement.Source, newElement.Source, StringComparison.Ordinal))
        {
            control.Source = CreateSource(newElement.Source);
        }

        PropWriter.Set(oldElement.Width, newElement.Width, value =>
        {
            if (value is { } width)
            {
                control.Width = width;
            }
        });

        PropWriter.Set(oldElement.Height, newElement.Height, value =>
        {
            if (value is { } height)
            {
                control.Height = height;
            }
        });

        PropWriter.Set(
            ParseStretch(oldElement.Stretch),
            ParseStretch(newElement.Stretch),
            value =>
            {
                if (value is { } stretch)
                {
                    control.Stretch = stretch;
                }
            });
    }

    private static Stretch? ParseStretch(string? value) =>
        value is not null && Enum.TryParse<Stretch>(value, true, out var parsed) ? parsed : null;

    private static ImageSource? CreateSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        // 走 PackUri：补 ms-appx: 绝对前缀，并把清单里的反斜杠路径规范化。
        // 直接 new Uri("ms-appx:///Assets\StoreLogo.png") 的话反斜杠会带进
        // Windows.Foundation.Uri，资源解析失败且<b>不报错</b>——图就是不出来。
        return PackUri.TryCreate(source) is { } uri ? new BitmapImage(uri) : null;
    }
}

/// <summary>
/// ListView / GridView 的公共行为（Items 为元素数组）。
/// </summary>
/// <remarks>
/// <b>这里套的是与 <see cref="ComboBoxHandler"/> 同一套受控选中设施。</b>两者共用的
/// 是 XAML 内核里的同一个基类 <c>Selector</c>：
/// <c>SelectedIndex</c> 的依赖属性变更会同步转到 <c>OnSelectedIndexChanged</c>
/// （<c>Selector_Partial.cpp:144-148</c>），而选中一旦真的变了，
/// <c>EndChange</c> 会<b>同步</b>调 <c>InvokeSelectionChanged</c> 把事件抛出来。
/// 也就是说"受控写回会触发 <c>SelectionChanged</c>"这件事对 <c>ListView</c> /
/// <c>GridView</c> 同样成立——写回策略见 <see cref="SelectionPolicy"/>。
/// <para>
/// <b>与 RadioButtons 唯一的不同：这里不用 <see cref="ReadyGate"/>。</b>
/// <c>RadioButtons</c> 有一道 <c>m_blockSelecting</c>（模板没套上时押住 <c>Select</c>），
/// 所以要先问控件进没进树；而 <c>Selector::OnSelectedIndexChanged</c> 开头的
/// 提前返回只有两个条件——<c>IsSelectionReentrancyAllowed()</c>（重入锁）与
/// <c>IsInit()</c>（正在解析 XAML），<b>与模板是否套上无关</b>。
/// 于是这里直接传"已就绪"，不是偷懒，是没有那个答案要问。
/// </para>
/// </remarks>
internal abstract class ItemsViewHandler<TElement, TControl> : ElementHandler<TElement, TControl>
    where TElement : Element
    where TControl : ListViewBase
{
    private static readonly WeakTable<TControl, (Action<int>? Selection, Action<int>? Click)> Callbacks = new();

    /// <summary>最近一次下发的受控值：万一控件自己飘了，纠正时要用。</summary>
    private static readonly WeakTable<TControl, SelectedTarget> Targets = new();

    /// <summary><c>SelectedIndex</c> 受控：写回会同步触发 SelectionChanged，需要回声抑制。</summary>
    private static readonly EchoGuard SelectionEcho = new();

    /// <summary>
    /// 正在改动 items（增删替）：期间的选中变动作者是 <c>Selector</c> 自己，不是用户。
    /// </summary>
    /// <remarks>
    /// 依据同为本文件的 <c>Selector</c> 源码：<c>NotifyOfSourceChanged</c> /
    /// <c>OnItemsChanged</c> 在集合增删时会 <c>BeginChange → Unselect/Select →
    /// EndChange</c>，走的就是会抛 <c>SelectionChanged</c> 的那条路。
    /// 例如移除当前选中项，控件会自己把选中变成 -1 并抛事件——
    /// 这一发不关用户的事，放着不管就等于"换一次数据源，回调白跑一次"。
    /// </remarks>
    private static readonly WeakTable<TControl, bool> Rebuilding = new();

    protected abstract IReadOnlyList<Element?> ItemsOf(TElement element);
    protected abstract Optional<int> SelectedIndexOf(TElement element);
    protected abstract Action<int>? SelectionCallbackOf(TElement element);
    protected abstract Action<int>? ItemClickCallbackOf(TElement element);
    protected abstract ListViewSelectionMode ModeOf(TElement element);
    protected abstract string? HeaderOf(TElement element);

    protected override void Update(
        Reconciler reconciler,
        TElement oldElement,
        TElement newElement,
        TControl control)
    {
        var oldHeader = HeaderOf(oldElement);
        var newHeader = HeaderOf(newElement);
        if (!Equals(oldHeader, newHeader))
        {
            control.Header = newHeader;
        }

        var oldMode = ModeOf(oldElement);
        var newMode = ModeOf(newElement);
        if (oldMode != newMode)
        {
            // 改 SelectionMode 会把选中态一起牵动，那一发 SelectionChanged 抛在
            // **旧订阅还挂着**的时候（Rebind 在 Update 最后才换回调），
            // 于是"没人点过列表"却回调了一次 —— 与第 14 处同一个形状，只是换了个属性。
            //
            // 源码依据（`dxaml/xcp/dxaml/lib/ListViewBase_Partial.cpp`，SelectionMode
            // 不在基类 Selector 上，它在 ListViewBase）：
            //   case KnownPropertyIndex::ListViewBase_SelectionMode:
            //       // Call OnSelectionModeChanged when the selection mode changes.
            //       IFC(OnSelectionModeChanged(old, new));
            //       // OnSelectionModeChanged will update all Selection related properties
            //       IFC(UpdateVisibleAndCachedItemsSelectionAndVisualState(false));
            // 注释是源码里逐字写着的：它会更新**所有**与选中相关的属性。
            // 具体收敛成什么（Multiple → Single 留第一个、→ None 全清）取决于
            // OnSelectionModeChanged 的函数体，那一段没取到（文件被截断），
            // 所以这里不猜值，只开窗——与 Slider 那半同样的代价不对称：
            // 窗没等到事件时代价是零，漏罩则是一发假回调。
            using (SelectionEcho.Silence(control))
            {
                control.SelectionMode = newMode;
            }
        }

        var clickEnabled = ItemClickCallbackOf(newElement) is not null;
        if (control.IsItemClickEnabled != clickEnabled)
        {
            control.IsItemClickEnabled = clickEnabled;
        }

        // 改 items 的这段时间，控件自己会因为集合变动重算选中并发事件
        // （见 Rebuilding 那段的注释）。这一段的作者不是用户，先关起来。
        Rebuilding.Set(control, true);
        try
        {
            reconciler.PatchItems(control, ItemsOf(oldElement), ItemsOf(newElement));
        }
        finally
        {
            Rebuilding.Set(control, false);
        }

        ApplySelectedIndex(control, SelectedIndexOf(newElement));

        Rebind(control, SelectionCallbackOf(newElement), ItemClickCallbackOf(newElement));
    }

    protected void Initialize(Reconciler reconciler, TControl control, TElement element)
    {
        control.Header = HeaderOf(element);
        control.SelectionMode = ModeOf(element);
        control.IsItemClickEnabled = ItemClickCallbackOf(element) is not null;

        foreach (var item in ItemsOf(element))
        {
            if (item is null)
            {
                continue;
            }

            control.Items.Add(reconciler.Build(item));
        }

        // 顺序不能反：先把带闸的事件处理器挂上，再写 SelectedIndex——
        // 这一次受控写回发出的 SelectionChanged 必须被认成回声，
        // 而不是"页面一出现就回调了一次 selected-item-changed"。
        Rebind(control, SelectionCallbackOf(element), ItemClickCallbackOf(element));
        ApplySelectedIndex(control, SelectedIndexOf(element));
    }

    protected override void Unmount(Reconciler reconciler, TControl control)
    {
        SelectionEcho.Forget(control);
        Callbacks.Remove(control);
        Targets.Remove(control);
        Rebuilding.Remove(control);
    }

    /// <summary>把受控值落到控件上。<b>没有值时要清掉登记</b>——否则控件已经不受控了。</summary>
    private static void ApplySelectedIndex(TControl control, Optional<int> index)
    {
        if (!index.HasValue)
        {
            Targets.Remove(control);
            return;
        }

        ApplySelectedIndex(control, index.Value);
    }

    private static void ApplySelectedIndex(TControl control, int index)
    {
        Targets.Set(control, new SelectedTarget(index));

        if (!SelectionPolicy.ShouldApply(control.Items.Count, control.SelectedIndex, index))
        {
            if (SelectionPolicy.IsOutOfRange(control.Items.Count, index))
            {
                // 不是"写了没效果"，是"写了会被拒"：Selector 会把这次变更整条 undo，
                // 并在 items 已存在时让属性变更回调以 E_INVALIDARG 收尾。
                // 受控值没跟上 items 变少——这是调用方的时序，不是控件能兑现的值。
                ReactorLog.Gate(
                    $"{Name(control)} 受控值 {index} 越界（Items.Count={control.Items.Count}），不下发");
            }

            return;
        }

        // Info 级但只在"真的要写"时才记：稳态下这一行不该出现，
        // 反复出现 = 控件在两轮之间自己漂走了。
        ReactorLog.Info(
            ReactorLogChannel.Patch,
            $"受控下发 {Name(control)}: {control.SelectedIndex} → {index}");

        if (SelectionGate.ShouldExpectEcho(
                index,
                isReady: true,
                Rebuilding.TryGetValue(control, out var busy) && busy))
        {
            SelectionEcho.Expect(control, index);
        }

        control.SelectedIndex = index;

        // 写入完了再回头看一眼：这一发的回声到现在还没人来领就撤销登记。
        // 留着它，等用户之后点到同一个下标，会被判成框架自己的回声吞掉
        // （成因与其余站点相同，见 EchoGuard.CancelIfUnconsumed 那段注释）。
        if (SelectionEcho.CancelIfUnconsumed(control))
        {
            ReactorLog.Gate($"{Name(control)} 本次下发无回声 → 撤销登记");
        }
    }

    /// <summary>
    /// 事件 → 用户回调的四道闸门，与 <see cref="ComboBoxHandler.Dispatch"/> 同构。
    /// </summary>
    /// <remarks>
    /// <b><c>callback is null</c> 不许在这一层短路。</b>纠正兑现的是"这个属性由 state
    /// 说了算"的承诺，与有没有人监听无关——<c>SelectionRestore.Schedule</c> 那一行
    /// 因此站在 <see cref="SelectionGate.ShouldRestoreAfterSuppress"/> 之后，
    /// 而不是在门口。第三道源码契约守着这件事。
    /// </remarks>
    private static void Dispatch(
        TControl control, SelectionChangedEventArgs args, Action<int>? callback)
    {
        var value = control.SelectedIndex;
        var tag = Name(control);

        // 就绪那一道直接给 true：<c>Selector</c> 没有 <c>RadioButtons</c> 那种模板闸门
        // （见类注释里的源码依据），"未就绪"在这里不是一个可问的问题。
        var verdict = SelectionGate.Decide(
            SelectionArgs.SelectedSomething(args),
            isReady: true,
            Rebuilding.TryGetValue(control, out var busy) && busy);

        if (verdict == SelectionVerdict.Pass && SelectionEcho.Consume(control, value))
        {
            verdict = SelectionVerdict.Echo;
        }

        if (SelectionGate.Suppress(verdict))
        {
            ReadyStats.Suppressed++;
            ReactorLog.Gate($"{tag} {SelectionGate.Reason(verdict)}，吞 {value}");

            if (SelectionGate.ShouldRestoreAfterSuppress(verdict))
            {
                ReactorLog.Gate($"{tag} 纠正回受控值（控件停在 {value}）");
                SelectionRestore.Schedule(control, Targets, c => c.Items.Count, ApplySelectedIndex);
            }

            return;
        }

        ReactorLog.Pass($"{tag} → 用户回调 SelectedIndex={value}");
        callback?.Invoke(value);
    }

    /// <summary>日志里的控件身份。<c>typeof</c> 的名字是稳定的，实例编号走 <see cref="CtlId"/>。</summary>
    private static string Name(TControl control) => $"{typeof(TControl).Name}{CtlId.Tag(control)}";

    private static void Rebind(TControl control, Action<int>? selection, Action<int>? click)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = (null, null);
            control.SelectionChanged += (s, args) =>
            {
                var view = (TControl)s;
                if (Callbacks.TryGetValue(view, out var current))
                {
                    // 不管这一轮有没有人监听，四道判据与纠正都要跑：
                    // 它们兑现的是"受控"，不是"送达"。
                    Dispatch(view, args, current.Selection);
                }
            };

            control.ItemClick += (s, e) =>
            {
                var view = (TControl)s;
                if (Callbacks.TryGetValue(view, out var current) && e.ClickedItem is UIElement clicked)
                {
                    current.Click?.Invoke(view.Items.IndexOf(clicked));
                }
            };
        }

        Callbacks[control] = (selection, click);
    }
}

internal sealed class ListViewHandler : ItemsViewHandler<ListViewElement, ListView>
{
    protected override ListView Mount(Reconciler reconciler, ListViewElement element)
    {
        var view = new ListView();
        Initialize(reconciler, view, element);
        return view;
    }

    protected override IReadOnlyList<Element?> ItemsOf(ListViewElement element) => element.Items;
    protected override Optional<int> SelectedIndexOf(ListViewElement element) => element.SelectedIndex;
    protected override Action<int>? SelectionCallbackOf(ListViewElement element) => element.OnSelectedIndexChanged;
    protected override Action<int>? ItemClickCallbackOf(ListViewElement element) => element.OnItemClick;
    protected override ListViewSelectionMode ModeOf(ListViewElement element) => element.SelectionMode;
    protected override string? HeaderOf(ListViewElement element) => element.Header;
}

internal sealed class GridViewHandler : ItemsViewHandler<GridViewElement, GridView>
{
    protected override GridView Mount(Reconciler reconciler, GridViewElement element)
    {
        var view = new GridView();
        Initialize(reconciler, view, element);
        return view;
    }

    protected override IReadOnlyList<Element?> ItemsOf(GridViewElement element) => element.Items;
    protected override Optional<int> SelectedIndexOf(GridViewElement element) => element.SelectedIndex;
    protected override Action<int>? SelectionCallbackOf(GridViewElement element) => element.OnSelectedIndexChanged;
    protected override Action<int>? ItemClickCallbackOf(GridViewElement element) => element.OnItemClick;
    protected override ListViewSelectionMode ModeOf(GridViewElement element) => element.SelectionMode;
    protected override string? HeaderOf(GridViewElement element) => element.Header;
}

/// <summary>WinUI 2 的 NavigationView：左侧菜单 + 内容区 + 返回按钮。</summary>
internal sealed class NavigationViewHandler : ElementHandler<NavigationViewElement, MuxControls.NavigationView>
{
    private static readonly WeakTable<
        MuxControls.NavigationView,
        (Action<int>? Selection, Action<int>? Invoked, Action? Back)> Callbacks = new();

    /// <summary>SelectedItem 受控：写回会触发 SelectionChanged，需要回声抑制。</summary>
    private static readonly EchoGuard SelectionEcho = new();

    /// <summary>受控目标值：<c>Loaded</c> 之后菜单项才到位时，靠它补发一次。</summary>
    private static readonly WeakTable<MuxControls.NavigationView, SelectedTarget> Targets = new();

    /// <summary>
    /// 菜单正在整批替换（<c>MenuItems</c> 重建 / 左右与顶部布局互切）。
    /// </summary>
    /// <remarks>
    /// <b>它是"持续标记"而不是"时间窗"，这一点是刻意的。</b>
    /// 这两处改动的后果由 <c>repeater</c> 承载，而 repeater 是<b>布局期</b>才把元素
    /// realize 出来的——源码注释原文（<c>release/2.8</c>
    /// <c>dev/NavigationView/NavigationView.cpp</c>，<c>OnSelectionModelSelectionChanged</c>）：
    /// <c>"Template has not been applied yet. SelectionModel's selectedIndex state will
    /// get properly updated after the repeater finishes loading."</c>
    /// 即：选中状态的重算会落在"repeater 加载完"那个时刻，而不是我们改集合的那一行。
    /// 用 <c>using (Silence(...))</c> 那种时间窗罩不住它——窗在我们返回时就关了，
    /// 而那一发事件还没到（与第 18 节记的那条边界是同一种形状）。
    /// 所以这里用显式开门 / 关门的标记，并且<b>开到受控值补发完</b>才关。
    /// </remarks>
    private static readonly WeakTable<MuxControls.NavigationView, bool> Rebuilding = new();

    protected override MuxControls.NavigationView Mount(
        Reconciler reconciler,
        NavigationViewElement element)
    {
        var nav = new MuxControls.NavigationView
        {
            Header = element.Header,
            IsPaneOpen = element.IsPaneOpen,
            IsSettingsVisible = element.IsSettingsVisible,
            IsBackButtonVisible = element.IsBackButtonVisible
                ? MuxControls.NavigationViewBackButtonVisible.Visible
                : MuxControls.NavigationViewBackButtonVisible.Collapsed,
            IsBackEnabled = element.IsBackEnabled,
            AlwaysShowHeader = element.AlwaysShowHeader,
            PaneDisplayMode = ToPaneMode(element.PaneDisplayMode),
        };

        ApplyMenuItems(nav, element.MenuItems);

        // 顺序不能反：先把带闸的事件处理器挂上，再写受控值——
        // 与 ItemsViewHandler.Initialize 那条同一个道理。
        Rebind(nav, element.OnSelectedIndexChanged, element.OnItemInvoked, element.OnBackRequested);
        ApplySelectedItem(nav, element.SelectedIndex);

        nav.Content = element.Content is null ? null : reconciler.Build(element.Content);

        // 菜单项要等 repeater 加载完才真正到位（源码依据见 Rebuilding 那段注释），
        // 所以 Loaded 之后按目标值再补发一次，否则"进页面就没有选中项"。
        ReadyGate.Arm(nav, ctl =>
        {
            if (Targets.TryGetValue(ctl, out var target))
            {
                ApplySelectedItem(ctl, target.Index);
            }
        });

        return nav;
    }

    protected override void Update(
        Reconciler reconciler,
        NavigationViewElement oldElement,
        NavigationViewElement newElement,
        MuxControls.NavigationView control)
    {
        // 只写真正变化的属性：NavigationView 会按用户操作自己开合 pane，
        // 每帧无条件写回 IsPaneOpen 会把用户的开合状态强行拽回来 → 导航条反复开合闪烁。
        if (!Equals(oldElement.Header, newElement.Header))
        {
            control.Header = newElement.Header;
        }

        if (oldElement.IsPaneOpen != newElement.IsPaneOpen)
        {
            control.IsPaneOpen = newElement.IsPaneOpen;
        }

        if (oldElement.IsSettingsVisible != newElement.IsSettingsVisible)
        {
            control.IsSettingsVisible = newElement.IsSettingsVisible;
        }

        if (oldElement.IsBackButtonVisible != newElement.IsBackButtonVisible)
        {
            control.IsBackButtonVisible = newElement.IsBackButtonVisible
                ? MuxControls.NavigationViewBackButtonVisible.Visible
                : MuxControls.NavigationViewBackButtonVisible.Collapsed;
        }

        if (oldElement.IsBackEnabled != newElement.IsBackEnabled)
        {
            control.IsBackEnabled = newElement.IsBackEnabled;
        }

        if (oldElement.AlwaysShowHeader != newElement.AlwaysShowHeader)
        {
            control.AlwaysShowHeader = newElement.AlwaysShowHeader;
        }

        // 下面这两处改动的作者都是我们，引发的选中重算却由 repeater 承载，
        // 到场时间在我们返回之后（源码依据见 <see cref="Rebuilding"/> 那段注释）。
        // 所以抑制用<b>持续标记</b>，并且开到受控值补发完才关——
        // 用 using 那种时间窗会在返回那一刻就关掉，而那一发事件还没到。
        var modeChanged = oldElement.PaneDisplayMode != newElement.PaneDisplayMode;
        var menuChanged = !SameMenuItems(oldElement.MenuItems, newElement.MenuItems);
        var rebuild = modeChanged || menuChanged;

        if (rebuild)
        {
            Rebuilding.Set(control, true);
        }

        try
        {
            if (modeChanged)
            {
                // 与 MenuItems 同一族：UpdateRepeaterItemsSource 在 Top / Left 之间切换时
                // 会把另一侧的 repeater 源置空、这一侧重建（源码同一个函数），
                // 选中项同样可能跟着塌一次。
                control.PaneDisplayMode = ToPaneMode(newElement.PaneDisplayMode);
            }

            if (menuChanged)
            {
                // MenuItems 是 Clear() 之后重新 Add 的。清空把选中容器一起带走了：
                // SelectionModel 的 SelectedItem() 变 nullptr，而 NavigationView 自己的
                // SelectedItem 依赖属性此刻还是旧值 —— 于是
                // OnSelectionModelSelectionChanged 那条早退（selectedItem == SelectedItem()）
                // 不成立，它会继续走 SetSelectedItemAndExpectItemInvoke… → ChangeSelection
                // → RaiseSelectionChangedEvent。这一发的作者是我们，不是用户。
                // （release/2.8 dev/NavigationView/NavigationView.cpp，函数体逐字核对过；
                // 早退条件是 m_shouldIgnoreNextSelectionChange || selectedItem == SelectedItem()
                // || !m_appliedTemplate。）
                ApplyMenuItems(control, newElement.MenuItems);
            }

            // 重建过就必须重新落一次受控值：控件里的 SelectedItem 可能已经被清空，
            // 而 element 侧的下标往往压根没变——那就是"丢状态"，不是假回调。
            ApplySelectedItem(control, newElement.SelectedIndex);
        }
        finally
        {
            if (rebuild)
            {
                Rebuilding.Set(control, false);
            }
        }

        reconciler.PatchSingleChild(control, oldElement.Content, newElement.Content);
        Rebind(
            control,
            newElement.OnSelectedIndexChanged is null
                ? null
                : Guard(control, newElement.OnSelectedIndexChanged),
            newElement.OnItemInvoked,
            newElement.OnBackRequested);
    }

    /// <summary>SelectionChanged 回调包装：回读下标 == 框架刚写入的下标 → 是回声，不回调。</summary>
    private static Action<int>? Guard(MuxControls.NavigationView control, Action<int> callback) =>
        value =>
        {
            if (SelectionEcho.Consume(control, value))
            {
                return;
            }

            callback(value);
        };

    protected override Element? SingleChildOf(NavigationViewElement element) => element.Content;

    /// <summary>
    /// 把受控选中项落到控件上。
    /// </summary>
    /// <remarks>
    /// <b><c>NavigationView</c> 没有 <c>SelectedIndex</c> 属性</b>，当前值只能从
    /// <c>MenuItems.IndexOf(SelectedItem)</c> 反查（<c>-1</c> = 没有选中）。
    /// 这是它与 <c>ComboBox</c> 那三个 <c>Selector</c> 唯一的结构差别，
    /// 判据本身照旧共用 <see cref="SelectionPolicy"/>。
    /// </remarks>
    private static void ApplySelectedItem(MuxControls.NavigationView control, int index)
    {
        Targets.Set(control, new SelectedTarget(index));

        if (!SelectionPolicy.ShouldApply(
                control.MenuItems.Count,
                control.MenuItems.IndexOf(control.SelectedItem),
                index))
        {
            if (SelectionPolicy.IsOutOfRange(control.MenuItems.Count, index))
            {
                ReactorLog.Gate(
                    $"NavigationView{CtlId.Tag(control)} 受控值 {index} 越界" +
                    $"（MenuItems.Count={control.MenuItems.Count}），不下发");
            }

            return;
        }

        if (SelectionGate.ShouldExpectEcho(
                index,
                ReadyGate.IsReady(control),
                Rebuilding.TryGetValue(control, out var busy) && busy))
        {
            SelectionEcho.Expect(control, index);
        }

        control.SelectedItem = index < 0 ? null : control.MenuItems[index];

        // 这一发大概率**根本不会有回声**：API 写入 SelectedItem 会同步走到
        // OnSelectionModelSelectionChanged，而那里在 <c>selectedItem == SelectedItem()</c>
        // 时直接 return —— 即"经由 API 选中"不抛事件（源码同上，函数体逐字核对过）。
        // 既然等不到就撤销，别把它留成一条陈旧期望。
        SelectionEcho.CancelIfUnconsumed(control);
    }

    protected override void Unmount(Reconciler reconciler, MuxControls.NavigationView control)
    {
        ReadyGate.Disarm(control);
        SelectionEcho.Forget(control);
        Callbacks.Remove(control);
        Targets.Remove(control);
        Rebuilding.Remove(control);
    }

    private static void ApplyMenuItems(
        MuxControls.NavigationView nav,
        IReadOnlyList<NavigationViewItemData> items)
    {
        nav.MenuItems.Clear();

        var list = items ?? Array.Empty<NavigationViewItemData>();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not { } data)
            {
                continue;
            }

            var item = new MuxControls.NavigationViewItem
            {
                Content = data.Content,
                Tag = i,
            };

            if (data.Icon is { } glyph)
            {
                item.Icon = new FontIcon { Glyph = glyph };
            }

            nav.MenuItems.Add(item);
        }
    }

    private static bool SameMenuItems(
        IReadOnlyList<NavigationViewItemData>? a,
        IReadOnlyList<NavigationViewItemData>? b)
    {
        a ??= Array.Empty<NavigationViewItemData>();
        b ??= Array.Empty<NavigationViewItemData>();
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static MuxControls.NavigationViewPaneDisplayMode ToPaneMode(NavPaneDisplayMode mode) =>
        mode switch
        {
            NavPaneDisplayMode.Left => MuxControls.NavigationViewPaneDisplayMode.Left,
            NavPaneDisplayMode.LeftMinimal => MuxControls.NavigationViewPaneDisplayMode.LeftMinimal,
            NavPaneDisplayMode.Top => MuxControls.NavigationViewPaneDisplayMode.Top,
            NavPaneDisplayMode.Auto => MuxControls.NavigationViewPaneDisplayMode.Auto,
            _ => MuxControls.NavigationViewPaneDisplayMode.LeftCompact,
        };

    private static void Rebind(
        MuxControls.NavigationView control,
        Action<int>? selection,
        Action<int>? invoked,
        Action? back)
    {
        if (!Callbacks.ContainsKey(control))
        {
            Callbacks[control] = (null, null, null);

            control.SelectionChanged += (_, args) =>
            {
                // 菜单重建期间的事件作者是我们（依据见 Rebuilding 那段注释）。
                // 受控不是送达：有没有人挂回调，这一道都得拦。
                //
                // 判据零那一半由下面那句 container 守卫天然兑现：源码
                // RaiseSelectionChangedEvent 在 nextItem 为 nullptr 时**不设**
                // SelectedItemContainer，于是"取消选中"那一发的容器是 null，
                // 走不到回调——等价于 SelectionArgs.SelectedSomething 为假。
                if (Rebuilding.TryGetValue(control, out var busy) && busy)
                {
                    return;
                }

                // 判据一（未就绪）：模板没套好期间控件一条都不抛
                // （<c>!m_appliedTemplate</c> 那条早退），挂载期写下去的受控值要等
                // <b>repeater 加载完</b>才补抛——而 repeater 是模板子树的一部分，
                // 它的 <c>Loaded</c> 早于控件自己的 <c>Loaded</c>，所以那一发回来时
                // <c>IsReady</c> 仍是 false。它的作者是我们，不是用户。
                // 缺这一问，启动瞬间会凭空回调一次（模板常用它切页 → 初始页被改掉）。
                // 行为模型见 <c>RebuildEchoSim.MountDefersRestore</c>。
                if (!ReadyGate.IsReady(control))
                {
                    return;
                }

                if (args.SelectedItemContainer is MuxControls.NavigationViewItem item &&
                    item.Tag is int index &&
                    Callbacks.TryGetValue(control, out var current))
                {
                    current.Selection?.Invoke(index);
                }
            };

            // ItemInvoked 与 SelectionChanged 的区别：点已选中项也会触发，
            // 模板用它做"重复点击主页 → 回到主页"，SelectionChanged 不会回调。
            control.ItemInvoked += (_, args) =>
            {
                // 重建同样会走 ChangeSelection → RaiseItemInvoked，那一发也不是用户。
                if (Rebuilding.TryGetValue(control, out var busy) && busy)
                {
                    return;
                }

                // 与 SelectionChanged 同一条：repeater 加载完补抛的那一发同样
                // 落在控件 Loaded 之前，<c>ChangeSelection</c> 也会顺手抛 ItemInvoked。
                if (!ReadyGate.IsReady(control))
                {
                    return;
                }

                if (!Callbacks.TryGetValue(control, out var current))
                {
                    return;
                }

                var index = args.InvokedItemContainer is MuxControls.NavigationViewItem item &&
                            item.Tag is int tagged
                    ? tagged
                    : -1; // Settings 项没有我们写入的 Tag

                current.Invoked?.Invoke(index);
            };

            control.BackRequested += (_, _) =>
            {
                if (Callbacks.TryGetValue(control, out var current))
                {
                    current.Back?.Invoke();
                }
            };
        }

        Callbacks[control] = (selection, invoked, back);
    }
}
