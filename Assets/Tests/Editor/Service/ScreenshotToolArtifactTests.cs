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
    public sealed class ScreenshotToolImageResultTests
    {
        private string _testRoot;
        private string _tempCapturePath;
        private byte[] _pngBytes;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "UnityGameViewScreenshotToolTests", Guid.NewGuid().ToString("N"));
            _tempCapturePath = Path.Combine(_testRoot, "capture.png");
            Directory.CreateDirectory(_testRoot);
            _pngBytes = CreatePngBytes(4, 3);
        }

        [TearDown]
        public void TearDown()
        {
            TryDeleteDirectory(_testRoot);
            _testRoot = null;
            _tempCapturePath = null;
            _pngBytes = null;
        }

        [Test]
        public async Task CaptureResult_ReturnsImageContentAndDeletesTemporaryFile()
        {
            var tool = CreateTool();

            var result = await tool.ExecuteAsync(JObject.FromObject(new { max_height = 640 }));

            Assert.That(result.IsError, Is.False);
            Assert.That(result.Content, Has.Count.EqualTo(1), DescribeResult(result));
            Assert.That(File.Exists(_tempCapturePath), Is.False);

            var image = result.Content[0];
            Assert.That(image.Type, Is.EqualTo(Tools.Protocol.ToolContentTypes.Image));
            Assert.That(image.MimeType, Is.EqualTo("image/png"));
            Assert.That(image.Text, Is.Null.Or.Empty);
            Assert.That(image.Data, Is.Not.Empty);
            Assert.That(Convert.FromBase64String(image.Data), Is.EqualTo(_pngBytes));
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
                TimeSpan.FromMilliseconds(1));
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
    }
}
