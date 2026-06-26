using System;
using System.IO;
using System.Text;
using SignalLoop.UnityCodeAgent.Settings;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Logging
{
    public class UnityCodeAgentLogger
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

        private const string LogPrefix = "#UnityCodeAgent";
        private const long DefaultMaxLogFileBytes = 5 * 1024 * 1024;
        private const int DefaultRetainedLogFileCount = 3;
        private static readonly object GlobalFileSync = new object();
        private static string CurrentProjectRoot = GetProjectRoot();
        private readonly string _logFilePath;
        private readonly long _maxLogFileBytes;
        private readonly int _retainedLogFileCount;

        public UnityCodeAgentLogger()
            : this(Path.Combine(GetCurrentProjectRoot(), ".unityCodeAgent", "client", "logs", "unity.log"), DefaultMaxLogFileBytes, DefaultRetainedLogFileCount)
        {
        }

        public UnityCodeAgentLogger(string logFilePath, long maxLogFileBytes, int retainedLogFileCount)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                throw new ArgumentException("Log file path is required.", nameof(logFilePath));
            }

            _logFilePath = logFilePath;
            _maxLogFileBytes = Math.Max(1, maxLogFileBytes);
            _retainedLogFileCount = Math.Max(0, retainedLogFileCount);
        }

        private static LogLevel GetCurrentMinLogLevel()
        {
            return UnityCodeAgentSettings.Instance.MinLogLevel;
        }

        private static bool GetCurrentLogToFile()
        {
            return UnityCodeAgentSettings.Instance.LogToFile;
        }

        private static string GetCurrentProjectRoot()
        {
            return CurrentProjectRoot;
        }

        public void Trace(string category, string message)
            => Write(LogLevel.Trace, category, message, null);

        public void Debug(string category, string message)
            => Write(LogLevel.Debug, category, message, null);

        public void Info(string category, string message)
            => Write(LogLevel.Info, category, message, null);

        public void Warning(string category, string message)
            => Write(LogLevel.Warning, category, message, null);

        public void Error(string category, string message, Exception exception = null)
            => Write(LogLevel.Error, category, message, exception);

        public void Fatal(string category, string message, Exception exception = null)
            => Write(LogLevel.Fatal, category, message, exception);

        private void Write(LogLevel level, string category, string message, Exception exception)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            var consoleFormatted = Format(level, category, message, exception, includeTimestamp: false);
            WriteToUnityConsole(level, consoleFormatted);

            if (!GetCurrentLogToFile())
            {
                return;
            }

            var fileFormatted = Format(level, category, message, exception, includeTimestamp: true);

            var directoryPath = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            try
            {
                lock (GlobalFileSync)
                {
                    RotateIfNeeded(_logFilePath, GetUtf8LineByteCount(fileFormatted), _maxLogFileBytes, _retainedLogFileCount);
                    using var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream);
                    writer.WriteLine(fileFormatted);
                }
            }
            catch (IOException ioException)
            {
                WriteToUnityConsole(LogLevel.Warning, $"[WARNING] {LogPrefix} [UnityCodeAgentLogger] Failed to write log file '{_logFilePath}'. {ioException.GetType().Name}: {ioException.Message}");
            }
            catch (UnauthorizedAccessException unauthorizedAccessException)
            {
                WriteToUnityConsole(LogLevel.Warning, $"[WARNING] {LogPrefix} [UnityCodeAgentLogger] Failed to write log file '{_logFilePath}'. {unauthorizedAccessException.GetType().Name}: {unauthorizedAccessException.Message}");
            }
        }

        private static bool IsEnabled(LogLevel level)
        {
            var currentMinLogLevel = GetCurrentMinLogLevel();
            return currentMinLogLevel != LogLevel.Off && level >= currentMinLogLevel;
        }

        private static string Format(LogLevel level, string category, string message, Exception exception, bool includeTimestamp)
        {
            var builder = new StringBuilder();
            if (includeTimestamp)
            {
                builder.Append(DateTimeOffset.UtcNow.ToString("O")).Append(' ');
            }

            builder.Append('[').Append(level.ToString().ToUpperInvariant()).Append("] ")
                .Append(LogPrefix).Append(" [").Append(category).Append("] ")
                .Append(message);

            if (exception != null)
            {
                builder.AppendLine();
                builder.Append(FormatException(exception));
            }

            return builder.ToString();
        }

        private static int GetUtf8LineByteCount(string line)
            => Encoding.UTF8.GetByteCount(line + Environment.NewLine);

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

        private static string FormatException(Exception exception)
        {
            var builder = new StringBuilder();
            FormatException(builder, exception, 0);
            return builder.ToString().TrimEnd();
        }

        private static void FormatException(StringBuilder builder, Exception exception, int depth)
        {
            if (depth > 0)
            {
                builder.AppendLine(Indent(depth - 1) + $"Inner exception {depth}:");
            }
            else
            {
                builder.AppendLine("Exception:");
            }

            builder.AppendLine(Indent(depth) + $"{exception.GetType().FullName}: {exception.Message}");

            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                builder.AppendLine(Indent(depth) + "Stack trace:");
                AppendIndentedMultiline(builder, exception.StackTrace, depth + 1);
            }

            if (exception.InnerException != null)
            {
                FormatException(builder, exception.InnerException, depth + 1);
            }
        }

        private static void AppendIndentedMultiline(StringBuilder builder, string value, int depth)
        {
            var indent = Indent(depth);
            using var reader = new StringReader(value);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                builder.AppendLine(indent + line);
            }
        }

        private static string Indent(int depth)
        {
            if (depth <= 0)
            {
                return string.Empty;
            }

            return new string(' ', depth * 2);
        }

        private static void WriteToUnityConsole(LogLevel level, string message)
        {
            if (level >= LogLevel.Error)
            {
                UnityEngine.Debug.LogError(message);
                return;
            }

            if (level == LogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning(message);
                return;
            }

            UnityEngine.Debug.Log(message);
        }

        private static string GetProjectRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
