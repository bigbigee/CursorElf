#define MyAppName "山竹 Cursor Elf"
#include "version.inc"

[Setup]
AppId={{B7F3D2A9-4C16-4E85-9A70-52D8C6E1B349}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=CursorElf
SetupIconFile=shazhu.ico
DefaultDirName={code:DefDir}
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir=dist
OutputBaseFilename=山竹CursorElf-setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayName={#MyAppName}

[Languages]
Name: "ch"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"
Name: "autostart"; Description: "开机自动启动"; GroupDescription: "附加任务:"

[Files]
Source: "dist\ImeCursorDot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\ImeCursorDot.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\ImeCursorDot.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "ImeCursorDot"; ValueData: """{app}\ImeCursorDot.exe"""; \
  Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\ImeCursorDot.exe"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function DefDir(Param: String): String;
begin
  if DirExists('D:\') then
    Result := 'D:\山竹CursorElf'
  else
    Result := ExpandConstant('{localappdata}\Programs\山竹CursorElf');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if Pos('SILENT', UpperCase(GetCmdTail)) > 0 then Exit; // silent uninstall always keeps user settings
    if MsgBox('是否同时删除个人配置（设置与自定义图案，位于 %AppData%\ImeCursorDot）？',
      mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\ImeCursorDot'), True, True, True);
  end;
end;
