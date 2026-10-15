# Wenn Windows oder der Virenschutz meckert

Die ehrliche Zusammenfassung zuerst: **Es gibt keine Einstellung, die einen
Fehlalarm zuverlässig verhindert.** Was hilft, ist eine Signatur — und bis dahin
ein Weg, auf dem man das Programm trotzdem benutzen kann.

## Warum das passiert

- **Unsigniert heißt namenlos.** Ein Installer ohne Signatur hat für Windows
  keinen überprüfbaren Herausgeber. SmartScreen zeigt dann „unbekannter
  Herausgeber“, und jede frische Fassung fängt bei der Reputation wieder bei null
  an. ([SmartScreen-Reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation))
- **Der Virenschutz rät, nicht nur vergleicht.** Moderne Scanner haben
  Machine-Learning-Modelle und Heuristiken, die unbekannte Dateien *bewerten*.
  Ein Installer tut von Natur aus Dinge, die auch Schadsoftware tut: Er entpackt
  Dateien, schreibt in den Benutzerordner, legt einen Autostart-Eintrag an und
  erzeugt einen Deinstaller. Dazu kommt, dass Inno Setup auch in echten
  Angriffskampagnen benutzt wird — deshalb schauen die Scanner dort genauer hin.
  ([Defender-Erkennungstechniken](https://learn.microsoft.com/en-us/defender-endpoint/adv-tech-of-mdav))
- **Die Verdichtung ist kein Hebel.** `lzma2/ultra64`, `lzma2/max`,
  `SolidCompression` — zu keinem davon gibt es belegte Aussagen, dass es
  Erkennungen auslöst oder verhindert. AIlight benutzt jetzt `lzma2/max` (die
  Stufe, die Inno Setup selbst vorschlägt): schneller beim Entpacken und weniger
  auffällig, aber ausdrücklich **kein Fix**.

## Was wirklich hilft: signieren

1. **Zertifikat besorgen.** Für Einzelpersonen:
   - Microsoft **Artifact Signing** (früher Trusted Signing): verwalteter Dienst,
     Einzelpersonen in den USA/Kanada, kostenpflichtiges Azure-Abo, laut Microsoft
     derzeit ab 9,99 $/Monat für bis zu 5.000 Signaturen.
     ([Übersicht](https://azure.microsoft.com/en-us/products/artifact-signing),
     [Häufige Fragen](https://learn.microsoft.com/en-us/azure/artifact-signing/faq))
   - Oder ein **IV-Zertifikat** einer Zertifizierungsstelle, z. B. SSL.com ab
     129 $/Jahr für einzelne Entwickler.
     ([SSL.com IV Code Signing](https://www.ssl.com/products/software-integrity/code-signing/iv/))
   - **EV** braucht es dafür nicht: Ein EV-Zertifikat ist teurer, springt aber
     nicht mehr an der SmartScreen-Warnung vorbei. Öffentlich vertrauenswürdige
     Signaturschlüssel müssen seit 2023 hardwaregestützt liegen
     ([CA/B-Forum](https://cabforum.org/working-groups/code-signing/requirements/)).
2. **Bauen und signieren.** Ein Aufruf genügt:

   ```powershell
   $env:AILIGHT_PFX = 'C:\pfad\zertifikat.pfx'
   $env:AILIGHT_PFX_PASSWORD = 'geheim'
   powershell -ExecutionPolicy Bypass -File tools\build.ps1 -Sign
   ```

   Das signiert `AIlight.exe` **vor** dem Packen (also liegt die signierte
   Programmdatei auch im Installer), gibt Inno Setup den Signaturbefehl mit
   (`/DSIGN`, damit auch der Deinstaller signiert wird) und signiert danach den
   fertigen `AIlightSetup.exe`. Statt einer PFX-Datei geht auch ein Zertifikat
   aus dem Speicher: `$env:AILIGHT_SIGN_SUBJECT = 'Mein Name'` oder
   `$env:AILIGHT_SIGN_THUMBPRINT = '…'`.

   In der CI passiert dasselbe automatisch, sobald die Geheimnisse
   `SIGNING_PFX_BASE64` und `SIGNING_PFX_PASSWORD` im Repository hinterlegt sind.
   Ohne Geheimnis bleibt der Installer unsigniert — dann steht es auch so in den
   Notizen zur Fassung.

   *Ehrliche Einschränkung:* Der signierte Weg ist hier nie gelaufen, weil kein
   Zertifikat vorliegt. Unsigniert ist die ganze Kette geprüft; das Signieren
   ist der dokumentierte, aber unerprobte Teil.

## Was man melden kann (und sollte)

Ein Fehlalarm bleibt kein Schicksal — er wird analysiert und in der Regel
innerhalb von Tagen korrigiert:

- **Microsoft Defender:** <https://www.microsoft.com/en-us/wdsi/filesubmission>
  → anmelden, *„Incorrectly detected as malware/malicious“*, die **genau**
  beanstandete Datei hochladen, Erkennungsnamen (z. B.
  `Trojan:Win32/Wacatac.B!ml`), SHA-256, Version und die Release-Adresse
  angeben. Mittlere Priorität wird laut Portal „innerhalb weniger Tage“
  bearbeitet; E-Mail-Einreichungen gibt es nicht.
  ([Einreichungsanleitung](https://learn.microsoft.com/en-us/defender-xdr/submission-guide))
- **McAfee** (auf diesem Rechner der aktive Scanner) hat ebenfalls ein Formular
  für Fehlalarme im Threat-Center.
- Hilfreich ist immer: die Prüfsumme aus `SHA256SUMS.txt`, die Quelladresse
  (dieses Repository, Tag der Fassung) und der Hinweis, womit die Datei gebaut
  wurde.

**Was wir nicht tun:** Nutzer auffordern, ihren Virenschutz abzuschalten oder
Ausnahmen einzurichten. Das ist keine Lösung, sondern ein Sicherheitsloch.

## Der Weg daran vorbei: die Portable-Fassung

Jede Fassung liegt auch als `AIlightPortable.zip` bereit — dasselbe Programm,
nur ohne Installer: entpacken, `AIlight.exe` starten, fertig. Damit gibt es
nichts, was wie ein Setup aussieht, und nichts, was sich einnistet. Autostart
und Einstellungen setzt das Programm selbst, wenn man es möchte.

Prüfen lässt sich beides vor dem Start:

```powershell
Get-FileHash .\AIlightSetup.exe -Algorithm SHA256     # vergleichen mit SHA256SUMS.txt
```

## Quellen

- <https://learn.microsoft.com/en-us/defender-endpoint/adv-tech-of-mdav>
- <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation>
- <https://learn.microsoft.com/en-us/defender-xdr/submission-guide>
- <https://learn.microsoft.com/en-us/azure/artifact-signing/faq>
- <https://cabforum.org/working-groups/code-signing/requirements/>
- <https://jrsoftware.org/ishelp/topic_setup_compression.htm>
- <https://jrsoftware.org/ishelp/topic_setup_signtool.htm>
