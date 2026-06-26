using System;
using System.IO;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Settings;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class UnityCodeAgentLoggerTests
    {
        private string _testRoot;
        private UnityCodeAgentLogger.LogLevel _originalMinLogLevel;
        private bool _originalLogToFile;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "UnityCodeAgentLoggerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);

            var settings = UnityCodeAgentSettings.Instance;
            _originalMinLogLevel = settings.MinLogLevel;
            _originalLogToFile = settings.LogToFile;
            settings.MinLogLevel = UnityCodeAgentLogger.LogLevel.Trace;
            settings.LogToFile = true;
        }

        [TearDown]
        public void TearDown()
        {
            var settings = UnityCodeAgentSettings.Instance;
            settings.MinLogLevel = _originalMinLogLevel;
            settings.LogToFile = _originalLogToFile;

            TryDeleteDirectory(_testRoot);
            _testRoot = null;
        }

        [Test]
        public void FileLogging_IncludesTimestampAndRollsOverWithRetention()
        {
            var logPath = Path.Combine(_testRoot, "unity.log");
            var logger = new UnityCodeAgentLogger(logPath, maxLogFileBytes: 260, retainedLogFileCount: 2);

            for (var index = 0; index < 10; index++)
            {
                logger.Info("LoggerTests", $"message-{index}-abcdefghijklmnopqrstuvwxyz");
            }

            Assert.That(File.Exists(logPath), Is.True);
            Assert.That(File.Exists(logPath + ".1"), Is.True);
            Assert.That(File.Exists(logPath + ".2"), Is.True);
            Assert.That(File.Exists(logPath + ".3"), Is.False);
            Assert.That(new FileInfo(logPath).Length, Is.LessThanOrEqualTo(260));
            Assert.That(new FileInfo(logPath + ".1").Length, Is.LessThanOrEqualTo(260));
            Assert.That(new FileInfo(logPath + ".2").Length, Is.LessThanOrEqualTo(260));

            var activeText = File.ReadAllText(logPath);
            Assert.That(activeText, Does.Contain("[INFO] #UnityCodeAgent [LoggerTests]"));
            Assert.That(activeText, Does.Match(@"^\d{4}-\d{2}-\d{2}T.* \[INFO\] #UnityCodeAgent \[LoggerTests\]"));
        }

        [Test]
        public void FileLogging_AllowsConcurrentReadWhileWriting()
        {
            var logPath = Path.Combine(_testRoot, "unity.log");
            var logger = new UnityCodeAgentLogger(logPath, maxLogFileBytes: 4096, retainedLogFileCount: 1);
            logger.Info("LoggerTests", "before-reader");

            using var reader = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            logger.Info("LoggerTests", "after-reader");

            using var textReader = new StreamReader(reader);
            var text = textReader.ReadToEnd();
            Assert.That(text, Does.Contain("before-reader"));
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
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
}
