"""卡死取证：连点之后不杀进程，查三件事——各项 IsSelected、进程 CPU、窗口还活不活。

用来回答"到底是控件卡死还是在忙别的"：
  · 两项同时 IsSelected=True → UI 与内部选中态脱钩（典型 SOAP 之后的重承症状）
  · CPU 持续高 → 卡在重算/重排里，不是事件没抛
  · 窗口没了但进程还在 → 崩了（这种情况先去看日志尾部，别继续点）

这两条告警已经<b>验过会响</b>，不是写在那儿没人碰的代码。自检办法：测试壳第 23 项
"诊断量具自检"（mode 22）故意造了两个反常场面——两个 groupName 不同的 RadioButton
（可同时选中）、一个后台空转 6 秒的按钮（CPU 拉满），分别跑：
    python winapp_diag.py --mode 22 --targets 甲,乙               # 该报 2 项同时选中
    python winapp_diag.py --mode 22 --targets "忙 6 秒:Button"    # 该报 CPU 占 N% 单核
第一次跑就把上面那个"0 项同时选中"的误报抓出来了（Button 没有选中态，不该判）。

迁移说明：原 uia_diag3.py 用 comtypes 读 SelectionItemPattern.CurrentIsSelected
并用 keybd_event 做"Alt+Tab 把窗口带回前台"（但 XAML 是 windowless 的，那套 posts
到 frame 并不总能到控件，走过的弯路够多了）。现在：
  · IsSelected 走 `winapp ui get-property <slug> -p IsSelected`
  · 点击走 `winapp ui invoke`（SelectionItemPattern，不要求窗口在前台）
  · CPU 走 PowerShell 的 Get-Process，与 winapp 无关，自己量

用法：
    python winapp_diag.py                       # 点三次，每点之后取样
    python winapp_diag.py --targets 第一,第三,第二,第一
    python winapp_diag.py --cpu-samples 5       # CPU 多采几针
"""

import argparse
import os
import re
import subprocess
import sys
import time

from _uia import (find_package, stop_app, sync_artifacts, set_mode, set_trace,
                  clear_trace, start_app, wait_window_gone, log_path)

from winapp_run import (ui, find_hwnd, find_slug, state_text, new_lines,
                        CALLBACK_MARK)

SEP = '-' * 62


def parse_targets(spec, default_type):
    """目标写成 <c>名字</c> 或 <c>名字:UIA控件类型</c>（逗号分隔）。

    类型不是可选的装饰：按名字搜会同时命中控件与它模板里那个同名的 Text，
    取到后者时 invoke 直接报 "does not support any invoke pattern"。
    """
    out = []
    for t in spec.split(','):
        t = t.strip()
        if not t:
            continue
        name, _, ctype = t.partition(':')
        out.append((name.strip(), ctype.strip() or default_type))
    return out


def selected_flags(hwnd, names):
    """读每个选项的 IsSelected。读不到记 '?'，别假装是 False。"""
    flags = []
    for name, ctype in names:
        slug = find_slug(hwnd, name, timeout=4.0, ctype=ctype)
        if not slug:
            flags.append(f'{name}=?')
            continue
        out = ui('get-property', slug, '-w', hwnd, '-p', 'IsSelected')
        m = re.search(r'IsSelected:\s*(\w+)', out)
        flags.append(f'{name}={m.group(1) if m else "?"}')
    return flags


def cpu_sample(tag=''):
    """取一次 UwpApp 进程的总 CPU 时间（秒）。取不到返回 None。"""
    r = subprocess.run(['powershell', '-NoProfile', '-Command',
                        '(Get-Process -Name UwpApp -EA SilentlyContinue | '
                        'Measure-Object -Property CPU -Sum).Sum'],
                       capture_output=True, text=True, timeout=30.0)
    raw = (r.stdout or '').strip()
    try:
        return float(raw)
    except ValueError:
        return None


def run():
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', type=int, default=9, help='测试壳菜单下标，9 = 元素画廊')
    ap.add_argument('--targets', default='第一,第三,第二',
                    help='依次点击的目标，写成 名字 或 名字:UIA控件类型')
    ap.add_argument('--target-type', default='RadioButton',
                    help='未写类型的目标默认按这个 UIA 控件类型找（默认 RadioButton）')
    ap.add_argument('--pause', type=float, default=2.5, help='每发之后的静置秒数')
    ap.add_argument('--cpu-samples', type=int, default=3, help='每次取样窗内的针数')
    ap.add_argument('--settle', type=float, default=4.0, help='启动后静置秒数')
    # 静置基线实测 ≈4% 单核，阈值取它的十几倍，只在"明显在忙"时才出声。
    ap.add_argument('--cpu-alarm', type=float, default=0.5,
                    help='单核占用超过这个比例就提示（0.5 = 50%%）')
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

    logp = log_path(pkg)
    start_app(pf)

    hwnd = find_hwnd(timeout=40.0)
    if not hwnd:
        print('=== 40s 内没找到 App 窗口 ===')
        return 2
    print(f'window  = HWND {hwnd}')
    time.sleep(args.settle)

    names = parse_targets(args.targets, args.target_type)
    print(SEP)
    print(f'{"#":5s}{"目标":8s}{"回调":>4s}  {"状态":10s}  {"各项 IsSelected":28s}  单核占用')

    prev_cpu = cpu_sample()
    for i, (name, ctype) in enumerate(names):
        hwnd = find_hwnd(timeout=20.0)
        if not hwnd:
            print(f'{i:<5d}{name:8s}       -- 窗口没了 → 进程可能崩了，看日志尾部')
            return 2

        slug = find_slug(hwnd, name, ctype=ctype)
        if not slug:
            print(f'{i:<5d}{name:8s}       -- 找不到该选项')
            continue

        offset = os.path.getsize(logp) if os.path.exists(logp) else 0
        invoked = ui('invoke', slug, '-w', hwnd)

        # CPU 采样从 invoke 之后<b>立刻</b>起算：点击自己的重算/重排开销就在头
        # 几百毫秒里，等 flags 读完再开表的话这段全漏掉，只会看到 0%。
        t0 = time.time()
        samples = [cpu_sample()]
        # args.pause 摊在针之间：既等重排落地，又不浪费这段墙钟。
        step = max(args.pause / max(args.cpu_samples, 1), 0.2)
        for _ in range(args.cpu_samples):
            time.sleep(step)
            samples.append(cpu_sample())
        elapsed = time.time() - t0
        vals = [c for c in samples if c is not None]
        delta = (max(vals) - min(vals)) if len(vals) >= 2 else 0.0
        load = delta / elapsed if elapsed > 0 else 0.0
        prev_cpu = vals[-1] if vals else None

        got = new_lines(logp, offset, CALLBACK_MARK)
        st = state_text(hwnd, '单选：')
        flags = selected_flags(hwnd, names)

        # 只在<b>真读到了</b>的项里数：读不到 IsSelected 的（记 '=' 后面是 '?'）
        # 根本不是可选项（比如一个 Button），把它们当 False 数进去会凭空报
        # "0 项同时选中"——这句是自检页第一次跑出来才暴露的：拿一个按钮当目标，
        # 脚本照样判"UI 与内部状态脱钩"，而它压根没有选中态可言。
        known = [f for f in flags if not f.endswith('=?')]
        multi = sum(1 for f in known if f.endswith('=True'))
        warn = ''
        if known and multi == 0:
            warn = '  ← 一项都没选中（UI 与内部状态脱钩）'
        elif multi > 1:
            warn = f'  ← {multi} 项同时选中（UI 与内部状态脱钩）'
        print(f'{i:<5d}{name:8s}{len(got):>4d}  {str(st):10s}  {" ".join(flags):28s}'
              f'  {load * 100:5.1f}%{warn}')
        if 'Invoked' not in invoked:
            print(f'        invoke 输出异常：{invoked[:100]!r}')
        if load > args.cpu_alarm:
            print(f'        CPU 占 {load * 100:.0f}% 单核、持续 {elapsed:.1f}s：'
                  f'像是在重算/重排，不像"事件没抛"')

    print(SEP)
    final = selected_flags(find_hwnd(timeout=20.0) or hwnd, names)
    print('末态 ', ' '.join(final))
    clear_trace(pkg)
    return 0


if __name__ == '__main__':
    sys.exit(run())
