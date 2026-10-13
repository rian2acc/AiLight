<#
    Misst, was der Nutzer wirklich spürt: die Zeit vom Drücken des Hotkeys
    (echte Tastatureingabe über SendInput) bis die Leiste sichtbar ist.

    Aufruf:
        powershell -NoProfile -ExecutionPolicy Bypass -File tools/hotkey_probe.ps1 -Exe dist\publish\AIlight.exe
#>

param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Runs = 4
)

$ErrorActionPreference = "Stop"
$exe = (Resolve-Path $Exe).Path

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class Input
{
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        public long padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static int CountVisible(int processId, int minWidth)
    {
        var count = 0;
        EnumWindows((hWnd, lParam) =>
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid != (uint)processId || !IsWindowVisible(hWnd)) return true;
            RECT rect;
            if (GetWindowRect(hWnd, out rect) && (rect.Right - rect.Left) >= minWidth) count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }

    public static void PressAltSpace()
    {
        var inputs = new INPUT[4];
        inputs[0].type = INPUT_KEYBOARD; inputs[0].ki.wVk = 0x12; // Alt
        inputs[1].type = INPUT_KEYBOARD; inputs[1].ki.wVk = 0x20; // Leertaste
        inputs[2].type = INPUT_KEYBOARD; inputs[2].ki.wVk = 0x20; inputs[2].ki.dwFlags = KEYEVENTF_KEYUP;
        inputs[3].type = INPUT_KEYBOARD; inputs[3].ki.wVk = 0x12; inputs[3].ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(4, inputs, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@ -ErrorAction SilentlyContinue

Get-Process AIlight -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 900

Write-Host "Starte AIlight im Infobereich (Vorbereitung läuft im Hintergrund) …"
Start-Process -FilePath $exe | Out-Null
Start-Sleep -Seconds 5   # Anlaufzeit: so ist es auch im Alltag (Autostart bei der Anmeldung)

$proc = Get-Process AIlight | Select-Object -First 1
Write-Host ""

for ($i = 1; $i -le $Runs; $i++) {
    # Öffnen
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    [Input]::PressAltSpace()
    while ($sw.Elapsed.TotalMilliseconds -lt 5000) {
        if ([Input]::CountVisible($proc.Id, 200) -gt 0) { break }
        Start-Sleep -Milliseconds 1
    }
    $open = [math]::Round($sw.Elapsed.TotalMilliseconds)
    Start-Sleep -Milliseconds 400

    # Schließen (derselbe Hotkey schaltet um)
    $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
    [Input]::PressAltSpace()
    while ($sw2.Elapsed.TotalMilliseconds -lt 5000) {
        if ([Input]::CountVisible($proc.Id, 200) -eq 0) { break }
        Start-Sleep -Milliseconds 1
    }
    $close = [math]::Round($sw2.Elapsed.TotalMilliseconds)

    Write-Host ("  Lauf {0}: Hotkey → Leiste sichtbar {1} ms | Hotkey → Leiste weg {2} ms" -f $i, $open, $close)
    Start-Sleep -Milliseconds 300
}

Get-Process AIlight -ErrorAction SilentlyContinue | Stop-Process -Force
