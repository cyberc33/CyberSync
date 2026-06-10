#ifndef BuildOutput
  #define BuildOutput "..\src\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#endif

#define MyAppName "CyberSync"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "CyberSync"
#define MyAppExeName "CyberSync.exe"

[Setup]
AppId={{0C5A631A-CF3E-4D2A-85AA-A0C51FDF6C7F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
DisableProgramGroupPage=no
OutputDir=.
OutputBaseFilename=CyberSync-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#BuildOutput}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
