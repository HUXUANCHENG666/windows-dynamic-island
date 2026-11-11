; Inno Setup 6 script. This file was NOT compiled here.
; On a Windows machine with Inno Setup installed:
;   1. Publish the exe (build\publish.sh, or the GitHub Actions artifact).
;   2. Put DynamicIsland.exe next to this script, or edit the Source path below.
;   3. Open this file in Inno Setup and compile.

#define MyAppName "灵动岛"
#define MyAppVersion "1.0.0"
#define MyAppExe "DynamicIsland.exe"

[Setup]
AppId={{7C2A9E14-6B51-4F0A-9D33-5E1B8C4A7F20}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\DynamicIsland
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=DynamicIsland-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExe}
WizardStyle=modern

[Files]
Source: "{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "启动灵动岛"; Flags: nowait postinstall skipifsilent
