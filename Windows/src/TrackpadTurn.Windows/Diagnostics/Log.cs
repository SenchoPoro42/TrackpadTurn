using System.Globalization;
using System.Threading.Channels;

namespace TrackpadTurn.Windows.Diagnostics;

/// <summary>
/// A small file log in %LOCALAPPDATA%\TrackpadTurn\Logs. Info lines are always written; debug
/// lines (every gesture decision) only while "Write a diagnostic log" is on.
/// </summary>
/// <remarks>
/// Writing happens on a background task, so the input thread and its mouse hook never wait for
/// the disk: a slow hook makes Windows remove it.
/// </remarks>
internal static class Log
{
    private const long MaxFileBytes = 4 * 1024 * 1024;

    private static readonly Channel<string> Lines = Channel.CreateBounded<string>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private static Task? writer;

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrackpadTurn", "Logs");

    public static string FilePath => Path.Combine(Folder, "TrackpadTurn.log");

    /// <summary>Set from the settings; read on the input thread without locking.</summary>
    public static volatile bool IsDebugEnabled;

    public static void Start()
    {
        writer ??= Task.Run(WriteLinesAsync);
    }

    public static void Info(string message) => Enqueue("INFO ", message);

    public static void Debug(string message)
    {
        if (IsDebugEnabled)
        {
            Enqueue("DEBUG", message);
        }
    }

    public static void Debug(Func<string> message)
    {
        if (IsDebugEnabled)
        {
            Enqueue("DEBUG", message());
        }
    }

    public static void Error(string message, Exception? exception = null) =>
        Enqueue("ERROR", exception is null ? message : $"{message}: {exception}");

    /// <summary>Writes what is queued, for a clean exit.</summary>
    public static void Flush()
    {
        Lines.Writer.TryComplete();
        writer?.Wait(TimeSpan.FromSeconds(2));
    }

    private static void Enqueue(string level, string message) =>
        Lines.Writer.TryWrite(string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}"));

    private static async Task WriteLinesAsync()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            RotateIfLarge();
            await using var file = new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
            await foreach (var line in Lines.Reader.ReadAllAsync())
            {
                await file.WriteLineAsync(line);
                if (Lines.Reader.Count == 0)
                {
                    await file.FlushAsync();
                }
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RotateIfLarge()
    {
        var current = new FileInfo(FilePath);
        if (current.Exists && current.Length > MaxFileBytes)
        {
            File.Move(FilePath, Path.Combine(Folder, "TrackpadTurn.previous.log"), overwrite: true);
        }
    }
}
