#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""示例文件的轻量语法冒烟：**括号配平**。

用法：
    python tools/parity/check_brackets.py [文件.cs ...]
    不带参数时检查全部 ``samples/Reactor.Gallery/Gallery/Samples/*.cs``。

为什么要有它：
    UWP 示例工程**不能用 CLI 随手 build**—— `dotnet build` 的增量清理会把 VS 生成的
    MSIX 布局删掉，app 就起不来了（踩过一次）。可改动示例又不想"盲改"，
    于是用这道最便宜的闸：跳掉字符串与注释之后数括号，挡住"少一个右括号"
    这种低级事故。

它不做的事：
    不做语义检查（方法存不存在、重载对不对都要真编译才知道）。
    所以跑绿了不等于能编译，只是**不会因为少一个括号而编译不过**。
"""

from __future__ import annotations

import sys
from pathlib import Path

BACKSLASH = chr(92)
PAIRS = {")": "(", "]": "[", "}": "{"}
OPENERS = "([{"
CLOSERS = ")]}"


def check(path: Path) -> str | None:
    """返回 None 表示通过，否则返回一句人话。"""
    text = path.read_text(encoding="utf-8")
    stack: list[tuple[str, int]] = []
    i, n, line = 0, len(text), 1

    while i < n:
        c = text[i]

        if c == "\n":
            line += 1
            i += 1
            continue

        if text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue

        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            # 块注释里的换行也要算，否则报错行号会偏。
            line += text.count("\n", i, n if j < 0 else j)
            i = n if j < 0 else j + 2
            continue

        if c == '"':
            i += 1
            while i < n and text[i] != '"':
                if text[i] == BACKSLASH:
                    i += 1
                if i < n and text[i] == "\n":
                    line += 1
                i += 1
            i += 1
            continue

        if c == "'":
            # 字符字面量：`'a'` 与 `'\''`，这里只需要正确跨过去。
            i += 1
            if i < n and text[i] == BACKSLASH:
                i += 1
            i += 1
            if i < n and text[i] == "'":
                i += 1
            continue

        if c in OPENERS:
            stack.append((c, line))
        elif c in CLOSERS:
            if not stack or stack[-1][0] != PAIRS[c]:
                expected = stack[-1][0] if stack else "空"
                return f"{path.name}:{line}　多出来的 '{c}'（这里应该配 '{expected}'）"
            stack.pop()

        i += 1

    if stack:
        return f"{path.name}:　有 {len(stack)} 个没闭合，最早是第 {stack[0][1]} 行的 '{stack[0][0]}'"

    return None


def main() -> int:
    args = sys.argv[1:]
    if args:
        files = [Path(a) for a in args]
    else:
        root = Path(__file__).resolve().parents[2]
        files = sorted((root / "samples/Reactor.Gallery/Gallery/Samples").glob("*.cs"))

    bad = 0
    for f in files:
        problem = check(f)
        if problem:
            print(f"❌ {problem}")
            bad += 1
        elif args:
            print(f"✅ {f.name}")

    if not args:
        print(f"检查 {len(files)} 个示例文件：{'全部配平' if bad == 0 else f'{bad} 个不配平'}")

    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(main())
