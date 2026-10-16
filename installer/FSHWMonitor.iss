; Installer for FS HW Monitor (Inno Setup 6). build.ps1 -Package runs it with the version from the .csproj:
;   ISCC.exe /DAppVersion=<version> installer\FSHWMonitor.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "FS HW Monitor"
#define AppExe "FSHWMonitor.exe"

[Setup]
; Never change AppId: upgrades and uninstall find the installed copy by it.
AppId={{A0FA562C-CB83-4E06-B7F6-1E9C45AE1F06}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=FS-HW-Monitor-Setup-{#AppVersion}
SetupIconFile=..\src\FSHWMonitor\Assets\fshw.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; The app reads hardware sensors as administrator, so it installs for all users.
PrivilegesRequired=admin
MinVersion=10.0
; Close a running copy before replacing it.
CloseApplications=yes
RestartApplications=no
#ifdef Sign
; build.ps1 -Sign defines the "fssign" command; this signs the setup and its uninstaller.
SignTool=fssign
SignedUninstaller=yes
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"
Name: "pawnio"; Description: "Install the PawnIO sensor driver (needed for CPU, RAM and fan temperatures)"; Check: not IsPawnIoInstalled

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; PawnIO is a separate signed driver; winget fetches the official installer.
Filename: "{sys}\cmd.exe"; Parameters: "/c winget install --id namazso.PawnIO --exact --source winget --silent --accept-package-agreements --accept-source-agreements"; \
  StatusMsg: "Installing the PawnIO sensor driver..."; Flags: runhidden waituntilterminated; Tasks: pawnio
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove the "Start with Windows" task if the app created one.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#AppName}"" /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"

[Code]
function IsPawnIoInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // winget can't always reach the network; tell the user how to finish later instead of failing.
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('pawnio') and not IsPawnIoInstalled and not WizardSilent then
    MsgBox('The PawnIO sensor driver could not be installed automatically.' + #13#10#13#10 +
           'FS HW Monitor still works, but CPU, RAM and fan temperatures need it. ' +
           'Right-click the dashboard later and choose "Install sensor driver (PawnIO)".',
           mbInformation, MB_OK);
end;
