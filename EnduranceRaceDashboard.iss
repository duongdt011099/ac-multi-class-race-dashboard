#define MyAppName "Endurance Race Dashboard"
#define MyAppVersion "1.0.4.1"
#define MyAppPublisher "Endurance Race Dashboard"
#define MyAppExeName "multi-class-race-dashboard.exe"
#define MyServiceName "EnduranceRace"
#define MyServiceDisplayName "Endurance Race Manager"
#define MyPublishDir "publish"

[Setup]
AppId={{A8E6E5B0-3F2C-4F0A-9B15-6C4A1D2E9F01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

DefaultDirName={autopf}\EnduranceRace
DefaultGroupName={#MyAppName}
UsePreviousAppDir=no

OutputDir=installer
; Must match UpdateCheck:InstallerFileName in appsettings.json - the update
; notification links the .exe produced by OutputBaseFilename as its download.
OutputBaseFilename=EnduranceRaceDashboardSetup
Compression=lzma
SolidCompression=yes

PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
; User-specific startup entries are guarded by IsAdminInstallMode; all-user installs use commonstartup.
UsedUserAreasWarning=no
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Excludes: "wwwroot\uploads,app.db"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "http://127.0.0.1:5000"
Name: "{group}\Start Dashboard"; Filename: "{app}\{#MyAppExeName}"; Check: not IsAdminInstallMode
Name: "{userstartup}\Endurance Race Dashboard"; Filename: "{app}\{#MyAppExeName}"; Flags: runminimized; Check: not IsAdminInstallMode
Name: "{userstartup}\Endurance Race Game Launcher"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--game-launcher-agent"; Flags: runminimized; Check: not IsAdminInstallMode
Name: "{commonstartup}\Endurance Race Game Launcher"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--game-launcher-agent"; Flags: runminimized; Check: IsAdminInstallMode

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "StopEnduranceRace"; Check: IsAdminInstallMode
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "DeleteEnduranceRace"; Check: IsAdminInstallMode

[Run]
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist; Check: IsAdminInstallMode
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist; Check: IsAdminInstallMode
Filename: "{sys}\sc.exe"; Parameters: "create {#MyServiceName} binPath= ""{app}\{#MyAppExeName}"" start= auto DisplayName= ""{#MyServiceDisplayName}"""; Flags: runhidden waituntilterminated; Check: IsAdminInstallMode
Filename: "{sys}\sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden waituntilterminated; Check: IsAdminInstallMode
Filename: "{app}\{#MyAppExeName}"; Parameters: "--game-launcher-agent"; Flags: runhidden nowait postinstall skipifsilent runasoriginaluser; Check: IsAdminInstallMode
Filename: "{app}\{#MyAppExeName}"; Parameters: "--game-launcher-agent"; Flags: runhidden nowait postinstall skipifsilent; Check: not IsAdminInstallMode
Filename: "{app}\{#MyAppExeName}"; Flags: runhidden nowait postinstall skipifsilent; Check: not IsAdminInstallMode
Filename: "http://127.0.0.1:5000"; Description: "Open {#MyAppName}"; Flags: postinstall shellexec skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if (not IsAdminInstallMode) and
     RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\EnduranceRace') then
  begin
    Result := 'An all-users Windows service is still installed. Uninstall the all-users version with administrator privileges before installing for the current user.';
  end;
end;
