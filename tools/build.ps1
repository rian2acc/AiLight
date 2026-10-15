# AIlight — alles in einem Rutsch bauen.
#
#   powershell -ExecutionPolicy Bypass -File tools\build.ps1
#
# Ergebnis:  dist\installer\AIlightSetup.exe   (das ist die Datei zum Weitergeben)
#            dist\installer\AIlightPortable.zip (dieselbe Fassung ohne Installer)
#            dist\installer\SHA256SUMS.txt      (Prüfsummen beider Dateien)
#
# Ablauf: Programm bauen → Selbsttest laufen lassen → Release veröffentlichen
#         → signieren (wenn ein Zertifikat da ist) → Installer packen → portable
#         Fassung und Prüfsummen schreiben. Bricht ab, sobald ein Schritt fehlschlägt.
#
# ── Signieren ───────────────────────────────────────────────────────────────
# Ohne Signatur stufen manche Virenschutz-Programme eine frisch gebaute Setup-Datei
# als verdächtig ein; das ist eine Heuristik, kein Befund (Einzelheiten und die
# Meldewege stehen in docs/signieren.md). Mit -Sign wird mit signtool signiert:
#
#   $env:AILIGHT_PFX = 'C:\pfad\zertifikat.pfx'
#   $env:AILIGHT_PFX_PASSWORD = 'geheim'
#   powershell -File tools\build.ps1 -Sign
#
# Statt einer Datei geht auch ein Zertifikat aus dem Speicher:
#   $env:AILIGHT_SIGN_SUBJECT = 'Mein Name'      (oder AILIGHT_SIGN_THUMBPRINT)
#
# Ohne Zertifikat bricht -Sign mit einer klaren Meldung ab — lieber sichtbar
# scheitern als still unsigniert ausliefern.

param(
    [switch]$SkipTest,          # Selbsttest überspringen
    [switch]$SkipInstaller,     # nur bis dist\publish bauen
    [switch]$Sign,              # signieren (braucht ein Zertifikat, siehe oben)
    [switch]$Uncompressed       # Installer ohne Verdichtung (falls ein Scanner meckert)
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\AIlight\AIlight.csproj'
$publish = Join-Path $root 'dist\publish'
$installer = Join-Path $root 'dist\installer'

# Eigenes .NET-SDK im Benutzerordner (portabel installiert)? Dann dieses nehmen.
$portable = Join-Path $env:USERPROFILE 'dotnet-sdk'
if (Test-Path (Join-Path $portable 'dotnet.exe')) {
    $env:DOTNET_ROOT = $portable
    $env:PATH = "$portable;$env:PATH"
}

$net = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
if (-not $net) { throw 'dotnet wurde nicht gefunden. Entweder ins PATH legen oder nach %USERPROFILE%\dotnet-sdk installieren.' }

function Step([string]$text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

# ── Signieren ───────────────────────────────────────────────────────────────

function Find-SignTool {
    $found = @()
    $fromPath = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
    if ($fromPath) { $found += $fromPath }
    foreach ($kits in @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:ProgramFiles\Windows Kits\10\bin")) {
        if (Test-Path $kits) {
            $found += Get-ChildItem -Path (Join-Path $kits '*\x64\signtool.exe') -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending | Select-Object -ExpandProperty FullName
        }
    }
    return $found | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

function Get-SignCertificateArgs {
    if ($env:AILIGHT_PFX) {
        if (-not (Test-Path $env:AILIGHT_PFX)) { throw "Das Zertifikat wurde nicht gefunden: $env:AILIGHT_PFX" }
        return @('/f', $env:AILIGHT_PFX, '/p', "$env:AILIGHT_PFX_PASSWORD")
    }
    if ($env:AILIGHT_SIGN_THUMBPRINT) { return @('/sha1', $env:AILIGHT_SIGN_THUMBPRINT) }
    if ($env:AILIGHT_SIGN_SUBJECT) { return @('/n', $env:AILIGHT_SIGN_SUBJECT) }
    throw 'Kein Zertifikat hinterlegt: AILIGHT_PFX (+ AILIGHT_PFX_PASSWORD), AILIGHT_SIGN_THUMBPRINT oder AILIGHT_SIGN_SUBJECT setzen.'
}

$signTool = $null
if ($Sign) {
    $signTool = Find-SignTool
    if (-not $signTool) { throw 'signtool.exe wurde nicht gefunden (Windows SDK installieren).' }
    Get-SignCertificateArgs | Out-Null   # früh prüfen, ob überhaupt ein Zertifikat da ist
}

function Invoke-Sign([string]$file) {
    if (-not $Sign) { return }
    $cert = Get-SignCertificateArgs
    $arguments = @('sign', '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256') + $cert + @($file)
    & $signTool @arguments
    if ($LASTEXITCODE -ne 0) { throw "Signieren fehlgeschlagen: $file" }
    Write-Host ("     signiert: {0}" -f (Split-Path -Leaf $file))
}

Step '1/6  Programm bauen'
& $net build $project -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }

if (-not $SkipTest) {
    Step '2/6  Selbsttest'
    $debugExe = Join-Path $root 'src\AIlight\bin\Debug\net8.0-windows\AIlight.exe'
    $log = Join-Path $root 'dist\selftest.txt'
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist') | Out-Null

    # Ein Fensterprogramm schreibt nicht von selbst in eine Weiterleitung —
    # deshalb den Ausgabestrom ausdrücklich in eine Datei umleiten.
    $test = Start-Process -FilePath $debugExe -ArgumentList '--selftest' -Wait -PassThru `
        -RedirectStandardOutput $log
    Get-Content $log -Encoding UTF8 | Write-Host
    if ($test.ExitCode -ne 0) { throw 'Selbsttest nicht bestanden — siehe Ausgabe oben.' }
} else {
    Write-Host "`n=== 2/6  Selbsttest übersprungen ===" -ForegroundColor Yellow
}

Step '3/6  Release veröffentlichen'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& $net publish $project -c Release -r win-x64 --self-contained true -o $publish -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'Veröffentlichen fehlgeschlagen.' }
Write-Host ("     {0:N0} Dateien, {1:N1} MB" -f (Get-ChildItem $publish -Recurse -File).Count,
    ((Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))

# Vor dem Packen signieren: so liegt die signierte Programmdatei auch im Installer.
Invoke-Sign (Join-Path $publish 'AIlight.exe')

if ($SkipInstaller) { return }

Step '4/6  Installer packen'
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw 'Inno Setup 6 wurde nicht gefunden. Von https://jrsoftware.org/isdl.php installieren (kostenlos) oder -SkipInstaller benutzen.'
}

New-Item -ItemType Directory -Force -Path $installer | Out-Null
$iss = Join-Path $root 'packaging\ailight.iss'
$isccArgs = @()
if ($Uncompressed) { $isccArgs += '/DNoCompression' }

# Mit Signatur bekommt Inno Setup den Signaturbefehl mit — dann ist auch der
# Deinstaller signiert (die Datei, die Scanner am liebsten anmeckern).
if ($Sign) {
    $cert = Get-SignCertificateArgs
    $quotedTool = if ($signTool -match '\s') { '$q' + $signTool + '$q' } else { $signTool }
    $command = $quotedTool + ' sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 ' +
        ($cert -join ' ') + ' $f'
    $isccArgs += '/DSIGN'
    $isccArgs += "/Ssigntool=$command"
}

$isccArgs += $iss
$isccOut = & $iscc @isccArgs 2>&1
if ($LASTEXITCODE -ne 0) {
    $isccOut | Write-Host
    throw 'Installer konnte nicht gepackt werden.'
}

$setup = Join-Path $installer 'AIlightSetup.exe'
Invoke-Sign $setup

Step '5/6  Portable Fassung (ohne Installer)'
$zip = Join-Path $installer 'AIlightPortable.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("     {0}  ({1:N1} MB)" -f (Split-Path -Leaf $zip), ((Get-Item $zip).Length / 1MB))

Step '6/6  Prüfsummen'
$lines = @()
foreach ($file in @($setup, $zip)) {
    $hash = (Get-FileHash -Path $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines += "$hash  $(Split-Path -Leaf $file)"
    Write-Host ('     {0}…  {1}' -f $hash.Substring(0, 16), (Split-Path -Leaf $file))
}

$sums = Join-Path $installer 'SHA256SUMS.txt'
[IO.File]::WriteAllText($sums, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))

Write-Host "`nFertig:" -ForegroundColor Green
Write-Host ("  {0}  ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
Write-Host ("  {0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
Write-Host ("  {0}" -f $sums)
