using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace RdpManager.Presentation.Services;

/// <summary>Where Deskpin's logs live: %LOCALAPPDATA%\RdpManager\logs\deskpin-yyyyMMdd.log.</summary>
public static class LogPaths
{
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RdpManager", "logs");

    public static string CurrentFile =>
        Path.Combine(Directory, $"deskpin-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>Best-effort retention: drops log files older than two weeks.</summary>
    public static void CleanupOldLogs(TimeSpan? keep = null)
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return;
            var cutoff = DateTime.UtcNow - (keep ?? TimeSpan.FromDays(14));
            foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "deskpin-*.log")
                         .Where(f => File.GetLastWriteTimeUtc(f) < cutoff))
            {
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception) { /* retention is best-effort */ }
    }
}

/// <summary>
/// Minimal file logger for the whole app (no extra packages). The Generic Host's default
/// providers are Console (invisible in a WinExe) and Debug (debugger-only) — this provider is
/// what actually lands ILogger output on disk. Information and above; Debug/Trace stay
/// debugger-only to keep the file readable.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private StreamWriter? _writer;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void Write(string line)
    {
        try
        {
            lock (_gate)
            {
                if (_writer is null)
                {
                    System.IO.Directory.CreateDirectory(LogPaths.Directory);
                    _writer = new StreamWriter(new FileStream(
                        LogPaths.CurrentFile, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        AutoFlush = true, // survive crashes — every line hits the disk immediately
                    };
                }
                _writer.WriteLine(line);
            }
        }
        catch (Exception) { /* logging must never take the app down */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // Short category ("RdpManager.Application.Files.FileTransferUseCase" → "FileTransferUseCase").
            var dot = _category.LastIndexOf('.');
            var category = dot >= 0 ? _category[(dot + 1)..] : _category;

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{Level(logLevel)}] {category}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            _provider.Write(line);
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "FATAL",
            _ => level.ToString().ToUpperInvariant(),
        };
    }
}
