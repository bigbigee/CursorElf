@echo off
rem 光标Elf 编译脚本：使用系统自带 .NET Framework 4.x 编译器，无第三方依赖
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /nologo /optimize /target:winexe /platform:x64 /out:src\ImeCursorDot.exe src\ImeDot.cs
if %errorlevel%==0 (
  echo 编译成功: src\ImeCursorDot.exe
) else (
  echo 编译失败
  exit /b 1
)
