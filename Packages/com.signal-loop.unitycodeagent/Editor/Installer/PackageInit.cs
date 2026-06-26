using System;
using SignalLoop.UnityCodeAgent.Logging;
using UnityEditor;

namespace SignalLoop.UnityCodeAgent.Editor.Installer
{
    [InitializeOnLoad]
    public static class PackageInit
    {
        private static readonly UnityCodeAgentLogger Log = new UnityCodeAgentLogger();

        static PackageInit()
        {
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnAfterAssemblyReload()
        {
            Log.Debug(nameof(PackageInit), "OnAfterAssemblyReload event.");
            RunInstaller();
        }

        private static void RunInstaller()
        {
            IFileSystem fileSystem = new EditorFileSystem();
            var skillsInstaller = new SkillsInstaller(fileSystem);

            var anyChanges = RunInstallers(
                () => skillsInstaller.InstallConfiguredSkills());

            Log.Debug(nameof(PackageInit), $"Install steps completed. changesApplied={anyChanges}");
        }

        public static bool RunInstallers(Func<bool> installSkills)
        {
            var skillsChanged = installSkills != null && installSkills();
            return skillsChanged;
        }
    }
}
