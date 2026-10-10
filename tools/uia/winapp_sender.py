"""裸 sender 对照（真机）：WinRT 会不会把同一个原生对象交成另一个托管包装？

背景（这是"继续"这一步要闭合的那条线）
------------------------------------
TreeView / HyperlinkButton / SettingsCard 三处修复的理由是：
事件委托里拿回调给的 <c>sender</c> 去查按控件建的表（<c>ConditionalWeakTable</c>，
<b>引用相等</b>），而 WinRT 不保证同一原生对象每次都交出同一个托管包装 ——
查不到就静默返回，表现为"点了没反应"。

可是把修复还原成旧写法重新编译跑真机，计数<b>照旧</b>（0→2→3），
也就是说在这条路径上 <c>sender</c> 恰好就是同一个 RCW，
<c>winapp_rebind.py</c> 因此<b>没有判别力</b>。

本脚本把那个"恰好"变成可直接读的量：
同一页上 A 侧走 Reactor handler（有修复），B 侧自己 new 控件、自己挂事件、
委托里<b>故意用 sender 查表</b>。两侧点同样的次数，B 侧分开记
<b>收到</b>（事件确实抛了）与<b>命中</b>（按 sender 查到了键），
外加 <c>ReferenceEquals(sender, control)</c> 的真假。

二值结论
--------
* B 侧「命中 < 收到」或「同引用 = False」⇒ 身份漂移在这条路径上<b>真的发生</b>，
  三处修复是真机可复现的修复 → 加 <c>--require-bare-lost</c> 把它钉成硬回归。
* B 侧「命中 = 收到」⇒ 该机型该路径上 WinRT 交回同一 RCW，修复是<b>按构造正确</b>
  的防御性改动，真机验不出来 → 判别力继续由 DetachedContractTests 的源码级契约承担。

两种结论都算闭合：前者把它升级成真机回归，后者把它明确定性为"不可达"。

用法：
    python winapp_sender.py                       # 两侧各点 2 下
    python winapp_sender.py --rounds 3 --require-bare-lost
"""

import argparse
import re
import subprocess
import sys
import time

from _uia import (find_package, stop_app, sync_artifacts, set_mode,
                  clear_trace, start_app, wait_window_gone)

APP_TITLE = 'UwpApp'
PROBE_MODE = 21                      # TestShell.Cases 里"裸 sender 对照"的下标
A_PREFIX = 'A 侧（有修复）：'
B_PREFIX = 'B 侧（裸 sender）：'


def ui(*args, timeout=60.0):
    r = subprocess.run(['winapp', 'ui', *[str(a) for a in args]],
                       capture_output=True, text=True, timeout=timeout)
    # stderr 也要带上：失败文案有一部分走 stderr，只收 stdout 会丢光现场信息。
    return ((r.stdout or '') + (r.stderr or '')).strip()


def find_hwnd(timeout=40.0):
    """按标题找 UWP 窗口的 HWND（不能用 -a 进程名：宿主叫 ApplicationFrameHost）。"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        for m in re.finditer(r'HWND\s+(\d+):\s*"([^"]*)"', ui('list-windows')):
            if APP_TITLE in m.group(2):
                return m.group(1)
        time.sleep(1.0)
    return None


def find_slug(hwnd, text, timeout=15.0, ctype=None):
    """每次都重查：元素随帧重建，slug 是一次性的。

    <c>ctype</c> 用来把命中限定到控件本身：卡片按名字搜会同时命中卡片
    （Button，有尺寸）与它模板里那个同名的 Text（尺寸 0）——取到后者，
    click 就报 "zero size — cannot click"，而卡片其实好好地在那儿。
    """
    deadline = time.time() + timeout
    while time.time() < deadline:
        out = (ui('search', text, '-w', hwnd, '--type', ctype) if ctype
               else ui('search', text, '-w', hwnd))
        if 'Found 0' not in out:
            m = re.search(r'^\s*([a-z0-9-]+(?:-[0-9a-f]+)?)\s+\S+', out, re.M)
            if m:
                return m.group(1)
        time.sleep(0.4)
    return None


def wait_ready(prefix, timeout=60.0):
    """等窗口<b>并且等页面真的画出来了</b>。

    只按标题拿到 HWND 是不够的：<c>ApplicationFrameHost</c> 会复用 HWND 值，
    抓到一个正在咽气的旧窗口时，标题仍是 UwpApp，但每一条 UIA 命令都返回空 ——
    表现为"状态行读不到"，而 App 其实完全正常（这条假红踩过）。
    所以这里把"能搜到页面上的那行字"当成窗口可用的判据。
    """
    deadline = time.time() + timeout
    while time.time() < deadline:
        hwnd = find_hwnd(timeout=10.0)
        if hwnd and 'Found 0' not in ui('search', prefix, '-w', hwnd):
            return hwnd
        time.sleep(0.5)
    return None


def read_line(prefix, timeout=15.0):
    """读页面上那一整行状态文本（A 侧或 B 侧）。每次自取句柄。"""
    deadline = time.time() + timeout
    while time.time() < deadline:
        hwnd = find_hwnd(timeout=20.0)
        if not hwnd:
            continue
        slug = find_slug(hwnd, prefix, timeout=3.0)
        if slug:
            v = ui('get-value', slug, '-w', hwnd)
            if v and prefix in v:
                return v.splitlines()[0]
        time.sleep(0.4)
    return None


def hit(name, via='invoke', ctype=None):
    """点一下。每一发都用<b>当下</b>的句柄：ApplicationFrameHost 会复用 HWND 值。"""
    hwnd = find_hwnd(timeout=20.0)
    if not hwnd:
        return False, '找不到 UwpApp 窗口'

    slug = find_slug(hwnd, name, ctype=ctype)
    if not slug:
        return False, f'找不到 {name!r}'

    # 屏外控件（IsOffscreen=True）的 invoke 不走真实点击路由，先滚进视口。
    ui('scroll-into-view', slug, '-w', hwnd)
    time.sleep(0.2)

    # 卡片走 invoke 而不是鼠标 click：click 要元素的屏幕矩形，屏外元素的矩形是 0，
    # 会报 "zero size — cannot click"；invoke 走 InvokePattern，不依赖坐标。
    # （裸侧那个卡片必须自己开 IsClickEnabled 才有 Button 身份 / 才抛 Click。）
    out = ui('click', slug, '-w', hwnd) if via == 'click' else ui('invoke', slug, '-w', hwnd)
    first = out.splitlines()[0] if out else '(无输出)'

    # 按"失败标记"判定，不认成功串：click 与 invoke 的成功文案不一样，
    # 认一边就会把另一边判成失败。
    failed = ('❌' in out) or ('No element' in out) or ('does not support' in out)
    if out and not failed:
        return True, first
    if failed:
        # 取证：把这一步看到的元素属性一起打出来（矩形 / 是否屏外），
        # 否则 "zero size" 只有一个结论、没有线索，只能靠猜。
        props = ui('get-property', slug, '-w', hwnd, '--property', 'IsOffscreen')
        print(f'      … 取证 {name!r} slug={slug} IsOffscreen={(props or "?").strip()[:80]}')
    return False, first


# (显示名, 驱动方式)
# (显示名, 驱动方式, UIA 控件类型)
A_TARGETS = [
    ('A-甲-1', 'invoke', 'TreeItem'),
    ('A-链接：点一下算一次', 'invoke', 'Hyperlink'),
    ('A-卡片：点一下算一次', 'click', 'Button'),
]
# 卡片的裸侧对照<b>取不到</b>，如实不凑：裸侧那一列整体落在滚动视口之外
# （取证：get-property IsOffscreen=True），树与链接走 invoke 不依赖矩形所以照样能打，
# 而 SettingsCard 不支持 invoke pattern、只能靠鼠标 click，click 又要矩形 ——
# 屏外元素的矩形是 0，点就打不中。把卡片挪到列首、给死高度、两列改对称都试过，
# 结论一样：这是视口限制，不是控件的问题。所以 B 侧只跑树与链接两个控件。
B_TARGETS = [
    ('B-甲-1', 'invoke', 'TreeItem'),
    ('B-链接：点一下算一次', 'invoke', 'Hyperlink'),
]
LABELS = ['树', '链接', '卡片']

A_RE = re.compile(r'树 (\d+) \| 链接 (\d+) \| 卡片 (\d+)')
# 不捕获标签：<c>(\S+)</c> 会把行首前缀一起吞掉（"裸 sender）：树" 里
# "sender）：树" 是连续非空白），标签按<b>出现顺序</b>对应 LABELS 更稳。
B_RE = re.compile(r'命中(\d+)/收到(\d+) 同引用=(True|False)')


def parse_a(line):
    m = A_RE.search(line or '')
    return tuple(int(g) for g in m.groups()) if m else None


def parse_b(line):
    """B 侧一行 → [(标签, 命中, 收到, 同引用), ...]（顺序与 LABELS 一致）。"""
    return [(LABELS[i], int(m.group(1)), int(m.group(2)), m.group(3) == 'True')
            for i, m in enumerate(B_RE.finditer(line or ''))]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--rounds', type=int, default=2, help='每个控件点几下')
    ap.add_argument('--settle', type=float, default=5.0, help='启动后静置秒数')
    ap.add_argument('--require-bare-lost', action='store_true',
                    help='把"裸侧命中 < 收到"当成硬条件（确认身份漂移真机成立后再开）')
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
    start_app(pf)

    hwnd = wait_ready(A_PREFIX, timeout=60.0)
    if not hwnd:
        print('=== 60s 内没等到可用的窗口（或页面没画出来）===')
        return 2
    print(f'window  = HWND {hwnd}')
    time.sleep(args.settle)

    failures = 0

    a0, b0 = parse_a(read_line(A_PREFIX)), parse_b(read_line(B_PREFIX))
    if a0 is None:
        print(f'=== 读不到 A 侧状态行（B={b0!r}）===')
        return 2
    if not b0:
        # B 侧一发都没收到之前那行也是合法的，只是这里就没法算差值了；
        # 按 0 起算即可（每次都是全新启动，计数器本来就是 0）。
        print('!! B 侧状态行解析不出三段，按 0 起算')

    a_line0, b_line0 = read_line(A_PREFIX), read_line(B_PREFIX)
    print(f'起点 A：{a_line0}')
    print(f'起点 B：{b_line0}')

    # 两侧点同样的次数：这是单变量对照的全部内容。
    for side, targets in (('A', A_TARGETS), ('B', B_TARGETS)):
        for name, via, ctype in targets:
            for _ in range(args.rounds):
                ok, out = hit(name, via, ctype)
                if not ok:
                    print(f'FAIL  {side} {name}：点不响 → {out}')
                    failures += 1
                time.sleep(1.0)

    a1 = parse_a(read_line(A_PREFIX))
    b1 = parse_b(read_line(B_PREFIX))
    if a1 is None or not b1:
        print(f'=== 点击后读不到状态行：A={a1!r} B={b1!r} ===')
        return 2

    print()
    print(f'点击后 A：{read_line(A_PREFIX)}')
    for i, label in enumerate(LABELS):
        got = a1[i] - a0[i]
        ok = got == args.rounds
        print(f'{"PASS" if ok else "FAIL"}  A/{label}：点了 {args.rounds} 下，'
              f'计数 {a0[i]} → {a1[i]}（+{got}）'
              + ('' if ok else f'  ← 期望 +{args.rounds}'))
        if not ok:
            failures += 1

    print()
    print(f'点击后 B：{read_line(B_PREFIX)}')
    drift = False
    for label, hit_n, fired_n, same in b1:
        before = next((x for x in b0 if x[0] == label), None)
        got_fired = fired_n - (before[2] if before else 0)
        got_hit = hit_n - (before[1] if before else 0)
        lost = got_fired - got_hit

        print(f'  ——  B/{label}：收到 {got_fired}、命中 {got_hit}'
              f'（丢了 {lost}）、sender 同引用={same}')
        if lost > 0 or not same:
            drift = True

        if got_fired == 0:
            print(f'FAIL  B/{label}：一次都没收到 —— 这一发没打到控件，'
                  f'对照不成立（不能算成身份漂移）')
            failures += 1

    print('注：卡片的裸侧对照未取到（那一列整体在滚动视口外，'
          'SettingsCard 又只认鼠标 click，屏外元素没有矩形可点）——'
          '上面只覆盖树与链接两个控件。')
    print()
    if drift:
        print('结论：身份漂移在这条路径上【真机成立】——裸侧按 sender 查表落空，'
              '三处修复是真机可复现的修复。可加 --require-bare-lost 钉成硬回归。')
        if not args.require_bare_lost:
            print('      （本次未开 --require-bare-lost，此结论只作记录）')
    else:
        print('结论：该机型该路径上 WinRT 交回【同一个 RCW】，裸侧按 sender 查表全部命中。'
              '三处修复是按构造正确的防御性改动，真机仍验不出来 —— '
              '判别力继续由 DetachedContractTests 的源码级契约承担。')
        if args.require_bare_lost:
            print('FAIL  --require-bare-lost 已开，但裸侧没有丢回调')
            failures += 1

    print(f'=== verdict: {"PASS" if failures == 0 else f"FAIL ({failures})"} ===')
    return 0 if failures == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
