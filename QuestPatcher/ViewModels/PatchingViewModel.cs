using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QuestPatcher.Core;
using QuestPatcher.Core.CoreMod;
using QuestPatcher.Core.Downgrading;
using QuestPatcher.Core.Downgrading.Models;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using QuestPatcher.Core.Utils;
using QuestPatcher.Models;
using QuestPatcher.Resources;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels
{
    public class PatchingViewModel : ViewModelBase
    {
        public bool IsPatchingInProgress { get => _isPatchingInProgress; set { if (_isPatchingInProgress != value) { this.RaiseAndSetIfChanged(ref _isPatchingInProgress, value); } } }
        private bool _isPatchingInProgress;

        public string PatchingStageText { get; private set; } = "";

        public string? CustomSplashPath => Config.PatchingOptions.CustomSplashPath;

        public ModLoader PreferredModLoader
        {
            get
            {
                var version = _installManager.InstalledApp?.SemVersion;
                return BeatSaberUtils.GetDefaultModLoader(version);
            }
        }

        public Config Config { get; }

        public OperationLocker Locker { get; }

        public ProgressViewModel ProgressBarView { get; }

        public ExternalFilesDownloader FilesDownloader { get; }

        private readonly PatchingManager _patchingManager;
        private readonly InstallManager _installManager;
        private readonly CoreModsManager _coreModsManager;
        private readonly DowngradeManger _downgradeManger;
        private readonly Window _mainWindow;

        public PatchingViewModel(Config config, OperationLocker locker, PatchingManager patchingManager,
            InstallManager installManager, CoreModsManager coreModsManager, DowngradeManger downgradeManger,
            Window mainWindow, ProgressViewModel progressBarView, ExternalFilesDownloader filesDownloader)
        {
            Config = config;
            Locker = locker;
            ProgressBarView = progressBarView;
            FilesDownloader = filesDownloader;

            _patchingManager = patchingManager;
            _installManager = installManager;
            _coreModsManager = coreModsManager;
            _downgradeManger = downgradeManger;
            _mainWindow = mainWindow;

            _patchingManager.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(_patchingManager.PatchingStage))
                {
                    OnPatchingStageChange(_patchingManager.PatchingStage);
                }
            };

            Config.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(Config.ExpertMode) && !Config.ExpertMode)
                {
                    Config.PatchingOptions.ModLoader = PreferredModLoader;
                }
            };
        }

        /// <returns>Whether to proceed with patching and the selected downgrade</returns>
        private async Task<SelectionData> FindPreferredVersion(ApkInfo apk)
        {
            string curVer = apk.Version;
            bool autoDowngrade = Config.PatchingOptions.AutoDowngrade;
            bool allowDowngrade = Config.PatchingOptions.AllowDowngrade;
            // first check for current
            bool coreAvailableForCur = await _coreModsManager.IsCoreModsAvailableAsync(curVer);

            if (autoDowngrade && coreAvailableForCur)
            {
                Log.Debug("Using current version");
                // Use the current one without downgrading
                return new SelectionData { Proceed = true, Downgrade = null };
            }

            // find downgrades with available core mods
            var downgrades = await _downgradeManger.GetAvailablePathAsync(curVer);

            if (Config.ExpertMode)
            {
                if (!allowDowngrade || downgrades.Count == 0)
                {
                    // can't downgrade
                    Log.Debug("Using current version because we can't downgrade");
                    return new SelectionData { Proceed = true, Downgrade = null };
                }

                // skip checking core mods, simply let the user select a version
                Log.Debug("Multiple versions available, asking to select version");
                var versions = new List<AppDiff?>(downgrades);
                versions.Insert(0, null);
                string message1 = $"当前游戏版本 {curVer}";
                return await VersionSelectViewModel.ShowDialog(_mainWindow, versions, message1);
            }

            var verWithCoreMods = await _coreModsManager.GetAllAvailableVersionsAsync();
            var moddableDowngrades = downgrades
                .Where(path => verWithCoreMods.Contains(path.ToVersion))
                .OrderByDescending(pair => pair.ToSemVer).ToList();

            if (!coreAvailableForCur && moddableDowngrades.Count == 0)
            {
                // no core mods and no moddable downgrades
                Log.Warning("No core mods and no moddable downgrades available for {Version}", curVer);
                var dialog = new DialogBuilder
                {
                    Title = "暂无可用版本",
                    Text = $"当前游戏版本 {apk.Version} 暂时还没有可用的核心MOD,\n" +
                           "也暂时没有有核心Mod的降级版本\n刚刚更新的最新版游戏可能需要一些时间才会有可用降级",
                    HideCancelButton = true
                };

                await dialog.OpenDialogue(_mainWindow);
                return new SelectionData { Proceed = false, Downgrade = null };
            }

            var moddable = new List<AppDiff?>(moddableDowngrades);
            if (coreAvailableForCur)
            {
                moddable.Insert(0, null);
            }

            if (Config.PatchingOptions.AutoDowngrade || moddable.Count == 1)
            {
                // use the newest moddable one
                Log.Debug("Only one moddable version available or auto selecting the newest");
                return new SelectionData { Proceed = true, Downgrade = moddable[0] };
            }

            // let user select a moddable version
            Log.Debug("Multiple moddable versions available, asking to select version");
            string message = coreAvailableForCur
                ? $"当前游戏版本 {apk.Version} 有可用的核心MOD，但也可以选择降级到其他有核心Mod的版本"
                : $"当前游戏版本 {apk.Version} 暂时还没有可用的核心MOD，但有多个有核心Mod的版本可以降级";
            return await VersionSelectViewModel.ShowDialog(_mainWindow, moddable, message);
        }

        public async void StartPatching()
        {
            var apk = _installManager.InstalledApp;
            if (apk == null)
            {
                Log.Warning("Trying to patch game without an installed app");
                return;
            }

            bool repatch = apk.ModLoader != null;

            if (!Config.PatchingOptions.CleanUpMods)
            {
                var builder = new DialogBuilder
                {
                    Title = "清理Mod", 
                    Text = "补丁选项 “清理Mod” 未启用\n这会跳过清理残留的旧版Mod导致游戏无法正常运行。"
                };
                
                builder.OkButton.Text = Strings.Generic_ContinueAnyway;
                if (!await builder.OpenDialogue(_mainWindow))
                {
                    return;
                }
            }

            var apkSemVer = apk.SemVersion;
            if (apkSemVer != null)
            {
                // check version and selected mod loader
                var modLoader = Config.PatchingOptions.ModLoader;
                string? text = null;
                if (modLoader == ModLoader.QuestLoader && apkSemVer > SharedConstants.BeatSaberLastQuestLoaderVersion)
                {
                    text = $"当前游戏版本 {apkSemVer.BaseVersion()} 的Mod需要 Scotland2 Mod注入器，而您选择了 QuestLoader。\n这会导致Mod无法加载并且需要重新打补丁。";
                }
                else if (modLoader == ModLoader.Scotland2 && apkSemVer <= SharedConstants.BeatSaberLastQuestLoaderVersion)
                {
                    text = $"当前游戏版本 {apkSemVer.BaseVersion()} 的Mod需要 QuestLoader Mod注入器，而您选择了 Scotland2。\n这会导致Mod无法加载并且需要重新打补丁。";
                }

                if (text != null)
                {
                    var builder = new DialogBuilder
                    {
                        Title = "不匹配的Mod注入器",
                        Text = text
                    };
                
                    builder.OkButton.Text = Strings.Generic_ContinueAnyway;
                    if (!await builder.OpenDialogue(_mainWindow))
                    {
                        return;
                    }
                }
            }
            
            if (Config.PatchingOptions.FlatScreenSupport)
            {
                // Disable VR requirement apparently causes infinite load
                var builder = new DialogBuilder
                {
                    Title = "禁用VR要求已启用",
                    Text = "您在补丁选项中禁用了VR要求，这可能会导致出现错误，例如启动游戏时无限加载"
                };
                
                builder.OkButton.Text = Strings.Generic_ContinueAnyway;
                if (!await builder.OpenDialogue(_mainWindow))
                {
                    return;
                }
            }

            IsPatchingInProgress = true;
            Locker.StartOperation();
            try
            {
                var selection = new SelectionData { Proceed = true, Downgrade = null };
                if (!repatch && Config.PatchingOptions.AllowDowngrade)
                {
                    // find out exactly what version to patch or not patching at all
                    selection = await FindPreferredVersion(apk);
                }

                // double check core mods availability
                if (selection.Proceed &&
                    (repatch || await IsCoreModsAvailable(selection.Downgrade?.ToVersion ?? apk.Version)))
                {
                    Log.Debug("Proceed with patching, AppDiff: {AppDiff}", selection.Downgrade);
                    await _patchingManager.PatchApp(selection.Downgrade);
                }
                else
                {
                    Log.Debug("Not proceed with patching");
                }
            }
            catch (FileDownloadFailedException ex)
            {
                Log.Error("Patching failed as essential files could not be downloaded: {Message}", ex.Message);

                var builder = new DialogBuilder
                {
                    Title = Strings.Patching_FileDownloadFailed_Title,
                    Text = Strings.Patching_FileDownloadFailed_Text,
                    HideCancelButton = true
                };

                await builder.OpenDialogue(_mainWindow);
            }
            catch (Exception ex)
            {
                // Print troubleshooting information for debugging
                Log.Error(ex, "Patching failed!");
                var builder = new DialogBuilder
                {
                    Title = Strings.Patching_PatchingFailed_Title,
                    Text = Strings.Patching_PatchingFailed_Text,
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                IsPatchingInProgress = false;
                Locker.FinishOperation();
            }

            if (_installManager.InstalledApp?.IsModded ?? false)
            {
                // Display a dialogue to give the user some info about what to expect next, and to avoid them pressing restore app by mistake
                Log.Debug("Patching completed successfully, displaying info dialogue");
                var builder = new DialogBuilder
                {
                    Title = Strings.Patching_PatchingSuccess_Title,
                    Text = Strings.Patching_PatchingSuccess_Text,
                    HideCancelButton = true
                };
                await builder.OpenDialogue(_mainWindow);
            }
        }

        private async Task<bool> IsCoreModsAvailable(string version)
        {
            try
            {
                var coreMods = await _coreModsManager.GetCoreModsAsync(version);
                if (coreMods.Count == 0)
                {
                    Log.Warning("Trying to patch game without available core mods!");
                    var builder = new DialogBuilder
                    {
                        Title = "没有核心MOD", Text = $"当前游戏版本 {version} 暂时还没有可用的核心MOD\n确定要继续打补丁吗？"
                    };

                    builder.OkButton.Text = Strings.Generic_ContinueAnyway;
                    if (!await builder.OpenDialogue(_mainWindow))
                    {
                        Log.Debug("Patching not started due to no core mods");
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to load core mods, continuing patching");
            }

            return true;
        }

        public async void SelectSplashPath()
        {
            try
            {
                var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    FileTypeFilter = new[]
                    {
                        FilePickerFileTypes.ImagePng
                    }
                });
                Config.PatchingOptions.CustomSplashPath = files.FirstOrDefault()?.Path.LocalPath;
                this.RaisePropertyChanged(nameof(CustomSplashPath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to select splash screen path");
            }
        }

        /// <summary>
        /// Updates the patching stage text in the view
        /// </summary>
        /// <param name="stage">The new patching stage</param>
        private void OnPatchingStageChange(PatchingStage stage)
        {
            PatchingStageText = stage switch
            {
                PatchingStage.NotStarted => Strings.PatchingStage_NotStarted,
                PatchingStage.Downgrading => Strings.PatchingStage_Downgrading,
                PatchingStage.FetchingFiles => Strings.PatchingStage_FetchFiles,
                PatchingStage.MovingToTemp => Strings.PatchingStage_MoveToTemp,
                PatchingStage.Patching => Strings.PatchingStage_Patching,
                PatchingStage.Signing => Strings.PatchingStage_Signing,
                PatchingStage.UninstallingOriginal => Strings.PatchingStage_UninstallOriginal,
                PatchingStage.InstallingModded => Strings.PatchingStage_InstallModded,
                PatchingStage.CleanUpMods => Strings.PatchingStage_CleanUpMods,
                PatchingStage.InstallCoreMods => Strings.PatchingStage_InstallCoreMods,
                _ => throw new NotImplementedException()
            };
            this.RaisePropertyChanged(nameof(PatchingStageText));
        }

        public struct SelectionData
        {
            public bool Proceed;
            public AppDiff? Downgrade;
        }
    }
}
