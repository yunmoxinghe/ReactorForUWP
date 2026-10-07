# 跑 UWP 示例。
#
# 为什么不能直接 F5 / 双击 exe：UWP 的 XAML 要求进程带 AppX 包身份，
# 直接执行 win-x64\*.exe 时 Windows.UI.Xaml.dll 会立刻 fail-fast
# （退出码 0xc0000409，fast-fail 子码 7 = FAST_FAIL_FATAL_APP_EXIT），
# 托管堆栈和 UnhandledException 都拦不住。所以这里走：
#   补 Assets -> 备好清单 -> 松散注册 -> 按包身份激活。
#
# 关于构建：
#   CLI 的 dotnet build 只出 exe，不出 AppxManifest.xml / resources.pri /
#   Microsoft.UI.Xaml.winmd（MSIX 那一步只在 VS 的 MSBuild 里跑），
#   而且它的增量清理还会把 VS 生成的那些文件删掉。
#   所以 VS 跑过之后就别再用 CLI 构建同一个示例。
#   反过来，CLI 构建完也能直接跑本脚本：清单缺了它会从源 Package.appxmanifest
#   现生成一份能注册的版本（补 ProcessorArchitecture / 语言 / 框架依赖）。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File samples/run.ps1                  # Template
#   powershell -ExecutionPolicy Bypass -File samples/run.ps1 -Project Gallery
#   powershell -ExecutionPolicy Bypass -File samples/run.ps1 -Platform arm64
param(
    [ValidateSet('Template', 'Gallery')]
    [string]$Project = 'Template',
    [ValidateSet('x64', 'arm64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
# Add-AppxPackage 会刷一大堆 Deployment operation progress，盖掉真正有用的报错。
$ProgressPreference = 'SilentlyContinue'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$projDir   = Join-Path $repoRoot "samples/Reactor.$Project"
$layout    = Join-Path $projDir "bin/$Platform/Debug/net10.0-windows10.0.26100.0/win-$Platform"
$manifest  = Join-Path $layout "AppxManifest.xml"
$srcManifest = Join-Path $projDir "Package.appxmanifest"
$exe       = Join-Path $layout "Reactor.$Project.exe"

if (-not (Test-Path $exe)) {
    throw "找不到 $exe`n先在 VS 里生成一次（Ctrl+Shift+B），或用 dotnet build 出 exe。"
}

# ---- 0. VS 生成的 AppX 布局优先 ----
#
# 注册必须挑**完整**的那份布局。VS 的 MSIX 那一步会在 win-<arch>/AppX 下产出
# 一份能直接部署的布局：带 resources.pri、带 Microsoft.UI.Xaml.winmd、
# 清单里 ProcessorArchitecture / 依赖都已就位。
# 而 CLI 的 dotnet build 只把 exe / dll 吐在 win-<arch> 根上，pri 与 winmd 都没有。
# 早先这里一律用根目录现造清单去注册，结果是：注册成功（Status=Ok）、激活也
# 报"完成"，但应用<b>根本不出现</b>——既不崩也不留日志，因为进程压根没起来。
# 现在有 AppX 就用 AppX，没有再退回下面那条自己补清单的老路。
$vsLayout = Join-Path $layout "AppX"
$vsManifest = Join-Path $vsLayout "AppxManifest.xml"

if (Test-Path $vsManifest) {
    Write-Host "==> 用 VS 生成的布局：$vsLayout"
    $layout   = $vsLayout
    $manifest = $vsManifest
    $exe      = Join-Path $layout "Reactor.$Project.exe"
}

# ---- 1. 清单：没有就自己生成一份 ----
#
# 源 Package.appxmanifest 是给 VS 的，直接拿去注册不够：
#   - 缺 ProcessorArchitecture（松散注册要求写死）
#   - Resource Language 是具体语言而不是 x-generate 占位
#   - Executable 是 $targetnametoken$.exe 令牌
#   - ms-resource: 引用解析不了（没有 resources.pri，见下）
#   - 缺两条框架依赖（WinUI 2 / VCLibs），那是 VS 的 WinUI targets 注入的
if (-not (Test-Path $manifest)) {
    if (-not (Test-Path $srcManifest)) { throw "找不到源清单 $srcManifest" }
    # （走到这里说明没有 AppX 布局，只有 CLI 产物，按下面那串补丁自己补一份。）
    Write-Host "==> 生成清单（VS 的 MSIX 布局不在，从源清单补一份）"

    [xml]$x = Get-Content $srcManifest
    $ns = $x.Package.NamespaceURI

    # 用 SetAttribute：PowerShell 的 XML 适配器只暴露文档里已经存在的属性，
    # 直接 $node.X = v 会报 "property cannot be found"。
    $x.Package.Identity.SetAttribute('ProcessorArchitecture', $Platform)
    $x.Package.Applications.Application.SetAttribute('Executable', "Reactor.$Project.exe")

    # 清单里 DisplayName / Description 走 ms-resource:，正常由 resources.pri 解析，
    # 而 CLI 构建不生成 pri，松散注册时解析不到就会报资源错误。这里按
    # DefaultLanguage（zh-CN）的 resw 把引用换成字面值。
    $resw = Join-Path $projDir "Strings/zh-CN/Resources.resw"
    if (Test-Path $resw) {
        [xml]$res = Get-Content $resw
        $strings = @{}
        foreach ($d in $res.root.data) { $strings[$d.name] = $d.value }
        foreach ($node in $x.SelectNodes('//*')) {
            foreach ($a in @($node.Attributes)) {
                if ($a.Value -match '^ms-resource:(.+)$' -and $strings.ContainsKey($Matches[1])) {
                    $a.Value = $strings[$Matches[1]]
                }
            }

            # 元素<b>文本</b>里的 ms-resource（<DisplayName>ms-resource:AppDisplayName</DisplayName>
            # 就是这种）。只替换属性会漏掉它，而它恰恰是包级别的显示名——
            # 留着不解析，注册时会报"windows.firewall 扩展找不到 NamedResource"
            # （防火墙规则用包显示名）。
            if ($node.ChildNodes.Count -eq 1 -and $node.FirstChild.NodeType -eq 'Text') {
                if ($node.InnerText -match '^ms-resource:(.+)$' -and $strings.ContainsKey($Matches[1])) {
                    $node.InnerText = $strings[$Matches[1]]
                }
            }
        }
    }

    # 只留一个 Resource 节点：松散注册没 pri，列多种语言等于声明了拿不出的资源。
    $resources = $x.SelectNodes('//*[local-name()="Resource"]')
    if ($resources.Count -gt 0) {
        $resources[0].SetAttribute('Language', 'ZH-CN')
        for ($i = $resources.Count - 1; $i -gt 0; $i--) {
            [void]$resources[$i].ParentNode.RemoveChild($resources[$i])
        }
    }

    # 框架依赖：MinVersion 要跟本机装的一致，写低了注册会报依赖不满足。
    # VCLibs 分 Debug / Release，默认按 Debug（示例默认跑 Debug）。
    $muxPublisher = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
    $vclibs = if ($env:REACTOR_VCLIBS_DEBUG -eq '0') {
        @{ Name = 'Microsoft.VCLibs.140.00';       Min = '14.0.33519.0' }
    } else {
        @{ Name = 'Microsoft.VCLibs.140.00.Debug'; Min = '14.0.33519.0' }
    }
    foreach ($d in @(
        @{ Name = 'Microsoft.UI.Xaml.2.8'; Min = '8.2501.31001.0' },
        $vclibs
    )) {
        $el = $x.CreateElement('PackageDependency', $ns)
        $el.SetAttribute('Name', $d.Name)
        $el.SetAttribute('MinVersion', $d.Min)
        $el.SetAttribute('Publisher', $muxPublisher)
        [void]$x.Package.Dependencies.AppendChild($el)
    }

    $x.Save($manifest)
}

# ---- 2. Assets：清单引用了这些 logo，缺了注册不过 ----
#
# 用 \* 拷内容：把目录 Copy-Item 到已存在的 Assets 上会套出 Assets\Assets。
$assets = Join-Path $projDir "Assets"
if (Test-Path $assets) {
    $target = Join-Path $layout "Assets"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $assets "*") -Destination $target -Recurse -Force

    # 源 Assets 里只有带限定符的文件（如 SplashScreen.scale-200.png），清单引用的却是
    # 不带限定符的 Assets\SplashScreen.png。正常流程由 resources.pri 按缩放/语言解析，
    # 而 CLI 构建不生成 pri，注册时就会报 0x80070002"找不到初始屏幕图像"。
    # 这里补一份去掉限定符的副本，等价于 pri 解析出来的结果。
    Get-ChildItem $target -Filter "*.scale-*.png" | ForEach-Object {
        $plain = Join-Path $target ($_.Name -replace '\.scale-\d+\.png$', '.png')
        Copy-Item $_.FullName -Destination $plain -Force
    }
}

[xml]$manifestXml = Get-Content $manifest
$identity = $manifestXml.Package.Identity.Name

# ---- 3. 先卸掉同 Identity 的旧注册 ----
# VS 部署过、或上次脚本注册过时，同 Identity 换个目录再注册会冲突。
$old = Get-AppxPackage -Name $identity -ErrorAction SilentlyContinue
if ($old) {
    Write-Host "==> remove old registration: $($old.PackageFullName)"
    $old | Remove-AppxPackage
}

Write-Host "==> register (loose) $identity"
Add-AppxPackage -Register $manifest | Out-Null

$pkg = Get-AppxPackage -Name $identity
if (-not $pkg) { throw "注册后仍查不到包，看上面的部署报错" }

# 激活必须走 shell: URI（带包身份），不能直接 Start-Process exe——
# 那样进程没身份，XAML 照样 fail-fast。
# Start-Process 不认 shell: 协议（报 "cannot find the file specified"），
# 得交给 cmd 的 start 内建命令；explorer.exe 也能开，但会多弹一层。
Write-Host "==> launch $($pkg.PackageFullName)"
$pfn = $pkg.PackageFullName
Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$pfn!App" -ErrorAction SilentlyContinue
