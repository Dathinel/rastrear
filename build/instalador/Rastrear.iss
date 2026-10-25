; Instalador de Rastrear (Inno Setup 7).
; Compilado por build\distribuir.ps1.
; Por usuario y sin permisos de administrador: {userpf}\Rastrear (%LOCALAPPDATA%\Programs\Rastrear).

#ifndef Version
  #error Falta /DVersion
#endif
#ifndef Origen
  #error Falta /DOrigen
#endif
#ifndef Licencia
  #error Falta /DLicencia
#endif

[Setup]
AppId={{1B143973-D66A-443A-82CA-6575294736B9}}
AppName=Rastrear
AppVersion={#Version}
AppVerName=Rastrear {#Version}
AppPublisher=Dathinel
AppPublisherURL=https://github.com/Dathinel/rastrear
AppSupportURL=https://github.com/Dathinel/rastrear/issues
AppUpdatesURL=https://github.com/Dathinel/rastrear/releases
AppCopyright=Copyright (C) 2026 Dathinel
VersionInfoVersion={#Version}.0
VersionInfoProductName=Rastrear
VersionInfoDescription=Instalador de Rastrear
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DefaultDirName={userpf}\Rastrear
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
LicenseFile={#Licencia}
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Rastrear
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
OutputBaseFilename=Rastrear-{#Version}-instalador

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Crear un acceso directo en el escritorio"; Flags: unchecked

[Files]
Source: "{#Origen}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Rastrear"; Filename: "{app}\Rastrear.App.exe"; Comment: "Seguimiento y visión con IA para video"
Name: "{autodesktop}\Rastrear"; Filename: "{app}\Rastrear.App.exe"; Comment: "Seguimiento y visión con IA para video"; Tasks: escritorio

[Run]
Filename: "{app}\Rastrear.App.exe"; Description: "Abrir Rastrear"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
