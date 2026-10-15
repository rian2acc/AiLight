# Changelog

Alle nennenswerten Änderungen an AIlight. Format angelehnt an
[Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
Versionen nach [Semantic Versioning](https://semver.org/lang/de/).

## [2.4.0] — 2026-10-15

### Neu

- **Einführung beim ersten Start.** Sieben Schritte erklären alles, was man zum
  Benutzen braucht: Willkommen, Schlüssel, Hotkey, Fragen, Befehle und Tasten,
  Aussehen, Fertig. Auf der Fragen-Seite tippt sich eine Beispiel-Frage selbst
  und wird beantwortet, damit man den Ablauf sieht, bevor man ihn benutzt.
  Geblättert wird mit „Weiter“, `←` `→`, `Enter` oder einem Klick auf einen
  Schritt; im letzten Schritt lassen sich Autostart und Vorbereitung einstellen,
  „Schlüssel erstellen“ führt direkt zu console.groq.com. Die Einführung läuft
  **einmal** und ist danach jederzeit wieder da: über `/welcome`, im Menü des
  Infobereich-Symbols und in den Einstellungen unter „Verhalten“.
- **Nach einer Frage bleibt nur das Gespräch.** Wird eine Frage abgeschickt,
  klappen die Vorschläge weg — mit Höhe, Deckkraft und einem kurzen
  Zurückweichen, nicht mit einem Schlag. Beim Tippen kommen sie sofort zurück,
  und die Pfeiltasten holen sie ebenfalls hervor. Die Regel dahinter ist einfach:
  gefragt wird, dann gehört die Fläche der Antwort.
- **Die Vorschläge ziehen als Liste herein.** Jede Zeile kommt einen Wimpernschlag
  nach der vorigen (26 ms Abstand), Absätze einer neuen Antwort ebenso — statt
  dass alles auf einmal dasteht.
- **Drei Punkte statt eines Blinkers.** Solange geantwortet wird, leuchten drei
  Punkte der Reihe nach auf; ein einzelner Punkt konnte „ich denke“ nicht erzählen.
- **Die Marke gibt Rückmeldung.** Beim Abschicken einer Frage wird sie kurz
  einen Hauch größer; unter der Eingabezeile wächst beim Hineinklicken eine
  dünne Akzentlinie aus der Mitte.

### Geändert

- **Eine Kurve für alles.** `Views/Curve.cs` rechnet kubische Bézier-Kurven wie
  eine Website — mit Newton-Verfahren, nicht mit „ungefähr“. Die Hauskurve ist
  die steile Auslaufkurve von macOS: `cubic-bezier(0.32, 0.72, 0, 1)`. Alle 30
  Übergänge in `Views/Theme.xaml` haben jetzt eine solche Kurve und Dauern aus
  dem Haus (Drücken 90 ms, Details 140 ms, Zurücknehmen 220 ms); vorher liefen
  sie **linear** — genau das fühlte sich hart an.
- **Bewegung liegt an einer Stelle** (`Views/Motion.cs`) — Zeiten, Maße und
  Kurven — und gehorcht zwei Schaltern: dem in AIlight und dem von Windows
  („Animationen im Clientbereich“). Ist einer aus, ist alles sofort da.
- **Das Scrollen hängt am Zeichentakt** (`Views/Glide.cs`) statt an einem
  16-ms-Zeitgeber: es läuft bildsynchron, und das Nachziehen ans Textende hängt
  von der Bilddauer ab, damit es auf 60- und 144-Hz-Bildschirmen gleich aussieht.
- **Der Auftritt der Leiste** ist um ein Drittel ruhiger und nutzt dieselbe Kurve;
  beim Erscheinen zieht der Lichtschein mit ein.
- Der Antwortbereich blendet nicht mehr als Ganzes ein, sondern Absatz für Absatz.

### Behoben

- **Animationen liefen auch ohne sichtbares Fenster.** Im Prüfstand und für die
  Bilder wurde die Oberfläche aufgebaut, ohne dass ein Fenster auf dem Bildschirm
  stand — die Bewegung lief trotzdem los, und weil kein Bild vergeht, blieben
  Vorschlagszeilen halb durchsichtig (im Bildschirmfoto ganz unsichtbar). Jetzt
  wird nur dort bewegt, wo es wirklich etwas zu sehen gibt, und sonst der
  Endzustand gesetzt. Aufgefallen ist es dem Selbsttest, nicht dem Auge.
- **Keine klebrigen Zustände mehr.** WPF-Animationen bleiben auf ihrem Endwert
  stehen und überschreiben danach jede Änderung derselben Eigenschaft — daher
  kamen halb durchsichtige Bereiche und feste Höhen, die nicht mehr wachsen
  wollten. `Views/Anim.cs` räumt jede Bewegung nach ihrem Ende ab und setzt den
  Grundwert auf den gewünschten Endzustand.
- **Zuklappen hinterlässt keine feste Höhe**, der Bereich misst sich danach
  wieder selbst.
- Der Verlauf (`/history`) öffnet die Liste zuverlässig, auch wenn gerade eine
  Antwort dasteht.

### Installer und Verteilung

- **Portable Fassung und Prüfsummen bei jedem Release.** Neben dem Installer
  liegt `AIlightPortable.zip` (dieselbe Version, entpacken und starten) und
  `SHA256SUMS.txt` mit beiden Prüfsummen — beides wird auch in der CI erzeugt und
  angehängt.
- **Signieren ist vorbereitet.** `tools\build.ps1 -Sign` signiert die
  Programmdatei vor dem Packen, übergibt Inno Setup den Signaturbefehl (damit
  auch der Deinstaller signiert wird) und signiert zum Schluss den fertigen
  Installer. In der CI passiert dasselbe, sobald `SIGNING_PFX_BASE64` und
  `SIGNING_PFX_PASSWORD` als Geheimnisse hinterlegt sind.
- **Verdichtung von `lzma2/ultra64` auf `lzma2/max`**: der Installer wird dadurch
  größer (gemessen 49,4 statt 48,5 MB, +1,5 %), entpackt aber schneller und
  wirkt weniger wie ein gepacktes Bündel. Ausdrücklich **kein belegter Schutz
  gegen Fehlalarme** — die einzige echte Abhilfe ist eine Signatur.
- **Vollständige Dateiangaben** in Programm und Installer (Hersteller, Copyright,
  Produkt und Version): Windows, Dateieigenschaften und Scanner lesen sie.
- **Anleitung für den Fehlalarm** in [docs/signieren.md](docs/signieren.md):
  warum es passiert, wie man signiert, wo man eine Falschmeldung einreicht
  (Microsoft und McAfee) und warum man den Virenschutz nicht abschalten sollte.

### Sicherheit

- Selbsttest von 52 auf **59 Prüfungen** erweitert. Neu sind:
  - die Hauskurve (Stützpunkte, steiler Verlauf, kein Überschwingen, Endpunkte),
  - **alle Übergänge des Aufbaus**: jeder muss eine Kurve haben und eine Dauer
    aus dem Haus — eine Prüfung, die bis in die Gestaltungsvorlagen hineinsieht,
  - der Windows-Schalter für Bewegung,
  - Scrollen ohne Bewegung (springt sofort, statt hängen zu bleiben),
  - das Verhalten der Vorschläge nach einer Frage (weg, beim Tippen zurück),
  - die Einführung: sieben Schritte, Blättern vor und zurück, Beschriftung,
    laufende Vorführung, vollständige Befehlsliste, sichtbare Marke am Pixel,
    der „Weiter“-Knopf innerhalb des Fensters und dass „gesehen“ gespeichert wird.
- **Wer AIlight schon benutzt, sieht die Einführung einmal.** Der Vermerk „schon
  gesehen“ ist neu und fehlt in bestehenden Einstellungen — er wird also beim
  ersten Start nach diesem Update auf `false` stehen. Wer sie nicht noch einmal
  ansehen will: einmal mit `Esc` schließen, danach kommt sie nicht wieder.

## [2.3.0] — 2026-10-15

### Neu

- **Neue Marke: „AI“ als ein einziges Bild.** Ein leuchtender Punkt oben, daraus
  gehen drei Strahlen nach unten. Die beiden äußeren ergeben mit dem Querstrich
  dazwischen ein **A**, der mittlere Strahl ist das **I**. Eine einzige Maßtabelle
  versorgt alles: `tools/make_icon.py` zeichnet das Programmsymbol (neun Größen,
  16–256 px), die Vektorgrafik in den Fenstern, die Notfallzeichnung im Programm
  (`Core/Icons.cs`) und das Logo in diesem README. Deshalb sieht die Marke in der
  Taskleiste, im Infobereich und in der Oberfläche gleich aus — kleine Größen
  bekommen nur kräftigere Striche, sonst verschwinden die Zwischenräume.
- **Die Marke sitzt überall im Programm**: im Kopf der Leiste in der Akzentfarbe,
  im Einstellungsfenster über der Navigation und noch einmal in der Vorschau des
  Bereichs „Aussehen“, damit man dort sofort sieht, was man einstellt.
- **Überall weiche Übergänge** statt harter Umschalter:
  - Beim Wechsel einer Einstellungs-Seite blendet die neue Seite auf und steigt
    8 px auf (220 ms) — vorher sprang sie ins Bild.
  - Das Einstellungsfenster selbst blendet beim Öffnen auf.
  - Knöpfe, Ampel-Knöpfe, Navigationszeilen, Schalter, Regler und Farbfelder
    blenden Hover und Drücken ein, statt Farbe umzuschalten.
  - Vorschlagszeilen bekommen eine eigene Beleuchtungsebene, die unter dem
    Mauszeiger weich aufgeht und ebenso weich wieder verschwindet.

### Geändert

- Alle Zeiten und Kurven der Bewegung stehen weiter an einer Stelle
  (`Views/Motion.cs`); neu dazugekommen sind `PageMs` (220 ms) und `PageRise`
  (8 px) für die Seitenwechsel.
- Wer in den Einstellungen die Bewegung abschaltet, bekommt weiterhin alles
  sofort und ohne Überblendung — auch die Seitenwechsel.
- Selbsttest von 48 auf **52 Prüfungen** erweitert. Neu sind zwei Marken-Prüfungen
  auf Pixel-Ebene (Lichtpunkt oben, Mittelstrahl, Querstrich, beide Schenkel des A)
  und eine Prüfung der Überblendungen: mitten im Wechsel muss die Deckkraft
  zwischen 0 und 1 liegen, am Ende genau 1 oder 0 — kein halb sichtbarer Rest.

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
