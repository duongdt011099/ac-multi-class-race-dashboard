using Microsoft.Extensions.Logging;

namespace MulticlassRace.Services;

/// <summary>
/// Minimal append-to-file logger. The dashboard runs without a console, so this is the only place
/// a log ends up for anyone to look at, and the tray icon has a menu item that opens the folder.
/// It rotates at 2 MB so a long-running dashboard cannot fill the disk.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxFileBytes = 2 * 1024 * 1024;

    private readonly string _path;
    private readonly string _archivePath;
    private readonly LogLevel _minimumLevel;
    private readonly object _gate = new();
    private bool _disposed;

    public FileLoggerProvider(string path, LogLevel minimumLevel = LogLevel.Information)
    {
        _path = Path.GetFullPath(path);
        _archivePath = $"{_path}.1";
        _minimumLevel = minimumLevel;

        try
        {
            var directory = Path.GetDirectoryName(_path);

            if (string.IsNullOrWhiteSpace(directory) is false)
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch
        {
            // Logging is not worth failing the app over. Without it the dashboard still runs, it
            // just has nowhere to write.
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() => _disposed = true;

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        if (_disposed || level < _minimumLevel || _minimumLevel == LogLevel.None)
        {
            return;
        }

        var line = string.Concat(
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            " [",
            LevelAbbreviation(level),
            "] ",
            category,
            ": ",
            message);

        if (exception is not null)
        {
            line = string.Concat(line, " | ", exception);
        }

        line = string.Concat(line, Environment.NewLine);

        lock (_gate)
        {
            try
            {
                RollIfNeeded(line.Length);
                File.AppendAllText(_path, line);
            }
            catch
            {
                // Losing a log line must never take the dashboard or the launcher down.
            }
        }
    }

    private void RollIfNeeded(int incomingLength)
    {
        var info = new FileInfo(_path);

        if (info.Exists is false || info.Length + incomingLength <= MaxFileBytes)
        {
            return;
        }

        File.Delete(_archivePath);
        File.Move(_path, _archivePath);
    }

    private static string LevelAbbreviation(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "NON"
    };

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

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _provider._minimumLevel && _provider._minimumLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) is false)
            {
                return;
            }

            _provider.Write(logLevel, _category, formatter(state, exception), exception);
        }
    }
}
