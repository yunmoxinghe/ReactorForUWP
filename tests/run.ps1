# 一键跑控制台测试集（第 1 层）：不开 App、不部署，退出码 0 = 全部通过。
#
#   .\tests\run.ps1                 # Release
#   .\tests\run.ps1 -Configuration Debug
#
# 测试集的分组与说明见 tests\README.md；要跑真 XAML 的那部分（压测 / 实验室）
# 用仓库根的 diag-run.ps1，那需要部署 UwpApp。
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Continue'
$proj = Join-Path $PSScriptRoot 'Reactor.Core.Tests/Reactor.Core.Tests.csproj'

& dotnet run --project $proj -c $Configuration
$code = $LASTEXITCODE

if ($code -ne 0) {
  "`n=== 测试集失败（退出码 $code）==="
} else {
  "`n=== 测试集全部通过 ==="
}

exit $code
