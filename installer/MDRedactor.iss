#define AppName "MD Redactor"
#define AppVersion "0.1.0"
#define AppExeName "MDRedactor.App.exe"
#define PublisherName "MD Redactor"

[Setup]
AppId={{6A6F7D2F-1F4C-4D28-9863-7E7A188D12D8}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#PublisherName}
DefaultDirName={localappdata}\Programs\MD Redactor
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=MDRedactorSetup-x64
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\MDRedactor.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
ChangesAssociations=yes
ShowLanguageDialog=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.MarkdownDocument=MD Redactor Markdown document
russian.MarkdownDocument=Markdown-документ MD Redactor

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MD Redactor"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\MD Redactor"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\MDRedactor.App.exe.WebView2"
Type: dirifempty; Name: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "MD Redactor"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletevalue

Root: HKCU; Subkey: "Software\Classes\.md\OpenWithList\{#AppExeName}"; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: none; ValueName: "MDRedactor.md"; Flags: uninsdeletevalue

Root: HKCU; Subkey: "Software\Classes\MDRedactor.md"; ValueType: string; ValueName: ""; ValueData: "{cm:MarkdownDocument}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\MDRedactor.md\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\MDRedactor.md\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletevalue
