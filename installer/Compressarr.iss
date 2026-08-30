; Inno Setup script for Compressarr - a tray-only background app with its entire UI in the
; browser (Radarr/Sonarr-style). Packages the self-contained win-x64 publish output produced by:
;   dotnet publish src/Compressarr.Desktop -c Release -r win-x64 -f net10.0-windows10.0.19041.0 --self-contained true -o publish/win-x64
;
; Build with: ISCC.exe installer\Compressarr.iss

#define MyAppName "Compressarr"
#define MyAppVersion "2.0.1"
#define MyAppPublisher "Mark Wasserman"
#define MyAppURL "https://github.com/MrWizardCT/Compressarr"
#define MyAppExeName "Compressarr.Desktop.exe"

[Setup]
; Stable AppId - do not change between versions, it's what lets an upgrade install recognize
; and replace a previous install rather than creating a second side-by-side entry.
AppId={{6884601A-BC28-4492-BD25-354A350A5114}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\publish
OutputBaseFilename=Compressarr-Setup-{#MyAppVersion}
SetupIconFile=..\src\Compressarr.Desktop\Assets\CompressarrIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Detects a running Compressarr.Desktop.exe (dev testing hit this constantly - a locked exe
; fails the whole install) and offers to close it before copying files, then relaunches it
; afterward if it was running.
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The entire self-contained publish output (exe, .NET/ASP.NET Core runtime, wwwroot, Assets) -
; recursesubdirs/createallsubdirs so wwwroot's own subfolders (assets/) come along too.
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Compressarr has no window of its own - it's a tray icon whose only UI is the browser, so
; "launch after install" is the equivalent of a normal app opening its main window on first run.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
