# 光标Elf

一个极轻量的 Windows 桌面小工具：在鼠标光标旁显示一个圆点，指示当前输入法的中/英文状态。

- 🟢 绿色 = 中文输入
- 🔴 红色 = 英文输入

按 `Shift` 切换微软拼音中英文时，圆点颜色自动跟随变化。

## 功能

- 圆点悬浮在光标旁，点击穿透、永不抢焦点
- 应用图标为红绿太极图案（`tools/MakeYinIcon.cs` 在构建时生成 `.ico`）
- 支持自定义颜色、大小（4–40px）、相对光标的方位（右下/左下/右上/左上）与水平/垂直像素间距
- 支持上传自定义图片作为中/英文指示图案，自动居中裁剪，可选显示为圆形或正方形；「恢复默认圆点」一键回退
- 系统托盘图标同步显示状态；右键菜单支持开机自启、手动校正颜色
- 待机 / 息屏唤醒后依然正常工作
- 多显示器、混合 DPI 缩放（如笔记本 + 外接屏）下跟随不偏移
- 资源占用极低（约 0.2% CPU、20 余 MB 内存）

## 使用

1. 运行安装包或直接运行编译好的 `ImeCursorDot.exe`
2. 首次启动如颜色与实际输入状态不符，右键托盘图标 →「当前实为中文/英文（校正颜色）」校一次
3. 之后每次单独轻点 `Shift` 即自动同步（按住 Shift 输入标点/大写字母不会误判）

设置入口：左键或右键托盘图标 →「设置（颜色·大小·位置）…」。
配置保存在 `%AppData%\ImeCursorDot\settings.ini`。

## 技术说明

Windows 11 24H2 起，微软拼音全面转向 TSF，传统 IMM32 API（`ImmGetConversionStatus`）对任何窗口都返回空上下文，中英文模式也不写入注册表，公开 API 无法跨进程读取。

本项目采用「镜像追踪」方案：以 25ms 轮询 `GetAsyncKeyState`（只读取系统实时按键状态，不安装键盘钩子），复刻微软拼音"单独轻点 Shift 切换中英文"的规则——按住 Shift 期间出现任何其他按键即取消切换判定。鼠标跟随由低级鼠标钩子触发 + 定时器兜底，坐标统一使用 `GetCursorPos`（与窗口定位同一坐标空间），保证混合 DPI 多屏不偏移。

## 编译

无需任何第三方依赖，使用系统自带的 .NET Framework 4.x 编译器：

```bat
build.cmd
```

或手动：

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize /target:winexe /platform:x64 /out:ImeCursorDot.exe src\ImeDot.cs
```

## 目录结构

```
src/        ImeDot.cs        主程序（单文件）
tests/      SelfTest.cs      端到端测试：注入 Shift 轻点，验证圆点变色
            PunctTest.cs     回归测试：Shift+标点不误切换、纯 Shift 轻点必切换
            MoveTest.cs      多屏跟随测试：跨显示器采样圆点偏移一致性
tests/      DotSnap.cs       回归测试：自定义图片渲染为蓝点、删除后恢复默认圆点
tools/      MakeYinIcon.cs   构建工具：生成红绿太极应用图标 (.ico)
            EnumWin.cs       诊断：枚举进程窗口
            FgDump.cs        诊断：打印前台窗口 IME 信号
```

## 安装

`dist/光标Elf-setup.exe` 为单文件安装包（Inno Setup 制作），默认安装到 `D:\光标Elf`，含开始菜单/桌面快捷方式与可选开机自启。

## 换电脑迁移指南

本仓库即全部项目资料（GitHub：`bigbigee/CursorElf`，私有）。新电脑上继续开发只需：

1. **拿源码**：`git clone https://github.com/bigbigee/CursorElf.git`（GitHub 直连超时则先设代理：`git config --global http.proxy http://127.0.0.1:7897`）
2. **编译**：无需安装任何东西，Windows 自带 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，运行 `build.cmd` 即产出 `dist\ImeCursorDot.exe`（会自动先生成太极图标 .ico）。注意：源码必须保持 C# 5 语法（该 csc 不支持新版语法）
3. **测试**：`csc /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll tests\XxxTest.cs` 编译后，先启动主程序再运行对应测试 exe
4. **安装包**：安装 Inno Setup 7（https://jrsoftware.org/isdl.php），用 `ISCC.exe setup.iss` 或 IDE 打开 `setup.iss` 编译
5. **发布**：安装 Git for Windows 与 GitHub CLI（gh），`gh auth login`（网页 device code 方式）；提交源码后 `gh release create vX.Y.Z dist\光标Elf-setup.exe --title ... --notes ...`。**不要把 exe 提交进仓库**（本机上大文件 push 走代理会 HTTP 408），二进制一律走 Release
6. **个人配置**（可选）：拷贝旧电脑 `%AppData%\ImeCursorDot\settings.ini`，以及其中的 `zh_custom.png` / `en_custom.png` 自定义图案

当前版本：v1.1.0（红绿太极图标 + 自定义上传图片）。
