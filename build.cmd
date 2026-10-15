@echo off
rem 山竹 Cursor Elf 编译脚本：使用系统自带 .NET Framework 4.x 编译器，无第三方依赖
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set VER=0.0.0
for /f "usebackq tokens=3" %%v in ("version.inc") do set VER=%%~v
if not exist shazhu.ico (
  %CSC% /nologo /target:exe /platform:x64 /r:System.Drawing.dll /out:tools\PngToIco.exe tools\PngToIco.cs
  tools\PngToIco.exe assets\shazhu.png shazhu.ico
)
if not exist dist mkdir dist
echo [assembly: System.Reflection.AssemblyVersion("%VER%")]> dist\ver.cs
echo [assembly: System.Reflection.AssemblyFileVersion("%VER%")]>> dist\ver.cs
%CSC% /nologo /optimize /target:winexe /platform:x64 /win32icon:shazhu.ico /r:Microsoft.VisualBasic.dll /out:dist\ImeCursorDot.exe src\ImeDot.cs dist\ver.cs
if %errorlevel%==0 (
  echo 编译成功 v%VER%: dist\ImeCursorDot.exe
) else (
  echo 编译失败
  exit /b 1
)
