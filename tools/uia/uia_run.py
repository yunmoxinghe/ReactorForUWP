"""【已被 winapp_run.py 取代；保留作"自造 UIA 驱动"的对照实现】

winapp 0.7.0 自带 `winapp ui`（inspect / search / invoke / get-value /
wait-for / screenshot / record …），UIA 交互不再需要自造 comtypes 封装。
日常回归请跑 `python winapp_run.py`：不依赖 comtypes，且 invoke 走
SelectionItemPattern，不要求窗口在前台（模拟鼠标那条路曾被前台锁误导过）。

留着本文件，是因为"模拟鼠标 vs UIA Select"的 A/B 是靠它做的——
要复现那条对照时用得上。它依赖 comtypes，本机两个 Python 都没装，
跑之前先 `pip install comtypes`。

UIA 端到端回归：无人值守地点真控件，数"一次点击产生几次用户回调"。

为什么需要它：受控选中的 bug 住在"事件到达序列"里，仿真层（第 1 层）没有订阅概念，
只能验判据；真机以前全靠手点，一轮几分钟还抢焦点。这里让脚本来点、来数、来判红绿。

用法：
    python uia_run.py                      # 元素画廊，依次点 第一 / 第三 / 第二
    python uia_run.py --mode 16 --target 第二
    python uia_run.py --expect 2           # 故意反着来，验证脚本自己没瞎

判据：每一发点击，日志里新增的 "<控件> → 用户回调" 恰好 expect 条（默认 1）。
退出码 0 = 全绿，1 = 有红的，2 = 环境/操作失败。
"""

import argparse
import os
import sys
import time

from _uia import (
    automation, find_package, log_path, stop_app, start_app, set_mode,
    sync_artifacts, app_window, descendants, describe, bring_to_front, mouse_click,
    set_trace, clear_trace, uia_module,
)

# comtypes 的 UIA 绑定改成惰性取（见 _uia.uia_module）：
# 装了 comtypes 才用得上，没装时本文件仍能 import，不至于连 --help 都跑不起来。
UIA = None

CALLBACK_MARK = '→ 用户回调'


def pattern(elem, pid, iface):
    """GetCurrentPattern 给的是 IUnknown，QueryInterface 的返回值必须接住。"""
    return elem.GetCurrentPattern(pid).QueryInterface(iface)


def wait_control(u, win, name, timeout=40.0):
    end = time.time() + timeout
    while time.time() < end:
        hits = [e for e in descendants(u, win, control_type=UIA.UIA_RadioButtonControlTypeId)
                if e.CurrentName == name]
        if hits:
            return hits[0]
        time.sleep(1.0)
    return None


def click(u, elem, win, via, args_scroll='auto'):
    """滚进视口 → 选中。offscreen 的元素 rect 是 0x0，不滚就点不到。

    <c>via=uia</c> 走 SelectionItemPattern；<c>via=mouse</c> 走真实鼠标，更接近真人。
    两者都先 <see cref="bring_to_front"/>：UWP 在后台时 UIA 的 Select 静默无效。
    """
    bring_to_front(win)
    try:
        # A/B 用：--scroll always 时即使元素已在视口内也滚一次。
        # 观测到的现象是"元素已在视口内时点击不触发事件"，这一句就是为了证它。
        if args_scroll == 'always' or elem.CurrentIsOffscreen:
            pattern(elem, UIA.UIA_ScrollItemPatternId,
                    UIA.IUIAutomationScrollItemPattern).ScrollIntoView()
            time.sleep(0.6)
    except Exception:
        pass
    if elem.CurrentIsOffscreen:
        return False
    if via == 'mouse':
        mouse_click(elem.CurrentBoundingRectangle)
    else:
        pattern(elem, UIA.UIA_SelectionItemPatternId,
                UIA.IUIAutomationSelectionItemPattern).Select()
    return True


def new_lines(path, offset, keep=None):
    """读 offset 之后的日志行。keep 为空则全收（build 噪声单独过滤）。"""
    if not os.path.exists(path):
        return []
    with open(path, 'r', encoding='utf-8', errors='replace') as f:
        f.seek(offset)
        lines = [l.rstrip() for l in f.readlines()]
    if keep is None:
        return [l for l in lines if ' build ' not in l]
    return [l for l in lines if keep in l]


def is_selected(elem):
    try:
        return pattern(elem, UIA.UIA_SelectionItemPatternId,
                       UIA.IUIAutomationSelectionItemPattern).CurrentIsSelected
    except Exception:
        return None


def state_text(u, win, prefix='单选：'):
    """页面上"单选：N"那行的当前值——状态到底变没变，只有 UI 说了算。"""
    for e in descendants(u, win, control_type=UIA.UIA_TextControlTypeId):
        if (e.CurrentName or '').startswith(prefix):
            return e.CurrentName
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', type=int, default=9, help='测试壳菜单下标，9 = 元素画廊')
    # 默认连点五发。以前只能点两发——第三发起控件就再也不抛事件了
    # （RCW 那个 bug：事件委托拿回调给的 sender 去查按引用相等建的表，查不到就静默返回）。
    # 修好之后连点八发仍然每发都有回调，所以现在点五发是有意义的回归判据。
    ap.add_argument('--targets', default='第一,第二,第三,第一,第二',
                    help='依次点击的选项名，逗号分隔；要点未选中项才会产生事件')
    ap.add_argument('--expect', type=int, default=1, help='每次点击期望的回调条数')
    ap.add_argument('--scroll', choices=('auto', 'always'), default='auto',
                    help='auto = 只在 offscreen 时滚；always = 每次都滚（A/B 用）')
    # 默认走 UIA 的 Select：<b>不依赖窗口在前台</b>。
    # mouse 更接近真人，但它要求窗口真在前台；UWP 由脚本 SetForegroundWindow
    # 时好时坏（前台锁），失败表现为"点了没反应"，那是量具坏不是 App 坏——
    # 排查时曾被它误导过一轮。要测输入全链路再显式加 --via mouse。
    ap.add_argument('--via', choices=('uia', 'mouse'), default='uia',
                    help='uia = SelectionItemPattern.Select（默认）；mouse = 真实鼠标点击')
    ap.add_argument('--fallback', action='store_true',
                    help='本发没回调时换另一种驱动方式再点一次（mouse ⇄ uia）')
    ap.add_argument('--state-prefix', default='单选：',
                    help='页面上显示状态的 TextBlock 前缀（原生对照页用 回调次数）')
    ap.add_argument('--verbose', action='store_true', help='失败时把本轮全部新增日志打出来')
    ap.add_argument('--settle', type=float, default=4.0,
                    help='启动后静置秒数：躲开"首帧受控下发"那条回声，它不是点击产生的')
    args = ap.parse_args()

    global UIA
    UIA = uia_module()

    pkg, pf = find_package()
    if not pkg:
        print('=== 定位不到包目录：先手动部署/启动一次 App ===')
        return 2
    print(f'package = {pf}')

    stop_app()
    sync_artifacts()
    set_mode(pkg, args.mode)

    # 闸门的"吞掉"是 Trace 级、默认不落盘。不开它，"点了没反应"在日志里就是
    # "一条都没有"，极易误判成"WinUI 根本没抛事件"——这次排查被它绕了一大圈。
    # 脚本自己开，别靠手。
    set_trace(pkg)

    logp = log_path(pkg)
    start_app(pf)

    u = automation()
    win = app_window(u, timeout=40.0)
    if win is None:
        print('=== 40s 内没找到 App 窗口 ===')
        return 2
    print('window =', describe(win))

    time.sleep(args.settle)   # 首帧的受控下发会漏一条回声，等它过去再开始数

    failures = 0
    for name in [t for t in args.targets.split(',') if t]:
        elem = wait_control(u, win, name)
        if elem is None:
            print(f'=== 找不到选项 {name!r} ===')
            failures += 1
            continue

        before_text = state_text(u, win, args.state_prefix)
        before_sel = is_selected(elem)
        rect = elem.CurrentBoundingRectangle
        offset = os.path.getsize(logp) if os.path.exists(logp) else 0
        if not click(u, elem, win, args.via, args.scroll):
            print(f'=== {name}: 滚进视口后仍 offscreen，点不了 ===')
            failures += 1
            continue

        time.sleep(2.0)
        # 重新查一次：点之前那个引用可能是已被卸载的旧控件（stale 元素会返回缓存值）
        fresh = wait_control(u, win, name, timeout=3.0)
        sel = is_selected(fresh) if fresh else None
        after_text = state_text(u, win, args.state_prefix)
        stale_note = '' if fresh else f' ← 点后树里已无该选项（控件被换掉了）'
        got = new_lines(logp, offset, CALLBACK_MARK)
        ok = len(got) == args.expect
        wr = win.CurrentBoundingRectangle
        print(f'{"PASS" if ok else "FAIL"}  点 {name}：回调 {len(got)} 条（期望 {args.expect}）'
              f' | IsSelected {before_sel}→{sel} | 状态 {before_text!r} → {after_text!r}'
              f'{stale_note}'
              f' | rect {rect.left:.0f},{rect.top:.0f} in win '
              f'{wr.left:.0f},{wr.top:.0f}-{wr.right:.0f},{wr.bottom:.0f}')
        for line in got:
            print('        ', line)
        if not ok and args.fallback:
            # 换另一种驱动方式再点一次：mouse 不行就试 UIA 的 Select，反之亦然。
            # 用来区分"控件整个卡死"和"某一条驱动路径失效"。
            other = 'uia' if args.via == 'mouse' else 'mouse'
            offset2 = os.path.getsize(logp) if os.path.exists(logp) else 0
            click(u, elem, win, other, args.scroll)
            time.sleep(2.0)
            got2 = new_lines(logp, offset2, CALLBACK_MARK)
            print(f'        ↆ 改用 {other} 重试：回调 {len(got2)} 条，状态 {state_text(u, win, args.state_prefix)!r}')

        if not ok:
            failures += 1
        if args.verbose:
            for line in new_lines(logp, offset):
                print('        ·', line)
            snap = [(e.CurrentName, is_selected(e), e.CurrentIsOffscreen)
                    for e in descendants(u, win, control_type=UIA.UIA_RadioButtonControlTypeId)]
            print('        快照：', snap)

    # 收尾关掉 Trace：只在本次无人值守运行期间开。
    # 留着文件的话，下次人手启动 App 也会降到 Trace 落盘，日志会淹掉要看的那几行。
    clear_trace(pkg)

    print(f'=== verdict: {"PASS" if failures == 0 else f"FAIL ({failures})"} ===')
    return 0 if failures == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
