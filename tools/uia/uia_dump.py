"""探路：启动 App → dump UIA 树里关心的控件。用来确认"UIA 能不能摸到它"，不做断言。

用法：python uia_dump.py [mode] [过滤关键字]
"""

【已被 winapp ui 取代】
树遍历这一块直接 `winapp ui inspect -w <hwnd> -i` 即可，不必自造。本机两个 Python 都没装 comtypes，本脚本实际跑不起来，仅作留档。


import os
import sys
import time

from _uia import (
    UIA, automation, find_package, log_path, stop_app, start_app, set_mode,
    sync_artifacts, app_window, descendants, describe,
)

mode = int(sys.argv[1]) if len(sys.argv) > 1 else 9
keyword = sys.argv[2] if len(sys.argv) > 2 else ''

pkg, pf = find_package()
if not pkg:
    sys.exit('定位不到包目录')
print('package =', pf)

stop_app()          # 先杀：App 活着时 AppX 里的 dll 被占用，拷不进去
sync_artifacts()
set_mode(pkg, mode)
print('mode =', mode)
start_app(pf)

u = automation()
win = app_window(u, timeout=40.0)
if win is None:
    sys.exit('=== 40s 内没找到 App 窗口 ===')
print('window =', describe(win))

# 等首帧：子树从空到有内容要几秒，别一上来就判"摸不到"
true_cond = u.CreateTrueCondition()
try:
    raw_cond = u.RawViewCondition
except Exception:
    raw_cond = true_cond

# 等 XAML 树真的出现：框架自己就有 7 个元素，用">3"当判据会立刻误判为"已就绪"
for waited in range(0, 41, 2):
    allkids = win.FindAll(UIA.TreeScope_Descendants, true_cond)
    if descendants(u, win, control_type=UIA.UIA_RadioButtonControlTypeId):
        break
    time.sleep(2)
print(f'ControlView 后代 = {allkids.Length}（等了 {waited}s）')
rawkids = win.FindAll(UIA.TreeScope_Descendants, raw_cond)
print(f'RawView   后代 = {rawkids.Length}')

print('--- 窗口直接子节点 ---')
kids = win.FindAll(UIA.TreeScope_Children, true_cond)
for i in range(min(kids.Length, 12)):
    print('   ', describe(kids.GetElement(i)))

print('--- 按 ControlType 统计（前 12 层） ---')
counts = {}
for i in range(min(allkids.Length, 400)):
    e = allkids.GetElement(i)
    counts[e.CurrentControlType] = counts.get(e.CurrentControlType, 0) + 1
for ct, n in sorted(counts.items(), key=lambda kv: -kv[1])[:12]:
    print(f'    ct={ct}: {n}')

for ct, label in ((UIA.UIA_RadioButtonControlTypeId, 'RadioButton'),
                  (UIA.UIA_TextControlTypeId, 'Text')):
    items = descendants(u, win, control_type=ct)
    print(f'--- {label}: {len(items)} 个')
    for e in items:
        if keyword and keyword not in (e.CurrentName or ''):
            continue
        print('   ', describe(e))

print('--- 日志尾部 ---')
p = log_path(pkg)
if os.path.exists(p):
    with open(p, 'r', encoding='utf-8', errors='replace') as f:
        for line in f.readlines()[-40:]:
            print('   ', line.rstrip())
