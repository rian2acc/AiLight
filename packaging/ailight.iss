; AIlight — Installer (Inno Setup 6)
;
; Ziel: Ein Freund lädt eine Datei, führt sie aus, und danach läuft das Programm.
; Keine Administratorrechte, keine .NET-Installation (die Anwendung bringt alles
; mit), Autostart-Eintrag, Deinstaller, Nutzerdaten bleiben beim Entfernen erhalten.
;
; Bauen:
;   ISCC.exe packaging\ailight.iss
; Erwartet dist\publish (dotnet publish, siehe build.ps1).
;
; ── Wenn der Virenschutz meckert ─────────────────────────────────────────────
; Der Installer ist nicht signiert. Manche Virenschutz-Programme stufen eine
; unbekannte, frisch gebaute Setup-Datei deshalb als verdächtig ein — das ist
; eine Heuristik, kein Befund. Was dagegen hilft, steht in docs/signieren.md:
; in Kurzform
;
;   1. signieren (der Haken unten, siehe #ifdef SIGN) — das ist die einzige
;      echte Abhilfe; eine Prüfsumme oder ein paar Metadaten ändern daran nichts,
;   2. eine Falschmeldung melden (Microsoft und McAfee haben dafür Formulare),
;   3. solange nichts signiert ist: die Portable-Fassung als ZIP anbieten, die
;      ohne Installer auskommt (build.ps1 -Portable).
;
; Zur Verdichtung: "lzma2/ultra64" wurde hier durch "lzma2/max" ersetzt — die
; Stufe, die Inno Setup selbst vorschlägt. Ein einzelnes Einstellungsfeld ist
; keine belegte Lösung für Fehlalarme; es macht den Installer aber weniger
; auffällig und schneller entpackbar.

#define AppName "AIlight"
#define AppVersion "2.4.0"
#define AppPublisher "AIlight"
#define AppPublisherUrl "https://github.com/rian2acc/AiLight"
#define AppSupportUrl "https://github.com/rian2acc/AiLight/issues"
#define AppUpdatesUrl "https://github.com/rian2acc/AiLight/releases/latest"
#define AppExe "AIlight.exe"
#define SourceDir "..\dist\publish"

[Setup]
AppId={{8F3C2B41-6D7E-4A19-9C22-5B7E1A0D4C33}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppPublisherUrl}
AppSupportURL={#AppSupportUrl}
AppUpdatesURL={#AppUpdatesUrl}
AppCopyright=Copyright (C) 2026 {#AppPublisher}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup — KI-Leiste für Windows
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
AllowNoIcons=yes
; Kein AppMutex: Eine laufende Fassung wird in InitializeSetup selbst beendet
; und abgewartet — sonst bricht eine erneute Installation (Update) ab, weil
; Inno Setup den Mutex der laufenden Instanz noch sieht.
; Keine Administratorrechte: Installation im Benutzerprofil.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist\installer
OutputBaseFilename=AIlightSetup
SetupIconFile=ailight.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; Verdichtung: die mittlere Stufe von Inno Setup. "ultra64" presste zwar noch
; etwas kleiner, kostete aber beim Entpacken viel Arbeitsspeicher und Zeit —
; und je stärker gepackt ein unbekanntes Setup aussieht, desto eher greifen
; Heuristiken zu.
#ifdef NoCompression
Compression=none
#else
Compression=lzma2/max
#endif
SolidCompression=yes
WizardStyle=modern
; Wird nur gesetzt, wenn mit /DSIGN gebaut wird (siehe tools/build.ps1 -Sign).
; Dann signiert Inno Setup auch den Deinstaller.
#ifdef SIGN
SignTool=signtool
SignedUninstaller=yes
#endif
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartApplications=no

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "AIlight mit Windows starten (empfohlen)"; GroupDescription: "Start:"; Flags: checkedonce
Name: "desktopicon"; Description: "Verknüpfung auf dem Desktop anlegen"; GroupDescription: "Start:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\{#AppName} deinstallieren"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Nach der Installation gleich starten: Beim ersten Start öffnet sich das
; Einstellungsfenster und fragt nach dem Groq-Schlüssel.
Filename: "{app}\{#AppExe}"; Description: "AIlight jetzt starten"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Laufende Instanz vor dem Entfernen sauber beenden.
Filename: "{app}\{#AppExe}"; Parameters: "--quit"; Flags: runhidden; RunOnceId: "AIlightBeenden"

[UninstallDelete]
; Nur der Programmordner wird entfernt. Nutzerdaten (%APPDATA%\AIlight) bleiben
; absichtlich erhalten — Schlüssel und Verlauf sollen ein Neuinstallieren überleben.
Type: filesandordirs; Name: "{app}\logs"

[Registry]
; Autostart-Eintrag nur anlegen, wenn die Aufgabe gewählt wurde. Die App hält
; ihn danach selbst aktuell (Häkchen in den Einstellungen).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "AIlight"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart; Flags: uninsdeletevalue

[Code]
{ Die Deinstallation muss den Autostart-Eintrag auch dann entfernen, wenn der
  Nutzer ihn in den Einstellungen selbst gesetzt hat. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AIlight');
end;

{ Beim Deinstallieren dasselbe: erst die laufende Fassung beenden, sonst meckert
  die Deinstallation, das Programm laufe noch — und bricht im stillen Lauf ab. }
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
  Existing: String;
begin
  Result := True;
  Existing := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Existing) then
  begin
    Exec(Existing, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2000);
  end;
end;

{ Beim Installieren eine eventuell laufende Fassung beenden, damit die Dateien
  ersetzt werden können. Der Aufruf benachrichtigt nur und endet sofort — das
  Herunterfahren der laufenden Fassung braucht danach noch einen Moment. }
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
  Existing: String;
begin
  Result := True;
  Existing := ExpandConstant('{localappdata}\{#AppName}\{#AppExe}');
  if FileExists(Existing) then
  begin
    Exec(Existing, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2000);
  end;
end;
