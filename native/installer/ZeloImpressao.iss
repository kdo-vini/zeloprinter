#define MyAppName "Zelo Impressão"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "Téchne Sistemas"
#define MyAppExeName "ZeloImpressao.exe"
#define PublishDir "..\..\release\dotnet\win-x64"

[Setup]
AppId={{B4F4C3D0-0AF5-42A3-9CB4-90E806C2D51B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Zelo Impressao
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\release\installer
OutputBaseFilename=Zelo-Impressao-{#MyAppVersion}-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\..\assets\printer.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=Global\Techne_Zelo_Impressao
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--show"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--show"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Zelo Impressao"; Flags: uninsdeletevalue
