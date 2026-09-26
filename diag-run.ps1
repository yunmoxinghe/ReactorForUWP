# 临时诊断脚本：构建 x64 → 同步到 AppX 松散布局 → 启动 → 打印日志尾部
$ErrorActionPreference = 'Continue'
$proj = 'D:\fluentapps\repos\test\ReactorForUWP\UwpApp\UwpApp.csproj'
$base = 'D:\fluentapps\repos\test\ReactorForUWP\UwpApp\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
$pf = '996f8761-686b-4dd5-b261-7301ef0e55a5_y0tg80em3rma2'
$log = "$env:LOCALAPPDATA\Packages\$pf\LocalState\reactor-startup.log"

$build = dotnet build $proj -p:Platform=x64 2>&1
if ($LASTEXITCODE -ne 0) {
  "=== BUILD FAILED ==="
  $build | Select-String -Pattern 'error' | Select-Object -First 10
  exit 1
}
$build | Select-String -Pattern '已成功' | Select-Object -Last 1
Get-Process -Name UwpApp -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
Copy-Item "$base\UwpApp.dll", "$base\UwpApp.exe", "$base\Reactor.uwp.dll" "$base\AppX\" -Force
Start-Process "shell:AppsFolder\$pf!App"
Start-Sleep -Seconds 6
"=== tail ==="
Get-Content $log | Select-Object -Last 12
$p = Get-Process -Name UwpApp -ErrorAction SilentlyContinue | Sort-Object StartTime -Descending | Select-Object -First 1
if ($p) { "alive pid=$($p.Id) Responding=$($p.Responding)" } else { "EXITED" }
