#!/usr/bin/env python3
"""官方图库 121 个控件 ↔ 本地示例的**覆盖矩阵**。

为什么要做这个
--------------
逐属性对齐（`docs/parity/winui3-gallery.md` 前几节）只回答"已有的示例写得像不像"。
它回答不了**覆盖面**：官方画廊里那些我们压根没有示例的控件，到底是什么情况？

"不适用"是个容易滥用的词——说一句"这是 WinUI 3 才有的"很省事，但没有证据。
所以这里不去猜，而是拿**元数据**说话：把候选类型名分别丢进

* `Microsoft.UI.Xaml.winmd`（WinUI 2.8.7，Mux 命名空间）
* `Windows.Foundation.UniversalApiContract.winmd`（UWP 平台控件）

按 UTF-16 定界符精确匹配（前后各两个 `\0`），命中即该类型在本机工具链里真实存在。
于是每条结论都能归到三类之一，而不是糊成一句"不适用"：

* ``MUX2`` / ``UWP`` —— **类型存在，本框架没暴露工厂**。这是框架侧的可补清单。
* ``无此类型`` —— 两边都没有。结合名字判是"WinUI 3/WASDK 独有"还是
  "官方那页根本不是控件（是教学主题）"。

用法::

    python tools/parity/coverage.py            # 打印矩阵
    python tools/parity/coverage.py --md       # 顺便写出 markdown 片段

新增控件后重跑即可，不用手工维护这份分类。
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OFFICIAL_INDEX = ROOT / "tools/parity/official/_index.md"
SAMPLE_INDEX = ROOT / "samples/Reactor.Gallery/Gallery/SampleIndex.cs"
FACTORIES = ROOT / "Reactor.uwp/Elements"

MUX_WINMD = (
    Path.home()
    / ".nuget/packages/microsoft.ui.xaml/2.8.7/lib/uap10.0/Microsoft.UI.Xaml.winmd"
)
UWP_WINMD = (
    Path("C:/Program Files (x86)/Windows Kits/10/References/10.0.26100.0")
    / "Windows.Foundation.UniversalApiContract/19.0.0.0"
    / "Windows.Foundation.UniversalApiContract.winmd"
)

# ---------------------------------------------------------------- 官方 → 本地
# 官方控件页 → 承载它的本地示例（一个控件可能被多个示例分档演示）。
# 键是 official/_index.md 里的控件名；值里的每一项都要在 SampleIndex 里真注册过。
COVERED: dict[str, tuple[str, ...]] = {
    "Acrylic": ("AcrylicBrushBasic",),
    "AppBarButton": ("AppBarButtonBasic",),
    # 官方 AppBarSeparator 页只有一例（分隔线分隔按钮组），本地没给它单开一页，
    # 而是在 AppBarButton 页里放了两根 —— 形态一致，算覆盖。
    "AppBarSeparator": ("AppBarButtonBasic",),
    "AppBarToggleButton": ("AppBarToggleButtonBasic",),
    "AutoSuggestBox": ("AutoSuggestBasic",),
    "Border": ("BorderBasic",),
    "BreadcrumbBar": ("BreadcrumbBarBasic",),
    "Button": ("ButtonBasic", "ButtonVariants"),
    "CalendarDatePicker": ("CalendarDatePickerBasic",),
    "CalendarView": ("CalendarViewBasic",),
    "Canvas": ("CanvasBasic",),
    "CheckBox": ("CheckBoxBasic",),
    "ColorPicker": ("ColorPickerBasic",),
    "ComboBox": ("ComboBoxBasic",),
    "CommandBar": ("CommandBarBasic",),
    "CommandBarFlyout": ("CommandBarFlyoutBasic", "TextCommandBarFlyoutBasic"),
    "ContentDialog": ("ContentDialogBasic",),
    "DatePicker": ("DatePickerBasic",),
    "DropDownButton": ("DropDownButtonBasic",),
    "Expander": ("ExpanderBasic",),
    "FlipView": ("FlipViewBasic",),
    "Flyout": ("FlyoutBasic",),
    "Grid": ("LayoutGrid",),
    "GridView": ("GridViewBasic",),
    "HyperlinkButton": ("HyperlinkBasic",),
    "IconElement": ("IconsBasic",),
    "Image": ("ImageBasic",),
    "InfoBadge": ("InfoBadgeBasic",),
    "InfoBar": ("InfoBarBasic",),
    "ItemsRepeater": ("VirtualizingListBasic",),
    "Line": ("LineBasic",),
    "ListView": ("ListViewBasic",),
    "MenuBar": ("MenuBarBasic",),
    "MenuFlyout": ("MenuFlyoutBasic",),
    "NavigationView": ("NavigationViewBasic",),
    "NumberBox": ("NumberBoxOptions",),
    "ParallaxView": ("ParallaxViewBasic",),
    "PasswordBox": ("PasswordBoxBasic",),
    "PersonPicture": ("PersonPictureBasic",),
    "PipsPager": ("PipsPagerBasic",),
    "Pivot": ("PivotBasic",),
    "Popup": ("PopupBasic",),
    "ProgressBar": ("ProgressBarBasic",),
    "ProgressRing": ("ProgressRingBasic",),
    "PullToRefresh": ("RefreshContainerBasic",),
    "RadialGradientBrush": ("RadialGradientBrushBasic",),
    "RadioButton": ("RadioButtonsBasic",),
    "RatingControl": ("RatingBasic",),
    "RelativePanel": ("RelativePanelBasic",),
    "RepeatButton": ("RepeatButtonBasic",),
    "RichEditBox": ("RichEditBoxBasic",),
    "RichTextBlock": ("RichTextBasic",),
    "ScrollViewer": ("ScrollViewerBasic",),
    "SemanticZoom": ("SemanticZoomBasic",),
    "Shape": ("ShapesBasic",),
    "Slider": ("SliderBasic", "SliderTicks"),
    "SplitButton": ("SplitButtonBasic",),
    "SplitView": ("SplitViewBasic", "SplitViewPaneBackground"),
    "StackPanel": ("StackPanelBasic",),
    "SwipeControl": ("SwipeControlBasic",),
    "TabView": ("TabViewBasic",),
    "TeachingTip": ("TeachingTipBasic",),
    "TextBlock": ("TextBlockStyles", "TextBlockFormatting"),
    "TextBox": ("TextBoxBasic", "TextBoxControlled", "TextBoxOptions"),
    "TimePicker": ("TimePickerBasic",),
    "ToggleButton": ("ToggleButtonBasic",),
    "ToggleSplitButton": ("ToggleSplitButtonBasic",),
    "ToggleSwitch": ("ToggleSwitchBasic",),
    "ToolTip": ("ToolTipBasic",),
    "TreeView": ("TreeViewBasic",),
    "VariableSizedWrapGrid": ("WrapGridBasic",),
    "Viewbox": ("ViewboxBasic",),
}

# 官方那页是**教学主题**，不是某个控件 —— 跟"有没有这个类型"无关。
TOPICS: dict[str, str] = {
    "AccessibilityKeyboard": "教学主题：键盘无障碍（TabIndex / AutomationProperties）。演示的是属性用法，没有对应控件类型。",
    "AccessibilityScreenReader": "教学主题：屏幕阅读器语义。同上，无对应控件类型。",
    "Binding": "教学主题：XAML 绑定机制。声明式树上没有 x:Bind，本库换成了「写法指南」那一组。",
    "Chart": "官方自己拿第三方图表库凑的一页，不是 WinUI 控件。",
    "Clipboard": "教学主题：剪贴板 API，非控件。",
    "CompactSizing": "教学主题：Compact 密度样式，靠资源字典切换，非独立控件。",
    "ConnectedAnimation": "教学主题：页面间连贯动画（ConnectedAnimationService），非控件。",
    "CustomUserControls": "教学主题：XAML 用户控件怎么写，本库对应「组件（Component）」这一层。",
    "CustomXamlConditionals": "教学主题：XAML 条件编译，非控件。",
    "ImplicitTransition": "教学主题：隐式过渡动画，非控件。",
    "PageTransition": "教学主题：页面切换动画，非控件。",
    "Sound": "教学主题：ElementSoundPlayer 音效开关。本库画廊自带 SoundService，设置页里可切。",
    "StoragePickers": "教学主题：文件选取器 API，非控件。",
    "Templates": "教学主题：ControlTemplate / DataTemplate，本库对应模板槽位那套。",
    "Typography": "教学主题：Typography 附加属性（连字、小型大写等），非控件。",
    "Windowing": "教学主题：窗口管理（WASDK AppWindow），非控件。",
    "XamlCompInterop": "教学主题：XAML 与 Composition 互操作，非控件。",
    "XamlResources": "教学主题：资源字典，非控件。",
    "XamlStyles": "教学主题：样式与 BasedOn，非控件。",
    "CaptureElementPreview": "教学主题：相机预览（CaptureElement），需设备权限，非画廊常规控件。",
    "Geometry": "教学主题：几何（PathGeometry 等）的用法，形状那页已覆盖 Stroke/Fill 侧。",
    "EasingFunction": "教学主题：缓动函数，非控件。",
    "ThemeTransition": "教学主题：主题过渡动画，非控件。",
    "SystemBackdrops": "教学主题：WASDK 系统背景（Mica/Acrylic），非控件本体。",
}

# 既没有类型、也不是教学页的：名字看着像控件，但本机工具链里确实不存在。
NOT_A_TYPE_NOTE = "WinUI 3 / Windows App SDK 独有：本机 WinUI 2.8.7 与 UWP 契约里都无此类型。"


def read_official() -> list[str]:
    names: list[str] = []
    for line in OFFICIAL_INDEX.read_text(encoding="utf-8").splitlines():
        m = re.match(r"- (\S+)", line)
        if m:
            names.append(m.group(1))
    return names


def has_type(blob: bytes, name: str) -> bool:
    """在 winmd 的 #Strings 流里按定界符精确匹配一个类型简单名。

    两个坑，都踩过：

    * **编码是 UTF-8，不是 UTF-16**。ECMA-335 里 `#Strings` 堆是 UTF-8 空终止，
      只有 `#US`（用户字符串堆）才是 UTF-16。一开始按 UTF-16 搜，连 `TextBlock`
      都搜不到 —— 整张表的结论全是假阴性。
    * **必须前后带定界符**。不卡边界的话 `Geometry` 会命中 `PathGeometry`。

    另有一处**本函数解决不了**的局限：它分不清 TypeDef 与 TypeRef。名字出现在
    元数据里，可能只是"某个签名引用了它"（UWP 契约里就能搜到 `NavigationView`）。
    所以这里的输出只当**线索**，终审交给 `probe_types.py`（让编译器回答）。
    """
    needle = b"\0" + name.encode("utf-8") + b"\0"
    return needle in blob


def load(name: str, path: Path) -> bytes:
    if not path.exists():
        print(f"⚠ 找不到 {name}：{path}", file=sys.stderr)
        return b""
    return path.read_bytes()


def local_components() -> set[str]:
    text = SAMPLE_INDEX.read_text(encoding="utf-8")
    return set(re.findall(r"Component<([A-Za-z0-9_]+)>", text))


def framework_factories() -> set[str]:
    names: set[str] = set()
    for path in FACTORIES.glob("*.cs"):
        text = path.read_text(encoding="utf-8", errors="replace")
        names.update(
            re.findall(
                r"public static\s+[A-Za-z0-9_<>,\. \[\]?]+\s+([A-Za-z0-9_]+)\s*\(", text
            )
        )
    return names


def load_probe() -> tuple[dict[str, list[str]], set[str]]:
    """读 `probe_types.py` 落下的编译器判定结果。

    有它就以它为准（编译器亲口说的，比 winmd 字符串搜索可靠）；没有就返回空，
    退回 winmd 启发式并在输出里提醒一句。
    """
    path = Path(__file__).resolve().parent / "type-probe.json"
    if not path.exists():
        return {}, set()
    data = json.loads(path.read_text(encoding="utf-8"))
    return data.get("found", {}), set(data.get("missing", []))


def main() -> int:
    official = read_official()
    local = local_components()
    factories = framework_factories()
    mux = load("WinUI 2.8.7 winmd", MUX_WINMD)
    uwp = load("UWP 契约 winmd", UWP_WINMD)
    probe_found, probe_missing = load_probe()
    using_probe = bool(probe_found or probe_missing)

    covered, missing = [], []
    for name in official:
        if name in COVERED:
            covered.append(name)
        else:
            missing.append(name)

    # 覆盖表里写了、但索引里没注册的 —— 映射写错了，要报出来而不是静默放过。
    ghost: dict[str, list[str]] = {}
    for name, samples in COVERED.items():
        unknown = [s for s in samples if s not in local]
        if unknown:
            ghost[name] = unknown

    print(f"官方控件 {len(official)} 个｜本地已覆盖 {len(covered)}｜未覆盖 {len(missing)}\n")

    if ghost:
        print("⚠ 覆盖表里有索引中不存在的示例名（映射写错了）：")
        for k, v in ghost.items():
            print(f"   {k} -> {', '.join(v)}")
        print()

    buckets: dict[str, list[tuple[str, str]]] = {
        "MUX2 有类型，框架未暴露": [],
        "UWP 有类型，框架未暴露": [],
        "WinUI3/WASDK 独有": [],
        "教学主题（非控件）": [],
        "未知（需人工看一眼）": [],
    }

    for name in missing:
        if name in TOPICS:
            buckets["教学主题（非控件）"].append((name, TOPICS[name]))
            continue

        if using_probe:
            # 编译器判过了，直接采信。
            if name in probe_found:
                ns = probe_found[name][0]
                buckets["MUX2 有类型，框架未暴露" if ns.startswith("Microsoft")
                        else "UWP 有类型，框架未暴露"].append(
                    (name, f"{ns} 里有此类型（编译器实测），本框架没暴露工厂。"))
            elif name in probe_missing:
                buckets["WinUI3/WASDK 独有"].append(
                    (name, "编译器实测：WinUI 2.8.7 与 UWP 契约里都无此类型。"))
            else:
                buckets["未知（需人工看一眼）"].append((name, "探针名单里没有它，重跑 probe_types.py。"))
            continue

        # 没跑过探针：退回 winmd 字符串搜索（只当线索，见 has_type 的注释）。
        if mux and has_type(mux, name):
            buckets["MUX2 有类型，框架未暴露"].append(
                (name, "Microsoft.UI.Xaml 2.8.7 里有此类型（winmd 线索，未过编译器）。"))
            continue
        if uwp and has_type(uwp, name):
            buckets["UWP 有类型，框架未暴露"].append(
                (name, "UWP 平台契约里有此类型（winmd 线索，未过编译器）。"))
            continue
        if re.fullmatch(r"[A-Z][A-Za-z0-9]{3,}", name):
            buckets["WinUI3/WASDK 独有"].append((name, NOT_A_TYPE_NOTE))
        else:
            buckets["未知（需人工看一眼）"].append((name, "名字不像类型，也没归类到教学主题。"))

    if not using_probe:
        print("（未找到 type-probe.json，分类退回 winmd 线索模式；"
              "跑 tools/parity/probe_types.py 可得编译器级结论。）\n")

    for title, items in buckets.items():
        if not items:
            continue
        print(f"── {title}（{len(items)}）")
        for name, note in sorted(items):
            tag = ""
            if "未暴露" in title:
                # 框架里有没有同名工厂？有就说明只是没写示例，性质不同。
                tag = "  ← 框架有同名工厂，只是没写示例" if name in factories else ""
            print(f"   {name}：{note}{tag}")
        print()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
