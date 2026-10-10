"""生成本画廊示例用的替代素材，输出到 `samples/Reactor.Gallery/Assets/SampleMedia/`。

## 为什么自己生成

官方 WinUI 3 Gallery 的示例引了一整套 `/Assets/SampleMedia/*`（treetops.jpg、
valley.jpg、Slices.png、ninegrid.gif、animated.gif …），这些内容**不在本地仓库**，
也不适合直接搬。缺素材的直接后果不是"少个文件"，而是**六个示例全指着同一个
应用 logo**（`Square150x150Logo.scale-100.png`）——于是 Stretch 三档、滚动、
头像、图标这几档的差别全部看不出来，对齐到 pixels 也没意义。

所以这里按需求"造"几张图，只求三件事：
1. **形状对**——横图 / 高图 / 方头像 / 透明小图标，比例照官方那几档；
2. **一眼看得出**——Stretch 差别、滚动是否真的溢出，靠它们才看得出来；
3. **可复现**——每张图都由这个脚本按固定算法生成，不是从哪拷来的不明二进制。

只用标准库：手写 PNG（zlib + struct），**不依赖 pillow**。
UWP 的 `Image` 对 PNG 支持完整（含 alpha），不需要 JPEG。

    python tools/parity/make_sample_media.py
"""

from __future__ import annotations

import math
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "samples" / "Reactor.Gallery" / "Assets" / "SampleMedia"


# ── PNG 编码（最小实现：8-bit RGBA，filter 0，单 IDAT） ──────────────
def write_png(path: Path, pixels: bytearray, width: int, height: int) -> None:
    """像素按行主序排列，每像素 4 字节 RGBA。"""
    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)  # filter type 0 = None
        raw += pixels[y * stride : (y + 1) * stride]

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)  # 8bit, colour type 6 = RGBA
    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )
    path.write_bytes(png)


class Canvas:
    """够用就好的画图原语：只有填充、渐变、圆、三角。"""

    def __init__(self, width: int, height: int) -> None:
        self.w = width
        self.h = height
        self.px = bytearray(width * height * 4)

    def blend(self, x: int, y: int, r: int, g: int, b: int, a: int = 255) -> None:
        if not (0 <= x < self.w and 0 <= y < self.h) or a <= 0:
            return
        i = (y * self.w + x) * 4
        sa = a / 255.0
        dr, dg, db, da = self.px[i], self.px[i + 1], self.px[i + 2], self.px[i + 3] / 255.0
        oa = sa + da * (1 - sa)
        if oa <= 0:
            return
        self.px[i] = int((r * sa + dr * da * (1 - sa)) / oa)
        self.px[i + 1] = int((g * sa + dg * da * (1 - sa)) / oa)
        self.px[i + 2] = int((b * sa + db * da * (1 - sa)) / oa)
        self.px[i + 3] = int(oa * 255)

    def vgradient(self, top: tuple[int, int, int], bottom: tuple[int, int, int]) -> None:
        for y in range(self.h):
            t = y / max(1, self.h - 1)
            r = int(top[0] + (bottom[0] - top[0]) * t)
            g = int(top[1] + (bottom[1] - top[1]) * t)
            b = int(top[2] + (bottom[2] - top[2]) * t)
            for x in range(self.w):
                self.blend(x, y, r, g, b, 255)

    def disc(self, cx: float, cy: float, radius: float, colour: tuple[int, int, int], alpha: int = 255) -> None:
        r0, g0, b0 = colour
        for y in range(int(cy - radius) - 1, int(cy + radius) + 2):
            for x in range(int(cx - radius) - 1, int(cx + radius) + 2):
                d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
                if d <= radius:
                    # 边缘一圈做半像素柔和，免得锯齿刺眼。
                    edge = min(1.0, radius - d + 0.5)
                    self.blend(x, y, r0, g0, b0, int(alpha * edge))

    def triangle(self, p0, p1, p2, colour: tuple[int, int, int], alpha: int = 255) -> None:
        def sign(a, b, c) -> float:
            return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])

        xs = [p[0] for p in (p0, p1, p2)]
        ys = [p[1] for p in (p0, p1, p2)]
        for y in range(int(min(ys)) - 1, int(max(ys)) + 2):
            for x in range(int(min(xs)) - 1, int(max(xs)) + 2):
                pt = (x + 0.5, y + 0.5)
                d1, d2, d3 = sign(p0, p1, pt), sign(p1, p2, pt), sign(p2, p0, pt)
                inside = (d1 >= 0 and d2 >= 0 and d3 >= 0) or (d1 <= 0 and d2 <= 0 and d3 <= 0)
                if inside:
                    self.blend(x, y, colour[0], colour[1], colour[2], alpha)

    def grid_lines(self, step: int, colour: tuple[int, int, int], width: int = 1) -> None:
        """淡淡的世界线：让 Stretch 的形变（尤其 Fill）看得出来。"""
        for y in range(0, self.h, step):
            for t in range(width):
                for x in range(self.w):
                    self.blend(x, y + t, colour[0], colour[1], colour[2], 60)
        for x in range(0, self.w, step):
            for t in range(width):
                for y in range(self.h):
                    self.blend(x + t, y, colour[0], colour[1], colour[2], 60)

    def save(self, path: Path) -> None:
        write_png(path, self.px, self.w, self.h)


def landscape_photo(path: Path, w: int = 400, h: int = 267) -> None:
    """横向风景：竖向渐变打底 + 太阳 + 山形 + 网格线。

    网格线是刻意的：`Stretch="Fill"` 会把网格拉歪，一眼就看出"变形了"，
    这正是 Image 那三档要演示的事。
    """
    c = Canvas(w, h)
    c.vgradient((126, 200, 227), (60, 110, 60))
    c.disc(w * 0.78, h * 0.22, h * 0.12, (255, 236, 160), 220)
    c.triangle((w * 0.08, h * 0.72), (w * 0.42, h * 0.28), (w * 0.76, h * 0.72), (72, 96, 66))
    c.triangle((w * 0.52, h * 0.75), (w * 0.80, h * 0.38), (w * 1.05, h * 0.75), (58, 80, 56))
    c.grid_lines(step=max(4, w // 20), colour=(255, 255, 255))
    c.save(path)


def tall_photo(path: Path, w: int = 240, h: int = 720) -> None:
    """高图：给 ScrollViewer 用的内容——它就靠这张图证明"内容确实溢出了"。"""
    c = Canvas(w, h)
    c.vgradient((32, 36, 74), (186, 104, 120))
    for band in range(6):
        y = h * band / 6.0
        c.disc(w * 0.5, y + h / 12.0, w * 0.18, (255, 255, 255), 30)
    c.grid_lines(step=24, colour=(255, 255, 255))
    c.save(path)


def avatar(path: Path, size: int = 128) -> None:
    """头像：PersonPicture 那档用，带 alpha（不是一块白底方块）。"""
    c = Canvas(size, size)
    c.disc(size / 2, size / 2, size * 0.48, (78, 92, 168), 60)
    c.disc(size / 2, size * 0.38, size * 0.17, (250, 250, 252))
    # 肩部用一个扁椭圆近似（Canvas 里没有 ellipse，用两个半圆 + 矩形糊一下）。
    cy = size * 0.80
    c.disc(size / 2, cy, size * 0.26, (250, 250, 252), 255)
    for x in range(int(size * 0.24), int(size * 0.76)):
        for y in range(int(cy), int(cy + size * 0.10)):
            c.blend(x, y, 250, 250, 252, 255)
    c.save(path)


def slices_icon(path: Path, size: int = 48) -> None:
    """官方 `Slices.png` 的替代品：六片扇叶，透明背景。

    图标类示例用它而不是应用 logo——logo 是应用自己的身份标识，
    拿它当"随便一个图标"演示会让示例语义跑偏。
    """
    c = Canvas(size, size)
    cx = cy = size / 2
    r = size * 0.46
    colours = [
        (0, 120, 212), (16, 137, 62), (255, 185, 0),
        (196, 43, 28), (136, 23, 152), (0, 153, 188),
    ]
    for i, colour in enumerate(colours):
        a0 = 2 * math.pi * i / len(colours)
        a1 = 2 * math.pi * (i + 0.86) / len(colours)
        c.triangle(
            (cx, cy),
            (cx + r * math.cos(a0), cy + r * math.sin(a0)),
            (cx + r * math.cos(a1), cy + r * math.sin(a1)),
            colour,
            255,
        )
    c.save(path)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    jobs = [
        ("treetops.png", lambda p: landscape_photo(p)),
        ("tall-cliff.png", lambda p: tall_photo(p)),
        ("avatar.png", lambda p: avatar(p)),
        ("slices.png", lambda p: slices_icon(p)),
    ]
    for name, job in jobs:
        target = OUT / name
        job(target)
        print(f"✅ {target.relative_to(ROOT)}  {target.stat().st_size / 1024:.1f} KB")
    print(f"\n输出目录：{OUT}")


if __name__ == "__main__":
    main()
