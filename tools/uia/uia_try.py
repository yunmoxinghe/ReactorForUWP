"""一次性试探：能不能把 RadioButtons 滚进视口并选中它。跑通了就固化进 uia_run.py。"""

【已被 winapp ui 取代】
滚入视口 + 选中 → `winapp ui scroll-into-view` + `invoke`。本机两个 Python 都没装 comtypes，本脚本实际跑不起来，仅作留档。


import os
import sys
import time

from _uia import (
    UIA, automation, find_package, log_path, stop_app, start_app, set_mode,
    sync_artifacts, app_window, descendants, describe,
)

mode = int(sys.argv[1]) if len(sys.argv) > 1 else 9
target = sys.argv[2] if len(sys.argv) > 2 else '第二'

pkg, pf = find_package()
stop_app()
sync_artifacts()
set_mode(pkg, mode)
logp = log_path(pkg)
offset = os.path.getsize(logp) if os.path.exists(logp) else 0
print('package =', pf, '| log offset =', offset)

start_app(pf)
u = automation()
win = app_window(u, timeout=40.0)
print('window =', describe(win))

rb = None
for waited in range(0, 41, 2):
    hits = [e for e in descendants(u, win, control_type=UIA.UIA_RadioButtonControlTypeId)
            if e.CurrentName == target]
    if hits:
        rb = hits[0]
        break
    time.sleep(2)
if rb is None:
    sys.exit('=== 没找到目标 RadioButton ===')
print(f'找到（等 {waited}s）：', describe(rb))

# GetCurrentPattern 返回的是 IUnknown，QueryInterface 的返回值必须接住
def pattern(elem, pid, iface):
    return elem.GetCurrentPattern(pid).QueryInterface(iface)

try:
    pattern(rb, UIA.UIA_ScrollItemPatternId, UIA.IUIAutomationScrollItemPattern).ScrollIntoView()
    print('ScrollIntoView 调用成功')
    time.sleep(1.0)
except Exception as ex:
    print('ScrollIntoView 不可用：', type(ex).__name__, ex)
print('滚动后：', describe(rb))

try:
    pattern(rb, UIA.UIA_SelectionItemPatternId, UIA.IUIAutomationSelectionItemPattern).Select()
    print('Select 调用成功')
except Exception as ex:
    print('Select 失败：', type(ex).__name__, ex)

time.sleep(2.0)
print('--- 本轮新增日志 ---')
with open(logp, 'r', encoding='utf-8', errors='replace') as f:
    f.seek(offset)
    for line in f.readlines():
        print('   ', line.rstrip())
