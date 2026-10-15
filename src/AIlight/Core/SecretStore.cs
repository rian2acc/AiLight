using System.Runtime.InteropServices;
using System.Text;

namespace AIlight.Core;

/// <summary>
/// Der API-Schlüssel liegt im Windows-Anmeldeinformationsspeicher (generische
/// Anmeldeinformation, lokal für diesen Benutzer) — nie in einer Datei, nie im Log.
/// </summary>
public static class SecretStore
{
    /// <summary>Ablageort des echten Schlüssels.</summary>
    public const string BuiltInTarget = "AIlight:groq-api-key";

    /// <summary>
    /// Der benutzte Eintrag. Über die Umgebungsvariable <c>AILIGHT_KEY_TARGET</c>
    /// umlenkbar — damit lässt sich der erste Start (noch kein Schlüssel) prüfen,
    /// ohne den echten Schlüssel anzufassen.
    /// </summary>
    public static string DefaultTarget
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("AILIGHT_KEY_TARGET");
            return string.IsNullOrWhiteSpace(custom) ? BuiltInTarget : custom.Trim();
        }
    }
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    /// <summary>
    /// Einträge früherer Fassungen. Wer schon einmal eine AIlight-Version (oder das
    /// Flow-Launcher-Plugin) benutzt hat, muss seinen Schlüssel nicht neu eintippen.
    /// </summary>
    private static readonly string[] LegacyTargets = { "ailight", "flow-groq" };

    /// <summary>Ist ein eigener Ablageort gesetzt (Umgebungsvariable)?</summary>
    private static bool IsCustomTarget => DefaultTarget != BuiltInTarget;

    /// <summary>
    /// Wo der Schlüssel eines Anbieters liegt. Für Groq bleibt es beim alten
    /// Namen — wer schon eine Fassung benutzt hat, muss seinen Schlüssel nicht
    /// neu eintippen.
    /// </summary>
    public static string Target(string provider)
    {
        var id = (provider ?? "").Trim().ToLowerInvariant();
        return id.Length == 0 || id == "groq" ? DefaultTarget : $"AIlight:{id}-api-key";
    }

    /// <summary>Schlüssel eines Anbieters lesen; leer, wenn keiner gespeichert ist.</summary>
    public static string Read(string provider) => ReadRaw(Target(provider));

    /// <summary>Schlüssel eines Anbieters setzen (leer = löschen).</summary>
    public static bool Write(string provider, string value) => WriteRaw(Target(provider), value);

    public static bool Delete(string provider) => DeleteRaw(Target(provider));

    public static bool HasKey(string provider) => Read(provider).Length > 0;

    /// <summary>Schlüssel lesen; leer, wenn keiner gespeichert ist.</summary>
    public static string ReadRaw(string target)
    {
        var value = ReadExact(target);
        // Der Blick auf frühere Einträge gilt nur für den Standardablageort —
        // sonst würde ein fremder Zielname den echten Schlüssel zurückliefern.
        if (value.Length > 0 || target != DefaultTarget || IsCustomTarget)
        {
            return value;
        }

        foreach (var legacy in LegacyTargets)
        {
            var old = ReadExact(legacy);
            if (old.Length > 0)
            {
                Log.Info($"API-Schlüssel aus dem früheren Eintrag „{legacy}“ übernommen");
                WriteRaw(target, old);
                return old;
            }
        }

        return "";
    }

    private static string ReadExact(string target)
    {
        if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out var pointer))
        {
            return "";
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
            if (credential.CredentialBlobSize <= 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return "";
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally
        {
            CredFree(pointer);
        }
    }

    /// <summary>Schlüssel setzen (leer = löschen).</summary>
    public static bool WriteRaw(string target, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DeleteRaw(target);
        }

        var bytes = Encoding.Unicode.GetBytes(value.Trim());
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                Comment = "API-Schlüssel für AIlight",
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = Environment.UserName,
            };

            if (CredWriteW(ref credential, 0))
            {
                return true;
            }

            var error = Marshal.GetLastWin32Error();
            Log.Error($"Schlüssel konnte nicht gespeichert werden (Fehler {error})");
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public static bool DeleteRaw(string target)
    {
        if (CredDeleteW(target, CRED_TYPE_GENERIC, 0))
        {
            return true;
        }

        // Nicht vorhanden ist kein Fehler.
        return Marshal.GetLastWin32Error() == 1168;
    }

    public static bool HasRaw(string target) => ReadRaw(target).Length > 0;
}
