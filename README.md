# AIlight

<p align="center">
  <img src="docs/bilder/logo.png" alt="AIlight-Logo: ein Lichtstrahl, der aus einem Punkt aufgeht" width="120" height="120">
</p>

<p align="center">
  <strong>KI-Leiste für Windows — Hotkey drücken, Frage tippen, gestreamte Antwort bekommen.</strong>
</p>

<p align="center">
  <a href="https://github.com/rian2acc/AiLight/releases/latest"><img src="https://img.shields.io/github/v/release/rian2acc/AiLight?label=release&style=flat-square" alt="Neueste Fassung"></a>
  <a href="https://github.com/rian2acc/AiLight/releases/latest"><img src="https://img.shields.io/github/downloads/rian2acc/AiLight/total?style=flat-square" alt="Downloads"></a>
  <a href="https://github.com/rian2acc/AiLight/blob/main/LICENSE"><img src="https://img.shields.io/github/license/rian2acc/AiLight?style=flat-square" alt="Lizenz"></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 8"></a>
  <a href="https://github.com/rian2acc/AiLight/actions"><img src="https://img.shields.io/github/actions/workflow/status/rian2acc/AiLight/build.yml?branch=main&style=flat-square" alt="Build-Status"></a>
</p>

---

> **Das Logo** ist ein Lichtstrahl, der aus einem leuchtenden Punkt aufgeht — bewusst kein
> vierstrahliges „KI-Funkeln“. Dieselbe Marke sitzt als Vektorgrafik in der Leiste,
> im Einstellungsfenster und im Programmsymbol (`tools/make_icon.py` erzeugt es reproduzierbar).

## ✨ Funktionen

| Funktion | Beschreibung |
|----------|--------------|
| **Globaler Hotkey** | `Alt+Space` (frei wählbar) — sofort da, aus jedem Programm |
| **Streaming-Antworten** | Der Text erscheint Wort für Wort über die Groq-API |
| **Smarte Vorschläge** | Frühere Fragen, Befehle, Zwischenablage — per Tastatur bedienbar |
| **macOS-Look** | Ruhige Flächen, Haarlinien, großzügige Ecken, Ampel-Knöpfe in den Einstellungen |
| **Alles anpassbar** | Akzentfarbe (9 macOS-Töne), Ecken-Radius, Schrift, Breite/Höhe/Deckkraft, Bewegung, Fußzeile |
| **Gesprächsgedächtnis** | Einstellbare Runden, `/forget` löscht |
| **Verlauf & Statistik** | Tokens, Kostenschätzung, durchsuchbar, exportierbar |
| **Infobereich** | Nur eine Instanz, Autostart, kein Hintergrunddienst |
| **Selbsttest eingebaut** | `AIlight.exe --selftest` → 43 Prüfungen, teils auf Pixel-Ebene |
| **Installer** | 48 MB Inno-Setup-Bündel — inklusive .NET, ohne Administratorrechte, pro Benutzer |

## 📥 Installation

### Variante A: Installer (empfohlen)

1. **`AIlightSetup.exe`** aus der [neuesten Fassung](https://github.com/rian2acc/AiLight/releases/latest) laden (≈48 MB)
2. Ausführen — installiert nach `%LOCALAPPDATA%\AIlight`, legt Startmenü-Eintrag und Autostart an
3. Beim ersten Start öffnet sich das Einstellungsfenster → **Groq-API-Schlüssel** einfügen (kostenlos auf [console.groq.com/keys](https://console.groq.com/keys))
4. `Alt+Space` drücken und fragen

> Der Installer ist unsigniert — Windows SmartScreen fragt beim ersten Start nach
> („Weitere Informationen“ → „Trotzdem ausführen“).

### Variante B: Aus dem Quellcode bauen

```powershell
# Voraussetzungen: .NET 8 SDK, Inno Setup 6
git clone https://github.com/rian2acc/AiLight.git
cd AiLight
powershell -ExecutionPolicy Bypass -File tools\build.ps1
# Ergebnis: dist\installer\AIlightSetup.exe
```

> Das Skript läuft den 43-Punkte-Selbsttest, veröffentlicht eine

> self-contained EXE-Mappe (kein .NET nötig) und packt den Installer. Schalter: `-SkipTest`, `-SkipInstaller`.

## ⌨️ Kurzübersicht

| Taste | Wirkung |
|-------|---------|
| `Alt+Space` | Leiste zeigen/verstecken (frei wählbar) |
| `Enter` | Frage senden / markierten Vorschlag ausführen |
| `Esc` | Anfrage abbrechen, dann Leiste schließen |
| `↑` `↓` / `Bild↑` `Bild↓` | Vorschläge durchblättern |
| `Tab` | Eingabe mit dem markierten Vorschlag füllen |
| `Strg+↑` / `Strg+↓` | Frühere Fragen durchblättern |
| `Strg+C` | Letzte Antwort kopieren (im Eingabefeld: normales Kopieren) |
| `Strg+N` | Neues Gespräch (Gedächtnis leeren) |
| `Strg+H` | Verlauf öffnen |
| `Strg+,` | Einstellungen öffnen |

**Befehle** (im leeren Eingabefeld `/` tippen):

| Befehl | Wirkung |
|--------|---------|
| `/new` | Frisches Gespräch beginnen |
| `/clip` | Über den Inhalt der Zwischenablage fragen |
| `/history [Begriff]` | Verlauf durchsuchen und öffnen |
| `/forget [Begriff]` | Verlauf löschen (ohne Begriff: alles) |
| `/stats` | Anzahl, Tokens, geschätzte Kosten |
| `/model [auto\|20b\|120b]` | Modell wechseln |
| `/prompt` | System-Prompt ansehen |
| `/conversation` | Zeigen, was an die API geht |
| `/copy` | Letzte Antwort in die Zwischenablage |
| `/save` | Letzte Antwort als `.md` speichern |
| `/settings` | Einstellungen öffnen |
| `/help` | Alle Befehle auflisten |
| `/quit` | AIlight beenden |

## 🎛️ Mach es zu deinem

Das ganze Aussehen ist einstellbar — jede Änderung wirkt sofort, ohne Neustart:

| Einstellung | Möglichkeiten |
|-------------|----------------|
| Akzentfarbe | 9 macOS-Töne: Blau, Türkis, Grün, Gelb, Orange, Rot, Pink, Lila, Graphit |
| Ecken-Radius | 8–22 — Leiste, Karten, Felder und Zeilen skalieren gemeinsam |
| Schrift | System (Segoe UI Variable), Segoe UI, Calibri, Arial |
| Leiste | Breite 420–1200, Abstand von oben 0–40, Schriftgröße 12–28, Deckkraft 60–100 % |
| Bewegung | Einblenden und Impuls an/aus |
| Antwort | Denk-Schritte automatisch aufklappen, Fußzeile (Modell · Tokens · Kosten) ein/aus |
| Verhalten | Hotkey, Autostart, Vorbereiten im Hintergrund, Schließen bei Fokusverlust, Protokoll |

## 🖼️ Bilder

<p float="left">
  <img src="docs/bilder/leiste-vorschlaege.png" alt="Leiste mit Vorschlägen" width="45%">
  <img src="docs/bilder/leiste-antwort.png" alt="Gestreamte Antwort" width="45%">
</p>
<p float="left">
  <img src="docs/bilder/einstellungen-dunkel-konto.png" alt="Einstellungen – Konto (dunkel)" width="45%">
  <img src="docs/bilder/einstellungen-hell-aussehen.png" alt="Einstellungen – Aussehen (hell)" width="45%">
</p>

*Weitere Bilder in [`docs/bilder/`](docs/bilder) — erzeugt von `AIlight.exe --shot docs\bilder`.*

## 🗂️ Wo die Daten liegen

| Daten | Ort |
|-------|-----|
| Einstellungen | `%APPDATA%\AIlight\settings.json` |
| Verlauf (JSONL) | `%APPDATA%\AIlight\history.jsonl` |
| Gesprächsgedächtnis | `%APPDATA%\AIlight\conversation.json` |
| Letzte Antwort | `%APPDATA%\AIlight\last_answer.md` |
| Gespeicherte Antworten | `%USERPROFILE%\Documents\AIlight-Antworten` |
| Protokoll (rollierend, 4 Dateien) | `%LOCALAPPDATA%\AIlight\logs\ailight.log` |
| API-Schlüssel | **Windows-Anmeldeinformationsspeicher** → `AIlight:groq-api-key` |

> Deinstallieren lässt Einstellungen, Verlauf und Schlüssel stehen. Neu installieren = sofort weiterarbeiten.

## 🛠️ Eingebaute Diagnose

```powershell
AIlight.exe --selftest           # 43 Prüfungen, Rückgabewert 0 = bestanden
AIlight.exe --smoke 4            # Leiste, Hotkey, Infobereich, Größe, Position
AIlight.exe --ask "Sag: ok"      # Frage ohne Fenster
AIlight.exe --ask-ui "2+2?"      # Simulierte Bedienung der Leiste
AIlight.exe --shot docs\bilder   # Alle Fenster als PNG zeichnen
AIlight.exe --show               # Leiste zeigen und offen halten (Esc schließt)
AIlight.exe --quit               # Laufende Instanz beenden
AIlight.exe --report out.txt     # Messläufe in eine Datei schreiben
```
Der Selbsttest zeichnet echte Fenster und prüft **echte Pixel** — zum Beispiel muss der
gefüllte Teil eines Reglers links in der Akzentfarbe stehen und eine markierte
Vorschlagszeile ihren Akzentstreifen an der erwarteten Stelle zeigen.

## 🏗️ Aufbau

```
src/AIlight/
  Program.cs           Einstieg: Testmodi → Einzelinstanz → Start
  App.cs               Lebenszyklus, Hotkey, Infobereich, Fenster, Prüfmodi
  MainWindow.*         Die Leiste: Eingabe, Vorschläge, Streaming-Markdown
  SettingsWindow.*     Sieben Bereiche, Live-Vorschau, eigener Rahmen
  AppSettings.cs       Einstellungen laden/speichern (JSON)
  SelfTest.cs          43 Prüfungen, teils auf Pixel-Ebene
  Commands.cs          Register der Schrägstrich-Befehle
  Suggestions.cs       Vorschlagszeilen unter der Eingabe
  Core/                Hotkey, Einzelinstanz, Schlüsselspeicher, Protokoll, Symbole, Pfade, atomares Schreiben
  Groq/                HTTP-Client, Streaming, Modelle, Verlauf
  Views/               Markdown-Darstellung, Themen (hell/dunkel), Bildschirmfotos
tools/                 build.ps1, make_icon.py, Messwerkzeuge
packaging/             ailight.iss (Inno Setup), ailight.ico
docs/bilder/           Bilder für dieses README
```

**Designentscheidungen:**

- **Ein Programm**, self-contained — kein Hintergrunddienst, kein Plugin-System
- **Vorbereitete Leiste** entsteht im Leerlauf → erster Hotkey in 6–40 ms
- **Direkt zur Groq-API** — kein Zwischenserver, keine Telemetrie
- **Selbsttest steckt in der EXE** — geprüft wird genau das, was Nutzer starten
- **Schlüssel im Anmeldeinformationsspeicher** — berührt nie die Festplatte
- **macOS-inspiriertes Designsystem** — eine Themendatei steuert Farben, Radien und Schrift; jedes Fenster folgt live
- **Eigene Marke**: Lichtstrahl aus einem Punkt — wiedererkennbar, kein Standard-Funkeln

## 📊 Messwerte (Release, dieser Rechner)

| Messgröße | Wert |
|-----------|------|
| Leiste erscheint (vorbereitet) | 6–40 ms |
| Leiste erscheint (kalt) | 0,5–0,8 s |
| Erstes Antwort-Token | 0,4–0,6 s |
| Leerlauf-CPU | 0 % |
| Leerlauf-RAM | ca. 90 MB |
| Installergröße | 48 MB (inklusive .NET 8) |
| Installierte Größe | ca. 145 MB |

## ⚠️ Bekannte Grenzen

- **Nur Groq** — ein Anbieter, kein Modell-Marktplatz
- **Nur Windows 10/11 x64**
- Konflikte beim Hotkey werden erkannt (Protokoll + Hinweis in der Oberfläche) 
- Unsigierter Installer → SmartScreen-Hinweis beim ersten Start
- Getestet auf dem Windows-10-Konto des Autors — nicht auf frischen virtuellen Maschinen

## 🤝 Mitmachen

Fehlerberichte und Pull Requests sind willkommen. Besonders gebraucht:

- Codesignatur / Microsoft-Store-Verpackung
- Weitere KI-Anbieter (OpenAI, Anthropic, lokal über Ollama)
- Linux/macOS über Avalonia oder MAUI
- Weitere Einheitstests für Groq-Client und Verlaufsspeicher
- Übersetzungen (Oberfläche und Kommentare sind auf Deutsch)

```powershell
# Schnelle Entwicklungsrunde
powershell tools\build.ps1 -SkipInstaller
AIlight.exe --selftest
```

## 📄 Lizenz

[MIT](LICENSE) — mach damit, was du willst; behalte nur den Hinweis.

## 🙏 Danke

- [Groq](https://groq.com/) für die schnelle Inferenz-API
- [.NET](https://dotnet.microsoft.com/) für self-contained Publishing
- [Inno Setup](https://jrsoftware.org/isinfo.php) für den Installer
- [Pillow](https://python-pillow.org/) für die Logo-Erzeugung in `tools/make_icon.py`
