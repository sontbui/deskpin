; Inno Setup script for RDP Manager (WinUI 3, unpackaged, self-contained publish).
; 1. Install Inno Setup: https://jrsoftware.org/isdl.php
; 2. Publish the app first (see installer/BUILD-INSTALLER.md).
; 3. Open this file in Inno Setup and click Build (or run: iscc installer\RdpManager.iss).
; Output: installer\Output\RdpManager-Setup.exe

#define AppName "Deskpin"
; AppVersion can be overridden by CI:  iscc /DAppVersion=1.2.3 installer\RdpManager.iss
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "sontbui"
#define AppExe "Deskpin.exe"
; Path to the self-contained publish folder (relative to this .iss file).
#define PublishDir "..\src\RdpManager.Presentation\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\RDP Manager
DefaultGroupName=RDP Manager
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=Output
OutputBaseFilename=Deskpin-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Per-user install so no admin rights are needed.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\RDP Manager"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\RDP Manager"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch RDP Manager"; Flags: nowait postinstall skipifsilent
