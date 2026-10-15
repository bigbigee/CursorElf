@echo off
rem 一键编译并运行全部端到端测试（需先启动 ImeCursorDot.exe，且测试期间不要动鼠标）
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FAIL=0
if not exist dist\tests mkdir dist\tests
for %%t in (SelfTest PunctTest MoveTest DotSnap SpeedTest FollowCheck) do (
  %CSC% /nologo /target:exe /platform:x64 /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:dist\tests\%%t.exe tests\%%t.cs
  if errorlevel 1 (
    echo [FAIL] %%t 编译失败
    set FAIL=1
  )
)
if %FAIL%==1 exit /b 1
echo ============ 编译完成，开始运行（测试期间请勿移动鼠标） ============
for %%t in (SelfTest PunctTest MoveTest DotSnap SpeedTest FollowCheck) do (
  echo ---- %%t ----
  dist\tests\%%t.exe
)
echo ============ 全部运行完毕，请核对上方 PASS / FAIL ============
