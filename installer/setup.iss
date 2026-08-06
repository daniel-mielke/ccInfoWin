; CCInfoWindows Inno Setup Script
; Requires Inno Setup 6.x (the standard download includes the ISPP preprocessor).

#define MyAppName "CCInfoWindows"
#define MyAppPublisher "Daniel Mielke"
#define MyAppURL "https://github.com/daniel-mielke/ccInfoWin"
#define MyAppExeName "CCInfoWindows.exe"

; The only sanctioned Release output directory -- see CLAUDE.md "Release Build Rules (STRICT)".
; dotnet publish is forbidden; its win-x64\ subtree is excluded in [Files] so a leftover copy
; can never be packaged in place of the current build.
#define ReleaseDir "..\CCInfoWindows\CCInfoWindows\bin\x64\Release\net9.0-windows10.0.19041.0"

; [Files] resolves relative paths against the script directory, preprocessor functions against
; the compiler's working directory -- hence the explicit SourcePath for the checks below.
#define ReleaseExePath AddBackslash(SourcePath) + AddBackslash(ReleaseDir) + MyAppExeName

#if !FileExists(ReleaseExePath)
  #error "Release output missing. Run: dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj -c Release -o CCInfoWindows/CCInfoWindows/bin/x64/Release/net9.0-windows10.0.19041.0/"
#endif

; Single source of truth for the version: the binary that is actually packaged. UpdateService
; compares the GitHub release tag against that same assembly version, so the number shown in
; Apps & Features, the installer filename and the update oracle cannot drift apart.
#define MyAppVersion GetVersionNumbersString(ReleaseExePath)

; Runtime data directory (Helpers/AppPaths.cs). Disjoint from {app}, so the uninstaller has to
; remove it explicitly -- it contains the WebView2 cookie jar with a live claude.ai sessionKey.
#define MyAppDataDir "{localappdata}\CCInfoWindows"

; Credential Manager targets from Services/CredentialService.cs.
#define SessionCredentialTarget "CCInfoWindows/claude-session"
#define OrgCredentialTarget "CCInfoWindows/claude-org"

[Setup]
AppId={{B8F2A1C3-D4E5-4F67-8901-2A3B4C5D6E7F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=CCInfoWindows-{#MyAppVersion}-Setup
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checked
Name: "autostart"; Description: "Start at Windows login"; GroupDescription: "Options:"; Flags: checked

[Files]
Source: "{#ReleaseDir}\*"; DestDir: "{app}"; Excludes: "\win-x64,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Tasks: autostart

; The in-app Settings toggle (Helpers/RegistryHelper.cs) writes the same value without the
; install-time task, so removal must not depend on the task having been selected.
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: none; ValueName: "{#MyAppName}"; \
  Flags: dontcreatekey uninsdeletevalue

[UninstallDelete]
; Settings, caches, usage history, crash log and the WebView2 profile whose cookie store holds
; a live claude.ai sessionKey (CLAUDE.md: purge cached copies of sensitive data).
Type: filesandordirs; Name: "{#MyAppDataDir}"

[UninstallRun]
; cmdkey is in-box and only ever receives target names, never the secret itself. It exits
; non-zero when a target is absent, which Inno ignores for [UninstallRun] entries.
Filename: "{sys}\cmdkey.exe"; Parameters: "/delete:{#SessionCredentialTarget}"; \
  Flags: runhidden; RunOnceId: "DeleteSessionCredential"
Filename: "{sys}\cmdkey.exe"; Parameters: "/delete:{#OrgCredentialTarget}"; \
  Flags: runhidden; RunOnceId: "DeleteOrgCredential"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
