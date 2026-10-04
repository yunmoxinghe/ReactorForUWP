# 临时诊断脚本：构建 x64 → 同步到 AppX 松散布局 → 启动 → 等压测跑完 → 打印本轮 summary
#
# 包 GUID 会随部署轮换，所以不写死 PackageFamilyName：
# 直接扫 %LOCALAPPDATA%\Packages 找带 LocalState\ReactorRuns 的那个包目录。
#
# 用法：.\diag-run.ps1 [-Mode N]
#   N = 测试壳菜单项下标：0~5 是压测 M0~M5，6 虚拟列表实验室，7 Echo 实验室，
#       8 CoreLoop 回归，9 元素画廊……（见 UwpApp\TestShell.cs 的 Cases 顺序）
#   Mode 缺省 = 不改，沿用 App 上一次读到的（首次跑为 1，即 M1）。
#   App 启动时读 LocalState\probe-mode.txt 决定落在哪一页，所以换页不用重新编译；
#   真人测试时直接点左侧菜单切换即可，这个参数只是给无人值守脚本用的。
param([int]$Mode = -1)
$ErrorActionPreference = 'Continue'
$proj = 'D:\fluentapps\repos\test\ReactorForUWP\UwpApp\UwpApp.csproj'
$base = 'D:\fluentapps\repos\test\ReactorForUWP\UwpApp\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'

# 1) 构建
$build = dotnet build $proj -p:Platform=x64 2>&1
if ($LASTEXITCODE -ne 0) {
  "=== BUILD FAILED ==="
  $build | Select-String -Pattern 'error' | Select-Object -First 15
  exit 1
}
$build | Select-String -Pattern '已成功' | Select-Object -Last 1

# 2) 定位包目录
#    a) 优先按上轮已有的 ReactorRuns 反查；
#    b) 首次跑时还没有该目录，退回用 manifest 里的 Identity Name 查已安装包。
$pkgRoot = Join-Path $env:LOCALAPPDATA 'Packages'
$pkg = Get-ChildItem $pkgRoot -Directory -ErrorAction SilentlyContinue |
       Where-Object { Test-Path (Join-Path $_.FullName 'LocalState\ReactorRuns') } |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $pkg) {
  $identity = '996f8761-686b-4dd5-b261-7301ef0e55a5'   # Package.appxmanifest / Identity Name
  $appx = Get-AppxPackage -Name $identity -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($appx) {
    $pkg = Get-ChildItem $pkgRoot -Directory -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -eq $appx.PackageFamilyName } | Select-Object -First 1
  }
}
if (-not $pkg) {
  "=== 定位不到包目录：先手动部署/启动一次 App，再重跑本脚本 ==="
  exit 1
}
$pf = $pkg.Name
$runsRoot = Join-Path $pkg.FullName 'LocalState\ReactorRuns'
New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null   # 首次跑时先建好，便于后续反查
"package = $pf"

# 3) 杀旧进程 + 同步产物 + 启动
Get-Process -Name UwpApp -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
Copy-Item "$base\UwpApp.dll", "$base\UwpApp.exe", "$base\Reactor.uwp.dll" "$base\AppX\" -Force
# 原生桥 DLL：P/Invoke 是运行时按 exe 旁边找，AppX 布局里没有就直接 DllNotFoundException。
if (Test-Path "$base\Reactor.Uwp.Native.dll") {
  Copy-Item "$base\Reactor.Uwp.Native.dll" "$base\AppX\" -Force
} else {
  "=== 缺 Reactor.Uwp.Native.dll（预编译产物不在 prebuilt\x64），原生工厂跑不起来 ==="
}
if ($Mode -ge 0) {
  Set-Content -Path (Join-Path $pkg.FullName 'LocalState\probe-mode.txt') -Value "$Mode" -Encoding ASCII
  "mode = $Mode (写入 probe-mode.txt)"
} else {
  $cur = Join-Path $pkg.FullName 'LocalState\probe-mode.txt'
  "mode = $(if (Test-Path $cur) { Get-Content $cur } else { '3（缺省）' })"
}
$before = @(Get-ChildItem $runsRoot -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
Start-Process "shell:AppsFolder\$pf!App"

# 4) 等本轮跑完：出现新的 run 目录且里面落了 summary.json
$run = $null
for ($i = 0; $i -lt 90; $i++) {
  Start-Sleep -Seconds 2
  $cand = Get-ChildItem $runsRoot -Directory -ErrorAction SilentlyContinue |
          Where-Object { $before -notcontains $_.Name } |
          Sort-Object Name -Descending | Select-Object -First 1
  if ($cand -and (Test-Path (Join-Path $cand.FullName 'summary.json'))) { $run = $cand; break }
}
$p = Get-Process -Name UwpApp -ErrorAction SilentlyContinue | Sort-Object StartTime -Descending | Select-Object -First 1
if ($p) { "alive pid=$($p.Id) Responding=$($p.Responding)" } else { "EXITED（可能崩了）" }

if (-not $run) {
  "=== 本轮没跑完（没拿到 summary.json），最近一次 events 尾部： ==="
  $last = Get-ChildItem $runsRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
  if ($last) {
    "run = $($last.Name)"
    Get-Content (Join-Path $last.FullName 'events.ndjson') -Tail 15 -ErrorAction SilentlyContinue
  }
  exit 1
}

# 5) 输出结论
"=== run = $($run.Name) ==="
Get-Content (Join-Path $run.FullName 'summary.json')
"=== events 尾部 ==="
Get-Content (Join-Path $run.FullName 'events.ndjson') -Tail 10
