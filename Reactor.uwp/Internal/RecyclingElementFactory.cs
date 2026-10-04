// ItemsRepeater 元素工厂的「必须回收」契约 —— 官方行为决定，不是可选优化。
//
// WinUI 官方 ViewManager.cpp（microsoft-ui-xaml / controls/dev/Repeater/ViewManager.cpp）：
//
//   auto children = repeater->Children();
//   if (CachedVisualTreeHelpers::GetParent(element) != static_cast<winrt::DependencyObject>(*repeater))
//       children.Append(element);
//
// 关键不在 Append，而在「什么时候不 Append」：元素只有尚未挂到 repeater 上时才会被加入
// Children，而 XAML 永远不负责把已挂载的元素摘下来。于是：
//
//   GetElement 每次都新建元素  ⇒ 每一个都永久留在 repeater 的 Children 里
//                             ⇒ 可视树随滚动无界增长
//                             ⇒ 实测：一轮急速滚动后 CLR 直接 FailFast（0x80131623）
//
// 所以工厂必须把 RecycleElement 交回来的容器重新从 GetElement 还回去。
// 官方 Reactor（microsoft-ui-xaml-reactor / src/Reactor/Core/ElementFactory.cs）也是这么做的，
// 并在注释里明确对齐了这段 ViewManager 行为 + 一条「不停放会留下幽灵行」的经验。
//
// 这里把该契约独立成一个薄层，供 VirtualizingList / 未来的 ElementFactory 复用：
//   1. GetElement   : 池里有就取出来复用，池空才新建；
//   2. RecycleElement: 不卸载（卸载白做，XAML 仍持有），折叠停放后入池；
//   3. 池有上限，超出部分不再复用 —— 那部分会成为永久孤儿，
//      上限要按「一次 realize 的行数」来给，而不是拍脑袋。
//
// 生命周期状态机（2026-10 新增，压测 invariants 的基础）：
//
//      New ──► Active ──► Pooled ──► Active ──► …
//                  │
//                  └──────► Discarded（池满 / 重复回收，不再复用）
//
//   禁止 Active→Active（double acquire）与 Pooled→Pooled（double recycle）。
//   库存守恒：Minted == Active + Pooled + Discarded（每个 quiescent 检查点都该成立）。
//   同一物理元素绝不能同时出现在 Active 与 Pool（active ∩ pooled = ∅）。
//
// 注意（2026-09 实测）：WinUI 2.8 的 ElementFactoryGetArgs 只有 Data / Parent，
// 没有 WinUI 3 的 Index；下标请走 ItemsRepeater 的 ElementPrepared / ElementIndexChanged，
// 不要用 Data 反查（重复数据项在信息论上不可恢复）。

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml;

namespace Reactor.Uwp.Internal;

/// <summary>元素在工厂里的租约状态。</summary>
internal enum ElementLeaseState
{
    /// <summary>刚被创建，尚未交给 ItemsRepeater。</summary>
    New,

    /// <summary>正被 ItemsRepeater 持有（在 realized 集合里）。</summary>
    Active,

    /// <summary>已被回收、停在池里等待复用。</summary>
    Pooled,

    /// <summary>已弃用：池满或发生了非法转换，永不再交给 ItemsRepeater。</summary>
    Discarded,
}

/// <summary>
/// 带回收池的 ItemsRepeater 元素工厂：把 <see cref="NativeElementFactory"/> 包一层，
/// 强制落实「容器复用 + 折叠停放 + 租约状态机」契约。
/// </summary>
internal sealed class RecyclingElementFactory : IDisposable
{
    private readonly Stack<UIElement> _pool = new();
    private readonly Dictionary<UIElement, ElementLeaseState> _lease =
        new(ReferenceEqualityComparer.Instance);
    private readonly Func<UIElement> _create;
    private readonly Action<UIElement, object?, UIElement?> _bind;
    private readonly int _maxPool;
    private readonly NativeElementFactory _native;
    private bool _disposed;

    /// <param name="create">新建容器。只有在池为空时才被调用。</param>
    /// <param name="bind">绑定内容；新建和复用都要调用，用来把数据写回容器。</param>
    /// <param name="maxPool">
    /// 池上限。必须不小于一次 realize 的行数，否则超出的容器会被丢弃成为永久孤儿。
    /// 官方 Reactor 取 512（MaxPooledContainers）。传 0 表示不复用（每次都新建）。
    /// </param>
    public RecyclingElementFactory(
        Func<UIElement> create,
        Action<UIElement, object?, UIElement?> bind,
        int maxPool = 512,
        string? logPath = null)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _bind = bind ?? throw new ArgumentNullException(nameof(bind));
        _maxPool = maxPool;

        _native = new NativeElementFactory(OnGetElement, OnRecycleElement, logPath);
    }

    /// <summary>可直接赋给 <c>ItemsRepeater.ItemTemplate</c>。</summary>
    public object ItemTemplate => _native.ItemTemplate;

    /// <summary>当前池中待复用的容器数量（观测用）。</summary>
    public int PooledCount => _pool.Count;

    /// <summary>
    /// 回收时是否折叠停放。默认 true。
    /// </summary>
    /// <remarks>
    /// WinUI <b>2</b> 的 ViewManager 不会把回收的元素从 repeater 的 Children 里摘掉，
    /// 元素仍在 visual tree 上却不参与布局 → 会以最后一次布局的矩形继续绘制 → 幽灵行。
    /// 折叠是本机实测确认过的 workaround（不是 WinUI 3 语义推导）。
    /// 压测里把这个开关做成 A/B：MODE 3（不折叠）/ MODE 4（折叠）对照幽灵行是否消失。
    /// </remarks>
    public bool CollapseOnRecycle { get; set; } = true;

    // ── 计数器（压测 invariants 用） ──────────────────────────────────────────

    /// <summary>GetElement 被调用次数。</summary>
    public int GetCalls { get; private set; }

    /// <summary>RecycleElement 被调用次数。</summary>
    public int RecycleCalls { get; private set; }

    /// <summary>累计新建的容器数。</summary>
    public int Minted { get; private set; }

    /// <summary>命中池、复用容器的次数。</summary>
    public int Reused { get; private set; }

    /// <summary>当前处于 Active 的容器数。</summary>
    public int Active { get; private set; }

    /// <summary>当前停在池里的容器数。</summary>
    public int Pooled { get; private set; }

    /// <summary>已被弃用的容器数（池满或非法转换）。</summary>
    public int DiscardedCount { get; private set; }

    /// <summary>同一个元素被连续 acquire 两次（Active→Active）。</summary>
    public int DoubleAcquire { get; private set; }

    /// <summary>同一个元素被回收两次，或回收了非 Active 的元素。</summary>
    public int DoubleRecycle { get; private set; }

    /// <summary>其它非法状态转换（例如已 Discarded 的元素又被取用）。</summary>
    public int InvalidTransition { get; private set; }

    /// <summary>回调里拿到了工厂从未见过的元素（native/managed 身份不一致的信号）。</summary>
    public int UnknownElement { get; private set; }

    /// <summary>违规总数：压测里必须为 0。</summary>
    public int Violations => DoubleAcquire + DoubleRecycle + InvalidTransition + UnknownElement;

    /// <summary>库存守恒：minted == active + pooled + discarded。</summary>
    public bool ConservationOk => Minted == Active + Pooled + DiscardedCount;

    /// <summary>复用率（命中池的比例）。</summary>
    public double ReuseRate => GetCalls == 0 ? 0 : (double)Reused / GetCalls;

    public string StatsSnapshot() =>
        $"get={GetCalls} recycle={RecycleCalls} minted={Minted} reused={Reused} " +
        $"active={Active} pooled={Pooled} discarded={DiscardedCount} " +
        $"violations={Violations}(da={DoubleAcquire} dr={DoubleRecycle} " +
        $"it={InvalidTransition} ue={UnknownElement}) " +
        $"conservation={(ConservationOk ? "OK" : "BROKEN")} reuseRate={ReuseRate:F3}";

    // ── 回调 ────────────────────────────────────────────────────────────────

    private UIElement OnGetElement(object? data, UIElement? parent)
    {
        var container = _pool.Count > 0 ? _pool.Pop() : _create();
        Acquire(container);

        // 顺序是硬要求：先绑定新数据，再恢复可见。
        // 反过来的话会存在「旧 Data + Visible」的一帧被渲染出去（GPT §④）。
        _bind(container, data, parent);
        container.Visibility = Visibility.Visible;
        return container;
    }

    private void OnRecycleElement(UIElement? element, UIElement? parent)
    {
        if (element is null)
        {
            UnknownElement++;
            return;
        }

        Release(element);
    }

    private void Acquire(UIElement container)
    {
        GetCalls++;

        if (!_lease.TryGetValue(container, out var state))
        {
            state = ElementLeaseState.New;
            _lease[container] = state;
            Minted++;
        }

        switch (state)
        {
            case ElementLeaseState.New:
                break;
            case ElementLeaseState.Pooled:
                Pooled--;
                Reused++;
                break;
            case ElementLeaseState.Active:
                // ItemsRepeater 在没回收的情况下又要了一次 → 双重拥有。
                DoubleAcquire++;
                InvalidTransition++;
                break;
            case ElementLeaseState.Discarded:
                // 已弃用的元素不该再被取用：要么池逻辑错，要么 native 侧绕过了我们。
                InvalidTransition++;
                break;
        }

        Active++;
        _lease[container] = ElementLeaseState.Active;
    }

    private void Release(UIElement element)
    {
        RecycleCalls++;

        if (!_lease.TryGetValue(element, out var state))
        {
            UnknownElement++;
            state = ElementLeaseState.Active;
        }

        if (state == ElementLeaseState.Active)
        {
            Active--;
        }
        else
        {
            DoubleRecycle++;
            InvalidTransition++;
        }

        if (CollapseOnRecycle)
        {
            // 不要在这里卸载 Reactor 状态：XAML 仍挂着这棵树，下次 GetElement 会原样复用它。
            // 折叠停放是必要的：repeater 不再布局它，但仍挂在树上，
            // 不折叠会以最后一次布局的矩形位置画出来 —— 列表上浮着一或多个"幽灵行"。
            element.Visibility = Visibility.Collapsed;
        }

        if (state == ElementLeaseState.Active && _pool.Count < _maxPool)
        {
            _pool.Push(element);
            Pooled++;
            _lease[element] = ElementLeaseState.Pooled;
            return;
        }

        // 池满，或这次回收本身就是非法的：容器就此成为永久孤儿（Discarded），
        // 再也不会从 GetElement 还回去。这是无界增长的来源，DiscardedCount 应当被监控。
        _lease[element] = ElementLeaseState.Discarded;
        DiscardedCount++;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _pool.Clear();
        _lease.Clear();
        _native.Dispose();
    }
}
