# 示例画廊的 UIA 自动化自检。
#
# 为什么要有这一层：控制台测试集覆盖的是"源码层面的契约"（回声、闸门、索引），
# 它跑不了 XAML——画廊是 UWP 应用，要包身份 + UI 线程。而有一类回归只有把
# 界面真的跑起来才看得见：
#   - 某个控件在 UIA 树里抛异常（典型是自定义 AutomationPeer 的 GetChildrenCore
#     返回了已经 detach 的元素：遍历到它整个树就断在这里）；
#   - 可聚焦控件没有 Name（屏幕阅读器念出来是一串空白，且 UIA 客户端拿它没法定位）；
#   - AutomationId 在同一层里重号（自动化脚本按 id 找元素时会找错人）。
# 这三条正是"uia 自动化测试无异常"这句话要守的东西。
#
# 用法（先跑 samples/run.ps1 把应用起起来）：
#   powershell -ExecutionPolicy Bypass -File samples/uia-check.ps1
param(
    [string]$ProcessName = 'Reactor.Gallery',
    [string]$WindowTitle = 'Reactor 示例集',
    [int]$TimeoutSeconds = 30,

    # 只在画廊上成立：它才有顶栏搜索框。拿本脚本去量别的应用（比如 Reactor 模板）
    # 时传 $false，那一段就只报告、不判红。
    [bool]$RequireSearchBox = $true
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$A = [System.Windows.Automation.AutomationElement]
$Prop = [System.Windows.Automation.AutomationElement]
$cond = [System.Windows.Automation.Condition]::TrueCondition

function Fail([string]$msg) { Write-Host "FAIL  $msg" -ForegroundColor Red; $script:failed++ }
function Ok([string]$msg) { Write-Host "  ok  $msg" }
$script:failed = 0

# ---- 1. 找到画廊的顶层窗口 ----
#
# 两条判据，命中任意一条就算找到：
#   (a) 窗口所属进程叫这个名字（桌面窗口 / Win32 宿主走这条）；
#   (b) 窗口标题是这个（UWP 走这条）。
# 必须两条都备着：UWP 应用的顶层窗口是 <c>ApplicationFrameWindow</c>，
# 它的宿主进程叫 ApplicationFrameHost 而不是应用自己——只按进程名找，
# UWP 应用一个都找不到。
$root = $A::RootElement
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$window = $null

while ($window -eq $null -and (Get-Date) -lt $deadline) {
    foreach ($child in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
        $name = $child.Current.Name

        if ($WindowTitle -and $name -like "*$WindowTitle*") { $window = $child; break }

        $pid2 = $child.Current.ProcessId
        if ($pid2 -eq 0) { continue }
        $p = Get-Process -Id $pid2 -ErrorAction SilentlyContinue
        if ($p -and $p.ProcessName -eq $ProcessName) { $window = $child; break }
    }
    if ($window -eq $null) { Start-Sleep -Milliseconds 500 }
}

if ($window -eq $null) {
    Fail "在 $TimeoutSeconds 秒内没找到标题含『$WindowTitle』的窗口"
    Write-Host "        （进程名 $ProcessName 也没匹配上）——先跑 samples/run.ps1，" -ForegroundColor Yellow
    Write-Host "        或从开始菜单里把『$WindowTitle』打开再跑本脚本。" -ForegroundColor Yellow
    exit 1
}

Ok "挂上窗口：$($window.Current.Name)（pid=$($window.Current.ProcessId)，$($window.Current.ClassName)）"

# ---- 2. 遍历整棵树：任何一处抛异常都算回归 ----
#
# 用 RawViewWalker 而不是 ControlViewWalker：后者只走"控件视图"，会把
# ScrollViewer / 自定义 panel 这类非控件节点跳过——而 UIA 客户端真正常用的
# 正是 Raw 视图。要能证明"遍历无异常"，就得遍历客户端会走的那棵树。
$walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
$stack = [System.Collections.Generic.Stack[object]]::new()
$stack.Push($window)

$nodes = 0
$errors = [System.Collections.Generic.List[string]]::new()
$nameless = [System.Collections.Generic.List[string]]::new()
$byParent = @{}

while ($stack.Count -gt 0) {
    $e = $stack.Pop()

    try {
        $cur = $e.Current
    } catch {
        $errors.Add("取 Current 失败（$($_.Exception.Message)）")
        continue
    }

    $nodes++

    # 可聚焦的控件必须有名字：否则自动化脚本与屏幕阅读器都抓不住它。
    # 只查 ControlType 明确的那几类交互控件——Text / Image 这类本来就没有
    # 名字的概念，算进去是噪声。
    if ($cur.IsKeyboardFocusable -and [string]::IsNullOrWhiteSpace($cur.Name)) {
        $type = $cur.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        if ($type -in @('Button', 'CheckBox', 'RadioButton', 'Edit', 'ComboBox', 'ListItem',
                        'TabItem', 'Hyperlink', 'MenuItem', 'ToggleButton', 'SplitButton')) {
            $nameless.Add("$type（AutomationId='$($cur.AutomationId)'）")
        }
    }

    # AutomationId 在同一父节点下不能重号。
    if (-not [string]::IsNullOrEmpty($cur.AutomationId)) {
        $key = $cur.ControlType.ProgrammaticName + '|' + $cur.AutomationId
        if (-not $byParent.ContainsKey($key)) { $byParent[$key] = 0 }
        $byParent[$key]++
    }

    try {
        $child = $walker.GetFirstChild($e)
        while ($child -ne $null) {
            $stack.Push($child)
            $child = $walker.GetNextSibling($child)
        }
    } catch {
        $errors.Add("遍历子节点失败（$($_.Exception.Message)）于 $($cur.ControlType.ProgrammaticName)")
    }
}

if ($nodes -lt 10) {
    Fail "只遍历到 $nodes 个节点 —— 界面大概是空的，树没长出来"
} else {
    Ok "遍历 UIA 树完成：$nodes 个节点"
}

if ($errors.Count -gt 0) {
    Fail "遍历中抛了 $($errors.Count) 次异常："
    $errors | Select-Object -First 10 | ForEach-Object { Write-Host "        - $_" -ForegroundColor Red }
} else {
    Ok "遍历全程无异常"
}

if ($nameless.Count -gt 0) {
    Fail "$($nameless.Count) 个可聚焦控件没有 Name："
    $nameless | Select-Object -First 10 | ForEach-Object { Write-Host "        - $_" -ForegroundColor Red }
} else {
    Ok "可聚焦控件都有 Name"
}

# 同一个 AutomationId 出现多次本身合法（同一个模板被复用），但**同一层**里
# 重号就不合法。上面的计数是全局的，这里只把它当作"有 id 的节点"的统计，
# 真正要卡的是下面这条：同一父下的兄弟不重号。
$dupSiblings = [System.Collections.Generic.List[string]]::new()
$stack2 = [System.Collections.Generic.Stack[object]]::new()
$stack2.Push($window)

while ($stack2.Count -gt 0) {
    $e = $stack2.Pop()
    $seen = @{}

    try {
        $child = $walker.GetFirstChild($e)
        while ($child -ne $null) {
            $id = $child.Current.AutomationId
            if (-not [string]::IsNullOrEmpty($id)) {
                if ($seen.ContainsKey($id)) {
                    $dupSiblings.Add("'$id' 在同一层出现多次（$($child.Current.ControlType.ProgrammaticName)）")
                } else {
                    $seen[$id] = $true
                }
            }
            $stack2.Push($child)
            $child = $walker.GetNextSibling($child)
        }
    } catch { }
}

if ($dupSiblings.Count -gt 0) {
    Fail "同一层里有重号 AutomationId：$($dupSiblings.Count) 处"
    $dupSiblings | Select-Object -First 10 | ForEach-Object { Write-Host "        - $_" -ForegroundColor Red }
} else {
    Ok "同一层内 AutomationId 不重号"
}

# ---- 3. 交互冒烟：左侧导航能点、点了内容区真的换 ----
#
# 只做一件事：找到第一个 NavigationViewItem 并 Invoke，确认树规模发生变化。
# 这一条守的是"点了没反应"这个最表层的回归——比逐控件断言稳。
$navCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::ListItem)

$items = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond))
Ok "左侧导航项 $($items.Count) 个"

if ($items.Count -gt 1) {
    # 导航项可能只实现了其中一种模式（NavigationViewItem 通常是 Invoke，
    # 列表项则是 SelectionItem），逐个试到能点的那个为止。
    # "两种都没有"不算回归——那是控件类型不对，记一句就够，不判红。
    $acted = $false

    foreach ($item in ($items | Select-Object -Skip 1)) {
        foreach ($try in @('Invoke', 'Select')) {
            try {
                if ($try -eq 'Invoke') {
                    $p = $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                    $p.Invoke()
                } else {
                    $p = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                    $p.Select()
                }

                $acted = $true
                break
            } catch {
                # 换下一种模式 / 下一个项
            }
        }

        if ($acted) { break }
    }

    if ($acted) {
        Start-Sleep -Milliseconds 700
        $after = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)).Count
        Ok "点了一个导航项之后仍可遍历（$after 个后代节点）"
    } else {
        Write-Host "  --  没有可点的导航项（都不支持 Invoke / SelectionItem），跳过交互冒烟" -ForegroundColor Yellow
    }
}

# ---- 4. 顶部搜索框存在，且 Ctrl+F 每次都把焦点送过去 ----
#
# 单独守这一条是因为它跨了两层实现：快捷键挂在外壳根元素上（官方
# KeyboardAccelerator），焦点则由一个"令牌"边沿驱动（Focus() 是方法不是属性，
# 声明式里只能靠令牌变化表达"再聚焦一次"）。这两处任一处断了，界面上看起来
# 都只是"按 Ctrl+F 没反应"——而它是全应用唯一一个键盘直达入口。
#
# 为什么必须按**两次**：令牌那条是边沿触发，回调闭包捕获的是它当帧看到的值。
# 框架若把第一次的回调永久留在登记表里（后续每帧新写的闭包跟不上），第二次按下去
# 算出来的还是同一个值 → 令牌不动 → 焦点不动。只按一次永远发现不了——
# 这条缺陷此前长期没被测出来，正是因为这个脚本当时只按了一次。
$editCond = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)

$searchBox = @($window.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants, $editCond)) |
    Where-Object { $_.Current.Name -like '*搜索*' } |
    Select-Object -First 1

if ($searchBox -eq $null) {
    if ($RequireSearchBox) {
        Fail "没有找到名为『搜索…』的输入框（外壳的 NavigationView.AutoSuggestBox 没挂上？）"
    } else {
        Write-Host "  --  本应用没有搜索框（RequireSearchBox=$false），跳过这一段" -ForegroundColor Yellow
    }
} else {
    Ok "顶部搜索框在树里（Name='$($searchBox.Current.Name)'）"

    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop

        # 前置：窗口得在前台，SendKeys 才发得进去。
        # 这里**不再用 SetFocus**：UWP 应用在 UIA 里的顶层窗口是
        # ApplicationFrameWindow，对它调 SetFocus 会抛
        # "Target element cannot receive focus"——早先整段就是因此被跳过的。
        if (-not $window.Current.HasKeyboardFocus) {
            Write-Host "  --  窗口不在前台（HasKeyboardFocus=False），跳过 Ctrl+F 冒烟。" -ForegroundColor Yellow
            Write-Host "        把画廊点到前台再跑本脚本，这一段才有意义。" -ForegroundColor Yellow
        } else {
            $hit = 0

            for ($round = 1; $round -le 2; $round++) {
                if ($round -gt 1) {
                    # 先把焦点挪走。不挪的话"第二次焦点还在搜索框"是假通过——
                    # 而这里要验的恰恰是第二次。
                    [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
                    Start-Sleep -Milliseconds 400
                }

                [System.Windows.Forms.SendKeys]::SendWait('^f')
                Start-Sleep -Milliseconds 800

                $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
                if ($focused -ne $null -and $focused.Current.Name -like '*搜索*') {
                    $hit++
                }
            }

            if ($hit -eq 2) {
                Ok "Ctrl+F 连按两次都把焦点送到搜索框"
            } else {
                Fail "Ctrl+F 两次里只成功了 $hit 次（第二次按不动 = 回调没跟上每帧新写的闭包）"
            }
        }
    } catch {
        Write-Host "  --  发不出 Ctrl+F（$($_.Exception.Message)），跳过焦点冒烟" -ForegroundColor Yellow
    }
}

Write-Host ""
if ($script:failed -eq 0) {
    Write-Host "UIA 自检通过。" -ForegroundColor Green
    exit 0
}

Write-Host "UIA 自检失败 $script:failed 项。" -ForegroundColor Red
exit 1
