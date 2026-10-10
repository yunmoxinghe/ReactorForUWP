"""受控控件回归：真机连点，断言每次点击都产生回调。

UIA 交互全部走 `winapp ui`（winapp 0.7.0 自带），不再自造 comtypes 封装：
它给的 inspect / search / get-value / invoke 足够覆盖本回归要的
"找到控件 → 点 → 读状态 → 数回调"，而且 invoke 走 SelectionItemPattern，
<b>不要求窗口在前台</b>——这一点比模拟鼠标可靠得多。

本脚本只负责 winapp ui 不管的部分：启停 App、写 mode/Trace 开关、
同步产物、读日志数回调、判红绿。

用法：
    python winapp_run.py                      # 默认点 5 发，每发期望 1 条回调
    python winapp_run.py --targets 第一,第二
    python winapp_run.py --expect 2           # 反向验证：应当判红、退出码 1
"""

import argparse
import os
import re
import subprocess
import sys
import time

from _uia import (find_package, stop_app, sync_artifacts, set_mode, set_trace,
                  clear_trace, start_app, wait_window_gone, log_path)

CALLBACK_MARK = '→ 用户回调'

# 数回调时还得限定<b>是哪个控件</b>，两个标记要同时满足：
#  · '→ 用户回调' 排除 Trace 级的"取消选中 / 纠正回受控值"，那些不是回调。
#  · 'RadioButtons#'（复数）排除同一画廊里那两个单选按钮（RadioButton，单数）——
#    它们跟 RadioButtons 共享同一个 radio 状态，点一次会连带抛自己的
#    Checked/Unchecked，也是真回调，但属于<b>别的控件</b>。
#    通吃的话每发多数 1～2 条，看着就像"回调翻倍"，其实是量具口径串了。
CALLBACK_OWNER = 'RadioButtons#'


def callbacks_of(logp, offset, owner=CALLBACK_OWNER):
    """读 offset 之后的"用户回调"行，只认 owner 那一个控件的。"""
    return [l for l in new_lines(logp, offset, CALLBACK_MARK) if owner in l]
APP_TITLE = 'UwpApp'


# ── winapp ui 封装 ──────────────────────────────────────────
def ui(*args, timeout=60.0):
    """跑一条 winapp ui 子命令，返回 stdout。失败返回空串（由调用方判空）。"""
    r = subprocess.run(['winapp', 'ui', *[str(a) for a in args]],
                       capture_output=True, text=True, timeout=timeout)
    # stderr 也要带上：失败文案（"does not support any invoke pattern" 之类）
    # 有一部分走 stderr，只收 stdout 的话现场信息直接丢光，只剩一个空串。
    return ((r.stdout or '') + (r.stderr or '')).strip()


def find_hwnd(timeout=40.0):
    """找 App 窗口的 HWND。

    用 `-a <进程名>` 定位 UWP 是<b>错的</b>：UWP 的顶层窗口是
    ApplicationFrameWindow，宿主进程叫 ApplicationFrameHost，不是 UwpApp。
    按进程名去找会命中 0 个窗口（曾经因此得到"search 0 命中"的假阴性）。
    所以这里从 list-windows 里按标题挑，再把 HWND 钉死传给后续命令。
    """
    deadline = time.time() + timeout
    while time.time() < deadline:
        out = ui('list-windows')
        for m in re.finditer(r'HWND\s+(\d+):\s*"([^"]*)"', out):
            hwnd, title = m.group(1), m.group(2)
            if APP_TITLE in title:
                return hwnd
        time.sleep(1.0)
    return None


def find_slug(hwnd, text, timeout=15.0, ctype=None):
    """按文本找元素，返回它的语义 slug。每次都重查：控件重建会让旧 slug 失效。

    <c>ctype</c> 把命中限定到控件本身（传 UIA 控件类型，如 <c>'RadioButton'</c>）。
    不加限定时按名字搜会同时命中"控件"与"它模板里那个同名的 Text"，取到后者就
    invoke 不了 —— Text 不支持任何 invoke pattern。踩过两次（卡片一次、这里一次），
    所以对外留了这个口子：<b>要点击就一定要限定类型</b>。
    """
    deadline = time.time() + timeout
    while time.time() < deadline:
        out = (ui('search', text, '-w', hwnd, '--type', ctype) if ctype
               else ui('search', text, '-w', hwnd))
        m = re.search(r'^\s*([a-z0-9-]+(?:-[0-9a-f]+)?)\s+\S+', out, re.M)
        if m and 'Found 0' not in out:
            return m.group(1)
        time.sleep(0.5)
    return None


def state_text(hwnd, prefix, timeout=10.0):
    """读页面上"单选：N"那行的当前值——状态到底变没变，只有 UI 说了算。"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        slug = find_slug(hwnd, prefix, timeout=3.0)
        if slug:
            v = ui('get-value', slug, '-w', hwnd)
            if v:
                return v.splitlines()[0].strip()
        time.sleep(0.5)
    return None


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


# ── 主流程 ─────────────────────────────────────────────────
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', type=int, default=9, help='测试壳菜单下标，9 = 元素画廊')
    # 默认连点五发。修好 RCW 那个 bug 之后，连点八发仍然每发都有回调，
    # 所以五发是有意义的回归判据（修之前第三发就哑）。
    ap.add_argument('--targets', default='第一,第二,第三,第一,第二',
                    help='依次点击的选项名，逗号分隔；要点未选中项才会产生事件')
    ap.add_argument('--expect', type=int, default=1, help='每次点击期望的回调条数')
    ap.add_argument('--state-prefix', default='单选：',
                    help='页面上显示状态的 TextBlock 前缀（原生对照页用 回调次数）')
    ap.add_argument('--settle', type=float, default=4.0,
                    help='启动后静置秒数：躲开"首帧受控下发"那条回声，它不是点击产生的')
    ap.add_argument('--verbose', action='store_true', help='把本轮全部新增日志打出来')
    args = ap.parse_args()

    pkg, pf = find_package()
    if not pkg:
        print('=== 定位不到包目录：先手动部署/启动一次 App ===')
        return 2
    print(f'package = {pf}')

    stop_app()
    wait_window_gone()   # 进程没了但窗口还在的话，下一步会抢到旧 HWND
    sync_artifacts()
    set_mode(pkg, args.mode)

    # 闸门的"吞掉"是 Trace 级、默认不落盘。不开它，"点了没反应"在日志里就是
    # "一条都没有"，极易误判成"WinUI 根本没抛事件"——这次排查被它绕了一大圈。
    set_trace(pkg)

    logp = log_path(pkg)
    start_app(pf)

    hwnd = find_hwnd(timeout=40.0)
    if not hwnd:
        print('=== 40s 内没找到 App 窗口 ===')
        return 2
    print(f'window  = HWND {hwnd}')

    time.sleep(args.settle)   # 首帧的受控下发会漏一条回声，等它过去再开始数

    failures = 0
    # 每一发都用<b>当下</b>的句柄。UWP 的顶层窗口由 ApplicationFrameHost 托管，
    # 句柄值会被系统复用：前一实例退出后同一个 HWND 可能已经属于别的进程，
    # 拿着它调 UIA 会得到 "The target window handle now belongs to a different
    # process — refusing to invoke"（连着跑两次脚本时必踩，单独跑一次看不出来）。
    for name in [t for t in args.targets.split(',') if t]:
        hwnd = find_hwnd(timeout=20.0) or hwnd
        slug = find_slug(hwnd, name)
        if not slug:
            print(f'=== 找不到选项 {name!r} ===')
            failures += 1
            continue

        before_text = state_text(hwnd, args.state_prefix)
        offset = os.path.getsize(logp) if os.path.exists(logp) else 0

        invoked = ui('invoke', slug, '-w', hwnd)
        if 'Invoked' not in invoked:
            print(f'FAIL  点 {name}：invoke 没成功 → {invoked[:120]!r}')
            failures += 1
            continue

        time.sleep(2.0)
        fresh = find_slug(hwnd, name, timeout=3.0)
        after_text = state_text(hwnd, args.state_prefix)
        stale_note = '' if fresh else ' ← 点后树里已无该选项（控件被换掉了）'
        got = callbacks_of(logp, offset)

        ok = len(got) == args.expect
        print(f'{"PASS" if ok else "FAIL"}  点 {name}：回调 {len(got)} 条（期望 {args.expect}）'
              f' | 状态 {before_text!r} → {after_text!r}{stale_note}')
        for line in got:
            print('        ', line)
        if args.verbose:
            for line in new_lines(logp, offset):
                print('        ·', line)
        if not ok:
            failures += 1

    # 收尾关掉 Trace：只在本次无人值守运行期间开。
    # 留着文件的话，下次人手启动 App 也会降到 Trace 落盘，日志会淹掉要看的那几行。
    clear_trace(pkg)

    print(f'=== verdict: {"PASS" if failures == 0 else f"FAIL ({failures})"} ===')
    return 0 if failures == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
