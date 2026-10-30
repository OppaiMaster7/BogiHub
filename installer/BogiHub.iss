; BogiHub installer (Inno Setup 6.7+). Build it with scripts/build-installer.ps1, which publishes the app, renders
; the artwork and passes AppVersion / PublishDir / ArtDir / ViGEmSetup on the command line.
;
; Per-user install: no admin rights for BogiHub itself (it lands in %LOCALAPPDATA%\Programs\BogiHub). Only the
; optional ViGEmBus driver asks for admin, through its own signed installer.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\BogiHub"
#endif
#ifndef ArtDir
  #define ArtDir "..\artifacts\installer-art"
#endif
#define ViGEmName "ViGEmBus_1.22.0_x64_x86_arm64.exe"
#ifndef ViGEmSetup
  #define ViGEmSetup "..\third_party\ViGEmBus\" + ViGEmName
#endif
#define RepoUrl "https://github.com/OppaiMaster7/BogiHub"

[Setup]
AppId={{F6382240-C16C-49BA-B123-E06191E8A22D}
AppName=BogiHub
AppVersion={#AppVersion}
AppVerName=BogiHub {#AppVersion}
AppPublisher=OppaiMaster7
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
AppCopyright=MIT License
VersionInfoVersion={#AppVersion}
VersionInfoDescription=BogiHub Setup

PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DefaultDirName={autopf}\BogiHub
DefaultGroupName=BogiHub
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableReadyPage=yes
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no

WizardStyle=modern dark hidebevels
WizardBackColor=#0A0C0F
WizardImageFile={#ArtDir}\wizard-100.png,{#ArtDir}\wizard-200.png
WizardSmallImageFile={#ArtDir}\small-100.png,{#ArtDir}\small-200.png
SetupIconFile=..\assets\bogihub.ico
UninstallDisplayIcon={app}\BogiHub.exe
UninstallDisplayName=BogiHub

OutputDir=..\dist
OutputBaseFilename=BogiHub-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes

[Messages]
WelcomeLabel2=This will install BogiHub {#AppVersion} on your computer.%n%nBogiHub makes the Steam Controller work as an Xbox controller, or as mouse and keyboard, in games outside Steam.%n%nIf BogiHub is running, Setup closes it for you first.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "autostart"; Description: "Start BogiHub with Windows (quietly, in the tray)"; GroupDescription: "Startup:"; Flags: unchecked
Name: "vigem"; Description: "Install the ViGEmBus driver: needed for gamepad mode (Windows asks for admin once)"; GroupDescription: "Driver:"; Check: not ViGEmInstalled

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ViGEmSetup}"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\BogiHub"; Filename: "{app}\BogiHub.exe"; Comment: "Steam Controller companion"
Name: "{autodesktop}\BogiHub"; Filename: "{app}\BogiHub.exe"; Comment: "Steam Controller companion"; Tasks: desktopicon

[Registry]
; Same value BogiHub's own "Start with Windows" setting uses, so the two stay in step.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "BogiHub"; ValueData: """{app}\BogiHub.exe"" --minimized"; Tasks: autostart
; Always removed on uninstall, also when it was switched on from inside the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "BogiHub"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\BogiHub.exe"; Description: "Open BogiHub now"; Flags: nowait postinstall skipifsilent

[Code]
const
  EVENT_MODIFY_STATE = $0002;
  SYNCHRONIZE = $00100000;
  WAIT_OBJECT_0 = 0;
  WAIT_ABANDONED = $80;

var
  NeedsReboot: Boolean;

function OpenEvent(Access: DWORD; Inherit: BOOL; Name: String): THandle; external 'OpenEventW@kernel32.dll stdcall';
function OpenMutex(Access: DWORD; Inherit: BOOL; Name: String): THandle; external 'OpenMutexW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): BOOL; external 'SetEvent@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Ms: DWORD): DWORD; external 'WaitForSingleObject@kernel32.dll stdcall';
function ReleaseMutex(Handle: THandle): BOOL; external 'ReleaseMutex@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): BOOL; external 'CloseHandle@kernel32.dll stdcall';

function ViGEmInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

{ Asks a running BogiHub to quit the normal way (same signal as "BogiHub.exe --exit"), so it releases every key and
  button it holds and unplugs its virtual pad, then waits up to 15 s for it to be gone. True = not running any more. }
function CloseBogiHub: Boolean;
var
  Ev, Mx: THandle;
  R: DWORD;
begin
  Result := True;
  Mx := OpenMutex(SYNCHRONIZE, False, 'Local\BogiHub.SingleInstance');
  if Mx = 0 then Exit; { not running }
  Ev := OpenEvent(EVENT_MODIFY_STATE, False, 'Local\BogiHub.Exit');
  if Ev <> 0 then
  begin
    SetEvent(Ev);
    CloseHandle(Ev);
  end;
  R := WaitForSingleObject(Mx, 15000);
  Result := (R = WAIT_OBJECT_0) or (R = WAIT_ABANDONED);
  { Never keep it: a BogiHub started by Setup must be able to take it. }
  if Result then ReleaseMutex(Mx);
  CloseHandle(Mx);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CloseBogiHub then
    Result := 'BogiHub is still running and did not close. Exit it from its tray icon, then try again.';
end;

procedure InstallViGEm;
var
  Code: Integer;
begin
  WizardForm.StatusLabel.Caption := 'Installing the ViGEmBus driver (confirm the Windows prompt)...';
  ExtractTemporaryFile('{#ViGEmName}');
  { The driver installs for the whole PC, so it needs admin: "runas" shows the UAC prompt for the signed setup. }
  if not ShellExec('runas', ExpandConstant('{tmp}\{#ViGEmName}'), '/exenoui /qn /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) then
    MsgBox('The ViGEmBus driver was not installed (the Windows prompt was cancelled).' + #13#10#13#10 +
           'BogiHub still works as mouse and keyboard. For gamepad mode, install the driver later from BogiHub > Settings.', mbInformation, MB_OK)
  else if Code = 3010 then
    NeedsReboot := True
  else if (Code <> 0) and not ViGEmInstalled then
    MsgBox('The ViGEmBus driver setup ended with code ' + IntToStr(Code) + '.' + #13#10#13#10 +
           'BogiHub still works as mouse and keyboard. For gamepad mode, install the driver later from BogiHub > Settings.', mbInformation, MB_OK);
end;

{ There is no "Ready" page, so the tasks page is the last stop before installing: say so on the button. }
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectTasks then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('vigem') then InstallViGEm;
end;

function NeedRestart: Boolean;
begin
  Result := NeedsReboot;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if not CloseBogiHub then
      MsgBox('BogiHub did not close by itself. If removal fails, exit it from its tray icon and run the uninstaller again.', mbInformation, MB_OK);
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    Data := ExpandConstant('{userappdata}\BogiHub');
    if DirExists(Data) and not UninstallSilent then
      if MsgBox('Also delete your BogiHub settings, profiles and logs?' + #13#10#13#10 +
                'Choose No to keep them for a later reinstall.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(Data, True, True, True);
  end;
end;
