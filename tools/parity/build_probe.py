"""**隔离编译**示例工程，用来在没有 AppX 副作用的前提下拿到真正的类型检查。

为什么不能直接 `dotnet build samples/Reactor.Gallery`：
它是打包 UWP 应用（`<OutputType>WinExe</OutputType>` + `EnableMsixTooling`），
CLI 构建的增量清理会删掉 VS 生成的 MSIX 布局，之后 app 就起不来了。

怎么做：整个工程（含 csproj）复制到 `%TEMP%/reactor-parity-build`，
把 `<ProjectReference>` 的相对路径换成框架工程的绝对路径后在那里编译。
改的是副本，原地那份 AppX 一个字节都不动。

用法：
    python tools/parity/build_probe.py            # 复制 + 编译
    python tools/parity/build_probe.py --sync     # 源文件已改过，增量同步后再编译
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SAMPLE = ROOT / "samples" / "Reactor.Gallery"
FRAMEWORK_PROJ = ROOT / "Reactor.uwp" / "Reactor.uwp.csproj"
PROBE_NAME = "reactor-parity-build"

# 要搬过去的顶层条目：非 Source/Assets 的一律带上，否则编译会因为缺文件失败。
COPY_ITEMS = [
    "Gallery",
    "Pages",
    "Assets",
    "Strings",
    "Properties",
    "App.cs",
    "AppSettings.cs",
    "SampleShell.cs",
    "SoundService.cs",
    "Package.appxmanifest",
]


def probe_dir() -> Path:
    return Path(tempfile.gettempdir()) / PROBE_NAME


def prepare() -> Path:
    target = probe_dir()
    target.mkdir(parents=True, exist_ok=True)

    for item in COPY_ITEMS:
        src = SAMPLE / item
        dst = target / item
        if src.is_dir():
            if dst.exists():
                shutil.rmtree(dst)
            shutil.copytree(src, dst)
        elif src.exists():
            shutil.copy2(src, dst)

    # 工程引用要改成绝对路径：副本在原目录之外，相对路径会指向不存在的地方。
    proj = target / "Reactor.Gallery.csproj"
    raw = proj.read_bytes()
    # 用正向斜杠，避免 XML 里的反斜杠在各种转义层里被吃掉。
    old = b".." + b"\\" + b".." + b"\\" + b"Reactor.uwp" + b"\\" + b"Reactor.uwp.csproj"
    new = str(FRAMEWORK_PROJ).replace("\\", "/").encode()
    if old in raw:
        proj.write_bytes(raw.replace(old, new))
    elif new not in raw:
        print("⚠️ 没在工程文件里找到框架的项目引用，请检查 COPY_ITEMS 是否漏了 csproj", file=sys.stderr)

    return proj


def build(proj: Path) -> int:
    cmd = [
        "dotnet", "build", str(proj),
        "-c", "Debug",
        "-p:Platform=x64",
        # 关掉证书自动签发：临时目录里没有 VS  Generated 的证书，留着会在打包那一步失败。
        "-p:AppxPackageSigningEnabled=False",
        "-p:GenerateTemporaryStoreCertificate=False",
        "-p:AppxBundle=Never",
    ]
    print("▶", " ".join(cmd))
    result = subprocess.run(cmd, cwd=proj.parent)
    return result.returncode


def main() -> int:
    parser = argparse.ArgumentParser(description="隔离编译 Gallery 示例工程")
    parser.add_argument(
        "--sync",
        action="store_true",
        help="副本已存在时只同步源文件（默认行为其实也会整体重删重拷）",
    )
    args = parser.parse_args()

    proj = prepare()
    print(f"副本目录：{probe_dir()}")
    sys.stdout.flush()
    return build(proj)


if __name__ == "__main__":
    raise SystemExit(main())
