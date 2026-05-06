#define AppName "Desktop Capsules"
#define AppExeName "DesktopCapsules.exe"
#define AppPublisher "Desktop Capsules"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#ifndef RuntimeMode
  #define RuntimeMode "framework-dependent"
#endif
#ifndef RequiresRuntime
  #define RequiresRuntime "true"
#endif

[Setup]
AppId={{87ECBB82-7637-4572-8F05-C96E642970C5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\DesktopCapsules
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=DesktopCapsules-{#AppVersion}-{#RuntimeMode}-Setup
SetupIconFile=..\Assets\DesktopCapsules.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function RequiresDotNetRuntime(): Boolean;
begin
  Result := CompareText('{#RequiresRuntime}', 'true') = 0;
end;

function IsWindowsDesktopRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
  RuntimeDir: string;
begin
  Result := False;
  RuntimeDir := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');

  if FindFirst(RuntimeDir + '\9.*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  if RequiresDotNetRuntime() and not IsWindowsDesktopRuntimeInstalled() then
  begin
    if MsgBox(
      'Desktop Capsules requires the Microsoft .NET 9 Windows Desktop Runtime (x64).' + #13#10 + #13#10 +
      'Open the download page now?',
      mbConfirmation,
      MB_YESNO) = IDYES then
    begin
      ShellExec(
        'open',
        'https://dotnet.microsoft.com/download/dotnet/9.0',
        '',
        '',
        SW_SHOWNORMAL,
        ewNoWait,
        ResultCode);
    end;

    Result := False;
  end;
end;
