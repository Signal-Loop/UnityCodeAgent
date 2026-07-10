using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Tools.CustomTools;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class ExecuteCSharpScriptInUnityEditorToolTests
    {
        [Test]
        public void CompilationGate_AllowsWhenCompilationIsInactive()
        {
            Assert.That(
                ExecuteCSharpScriptInUnityEditorTool.BuildCompilationBlockedResult(
                    isCompiling: false),
                Is.Null);
        }

        [Test]
        public void CompilationGate_BlocksWhileCompilationIsActive()
        {
            Assert.That(
                ExecuteCSharpScriptInUnityEditorTool.BuildCompilationBlockedResult(
                    isCompiling: true),
                Is.Not.Null);
        }
    }
}
