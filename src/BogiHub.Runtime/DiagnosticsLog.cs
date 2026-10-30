using System.Text;

namespace BogiHub.Runtime;

public enum LogLevel { Info, Warning, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public override string ToString() => $"{Time:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {Message}";
}

/// <summary>
/// Bounded, thread-safe event log. Old entries fall off; it never grows without limit.
/// Optionally mirrored to a file (rolled over at 2 MB) so problems can be investigated after the fact.
/// </summary>
public sealed class DiagnosticsLog(int capacity = 500, string? filePath = null)
{
    private const long MaxFileBytes = 2 * 1024 * 1024;

    private readonly LinkedList<LogEntry> _entries = new();
    private readonly object _lock = new();

    public event Action<LogEntry>? Added;

    /// <summary>Where the log is written on disk, if anywhere.</summary>
    public string? FilePath { get; } = filePath;

    public void Info(string message) => Add(LogLevel.Info, message);
    public void Warn(string message) => Add(LogLevel.Warning, message);
    public void Error(string message) => Add(LogLevel.Error, message);

    public void Add(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (_lock)
        {
            _entries.AddLast(entry);
            while (_entries.Count > capacity) _entries.RemoveFirst();
            WriteToFile(entry);
        }
        Added?.Invoke(entry);
    }

    private void WriteToFile(LogEntry entry)
    {
        if (FilePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var info = new FileInfo(FilePath);
            if (info.Exists && info.Length > MaxFileBytes) File.Move(FilePath, FilePath + ".old", overwrite: true);
            File.AppendAllText(FilePath, entry + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down; the in-memory log still has the entry.
        }
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock) return [.. _entries];
    }

    public string Export(string header)
    {
        var sb = new StringBuilder(header).AppendLine().AppendLine();
        foreach (var e in Snapshot()) sb.AppendLine(e.ToString());
        return sb.ToString();
    }
}
