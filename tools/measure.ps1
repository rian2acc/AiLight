<#
    Messwerkzeug für die Zahlen, die AIlight verspricht — gemessen von außen,
    nicht mit Zahlen, die das Programm über sich selbst schreibt.

    Aufruf:
        powershell -NoProfile -ExecutionPolicy Bypass -File tools/measure.ps1 -Exe dist\publish\AIlight.exe
#>

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Runs = 3,
    [int]$IdleSeconds = 12
)

$ErrorActionPreference = "Stop"
$exe = (Resolve-Path $Exe).Path
Write-Host "Messung an: $exe"
Write-Host ""

function Stop-Ailight {
    Get-Process AIlight -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# Die Leiste ist ein Werkzeugfenster (kein Taskleisten-Eintrag). PowerShells
# MainWindowHandle übersieht solche Fenster deshalb — hier wird selbst gezählt.
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class WindowProbe
{
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public static int CountVisible(int processId, int minWidth)
    {
        var found = new List<IntPtr>();
        EnumWindows((hWnd, lParam) =>
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid != (uint)processId || !IsWindowVisible(hWnd)) return true;
            RECT rect;
            if (GetWindowRect(hWnd, out rect) && (rect.Right - rect.Left) >= minWidth) found.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return found.Count;
    }
}
"@ -ErrorAction SilentlyContinue

# ── 1. Start bis das Fenster sichtbar ist ───────────────────────────────────
Write-Host "Start bis Leiste sichtbar (Prozessstart bis Fensterhandle):"
$starts = @()
for ($i = 1; $i -le $Runs; $i++) {
    Stop-Ailight
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $exe -ArgumentList "--show" -PassThru
    $found = $false
    while ($sw.Elapsed.TotalSeconds -lt 30) {
        $proc.Refresh()
        if ($proc.HasExited) { break }
        if ([WindowProbe]::CountVisible($proc.Id, 200) -gt 0) { $found = $true; break }
        Start-Sleep -Milliseconds 5
    }

    $ms = [math]::Round($sw.Elapsed.TotalMilliseconds)
    $starts += $ms
    Write-Host ("  Lauf {0}: {1} ms (Fenster gefunden: {2})" -f $i, $ms, $found)
}

# ── 2. Leerlauf im Infobereich ──────────────────────────────────────────────
Stop-Ailight
Write-Host ""
Write-Host "Leerlauf im Infobereich (Ziel: 0 % CPU, wenig RAM):"
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 6   # Anlaufphase abwarten
$proc.Refresh()
$cpuBefore = $proc.TotalProcessorTime
$samples = @()
$clock = [System.Diagnostics.Stopwatch]::StartNew()
while ($clock.Elapsed.TotalSeconds -lt $IdleSeconds) {
    $proc.Refresh()
    $samples += $proc.WorkingSet64 / 1MB
    Start-Sleep -Milliseconds 500
}
$proc.Refresh()
$cpuAfter = $proc.TotalProcessorTime
$cpuSeconds = ($cpuAfter - $cpuBefore).TotalSeconds
$window = $clock.Elapsed.TotalSeconds
$ram = [math]::Round(($samples | Measure-Object -Average).Average, 1)
$cpuPercent = [math]::Round(($cpuSeconds / $window) * 100, 2)
Write-Host ("  RAM {0} MB | CPU {1} s über {2:N1} s = {3} %" -f $ram, [math]::Round($cpuSeconds, 4), $window, $cpuPercent)

# ── 3. Beenden ──────────────────────────────────────────────────────────────
$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $exe --quit | Out-Null
$left = Get-Process AIlight -ErrorAction SilentlyContinue
while ($left -and $sw.Elapsed.TotalSeconds -lt 20) {
    Start-Sleep -Milliseconds 50
    $left = Get-Process AIlight -ErrorAction SilentlyContinue
}
Write-Host ("  Beenden über --quit: {0} ms | Restprozesse: {1}" -f [math]::Round($sw.Elapsed.TotalMilliseconds), (@($left).Count))

Write-Host ""
$sorted = $starts | Sort-Object
$median = $sorted[[int][math]::Floor($sorted.Count / 2)]
Write-Host ("Start-Median: {0} ms (Läufe: {1})" -f $median, ($starts -join ", "))
