#define MyAppName "Endurance Race Dashboard"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "Endurance Race Dashboard"
#define MyAppExeName "multi-class-race-dashboard.exe"
#define MyLegacyServiceName "EnduranceRace"
; The installer's own memory of last time. install.json is what the app reads; this is only used to
; pre-fill the page on an upgrade, which matters when the game sits somewhere auto-detection misses.
#define MyRegKey "Software\Endurance Race Dashboard"
#define MyRegValue "GamePath"
#define MyPublishDir "publish"
; The CSP Lua app that the dashboard imports results from. Only these three files are shipped: the
; dashboard never needs lib.lua, and the results and debug log the app keeps next to itself are the
; user's, not ours.
#define MyLuaAppDir "assetto-corsa-app\Multi_Class"

[Setup]
AppId={{A8E6E5B0-3F2C-4F0A-9B15-6C4A1D2E9F01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

; The dashboard starts Assetto Corsa, which only works from a signed-in user session, so it is a
; per-user app: it must live somewhere a normal user can write to (it keeps its log next to the
; executable) and it starts from the sign-in shortcut.
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
UsePreviousAppDir=no

OutputDir=installer
; Must match UpdateCheck:InstallerFileName in appsettings.json - the update
; notification links the .exe produced by OutputBaseFilename as its download.
OutputBaseFilename=EnduranceRaceDashboardSetup
Compression=lzma
SolidCompression=yes

PrivilegesRequired=lowest
; Offered for one case only: removing the Windows service that older versions installed.
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Excludes: "wwwroot\uploads,app.db*,logs"

; The CSP Lua app goes into the user's game folder rather than {app}, because Assetto Corsa only
; loads apps\lua from there. Listing the three files individually rather than copying the folder
; keeps the debug log and saved results the app keeps beside itself out of it. uninsneveruninstall:
; the game folder belongs to the user and the dashboard has no business tidying it away. Copying
; through a [Files] entry rather than by hand also means a write failure is reported by Inno instead
; of being swallowed into a zero exit code.
Source: "{#MyLuaAppDir}\Multi_Class.lua"; DestDir: "{code:LuaAppDestDir}"; Flags: ignoreversion uninsneveruninstall
Source: "{#MyLuaAppDir}\manifest.ini"; DestDir: "{code:LuaAppDestDir}"; Flags: ignoreversion uninsneveruninstall
Source: "{#MyLuaAppDir}\icon.png"; DestDir: "{code:LuaAppDestDir}"; Flags: ignoreversion uninsneveruninstall

[UninstallDelete]
; Written from code below, so the uninstaller never learns about it and has to be told. The files this
; installer puts into the Assetto Corsa folder are deliberately not listed: the game folder belongs to
; the user, and the app sitting next to it does no harm.
Type: files; Name: "{app}\install.json"

; Written by the app on its first run. Removed with install.json so that reinstalling really is a
; fresh start: leaving a baseline behind would let results written before the reinstall come back as
; import prompts, which is exactly the behaviour it exists to prevent.
Type: files; Name: "{app}\result-scan.json"

[Tasks]
; Inno only shows a shortcut as an option when it is tied to a task, so the desktop icon needs one.
; Tasks are checked unless told otherwise, so this is pre-ticked: the dashboard is a background app
; with no window, so an easy way back into it is the point. Add "Flags: unchecked" to opt out.
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Icons]
; Every entry points at the executable rather than the URL. The app notices it is already running,
; opens the dashboard in the browser and steps aside, so clicking a shortcut always lands on the
; page - even after the app has been closed from the tray.
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--open-dashboard"
Name: "{group}\Start Dashboard"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--open-dashboard"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--open-dashboard"; Tasks: desktopicon
Name: "{userstartup}\Endurance Race Dashboard"; Filename: "{app}\{#MyAppExeName}"

[UninstallRun]
; The dashboard is a windowless executable, so it cannot be closed from a window. Killing it is the
; only way to release the files, and it also removes the tray icon.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden waituntilterminated; RunOnceId: "KillEnduranceRaceProcesses"
; Older versions installed a Windows service that ran the same executable in session 0, where it
; could not start the game. Leave nothing behind for it to hold on to.
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyLegacyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "StopLegacyService"; Check: IsAdminInstallMode
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyLegacyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "DeleteLegacyService"; Check: IsAdminInstallMode

[Run]
; Same cleanup on the way in, so an upgrade does not leave the old session-0 copy fighting for the
; port. Only possible with administrator rights, hence the dialog on the setup page.
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyLegacyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist; Check: IsAdminInstallMode
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyLegacyServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist; Check: IsAdminInstallMode

[Code]
var
  AcPage: TInputDirWizardPage;
  AcFolder: String;
  OriginalBrowseClick: TNotifyEvent;

function IsAssettoCorsaFolder(const Path: String): Boolean;
begin
  Result := False;
  if Path = '' then
    Exit;
  { Both names ship: acs.exe on retail installs, AssettoCorsa.exe on some Steam builds. }
  Result := FileExists(Path + '\acs.exe') or FileExists(Path + '\AssettoCorsa.exe');
end;

function CanWriteTo(const Path: String): Boolean;
var
  TestFile: String;
  Written: Boolean;
begin
  Result := False;
  if Path = '' then
    Exit;

  { A folder can be listed and still refuse writes, and a failure here means the Lua app silently
    does not get installed later, so this is worth the throwaway file. }
  TestFile := Path + '\_endurance_write_test.tmp';
  Written := False;
  try
    Written := SaveStringToFile(TestFile, 'test', False);
  except
    Written := False;
  end;

  if Written then
  begin
    Result := FileExists(TestFile);
    DeleteFile(TestFile);
  end;
end;

function FirstUsableFolder(const Folders: TArrayOfString): String;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to GetArrayLength(Folders) - 1 do
  begin
    if IsAssettoCorsaFolder(Folders[I]) then
    begin
      Result := Folders[I];
      Exit;
    end;
  end;
end;

function ReadSteamPath: String;
var
  Value: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', Value) and (Value <> '') then
  begin
    Result := Value;
    Exit;
  end;

  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath', Value) and
     (Value <> '') and (Value <> '0') then
  begin
    Result := Value;
  end;
end;

function DetectAssettoCorsaFolder: String;
var
  Steam: String;
begin
  Steam := ReadSteamPath;

  { Both spellings exist in the wild. Retail Steam installs use "Assetto Corsa", but plenty of
    installs - including plenty of sim racing setups - use "assettocorsa". }
  if Steam <> '' then
  begin
    Result := FirstUsableFolder([
      Steam + '\steamapps\common\Assetto Corsa',
      Steam + '\steamapps\common\assettocorsa']);
    if Result <> '' then
      Exit;
  end;

  Result := FirstUsableFolder([
    ExpandConstant('{commonpf32}\Steam\steamapps\common\Assetto Corsa'),
    ExpandConstant('{commonpf32}\Steam\steamapps\common\assettocorsa'),
    ExpandConstant('{commonpf}\Steam\steamapps\common\Assetto Corsa'),
    ExpandConstant('{commonpf}\Steam\steamapps\common\assettocorsa'),
    'C:\Assetto Corsa',
    'C:\assettocorsa']);
end;

function ReadPreviousGamePath: String;
var
  Value: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, '{#MyRegKey}', '{#MyRegValue}', Value) and (Value <> '') then
    Result := Value;
end;

procedure RefreshAcNext;
begin
  { Next follows whether there is a usable game folder, so the user cannot walk past the page into
    an install that quietly produces no liveries, no track previews and no Lua app. Checked against
    the box itself rather than a copy taken when the page opened, so it stays honest while editing. }
  AcFolder := Trim(AcPage.Values[0]);
  WizardForm.NextButton.Enabled := IsAssettoCorsaFolder(AcFolder) and CanWriteTo(AcFolder);
end;

procedure AcEditChanged(Sender: TObject);
begin
  RefreshAcNext;
end;

procedure AcBrowseClicked(Sender: TObject);
begin
  { Chained rather than replaced: Setup has its own handler on this button and overwriting it would
    leave a Browse button that does nothing. Refreshed afterwards because the folder picker sets the
    edit's text without going through OnChange, which would otherwise leave Next stale. }
  if OriginalBrowseClick <> nil then
    OriginalBrowseClick(Sender);
  RefreshAcNext;
end;

procedure InitializeWizard;
begin
  { /ACFOLDER= exists so the setup can be tested unattended and so a scripted rollout can skip the
    guessing. What was installed last time is the next best guess, and detection after that. }
  AcFolder := ExpandConstant('{param:ACFOLDER|}');
  if AcFolder = '' then
    AcFolder := ReadPreviousGamePath;
  if AcFolder = '' then
    AcFolder := DetectAssettoCorsaFolder;

  { A directory page rather than a plain text page, so the user gets a Browse button instead of being
    asked to type a path by hand. No "make new folder" button: this folder has to exist already.
    Anchored after the tasks page on purpose - hanging a second directory page directly off the
    program's own Select Directory page makes Setup abort when it tries to leave that page. }
  AcPage := CreateInputDirPage(wpSelectTasks, '', '', '', False, '');
  AcPage.Add('Assetto Corsa Game Folder:');
  AcPage.Values[0] := AcFolder;

  AcPage.Edits[0].OnChange := @AcEditChanged;
  if AcPage.Buttons[0] <> nil then
  begin
    OriginalBrowseClick := AcPage.Buttons[0].OnClick;
    AcPage.Buttons[0].OnClick := @AcBrowseClicked;
  end;
end;

function FolderProblem: String;
var
  Folder: String;
begin
  Folder := Trim(AcPage.Values[0]);
  if Folder = '' then
  begin
    Result := 'Choose the Assetto Corsa folder, the one that contains acs.exe.';
    Exit;
  end;

  if not IsAssettoCorsaFolder(Folder) then
  begin
    Result := 'There is no acs.exe or AssettoCorsa.exe in "' + Folder + '".' + #13#10 + #13#10 +
      'Pick the Assetto Corsa folder itself, not the Steam library that holds it.';
    Exit;
  end;

  Result := 'The dashboard could not write to "' + Folder + '", so the Multiple Class Race app ' +
    'cannot be installed into it.' + #13#10 + #13#10 +
    'Go back and choose "Install for all users".';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { Re-checked on entry as well as on edit: coming back to the page is the one moment the box can
    have changed without firing anything. }
  if CurPageID = AcPage.ID then
    RefreshAcNext;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = AcPage.ID then
  begin
    { Worked out here rather than read back off the Next button: in a silent run there is no button
      to read, and a page nobody can leave is the worst possible outcome for an unattended install. }
    AcFolder := Trim(AcPage.Values[0]);
    Result := IsAssettoCorsaFolder(AcFolder) and CanWriteTo(AcFolder);
    WizardForm.NextButton.Enabled := Result;

    if not Result then
      MsgBox(FolderProblem, mbError, MB_OK);
  end;
end;

function JsonEscape(const Value: String): String;
var
  I: Integer;
  Piece: String;
begin
  { Hand rolled rather than StringChangeEx: only backslashes and quotes can need escaping in a
    Windows path, and a loop says exactly which two without depending on the signature. }
  Result := '';
  for I := 1 to Length(Value) do
  begin
    Piece := Value[I];
    if Piece = '\' then
      Result := Result + '\\'
    else if Piece = '"' then
      Result := Result + '\"'
    else
      Result := Result + Piece;
  end;
end;

procedure WriteInstallJson;
var
  Content: String;
begin
  Content := '{"GamePath":"' + JsonEscape(AcFolder) + '"}';
  { UTF-8 because a Windows user name can carry anything, and the app reads the file with a reader
    that copes with the byte order mark this writes. }
  SaveStringsToUTF8File(ExpandConstant('{app}\install.json'), [Content], False);
  RegWriteStringValue(HKCU, '{#MyRegKey}', '{#MyRegValue}', AcFolder);
  Log('Wrote install.json with game path ' + AcFolder);
end;

{ Destination for the three Lua app files named in [Files]. Asked for while files are copied, which
  is after the wizard pages, so AcFolder is already the user's answer. }
function LuaAppDestDir(Param: String): String;
begin
  Result := AcFolder + '\apps\lua\Multi_Class';
end;

procedure VerifyLuaApp;
var
  DestDir: String;
begin
  DestDir := LuaAppDestDir('');
  if FileExists(DestDir + '\Multi_Class.lua') then
  begin
    Log('Multiple Class Race app installed into ' + DestDir);
    Exit;
  end;

  { Inno reports a copy failure itself and fails the install, so reaching here means something
    quieter went wrong. Worth saying out loud rather than letting the user discover it when the app
    does not show up in Assetto Corsa. }
  MsgBox('The dashboard was installed, but the Multiple Class Race app is not in "' +
    DestDir + '".' + #13#10 + #13#10 +
    'Run the dashboard installer again to put it there, or copy Multi_Class.lua, manifest.ini ' +
    'and icon.png from its assetto-corsa-app\Multi_Class folder into that folder by hand.',
    mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteInstallJson;
    VerifyLuaApp;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if (not IsAdminInstallMode) and
     RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\{#MyLegacyServiceName}') then
  begin
    Result := 'An older version installed a Windows service called {#MyLegacyServiceName}, and it is still running.' +
              Chr(13) + Chr(10) + Chr(13) + Chr(10) +
              'Choose "Install for all users" on the setup page to let the installer remove it. ' +
              'Otherwise the old copy keeps the dashboard port and the new one will not start.';
    Exit;
  end;

  { Unattended installs never show the page, so they get one final chance at the guesses made in
    InitializeWizard instead of writing an install.json that points nowhere. }
  if not IsAssettoCorsaFolder(AcFolder) then
    Result := 'No Assetto Corsa folder was given and none could be detected. Pass /ACFOLDER="<path>" ' +
              'to install unattended, or rerun the setup and choose the folder on screen.';
end;
