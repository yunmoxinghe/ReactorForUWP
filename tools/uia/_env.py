import sys
from comtypes.client import GetModule, CreateObject
GetModule('UIAutomationCore.dll')
import comtypes.gen.UIAutomationClient as UIA

def automation():
    return CreateObject(UIA.CUIAutomation, interface=UIA.IUIAutomation)
