using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Tools = SignalLoop.UnityCodeAgent.Tools;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class ScreenshotToolArtifactResultTests
    {
        private string _testRoot;
        private string _tempCapturePath;
        private string _artifactRoot;
        private byte[] _pngBytes;
        private DateTime _testStartedUtc;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "UnityGameViewScreenshotToolTests", Guid.NewGuid().ToString("N"));
            _tempCapturePath = Path.Combine(_testRoot, "capture.png");
            _artifactRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".unityCodeAgent", "screenshots"));
            Directory.CreateDirectory(_testRoot);
            Directory.CreateDirectory(_artifactRoot);
            _pngBytes = CreatePngBytes(4, 3);
            _testStartedUtc = DateTime.UtcNow;
        }

        [TearDown]
        public void TearDown()
        {
            DeleteArtifactsCreatedDuringTest();
            TryDeleteDirectory(_testRoot);
            _testRoot = null;
            _tempCapturePath = null;
            _artifactRoot = null;
            _pngBytes = null;
        }

        [Test]
        public async Task CaptureResult_IncludesSavedPathTextAndImageContent()
        {
            var tool = CreateTool();

            var result = await tool.ExecuteAsync(JObject.FromObject(new { max_height = 640 }));

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content, Has.Count.EqualTo(2), DescribeResult(result));

            var text = result.Content[0];
            Assert.That(text.Type, Is.EqualTo("text"));
            Assert.That(text.Text, Does.Contain("Screenshot saved to: "));
            Assert.That(text.Text, Does.Contain("PNG image data is also attached"));

            var artifactPath = ExtractArtifactPath(text.Text);
            Assert.That(File.Exists(artifactPath), Is.True);
            Assert.That(Path.GetFullPath(artifactPath), Does.StartWith(Path.GetFullPath(_artifactRoot)));
            Assert.That(File.ReadAllBytes(artifactPath), Is.EqualTo(_pngBytes));

            var image = result.Content[1];
            Assert.That(image.Type, Is.EqualTo(Tools.Protocol.ToolContentTypes.Image));
            Assert.That(image.MimeType, Is.EqualTo("image/png"));
            Assert.That(image.Data, Is.Not.Empty);
            Assert.That(Convert.FromBase64String(image.Data), Is.EqualTo(_pngBytes));
        }

        [TestCase(51)]
        public async Task CaptureResult_PrunesOldAndExcessArtifactsWithoutDeletingCurrentArtifact(int existingArtifactCount)
        {
            var oldPath = Path.Combine(_artifactRoot, "old.png");
            File.WriteAllBytes(oldPath, _pngBytes);
            File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-4));

            for (var index = 0; index < existingArtifactCount; index++)
            {
                var path = Path.Combine(_artifactRoot, $"existing_{index:00}.png");
                File.WriteAllBytes(path, _pngBytes);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-60 + index));
            }

            var tool = CreateTool();

            var result = await tool.ExecuteAsync(JObject.FromObject(new { max_height = 640 }));

            Assert.That(result.Content, Has.Count.EqualTo(2), DescribeResult(result));
            var artifactPath = ExtractArtifactPath(result.Content[0].Text);
            Assert.That(File.Exists(artifactPath), Is.True);
            Assert.That(File.Exists(oldPath), Is.False);
            Assert.That(Directory.GetFiles(_artifactRoot, "*.png", SearchOption.TopDirectoryOnly), Has.Length.LessThanOrEqualTo(50));
        }

        private Tools.CustomTools.GetUnityGameViewWindowScreenshotTool CreateTool()
        {
            return new Tools.CustomTools.GetUnityGameViewWindowScreenshotTool(
                () => _tempCapturePath,
                path =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, _pngBytes);
                },
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(1),
                () => _artifactRoot);
        }

        private static string ExtractArtifactPath(string text)
        {
            const string prefix = "Screenshot saved to: ";
            var start = text.IndexOf(prefix, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            start += prefix.Length;

            var end = text.IndexOf('\n', start);
            return end < 0 ? text.Substring(start).Trim() : text.Substring(start, end - start).Trim();
        }

        private static string DescribeResult(Tools.Protocol.ToolsCallResult result)
        {
            var content = result.Content == null
                ? "<null>"
                : string.Join(", ", result.Content.Select(item =>
                    item == null
                        ? "<null item>"
                        : $"type={item.Type ?? "<null>"}, hasText={!string.IsNullOrEmpty(item.Text)}, hasData={!string.IsNullOrEmpty(item.Data)}, mime={item.MimeType ?? "<null>"}"));

            var toolAssembly = typeof(Tools.CustomTools.GetUnityGameViewWindowScreenshotTool).Assembly;
            return $"LoadedToolAssembly={toolAssembly.Location}; ModuleVersionId={toolAssembly.ManifestModule.ModuleVersionId}; Content=[{content}]";
        }

        private static byte[] CreatePngBytes(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                var pixels = new Color32[width * height];
                for (var index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = new Color32(32, 96, 192, 255);
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
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

        private void DeleteArtifactsCreatedDuringTest()
        {
            if (string.IsNullOrWhiteSpace(_artifactRoot) || !Directory.Exists(_artifactRoot))
            {
                return;
            }

            foreach (var path in Directory.GetFiles(_artifactRoot, "*.png", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                if (name == "old.png" || name.StartsWith("existing_", StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(path);
                    continue;
                }

                if (name.StartsWith("game_view_", StringComparison.OrdinalIgnoreCase)
                    && File.GetLastWriteTimeUtc(path) >= _testStartedUtc.AddSeconds(-1))
                {
                    TryDeleteFile(path);
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
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
