#ifndef RepoRoot
  #error Build this installer using build-installer.cmd
#endif
#include "Prerequisites.iss"

[Setup]
AppId={{A49DDE6A-1C37-4F62-AC69-DC8351065B5A}
AppName=Windows Alarm
AppVersion=1.0.0
AppPublisher=JaehongLIM2
AppPublisherURL=https://github.com/JaehongLIM2/alarm_program
DefaultDirName={localappdata}\Programs\WindowsAlarm
DefaultGroupName=Windows Alarm
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
DisableProgramGroupPage=yes
OutputDir={#RepoRoot}\artifacts\installer
OutputBaseFilename=WindowsAlarm-Setup-win-x64
Compression=lzma2/fast
SolidCompression=yes
UninstallDisplayIcon={app}\WindowsAlarm.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UsePreviousLanguage=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면 바로가기 만들기"; Flags: unchecked

[Files]
Source: "{#AppDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\scripts\Remove-InstalledAlarm.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "{#RepoRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\docs\*.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#DependencyDir}\{#DotNetFile}"; Flags: dontcopy nocompression
Source: "{#DependencyDir}\{#WebViewFile}"; Flags: dontcopy nocompression

[Icons]
Name: "{userprograms}\Windows Alarm"; Filename: "{app}\WindowsAlarm.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Windows Alarm"; Filename: "{app}\WindowsAlarm.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WindowsAlarm.exe"; WorkingDir: "{app}"; Description: "Windows Alarm 실행"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function InitializeUninstall: Boolean;
var
  ExitCode: Integer;
  DataArg: String;
begin
  { All commands target this app's tasks/data. Shared runtimes remain installed. }
  DataArg := ExpandConstant('{param:ALARMDATADIR|{localappdata}\WindowsAlarm}');
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\tools\Remove-InstalledAlarm.ps1') + '" -InstallDirectory "' +
    ExpandConstant('{app}') + '" -DataDirectory "' + DataArg + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Result := Result and (ExitCode = 0);
  if not Result then
    MsgBox('예약 정리에 실패했습니다. 앱을 닫고 로그와 데이터 폴더 권한을 확인한 뒤 제거를 다시 실행하세요.', mbError, MB_OK);
end;
