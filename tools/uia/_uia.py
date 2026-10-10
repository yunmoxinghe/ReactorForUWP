"""环境公共层：包定位、进程启停、Trace/mode 开关、产物同步。

UIA 本身不再由本模块负责——`winapp ui` 自带完整 UIA 能力
（inspect / search / invoke / get-value / wait-for / screenshot …），
比这里原先自造的 comtypes 封装更全、也不用装依赖。

comtypes 因此改成<b>惰性</b>导入：只有真正要用 COM 自动化时才 import，
这样"启停 App / 写开关 / 同步产物"这些活在任何 Python 上都能跑，
不会因为缺 comtypes 而连启动都失败（曾经因为这个让"PASS"来路不明）。
"""

import os
import subprocess
import time

_UIA = None


def uia_module():
    """惰性导入 comtypes 的 UIA 绑定。只在确实要用 COM 时调用。"""
    global _UIA
    if _UIA is None:
        from comtypes.client import GetModule
        GetModule('UIAutomationCore.dll')
        import comtypes.gen.UIAutomationClient as UIA
        _UIA = UIA
    return _UIA

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
BUILD = os.path.join(
    REPO, 'UwpApp', 'bin', 'x64', 'Debug', 'net10.0-windows10.0.26100.0', 'win-x64')
APPX = os.path.join(BUILD, 'AppX')
IDENTITY = '996f8761-686b-4dd5-b261-7301ef0e55a5'   # Package.appxmanifest / Identity Name


def automation():
    """建 CUIAutomation 实例。需要 comtypes；缺依赖时给明确报错而不是 ImportError 炸在模块头。"""
    try:
        from comtypes.client import CreateObject
    except ImportError as e:
        raise RuntimeError(
            '需要 comtypes（pip install comtypes）。'
            '若只是启停/开关，不必用这条路径——UIA 交互请走 `winapp ui`。') from e
    UIA = uia_module()
    return CreateObject(UIA.CUIAutomation, interface=UIA.IUIAutomation)


# ── 包定位 ────────────────────────────────────────────────
def find_package():
    """返回 (包目录, PackageFamilyName)。优先按 LocalState\\ReactorRuns 反查。"""
    root = os.path.join(os.environ['LOCALAPPDATA'], 'Packages')
    hits = []
    for name in sorted(os.listdir(root)):
        if os.path.isdir(os.path.join(root, name, 'LocalState', 'ReactorRuns')):
            hits.append(name)
    if hits:
        hits.sort(key=lambda n: os.path.getmtime(os.path.join(root, n)), reverse=True)
        pf = hits[0]
        return os.path.join(root, pf), pf

    out = subprocess.run(
        ['powershell', '-NoProfile', '-Command',
         f"(Get-AppxPackage -Name {IDENTITY} | Select-Object -First 1).PackageFamilyName"],
        capture_output=True, text=True)
    pf = out.stdout.strip()
    if not pf:
        return None, None
    return os.path.join(root, pf), pf


def local_state(pkg_dir):
    """LocalState 可能是符号链接（本项目实际指向 D:\\WpSystem\\...），os.path 会跟随。"""
    p = os.path.join(pkg_dir, 'LocalState')
    os.makedirs(os.path.join(p, 'ReactorRuns'), exist_ok=True)
    return p


def log_path(pkg_dir):
    return os.path.join(local_state(pkg_dir), 'reactor-startup.log')


# ── 部署与启停 ─────────────────────────────────────────────
def sync_artifacts():
    """把最新产物拷进 AppX 松散布局。少了 Reactor.Uwp.Native.dll 会 DllNotFound。

    必须先 <see cref="stop_app"/>：App 活着时 AppX 里的 dll 被占用（WinError 32）。
    """
    import shutil
    for f in ('UwpApp.dll', 'UwpApp.exe', 'Reactor.uwp.dll', 'Reactor.Uwp.Native.dll'):
        src = os.path.join(BUILD, f)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(APPX, f))


def stop_app():
    """杀到<b>确认没了</b>为止。

    UWP 是单实例：进程还活着时再 `start` 只会<b>激活现有实例</b>，
    新写的 probe-mode.txt 根本不会被读——于是"换了 mode 跑"其实一直在跑
    同一个页面，这种假对照比不对照更糟。所以这里必须轮询到进程消失。
    """
    for _ in range(20):
        subprocess.run(['powershell', '-NoProfile', '-Command',
                        'Get-Process -Name UwpApp -EA SilentlyContinue | Stop-Process -Force'],
                       capture_output=True)
        time.sleep(0.5)
        r = subprocess.run(['powershell', '-NoProfile', '-Command',
                            '(Get-Process -Name UwpApp -EA SilentlyContinue | Measure-Object).Count'],
                           capture_output=True, text=True)
        if (r.stdout or '').strip() == '0':
            time.sleep(0.5)
            return True
    print('!! UwpApp 进程杀不掉，后续跑的可能是旧实例')
    return False


def start_app(pf):
    subprocess.Popen(['cmd', '/c', 'start', '', f'shell:AppsFolder\\{pf}!App'],
                     shell=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def app_hwnds():
    """当前所有标题含 UwpApp 的窗口 HWND（走 <c>winapp ui list-windows</c>）。"""
    out = subprocess.run(['winapp', 'ui', 'list-windows'],
                         capture_output=True, text=True).stdout or ''
    import re
    return [m.group(1) for m in re.finditer(r'HWND\s+(\d+):\s*"([^"]*UwpApp[^"]*)"', out)]


def wait_window_gone(timeout=25.0):
    """等 <c>stop_app</c> 之后<b>窗口</b>也从窗口列表里消失。

    <c>stop_app</c> 轮询的是<b>进程</b>没了，但窗口列表可能还留着一个将死的
    <c>ApplicationFrameWindow</c>。紧接着启动下一轮时，按标题找 HWND 会抢到
    这个旧句柄——它马上就失效，于是后续每一条 UIA 命令都返回空，
    表现为"invoke 没成功"，而 App 其实完全正常。
    连着跑两个脚本时必踩，单独跑一次又全绿——典型的假红。
    """
    deadline = time.time() + timeout
    while time.time() < deadline:
        if not app_hwnds():
            time.sleep(0.4)
            return True
        time.sleep(0.4)
    return False


def set_mode(pkg_dir, mode):
    with open(os.path.join(local_state(pkg_dir), 'probe-mode.txt'), 'w') as f:
        f.write(str(mode))


def set_trace(pkg_dir, channels='Input,Patch'):
    """打开 Trace 落盘。

    闸门的"吞掉"是 Trace 级、默认<b>不落盘</b>。不开这个开关，真机上只看得见
    "放行"、看不见"被吞"——于是"点了没反应"会在日志里表现为"一条都没有"，
    极易误判成"WinUI 根本没抛事件"。脚本必须自己开，别靠手。
    """
    with open(os.path.join(local_state(pkg_dir), 'trace-input.txt'), 'w') as f:
        f.write(channels)


def clear_trace(pkg_dir):
    """关掉 Trace 落盘（回到默认档）。"""
    p = os.path.join(local_state(pkg_dir), 'trace-input.txt')
    if os.path.exists(p):
        os.remove(p)


# ── UIA 查找 ──────────────────────────────────────────────
def app_window(u, pid=None, timeout=30.0):
    """找 UwpApp 的顶层窗口。pid 给了就按 pid 精确匹配。"""
    end = time.time() + timeout
    while time.time() < end:
        root = u.GetRootElement()
        cond = u.CreateTrueCondition()
        kids = root.FindAll(UIA.TreeScope_Children, cond)
        for i in range(kids.Length):
            e = kids.GetElement(i)
            if pid is not None and e.CurrentProcessId == pid:
                return e
            if pid is None and (e.CurrentClassName == 'ApplicationFrameWindow'
                                or e.CurrentName and 'UwpApp' in e.CurrentName):
                return e
        time.sleep(0.5)
    return None


def descendants(u, root, control_type=None, name=None, limit=None):
    """在子树里按 ControlType / Name 找元素。"""
    conds = []
    if control_type is not None:
        conds.append(u.CreatePropertyCondition(UIA.UIA_ControlTypePropertyId, control_type))
    if name is not None:
        conds.append(u.CreatePropertyCondition(UIA.UIA_NamePropertyId, name))
    cond = conds[0]
    for c in conds[1:]:
        cond = u.CreateAndCondition(cond, c)
    found = root.FindAll(UIA.TreeScope_Descendants, cond)
    out = [found.GetElement(i) for i in range(found.Length)]
    return out[:limit] if limit else out


# ── 窗口置前与真实鼠标 ────────────────────────────────────
import ctypes
from ctypes import wintypes

_user32 = ctypes.WinDLL('user32', use_last_error=True)
_user32.SetForegroundWindow.argtypes = [wintypes.HWND]
_user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
_user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
_user32.mouse_event.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
                                wintypes.DWORD, ctypes.c_void_p]
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004
SW_RESTORE = 9


def bring_to_front(win):
    """UWP 在后台时不派发输入：UIA 的 Select 静默无效果，必须先把窗口拉到前台。"""
    hwnd = win.CurrentNativeWindowHandle
    if not hwnd:
        return False
    _user32.ShowWindow(hwnd, SW_RESTORE)
    return bool(_user32.SetForegroundWindow(hwnd))


def mouse_click(rect):
    """按元素矩形中心点一下。比 UIA 的 Select 更接近真人：走完整输入路径。"""
    x = int((rect.left + rect.right) / 2)
    y = int((rect.top + rect.bottom) / 2)
    _user32.SetCursorPos(x, y)
    time.sleep(0.05)
    _user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, None)
    time.sleep(0.05)
    _user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, None)
    return x, y


def describe(e):
    try:
        r = e.CurrentBoundingRectangle
        rect = f"{r.left:.0f},{r.top:.0f} {r.right - r.left:.0f}x{r.bottom - r.top:.0f}"
    except Exception:
        rect = '?'
    return (f"name={e.CurrentName!r} cls={e.CurrentClassName!r} "
            f"ct={e.CurrentControlType} off={e.CurrentIsOffscreen} rect={rect}")
