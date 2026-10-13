using System.IO;

namespace AIlight.Core;

/// <summary>Alle Ablageorte an einer Stelle (Auftrag: nichts versteckt ablegen).</summary>
public static class Paths
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AIlight");

    public static string LocalFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIlight");

    public static string LogFolder => Path.Combine(LocalFolder, "logs");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    public static string HistoryFile => Path.Combine(DataFolder, "history.jsonl");

    public static string ConversationFile => Path.Combine(DataFolder, "conversation.json");

    public static string LastAnswerFile => Path.Combine(DataFolder, "last_answer.md");

    public static string DefaultAnswersFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AIlight-Antworten");

    public static string IconFile
    {
        get
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "ailight.ico");
            return File.Exists(beside) ? beside : "";
        }
    }

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(LocalFolder);
        Directory.CreateDirectory(LogFolder);
    }
}
