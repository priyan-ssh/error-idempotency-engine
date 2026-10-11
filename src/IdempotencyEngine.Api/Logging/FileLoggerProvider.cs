namespace IdempotencyEngine.Api.Logging;

/// <summary>
/// A custom logger provider that writes log messages to a specified file.
/// </summary>
public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logPath;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLoggerProvider"/> class with the specified log file path.
    /// If the directory for the log file does not exist, it will be created.
    /// </summary>
    /// <param name="logPath"></param>
    public FileLoggerProvider(string logPath)
    {
        _logPath = logPath;
        var dir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// Creates a new <see cref="FileLogger"/> instance for the specified category name.
    /// </summary>
    /// <param name="categoryName"></param>
    /// <returns></returns>
    public ILogger CreateLogger(string categoryName) => new FileLogger(_logPath, categoryName, _lock);

    /// <summary>
    /// Disposes of the logger provider. This implementation does not hold any unmanaged resources, so no action is taken.
    /// </summary>
    public void Dispose() { }
}


/// <summary>
/// A custom logger that writes log messages to a specified file.
/// </summary>
public class FileLogger : ILogger
{
    private readonly string _logPath;
    private readonly string _categoryName;
    private readonly object _lock;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLogger"/> class with the specified log file path, category name, and lock object for thread safety.
    /// </summary>
    /// <param name="logPath"></param>
    /// <param name="categoryName"></param>
    /// <param name="lock"></param>
    public FileLogger(string logPath, string categoryName, object @lock)
    {
        _logPath = logPath;
        _categoryName = categoryName;
        _lock = @lock;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;

    public bool IsEnabled(LogLevel logLevel) => true;

    /// <summary>
    /// Logs a message to the specified log file with the given log level, event ID, state, exception, and formatter function. The log message is formatted with a timestamp, log level, category name, and the formatted message. If an exception is provided, it is appended to the log message. Thread safety is ensured by locking on a shared object when writing to the file.
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    /// <param name="logLevel"></param>
    /// <param name="eventId"></param>
    /// <param name="state"></param>
    /// <param name="exception"></param>
    /// <param name="formatter"></param>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (formatter == null) return;

        var message = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_categoryName}: {formatter(state, exception)}";
        if (exception != null)
        {
            message += Environment.NewLine + exception;
        }

        lock (_lock)
        {
            File.AppendAllText(_logPath, message + Environment.NewLine);
        }
    }
}

/// <summary>
/// Extension methods for adding the <see cref="FileLoggerProvider"/> to the logging builder.
/// </summary>
public static class FileLoggerExtensions
{
    /// <summary>
    /// Adds a <see cref="FileLoggerProvider"/> to the logging builder, which writes log messages to the specified file path. If the directory for the log file does not exist, it will be created.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="path"></param>
    /// <returns>The updated logging builder.</returns>
    public static ILoggingBuilder AddFileLog(this ILoggingBuilder builder, string path)
    {
        builder.AddProvider(new FileLoggerProvider(path));
        return builder;
    }
}
