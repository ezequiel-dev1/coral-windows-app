namespace Coral.WinApp.Bridge;

public sealed class RollingFileLoggerProvider(string directory) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(directory, categoryName);
    public void Dispose() { }
    private sealed class FileLogger(string directory, string category) : ILogger
    {
        private static readonly object Sync = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try { Directory.CreateDirectory(directory); var path = Path.Combine(directory, $"bridge-{DateTime.UtcNow:yyyyMMdd}.log"); lock (Sync) { if (File.Exists(path) && new FileInfo(path).Length > 10 * 1024 * 1024) File.Move(path, path + ".1", true); File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {level} {category}: {formatter(state, exception)}{Environment.NewLine}"); } } catch { }
        }
    }
}
