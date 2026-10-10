"""ToggleSwitch / RadioButton 真机回归：断言每次操作恰好产生一条用户回调。

这两个控件刚补过"Unmount 真解绑"，画廊里正好有带回调的场景
（ElementGallery 的"输入与选择"区），所以这里能真机闭环——
不像 TreeView / SettingsCard 那三处只能靠静态契约。

注意 RadioButton 的规矩与 RadioButtons 一样：<b>点已选中的项不产生事件</b>。
所以两个选项必须交替点，否则会拿到"合法的 0 条"被误读成 bug。
ToggleSwitch 每次点都翻转，不受这条限制。

判据：每发操作 → 恰好 1 条 `→ 用户回调`，且页面状态文本跟着变。

用法：
    python winapp_switches.py                 # 开关 3 发 + 单选交替 4 发
    python winapp_switches.py --expect 2      # 反向验证：应当判红、退出码 1
"""

import argparse
import os
import sys
import time

from _uia import (find_package, stop_app, sync_artifacts, set_mode, set_trace,
                  clear_trace, start_app, wait_window_gone, log_path)

from winapp_run import (ui, find_hwnd, find_slug, state_text, new_lines,
                        CALLBACK_MARK)

SEP = '-' * 62


def run():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', type=int, default=9, help='测试壳菜单下标，9 = 元素画廊')
    ap.add_argument('--rounds', type=int, default=2, help='开关点几发')
    ap.add_argument('--expect', type=int, default=1, help='每次操作期望的回调条数')
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
    set_trace(pkg)          # 闸门"吞掉"是 Trace 级，不开会误判成"没抛事件"

    logp = log_path(pkg)
    start_app(pf)

    hwnd = find_hwnd(timeout=40.0)
    if not hwnd:
        print('=== 40s 内没找到 App 窗口 ===')
        return 2
    print(f'window  = HWND {hwnd}')
    time.sleep(args.settle)

    failures = 0

    def fire(name, slug_key, state_prefix, mark, expect, expect_change=True,
             pair=False):
        """pair=True 时还要求回调里 True / False 各一条（见下方 RadioButton 的注释）。"""
        """操作一次并断言回调条数。"""
        nonlocal failures
        hwnd = find_hwnd(timeout=20.0)
        if not hwnd:
            print(f'=== 窗口没了（点 {name} 时）===')
            failures += 1
            return
        slug = find_slug(hwnd, slug_key)
        if not slug:
            print(f'=== 找不到 {name}（按 {slug_key!r} 搜）===')
            failures += 1
            return

        # 必须先滚进视口。对一个<b>屏外</b>控件发 TogglePattern，UIA 走的是
        # 直接改状态的路径，不是真实点击路由——实测那样拿到的用户回调是 0 条，
        # 而页面状态照样会变，极易被误读成"回调没触发"。
        # 滚一次不一定到位（页面可能还在建），所以滚到 IsOffscreen 为假为止。
        offscreen = True
        for attempt in range(4):
            ui('scroll-into-view', slug, '-w', hwnd)
            time.sleep(1.0)
            probe = ui('get-property', slug, '-w', hwnd, '-p', 'IsOffscreen')
            offscreen = 'IsOffscreen: True' in probe
            if not offscreen:
                break
        if offscreen:
            print(f'=== {name}：滚了 4 次仍在屏外，这一发不算数 ===')
            failures += 1
            return
        if attempt:
            print(f'        （{name} 滚了 {attempt + 1} 次才进视口）')

        before = state_text(hwnd, state_prefix)
        offset = os.path.getsize(logp) if os.path.exists(logp) else 0
        out = ui('invoke', slug, '-w', hwnd)
        if 'Invoked' not in out:
            print(f'FAIL  {name}：invoke 没成功 → {out[:120]!r}')
            failures += 1
            return
        time.sleep(2.0)
        after = state_text(hwnd, state_prefix)
        got = [l for l in new_lines(logp, offset, CALLBACK_MARK) if mark in l]

        ok = len(got) == expect
        if expect_change and before == after:
            ok = False

        note = ''
        if pair:
            has_true = any('=True' in l for l in got)
            has_false = any('=False' in l for l in got)
            if not (has_true and has_false):
                ok = False
                note = ' ← 缺 True/False 配平'
            else:
                note = ' [True+False 配平]'

        if not ok:
            failures += 1
        print(f'{"PASS" if ok else "FAIL"}  {name:12s}：回调 {len(got)} 条'
              f'（期望 {expect}）| {before!r} → {after!r}{note}')
        for line in got:
            print('        ', line)

    # ── ToggleSwitch：每发都翻转，不受"已选中"限制 ──
    for i in range(args.rounds):
        fire(f'开关 #{i + 1}', '开关', 'ToggleSwitch：', 'ToggleSwitch#',
             expect=args.expect)

    # ── RadioButton：必须交替点，点已选中项不产生事件 ──
    # 期望 2 条<b>且一 True 一 False</b>：同组两个按钮，一个让位（Unchecked）
    # 一个上位（Checked），各抛一次。这不是"回调翻倍"，是 WinUI 的正确行为——
    # 判成 1 条会把正常现象读成 bug，判成 2 条但不管符号又会放过真的翻倍。
    for name in ['选项 A', '选项 B', '选项 A', '选项 B']:
        # RadioButton 天然是开关的 2 倍（同组两个各抛一次），所以跟着
        # args.expect 一起缩放——这样 --expect 2 能把两条路径一起推向红。
        fire(name, name, '单选：', 'RadioButton#', expect=args.expect * 2,
             pair=args.expect == 1)

    clear_trace(pkg)
    print(SEP)
    print(f'=== verdict: {"PASS" if failures == 0 else f"FAIL ({failures})"} ===')
    return 0 if failures == 0 else 1


if __name__ == '__main__':
    sys.exit(run())
