; Inno Setup script for VidSlice.
; Compile locally:  iscc installer\VidSlice.iss
; CI passes the version + source dir, e.g.:
;   iscc /DMyAppVersion=1.0.0 /DSourceDir=..\build\publish installer\VidSlice.iss

#define MyAppName "VidSlice"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "VidSlice"
#define MyAppURL "https://github.com/ninad-k/VidSlice"
#define MyAppExeName "VidSlice.exe"

; Folder containing the published, self-contained app (VidSlice.exe + DLLs + Resources).
#ifndef SourceDir
  #define SourceDir "..\build\publish"
#endif

[Setup]
; Stable AppId so upgrades replace the previous install in place.
AppId={{8F2A6B14-9C3E-4D7A-B5E1-2A7C9D4E6F30}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=VidSlice-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\VidSlice\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
