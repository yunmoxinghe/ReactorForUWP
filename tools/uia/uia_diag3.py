"""卡死取证：点三次 → 第三次后不杀进程，量 CPU + 看窗口还活不活。

用法：python uia_diag3.py
"""

【已被 winapp_diag.py 取代】
IsSelected 走 `winapp ui get-property -p IsSelected`，不需要自己摸 UIA pattern。本机两个 Python 都没装 comtypes，本脚本实际跑不起来，仅作留档。

import argparse
import os
import subprocess
import sys
import time

from _uia import (
    UIA, automation, find_package, log_path, stop_app, start_app, set_mode,
    sync_artifacts, app_window, descendants, bring_to_front, mouse_click,
    set_trace,
)


def pattern(elem, pid, iface):
    """GetCurrentPattern 给的是 IUnknown，QueryInterface 的返回值必须接住。"""
    return elem.GetCurrentPattern(pid).QueryInterface(iface)


def is_selected(u, win, name):
    for e in descendants(u, win, control_type=UIA.UIA_RadioButtonControlTypeId):
        if e.CurrentName == name:
            try:
                p = pattern(e, UIA.UIA_SelectionItemPatternId,
                            UIA.IUIAutomationSelectionItemPattern)
                return p.CurrentIsSelected
            except Exception:
                return None
    return None


def snapshot(u, win, names):
    """全部选项的选中态 + 页面状态文本。UI 与内部状态脱钩时会出现"两项同时选中"。"""
    flags = []
    for n in names:
        try:
            flags.append(f'{n}={int(is_selected(u, win, n) or 0)}')
        except Exception:
            flags.append(f'{n}=?')
    txt = None
    for e in descendants(u, win, control_type=UIA.UIA_TextControlTypeId):
        t = e.CurrentName or ''
        if '采样：' in t:
            txt = t
        elif ('回调次数' in t or '单选：' in t or '事件：' in t) and txt is None:
            txt = t
    return ' '.join(flags) + (f' | {txt}' if txt else '')


def click(u, elem, win, via):
    bring_to_front(win)
    try:
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


def cpu_of(pid):
    """采样 1 秒 CPU。返回 (percent, 说明)。"""
    ps = (
        f"$p = Get-Process -Id {pid} -EA SilentlyContinue; "
        "if ($p) { $a=$p.TotalProcessorTime.TotalMilliseconds; Start-Sleep 1; "
        "$p.Refresh(); $b=$p.TotalProcessorTime.TotalMilliseconds; "
        "$n=[System.Diagnostics.Process]::GetCurrentProcess().NumberOfCores if(!$n){$n=$env:NUMBER_OF_PROCESSORS}; "
        "$n=[int]$env:NUMBER_OF_PROCESSORS; "
        "$cpu=[math]::Round(($b-$a)/10/$n,1); "
        "\"cpu=$cpu ws=$([math]::Round($p.WorkingSet64/1MB,0))MB threads=$($p.Threads.Count)\" } "
        "else { 'gone' }"
    )
    r = subprocess.run(['powershell', '-NoProfile', '-Command', ps],
                       capture_output=True, text=True, timeout=30)
    return (r.stdout or '').strip()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', type=int, default=9)
    ap.add_argument('--targets', default='第一,第三,第二')
    ap.add_argument('--via', choices=('mouse', 'uia'), default='mouse')
    args = ap.parse_args()

    pkg, pf = find_package()
    logp = log_path(pkg)
    stop_app()
    sync_artifacts()
    set_mode(pkg, args.mode)
    set_trace(pkg)
    print(f'package = {pf}')
    start_app(pf)

    u = automation()
    win = app_window(u, timeout=45)
    if win is None:
        root = u.GetRootElement()
        kids = root.FindAll(UIA.TreeScope_Children, u.CreateTrueCondition())
        print(f'=== 找不到窗口，顶层窗口 {kids.Length} 个：')
        for i in range(kids.Length):
            e = kids.GetElement(i)
            print('   -', repr(e.CurrentName)[:50], e.CurrentClassName, 'pid', e.CurrentProcessId)
        return 2
    pid = win.CurrentProcessId
    print(f'pid={pid}')

    time.sleep(6.0)
    # 确认页面真的换了：日志里 trace-switch 之后应该有本页的特征行
    with open(logp, 'r', encoding='utf-8', errors='replace') as f:
        head = [l.rstrip() for l in f.readlines()[-40:] if 'trace-switch' in l or '[raw]' in l]
    print(f'启动日志特征行 {len(head)} 条：')
    for l in head[:5]:
        print('   ', l)

    for i, name in enumerate(args.targets.split(','), 1):
        elems = [e for e in descendants(u, win, control_type=UIA.UIA_RadioButtonControlTypeId)
                 if e.CurrentName == name]
        if not elems:
            print(f'{i}. {name}: 找不到')
            continue
        before = snapshot(u, win, args.targets.split(','))
        off = os.path.getsize(logp) if os.path.exists(logp) else 0
        click(u, elems[0], win, args.via)
        time.sleep(2.5)
        after = snapshot(u, win, args.targets.split(','))
        with open(logp, 'r', encoding='utf-8', errors='replace') as f:
            f.seek(off)
            new = [l.rstrip() for l in f.readlines()]
        builds = sum(1 for l in new if 'build ' in l)
        print(f'{i}. 点 {name}')
        print(f'      前 {before}')
        print(f'      后 {after} | 新增日志 {len(new)} 条'
              f'（其中 build {builds} 条，非 build {len(new)-builds} 条）')
        for line in [l for l in new if 'build ' not in l][:80]:
            print('     ·', line)
        print('   ', cpu_of(pid))

    # 最后一次探活：UIA 还能不能做一次无关操作（读窗口名 + 找按钮点一下）
    print('--- 探活 ---')
    t0 = time.time()
    try:
        n = descendants(u, win, control_type=UIA.UIA_ButtonControlTypeId)
        print(f'读 UIA 树耗时 {time.time()-t0:.2f}s，按钮 {len(n)} 个')
    except Exception as ex:
        print(f'UIA 读树失败（UI 线程多半卡死）：{ex}')
    print('末态：', cpu_of(pid))
    print(f'进程留着不杀，pid={pid}，可手动抓栈')


if __name__ == '__main__':
    main()
