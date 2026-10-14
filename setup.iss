#define MyAppName "山竹 Cursor Elf"
#define MyAppVersion "1.2.0"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=CursorElf
SetupIconFile=shazhu.ico
DefaultDirName=D:\山竹CursorElf
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir=dist
OutputBaseFilename=山竹CursorElf-setup-1.2.0
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayName={#MyAppName}

[Languages]
Name: "ch"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"

[Files]
Source: "dist\ImeCursorDot.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\ImeCursorDot.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\ImeCursorDot.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ImeCursorDot.exe"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent
