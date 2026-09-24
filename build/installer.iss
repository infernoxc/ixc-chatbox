; Inno Setup 6 script for IXC ChatBox (optional .exe installer).
; Built by build\build.ps1 -Installer  (passes AppVersion and SourceDir). Per-user install, no admin rights.
; The .exe simply unpacks the release files and runs scripts\install.ps1 - the same thing Install.bat does.
; Copyright (c) 2026 Ishan (InFerNoxC) - MIT License

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\stage"
#endif

[Setup]
AppId={{8D1F4C22-9E3A-4B7D-B5C6-2A8E0F9D3B02}
AppName=IXC ChatBox
AppVersion={#AppVersion}
AppPublisher=Ishan (InFerNoxC)
AppPublisherURL=https://github.com/infernoxc/ixc-chatbox
AppSupportURL=https://github.com/infernoxc/ixc-chatbox/issues
DefaultDirName={localappdata}\IXC-OBS\package-IXC-ChatBox
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=IXC-ChatBox-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile={#SourceDir}\LICENSE
UninstallDisplayName=IXC ChatBox
Uninstallable=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\install.ps1"""; StatusMsg: "Installing IXC ChatBox..."; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{localappdata}\IXC-OBS\app\scripts\uninstall.ps1"" -App chat"; Flags: runhidden waituntilterminated; RunOnceId: "IXCUninstall"
