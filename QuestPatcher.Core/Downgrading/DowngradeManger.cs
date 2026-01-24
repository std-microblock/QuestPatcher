using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuestPatcher.Core.Downgrading.Models;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Utils;
using Serilog;

namespace QuestPatcher.Core.Downgrading
{
    public class DowngradeManger : SharableLoading<DowngradeIndex>
    {
        private const string IndexUrl = "https://github.com/Lauriethefish/mbf-diffs/releases/download/1.0.0/index.json";

        private const string Crc32Url =
            "https://github.com/Lauriethefish/mbf-diffs/releases/download/1.0.0/assets.crc32.json";

        private const string DiffUrlBase = "https://github.com/Lauriethefish/mbf-diffs/releases/download/1.0.0/";
        
        private readonly Config _config;
        private readonly InstallManager _installManager;
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly IUserPrompter _prompter;
        private readonly string _outputFolder;
        private readonly HttpClient _httpClient = new();

        public DowngradeManger(Config config, InstallManager installManager, ExternalFilesDownloader filesDownloader,
            AndroidDebugBridge debugBridge, SpecialFolders specialFolders, IUserPrompter prompter)
        {
            _config = config;
            _installManager = installManager;
            _filesDownloader = filesDownloader;
            _debugBridge = debugBridge;
            _prompter = prompter;
            _outputFolder = specialFolders.DowngradeFolder;
        }

        protected override async Task<DowngradeIndex> LoadAsync(CancellationToken cToken)
        {
            Log.Information("Loading downgrade index");
            var indexJsonTask = Task.Run(() => _httpClient.GetStringAsync(IndexUrl, cToken), cToken);
            var checksumsJsonTask = Task.Run(() => _httpClient.GetStringAsync(Crc32Url, cToken), cToken);
            await Task.WhenAll(indexJsonTask, checksumsJsonTask);

            string indexJson = await indexJsonTask;
            string checksumsJson = await checksumsJsonTask;
            var appDiffs = JsonSerializer.Deserialize<IList<AppDiff>>(indexJson);
            var checksums = JsonSerializer.Deserialize<Dictionary<string, uint>>(checksumsJson);

            if (appDiffs is null || checksums is null)
            {
                throw new Exception("Failed to deserialize data, invalid data structure");
            }

            var paths = appDiffs
                .GroupBy(diff => diff.FromVersion)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(diff => diff.ToSemVer).ToList());

            Log.Debug("Loaded {Diffs} app diffs and {Checksums} checksums", appDiffs.Count, checksums.Count);
            return new DowngradeIndex(paths, checksums);
        }

        public async Task<IReadOnlyList<AppDiff>> GetAvailablePathAsync(string? fromVersion, bool refresh = false)
        {
            if (string.IsNullOrWhiteSpace(fromVersion))
            {
                return Array.Empty<AppDiff>();
            }

            var index = await GetOrLoadAsync(refresh);
            return index.Paths.TryGetValue(fromVersion, out var paths)
                ? paths.AsReadOnly()
                : Array.Empty<AppDiff>();
        }
        
        /// <summary>
        /// Downgrade the installed app to the specified version.
        /// </summary>
        /// <param name="appDiff">The downgrade appDiff</param>
        /// <returns>True if downgrade succeed, false if canceled</returns>
        /// <exception cref="FileDownloadFailedException">When necessary files needed for downgrade failed to download</exception>
        /// <exception cref="DowngradeException">When downgrade failed</exception>
        public async Task<bool> DowngradeApp(AppDiff appDiff)
        {
            var apk = _installManager.InstalledApp;
            if (apk == null)
            {
                Log.Error("Cannot downgrade app that is not installed!");
                throw new DowngradeException("App is not installed");
            }

            //Sanity check
            if (appDiff.FromVersion != apk.Version)
            {
                Log.Warning("Apk version {Version} does not match with downgrade path's FromVersion {FromVersion}",
                    apk.Version, appDiff.FromVersion);
                throw new DowngradeException("Apk version mismatch");
            }

            Log.Information("Starting downgrade from {FromVersion} to {ToVersion}", appDiff.FromVersion,
                appDiff.ToVersion);

            if (!await PrepareFiles(apk, appDiff))
            {
                Log.Warning("Prepare files did not succeed, not downgrading");
                return false;
            }

            string apkPath = await PatchFiles(apk, appDiff);
            await ReplaceAppWithDowngraded(apkPath, appDiff.ObbDiffs);
            return true;
        }

        /// <summary>
        ///     Download and verify all related files
        /// </summary>
        internal async Task<bool> PrepareFiles(ApkInfo apk, AppDiff appDiff)
        {
            // Check apk crc
            if (!await HashUtil.CheckCrc32Async(apk.Path, appDiff.ApkDiff.FileCrc))
            {
                Log.Error("Apk file has incorrect CRC, is the apk not original?");
                throw new DowngradeException("Apk file is corrupted");
            }

            // download apk diff
            if (!await DownloadAndVerifyDiffFile(appDiff.ApkDiff))
            {
                return false;
            }

            // obb files
            foreach (var fileDiff in appDiff.ObbDiffs)
            {
                // download the source obb file from device
                string? sourcePath;
                try
                {
                    sourcePath = await _installManager.DownloadObbFile(fileDiff.FileName, _outputFolder);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Failed to download obb file from device {ObbName}", fileDiff.FileName);
                    throw new DowngradeException("Failed to download obb file from device", e);
                }

                if (sourcePath == null)
                {
                    Log.Error("Obb file {FileName} not found on the device", fileDiff.FileName);
                    throw new DowngradeException("Obb file not found on the device");
                }

                // verify the source obb file
                bool match = await HashUtil.CheckCrc32Async(sourcePath, fileDiff.FileCrc);
                if (!match)
                {
                    Log.Error("Source obb file {FileName} has incorrect CRC", fileDiff.FileName);
                    throw new DowngradeException("Obb file is corrupted");
                }

                // Download the diff file
                if (!await DownloadAndVerifyDiffFile(fileDiff))
                {
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> DownloadAndVerifyDiffFile(FileDiff fileDiff)
        {
            string diffPath = Path.Combine(_outputFolder, fileDiff.DiffName);
            string uri = $"{DiffUrlBase}{fileDiff.DiffName}";
            await _filesDownloader.DownloadUri(uri, diffPath);
            // check diff file crc
            if (Data!.Checksums.TryGetValue(fileDiff.DiffName, out uint diffCrc))
            {
                if (!await HashUtil.CheckCrc32Async(diffPath, diffCrc))
                {
                    Log.Error("Diff file {DiffName} has incorrect CRC", fileDiff.DiffName);
                    throw new DowngradeException("Diff file is corrupted");
                }
            }
            else
            {
                Log.Warning("CRC for diff file {DiffName} is unknown", fileDiff.DiffName);
                return await _prompter.PromptMissingDowngradeAssetCrc(fileDiff.DiffName);
            }

            return true;
        }

        /// <summary>
        /// </summary>
        /// <param name="apk"></param>
        /// <param name="appDiff"></param>
        /// <returns></returns>
        internal async Task<string> PatchFiles(ApkInfo apk, AppDiff appDiff)
        {
            // patch apk
            string apkPath = await PatchFile(appDiff.ApkDiff, apk.Path);
            // patch obb
            foreach (var fileDiff in appDiff.ObbDiffs)
            {
                await PatchFile(fileDiff);
            }

            return apkPath;
        }
        
        private async Task<string> PatchFile(FileDiff fileDiff, string? sourcePathOverride = null)
        {
            Log.Information("Patching file {FileName} with {DiffName}", fileDiff.FileName, fileDiff.DiffName);
            string diffPath = Path.Combine(_outputFolder, fileDiff.DiffName);
            string sourcePath = sourcePathOverride ?? Path.Combine(_outputFolder, fileDiff.FileName);
            string outputPath = Path.Combine(_outputFolder, fileDiff.OutputFileName);
            //TODO Can the output file have the same name as the source?
            await FilePatcher.PatchFileAsync(sourcePath, outputPath, diffPath);
            
            // check output file crc
            if (!await HashUtil.CheckCrc32Async(outputPath, fileDiff.OutputCrc))
            {
                Log.Error("Patched output file {FileName} has incorrect CRC", fileDiff.OutputFileName);
                throw new DowngradeException("Patched output file is corrupted");
            }
            
            return outputPath;
        }
        
        // This has a lot of copy-pasted code from PatchingManager.
        // Refactor to a shared method in InstallManager may cause a lot of future upstream merge conflicts
        private async Task ReplaceAppWithDowngraded(string apkPath, IList<FileDiff> obbDiffs)
        {
            // Close any running instance of the app.
            await _debugBridge.ForceStop(SharedConstants.BeatSaberPackageID);
            
            // backup stuff
            Log.Information("Backing up data directory");
            string? dataBackupPath;
            try
            {
                dataBackupPath = await _installManager.CreateDataBackup();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create data backup");
                dataBackupPath = null;
            }

            Log.Information("Backing up obb directory");
            string? obbBackupPath;
            try
            {
                obbBackupPath = await _installManager.CreateObbBackup();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create obb backup");
                obbBackupPath = null; // Indicate that the backup failed
            }

            // Uninstall and reinstall the diff patched apk
            
            try
            {
                await _debugBridge.UninstallApp(SharedConstants.BeatSaberPackageID);
            }
            catch (AdbException)
            {
                Log.Warning("Failed to uninstall the original APK, could be already uninstalled");
                Log.Warning("Will continue with installing downgraded one anyway");
            }

            Log.Information("Installing downgraded APK");
            await _debugBridge.InstallApp(apkPath);

            // Restore backups
            
            if (dataBackupPath != null)
            {
                Log.Information("Restoring data backup");
                try
                {
                    await _installManager.RestoreDataBackup(dataBackupPath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to restore data backup");
                }
            }

            if (obbBackupPath != null)
            {
                Log.Information("Restoring obb backup");
                try
                {
                    await _installManager.RestoreObbBackup(obbBackupPath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to restore obb backup");
                }
            }

            try
            {
                // Push patched obb files
                Log.Information("Pushing patched obb files");
                await PushObbFiles(obbDiffs);
            }
            catch (Exception e)
            {
                Log.Error("Failed to push obb files");
                throw new DowngradeException("Failed to push patched obb files", e);
            }
            
            await _installManager.NewApkInstalled(apkPath);

            Log.Information("App Downgraded successfully");
        }

        /// <summary>
        ///     Push
        /// </summary>
        /// <param name="obbDiffs"></param>
        /// <exception cref="DowngradeException"></exception>
        internal async Task PushObbFiles(IList<FileDiff> obbDiffs)
        {
            foreach (var obbDiff in obbDiffs)
            {
                string obbPath = Path.Combine(_outputFolder, obbDiff.OutputFileName);
                string obbName = Path.GetFileName(obbPath);
                Log.Debug("Pushing patched obb file {ObbName}", obbName);
                await _installManager.ReplaceObbFile(obbDiff.FileName, obbDiff.OutputFileName, obbPath);
            }
        }

        /// <summary>
        /// Whether the downgrade feature is available for the current app.
        /// Does not guarantee that downgrades are available for this installed version.
        ///
        /// Requirements: Current app is Beat Saber, not modded, and is > 1.34.2
        /// </summary>
        public static bool DowngradeFeatureAvailable(ApkInfo? app, string packageId)
        {
            return packageId == SharedConstants.BeatSaberPackageID 
                   && app is {IsModded: false} && app.SemVersion != null 
                   && app.SemVersion > SharedConstants.BeatSaberPreAssetsRefactorVersion;
        }
    }
}
