; PiP Studio installer (Inno Setup 6).
; Built by the release workflow, or locally by "build.bat release" when Inno Setup is installed:
;   ISCC.exe /DAppVersion=1.0.0 /DSourceDir=..\publish-release installer\PiPStudio.iss
; Installs for the current user only (no admin prompt) into %LocalAppData%\Programs\PiP Studio.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish-release"
#endif
#ifndef OutputDir
  #define OutputDir "Output"
#endif

[Setup]
; Keep this AppId forever: it's how Windows knows a new version is an update of the same app.
AppId={{FE41C80D-B047-4C51-BF61-37DAF8585357}
AppName=PiP Studio
AppVersion={#AppVersion}
AppVerName=PiP Studio {#AppVersion}
AppPublisher=NauticalNoodles
AppPublisherURL=https://github.com/NauticalNoodles
DefaultDirName={autopf}\PiP Studio
DefaultGroupName=PiP Studio
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=PiPStudio-Setup-{#AppVersion}
SetupIconFile=..\PiPStudio.ico
UninstallDisplayIcon={app}\PiPStudio.exe
UninstallDisplayName=PiP Studio
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
; close a running copy before installing or uninstalling (the app holds this mutex while it runs)
AppMutex=PiPStudio.SingleInstance.v2
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start PiP Studio when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceDir}\PiPStudio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\PiP Studio"; Filename: "{app}\PiPStudio.exe"
Name: "{autodesktop}\PiP Studio"; Filename: "{app}\PiPStudio.exe"; Tasks: desktopicon

[Registry]
; the same entry the app's own "Start PiP Studio when I sign in" setting uses, so the two always agree
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PiPStudio"; ValueData: """{app}\PiPStudio.exe"" --startup"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\PiPStudio.exe"; Description: "Launch PiP Studio"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // the app can turn "start when I sign in" on by itself; remove that entry too
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PiPStudio');
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{userappdata}\PiPStudio');
    if DirExists(SettingsDir) and not UninstallSilent then
      if MsgBox('Also delete your PiP Studio settings and saved PiPs?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(SettingsDir, True, True, True);
  end;
end;
