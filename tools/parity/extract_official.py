#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""把 WinUI 3 Gallery 里「真正决定像素」的那一段 XAML 抽出来。

用法：
    python tools/parity/extract_official.py [--src DIR] [--out DIR]

为什么需要它：
    官方每个控件目录下有两种源码，别抽错——
      * ``<Name>Page.xaml``          —— 真正跑起来的那棵树（含 Width/Height/Spacing/
                                       Margin/Style 等一切影响像素的属性），
                                       ``controls:ControlExample.Example`` 里那一段才是本体；
      * ``*.txt``                    —— 只是页面下方「源码展示」用的**简化片段**，
                                       故意省掉了尺寸与排版属性，照它对齐必然偏。
    所以本脚本只抽 Page.xaml 里 Example 那一段，txt 一概不碰。

输出：
    --out 目录下每个控件一个 ``<Control>.md``：
        例 1  <HeaderText 或 SampleDefinition>
        ---- xaml
        <Example 里的元素，XML 规范化后带缩进>
    另外写一份 ``_index.md``：控件名 → 示例条数，便于先看全貌再挑着读。
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# XAML 里这一串是 ControlExample 的命名空间前缀，官方换过两次名字，两个都认。
CE_TAGS = ("ControlExample", "ControlExampleSample")

WS_RE = re.compile(r"[ \t]+")


def strip_ns(tag: str) -> str:
    """`{http://...}ControlExample` → `ControlExample`。"""
    return tag.split("}", 1)[1] if "}" in tag else tag


def normalize(text: str) -> str:
    """去空行、去行尾空白；顺手把 `x:Name="Control1"` 这类噪音留着（它对像素没影响，
    但删了会让人怀疑是不是漏了属性，留着反而更好核对）。"""
    lines = [ln.rstrip() for ln in text.splitlines()]
    return "\n".join(ln for ln in lines if ln.strip())


def dedent(text: str) -> str:
    """ET 序列化出来的是**原文件里的缩进**，整段带着十几个空格的前导，读起来很累。
    这里按最小公共缩进统一左移（只动行首空白，不动属性内部的空格）。"""
    lines = text.splitlines()
    # 第一行是根元素（缩进恒为 0），把它算进去最小值就成了 0，等于没 dedent。
    rest = lines[1:]
    indents = [len(ln) - len(ln.lstrip(" ")) for ln in rest if ln.strip()]
    if not indents:
        return text
    cut = min(indents)
    return "\n".join(
        [lines[0]] + [ln[cut:] if len(ln) >= cut else ln for ln in rest])


def is_control_example(elem: ET.Element) -> bool:
    return strip_ns(elem.tag) in CE_TAGS


def find_example(elem: ET.Element) -> list[ET.Element]:
    """ControlExample 里「真正渲染」的那一段，官方有两种写法，都要认：

    1. 老写法：塞在属性元素 `<controls:ControlExample.Example>` 里；
    2. 新写法：直接当 ControlExample 的内容放（`<controls:ControlExample>` 下面
       就是 StackPanel 那一棵树）。

    第 2 种要注意**别把选项面板当成示例**：`.Output` / `.Options` /
    `.Substitutions` / `.Xaml` 这些属性元素是带点的，按"tag 里有没有点"就能分开。
    """
    for child in elem:
        if strip_ns(child.tag) in ("ControlExample.Example", "Example"):
            return list(child)

    return [c for c in elem if "." not in strip_ns(c.tag)]


def header_of(elem: ET.Element) -> str:
    """标题：优先 HeaderText，其次 SampleDefinition（指向那个 txt），都没有就给序号。"""
    for key in ("HeaderText", "SampleDefinition", "x:Name"):
        if key in elem.attrib:
            return elem.attrib[key]
    return "(无标题)"


def dump(elem: ET.Element) -> str:
    """把一个元素（及其子树）序列化成可读 XAML。

    官方原文件里 xmlns 声明全在根上，这里每个片段单独输出，所以要把
    默认命名空间补回去，否则读起来像是没有命名空间的元素。
    """
    ET.register_namespace("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation")
    ET.register_namespace("x", "http://schemas.microsoft.com/winfx/2006/xaml")
    raw = ET.tostring(elem, encoding="unicode")
    return normalize(raw)


def collect(xaml: Path) -> list[tuple[str, str]]:
    """返回 [(标题, 示例 XAML)]。"""
    try:
        root = ET.parse(xaml).getroot()
    except ET.ParseError as exc:
        print(f"  跳过（XML 解析失败：{exc}）：{xaml.name}", file=sys.stderr)
        return []

    out: list[tuple[str, str]] = []
    for elem in root.iter():
        if not is_control_example(elem):
            continue
        children = find_example(elem)
        if not children:
            continue
        body = "\n".join(dedent(dump(c)) for c in children)
        out.append((header_of(elem), body))
    return out


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--src",
        default=r"D:\fluentapps\repos\references\WinUI-Gallery\WinUIGallery\Samples",
        help="官方 Samples 目录（sparse clone 出来的那份）")
    parser.add_argument(
        "--out",
        default=r"D:\fluentapps\repos\test\ReactorForUWP\tools\parity\official",
        help="输出目录")
    args = parser.parse_args()

    src = Path(args.src)
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    if not src.is_dir():
        print(f"源目录不存在：{src}", file=sys.stderr)
        print("先 clone：git clone --depth 1 --filter=blob:none --sparse "
              "https://github.com/microsoft/WinUI-Gallery.git", file=sys.stderr)
        return 2

    index: list[str] = []
    written = 0
    for page in sorted(src.glob("*/*Page.xaml")):
        control = page.parent.name
        samples = collect(page)
        if not samples:
            continue

        lines = [f"# {control}", ""]
        for i, (header, body) in enumerate(samples, 1):
            lines.append(f"## 例 {i}　{header}")
            lines.append("")
            lines.append("```xaml")
            lines.append(body)
            lines.append("```")
            lines.append("")

        (out_dir / f"{control}.md").write_text("\n".join(lines), encoding="utf-8")
        index.append(f"- {control}　（{len(samples)} 例）")
        written += 1

    (out_dir / "_index.md").write_text(
        "\n".join(["# 官方示例索引（只含 Page.xaml 的 Example 段）", ""] + index) + "\n",
        encoding="utf-8")

    print(f"写出 {written} 个控件的示例片段 → {out_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
