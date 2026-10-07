#!/usr/bin/env bash
# 逐页扫描：点开每一个左侧导航项，在该页的 UIA 全树里找「可聚焦但没有 Name」的控件。
#
# 为什么要有这一层：uia-check.ps1 只量「当前停在的那一页」。Gallery 有十几页，
# 一个页面干净不代表下一页干净——无名可聚焦控件（读屏念空白、自动化脚本抓不住）
# 的复现条件恰恰是「切到那一页」。
#
# 导航项怎么定位：名字（"首页"）在整棵树里不唯一（首页内容区的分类卡也叫
# "文本与提示"），所以不能拿名字当选择器；而 slug 是内容哈希，窗口一挪位置就全变。
# 于是这里先 inspect 一次，从导航区那一段里把「名字 → slug」现查出来再点。
#
# 用法（先起好应用）：
#   bash samples/uia-scan-pages.sh <HWND>
set -u

HWND="${1:?用法: uia-scan-pages.sh <HWND>}"
WINAPP="${WINAPP:-winapp}"

NAV=(
  "首页"
  "全部示例"
  "文本与提示"
  "按钮"
  "日期与时间"
  "输入与选择"
  "命令与外壳"
  "集合与虚拟化"
  "布局与容器"
  "状态与信息"
  "媒体、图像与图标"
)

# 只查「本来该有名字」的交互控件类型。Text / Image / Group 这类没有名字的概念。
TYPES='Button|Edit|ComboBox|CheckBox|RadioButton|ListItem|TabItem|Hyperlink|MenuItem|ToggleButton|SplitButton|ToggleSwitch|Slider'

# 取导航区那一段（MenuItemsHost 到 FooterItemsScrollViewer 之间）里的「名字 → slug」。
nav_slugs() {
  "$WINAPP" ui inspect -w "$HWND" -d 12 2>&1 \
    | sed -n '/MenuItemsHost/,/FooterItemsScrollViewer/p' \
    | grep -E '^\s+\S+ ListItem "' \
    | sed -E 's/^\s+([^ ]+) .*ListItem "([^"]*)".*/\2\t\1/'
}

total=0
fail=0

# 每次导航后重新取一次：上一页的状态可能让导航项的哈希变掉。
rows="$(nav_slugs)"

for label in "${NAV[@]}"; do
  slug="$(printf '%s\n' "$rows" | awk -F'\t' -v n="$label" '$1 == n { print $2; exit }')"

  if [ -z "$slug" ]; then
    echo "[跳过] $label —— 导航区里没找到这一项"
    continue
  fi

  if ! "$WINAPP" ui invoke "$slug" -w "$HWND" >/dev/null 2>&1; then
    echo "[跳过] $label —— $slug 点不动"
    continue
  fi

  sleep 0.8

  # 全树，不做 --hide-offscreen：屏外元件同样是读者能滚到的，漏掉它等于漏报。
  out="$("$WINAPP" ui inspect -w "$HWND" -d 24 2>&1)"

  # 有名：slug Type "Name" (bounds)；无名：slug Type (bounds) —— 直接跟左括号。
  hits="$(printf '%s\n' "$out" \
    | grep -oE "^\s+\S+ ($TYPES) \(" \
    | grep -oE "($TYPES) \(" \
    | sort | uniq -c | sort -rn)"

  nodes="$(printf '%s\n' "$out" | grep -cE "^\s+\S+ \S" || true)"

  total=$((total + 1))

  if [ -z "$hits" ]; then
    echo "[干净] $label（约 $nodes 行节点）"
  else
    fail=$((fail + 1))
    echo "[红灯] $label —— 无名可聚焦控件："
    printf '%s\n' "$hits" | sed 's/^/         /'
  fi

  rows="$(nav_slugs)"
done

echo ""
echo "扫了 $total 页，$fail 页有红灯。"
[ "$fail" -eq 0 ] || exit 1
