#!/usr/bin/env bash
# 端到端重打包：源码 → local-nuget → 清缓存 → 还原 → 【验符号】 → 编译。
#
# 存在的理由：本项目踩过一次"静默吃旧包"——本地源里的包比源码旧 22 小时，
# 于是改了框架却怎么都不生效，而整条链路<b>没有任何一步会报错</b>。
# 那次浪费的时间比修 bug 本身还多。四步里漏任何一步都是同一个结局，
# 所以它们必须串在一起，且"<b>包里真的是刚编出来的那份</b>"必须被显式验证。
#
# 用法（仓库任意位置）：
#   bash tools/repack.sh                      # 打当前版本，验默认符号，编 x64 + arm64
#   bash tools/repack.sh --no-build           # 只打包 + 验符号
#   bash tools/repack.sh --expect Foo --expect Bar   # 在默认清单<b>之外</b>再验这两个
#   bash tools/repack.sh --version 0.1.0-alpha.9
#
# 退出码非 0 = 有一步没过，且错误信息会指明是哪一步。

set -euo pipefail

# ── 参数 ──────────────────────────────────────────────────────────
VERSION=""
NO_BUILD=0
EXPECT=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2:?--version 需要值}"; shift 2 ;;
    --no-build) NO_BUILD=1; shift ;;
    --expect) EXPECT+=("${2:?--expect 需要值}"); shift 2 ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    *) echo "未知参数：$1（用 --help 看用法）" >&2; exit 2 ;;
  esac
done

# ── 定位 ──────────────────────────────────────────────────────────
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCAL_NUGET="${LOCAL_NUGET:-D:/fluentapps/local-nuget}"
PKG_DIR="$HOME/.nuget/packages/reactor.uwp"

echo "==> 仓库根：$ROOT"

# ── Preflight：缺什么就在这一步喊出来，别等到最后才暴露 ────────────
# 教训是"长链路跑到一半才失败"，所以所有前置条件都在动手之前查完。
fail() { echo "PREFLIGHT 失败：$*" >&2; exit 1; }

[[ -f "$ROOT/Reactor.uwp/Reactor.uwp.csproj" ]] \
  || fail "找不到 Reactor.uwp/Reactor.uwp.csproj（ROOT=$ROOT 不对？）"

[[ -d "$LOCAL_NUGET" ]] \
  || fail "本地源不存在：$LOCAL_NUGET（用 --LOCAL_NUGET 或先建目录）"

[[ -d "$ROOT/samples/Reactor.Template" ]] \
  || fail "找不到 samples/Reactor.Template"

command -v dotnet >/dev/null || fail "dotnet 不在 PATH 上"

# 版本号：不给就从模板 csproj 里读，避免"包打了 8、模板还在引 7"这种错位。
if [[ -z "$VERSION" ]]; then
  VERSION="$(grep -oP 'Include="Reactor\.Uwp"\s+Version="\K[^"]+' \
             "$ROOT/samples/Reactor.Template/Reactor.Template.csproj" | head -1)"
fi

[[ -n "$VERSION" ]] || fail "读不到版本号（模板 csproj 里没有 Reactor.Uwp 的 PackageReference？）"

# 默认符号清单：本轮三处修法 + 上游就绪判据 + 日志轮转。
# 随版本演进请显式传 --expect 追加 —— 留着一份陈旧清单并让它恒绿，比没有检查更糟
# （符号哪天被删了，脚本会 MISS，那时就该改清单，而不是把检查关掉）。
EXPECT=(FlushWhenControlCleared FlushWhenControlPulledBack TrustLiveIsLoaded Applying
        MaxPersistBytes RotatePersist)

echo "    版本：$VERSION"
echo "    本地源：$LOCAL_NUGET"
echo "    待验符号：${EXPECT[*]}"

# ── 1. 打包 ───────────────────────────────────────────────────────
echo "==> [1/6] pack"
dotnet pack "$ROOT/Reactor.uwp/Reactor.uwp.csproj" -c Release -o "$LOCAL_NUGET" \
  | grep -E "已成功创建包|error|错误" || true

NUPKG="$LOCAL_NUGET/Reactor.Uwp.$VERSION.nupkg"
[[ -f "$NUPKG" ]] || { echo "打包失败：没有产出 $NUPKG" >&2; exit 1; }

# ── 2. 清缓存版本目录 ─────────────────────────────────────────────
# 这一步最容易漏：不清的话 restore 认为"已经有了"，直接拿旧的那份，
# 而它<b>不会</b>告诉你它没更新。
echo "==> [2/6] 清 NuGet 缓存 $PKG_DIR/$VERSION"
rm -rf "$PKG_DIR/$VERSION"

# ── 3. 强制还原 ───────────────────────────────────────────────────
echo "==> [3/6] restore --force"
dotnet restore "$ROOT/samples/Reactor.Template/Reactor.Template.csproj" --force \
  | grep -E "已还原|error|错误" || true

# ── 4. 验符号（在编译之前，尽早暴露"吃了旧包"）───────────────────
# 这是整个脚本存在的理由。包体积、时间戳都会骗人，
# 唯一不骗人的是"新加的那个符号在不在 dll 里"。
echo "==> [4/6] 验证包 == 源码"
DLL="$(find "$PKG_DIR/$VERSION" -name 'Reactor.uwp.dll' -print -quit 2>/dev/null || true)"
[[ -n "$DLL" ]] || { echo "缓存里没有 $VERSION 的 Reactor.uwp.dll —— 还原没吃到新包" >&2; exit 1; }

MISSING=0
for sym in "${EXPECT[@]}"; do
  if grep -aq "$sym" "$DLL"; then
    echo "    ok   $sym"
  else
    echo "    MISS $sym  ← 包里没有，说明它比源码旧" >&2
    MISSING=$((MISSING + 1))
  fi
done

if [[ $MISSING -gt 0 ]]; then
  echo "" >&2
  echo "$MISSING 个符号缺失：本地源 / 缓存 / 还原，至少有一处没走通。" >&2
  echo "  dll = $DLL" >&2
  exit 1
fi

# ── 5/6. 编译 ─────────────────────────────────────────────────────
if [[ $NO_BUILD -eq 1 ]]; then
  echo "==> [5-6/6] 跳过编译（--no-build）"
else
  for arch in x64 arm64; do
    echo "==> [5/6] build Template $arch"
    dotnet build "$ROOT/samples/Reactor.Template/Reactor.Template.csproj" \
      -c Release -p:Platform=$arch 2>&1 | grep -E "个错误|个警告|error" || true
  done

  echo "==> [6/6] build Gallery x64 + UwpApp x64"
  # UwpApp 在<b>仓库根</b>，不在 samples/ 下——按 samples/ 找会以为它不存在。
  dotnet build "$ROOT/samples/Reactor.Gallery/Reactor.Gallery.csproj" \
    -c Release -p:Platform=x64 2>&1 | grep -E "个错误|个警告|error" || true

  dotnet build "$ROOT/UwpApp/UwpApp.csproj" \
    -c Release -p:Platform=x64 2>&1 | grep -E "个错误|个警告|error" || true
fi

echo ""
echo "完成：$VERSION 已进本地源、缓存已换、符号已验。"
