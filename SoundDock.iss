#define AppName "SoundDock"
#ifndef AppVersion
  #define AppVersion "1.4.0"
#endif
#define ExeName "SoundDock.exe"

[Setup]
AppId={{CA25E0B1-C737-4EC1-BC1E-4B23DE9207D2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=SoundDock
AppComments=Taskbar audio controller and output switcher
DefaultDirName={userpf}\SoundDock
DefaultGroupName=SoundDock
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UninstallDisplayName=SoundDock
UninstallDisplayIcon={app}\{#ExeName}
SetupIconFile=SoundDock\Assets\app.ico
OutputDir=dist
OutputBaseFilename=SoundDock-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
AppMutex=Local\SoundDock.8F059D85-3950-437B-BDF2-BDB65CC85E55,Local\BarTone.8F059D85-3950-437B-BDF2-BDB65CC85E55,Local\TaskbarAudio.8F059D85-3950-437B-BDF2-BDB65CC85E55
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SoundDock"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\SoundDock"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "Launch SoundDock"; Flags: nowait postinstall skipifsilent

[Code]
// Only remove the Run value when it refers to the copy being uninstalled.
// User settings are kept so a later reinstall can restore preferences.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: string;
  InstalledCommand: string;
  RunKey: string;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
  InstalledCommand := '"' + ExpandConstant('{app}\SoundDock.exe') + '" --background';
  if RegQueryStringValue(HKCU, RunKey, 'SoundDock', StartupCommand) then
  begin
    if CompareText(StartupCommand, InstalledCommand) = 0 then
      RegDeleteValue(HKCU, RunKey, 'SoundDock');
  end;
end;
