; Gravicode CAD Studio - Windows installer (Inno Setup 6).
; Built by .github/workflows/gravicode-cad-studio-release.yml:
;   iscc /DAppVersion=1.0.0 /DSourceDir=<publish dir> /DOutputDir=<dir> GravicodeCadStudio.iss
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\studio\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\release"
#endif

[Setup]
AppId={{6E4E8B3A-2C7F-4B8E-9C41-6A7D3E1F0C5B}
AppName=Gravicode CAD Studio
AppVersion={#AppVersion}
AppVerName=Gravicode CAD Studio {#AppVersion}
AppPublisher=Gravicode Studios
AppPublisherURL=https://github.com/DotNetVibeCoderz/Vibe_CAD
AppSupportURL=https://github.com/DotNetVibeCoderz/Vibe_CAD/issues
DefaultDirName={autopf}\Gravicode CAD Studio
DefaultGroupName=Gravicode CAD Studio
UninstallDisplayIcon={app}\GravicodeCadStudio.exe
SetupIconFile=..\..\assets\icon.ico
OutputDir={#OutputDir}
OutputBaseFilename=GravicodeCadStudio-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assoc"; Description: "Open .gcproj files with Gravicode CAD Studio"; GroupDescription: "File associations:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Gravicode CAD Studio"; Filename: "{app}\GravicodeCadStudio.exe"
Name: "{group}\{cm:UninstallProgram,Gravicode CAD Studio}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Gravicode CAD Studio"; Filename: "{app}\GravicodeCadStudio.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.gcproj"; ValueType: string; ValueName: ""; ValueData: "GravicodeCadStudio.Project"; Flags: uninsdeletevalue; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\GravicodeCadStudio.Project"; ValueType: string; ValueName: ""; ValueData: "Gravicode CAD project"; Flags: uninsdeletekey; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\GravicodeCadStudio.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\GravicodeCadStudio.exe,0"; Tasks: assoc
Root: HKA; Subkey: "Software\Classes\GravicodeCadStudio.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GravicodeCadStudio.exe"" ""%1"""; Tasks: assoc

[Run]
Filename: "{app}\GravicodeCadStudio.exe"; Description: "{cm:LaunchProgram,Gravicode CAD Studio}"; Flags: nowait postinstall skipifsilent
