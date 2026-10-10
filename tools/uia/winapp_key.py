"""正交验证：某条路径"点了没反应"之后，键盘还能不能驱动同一个控件。

区分两种病（修法完全不同，别混为一谈）：
  A. 控件内部状态整个卡死 —— invoke 和键盘两条路都不抛事件
  B. 只有某一条输入路径坏了 —— 另一条仍能驱动

做法（每段都先保证"目标不是当前选中项"，否则拿到的 0 条是假的）：
  · 走自动驾驶：用 invoke 依次选"当前未选中"的选项，数回调
  · 走 OS 输入队列：不重新聚焦，直接按方向键，数回调
最后把两条路的结果并排打表——只要 invoke 哑了而键盘还活着，就能立刻判定是 B。

两个已经踩过的坑，写在显眼处以免重犯：
  1. invoke 一个<b>已选中</b>的项，WinUI 会正确地什么都不抛。脚本若不避开，
     那一发就是"假的 0 条"，会被误读成 bug。→ 每次都先读状态，只点未选中项。
  2. `send-keys --target <slug>` 会先把焦点给过去，而对 RadioButtons
     来说聚焦本身可能就改一次选中，再按方向键又改一次，于是拿到 2 条。
     → 键盘段只在开头聚焦一次，之后纯按键。
  3. RadioButtons 的方向键<b>不绕回</b>（实测：在末项上按 down，连按 4 次
     状态纹丝不动，命令本身回 ✅）。所以走到边界的那一发同样是合法的 0 条。
     → 键盘段按当前下标自动换方向：还走得动就按主方向，到边界就掉头。

迁移说明：原 uia_key.py 用 comtypes + user32.keybd_event，本机两个 Python 都没装
comtypes，跑不起来。现在键盘走 `winapp ui send-keys`：UWP/XAML 是 windowless 的，
post-message 会被 XAML 忽略，**必须带 --via send-input**（winapp 自己的帮助里也
写了这条），否则"按了没反应"是量具坏、不是 App 坏。

用法：
    python winapp_key.py                    # invoke 3 发 + 键盘 3 发
    python winapp_key.py --rounds 2
    python winapp_key.py --key right
    python winapp_key.py --expect 2         # 反向验证：应当判红、退出码 1
"""

import argparse
import os
import re
import sys
import time

from _uia import (find_package, stop_app, sync_artifacts, set_mode, set_trace,
                  clear_trace, start_app, wait_window_gone, log_path)

from winapp_run import (ui, find_hwnd, find_slug, state_text, new_lines,
                        callbacks_of)

SEP = '-' * 62


def current_index(hwnd, prefix='单选：'):
    """读页面状态文本里的当前下标；读不到返回 None。"""
    text = state_text(hwnd, prefix)
    if not text:
        return None
    m = re.search(r'(\d+)\s*$', text)
    return int(m.group(1)) if m else None


def pick_not_current(names, idx):
    """挑一个"当前未选中"的目标。这是本脚本唯一可靠的取法。"""
    for i, name in enumerate(names):
        if idx is None or i != idx:
            return i, name
    return 0, names[0]


def send_keys(hwnd, keys, target=None):
    """发一次键盘输入。UWP/XAML 必须 --via send-input，见文件头注释。"""
    cmd = ['send-keys', keys, '-w', hwnd, '--via', 'send-input']
    if target:
        cmd += ['--target', target]
    return ui(*cmd)


def run():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', type=int, default=9, help='测试壳菜单下标，9 = 元素画廊')
    ap.add_argument('--targets', default='第一,第二,第三',
                    help='选项名，逗号分隔；下标按此顺序对应状态里的数字')
    ap.add_argument('--expect', type=int, default=1, help='每次操作期望的回调条数')
    ap.add_argument('--rounds', type=int, default=1, help='整组跑几轮')
    ap.add_argument('--key', default='down', help='键盘段按下的键（down/up/left/right）')
    ap.add_argument('--settle', type=float, default=4.0, help='启动后静置秒数')
    args = ap.parse_args()

    pkg, pf = find_package()
    if not pkg:
        print('=== 定位不到包目录：先手动部署/启动一次 App ===')
        return 2
    print(f'package = {pf}')

    stop_app()
    wait_window_gone()
    sync_artifacts()
    set_mode(pkg, args.mode)
    set_trace(pkg)          # 闸门的"吞掉"是 Trace 级，不开会误判成"没抛事件"

    logp = log_path(pkg)
    start_app(pf)

    hwnd = find_hwnd(timeout=40.0)
    if not hwnd:
        print('=== 40s 内没找到 App 窗口 ===')
        return 2
    print(f'window  = HWND {hwnd}')

    time.sleep(args.settle)

    names = [t for t in args.targets.split(',') if t]
    rows = []
    failures = 0

    def record(tag, label, name, got, before, after, ok_name_ok=True):
        """记一行结果。回调数按 >= 判定：键盘一路构图 treeshot 可能一次给两条。"""
        nonlocal failures
        ok = len(got) >= args.expect and ok_name_ok
        if not ok:
            failures += 1
        rows.append((tag, label, name, len(got), f'{before!r}→{after!r}',
                     'ok' if ok else 'NG'))
        print(f'{"PASS" if ok else "FAIL"}  {label:14s} {name:8s}：'
              f'回调 {len(got)} 条（期望 ≥{args.expect}）| {before!r} → {after!r}')

    def press_and_count(tag, label, name, keys, target=None):
        hwnd = find_hwnd(timeout=20.0)
        before = current_index(hwnd)
        offset = os.path.getsize(logp) if os.path.exists(logp) else 0
        out = send_keys(hwnd, keys, target=target)
        time.sleep(2.0)
        got = callbacks_of(logp, offset)
        after = current_index(hwnd)
        if not out:
            print('        ← 命令无输出：先怀疑量具没生效，别急着判 App 坏')
        record(tag, label, name, got, before, after)

    # 键盘段只在开头聚焦一次：之后纯按键，否则聚焦本身会算进回调（见文件头坑 2）。
    for rnd in range(1, args.rounds + 1):
        # ── 自动驾驶段 ──
        for i in range(len(names)):
            hwnd = find_hwnd(timeout=20.0)
            idx = current_index(hwnd)
            pos, name = pick_not_current(names, idx)
            slug = find_slug(hwnd, name)
            if not slug:
                print(f'=== 找不到选项 {name!r} ===')
                failures += 1
                continue
            before = idx
            offset = os.path.getsize(logp) if os.path.exists(logp) else 0
            invoked = ui('invoke', slug, '-w', hwnd)
            if 'Invoked' not in invoked:
                print(f'FAIL  invoke        {name}：invoke 没成功 → {invoked[:100]!r}')
                failures += 1
                continue
            time.sleep(2.0)
            got = callbacks_of(logp, offset)
            record(f'{rnd}.invoke{i}', 'invoke', name, got, before, current_index(hwnd))

        # ── 键盘段 ──
        last = len(names) - 1
        for i in range(len(names)):
            # 第一次带 target 把焦点给到控件；之后不再重复聚焦（坑 2）。
            fresh = find_hwnd(timeout=20.0)
            target = find_slug(fresh, names[0]) if i == 0 else None
            # 到边界就掉头：绕不回去的方向键按下去是合法的 no-op（坑 3）。
            idx = current_index(fresh)
            key = args.key
            if idx is not None:
                if idx >= last and key in ('down', 'right'):
                    key = 'up' if key == 'down' else 'left'
                elif idx <= 0 and key in ('up', 'left'):
                    key = 'down' if key == 'up' else 'right'
            press_and_count(f'{rnd}.key{i}', f'键盘({key})', names[i],
                            key, target=target)

    print(SEP)
    print(f'{"#":12s}{"路径":16s}{"目标":8s}{"回调":>4s}  {"下标变化":16s}  判定')
    for tag, label, name, n, delta, verdict in rows:
        print(f'{tag:12s}{label:16s}{name:8s}{n:>4d}  {delta:16s}  {verdict}')

    by_path = {}
    for _, label, _, n, _, _ in rows:
        by_path.setdefault(label, []).append(n)
    print(SEP)
    for label, counts in sorted(by_path.items()):
        alive = all(c > 0 for c in counts)
        print(f'{label:16s} 各发回调 = {counts}   → {"活着" if alive else "哑了"}')

    clear_trace(pkg)
    print(f'=== verdict: {"PASS" if failures == 0 else f"FAIL ({failures})"} ===')
    return 0 if failures == 0 else 1


if __name__ == '__main__':
    sys.exit(run())
