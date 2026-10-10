"""示例文件的静态符号校验。

背景：示例工程**不能**用 CLI 构建（dotnet build 会删掉 VS 生成的 MSIX 布局，
app 会起不来），所以拿不到编译器的错误列表。这个脚本做力所能及的替代检查：

1. 从框架源码里建一张"可能被调用的方法名"表（Factories.* 的静态工厂、
   ElementExtensions.* 的链式修饰器，以及示例目录里自己定义的本地辅助方法）。
2. 扫每个示例文件里出现的调用（`.Foo(` 与 `Foo(`），看名字是不是在表里。
3. 不在表里的列出来——多数会是 BCL 的东西（ToString / Math.Round /
   Color.FromArgb …），用一张白名单筛掉；剩下的才值得人去看一眼。

它抓不住参数类型错配（那要真的类型检查），但能挡住"方法名根本不存在"
和"方法名拼错"这两类，而这恰好是手写大片声明式代码最常见的翻车方式。
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FRAMEWORK = ROOT / "Reactor.uwp"
SAMPLES = ROOT / "samples" / "Reactor.Gallery" / "Gallery" / "Samples"

# 提取方法定义名。**必须一行一行来**：整文件一把梭的话，前一个匹配可能横跨到
# 下一处定义中间（非重叠匹配的锅），像 `Caption` 这种紧凑的一行写法的扩展方法
# 会被整段吞掉，符号表反而漏掉最常用的那批 API。
DEF_LINE_RE = re.compile(
    r"^\s*(?:public|internal|protected|private|static)\b"
    r"[^=;]*?\b(\w+)\s*(?:<[^>]*>)?\s*\("
)
CALL_RE = re.compile(r"(?:\.\s*|\b)(\w+)\s*(?:<[^()]*>)?\s*\(")

# 示例文件里自己声明的 helper（常常连访问修饰符都不带，`void Pick(string s) => …`）。
LOCAL_DEF_RE = re.compile(
    r"^\s{4,}(?:(?:private|public|internal|protected|static|readonly|async|virtual|sealed|override)\s+)*"
    # 行末收尾允许 `=>`（表达式体）、`{`（同行开体）、或直接换行（body 在下一行）。
    r"[\w<>\[\]\.,\?\s]+?\s(\w+)\s*\([^;{]*\)\s*(?:=>|\{|$)"
)
CONTROL_KEYWORDS = {
    "if", "for", "foreach", "while", "switch", "catch", "using", "lock",
    "fixed", "return", "nameof", "typeof", "sizeof", "new", "else", "try",
    "do", "checked", "unchecked", "await", "throw", "yield", "case",
}

# BCL / 语言层面的常见调用，不算"用了框架里没有的 API"。
BUILTIN = {
    # object / string / 集合
    "ToString", "Equals", "GetHashCode", "Contains", "StartsWith", "EndsWith",
    "IndexOf", "LastIndexOf", "Substring", "Split", "Trim", "TrimStart", "TrimEnd",
    "Replace", "PadLeft", "PadRight", "ToUpper", "ToLower", "ToUpperInvariant",
    "Concat", "Join", "Format", "IsNullOrEmpty", "IsNullOrWhiteSpace",
    "Append", "AppendLine", "Clear", "CopyTo", "Insert", "Remove", "AddRange",
    "Select", "Where", "ToList", "ToArray", "First", "FirstOrDefault", "Last",
    "Any", "All", "Count", "Sum", "Min", "Max", "Average", "OrderBy",
    "OrderByDescending", "ThenBy", "Take", "Skip", "Distinct", "Reverse",
    "ContainsKey", "TryGetValue", "TryAdd", "Add", "RemoveAt", "Sort",
    # 数值 / 颜色 / 几何 / 常用构造函数（类型名不属于"框架的 API"，但会被当成调用）。
    "Round", "Floor", "Ceiling", "Abs", "Clamp", "Parse", "TryParse", "FromArgb",
    "FromRgb", "Pow", "Sqrt",
    "AddDays", "AddMonths", "AddYears", "Now", "Today", "UtcNow",
    "SelectMany", "ConvertAll", "Range", "Repeat", "Cast", "OfType", "Zip",
    "Append", "Prepend", "ElementAt", "Single", "SingleOrDefault",
    "Thickness", "CornerRadius", "GridLength", "Point", "Size", "Color",
    "SolidColorBrush", "LinearGradientBrush", "RadialGradientBrush", "AcrylicBrush",
    "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "Uri",
    "List", "Dictionary", "ObservableCollection", "Array", "Enumerable",
    "Task", "CancellationToken", "FontWeight", "FontFamily", "Symbol",
    # 本仓库示例里自造的、声明在同文件内的 helper（名字变动频繁，放过）
    "GetType", "CompareTo",
    # 本仓库的类型名（构造函数调用会被当成方法调用，这里放行）
    "Optional", "Windows", "Types", "Colors", "VirtualKey", "Orientation",
    "TextBlock", "Run", "Paragraph", "ResourceBinding", "KeyboardAcceleratorSpec",
}


def strip_comments_and_strings(text: str) -> str:
    """去掉注释与字符串字面量。

    不做这一步的话，`TextBlock("挪到 (40,40)")` 里的中文会被当成一次调用报出来，
    噪音大到没法看。代价是插值字符串 `$"...{Foo()}"` 里的调用逃过检查——那部分
    代码在示例里都是 `setXxx(...)` 之类的短表达式，出问题的概率可以忽略。
    """
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
        if ch == '"':
            i += 1
            while i < n and text[i] != '"':
                if text[i] == "\\":
                    i += 1
                i += 1
            i += 1
            out.append('""')
            continue
        if ch == "'":
            # 字符字面量（含转义），吃干净免得破坏后面的配对判断。
            i += 1
            while i < n and text[i] != "'":
                if text[i] == "\\":
                    i += 1
                i += 1
            i += 1
            out.append("''")
            continue
        out.append(ch)
        i += 1
    return "".join(out)


def build_symbol_table() -> set[str]:
    """建可用方法名表：框架源码 + 示例目录里自己声明的方法。"""
    names: set[str] = set()
    sources = list(FRAMEWORK.rglob("*.cs")) + list(SAMPLES.rglob("*.cs"))
    for path in sources:
        if "obj" in path.parts or "bin" in path.parts:
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for line in text.splitlines():
            stripped = line.strip()
            # 跳过注释行，免得把 XML doc 里的 `<summary><c>Foo(...)` 当成定义。
            if stripped.startswith("//") or stripped.startswith("///"):
                continue
            for pattern in (DEF_LINE_RE, LOCAL_DEF_RE):
                match = pattern.match(line)
                if match:
                    name = match.group(1)
                    if name not in CONTROL_KEYWORDS:
                        names.add(name)
                    break
    return names


def scan() -> int:
    symbols = build_symbol_table()
    problems: list[str] = []

    for path in sorted(SAMPLES.rglob("*.cs")):
        text = strip_comments_and_strings(path.read_text(encoding="utf-8", errors="replace"))

        unknown: dict[str, int] = {}
        for match in CALL_RE.finditer(text):
            name = match.group(1)
            if name in BUILTIN or name in symbols:
                continue
            # 首字母小写的多半是局部变量/字段名或语言关键字，放过。
            if name[:1].islower():
                continue
            unknown[name] = unknown.get(name, 0) + 1

        if unknown:
            listed = "、".join(f"{n}×{c}" for n, c in sorted(unknown.items()))
            problems.append(f"  {path.name}: {listed}")

    print(f"符号表里共 {len(symbols)} 个方法名，扫了 {len(list(SAMPLES.rglob('*.cs')))} 个示例文件")
    if problems:
        print("\n以下调用在框架源码与示例里都找不到定义（请逐个确认是不是拼错）：")
        problems = problems[:40]
        for line in problems:
            print(line)
        print(f"\n共 {len(problems)} 个文件存疑")
        return 1
    print("✅ 所有调用都能在框架或示例自身找到定义")
    return 0


if __name__ == "__main__":
    raise SystemExit(scan())
