using System;
using System.IO;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Settings;

namespace SignalLoop.UnityCodeAgent.Editor.Installer
{
    public sealed class SkillsInstallResult
    {
        public bool Success { get; set; }
        public int SkillFoldersUpdated { get; set; }
        public int FilesUpdated { get; set; }
        public string ErrorMessage { get; set; }

        public bool AnyChanges => FilesUpdated > 0;

        public static SkillsInstallResult Failure(string errorMessage)
            => new SkillsInstallResult { Success = false, ErrorMessage = errorMessage };
    }

    public sealed class SkillsInstaller
    {
        private readonly IFileSystem _fileSystem;
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();

        public SkillsInstaller(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public bool InstallConfiguredSkills()
        {
            var sourcePath = UnityCodeAgentSkillsInstallTargetDrawer.ResolveSkillsSourcePath();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                _log.Warning(nameof(SkillsInstaller), "Could not locate the Skills source directory within the package. Skipping skills install.");
                return false;
            }

            var settings = UnityCodeAgentSettings.Instance;
            var targetPath = settings.GetEffectiveSkillsTargetPath();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                _log.Warning(nameof(SkillsInstaller), "Skills target directory is empty. Skipping skills install.");
                return false;
            }

            var result = Install(sourcePath, targetPath);
            return result.Success && result.AnyChanges;
        }

        public SkillsInstallResult Install(string sourcePath, string targetPath)
        {
            if (!_fileSystem.DirectoryExists(sourcePath))
            {
                _log.Error(nameof(SkillsInstaller), $"Skills source directory not found: {sourcePath}");
                return SkillsInstallResult.Failure($"Source directory not found: {sourcePath}");
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return SkillsInstallResult.Failure("Target path must not be empty.");
            }

            try
            {
                var result = new SkillsInstallResult { Success = true };
                foreach (var skillFolder in _fileSystem.GetDirectories(sourcePath))
                {
                    var folderName = GetDirectoryName(skillFolder);
                    var targetSkillFolder = NormalizePath(Path.Combine(targetPath, folderName));
                    var filesCopied = CopyDirectoryRecursive(skillFolder, targetSkillFolder);
                    if (filesCopied <= 0)
                    {
                        _log.Trace(nameof(SkillsInstaller), $"Skill '{folderName}' is already up to date.");
                        continue;
                    }

                    result.SkillFoldersUpdated++;
                    result.FilesUpdated += filesCopied;
                    _log.Info(nameof(SkillsInstaller), $"Installed skill '{folderName}' to {targetSkillFolder}. filesUpdated={filesCopied}");
                }

                return result;
            }
            catch (Exception exception)
            {
                _log.Error(nameof(SkillsInstaller), "Failed to install skills.", exception);
                return SkillsInstallResult.Failure(exception.Message);
            }
        }

        public bool RelocateInstalledSkills(string sourcePath, string currentTargetPath, string newTargetPath)
        {
            var installResult = Install(sourcePath, newTargetPath);
            if (!installResult.Success)
            {
                return false;
            }

            var normalizedCurrentTargetPath = NormalizePath(currentTargetPath ?? string.Empty).TrimEnd('/');
            var normalizedNewTargetPath = NormalizePath(newTargetPath ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalizedCurrentTargetPath) ||
                string.Equals(normalizedCurrentTargetPath, normalizedNewTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                return installResult.AnyChanges;
            }

            var removedAnyExistingSkills = false;
            foreach (var skillFolder in _fileSystem.GetDirectories(sourcePath))
            {
                var folderName = GetDirectoryName(skillFolder);
                var oldTargetSkillFolder = NormalizePath(Path.Combine(normalizedCurrentTargetPath, folderName));
                if (!_fileSystem.DirectoryExists(oldTargetSkillFolder))
                {
                    continue;
                }

                removedAnyExistingSkills |= DeleteInstalledFilesRecursive(skillFolder, oldTargetSkillFolder);
            }

            return installResult.AnyChanges || removedAnyExistingSkills;
        }

        private bool DeleteInstalledFilesRecursive(string sourceDir, string oldTargetDir)
        {
            if (!_fileSystem.DirectoryExists(oldTargetDir))
            {
                return false;
            }

            var removedAnyFiles = false;

            foreach (var sourceFilePath in _fileSystem.GetFiles(sourceDir))
            {
                var fileName = _fileSystem.GetFileName(sourceFilePath);
                if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var oldTargetFilePath = NormalizePath(Path.Combine(oldTargetDir, fileName));
                if (!_fileSystem.FileExists(oldTargetFilePath))
                {
                    continue;
                }

                _fileSystem.DeleteFile(oldTargetFilePath);
                removedAnyFiles = true;
                _log.Trace(nameof(SkillsInstaller), $"Removed old skill file: {oldTargetFilePath}");
            }

            foreach (var sourceSubDir in _fileSystem.GetDirectories(sourceDir))
            {
                var subDirName = GetDirectoryName(sourceSubDir);
                var oldTargetSubDir = NormalizePath(Path.Combine(oldTargetDir, subDirName));
                removedAnyFiles |= DeleteInstalledFilesRecursive(sourceSubDir, oldTargetSubDir);
            }

            if (_fileSystem.DirectoryExists(oldTargetDir) &&
                _fileSystem.GetFiles(oldTargetDir).Length == 0 &&
                _fileSystem.GetDirectories(oldTargetDir).Length == 0)
            {
                _fileSystem.DeleteDirectory(oldTargetDir, recursive: false);
                _log.Trace(nameof(SkillsInstaller), $"Removed empty old skill directory: {oldTargetDir}");
                return true;
            }

            return removedAnyFiles;
        }

        private int CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            var filesCopied = 0;

            if (!_fileSystem.DirectoryExists(targetDir))
            {
                _fileSystem.CreateDirectory(targetDir);
                _log.Trace(nameof(SkillsInstaller), $"Created directory: {targetDir}");
            }

            foreach (var filePath in _fileSystem.GetFiles(sourceDir))
            {
                var fileName = _fileSystem.GetFileName(filePath);
                if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var destPath = NormalizePath(Path.Combine(targetDir, fileName));
                if (!ShouldCopyFile(filePath, destPath))
                {
                    continue;
                }

                _fileSystem.CopyFile(filePath, destPath, overwrite: true);
                _log.Trace(nameof(SkillsInstaller), $"Copied: {destPath}");
                filesCopied++;
            }

            foreach (var subDir in _fileSystem.GetDirectories(sourceDir))
            {
                var subDirName = GetDirectoryName(subDir);
                var targetSubDir = NormalizePath(Path.Combine(targetDir, subDirName));
                filesCopied += CopyDirectoryRecursive(subDir, targetSubDir);
            }

            return filesCopied;
        }

        private bool ShouldCopyFile(string sourcePath, string destPath)
        {
            if (!_fileSystem.FileExists(destPath))
            {
                return true;
            }

            try
            {
                return _fileSystem.ComputeFileHash(sourcePath) != _fileSystem.ComputeFileHash(destPath);
            }
            catch (Exception exception)
            {
                _log.Warning(nameof(SkillsInstaller), $"Cannot compute hash, will copy file. Error: {exception.Message}");
                return true;
            }
        }

        private static string GetDirectoryName(string path)
            => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        private static string NormalizePath(string path) => path.Replace("\\", "/");
    }
}
