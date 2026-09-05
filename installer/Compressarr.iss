; Inno Setup script for Compressarr - a tray-only background app with its entire UI in the
; browser (Radarr/Sonarr-style). Packages the framework-dependent win-x64 publish output produced
; by (requires the matching .NET Desktop/ASP.NET Core runtime already installed on the target
; machine - see README for the download link):
;   dotnet publish src/Compressarr.Desktop -c Release -r win-x64 -f net10.0-windows10.0.19041.0 --self-contained false -o publish/win-x64-fx
;
; Framework-dependent only as of 2.1.1 (2026-09-05): the previous self-contained "Full" build
; (275MB, bundling its own .NET runtime) was flagged by Windows Defender's cloud/SmartScreen
; reputation classifier (Program:Win32/Contebrew.A!ml) on a real download - a live reputation
; heuristic VirusTotal's static engine never reproduced (it scanned the identical file 0/68 clean,
; Microsoft's own engine included). A self-signed cert carries no publisher reputation, and a
; large, rarely-downloaded bundle is exactly what that classifier flags. Dropping to a single,
; much smaller framework-dependent build removes that exposure entirely rather than chasing it.
;
; Build with: ISCC.exe installer\Compressarr.iss

#define MyAppName "Compressarr"
#define MyAppVersion "2.1.1"
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
; Uncompressed: LZMA2's compressed/embedded-payload structure is what triggered a Microsoft
; Defender false positive (Trojan:Win32/Wacatac.B!ml) on the notification feature's otherwise
; completely benign HttpClient code - confirmed via a controlled A/B (identical payload, only the
; compression setting changed) before landing this. Installer size grows accordingly.
Compression=none
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Fallback only - PrepareToInstall below force-kills Compressarr.Desktop.exe directly and runs
; before this, so in practice there's nothing left for Restart Manager to find. Left in as a
; defensive no-op rather than relied on: RestartManager's graceful WM_QUERYENDSESSION handshake
; is unreliable against a tray-only app that's never had a window shown/interacted with (a known
; Inno/RestartManager limitation), and a botched close attempt left the app running but with its
; message loop wedged - unresponsive even to its own tray Exit command, requiring Task Manager.
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Wipe the whole install directory before laying down fresh files. Without this, an in-place
; upgrade only overwrites files the current package ships - it never removes files that belonged
; to a PREVIOUS install but aren't part of this one. Confirmed real-world impact 2026-09-05:
; upgrading a self-contained install (which bundles coreclr.dll/hostfxr.dll/hostpolicy.dll
; directly in {app}) to this framework-dependent build left those runtime files behind, and
; .NET's host detects a local coreclr.dll as "this is a self-contained app" - it then searches
; for the runtime INSIDE {app} instead of the real machine-wide install, failing with "You must
; install or update .NET" even though the correct runtime is genuinely installed system-wide.
Type: filesandordirs; Name: "{app}"

[Files]
; The framework-dependent publish output (exe, wwwroot, Assets - no bundled .NET runtime) -
; recursesubdirs/createallsubdirs so wwwroot's own subfolders (assets/) come along too.
Source: "..\publish\win-x64-fx\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Compressarr has no window of its own - it's a tray icon whose only UI is the browser, so
; "launch after install" is the equivalent of a normal app opening its main window on first run.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  UninstallString: String;
  ResultCode: Integer;
begin
  Result := True;
  // If any previous version is already installed, silently run ITS OWN registered uninstaller
  // before this version's files ever get laid down. Confirmed necessary 2026-09-05: switching
  // from a self-contained build to this framework-dependent one left coreclr.dll/hostfxr.dll
  // behind from the old install - an in-place upgrade only overwrites files the NEW package
  // ships, it never removes files that belonged only to the OLD one. .NET's host then treated
  // {app} itself as a self-contained runtime root and failed to find the real machine-wide
  // runtime, even though it was correctly installed system-wide. Running the old uninstaller
  // first guarantees a clean slate on every future upgrade, not just this one - the
  // [InstallDelete] entry below is a defensive backstop in case this ever can't find/run it.
  if RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6884601A-BC28-4492-BD25-354A350A5114}_is1', 'UninstallString', UninstallString) then
  begin
    UninstallString := RemoveQuotes(UninstallString);
    Exec(UninstallString, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  // Force-close any running instance before Setup's own CloseApplications/Restart Manager check
  // ever runs (PrepareToInstall always executes first). Compressarr saves settings to disk
  // immediately, so nothing is lost by killing it outright - and doing so here sidesteps Restart
  // Manager's WM_QUERYENDSESSION handshake entirely, which is what used to leave the app running
  // with a wedged message loop that couldn't even be closed via its own tray Exit command.
  Exec('taskkill.exe', '/IM "{#MyAppExeName}" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
