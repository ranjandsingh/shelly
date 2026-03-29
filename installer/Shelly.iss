#define MyAppName "Shelly"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "ranjandsingh"
#define MyAppURL "https://github.com/ranjandsingh/shelly"
#define MyAppExeName "Shelly.exe"

[Setup]
AppId={{8F2B4A6E-3C1D-4E7F-9A5B-2D8C6F0E1A3B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputBaseFilename=Shelly-{#MyAppVersion}-setup
SetupIconFile=..\Resources\windows\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
OutputDir=..\artifacts
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupentry"; Description: "Start with &Windows"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; \
  Flags: nowait postinstall skipifsilent

[Code]
function IsWebView2Installed: Boolean;
var
  RegValue: String;
begin
  Result := RegQueryStringValue(HKLM,
    'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'pv', RegValue) or
    RegQueryStringValue(HKCU,
    'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'pv', RegValue);
end;

procedure InstallWebView2;
var
  ResultCode: Integer;
begin
  DownloadTemporaryFile(
    'https://go.microsoft.com/fwlink/p/?LinkId=2124703',
    'MicrosoftEdgeWebview2Setup.exe', '', nil);
  Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'),
    '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    if not IsWebView2Installed then
    begin
      Log('WebView2 Runtime not found, installing...');
      InstallWebView2;
    end;
  end;
end;
