"""正交验证：鼠标点了没反应之后，键盘还能不能驱动同一个控件。

用来区分两件事：
  A. 整个 RadioButtons 的内部状态卡死（鼠标、键盘都不抛事件）
  B. 只有"鼠标点击"这条路径坏了（键盘仍能驱动）

点"第一"（应当成功）→ 点"第二"（已知会失败）→ 按方向键，看有没有回调。
"""

【已被 winapp_key.py 取代】
键盘走 `winapp ui send-keys --via send-input`（XAML 是 windowless 的，post-message 到不了控件）。本机两个 Python 都没装 comtypes，本脚本实际跑不起来，仅作留档。


import ctypes
import os
import sys
import time

from _uia import (
    UIA, automation, find_package, log_path, stop_app, start_app, set_mode,
    sync_artifacts, app_window, descendants, describe, bring_to_front, mouse_click,
)

VK_DOWN = 0x28
KEYEVENTF_KEYUP = 0x0002

_user32 = ctypes.WinDLL('user32', use_last_error=True)


def key(vk):
    _user32.keybd_event(vk, 0, 0, 0)
    time.sleep(0.05)
    _user32.keybd_event(vk, 0, KEYEVENTF_KEYUP, 0)


def pattern(elem, pid, iface):
    return elem.GetCurrentPattern(pid).QueryInterface(iface)


def state_text(u, win):
    for e in descendants(u, win, control_type=UIA.UIA_TextControlTypeId):
        if (e.CurrentName or '').startswith('单选：'):
            return e.CurrentName
    return None


def find(u, win, name, timeout=20.0):
    end = time.time() + timeout
    while time.time() < end:
        hits = [e for e in descendants(u, win, control_type=UIA.UIA_RadioButtonControlTypeId)
                if e.CurrentName == name]
        if hits:
            return hits[0]
        time.sleep(0.5)
    return None


def click_elem(elem, win):
    bring_to_front(win)
    try:
        pattern(elem, UIA.UIA_ScrollItemPatternId,
                UIA.IUIAutomationScrollItemPattern).ScrollIntoView()
        time.sleep(0.6)
    except Exception:
        pass
    mouse_click(elem.CurrentBoundingRectangle)


pkg, pf = find_package()
stop_app()
sync_artifacts()
set_mode(pkg, 9)
logp = log_path(pkg)
start_app(pf)
u = automation()
win = app_window(u, timeout=40.0)
print('window =', describe(win))
time.sleep(4.0)

for name in ('第一', '第二'):
    elem = find(u, win, name)
    off = os.path.getsize(logp) if os.path.exists(logp) else 0
    click_elem(elem, win)
    time.sleep(2.0)
    with open(logp, 'r', encoding='utf-8', errors='replace') as f:
        f.seek(off)
        new = [l.rstrip() for l in f.readlines() if '用户回调' in l or '/TRC' in l]
    print(f'鼠标点 {name}: 状态 {state_text(u, win)!r} | 回调 {len([l for l in new if "用户回调" in l])} 条')
    for l in new:
        print('    ', l)

print('--- 改成键盘 ---')
for i in range(3):
    off = os.path.getsize(logp) if os.path.exists(logp) else 0
    key(VK_DOWN)
    time.sleep(1.5)
    with open(logp, 'r', encoding='utf-8', errors='replace') as f:
        f.seek(off)
        new = [l.rstrip() for l in f.readlines() if '用户回调' in l or '/TRC' in l]
    print(f'按 Down #{i + 1}: 状态 {state_text(u, win)!r} | 回调 {len([l for l in new if "用户回调" in l])} 条')
    for l in new:
        print('    ', l)
