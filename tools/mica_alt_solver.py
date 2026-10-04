#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Mica / Mica Alt 材质配方的正向计算与参数反推求解器。

背景
----
WinUI 的 Mica 效果图(effect graph)与 Mica-For-UWP 的 BackdropMicaBrush 完全一致:

    step0  B       = GaussianBlur(wallpaper)              # 官方走 TryCreateBlurredWallpaperBackdropBrush
    step1  K       = Composite(Black, B)                  # 黑底合成,仅用于把 alpha 填满 -> 等于 B
    step2  S       = Blend(Color,      fg=T @ lum_op, bg=K)
    step3  out     = Blend(Luminosity, fg=T @ tint_op, bg=S)

其中 T 为 tint 色,@x 表示 OpacityEffect 把 alpha 置为 x。
Blend 语义遵循 W3C Compositing and Blending(即 D2D 的 D2D1_BLEND_MODE_COLOR / LUMINOSITY):

    Color(Cb, Cs)      = SetLum(Cs, Lum(Cb))      # 取前景 H/S,背景 L
    Luminosity(Cb, Cs) = SetLum(Cb, Lum(Cs))      # 取背景 H/S,前景 L
    带 alpha 合成:      Cm = (1 - a) * Cb + a * B(Cb, Cs)

已公开的官方参数(WinUI SystemBackdropBrushFactory,见 AvaloniaUI/Avalonia#10719)
--------------------------------------------------------------------------------
    Mica Base  浅色: T = #F3F3F3, tint_op = 0.5, lum_op = 1.0
    Mica Base  深色: T = #202020, tint_op = 0.8, lum_op = 1.0
    fallback         = TintColor 同值(高对比度/降级时直接铺纯色)

    Mica Alt   : T = SolidBackgroundFillColorBaseAlt = 浅色 #DADADA / 深色 #0A0A0A
                 tint_op 与 lum_op 未公开 —— 本脚本负责把它们解出来。

反推原理(关键技巧)
------------------
把桌面壁纸设成**纯色**。纯色经过高斯模糊后仍是同一个颜色,于是 B 完全已知,
模糊半径、采样区域、DWM 与 Composition 的差异全部被消除。

于是:
    out = F(B; T, tint_op, lum_op)
两个未知数,取**两张不同的纯色壁纸**各截图取色一次,即可联立解出 tint_op 与 lum_op。

用法
----
    # 1) 先做链路自检:用官方 Mica Base 参数算一遍,与你截到的 Mica Base 像素比对
    python mica_alt_solver.py check --theme light --wallpaper FF0000 --observed D6D6D6

    # 2) 反推 Mica Alt 的 tint_op / lum_op(需要两张纯色壁纸各取一次色)
    python mica_alt_solver.py solve --theme light \
        --wallpaper1 FF0000 --alt1 C8C8C8 \
        --wallpaper2 0058D0 --alt2 B4B4B4

    # 3) 正向验证:给定参数算输出
    python mica_alt_solver.py forward --theme light --wallpaper FF0000 \
        --tint DADADA --tint-op 0.5 --lum-op 1.0

注意
----
* 截图必须在**窗口处于激活状态**下取,否则官方会降级成纯色 fallback。
* 取色点要避开内容层(官方 guidance 要求中间层用 LayerFillColorDefaultBrush,
  那是半透明的,会污染取色)。最稳的做法是取标题栏区域的空白像素。
* D2D 的 blend 在 sRGB(gamma)空间进行,本脚本默认如此;
  若实测偏差系统性偏大,可试 --linear 切到线性空间再算一次。
"""

import argparse
import sys


# ---------------------------------------------------------------- 颜色工具

def hex_to_rgb(s: str):
    s = s.strip().lstrip("#")
    if len(s) != 6:
        raise ValueError(f"颜色必须是 6 位十六进制, got: {s!r}")
    return tuple(int(s[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def rgb_to_hex(c) -> str:
    return "".join(f"{int(round(max(0.0, min(1.0, v)) * 255)):02X}" for v in c)


def srgb_to_linear(c):
    def f(v):
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    return tuple(f(v) for v in c)


def linear_to_srgb(c):
    def f(v):
        return v * 12.92 if v <= 0.0031308 else 1.055 * (v ** (1 / 2.4)) - 0.055
    return tuple(f(v) for v in c)


# ---------------------------------------------------------------- W3C blend 原语

def lum(c):
    """W3C Lum(C) = 0.3R + 0.59G + 0.11B"""
    return 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]


def clip_color(c):
    l = lum(c)
    n = min(c)
    x = max(c)
    if n < 0.0:
        denom = (l - n)
        k = 0.0 if denom == 0.0 else l / denom
        c = tuple(l + (v - l) * k for v in c)
    if x > 1.0:
        denom = (x - l)
        k = 0.0 if denom == 0.0 else (1.0 - l) / denom
        c = tuple(l + (v - l) * k for v in c)
    return c


def set_lum(c, l):
    d = l - lum(c)
    return clip_color(tuple(v + d for v in c))


def blend_color(cb, cs):
    """Color(Cb, Cs) = SetLum(Cs, Lum(Cb)) —— 取前景 H/S,背景 L"""
    return set_lum(cs, lum(cb))


def blend_luminosity(cb, cs):
    """Luminosity(Cb, Cs) = SetLum(Cb, Lum(Cs)) —— 取背景 H/S,前景 L"""
    return set_lum(cb, lum(cs))


def over(cb, cs, a):
    """带前景 alpha 的合成:Cm = (1-a)*Cb + a*B(Cb, Cs)"""
    return tuple((1.0 - a) * cb[i] + a * cs[i] for i in range(3))


# ---------------------------------------------------------------- Mica 管线

def mica_forward(b, t, tint_op, lum_op, linear=False):
    """
    正向计算 Mica/Mica Alt 输出颜色。

    b: 壁纸(模糊后)RGB,0..1
    t: tint 色 RGB,0..1
    tint_op: TintOpacity
    lum_op : LuminosityOpacity
    返回 RGB,0..1
    """
    if linear:
        b, t = srgb_to_linear(b), srgb_to_linear(t)

    # step1: 黑底合成 —— 结果就是 b 本身
    k = b

    # step2: Blend(Color, fg=t @ lum_op, bg=k)
    s = over(k, blend_color(k, t), lum_op)

    # step3: Blend(Luminosity, fg=t @ tint_op, bg=s)
    out = over(s, blend_luminosity(s, t), tint_op)

    return linear_to_srgb(out) if linear else out


# ---------------------------------------------------------------- 求解

def _err(b, t, tint_op, lum_op, target, linear):
    got = mica_forward(b, t, tint_op, lum_op, linear)
    return sum((got[i] - target[i]) ** 2 for i in range(3))


def solve_two(w1, t, o1, w2, o2, linear=False, coarse=0.02, fine=0.002):
    """
    用两张纯色壁纸的观测输出,网格搜索 Mica Alt 的 (tint_op, lum_op)。
    先在 0..1 上粗扫,再在最优邻域精化。
    """
    def total(a, l):
        return _err(w1, t, a, l, o1, linear) + _err(w2, t, a, l, o2, linear)

    best = (None, None, float("inf"))
    a = 0.0
    while a <= 1.0001:
        l = 0.0
        while l <= 1.0001:
            e = total(a, l)
            if e < best[2]:
                best = (a, l, e)
            l += coarse
        a += coarse

    a0, l0, _ = best
    best = (a0, l0, total(a0, l0))
    da = -coarse
    while da <= coarse + 1e-9:
        dl = -coarse
        while dl <= coarse + 1e-9:
            a, l = a0 + da, l0 + dl
            if 0.0 <= a <= 1.0 and 0.0 <= l <= 1.0:
                e = total(a, l)
                if e < best[2]:
                    best = (a, l, e)
            dl += fine
        da += fine

    return best


# ---------------------------------------------------------------- 预设

THEMES = {
    # tint 色: Mica Base 用 SolidBackgroundFillColorBase, Mica Alt 用 ...BaseAlt
    "light": {"base": "F3F3F3", "alt": "DADADA"},
    "dark": {"base": "202020", "alt": "0A0A0A"},
}

KNOWN_BASE = {
    "light": {"tint_op": 0.5, "lum_op": 1.0},
    "dark": {"tint_op": 0.8, "lum_op": 1.0},
}


# ---------------------------------------------------------------- CLI

def cmd_check(args):
    theme = args.theme
    b = hex_to_rgb(args.wallpaper)
    t = hex_to_rgb(THEMES[theme]["base"])
    p = KNOWN_BASE[theme]
    got = mica_forward(b, t, p["tint_op"], p["lum_op"], args.linear)
    obs = hex_to_rgb(args.observed)
    d = max(abs(got[i] - obs[i]) for i in range(3))
    print(f"主题          : {theme}")
    print(f"壁纸(纯色)   : #{args.wallpaper.upper()}")
    print(f"官方参数      : TintColor=#{THEMES[theme]['base']} "
          f"TintOpacity={p['tint_op']} LuminosityOpacity={p['lum_op']}")
    print(f"公式算出      : #{rgb_to_hex(got)}")
    print(f"你实际截到    : #{args.observed.upper()}")
    print(f"最大通道偏差  : {d * 255:.1f} / 255")
    print()
    if d * 255 > 12:
        print("偏差偏大,可能原因:")
        print("  1. 取色点落在内容层上(应取标题栏空白区)")
        print("  2. 窗口未激活 -> 官方已降级为纯色 fallback")
        print("  3. blend 空间不对,加 --linear 再试一次")
        print("  4. 壁纸不是纯色(纯色才能消掉模糊的影响)")
    else:
        print("链路一致,可以放心用这套公式反推 Mica Alt。")


def cmd_solve(args):
    theme = args.theme
    t = hex_to_rgb(THEMES[theme]["alt"])
    w1, o1 = hex_to_rgb(args.wallpaper1), hex_to_rgb(args.alt1)
    w2, o2 = hex_to_rgb(args.wallpaper2), hex_to_rgb(args.alt2)
    a, l, e = solve_two(w1, t, o1, w2, o2, args.linear)
    print(f"主题            : {theme}")
    print(f"Mica Alt 的 tint : #{THEMES[theme]['alt']}")
    print(f"壁纸1 #{args.wallpaper1.upper()} -> 实测 #{args.alt1.upper()}")
    print(f"壁纸2 #{args.wallpaper2.upper()} -> 实测 #{args.alt2.upper()}")
    print()
    print(f"  TintOpacity       = {a:.3f}")
    print(f"  LuminosityOpacity = {l:.3f}")
    print(f"  残差              = {e:.6f}")
    print()
    print("回填验证:")
    for w, o in ((w1, o1), (w2, o2)):
        got = mica_forward(w, t, a, l, args.linear)
        print(f"  #{rgb_to_hex(w)} -> 算得 #{rgb_to_hex(got)} / 实测 #{rgb_to_hex(o)}")


def cmd_forward(args):
    b = hex_to_rgb(args.wallpaper)
    t = hex_to_rgb(args.tint)
    got = mica_forward(b, t, args.tint_op, args.lum_op, args.linear)
    print(f"壁纸 #{args.wallpaper.upper()} + Tint #{args.tint.upper()} "
          f"(tint_op={args.tint_op}, lum_op={args.lum_op})")
    print(f"  -> #{rgb_to_hex(got)}")


def main():
    ap = argparse.ArgumentParser(description="Mica / Mica Alt 配方计算与反推")
    ap.add_argument("--linear", action="store_true", help="在线性空间做 blend(默认 sRGB)")
    sub = ap.add_subparsers(dest="cmd", required=True)

    c = sub.add_parser("check", help="用官方 Mica 参数自检公式链路")
    c.add_argument("--theme", choices=list(THEMES), required=True)
    c.add_argument("--wallpaper", required=True)
    c.add_argument("--observed", required=True)
    c.set_defaults(func=cmd_check)

    s = sub.add_parser("solve", help="反推 Mica Alt 的 tint_op / lum_op")
    s.add_argument("--theme", choices=list(THEMES), required=True)
    s.add_argument("--wallpaper1", required=True)
    s.add_argument("--alt1", required=True)
    s.add_argument("--wallpaper2", required=True)
    s.add_argument("--alt2", required=True)
    s.set_defaults(func=cmd_solve)

    f = sub.add_parser("forward", help="正向计算")
    f.add_argument("--wallpaper", required=True)
    f.add_argument("--tint", required=True)
    f.add_argument("--tint-op", type=float, required=True)
    f.add_argument("--lum-op", type=float, required=True)
    f.set_defaults(func=cmd_forward)

    args = ap.parse_args()
    try:
        args.func(args)
    except ValueError as ex:
        print(f"输入错误: {ex}", file=sys.stderr)
        sys.exit(2)


if __name__ == "__main__":
    main()
