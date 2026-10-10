"""命名参数校验：示例里写的 `foo: bar` 必须在框架或示例的定义里真有过这个形参名。

`check_api.py` 只比对**方法名**，抓不到"方法存在、形参名拼错"这一类——而这恰恰
是这种链式 API 最容易出的错：`InfoBadge(badgeStyle: …)` 和 `InfoBadge(style: …)`
肉眼几乎看不出区别，编译器一眼就红。本脚本把后者自动化。

做法：
1. 扫全部框架源码与示例源码，把每个括号里的形参名收进一张表；
2. 扫示例文件里 `name:` 形态的用法（排除 `case x:`、`?:`、`https:` 等干扰）；
3. 不在表里的列出来。

注意它是**保守**检查：形参名表做得很宽（宁可漏报也不误报），所以报告出来的
每一条都值得人去看一眼，而没报告不代表类型一定对（那要真的类型检查）。
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FRAMEWORK = ROOT / "Reactor.uwp"
SAMPLES = ROOT / "samples" / "Reactor.Gallery" / "Gallery" / "Samples"

# `name:` —— 前面不能是 `:` 或标识符字符（挡掉 `a::b`、`x:y`），后面不能再跟 `:`
NAMED_ARG_RE = re.compile(r"(?<![:\w])\b([a-zA-Z_]\w*)\s*:\s*(?![:\w])")

# 类型声明行：`partial class Foo : Component`（这个 `:` 不是命名参数）。
DECL_RE = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)?\s*"
    r"(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+|record\s+)*"
    r"(?:class|interface|struct|enum|record)\b"
)

# 形参 `int x, string y = null, Func<int> z` 里最后一个标识符就是名字。
PARAM_NAME_RE = re.compile(r"([a-zA-Z_]\w*)\s*=\s*(?:null|true|false|[A-Za-z_0-9\"'])")


def strip_noise(text: str) -> str:
    """去掉注释与字符串字面量。

    **必须先按行砍掉行注释**：文档注释里常有 `UpOnly 只放大、DownOnly 只缩小`
    这种"名字 + 冒号"的中文说明，如果混在处理完的字符串里再正则，就会被当成
    命名参数报出来一轮假阳性。
    """
    kept = [
        line
        for line in text.splitlines()
        if not line.lstrip().startswith("//") and not line.lstrip().startswith("///")
    ]
    text = "\n".join(kept)

    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        ch = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if ch in ('"', "'"):
            quote = ch
            i += 1
            while i < n and text[i] != quote:
                if text[i] == "\\":
                    i += 1
                i += 1
            i += 1
            out.append('""' if quote == '"' else "''")
            continue
        out.append(ch)
        i += 1
    return "".join(out)


def collect_sources() -> list[Path]:
    sources = list(FRAMEWORK.rglob("*.cs")) + list(SAMPLES.rglob("*.cs"))
    return [p for p in sources if "obj" not in p.parts and "bin" not in p.parts]


def build_param_table(sources: list[Path]) -> set[str]:
    params: set[str] = set()
    for path in sources:
        text = strip_noise(path.read_text(encoding="utf-8", errors="replace"))
        for paren in re.finditer(r"\(([^()]*)\)", text):
            inner = paren.group(1).strip()
            if not inner or ("," not in inner and " " not in inner and "=" not in inner):
                continue
            # `type name` 形式：取逗号分隔片段里最后一个标识符。
            for piece in inner.split(","):
                piece = piece.strip()
                if not piece or "=" in piece:
                    continue
                tokens = re.findall(r"\b([a-zA-Z_]\w*)\b", piece)
                if tokens:
                    params.add(tokens[-1])
            # 带默认值的：`bool foo = true` / `string? tag = null`
            for match in PARAM_NAME_RE.finditer(inner):
                params.add(match.group(1))

        # 多行形参列表上面那条收不全：参数里出现元组之类的嵌套括号时，
        # `\(([^()]*)\)` 会在内层的 `(double Offset, Color Color)` 处提前收尾，
        # 后面的 `Point? center = null,` 全被漏掉。这里按行补一遍。
        for line in text.splitlines():
            if ";" in line:
                continue
            match = re.match(
                r"^\s+(?:params\s+|this\s+|ref\s+|in\s+|out\s+)?"
                r"[A-Za-z_][\w\.\?<>,\[\]\(\) ]*?\s+([a-zA-Z_]\w*)\s*"
                r"(?:=\s*[^,]*)?[,)]\s*$",
                line,
            )
            if match:
                params.add(match.group(1))
    # 记录/构造函数式：`public record Foo(double Left = 0, …)` 已经覆盖；这里补
    # 几个被横结肠写法定死的常见键，避免 `"Score": 1` 这种字典初值误报。
    return params


def scan() -> int:
    sources = collect_sources()
    params = build_param_table(sources)
    bad: dict[str, list[str]] = {}
    checked = 0

    for path in sorted(SAMPLES.rglob("*.cs")):
        checked += 1
        text = strip_noise(path.read_text(encoding="utf-8", errors="replace"))
        # 去掉 `case X:` / `default:` 与 URL，它们不是命名参数。
        text = re.sub(r"\b(case|default)\b[^:\n]{0,40}:", " ", text)
        text = re.sub(r"https?:\S*", " ", text)
        unknown: set[str] = set()
        for line in text.splitlines():
            # 类型声明的基类/接口子句（`: Component`）长得和命名参数一模一样，
            # 必须整行排除，否则每个文件都会"报出"自己的类名。
            if DECL_RE.match(line):
                continue
            for match in NAMED_ARG_RE.finditer(line):
                # 三元表达式的 `? a : b` 与命名参数共用同一个冒号，用"冒号前
                # 同行有没有问号"来区分。
                if "?" in line[: match.start()]:
                    continue
                name = match.group(1)
                if name not in params:
                    unknown.add(name)
        if unknown:
            bad[path.name] = sorted(unknown)

    print(f"形参名表 {len(params)} 个，扫了 {checked} 个示例文件")
    if bad:
        print("\n以下命名参数在框架与示例的定义里找不到同名形参（逐条确认）：")
        for name, names in bad.items():
            print(f"  {name}: {', '.join(names)}")
        print(f"\n{len(bad)} 个文件存疑")
        return 1
    print("✅ 所有命名参数都能在定义里找到同名形参")
    return 0


if __name__ == "__main__":
    raise SystemExit(scan())
