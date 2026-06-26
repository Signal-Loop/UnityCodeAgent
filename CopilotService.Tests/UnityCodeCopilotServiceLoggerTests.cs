using NUnit.Framework;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Options;
using UnityCodeCopilot.Service.Settings;

namespace UnityCodeCopilot.Service.Tests;

public sealed class UnityCodeCopilotServiceLoggerTests
{
    private string? _testRoot;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "UnityCodeCopilotServiceLoggerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [TearDown]
    public void TearDown()
    {
        TryDeleteDirectory(_testRoot);
        _testRoot = null;
    }

    [Test]
    public void FileLogging_RollsOverWithBoundedRetention()
    {
        var logger = CreateLogger(maxLogFileBytes: 300, retainedLogFileCount: 2);
        var logPath = Path.Combine(_testRoot!, ".unityCodeAgent", "service", "logs", "service.log");

        for (var index = 0; index < 12; index++)
        {
            logger.Info("LoggerTests", $"message-{index}-abcdefghijklmnopqrstuvwxyz");
        }

        Assert.That(File.Exists(logPath), Is.True);
        Assert.That(File.Exists(logPath + ".1"), Is.True);
        Assert.That(File.Exists(logPath + ".2"), Is.True);
        Assert.That(File.Exists(logPath + ".3"), Is.False);
        Assert.That(new FileInfo(logPath).Length, Is.LessThanOrEqualTo(300));
        Assert.That(new FileInfo(logPath + ".1").Length, Is.LessThanOrEqualTo(300));
        Assert.That(new FileInfo(logPath + ".2").Length, Is.LessThanOrEqualTo(300));
        Assert.That(File.ReadAllText(logPath), Does.Match(@"^\d{4}-\d{2}-\d{2}T.* \[INFO\] \[LoggerTests\]"));
    }

    [Test]
    public void FileLogging_AllowsConcurrentReadWhileWriting()
    {
        var logger = CreateLogger(maxLogFileBytes: 4096, retainedLogFileCount: 1);
        var logPath = Path.Combine(_testRoot!, ".unityCodeAgent", "service", "logs", "service.log");
        logger.Info("LoggerTests", "before-reader");

        using var reader = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        logger.Info("LoggerTests", "after-reader");

        using var textReader = new StreamReader(reader);
        var text = textReader.ReadToEnd();
        Assert.That(text, Does.Contain("before-reader"));
    }

    private UnityCodeCopilotServiceLogger CreateLogger(long maxLogFileBytes, int retainedLogFileCount)
    {
        var options = new ServiceOptions
        {
            ProjectRoot = _testRoot!,
            MinLogLevel = UnityCodeCopilotServiceLogger.LogLevel.Trace,
            LogToFile = true,
        };

        return new UnityCodeCopilotServiceLogger(
            new ProjectPaths(_testRoot!),
            options,
            maxLogFileBytes,
            retainedLogFileCount);
    }

    private static void TryDeleteDirectory(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
