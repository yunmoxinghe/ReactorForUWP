@echo off
set "MSVC=C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231"
set "SDK=C:\Program Files (x86)\Windows Kits\10"
set "SDKVER=10.0.26100.0"
set "INCLUDE=%MSVC%\include;%SDK%\Include\%SDKVER%\ucrt;%SDK%\Include\%SDKVER%\um;%SDK%\Include\%SDKVER%\shared;%SDK%\Include\%SDKVER%\cppwinrt;%SDK%\Include\%SDKVER%\winrt"
set "LIB=%MSVC%\lib\x64;%SDK%\Lib\%SDKVER%\ucrt\x64;%SDK%\Lib\%SDKVER%\um\x64"
set "PATH=%MSVC%\bin\Hostx64\x64;%PATH%"
cd /d "D:\fluentapps\repos\test\ReactorForUWP\Reactor.Uwp.Native"
cl -nologo -std:c++20 -EHsc -W3 -O2 -MD -DWINAPI_FAMILY=WINAPI_FAMILY_APP -D_AMD64_ -I generated -c factory.cpp -Fo:factory.obj
link -nologo -DLL -OUT:Reactor.Uwp.Native.dll factory.obj WindowsApp.lib -SUBSYSTEM:WINDOWS -NXCOMPAT -DYNAMICBASE
