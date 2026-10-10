"""UwpApp 里「Blank 模板页」（mode 12）的导航回归：点"设置"进不进得去，点"主页"回不回得来。

<b>名字里的"模板页"指的是 UwpApp 测试壳里的那一页，不是 samples/Reactor.Template
那个独立的模板 app。</b>这两个都叫"模板"，说错一次就查错一个工程（踩过）。
samples 那个是独立 UWP 包、走 PackageReference 吃 nuget 上的包；这一页是 UwpApp
里的一个用例，吃的是本仓库源码。要验"包里的修复有没有到用户手上"，得看前者。

为什么单给这一页开一个脚本：它跟画廊里那些计数页的差别在——<b>可观测结果不是
计数器，是整页内容换掉</b>。所以判据用"设置页独有的文本有没有出现"（设置页有
外观 / 声音 / 关于 三个分组标题，主页有"欢迎来到主页……"），而不是回调条数。

用法：
    python winapp_blankpage.py                 # 主页 → 设置 → 主页，各一次
    python winapp_blankpage.py --rounds 2      # 来回多走几趟（看会不会越走越坏）
    python winapp_blankpage.py --via click     # 强制走鼠标点击（要矩形，屏外点不中）
    python winapp_blankpage.py --via invoke    # 走 InvokePattern（不依赖矩形）
"""

import argparse
import json
import os
import re
import subprocess
import sys
import time

from _uia import (find_package, stop_app, sync_artifacts, set_mode, set_trace,
                  clear_trace, start_app, wait_window_gone, log_path)

from winapp_run import ui, find_hwnd

SEP = '-' * 62

HOME_TEXT = '欢迎来到主页'
SETTINGS_TEXT = '外观'          # 设置页第一个分组标题，主页没有

# Windowless XAML 的"设置"项在 UIA 里可能是 TabItem（Top 面板）也可能是
# ListItem（Left 面板），取决于上次存下来的"导航位置"。两种都认。
SETTINGS_TYPES = ('TabItem', 'ListItem')


ROW_RE = re.compile(
    r'^\s*(?P<slug>[a-z0-9-]+(?:-[0-9a-f]+)?)\s+(?P<type>\S+)\s+"(?P<name>[^"]*)"'
    r'(?:\s+\((?P<x>-?\d+),(?P<y>-?\d+)\s+(?P<w>\d+)x(?P<h>\d+)\))?',
    re.M)


def search(hwnd, text):
    """返回 [(slug, type, name, 矩形描述)]。

    <b>名字要精确相等</b>：search 是子串匹配，搜"设置"会把测试壳菜单里的
    "设置页复现"也捞进来——第一次跑就是点了它，看着 PASS，其实点的是另一个页面的
    菜单项（与 RadioButton/RadioButtons 那次串味同一个坑）。
    """
    out = ui('search', text, '-w', hwnd)
    rows = []
    for m in ROW_RE.finditer(out):
        if m.group('name') != text:
            continue
        rect = (f'{m.group("x")},{m.group("y")} {m.group("w")}x{m.group("h")}'
                if m.group('x') else '无矩形')
        rows.append((m.group('slug'), m.group('type'), m.group('name'), rect))
    return rows


def visible(hwnd, text):
    """页面上"看得见"这个文本吗：命中且矩形不是 0x0（屏外元素的矩形是 0）。"""
    return any(row[3] != '无矩形' and not row[3].endswith(' 0x0')
               for row in search(hwnd, text))


def exists(hwnd, text):
    return bool(search(hwnd, text))


def pick(hwnd, text, types):
    """在命中里挑一个指定 UIA 类型的，返回 (slug, 描述)。"""
    for slug, ctype, name, rect in search(hwnd, text):
        if ctype in types:
            return slug, f'{ctype} {rect}'
    return None, None


def drive(hwnd, slug, via):
    return ui(via, slug, '-w', hwnd)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', type=int, default=12, help='测试壳下标，12 = Blank 模板页')
    ap.add_argument('--rounds', type=int, default=1, help='来回走几趟')
    ap.add_argument('--via', default='auto', choices=('auto', 'click', 'invoke'),
                    help='auto = 先 invoke，invoke 不支持再退回鼠标 click')
    ap.add_argument('--settle', type=float, default=5.0, help='启动后静置秒数')
    ap.add_argument('--pause', type=float, default=2.0, help='每发之后的静置秒数')
    args = ap.parse_args()

    pkg, pf = find_package()
    if not pkg:
        print('=== 定位不到包目录：先手动部署/启动一次 App ===')
        return 2

    stop_app()
    wait_window_gone()
    sync_artifacts()
    set_mode(pkg, args.mode)
    set_trace(pkg)
    start_app(pf)

    # 可用性判据不是"窗口在"，而是"页面那行字在"：窗口标题会被 ApplicationFrameHost
    # 复用，光看 HWND 会连到上一轮的残骸上。
    hwnd = None
    deadline = time.time() + 60.0
    while time.time() < deadline:
        hwnd = find_hwnd(timeout=20.0)
        if hwnd and exists(hwnd, HOME_TEXT):
            break
        time.sleep(0.5)
    if not hwnd:
        print('=== 60s 内没拿到可用的模板页窗口 ===')
        return 2
    print(f'window  = HWND {hwnd}')
    time.sleep(args.settle)

    fails = []
    for r in range(args.rounds):
        print(SEP)
        print(f'第 {r + 1} 趟')
        for target, expect, label in (('设置', SETTINGS_TEXT, '进设置页'),
                                      ('主页', HOME_TEXT, '回主页')):
            hwnd = find_hwnd(timeout=20.0) or hwnd

            before = exists(hwnd, expect)
            slug, desc = pick(hwnd, target, SETTINGS_TYPES)
            if not slug:
                # 找不到目标也要说清楚为什么：把名字相同的所有命中都打出来。
                rows = search(hwnd, target)
                allrows = [f'{t}:{n}({r})' for _, t, n, r in rows]
                print(f'  ✗ {label}：找不到 {target!r}（精确同名命中 {len(rows)} 个：{allrows}）')
                fails.append(f'{label}: 找不到 {target}')
                continue

            out = (drive(hwnd, slug, 'invoke') if args.via == 'auto'
                   else drive(hwnd, slug, args.via))
            if 'does not support' in out and args.via == 'auto':
                out = drive(hwnd, slug, 'click')
            time.sleep(args.pause)

            hwnd = find_hwnd(timeout=20.0) or hwnd
            after = visible(hwnd, expect)
            ok = after
            print(f'  {"PASS" if ok else "FAIL"} {label}：{target!r} {desc}'
                  f' → {expect!r} {"在" if after else "不在"}'
                  f'（点之前在树上={before}）')
            if not ok:
                print(f'       驱动输出：{out[:160]!r}')
                fails.append(f'{label}: 点了没切到 {expect}')

    print(SEP)
    clear_trace(pkg)
    if fails:
        print('=== verdict: FAIL ===')
        for f in fails:
            print('  -', f)
        return 1
    print('=== verdict: PASS ===')
    return 0


if __name__ == '__main__':
    sys.exit(main())
