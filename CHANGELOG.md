# Changelog

Alle nennenswerten Änderungen an AIlight. Format angelehnt an
[Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
Versionen nach [Semantic Versioning](https://semver.org/lang/de/).

## [2.0.0] — 2026-10-14

### Neu

- **macOS-inspiriertes Design**: ruhige Flächen, Haarlinien statt kräftiger
  Rahmen, großzügige Eckenradien, Ampelfarben links oben in den Einstellungen,
  neun macOS-Akzentfarben (Blau, Türkis, Grün, Gelb, Orange, Rot, Pink, Lila,
  Graphit).
- **Mehr Anpassung, alles live**:
  - Ecken-Radius von 8 bis 22 — Fenster, Karten, Felder und Zeilen skalieren gemeinsam.
  - Schrift wählbar: System (Segoe UI Variable), Segoe UI, Calibri, Arial.
  - Bewegung abschaltbar: Einblenden der Leiste und Pulsieren des Impulses.
  - Denk-Schritte automatisch aufklappen (Standard: eingeklappt).
  - Fußzeile mit Modell, Tokens und Kosten ausblendbar.
- **Konto-Seite**: „Schlüssel erstellen“-Knopf direkt zu console.groq.com/keys.
- **Über-Seite**: Knopf zum Projekt auf GitHub.

### Behoben

- **Doppelte Antwortfragmente**: brach die Verbindung mitten im Stream ab, wurde
  bislang blind neu gestartet und der bereits angezeigte Anfang stand doppelt
  im Bild. Jetzt bleibt der Text stehen und Enter fragt erneut.
- **Verlauf, Gespräch und Einstellungen werden atomar geschrieben** (Temporärdatei
  + Umbenennen) — ein Absturz kann keine Datei mehr halbfertig zurücklassen.
- **Thread-Sicherheit**: alle Lesezugriffe auf die gestreamte Antwort (Strg+C,
  Kopieren, Speichern, neues Gespräch, Verlaufsansicht) laufen unter demselben
  Lock wie der Zeichentakt.

### Sicherheit

- Selbsttest startet neue Prüfungen für Radius-/Schrift-Grenzen und die neuen
  Schalter; alle 43 Prüfungen laufen auf dem ausgelieferten EXE.
- `.gitignore` schützt Nutzerdaten zusätzlich, falls sie einmal im
  Entwicklungsordner angelegt werden.
- README-Screenshots wieder sichtbar (liegen jetzt unter `docs/bilder/`).

## [1.0.0] — 2026-10-13

### Neu

- Erste Fassung: globale Hotkey-Leiste (`Alt+Space`), Streaming-Antworten über
  die Groq-API, Vorschläge, Verlauf, Statistik, Systemtray, Autostart,
  Einstellungsfenster mit sieben Bereichen.
- Selbsttest eingebaut (41 Prüfungen, teils auf Pixel-Ebene).
- Installer (Inno Setup, ohne Administratorrechte) und CI auf Windows.
