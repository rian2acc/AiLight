# AIlight — alles in einem Rutsch bauen.
#
#   powershell -ExecutionPolicy Bypass -File tools\build.ps1
#
# Ergebnis:  dist\installer\AIlightSetup.exe   (das ist die Datei zum Weitergeben)
#
# Ablauf: Programm bauen → Selbsttest laufen lassen → Release veröffentlichen
#         → Installer packen. Bricht ab, sobald ein Schritt fehlschlägt.

param(
    [switch]$SkipTest,          # Selbsttest überspringen
    [switch]$SkipInstaller      # nur bis dist\publish bauen
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

Step '1/4  Programm bauen'
& $net build $project -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }

if (-not $SkipTest) {
    Step '2/4  Selbsttest'
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
    Write-Host "`n=== 2/4  Selbsttest übersprungen ===" -ForegroundColor Yellow
}

Step '3/4  Release veröffentlichen'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& $net publish $project -c Release -r win-x64 --self-contained true -o $publish -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'Veröffentlichen fehlgeschlagen.' }
Write-Host ("     {0:N0} Dateien, {1:N1} MB" -f (Get-ChildItem $publish -Recurse -File).Count,
    ((Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))

if ($SkipInstaller) { return }

Step '4/4  Installer packen'
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw 'Inno Setup 6 wurde nicht gefunden. Von https://jrsoftware.org/isdl.php installieren (kostenlos) oder -SkipInstaller benutzen.'
}

New-Item -ItemType Directory -Force -Path $installer | Out-Null
$isccOut = & $iscc (Join-Path $root 'packaging\ailight.iss') 2>&1
if ($LASTEXITCODE -ne 0) {
    $isccOut | Write-Host
    throw 'Installer konnte nicht gepackt werden.'
}

$setup = Join-Path $installer 'AIlightSetup.exe'
Write-Host ("`nFertig: {0}  ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB)) -ForegroundColor Green
