"""重挂探针页回归：TreeView / HyperlinkButton / SettingsCard 回调必须"恰好一次"。

它盯这次刚修的两个形状（详见 <c>RebindProbePage</c> 的注释）：

1. <b>委托不许拿 sender 查表。</b>这三个 handler 原本在事件委托里用回调给的
   <c>sender</c> 去查按控件建的表（引用相等），而 WinRT 不保证它和订阅时是同一个
   托管包装——查不到就静默返回，表现为"点了没反应"。
2. <b>Unmount 必须真解绑。</b>原本只摘表键不解绑，重挂时会再挂一份委托，
   此后每次点击都是双份回调。

判别力实测结论（写入者注：这是反向对照跑出来的，不是推理出来的）
---------------------------------------------------------------
<b>第二段（卸载/重挂）目前没有判别力。</b>把 Tree / Template 三处 -= 全删掉重新
编译跑同一条流程，结果完全一样：仍然各 +1，一条都不翻倍。原因是协调器这条路
<b>不会复用那个原生控件</b>——每次挂载都是 new 一个，bug 因此不可达。
（据此还排掉了一个假阴性：变异确实进了 dll，产物时间戳晚于源码。）

<b>第一段（点了要有回调）在这三个控件上同样没有判别力。</b>把修复还原成
<s>旧写法</s>（委托里用 s 查表）重新编译再跑，计数照旧 0→2→3。
也就是说 WinRT 在这三个控件上给出的 sender 恰好就是同一个 RCW 包装 ——
RadioButtons 那次吃到的 identity 漂移（日志里 CtlId 从 #1 跳 #2）
在这条路径上没发生。

所以：本脚本现在的 PASS 只能说明"这三个控件的回调通路是通的"，
<b>不足以证明那两处修复是对的</b>。真正盯着它们的是
DetachedContractTests 里的源码级契约（全目录扫描 + 变异验证）。

<b>2026-10-10 补：这条线已经用 <c>winapp_sender.py</c>（裸 sender 对照页）闭合了。</b>
那一页把"恰好"变成可观测的量（收到 / 命中 / <c>ReferenceEquals(sender, control)</c>），
实测 3 发：裸侧"命中 3/收到 3、同引用=True" —— 该机型该路径上 WinRT 交回的是
<b>同一个 RCW</b>，所以这两处修复是<b>按构造正确</b>的防御性改动，<b>真机验不出来</b>。
判别力继续由源码级契约承担；这不是待办，是结论。
（卡片的裸侧对照因那一列整体在滚动视口外而没取到，详见那个脚本的注释。）

为什么不用 invoke 后的 slug：每帧重渲染都会让<b>元素重建</b>，旧 slug 当场失效
（拿到的是 stale 引用，或者干脆报 No element found）。所以每一步都用
<b>文本重新 search</b>，命令变成无状态的，跟距离上次点击多久无关。

用法：
    python winapp_rebind.py                 # 三个控件各点 2 下，再卸载/重挂后各点 1 下
    python winapp_rebind.py --rounds 3
"""

import argparse
import re
import subprocess
import sys
import time

from _uia import (find_package, stop_app, sync_artifacts, set_mode, set_trace,
                  clear_trace, start_app, wait_window_gone)

APP_TITLE = 'UwpApp'
PROBE_MODE = 20                      # TestShell.Cases 里"重挂探针"的下标
STATE_PREFIX = '重挂探针：'


def ui(*args, timeout=60.0):
    r = subprocess.run(['winapp', 'ui', *[str(a) for a in args]],
                       capture_output=True, text=True, timeout=timeout)
    # stderr 也要带上：失败文案（"does not support any invoke pattern" 之类）
    # 有一部分走 stderr，只收 stdout 的话现场信息直接丢光，只剩一个空串。
    return ((r.stdout or '') + (r.stderr or '')).strip()


def find_hwnd(timeout=40.0):
    """按标题找 UWP 窗口的 HWND（不能用 -a 进程名：UWP 顶层窗口宿主叫 ApplicationFrameHost）。"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        m = re.search(r'HWND\s+(\d+):\s*"([^"]*)"', ui('list-windows'))
        for m in re.finditer(r'HWND\s+(\d+):\s*"([^"]*)"', ui('list-windows')):
            if APP_TITLE in m.group(2):
                return m.group(1)
        time.sleep(1.0)
    return None


def find_slug(hwnd, text, timeout=15.0):
    """每次都重查：元素随帧重建，slug 是一次性的。"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        out = ui('search', text, '-w', hwnd)
        if 'Found 0' not in out:
            m = re.search(r'^\s*([a-z0-9-]+(?:-[0-9a-f]+)?)\s+\S+', out, re.M)
            if m:
                return m.group(1)
        time.sleep(0.4)
    return None


def counts(timeout=10.0):
    """读页面那行状态文本，解析出三个计数。（每次自取句柄，理由同 hit）"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        hwnd = find_hwnd(timeout=20.0)
        if not hwnd:
            continue
        slug = find_slug(hwnd, STATE_PREFIX, timeout=3.0)
        if slug:
            v = ui('get-value', slug, '-w', hwnd)
            m = re.search(r'Tree 回调 (\d+) \| 链接 (\d+) \| 卡片 (\d+)', v or '')
            if m:
                return (int(m.group(1)), int(m.group(2)), int(m.group(3))), v.splitlines()[0]
        time.sleep(0.4)
    return None, None


def hit(name, via='invoke'):
    # 每一发都用<b>当下</b>的句柄：UWP 顶层窗口宿主（ApplicationFrameHost）会
    # 复用 HWND 值，拿旧句柄会被拒绝（"belongs to a different process"）。
    hwnd = find_hwnd(timeout=20.0)
    if not hwnd:
        return False, '找不到 UwpApp 窗口'
    """怎么点它。<c>SettingsCard</c> 是 Button 但<b>不支持任何 invoke pattern</b>，
    只能退回鼠标点击；TreeItem / Hyperlink 走 InvokePattern 就行。"""
    slug = find_slug(hwnd, name)
    if not slug:
        return False, f'找不到 {name!r}'

    out = ui('click', slug, '-w', hwnd) if via == 'click' else ui('invoke', slug, '-w', hwnd)
    first = out.splitlines()[0] if out else '(无输出)'

    # 按"失败标记"判定，不认成功串：两种写法各自的成功文案不一样
    # （invoke 说 "Invoked … via XXXPattern"，click 说 "✅ click on … at (x, y)"），
    # 认一边就会把另一边判成失败——卡片就被这么误判过一轮。
    failed = ('❌' in out) or ('No element' in out) or ('does not support' in out)
    if out and not failed:
        return True, first
    return False, first


# 三个目标：(显示名, 驱动方式, 在计数元组里的下标)
TARGETS = [
    ('甲-1', 'invoke', 0),
    ('超链接：点一下算一次', 'invoke', 1),
    ('设置卡片：点一下算一次', 'click', 2),
]
LABELS = ['Tree', '链接', '卡片']


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--rounds', type=int, default=2, help='卸载之前每个控件点几下')
    ap.add_argument('--settle', type=float, default=5.0, help='启动后静置秒数')
    args = ap.parse_args()

    pkg, pf = find_package()
    if not pkg:
        print('=== 定位不到包目录 ===')
        return 2
    print(f'package = {pf}')

    stop_app()
    wait_window_gone()   # 进程没了但窗口还在的话，下一步会抢到旧 HWND
    sync_artifacts()
    set_mode(pkg, PROBE_MODE)
    set_trace(pkg)
    start_app(pf)

    hwnd = find_hwnd(timeout=40.0)
    if not hwnd:
        print('=== 40s 内没找到窗口 ===')
        return 2
    print(f'window  = HWND {hwnd}')
    time.sleep(args.settle)

    failures = 0

    # 第一段：不卸载，直接点 N 下 —— 验"回调不丢"（sender 那条修对了没有）
    base, line = counts()
    if base is None:
        print(f'=== 读不到状态文本：{line!r} ===')
        return 2
    print(f'起点：{line}')

    for name, via, idx in TARGETS:
        for _ in range(args.rounds):
            ok, out = hit(name, via)
            if not ok:
                print(f'FAIL  {LABELS[idx]}：点不响 → {out}')
                failures += 1
            time.sleep(1.2)

        now, line = counts()
        if now is None:
            print(f'=== {LABELS[idx]}：读到不了状态 ===')
            failures += 1
            continue
        got = now[idx] - base[idx]
        ok = got == args.rounds
        print(f'{"PASS" if ok else "FAIL"}  {LABELS[idx]}：点了 {args.rounds} 下，'
              f'计数 {base[idx]} → {now[idx]}（+{got}）'
              + ('' if ok else f'  ← 期望 +{args.rounds}'))
        if not ok:
            failures += 1
        base = now

    # 第二段：卸载再重挂，然后各点一下 —— 验"回调不重"（Unmount 真解绑了没有）
    unmount_hwnd = find_hwnd(timeout=20.0) or hwnd
    slug = find_slug(unmount_hwnd, '卸载子树')
    if not slug:
        print('=== 找不到"卸载子树"按钮 ===')
        failures += 1
    else:
        ui('invoke', slug, '-w', unmount_hwnd)
        time.sleep(1.5)
        again_hwnd = find_hwnd(timeout=20.0) or unmount_hwnd
        again = find_slug(again_hwnd, '重挂子树')
        if not again:
            print('=== 卸载后没变成"重挂子树"按钮 ===')
            failures += 1
        else:
            ui('invoke', again, '-w', again_hwnd)
            time.sleep(2.0)

        base, line = counts()
        print(f'重挂后：{line}')

        for name, via, idx in TARGETS:
            ok, out = hit(name, via)
            if not ok:
                print(f'FAIL  {LABELS[idx]}（重挂后）：点不响 → {out}')
                failures += 1
                continue
            time.sleep(1.2)
            now, line = counts()
            got = now[idx] - base[idx]
            ok = got == 1
            print(f'{"PASS" if ok else "FAIL"}  {LABELS[idx]}（重挂后）：点 1 下，'
                  f'计数 {base[idx]} → {now[idx]}（+{got}）'
                  + ('' if ok else '  ← 期望 +1，多出来就是"重挂时又被绑了一份"'))
            if not ok:
                failures += 1
            base = now

    clear_trace(pkg)
    print(f'=== verdict: {"PASS" if failures == 0 else f"FAIL ({failures})"} ===')
    return 0 if failures == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
