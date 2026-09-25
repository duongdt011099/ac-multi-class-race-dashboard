#define MyAppName "Endurance Race Dashboard"
#define MyAppVersion "1.0.0"
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

OutputDir=installer
OutputBaseFilename=EnduranceRaceDashboardSetup
Compression=lzma
SolidCompression=yes

PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "http://127.0.0.1:5000"

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist

[Run]
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\sc.exe"; Parameters: "create {#MyServiceName} binPath= ""{app}\{#MyAppExeName}"" start= auto DisplayName= ""{#MyServiceDisplayName}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start {#MyServiceName}"; Flags: runhidden waituntilterminated
Filename: "http://127.0.0.1:5000"; Description: "Open {#MyAppName}"; Flags: postinstall shellexec skipifsilent
