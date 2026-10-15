<#
    Den Selbsttest auswerten — vollständig, nicht nur den Rückgabewert.

    Warum zwei Bedingungen statt einer: ein Lauf, der mitten in den Prüfungen
    abbricht, kann ohne Ergebniszeile enden und trotzdem als Erfolg durchgehen.
    Genau das ist passiert — der CI-Schritt meldete „success“, während im
    Protokoll zwei Prüfungen rot standen und die Bilanz ganz fehlte. Eine grüne
    Anzeige, die nichts bedeutet, ist schlimmer als gar keine. Wer grün meldet,
    muss deshalb auch die vollständige Bilanz vorweisen können.

    Aufruf mit einer EXE:
        .\tools\verify-selftest.ps1 -Exe 'dist\publish\AIlight.exe'

    Aufruf mit schon geschriebener Ausgabe (z. B. aus build.ps1, das den
    Ausgabestrom eines Fensterprogramms in eine Datei umleitet):
        .\tools\verify-selftest.ps1 -Log 'dist\selftest.txt' -ExitCode $test.ExitCode

    Rückgabe: 0 = alles grün, sonst 1 (oder der Rückgabewert der EXE).
#>
param(
    [string]$Exe,
    [string]$Log,
    [int]$ExitCode = 0
)

$ErrorActionPreference = 'Stop'

# Jeder Weg endet mit einem Rückgabewert: 0 heißt grün, alles andere rot. Ein
# Fehler, der ungefangen nach oben liefe, wäre für den CI-Schritt nicht von einem
# Erfolg zu unterscheiden — und genau das darf nicht passieren.
trap {
    Write-Host "Selbsttest: unerwarteter Fehler — $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$lines = @()
$code = $ExitCode

if (-not [string]::IsNullOrWhiteSpace($Exe)) {
    if (-not (Test-Path -LiteralPath $Exe)) {
        throw "Selbsttest-EXE nicht gefunden: $Exe"
    }

    # Voller Pfad: so hängt das Ergebnis nicht davon ab, aus welchem Verzeichnis
    # der Aufruf kommt.
    $exePath = (Resolve-Path -LiteralPath $Exe).ProviderPath
    $exeDir = Split-Path -Parent $exePath

    # Ein Fensterprogramm hängt sich an die Konsole des Aufrufers an
    # (AttachConsole) und schreibt dann dorthin: bei einem direkten Aufruf
    # („& $Exe“) geht die Ausgabe verloren, und ein leerer Mitlesevorgang sieht
    # anschließend aus wie ein bestandener Lauf. Genau das hat den CI-Schritt
    # grün gemeldet, obwohl nichts geprüft worden war. Deshalb der Weg über eine
    # Datei — denselben nimmt build.ps1.
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('ailight-selftest-' + [Guid]::NewGuid().ToString('N') + '.txt')
    try {
        $run = Start-Process -FilePath $exePath -WorkingDirectory $exeDir -ArgumentList '--selftest' `
            -Wait -PassThru -RedirectStandardOutput $tmp
        $code = $run.ExitCode
        if (Test-Path -LiteralPath $tmp) {
            $lines = @(Get-Content -LiteralPath $tmp -Encoding UTF8)
        }
    }
    finally {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    }

    $lines | ForEach-Object { Write-Host $_ }
}
elseif (-not [string]::IsNullOrWhiteSpace($Log)) {
    if (-not (Test-Path -LiteralPath $Log)) {
        throw "Selbsttest-Ausgabe nicht gefunden: $Log"
    }

    $lines = @(Get-Content -LiteralPath $Log -Encoding UTF8)
}
else {
    throw 'Bitte -Exe oder -Log angeben.'
}

# Kein Rückgabewert ist kein gutes Zeichen: Windows PowerShell 5.1 wartet
# bei einem Fensterprogramm nicht und liefert dann weder Ausgabe noch Code.
# In diesem Fall wird bewusst rot gemeldet — lieber sichtbar scheitern als
# still durchwinken. (build.ps1 nimmt deshalb den Weg über -Log.)
if ($null -eq $code) {
    Write-Host 'Selbsttest: kein Rückgabewert vom Programm — es ist nicht durchgelaufen.' -ForegroundColor Red
    exit 1
}

if ($code -ne 0) {
    Write-Host "Selbsttest: Rückgabewert $code — siehe Ausgabe oben." -ForegroundColor Red
    exit $code
}

# Umlaute werden hier bewusst nicht geprüft: die Konsole des Läufers schreibt
# sie nicht zuverlässig durch (aus „Prüfungen“ wird dort „Pr?fungen“).
$summary = $lines | Select-String -Pattern 'Ergebnis: (\d+) von (\d+)' | Select-Object -Last 1
if (-not $summary) {
    Write-Host 'Selbsttest: keine Ergebniszeile — der Lauf ist nicht durchgelaufen.' -ForegroundColor Red
    exit 1
}

$groups = $summary.Matches[0].Groups
$line = $summary.Line.Trim()
if ($groups[1].Value -ne $groups[2].Value) {
    Write-Host "Selbsttest: $line" -ForegroundColor Red
    exit 1
}

Write-Host "Selbsttest: $line" -ForegroundColor Green
exit 0
