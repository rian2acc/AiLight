# AIlight — KI-Leiste für Windows

Ein Hotkey, eine Zeile, eine Antwort. Kein Fenster in der Taskleiste, kein Konto,
keine Cloud von mir: Die Leiste schickt deine Frage direkt an die Groq-API und
schreibt die Antwort mit, während sie entsteht.

```
Alt+Space   →   Leiste erscheint   →   Frage tippen   →   Enter   →   Antwort
```

Standard-Hotkey ist **Alt+Space**, einstellbar über das Symbol im Infobereich
oder mit **Strg+,**.

---

## 1. Weitergeben an einen Freund

Es gibt genau **eine** Datei:

```
dist\installer\AIlightSetup.exe     (ca. 46 MB)
```

Der Ablauf beim Empfänger:

1. **Installieren** — Doppelklick. Keine Administratorrechte nötig, nichts
   runterladen: .NET und alles andere ist in der Datei enthalten. Die Installation
   landet im Benutzerprofil (`%LOCALAPPDATA%\AIlight`), Autostart und
   Startmenü-Einträge werden angelegt, ein Deinstaller kommt mit.
2. **Schlüssel eintragen** — beim ersten Start merkt AIlight, dass noch kein
   Schlüssel da ist, öffnet automatisch die Einstellungen und zeigt einen Hinweis
   im Infobereich. Einmal auf <https://console.groq.com/keys> einen
   kostenlosen Schlüssel erzeugen, einfügen, **„Schlüssel testen“**, fertig.
3. **Benutzen** — Alt+Space.

Der Schlüssel liegt danach im Windows-Anmeldeinformationsspeicher (nicht in einer
Datei, nicht im Log). Wer schon eine ältere AIlight-Fassung benutzt hat, dessen
Schlüssel wird beim Start automatisch übernommen.

**Deinstallieren:** Startmenü → *AIlight deinstallieren*, oder Windows →
Einstellungen → Apps. Läuft AIlight gerade, beendet der Deinstaller es selbst;
du musst vorher nichts schließen. Programm, Autostart und Verknüpfungen
verschwinden; Einstellungen, Verlauf und Schlüssel bleiben absichtlich erhalten
— eine Neuinstallation ist danach sofort wieder benutzbar.

Dasselbe gilt für ein Update: einfach `AIlightSetup.exe` erneut starten — eine
laufende Fassung wird dabei beendet, damit die Dateien ersetzt werden können.

---

## 2. Bedienung

| Taste | Wirkung |
| --- | --- |
| `Alt+Space` | Leiste zeigen/verstecken (einstellbar) |
| `Enter` | Frage abschicken · markierten Vorschlag ausführen · bei Abbruch fortsetzen |
| `Esc` | laufende Anfrage abbrechen, sonst Leiste schließen |
| `↑` `↓`, `Bild↑` `Bild↓` | Vorschläge durchgehen |
| `Tab` | markierten Vorschlag ins Eingabefeld übernehmen |
| `Strg+↑` / `Strg+↓` | frühere Fragen durchblättern |
| `Strg+C` | letzte Antwort kopieren (im Eingabefeld normal kopieren) |
| `Strg+N` | neues Gespräch (Gedächtnis leeren) |
| `Strg+H` | Verlauf anzeigen |
| `Strg+,` | Einstellungen |

Im leeren Eingabefeld stehen frühere Fragen und die wichtigsten Befehle.
Tippt man eine Frage, steht oben eine Zeile „Fragen: …“ — Enter schickt sie ab.

### Befehle (mit `/`, das alte `#` funktioniert weiter)

| Befehl | Wirkung |
| --- | --- |
| `/new` | neues Gespräch beginnen |
| `/clip` | Frage zur Zwischenablage stellen |
| `/history [Wort]` | Verlauf durchsuchen und öffnen |
| `/forget [Wort]` | Verlauf löschen (ohne Wort: alles) |
| `/stats` | Anzahl, Tokens, Kosten |
| `/model [auto\|20b\|120b]` | Modell wählen |
| `/prompt` | System-Prompt anzeigen |
| `/conversation` | zeigen, was mitgesendet wird |
| `/copy` | letzte Antwort in die Zwischenablage |
| `/save` | letzte Antwort als Datei speichern |
| `/settings` | Einstellungen öffnen |
| `/help` | Befehlsliste |
| `/quit` | AIlight beenden |

### Einstellungen

Links die Bereiche, rechts Karten — keine lange Liste, kein Suchen:

| Bereich | Was drin ist |
| --- | --- |
| **Konto** | API-Schlüssel speichern, prüfen, entfernen (mit „Schlüssel testen“) |
| **Modell** | Modell (`auto` wählt 20b oder 120b je nach Frage), Antwortlänge, Denk-Aufwand, Websuche, System-Prompt |
| **Gespräch** | Runden im Gedächtnis, Gedächtnis leeren, was mitgesendet wird |
| **Verhalten** | Hotkey (Tastenkombination einfach ins Feld drücken), Start mit Windows, Leiste vorab aufbauen, bei Fokusverlust ausblenden, Protokoll |
| **Aussehen** | Farben, Akzentfarbe, Breite, Abstand von oben, Schriftgröße, Deckkraft — mit **Vorschau der Leiste** |
| **Verlauf** | Aufbewahrung, Statistik (Anzahl, Tokens, Kosten), Ordner öffnen, löschen |
| **Über** | Version, Pfade, Ordner und Protokoll öffnen |

Alles wirkt sofort, es gibt kein „OK“. Die Aussehen-Vorschau zeigt in Echtzeit,
wie die Leiste mit den gewählten Farben, der Breite, der Schriftgröße und der
Deckkraft aussieht. Das Fenster hat keinen Systemrahmen: eigener Streifen zum
Ziehen, eigene Knöpfe, **Esc schließt**.

---

## 3. Wo liegen die Daten

| Was | Ort |
| --- | --- |
| Einstellungen | `%APPDATA%\AIlight\settings.json` |
| Verlauf (eine Zeile je Frage) | `%APPDATA%\AIlight\history.jsonl` |
| Gespräch, das mitgesendet wird | `%APPDATA%\AIlight\conversation.json` |
| letzte Antwort | `%APPDATA%\AIlight\last_answer.md` |
| gespeicherte Antworten | `%USERPROFILE%\Dokumente\AIlight-Antworten` |
| Log | `%LOCALAPPDATA%\AIlight\logs\ailight.log` (rolliert, 4 Dateien; liegt im Programmordner und verschwindet mit der Deinstallation) |
| API-Schlüssel | Windows-Anmeldeinformationsspeicher, Eintrag `AIlight:groq-api-key` |

---

## 4. Selbst bauen

Voraussetzungen:

* **.NET 8 SDK** — entweder im `PATH` oder portabel unter `%USERPROFILE%\dotnet-sdk`
* **Inno Setup 6** — <https://jrsoftware.org/isdl.php> (kostenlos)
* optional **Python mit Pillow**, nur wenn das Programmsymbol neu gezeichnet
  werden soll: `python tools\make_icon.py`

Dann ein Befehl:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build.ps1
```

Das Skript baut das Programm, lässt den Selbsttest laufen (**41 Prüfungen**,
ohne Netz), veröffentlicht die Release-Fassung nach `dist\publish` und packt
`dist\installer\AIlightSetup.exe`. Es bricht ab, sobald ein Schritt fehlschlägt.
Schalter: `-SkipTest` (ohne Selbsttest), `-SkipInstaller` (nur bis `dist\publish`).

### Prüfen statt raten

Dieselbe EXE, die der Nutzer startet, kann sich selbst prüfen und messen:

```powershell
AIlight.exe --selftest                 # 41 Prüfungen, Exit-Code 0 = grün
AIlight.exe --smoke 4                  # Leiste, Hotkey, Tray, Größe, Position
AIlight.exe --ask "Sag nur: läuft"     # echte Frage ohne Fenster
AIlight.exe --ask-ui "Was ist 2+4?"    # Frage wie von Hand in die Leiste getippt
AIlight.exe --shot dist\bilder         # Bildschirmfotos aller Fenster und Bereiche
AIlight.exe --show                     # Leiste zeigen und offen lassen
AIlight.exe --quit                     # laufende Instanz beenden
```

`--shot` schreibt PNG-Bilder der Leiste (leer und mit Antwort) und jedes
Einstellungsbereichs in Dunkel und Hell — praktisch, um die Gestaltung
anzusehen, ohne das Programm zu bedienen. Die Prüfungen dazu sind nicht nur
„läuft ohne Fehler“: der Selbsttest liest echte Pixel aus den gerenderten
Fenstern — der Regler muss links im Akzent gefüllt sein und nicht rechts, die
gewählte Vorschlagszeile muss ihren Akzentstreifen haben.

Mit `--report datei.txt` landen die Ergebnisse von `--smoke`, `--ask` und
`--ask-ui` in einer Datei. `--selftest` schreibt immer auf die Konsole und meldet
über den Exit-Code (0 = grün, 1 = rot); `--show` schreibt seinen Ablauf ins Log
(`%LOCALAPPDATA%\AIlight\logs\ailight.log`) und bleibt offen, bis du Esc drückst.

Für den Erststart-Test ohne echten Schlüssel gibt es die Umgebungsvariable
`AILIGHT_KEY_TARGET` — damit läuft der Test gegen einen zweiten, leeren
Eintrag im Anmeldeinformationsspeicher:

```powershell
$env:AILIGHT_KEY_TARGET = 'AIlight:test-ohne-key'; AIlight.exe --smoke 3
```

---

## 5. Aufbau (kurz)

```
src/AIlight/
  Program.cs            Einstieg: Prüfmodus, Einzelinstanz, sonst starten
  App.cs                Lebenszyklus, Hotkey, Tray, Fenster, Prüfmodi
  MainWindow.*          die Leiste: Eingabe, Vorschläge, gestreamte Antwort
  SettingsWindow.*      Einstellungen (alles sofort wirksam)
  AppSettings.cs        Einstellungen laden/speichern
  SelfTest.cs           der eingebaute Prüfstand
  Commands.cs           Befehlsliste
  Suggestions.cs        Zeilen unter dem Eingabefeld
  Core/                 Hotkey, Einzelinstanz, Schlüsselspeicher, Log, Icons, Pfade
  Groq/                 HTTP-Client, Streaming, Modelle, Verlauf
  Views/                Markdown-Darstellung, Thema (hell/dunkel), Bildschirmfotos
tools/                  build.ps1, Selbsttest-Helfer, Messwerkzeuge
packaging/              ailight.iss (Installer), ailight.ico
```

Bewusste Entscheidungen für „möglichst einfach und ressourcensparend“:

* **Kein Framework, kein Plugin-System, kein Hintergrunddienst.** Eine EXE, ein
  Tray-Symbol, ein Fenster — sonst nichts.
* **Kein zweiter Prozess.** Die Leiste entsteht im selben Programm
  (`PreloadBar` baut sie im Leerlauf vor, damit der erste Hotkey sofort wirkt).
* **Nur eine Netzverbindung**, direkt zur API; kein Zwischenserver.
* **Der Selbsttest ist Teil des Programms**, nicht ein separates Testprojekt —
  dadurch prüft er genau die EXE, die ausgeliefert wird.

### Gemessen (Release-Fassung, dieser Rechner)

| Vorgang | Wert |
| --- | --- |
| Leiste erscheint nach Hotkey | ~6–40 ms (vorgebaut) |
| Leiste erscheint beim Kaltstart | ~0,5–0,8 s |
| Erster Buchstabe der Antwort | ~0,4–0,6 s |
| Leerlauf | 0 % CPU, ~90 MB Arbeitsspeicher |
| Installer | 46 MB (enthält .NET) |
| Installiert auf der Platte | ~145 MB |

`.NET` ist mitgeliefert, deshalb ist der Installer nicht kleiner. Das ist der
Preis dafür, dass beim Empfänger **nichts** installiert werden muss.

---

## 6. Bekannte Grenzen (ehrlich)

* Nur Groq als Anbieter — genau eine API, keine Auswahl.
* Nur Windows 10/11, 64 Bit.
* Ist `Alt+Space` schon belegt (z. B. von PowerToys), meldet AIlight das im
  Log und im Einstellungsfenster; dann einen anderen Hotkey wählen.
* Getestet wurde auf Windows 10, mit dem Konto dieses Rechners — nicht auf
  einem frischen Windows-Konto ohne .NET-SDK und ohne Python.
* Der Installer ist unsigniert; Windows kann beim ersten Start „Weitere
  Informationen → Trotzdem ausführen“ verlangen (SmartScreen). Eine
  Code-Signatur wäre der nächste Schritt für eine breite Weitergabe.
