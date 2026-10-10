#!/usr/bin/env python3
"""用**编译器**判定"官方图库里某个控件，本机 UWP + WinUI 2 到底有没有这个类型"。

为什么不用 winmd 字符串搜索
----------------------------
`coverage.py` 那一步是在 winmd 的 `#Strings` 流里搜类型名。它能给出线索，
但**分不清 TypeDef 和 TypeRef**：一个名字出现在元数据里，可能是"本程序集定义了它"，
也可能只是"某个方法签名引用了它"。导航控件在 UWP 契约里也能搜到 `NavigationView`
就是这么来的 —— 噪声。

所以这里换终审：生成一行行 `typeof(全名)`，让 C# 编译器自己说有没有。
`CS0246`（未能找到类型或命名空间名）就是硬证据，比字符串匹配可靠。

这决定了后面归类时说的是"WinUI 3 独有"还是"**本机有、我们没暴露**"
—— 后者是框架的可补清单，两者的后续动作完全不同，不能含糊。

用法::

    python tools/parity/probe_types.py

依赖 `coverage.py` 给出的未覆盖名单，以及 `%TEMP%/reactor-parity-build` 那个
隔离工程（有正确的 UWP + WinUI 2 引用）。跑完会把探针文件删掉，不留残留。
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import coverage as cov  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
PROBE_DIR = Path.home() / "AppData/Local/Temp/reactor-parity-build"
PROBE_FILE = PROBE_DIR / "TypeProbe.g.cs"
RESULT = Path(__file__).resolve().parent / "type-probe.json"

# 一个类型可能落在这几个命名空间里；逐个试，只要有一个编译过就算"本机有"。
NAMESPACES = (
    "Microsoft.UI.Xaml.Controls",
    "Windows.UI.Xaml.Controls",
    "Windows.UI.Xaml.Controls.Maps",
    "Microsoft.UI.Xaml.Media",
    "Windows.UI.Xaml.Media",
)


def candidates() -> list[str]:
    """官方有、本地没示例、且不是教学主题的 —— 也就是"可能是个真控件"的那些。"""
    return [n for n in cov.read_official() if n not in cov.COVERED and n not in cov.TOPICS]


def emit(names: list[str]) -> dict[int, tuple[str, str]]:
    """生成探针源码，返回 {行号: (控件名, 命名空间)}。"""
    mapping: dict[int, tuple[str, str]] = {}
    lines = [
        "// 自动生成，勿手改：类型存在性探针（tools/parity/probe_types.py）。",
        "// 每一行 typeof 一个候选类型；编译报 CS0246 的那行 = 本机没有这个类型。",
        "namespace Reactor.Gallery.Probe;",
        "",
        "internal static class TypeProbe",
        "{",
        "    public static void Run()",
        "    {",
    ]
    for name in names:
        for ns in NAMESPACES:
            lines.append(f"        _ = typeof(global::{ns}.{name});")
            mapping[len(lines)] = (name, ns)
    lines += ["    }", "}", ""]
    PROBE_FILE.write_text("\n".join(lines), encoding="utf-8")
    return mapping


def compile_probe() -> str:
    proc = subprocess.run(
        [
            "dotnet", "build", "Reactor.Gallery.csproj",
            "-c", "Debug", "-p:Platform=x64",
            "-p:AppxPackageSigningEnabled=False",
            "-p:GenerateTemporaryStoreCertificate=False",
            "-p:AppxBundle=Never",
        ],
        cwd=PROBE_DIR,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    return (proc.stdout or "") + (proc.stderr or "")


def main() -> int:
    if not PROBE_DIR.exists():
        print(f"⚠ 隔离工程不存在：{PROBE_DIR}\n  先跑一次 tools/parity/build_probe.py。")
        return 1

    names = candidates()
    mapping = emit(names)
    print(f"探针：{len(names)} 个候选 × {len(NAMESPACES)} 个命名空间 = {len(mapping)} 行\n")

    try:
        output = compile_probe()
    finally:
        PROBE_FILE.unlink(missing_ok=True)  # 不留残留，免得污染下次 build_probe

    # 两种错误码都要认，漏一种就会把整表判反：
    #   CS0246 = 命名空间本身不存在（如 Windows.UI.Xaml.Controls.Maps 没被引用到）
    #   CS0234 = 命名空间在，但里面没这个类型（如 Microsoft.UI.Xaml.Controls 里没 AppWindow）
    # 一开始只认 CS0246，结果 25 个候选全部"存在" —— 全是 CS0234 漏判出来的假阳性。
    missing_lines: set[int] = set()
    for m in re.finditer(
        r"TypeProbe\.g\.cs\((\d+),\s*\d+\):\s*(?:error|错误)\s+CS02(?:34|46)", output
    ):
        missing_lines.add(int(m.group(1)))

    found: dict[str, list[str]] = {}
    for line, (name, ns) in mapping.items():
        if line not in missing_lines:
            found.setdefault(name, []).append(ns)

    has, hasnt = [], []
    for name in names:
        (has if name in found else hasnt).append(name)

    print(f"── 本机**有**此类型（{len(has)}）→ 框架没暴露工厂，属于可补清单")
    for n in has:
        print(f"   {n:22s} {' / '.join(found[n])}")
    print()
    print(f"── 本机**没有**此类型（{len(hasnt)}）→ WinUI 3 / WASDK 独有")
    print("   " + "、".join(hasnt))
    print()

    # 落一份 JSON：coverage.py 读它做终审分类，避免那边再靠 winmd 字符串猜。
    RESULT.write_text(
        json.dumps({"found": found, "missing": hasnt}, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    print(f"结果已写入 {RESULT.relative_to(ROOT)}")

    if not missing_lines and has:
        print("（注意：一次 CS0246 都没有，检查探针是不是真的参与编译了。）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
