# Changelog

Alle nennenswerten Änderungen an AIlight. Format angelehnt an
[Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
Versionen nach [Semantic Versioning](https://semver.org/lang/de/).

## [2.2.0] — 2026-10-15

### Neu

- **Auftritt wie ein Spotlight**: Die Leiste taucht aus der Deckkraft auf,
  steigt einen Hauch auf und wächst minimal (180 ms, kubisch ausklingend) —
  vorher war es ein reines Einblenden. Der Abgang ist kürzer (130 ms) und
  ebenfalls weich; deshalb wirkt das Schließen nicht mehr hart. Alle Zeiten und
  Kurven stehen an einer Stelle (`Views/Motion.cs`).
- **Nichts springt mehr**: Die Vorschlagsliste gleitet zur gewählten Zeile,
  statt bei jedem Tastendruck zu springen (`Views/Glide.cs`) — und nur dann,
  wenn die Zeile wirklich nicht mehr im Bild ist. Die Antwort zieht beim
  Schreiben sanft ans Textende nach, statt in Stufen zu hüpfen.
- **Lichtschein von oben**: Ein schmaler Verlauf über der Eingabezeile gibt der
  Leiste Materialtiefe (Haarlinie oben, weicher Schattenhof darunter), ohne die
  Fläche milchig zu machen.

### Behoben

- **Die Leiste schwebt nicht mehr vor den Einstellungen**: Öffnen sich die
  Einstellungen (Befehl `/settings`, Tray oder `Strg+,`), zieht sich die Leiste
  zurück — und holt sich dabei **keinen fremden Fokus** zurück. Vorher blieb sie
  absichtlich stehen und nahm dem Einstellungsfenster sofort wieder die Tastatur.
- **Hotkey bei offenen Einstellungen**: `Alt+Space` holt jetzt das
  Einstellungsfenster nach vorn, statt die Leiste darüber zu legen.
- **Ruckeln beim Schreiben**: Der Antwortbereich wird nur noch neu gesetzt, wenn
  sich der Text wirklich geändert hat. Vorher wurden bis zu 14-mal je Sekunde
  dieselben Absätze neu aufgebaut — das flackerte und kostete Leistung.
- **Mittige Lage**: Die Leiste wurde beim ersten Öffnen nach der eingestellten
  Startbreite (680) gesetzt statt nach der wirklich eingestellten Breite — nach
  einer Breitenänderung saß sie nicht mehr mittig.
- **Schriftgröße wirkt sofort**: Eine geänderte Schriftgröße erreichte den
  bereits angezeigten Antworttext erst beim nächsten Zeichentakt; jetzt wird er
  im selben Augenblick neu gesetzt.
- **Meldungen ohne laufende Sitzung**: Die Messmodi (`--shot`, `--smoke`,
  `--ask`, `--ask-ui`) laufen jetzt auch dann, wenn im Infobereich schon eine
  Fassung wartet — vorher wurden sie wortlos dorthin abgeschoben.

### Sicherheit

- Selbsttest auf 48 Prüfungen erweitert: Bewegung (Zeiten, Kurve läuft von 0
  nach 1 und nie zurück), das Zurücktreten der Leiste samt Grundstellung danach,
  sofortiges Schließen ohne Bewegung und der Beweis, dass unveränderter
  Antworttext nicht neu gezeichnet wird.

## [2.1.0] — 2026-10-14

### Neu

- **Neue Marke**: Das Programmsymbol ist jetzt ein Lichtstrahl, der aus einem
  leuchtenden Punkt aufgeht — bewusst kein vierstrahliges „KI-Funkeln“, wie es
  viele Assistenten benutzen. `tools/make_icon.py` zeichnet es reproduzierbar in
  neun Größen (16 bis 256 px); als Vektorgrafik (`Icon.Beam`) sitzt dieselbe Form
  in der Leiste, in den Einstellungen und in den Zeilen der Vorschläge.
- **Einheitliche Sprache**: README auf Deutsch (wie Programm, Kommentare und
  Changelog), mit Bildern, Badges und dem neuen Logo.
- `docs/bilder/logo.png` kommt direkt aus der 256-px-Ebene des Symbols — README
  und Programmsymbol können nicht auseinanderlaufen.

### Behoben

- **Schein am Symbolrand**: Der Lichtschein des Symbols lief über die
  abgerundete Fläche hinaus und wurde an der Bildkante hart abgeschnitten (bei
  256 px ein flacher grauer Streifen über die volle Breite). Strahl, Hof und
  Kernlicht werden jetzt auf die Fläche beschnitten — im Generator und in der
  vom Programm gezeichneten Ersatzfassung.

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
