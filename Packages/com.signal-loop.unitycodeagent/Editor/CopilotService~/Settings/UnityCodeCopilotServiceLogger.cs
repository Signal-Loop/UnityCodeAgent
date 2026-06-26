using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Options;

namespace UnityCodeCopilot.Service.Settings;

public class UnityCodeCopilotServiceLogger
{
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Fatal = 5,
        Off = 6,
    }

    private const long DefaultMaxLogFileBytes = 5 * 1024 * 1024;
    private const int DefaultRetainedLogFileCount = 3;
    private readonly object _sync = new();
    private readonly string _logFilePath;
    private readonly long _maxLogFileBytes;
    private readonly int _retainedLogFileCount;
    private readonly ServiceOptions _options;

    public UnityCodeCopilotServiceLogger(ProjectPaths paths, ServiceOptions options)
        : this(paths, options, DefaultMaxLogFileBytes, DefaultRetainedLogFileCount)
    {
    }

    public UnityCodeCopilotServiceLogger(ProjectPaths paths, ServiceOptions options, long maxLogFileBytes, int retainedLogFileCount)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _logFilePath = Path.Combine(paths.LogsRoot.Replace('/', Path.DirectorySeparatorChar), "service.log");
        _maxLogFileBytes = Math.Max(1, maxLogFileBytes);
        _retainedLogFileCount = Math.Max(0, retainedLogFileCount);
    }

    public void Trace(string category, string message, params (string Key, object? Value)[] properties)
        => Write(LogLevel.Trace, category, message, null, properties);

    public void Debug(string category, string message, params (string Key, object? Value)[] properties)
        => Write(LogLevel.Debug, category, message, null, properties);

    public void Info(string category, string message, params (string Key, object? Value)[] properties)
        => Write(LogLevel.Info, category, message, null, properties);

    public void Warning(string category, string message, params (string Key, object? Value)[] properties)
        => Write(LogLevel.Warning, category, message, null, properties);

    public void Error(string category, string message, Exception? exception = null, params (string Key, object? Value)[] properties)
        => Write(LogLevel.Error, category, message, exception, properties);

    public void Fatal(string category, string message, Exception? exception = null, params (string Key, object? Value)[] properties)
        => Write(LogLevel.Fatal, category, message, exception, properties);

    private void Write(LogLevel level, string category, string message, Exception? exception, params (string Key, object? Value)[] properties)
    {
        if (level < _options.MinLogLevel || _options.MinLogLevel == LogLevel.Off)
        {
            return;
        }

        var line = FormatLine(level, category, message, exception, properties);

        if (_options.LogToFile)
        {
            var directoryPath = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            lock (_sync)
            {
                RotateIfNeeded(_logFilePath, GetUtf8LineByteCount(line), _maxLogFileBytes, _retainedLogFileCount);
                using var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(line);
            }
        }

        Console.WriteLine(line);
    }

    private static string FormatLine(LogLevel level, string category, string message, Exception? exception, params (string Key, object? Value)[] properties)
    {
        var formattedProperties = properties.Length == 0
            ? string.Empty
            : " " + string.Join(" ", properties.Select(property => $"{property.Key}={FormatValue(property.Value)}"));

        var formattedException = exception == null
            ? string.Empty
            : $" exception={exception.GetType().Name}:{exception.Message} stacktrace={exception.StackTrace?.Replace('\r', ' ').Replace('\n', ' ') ?? "null"}";

        return $"{DateTimeOffset.UtcNow:O} [{ToLevelToken(level)}] [{category}] {message}{formattedProperties}{formattedException}";
    }

    private static string FormatValue(object? value)
        => value switch
        {
            null => "null",
            string text => text.Replace('\r', ' ').Replace('\n', ' '),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
        };

    private static int GetUtf8LineByteCount(string line)
        => System.Text.Encoding.UTF8.GetByteCount(line + Environment.NewLine);

    private static void RotateIfNeeded(string logFilePath, int nextLineByteCount, long maxLogFileBytes, int retainedLogFileCount)
    {
        if (!File.Exists(logFilePath))
        {
            return;
        }

        var currentSize = new FileInfo(logFilePath).Length;
        if (currentSize <= 0 || currentSize + nextLineByteCount <= maxLogFileBytes)
        {
            return;
        }

        if (retainedLogFileCount <= 0)
        {
            File.Delete(logFilePath);
            return;
        }

        var oldestPath = GetRotatedLogPath(logFilePath, retainedLogFileCount);
        if (File.Exists(oldestPath))
        {
            File.Delete(oldestPath);
        }

        for (var index = retainedLogFileCount - 1; index >= 1; index--)
        {
            var sourcePath = GetRotatedLogPath(logFilePath, index);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var destinationPath = GetRotatedLogPath(logFilePath, index + 1);
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(sourcePath, destinationPath);
        }

        File.Move(logFilePath, GetRotatedLogPath(logFilePath, 1));
    }

    private static string GetRotatedLogPath(string logFilePath, int index)
        => $"{logFilePath}.{index}";

    private static string ToLevelToken(LogLevel level)
        => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Fatal => "FATAL",
            LogLevel.Off => "OFF",
            _ => level.ToString().ToUpperInvariant(),
        };
}
