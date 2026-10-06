using System;
using Reactor.Uwp.Internal;

namespace Reactor.Core.Tests;

/// <summary>
/// 「写了一个<b>不是受控值</b>的属性，控件却把受控值改了」的行为模型。
/// </summary>
/// <remarks>
/// <para>
/// 真身有两类，形状完全一样：
/// <list type="bullet">
///   <item>改 <c>SelectionMode</c>（<c>ListViewBase_Partial.cpp</c> 的
///         <c>ListViewBase_SelectionMode</c> 分支 → <c>OnSelectionModeChanged</c>，
///         注释原文 <c>will update all Selection related properties</c>）；</item>
///   <item>重建 <c>MenuItems</c>（<c>NavigationView.cpp</c> 的
///         <c>OnSelectionModelSelectionChanged</c>：选中容器被清空后
///         <c>selectedItem != SelectedItem()</c>，那条早退不成立，事件照抛）。</item>
/// </list>
/// 共同点是：<b>作者是我们，但那一发事件抛在旧订阅还挂着的时候</b>
/// （<c>Rebind</c> 在 <c>Update</c> 最后才换回调）。
/// </para>
/// <para>
/// 用的都是 Link 进来的<b>真家伙</b>：<c>EchoGuard</c>（含 <c>Silence</c>）与
/// <c>SelectionPolicy</c>。模型不复制判据，只复制<b>次序</b>。
/// </para>
/// </remarks>
internal sealed class SiblingWriteSim
{
    /// <summary>
    /// 开关 A：写会牵动受控值的兄弟属性时开<b>静默窗</b>。
    /// 关掉它应当只放出「假回调」这一类，不该动「丢选中」那一类。
    /// </summary>
    public bool SilenceOnSiblingWrite = true;

    /// <summary>
    /// 开关 B：items 重建之后<b>重新下发</b>受控选中值。
    /// 关掉它应当只放出「丢选中」这一类，不该动「假回调」那一类。
    /// </summary>
    public bool ReapplyAfterRebuild = true;

    /// <summary>
    /// 无关对照：控件到底会不会因为兄弟属性被写而改选中。
    /// 关掉它，两类问题都该消失 —— 否则说明病因认错了。
    /// </summary>
    public bool ControlMovesSelectionOnSiblingWrite = true;

    /// <summary>不是用户动的，却被当成用户输入回调出去的次数。</summary>
    public int Spurious;

    /// <summary>一轮渲染结束后，控件选中值与声明值<b>不一致</b>的帧数（选中丢了）。</summary>
    public int Lost;

    /// <summary>控件自己动过选中值的次数（证明上面两条判据不是空转）。</summary>
    public int Moved;

    private readonly EchoGuard _echo = new();
    private readonly object _control = new();

    private int _count;
    private int _selected = -1;
    private int _state = -1;
    private int _sibling;
    private Action<int>? _callback;

    /// <summary>控件当前的选中下标。</summary>
    public int Selected => _selected;

    /// <summary>state（声明值）当前是多少。</summary>
    public int State => _state;

    /// <summary>挂载：订阅先挂上，再下发受控值（真 handler 就是这个次序）。</summary>
    public void Mount(int count, int index, Action<int>? callback)
    {
        _count = count;
        _selected = -1;
        _state = index;
        _callback = callback;
        Apply(index);
    }

    /// <summary>
    /// 一轮渲染。
    /// </summary>
    /// <param name="count">items 数量（变了就是重建）。</param>
    /// <param name="sibling">那个"不是受控值"的兄弟属性（变了控件就可能自己动选中）。</param>
    /// <param name="index">声明的受控选中值。</param>
    public void Update(int count, int sibling, int index, Action<int>? callback)
    {
        var rebuilt = count != _count;

        if (sibling != _sibling)
        {
            _sibling = sibling;

            if (SilenceOnSiblingWrite)
            {
                using (_echo.Silence(_control))
                {
                    SiblingSideEffect();
                }
            }
            else
            {
                SiblingSideEffect();
            }
        }

        if (rebuilt)
        {
            _count = count;

            if (SilenceOnSiblingWrite)
            {
                using (_echo.Silence(_control))
                {
                    RebuildSideEffect();
                }
            }
            else
            {
                RebuildSideEffect();
            }
        }

        _state = index;

        // 开关 B 就在这一行。items 重建把选中清空之后，声明值往往压根没变，
        // 于是"只按声明值变了才写"的判据永远不会补这一笔 —— 选中就此丢了。
        if (!rebuilt || ReapplyAfterRebuild)
        {
            Apply(_state);
        }

        _callback = callback;

        // 声明值越界时控件兑现不了（那是调用方的时序，不是本条线要管的事），
        // 与"选中被清空后没补回来"区分开，否则两类问题会混成一个数。
        if (_selected != _state && !SelectionPolicy.IsOutOfRange(_count, _state))
        {
            Lost++;
        }
    }

    /// <summary>用户点了一下：这一发<b>必须</b>回调出去，不算假回调。</summary>
    public void UserClick(int index)
    {
        if (index < 0 || index >= _count)
        {
            return;
        }

        SetSelectedCore(index, fromUser: true);
    }

    /// <summary>
    /// 兄弟属性被写之后控件的反应：<b>值只有控件自己知道</b>——
    /// 这正是 <c>Expect</c> 装不下、只能开窗的那一类。
    /// </summary>
    private void SiblingSideEffect()
    {
        if (!ControlMovesSelectionOnSiblingWrite)
        {
            return;
        }

        // 三档，对应 SelectionMode 的三种真实形状：
        //   0 = Multiple：多选，已有选中不动（这就是"无关对照"要的那档）；
        //   1 = None：清空，选中变 -1；
        //   2 = Single：只留第一个，选中从 2 收敛到 0。
        // 后两档的关键是<b>收敛成什么只有控件知道</b>——这正是 Expect 装不下、
        // 只能开窗的那一类。
        var next = _sibling switch
        {
            1 => -1,
            2 => _selected > 0 ? 0 : _selected,
            _ => _selected,
        };

        SetSelectedCore(next);
    }

    /// <summary>items 被 Clear + 重建：选中容器没了，选中值塌成 -1。</summary>
    private void RebuildSideEffect()
    {
        if (!ControlMovesSelectionOnSiblingWrite)
        {
            return;
        }

        SetSelectedCore(_selected > 0 ? 0 : _selected);
        SetSelectedCore(-1);
    }

    private void Apply(int index)
    {
        if (!SelectionPolicy.ShouldApply(_count, _selected, index))
        {
            return;
        }

        _echo.Expect(_control, index);
        SetSelectedCore(index);
        _echo.CancelIfUnconsumed(_control);
    }

    private void SetSelectedCore(int index, bool fromUser = false)
    {
        if (_selected == index)
        {
            return;
        }

        _selected = index;
        Moved++;

        // 事件抛给<b>此刻挂着的订阅</b>——真代码里它是上一轮那个（Rebind 在最后）。
        var value = _selected;

        if (_echo.Consume(_control, value))
        {
            return;
        }

        // 用户自己点的：回调出去是对的，不算在这一条线上。
        if (fromUser)
        {
            return;
        }

        Spurious++;

        // 回调跑出去 = setState：state 被这一发凭空改掉。
        _state = value;
    }
}
