using System.IO.Pipes;
using System.Text;

namespace AIlight.Core;

/// <summary>
/// Genau eine Instanz. Ein zweiter Start schickt der laufenden Instanz
/// „show“ (Leiste nach vorn) oder „quit“ (beenden) über eine benannte Pipe.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "AIlight.Launcher.Single";
    private const string PipeName = "AIlight.Launcher.Pipe";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancel = new();
    private NamedPipeServerStream? _server;
    private Task? _listen;
    private bool _disposed;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirst = createdNew;
    }

    public bool IsFirst { get; }

    /// <summary>Nachrichten der zweiten Instanz („show“ oder „quit“).</summary>
    public event Action<string>? MessageReceived;

    /// <summary>Wartet im Hintergrund auf Nachrichten weiterer Starts.</summary>
    public void Listen()
    {
        if (!IsFirst)
        {
            return;
        }

        _server = new NamedPipeServerStream(
            PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _listen = Task.Run(async () =>
        {
            while (!_cancel.IsCancellationRequested)
            {
                try
                {
                    await _server.WaitForConnectionAsync(_cancel.Token).ConfigureAwait(false);
                    using var reader = new StreamReader(_server, Encoding.UTF8, leaveOpen: true);
                    var text = (await reader.ReadToEndAsync().ConfigureAwait(false)).Trim();
                    if (text.Length > 0)
                    {
                        MessageReceived?.Invoke(text);
                    }

                    _server.Disconnect();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warn($"Pipe-Nachricht fehlgeschlagen: {ex.Message}");
                    return;
                }
            }
        });
    }

    /// <summary>Einer laufenden Instanz etwas sagen. „false“ wenn keine läuft.</summary>
    public static bool Send(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(600);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.Write(message);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Keine laufende Instanz erreichbar: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return; // darf auch zweimal aufgerufen werden (Programm und App räumen beide auf)
        }

        _disposed = true;
        try
        {
            _cancel.Cancel();
            _server?.Dispose();
            if (IsFirst)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Aufräumen der Einzelinstanz: {ex.Message}");
        }

        _mutex.Dispose();
        _cancel.Dispose();
        _ = _listen;
    }
}
