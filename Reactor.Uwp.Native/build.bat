@echo off
setlocal enabledelayedexpansion

rem 原生桥（给 ItemsRepeater 补的 IElementFactory）构建脚本。
rem
rem   用法：build.bat [x64 ^| arm64]      不给参数时构建 x64。
rem
rem 两个架构都从 x64 主机交叉编译（Hostx64\<arch>），产物分别落到
rem prebuilt\<arch>\Reactor.Uwp.Native.dll，打包时按 RID 进
rem runtimes\win-<arch>\native\。
rem
rem 前提：generated\ 里要有 cppwinrt 生成的投影头。它是构建产物、不入库，
rem 没了就重新生成（见 tools\cppwinrt.nupkg）：
rem   cppwinrt.exe -in "C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.26100.0\Windows.winmd" ^
rem                 -in winmd\Microsoft.UI.Xaml.winmd -out generated

set "ARCH=%~1"
if "%ARCH%"=="" set "ARCH=x64"

set "MSVC=C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231"
set "SDK=C:\Program Files (x86)\Windows Kits\10"
set "SDKVER=10.0.26100.0"

if /i "%ARCH%"=="arm64" (
    set "LIBARCH=arm64"
    set "BINARCH=Hostx64\arm64"
    set "MACHINE=ARM64"
    set "ARCHDEF=_ARM64_"
) else if /i "%ARCH%"=="x64" (
    set "LIBARCH=x64"
    set "BINARCH=Hostx64\x64"
    set "MACHINE=X64"
    set "ARCHDEF=_AMD64_"
) else (
    echo [build] 不支持的架构: %ARCH%  ^(只支持 x64 / arm64^)
    exit /b 1
)

set "INCLUDE=%MSVC%\include;%SDK%\Include\%SDKVER%\ucrt;%SDK%\Include\%SDKVER%\um;%SDK%\Include\%SDKVER%\shared;%SDK%\Include\%SDKVER%\cppwinrt;%SDK%\Include\%SDKVER%\winrt"
set "LIB=%MSVC%\lib\%LIBARCH%;%SDK%\Lib\%SDKVER%\ucrt\%LIBARCH%;%SDK%\Lib\%SDKVER%\um\%LIBARCH%"
set "PATH=%MSVC%\bin\%BINARCH%;%PATH%"

cd /d "%~dp0"

rem winrt\base.h 由 SDK 的 Include\<ver>\cppwinrt 提供（已在上文 INCLUDE 里），
rem generated\ 里只需有本次真正要用到的那几个投影头。
if not exist "generated\winrt\Microsoft.UI.Xaml.Controls.h" (
    echo [build] 缺少 generated\winrt\Microsoft.UI.Xaml.Controls.h —— 先用 cppwinrt 生成投影头（见本文件顶部注释）。
    exit /b 1
)

echo [build] 架构=%ARCH%  工具链=%BINARCH%
rem /W4 + /permissive-：C++/WinRT 代码要求符合标准的名称查找，别名模板的坑基本靠它兜住。
rem /utf-8：源码里的中文注释按 UTF-8 解析（不加会把 UTF-8 中文当本地代码页，最坏情况报错）。
cl -nologo -std:c++20 -EHsc -W4 -permissive- -Zc:__cplusplus -utf-8 -O2 -MD -DWINAPI_FAMILY=WINAPI_FAMILY_APP -D%ARCHDEF% -I generated -c factory.cpp -Fo:factory.%ARCH%.obj
if errorlevel 1 (
    echo [build] 编译失败
    exit /b 1
)

if not exist "prebuilt\%ARCH%" mkdir "prebuilt\%ARCH%"

link -nologo -DLL -MACHINE:%MACHINE% -OUT:prebuilt\%ARCH%\Reactor.Uwp.Native.dll factory.%ARCH%.obj WindowsApp.lib -SUBSYSTEM:WINDOWS -NXCOMPAT -DYNAMICBASE
if errorlevel 1 (
    echo [build] 链接失败
    exit /b 1
)

echo [build] 完成: prebuilt\%ARCH%\Reactor.Uwp.Native.dll
