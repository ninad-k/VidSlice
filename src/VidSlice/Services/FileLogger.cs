using System.IO;
using Microsoft.Extensions.Logging;

namespace VidSlice.Services;

/// <summary>
/// Tiny thread-safe file logger provider — appends one line per log entry to a
/// rolling daily file. Avoids pulling in a heavier logging framework.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _gate = new();

    private const int RetentionDays = 14;

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
        PruneOldLogs();
    }

    /// <summary>Delete log files older than the retention window so they don't accumulate forever.</summary>
    private void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "vidslice-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    try { File.Delete(file); } catch { /* in use or gone */ }
                }
            }
        }
        catch { /* pruning is best-effort */ }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(string line)
    {
        // Date is intentionally read here (not in tests) so logs roll daily.
        var file = Path.Combine(_directory, $"vidslice-{DateTime.Now:yyyy-MM-dd}.log");
        lock (_gate)
        {
            try { File.AppendAllText(file, line + Environment.NewLine); }
            catch { /* logging must never throw */ }
        }
    }

    public void Dispose() { }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            var shortCat = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
            var line = $"{DateTime.Now:HH:mm:ss} [{logLevel,-11}] {shortCat}: {msg}";
            if (exception is not null) line += Environment.NewLine + exception;
            provider.Write(line);
        }
    }
}
