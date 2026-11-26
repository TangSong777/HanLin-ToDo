#define MyAppName "HanLin-ToDo"
#define SetupIntroZh "把待办便签放在 Windows 桌面。升级便携版时，可以选择原来的程序文件夹。"
#define SetupIntroEn "Keep your tasks on the Windows desktop. To upgrade a portable copy, select its existing folder."

#ifndef PayloadDir
  #error Use tools/Build-Installer.ps1 to prepare PayloadDir.
#endif

#ifndef MyAppVersion
  #error Use tools/Build-Installer.ps1 to read the project version.
#endif

#include "Brand\HanLinWizard.iss"

[Setup]
AppId={{2D0974C7-F89D-42AF-9C3E-409258E31C87}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=TangSong777
VersionInfoProductName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={code:DefaultInstallDir}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
OutputBaseFilename=HanLin-ToDo-Setup-{#MyAppVersion}
SetupIconFile=..\src\HanLin.ToDo\Assets\HanLin-ToDo.ico
UninstallDisplayIcon={app}\HanLin-ToDo.exe

[Files]
Source: "{#PayloadDir}\HanLin-ToDo.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\HanLin-ToDo"; Filename: "{app}\HanLin-ToDo.exe"; WorkingDir: "{app}"
Name: "{group}\设置"; Filename: "{app}\HanLin-ToDo.exe"; Parameters: "--settings"; WorkingDir: "{app}"

[Run]
Filename: "{app}\HanLin-ToDo.exe"; WorkingDir: "{app}"; Description: "{cm:HanLinLaunch,{#MyAppName}}"; Flags: postinstall nowait skipifsilent

[Code]
const RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function StartupExecutable: String;
var
  Command: String;
  EndQuote: Integer;
begin
  Result := '';
  if RegQueryStringValue(HKCU, RunKey, 'HanLin-ToDo', Command) then
  begin
    if (Length(Command) < 2) or (Command[1] <> '"') then Exit;
    Delete(Command, 1, 1);
    EndQuote := Pos('"', Command);
    if EndQuote > 1 then Result := Copy(Command, 1, EndQuote - 1);
  end;
end;

function DefaultInstallDir(Param: String): String;
var
  Exe: String;
begin
  Exe := StartupExecutable;
  if (CompareText(ExtractFileName(Exe), 'HanLin-ToDo.exe') = 0) and FileExists(Exe) then
    Result := ExtractFileDir(Exe)
  else
    Result := ExpandConstant('{localappdata}\Programs\HanLin\HanLin-ToDo');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and
      (CompareText(StartupExecutable, ExpandConstant('{app}\HanLin-ToDo.exe')) = 0) then
    RegDeleteValue(HKCU, RunKey, 'HanLin-ToDo');
end;
