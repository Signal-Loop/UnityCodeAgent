using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Editor.Installer;

namespace SignalLoop.UnityCodeAgent.Editor.Tests.Installer
{
    public sealed class PackageInitTests
    {
        [Test]
        public void RunInstallers_ReturnsTrue_WhenSkillsInstallerChangesFiles()
        {
            Assert.That(PackageInit.RunInstallers(() => true), Is.True);
        }

        [Test]
        public void RunInstallers_ReturnsFalse_WhenSkillsInstallerDoesNotChangeFiles()
        {
            Assert.That(PackageInit.RunInstallers(() => false), Is.False);
        }

        [Test]
        public void RunInstallers_ReturnsFalse_WhenSkillsInstallerIsMissing()
        {
            Assert.That(PackageInit.RunInstallers(null), Is.False);
        }
    }
}
