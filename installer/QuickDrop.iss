[Setup]
AppId=QuickDrop.Renanlvrt
AppName=QuickDrop
AppVersion=0.1.0
AppPublisher=Renanlvrt
DefaultDirName={localappdata}\Programs\QuickDrop
DefaultGroupName=QuickDrop
UninstallDisplayIcon={app}\QuickDrop.exe
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir=..\release
OutputBaseFilename=QuickDrop-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "..\release\QuickDrop.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\QuickDrop"; Filename: "{app}\QuickDrop.exe"

[Run]
Filename: "{app}\QuickDrop.exe"; Description: "Launch QuickDrop"; Flags: nowait postinstall skipifsilent
