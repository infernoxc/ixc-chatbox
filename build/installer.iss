; IXC Suite installer (IXC Music + IXC ChatBox) - Inno Setup 6. Built by build\build.ps1 -Installer.
; Per user, no admin rights. Express setup by default; "Advanced" shows the options. The real work is done by
; scripts\install.ps1 (the same script Install.bat uses), so both ways install exactly the same thing.
; Copyright (c) 2026 Ishan (InFerNoxC) - MIT License

#ifndef AppVersion
  #define AppVersion "3.0.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\stage"
#endif

[Setup]
AppId={{8D1F4C22-9E3A-4B7D-B5C6-2A8E0F9D3B02}
AppName=IXC
AppVersion={#AppVersion}
AppVerName=IXC {#AppVersion}
AppPublisher=Ishan (InFerNoxC)
AppPublisherURL=https://github.com/infernoxc/ixc-chatbox
AppSupportURL=https://github.com/infernoxc/ixc-chatbox/issues
DefaultDirName={localappdata}\IXC-OBS\setup
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=IXC-Setup-v{#AppVersion}
SetupIconFile={#SourceDir}\src\core\ixc.ico
UninstallDisplayIcon={localappdata}\IXC-OBS\app\core\ixc-core.exe
UninstallDisplayName=IXC (Music + ChatBox for OBS)
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile={#SourceDir}\LICENSE
CloseApplications=no
MinVersion=10.0
Uninstallable=yes

[Tasks]
Name: "startup"; Description: "Start IXC with Windows (recommended - the chat voice, music and phone remote are always ready)"
Name: "obs"; Description: "Set up OBS automatically (turn on its WebSocket server and add the IXC panels)"
Name: "desktop"; Description: "Desktop icon"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[UninstallRun]
Filename: "{sysnative}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{localappdata}\IXC-OBS\app\scripts\uninstall.ps1"" -Quiet"; Flags: runhidden waituntilterminated; RunOnceId: "IXCUninstall"

[Run]
Filename: "{localappdata}\IXC-OBS\app\core\ixc-core.exe"; Description: "Open IXC and finish the setup"; Flags: postinstall nowait skipifsilent; Check: Installed

[Code]
var
  ModePage: TInputOptionWizardPage; Ok: Boolean; ResultMsg: String;

function Installed(): Boolean; begin Result := Ok; end;

function DotNet48(): Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not DotNet48() then begin
    if not WizardSilent() then MsgBox('IXC needs .NET Framework 4.8, which is part of Windows 10 (version 1903 or newer) and Windows 11.' + #13#10#13#10 + 'Please run Windows Update, then start this setup again.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure InitializeWizard();
begin
  ModePage := CreateInputOptionPage(wpLicense, 'How do you want to install IXC?', 'Most people choose Express.',
    'Express sets everything up for you. Advanced lets you choose what IXC changes on this PC.', True, False);
  ModePage.Add('Express (recommended) - install, start IXC with Windows and set up OBS automatically');
  ModePage.Add('Advanced - choose the options yourself');
  ModePage.SelectedValueIndex := 0;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = wpSelectTasks) and (ModePage.SelectedValueIndex = 0);
end;

function ObsRunning(): Boolean;
var Code: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\cmd.exe'), '/c tasklist /FI "IMAGENAME eq obs64.exe" | find /I "obs64.exe" >nul', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  // OBS rewrites its settings when it closes, so IXC can only add its panels while OBS is closed
  if (CurPageID = wpReady) and not WizardSilent() and (WizardIsTaskSelected('obs') or (ModePage.SelectedValueIndex = 0)) then
    while ObsRunning() do
      if MsgBox('Please close OBS Studio (File > Exit) so IXC can add its panels to OBS.' + #13#10#13#10 + 'Click Retry after closing OBS, or Cancel to continue without changing OBS now (IXC can do it later from System check).', mbInformation, MB_RETRYCANCEL) = IDCANCEL then Break;
end;

function ReadReport(F: String): String;
var S: AnsiString; P: Integer;
begin
  Result := '';
  if LoadStringFromFile(F, S) then begin
    P := Pos('"message":', S);
    if P > 0 then begin Result := Copy(S, P + 12, 400); P := Pos('"', Result); if P > 0 then Result := Copy(Result, 1, P - 1); end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Args, Report: String; Code: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  Report := ExpandConstant('{tmp}\ixc-install.json');
  Args := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\install.ps1') + '" -Report "' + Report + '"';
  if (ModePage.SelectedValueIndex = 1) then begin
    if not WizardIsTaskSelected('startup') then Args := Args + ' -NoStartup';
    if not WizardIsTaskSelected('obs') then Args := Args + ' -NoObs';
    if WizardIsTaskSelected('desktop') then Args := Args + ' -Desktop';
  end;
  WizardForm.StatusLabel.Caption := 'Setting up IXC (stopping the old version, backing up your settings, starting IXC)...';
  Ok := Exec(ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  ResultMsg := ReadReport(Report);
  if not Ok then begin
    if ResultMsg = '' then ResultMsg := 'IXC could not be installed (error ' + IntToStr(Code) + ').';
    if not WizardSilent() then MsgBox(ResultMsg + #13#10#13#10 + 'Nothing was lost: your settings were backed up. You can run this setup again, or see docs\TROUBLESHOOTING.md.', mbError, MB_OK);
  end;
end;
